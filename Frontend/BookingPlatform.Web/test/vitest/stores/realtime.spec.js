import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { RealtimeStatus } from '@/constants/realtimeEvents.js'
import { useAuthStore } from '@/stores/auth.js'

vi.mock('@/services/realtimeService.js', () => ({ createVenueConnection: vi.fn() }))
vi.mock('@/services/authService.js', () => ({ authService: { refresh: vi.fn() } }))

const { createVenueConnection } = await import('@/services/realtimeService.js')
const { authService } = await import('@/services/authService.js')
const { useRealtimeStore, RESYNC_AFTER_HIDDEN_MS, RETRY_DELAYS_MS } =
  await import('@/stores/realtime.js')

function fakeConnection() {
  const handlers = {}
  const connection = {
    handlers,
    on: vi.fn((name, fn) => {
      handlers[name] = fn
    }),
    onreconnecting: vi.fn((fn) => {
      connection.reconnecting = fn
    }),
    onreconnected: vi.fn((fn) => {
      connection.reconnected = fn
    }),
    onclose: vi.fn((fn) => {
      connection.closed = fn
    }),
    start: vi.fn().mockResolvedValue(),
    stop: vi.fn().mockResolvedValue(),
    invoke: vi.fn().mockResolvedValue(),
  }
  return connection
}

const event = (overrides = {}) => ({
  eventId: crypto.randomUUID(),
  type: 'BookingCreated',
  venueId: 'v1',
  entityId: 'b1',
  version: 1,
  businessDate: '2026-10-09',
  occurredAt: '2026-10-09T15:00:00Z',
  ...overrides,
})

function signIn() {
  const auth = useAuthStore()
  auth.accessToken = 'token-1'
  auth.tenantId = 't1'
  auth.expiresAt = Date.now() + 15 * 60_000
  return auth
}

