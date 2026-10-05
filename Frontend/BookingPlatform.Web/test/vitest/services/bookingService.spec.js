import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createIdempotencyKeeper, isRetryableFailure } from '@/services/idempotency.js'

vi.mock('@/services/http.js', () => ({
  http: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}))

const { http } = await import('@/services/http.js')
const { bookingService, holdService } = await import('@/services/bookingService.js')
const { customerService, auditService } = await import('@/services/customerService.js')

describe('createIdempotencyKeeper', () => {
  it('reuses the key while the payload is unchanged and issues a new one after reset or a change', () => {
    let n = 0
    const keeper = createIdempotencyKeeper(() => `k${++n}`)
    expect(keeper.keyFor({ a: 1 })).toBe('k1')
    expect(keeper.keyFor({ a: 1 })).toBe('k1')
    expect(keeper.keyFor({ a: 2 })).toBe('k2')
    keeper.reset()
    expect(keeper.keyFor({ a: 2 })).toBe('k3')
  })

  it('treats network failures and server errors as retryable', () => {
    expect(isRetryableFailure({ status: 0 })).toBe(true)
    expect(isRetryableFailure({ status: 503 })).toBe(true)
    expect(isRetryableFailure({ status: 409 })).toBe(false)
    expect(isRetryableFailure({ status: 422 })).toBe(false)
  })
})

describe('bookingService', () => {
  beforeEach(() => vi.clearAllMocks())

  it('sends the Idempotency-Key and reports replays', async () => {
    http.post.mockResolvedValue({ data: { id: 'b1' }, headers: { 'idempotent-replayed': 'true' } })
    const result = await bookingService.create({ venueId: 'v1' }, 'key-1')
    expect(http.post).toHaveBeenCalledWith(
      '/v1/bookings',
      { venueId: 'v1' },
      {
        headers: { 'Idempotency-Key': 'key-1' },
      },
    )
    expect(result).toEqual({ booking: { id: 'b1' }, replayed: true })
  })

  it('maps status commands to endpoints with the version', async () => {
    http.post.mockResolvedValue({ data: {} })
    await bookingService.command('b 1', 'checkIn', 3)
    expect(http.post).toHaveBeenCalledWith('/v1/bookings/b%201/check-in', { version: 3 })
    await bookingService.cancel('b1', { reason: '', version: 4 })
    expect(http.post).toHaveBeenLastCalledWith('/v1/bookings/b1/cancel', {
      reason: null,
      version: 4,
    })
    await expect(bookingService.command('b1', 'explode', 1)).rejects.toThrow(
      'Unknown booking command',
    )
  })

  it('joins status filters and drops empty list parameters', async () => {
    http.get.mockResolvedValue({ data: { items: [] } })
    await bookingService.list({
      venueId: 'v1',
      status: ['CONFIRMED', 'SEATED'],
      q: '',
      customerId: null,
    })
    expect(http.get).toHaveBeenCalledWith('/v1/bookings', {
      params: { venueId: 'v1', status: 'CONFIRMED,SEATED' },
      signal: undefined,
    })
  })

  it('loads the board and releases holds', async () => {
    http.get.mockResolvedValue({ data: { bookings: [] } })
    await bookingService.board('v1', '2026-10-09')
    expect(http.get).toHaveBeenCalledWith('/v1/bookings/board', {
      params: { venueId: 'v1', date: '2026-10-09' },
      signal: undefined,
    })
    await holdService.release('h1')
    expect(http.delete).toHaveBeenCalledWith('/v1/holds/h1')
  })
})

describe('customerService and auditService', () => {
  beforeEach(() => vi.clearAllMocks())

  it('searches customers and pages audit logs with a cursor', async () => {
    http.get.mockResolvedValue({ data: { items: [] } })
    await customerService.list({ q: 'ada' })
    expect(http.get).toHaveBeenCalledWith('/v1/customers', {
      params: { q: 'ada', page: 1, pageSize: 25 },
      signal: undefined,
    })
    await auditService.list({ entityType: 'booking', before: 'abc', action: '' })
    expect(http.get).toHaveBeenLastCalledWith('/v1/audit-logs', {
      params: { entityType: 'booking', before: 'abc', limit: 50 },
    })
  })
})
