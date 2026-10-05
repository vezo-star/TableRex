import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { mountWithQuasar } from '../helpers/mountWithQuasar.js'
import { useAuthStore } from '@/stores/auth.js'

vi.mock('@/services/availabilityService.js', () => ({ availabilityService: { search: vi.fn() } }))
vi.mock('@/services/bookingService.js', () => ({
  BookingCommands: {},
  bookingService: {
    create: vi.fn(),
    get: vi.fn(),
    history: vi.fn(),
    command: vi.fn(),
    cancel: vi.fn(),
    update: vi.fn(),
    move: vi.fn(),
  },
  holdService: { create: vi.fn(), release: vi.fn() },
  blockService: { create: vi.fn() },
}))
vi.mock('@/services/customerService.js', () => ({ customerService: { list: vi.fn() } }))

const { availabilityService } = await import('@/services/availabilityService.js')
const { bookingService, holdService, blockService } = await import('@/services/bookingService.js')
const { default: BookingCreateDialog } =
  await import('@/components/bookings/BookingCreateDialog.vue')
const { default: BookingDrawer } = await import('@/components/bookings/BookingDrawer.vue')
const { default: BlockDialog } = await import('@/components/bookings/BlockDialog.vue')

const rules = {
  slotIntervalMinutes: 15,
  defaultDurationMinutes: 120,
  minDurationMinutes: 30,
  maxDurationMinutes: 240,
}
const venue = { id: 'v1', name: 'Bistro', timeZone: 'Africa/Johannesburg', rules }
const resources = [
  { id: 'r1', name: 'T1', minCapacity: 1, capacity: 4, isActive: true },
  { id: 'r2', name: 'T2', minCapacity: 1, capacity: 4, isActive: true },
]

function pinia(permissions) {
  const p = createPinia()
  setActivePinia(p)
  const auth = useAuthStore()
  auth.accessToken = 'token'
  auth.tenantId = 't1'
  auth.me = { displayName: 'Ada', permissions, roles: [] }
  return p
}

const availability = {
  venueId: 'v1',
  date: '2026-10-09',
  timeZone: venue.timeZone,
  isClosed: false,
  slots: [
    {
      time: '19:00',
      startsAt: '2026-10-09T17:00:00Z',
      endsAt: '2026-10-09T19:00:00Z',
      available: true,
      options: [
        { kind: 'RESOURCE', id: 'r1', name: 'T1', capacity: 4, resourceIds: ['r1'] },
        { kind: 'RESOURCE', id: 'r2', name: 'T2', capacity: 4, resourceIds: ['r2'] },
      ],
    },
  ],
}

function booking(overrides = {}) {
  return {
    id: 'b1',
    reference: 'BK-1',
    status: 'CONFIRMED',
    source: 'PHONE',
    partySize: 2,
    startsAt: '2020-01-01T17:00:00Z',
    endsAt: '2020-01-01T19:00:00Z',
    businessDate: '2020-01-01',
    time: '19:00',
    durationMinutes: 120,
    timeZone: venue.timeZone,
    customer: { id: 'c1', firstName: 'Ada', lastName: 'Lovelace', email: null, phone: null },
    resources: [{ resourceId: 'r1', name: 'T1', code: 'T1', combinationId: null }],
    version: 3,
    createdAt: '2019-12-01T10:00:00Z',
    ...overrides,
  }
}

describe('BookingCreateDialog', () => {
  beforeEach(() => vi.clearAllMocks())

  async function open(prefill = null) {
    const wrapper = await mountWithQuasar(BookingCreateDialog, {
      pinia: pinia(['booking.create']),
      props: { modelValue: false, venue, resources, prefill },
    })
    await wrapper.setProps({ modelValue: true })
    await flushPromises()
    return wrapper
  }

  it('prefills from the board, holds the chosen table and converts the hold with a stable key', async () => {
    availabilityService.search.mockResolvedValue(availability)
    holdService.create.mockResolvedValue({
      id: 'h1',
      token: 'secret-token',
      expiresAt: '2026-10-09T15:00:00Z',
      resourceIds: ['r2'],
    })
    bookingService.create
      .mockRejectedValueOnce({ response: { status: 503, data: { code: 'INTERNAL_ERROR' } } })
      .mockResolvedValueOnce({
        booking: { id: 'b1', reference: 'BK-1', businessDate: '2026-10-09' },
        replayed: true,
      })

    const wrapper = await open({ date: '2026-10-09', time: '19:00', resourceId: 'r2' })
    const vm = wrapper.vm
    expect(availabilityService.search).toHaveBeenCalledWith({
      venueId: 'v1',
      date: '2026-10-09',
      partySize: 2,
      durationMinutes: 120,
    })
    expect(vm.selectedSlot.time).toBe('19:00')
    expect(vm.optionKey).toBe('RESOURCE:r2')

    await vm.placeHold()
    await flushPromises()
    expect(holdService.create.mock.calls[0][0]).toMatchObject({
      time: '19:00',
      allocation: { mode: 'RESOURCES', resourceIds: ['r2'] },
    })
    expect(vm.step).toBe('customer')

    vm.customerMode = 'new'
    vm.newCustomer.firstName = ' Grace '
    vm.newCustomer.phone = '082 555 0100'
    await vm.submit()
    await flushPromises()
    expect(vm.error.status).toBe(503)

    await vm.submit()
    await flushPromises()
    const [first, firstKey] = bookingService.create.mock.calls[0]
    const [, secondKey] = bookingService.create.mock.calls[1]
    expect(first).toMatchObject({
      holdToken: 'secret-token',
      allocation: null,
      customer: { customerId: null, firstName: 'Grace', phone: '082 555 0100', email: null },
      source: 'PHONE',
      status: 'CONFIRMED',
    })
    expect(secondKey).toBe(firstKey)
    expect(wrapper.emitted('created')[0][0].id).toBe('b1')
    expect(holdService.release).not.toHaveBeenCalled()
  })

  it('releases the hold when going back and refreshes availability when the table was taken', async () => {
    availabilityService.search.mockResolvedValue(availability)
    holdService.create
      .mockResolvedValueOnce({
        id: 'h1',
        token: 't',
        expiresAt: '2026-10-09T15:00:00Z',
        resourceIds: ['r1'],
      })
      .mockRejectedValueOnce({
        response: { status: 409, data: { code: 'RESOURCE_NOT_AVAILABLE', detail: 'taken' } },
      })
    const wrapper = await open({ date: '2026-10-09', time: '19:00' })
    const vm = wrapper.vm

    await vm.placeHold()
    await vm.backToWhen()
    expect(holdService.release).toHaveBeenCalledWith('h1')

    await vm.placeHold()
    await flushPromises()
    expect(availabilityService.search).toHaveBeenCalledTimes(2)
    expect(vm.error.message).toContain('just taken')
    expect(vm.step).toBe('when')
  })

  it('requires a customer unless the booking is a walk-in', async () => {
    const wrapper = await open()
    const vm = wrapper.vm
    vm.customerMode = 'existing'
    expect(vm.customerReady).toBe(false)
    vm.form.source = 'WALK_IN'
    await flushPromises()
    expect(vm.customerModes.map((m) => m.value)).toContain('none')
    vm.customerMode = 'none'
    expect(vm.customerReady).toBe(true)
  })
})