describe('realtime store', () => {
  let connection

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    connection = fakeConnection()
    createVenueConnection.mockReturnValue(connection)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('does not connect without a tenant session', async () => {
    await useRealtimeStore().start()
    expect(createVenueConnection).not.toHaveBeenCalled()
  })

  it('connects, joins the selected venue, then asks listeners to resync', async () => {
    signIn()
    const realtime = useRealtimeStore()
    const order = []
    connection.invoke.mockImplementation(async (method) => order.push(method))
    realtime.subscribe({ onResync: () => order.push('resync') })
    realtime.setVenue('v1')

    await realtime.start()

    expect(connection.start).toHaveBeenCalledOnce()
    expect(order).toEqual(['JoinVenue', 'resync'])
    expect(connection.invoke).toHaveBeenCalledWith('JoinVenue', 'v1')
    expect(realtime.status).toBe(RealtimeStatus.CONNECTED)
    expect(realtime.isLive).toBe(true)
  })

  it('supplies the current token and refreshes it when it is about to expire', async () => {
    const auth = signIn()
    await useRealtimeStore().start()
    const { accessTokenFactory } = createVenueConnection.mock.calls[0][0]
    expect(await accessTokenFactory()).toBe('token-1')

    auth.expiresAt = Date.now() + 5_000
    authService.refresh.mockResolvedValue({
      accessToken: 'token-2',
      expiresAt: new Date(Date.now() + 900_000).toISOString(),
      userId: 'u1',
      tenantId: 't1',
      memberships: [],
    })
    expect(await accessTokenFactory()).toBe('token-2')
    expect(authService.refresh).toHaveBeenCalledOnce()
    auth.clear()
  })

  it('delivers events for the selected venue once, with their type', async () => {
    signIn()
    const realtime = useRealtimeStore()
    const received = []
    realtime.subscribe({ onEvent: (e) => received.push(e) })
    realtime.setVenue('v1')
    await realtime.start()

    const first = event()
    connection.handlers.BookingCreated(first)
    connection.handlers.BookingCreated(first)
    connection.handlers.BookingMoved(event({ type: 'BookingMoved', venueId: 'other' }))
    connection.handlers.BookingMoved(event({ type: 'BookingMoved', version: 2 }))

    expect(received.map((e) => [e.type, e.version])).toEqual([
      ['BookingCreated', 1],
      ['BookingMoved', 2],
    ])
    expect(realtime.lastEventAt).not.toBeNull()
  })

  it('rejoins and resyncs after an automatic reconnect', async () => {
    signIn()
    const realtime = useRealtimeStore()
    const resync = vi.fn()
    realtime.subscribe({ onResync: resync })
    realtime.setVenue('v1')
    await realtime.start()
    resync.mockClear()
    connection.invoke.mockClear()

    connection.reconnecting()
    expect(realtime.status).toBe(RealtimeStatus.RECONNECTING)
    expect(realtime.isLive).toBe(false)

    await connection.reconnected()
    expect(connection.invoke).toHaveBeenCalledWith('JoinVenue', 'v1')
    expect(resync).toHaveBeenCalledOnce()
    expect(realtime.isLive).toBe(true)
  })

  it('retries with backoff when the connection cannot start or closes, until stopped', async () => {
    vi.useFakeTimers()
    signIn()
    const realtime = useRealtimeStore()
    connection.start.mockRejectedValueOnce(new Error('offline'))

    await realtime.start()
    expect(realtime.status).toBe(RealtimeStatus.DISCONNECTED)

    await vi.advanceTimersByTimeAsync(RETRY_DELAYS_MS[0])
    expect(connection.start).toHaveBeenCalledTimes(2)
    expect(realtime.status).toBe(RealtimeStatus.CONNECTED)

    connection.closed(new Error('server went away'))
    expect(realtime.status).toBe(RealtimeStatus.DISCONNECTED)
    await vi.advanceTimersByTimeAsync(RETRY_DELAYS_MS[0])
    expect(connection.start).toHaveBeenCalledTimes(3)

    await realtime.stop()
    connection.closed()
    await vi.advanceTimersByTimeAsync(RETRY_DELAYS_MS.at(-1))
    expect(connection.start).toHaveBeenCalledTimes(3)
    expect(connection.stop).toHaveBeenCalled()
  })

  it('switches venue groups and reports a denied join as not live', async () => {
    signIn()
    const realtime = useRealtimeStore()
    realtime.setVenue('v1')
    await realtime.start()

    connection.invoke.mockImplementation(async (method, venueId) => {
      if (method === 'JoinVenue' && venueId === 'v2') {
        throw new Error('FORBIDDEN')
      }
    })
    await realtime.setVenue('v2')

    expect(connection.invoke).toHaveBeenCalledWith('LeaveVenue', 'v1')
    expect(connection.invoke).toHaveBeenCalledWith('JoinVenue', 'v2')
    expect(realtime.status).toBe(RealtimeStatus.CONNECTED)
    expect(realtime.isLive).toBe(false)
  })

  it('resyncs when a tab returns after a long sleep and reconnects a closed connection', async () => {
    vi.useFakeTimers()
    signIn()
    const realtime = useRealtimeStore()
    const resync = vi.fn()
    realtime.subscribe({ onResync: resync })
    await realtime.start()
    resync.mockClear()

    realtime.visibilityChanged(true)
    vi.advanceTimersByTime(5_000)
    realtime.visibilityChanged(false)
    expect(resync).not.toHaveBeenCalled()

    realtime.visibilityChanged(true)
    vi.advanceTimersByTime(RESYNC_AFTER_HIDDEN_MS)
    realtime.visibilityChanged(false)
    expect(resync).toHaveBeenCalledOnce()

    connection.closed(new Error('lost'))
    realtime.visibilityChanged(false)
    await vi.advanceTimersByTimeAsync(0)
    expect(connection.start).toHaveBeenCalledTimes(2)
  })
})