describe('BookingDrawer', () => {
  beforeEach(() => vi.clearAllMocks())

  async function open(
    permissions = [
      'booking.view',
      'booking.checkin',
      'booking.seat',
      'booking.update',
      'booking.cancel',
      'booking.move',
    ],
  ) {
    const wrapper = await mountWithQuasar(BookingDrawer, {
      pinia: pinia(permissions),
      props: { modelValue: false, bookingId: null, venue, resources },
    })
    await wrapper.setProps({ modelValue: true, bookingId: 'b1' })
    await flushPromises()
    return wrapper
  }

  it('offers actions by status and permission and sends the version with commands', async () => {
    bookingService.get.mockResolvedValue(booking())
    bookingService.command.mockResolvedValue(booking({ status: 'CHECKED_IN', version: 4 }))
    const wrapper = await open()
    const vm = wrapper.vm
    expect(vm.actions.map((a) => a.key)).toEqual(['checkIn', 'seat', 'noShow', 'cancel'])

    await vm.runAction(vm.actions[0])
    await flushPromises()
    expect(bookingService.command).toHaveBeenCalledWith('b1', 'checkIn', 3)
    expect(vm.booking.status).toBe('CHECKED_IN')
    expect(wrapper.emitted('changed')).toHaveLength(1)
  })

  it('hides actions the user may not perform', async () => {
    bookingService.get.mockResolvedValue(booking())
    const wrapper = await open(['booking.view'])
    expect(wrapper.vm.actions).toEqual([])
    expect(wrapper.vm.editable).toBe(false)
    expect(wrapper.vm.movable).toBe(false)
  })

  it('reloads the booking after a concurrency conflict', async () => {
    bookingService.get
      .mockResolvedValueOnce(booking())
      .mockResolvedValueOnce(booking({ partySize: 5, version: 4 }))
    bookingService.update.mockRejectedValue({
      response: { status: 409, data: { code: 'CONCURRENCY_CONFLICT', detail: 'stale' } },
    })
    const wrapper = await open()
    const vm = wrapper.vm
    vm.startEdit()
    vm.editForm.partySize = 3
    await vm.saveEdit()
    await flushPromises()
    expect(bookingService.update).toHaveBeenCalledWith('b1', {
      partySize: 3,
      specialRequests: null,
      internalNotes: null,
      version: 3,
    })
    expect(vm.booking.version).toBe(4)
    expect(vm.booking.partySize).toBe(5)
  })

  it('moves only the changed fields', async () => {
    bookingService.get.mockResolvedValue(booking())
    bookingService.move.mockResolvedValue(booking({ time: '20:00', version: 4 }))
    const wrapper = await open()
    const vm = wrapper.vm
    vm.startMove()
    vm.moveForm.time = '20:00'
    vm.moveForm.resourceIds = ['r2']
    await vm.saveMove()
    await flushPromises()
    expect(bookingService.move).toHaveBeenCalledWith('b1', {
      date: null,
      time: '20:00',
      durationMinutes: null,
      allocation: { mode: 'RESOURCES', resourceIds: ['r2'] },
      version: 3,
    })
  })
})

describe('BlockDialog', () => {
  beforeEach(() => vi.clearAllMocks())

  it('converts venue-local times to UTC and wraps past midnight', async () => {
    blockService.create.mockResolvedValue({ id: 'k1' })
    const wrapper = await mountWithQuasar(BlockDialog, {
      pinia: pinia(['resource.update']),
      props: {
        modelValue: false,
        venue,
        resources,
        prefill: { date: '2026-10-09', resourceId: 'r1' },
      },
    })
    await wrapper.setProps({ modelValue: true })
    const vm = wrapper.vm
    vm.form.start = '22:00'
    vm.form.end = '01:00'
    await vm.save()
    await flushPromises()
    expect(blockService.create).toHaveBeenCalledWith({
      venueId: 'v1',
      resourceIds: ['r1'],
      startsAt: '2026-10-09T20:00:00.000Z',
      endsAt: '2026-10-09T23:00:00.000Z',
      reason: 'UNAVAILABLE',
      notes: null,
    })
    expect(wrapper.emitted('created')).toBeTruthy()
  })
})
