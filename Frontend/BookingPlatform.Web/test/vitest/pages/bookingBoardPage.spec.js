import { h } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createTestRouter, mountWithQuasar } from '../helpers/mountWithQuasar.js'
import { useAuthStore } from '@/stores/auth.js'
import { useVenuesStore } from '@/stores/venues.js'

vi.mock('@/services/venueService.js', () => ({
  venueService: {
    listVenues: vi.fn(),
    listSections: vi.fn(),
    listAllResources: vi.fn(),
    listFloorPlans: vi.fn(),
  },
}))
vi.mock('@/services/bookingService.js', () => ({
  BookingCommands: {},
  bookingService: { board: vi.fn(), get: vi.fn(), move: vi.fn() },
  holdService: { create: vi.fn(), release: vi.fn() },
  blockService: { create: vi.fn(), cancel: vi.fn() },
}))

const { venueService } = await import('@/services/venueService.js')
const { bookingService } = await import('@/services/bookingService.js')
const { default: BookingBoardPage } = await import('@/pages/bookings/BookingBoardPage.vue')
const { QLayout, QPageContainer } = await import('quasar')

const Host = {
  render: () => h(QLayout, null, () => h(QPageContainer, null, () => h(BookingBoardPage))),
}

const rules = {
  slotIntervalMinutes: 15,
  defaultDurationMinutes: 120,
  minDurationMinutes: 30,
  maxDurationMinutes: 240,
}
const venue = { id: 'v1', name: 'Bistro', timeZone: 'Africa/Johannesburg', status: 'ACTIVE', rules }

function setup(permissions) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore()
  auth.accessToken = 'token'
  auth.tenantId = 't1'
  auth.me = { displayName: 'Ada', permissions, roles: [] }
  const venues = useVenuesStore()
  venues.tenantId = 't1'
  venues.venues = [venue]
  venues.currentVenueId = 'v1'
  venues.loaded = true
  return pinia
}

const board = {
  venueId: 'v1',
  date: '2026-10-09',
  timeZone: venue.timeZone,
  isClosed: false,
  periods: [{ startsAt: '2026-10-09T15:00:00Z', endsAt: '2026-10-09T21:00:00Z' }],
  slotIntervalMinutes: 15,
  turnoverMinutes: 0,
  truncated: false,
  blocks: [],
  bookings: [
    {
      id: 'b1',
      reference: 'BK-ABC',
      status: 'CONFIRMED',
      source: 'PHONE',
      partySize: 4,
      startsAt: '2026-10-09T17:00:00Z',
      endsAt: '2026-10-09T19:00:00Z',
      businessDate: '2026-10-09',
      time: '19:00',
      durationMinutes: 120,
      timeZone: venue.timeZone,
      customer: { id: 'c1', firstName: 'Grace', lastName: 'Hopper' },
      resources: [{ resourceId: 'r1', name: 'T1', code: 'T1' }],
      version: 1,
    },
  ],
}

describe('BookingBoardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    venueService.listAllResources.mockResolvedValue([
      {
        id: 'r1',
        name: 'T1',
        sectionId: null,
        sortOrder: 0,
        minCapacity: 1,
        capacity: 4,
        isActive: true,
      },
    ])
    venueService.listSections.mockResolvedValue([])
    venueService.listFloorPlans.mockResolvedValue([])
    bookingService.board.mockResolvedValue(board)
  })

  it('loads the board for the date in the URL and switches between views', async () => {
    const router = createTestRouter([{ path: '/bookings', name: 'bookings', component: Host }])
    const wrapper = await mountWithQuasar(Host, {
      pinia: setup(['booking.view', 'booking.create', 'floorplan.view']),
      router,
      initialPath: '/bookings?date=2026-10-09',
    })
    await flushPromises()

    expect(bookingService.board).toHaveBeenCalledWith('v1', '2026-10-09', expect.anything())
    expect(wrapper.find('[data-test="board-summary"]').text()).toContain('1 bookings · 4 covers')
    expect(wrapper.text()).toContain('Grace Hopper')
    expect(wrapper.find('[data-test="board-new-booking"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="board-block"]').exists()).toBe(false)

    wrapper.findComponent(BookingBoardPage).vm.view = 'list'
    await flushPromises()
    expect(wrapper.find('[data-test="booking-list"]').text()).toContain('BK-ABC')
    expect(router.currentRoute.value.query.view).toBe('list')

    wrapper.findComponent(BookingBoardPage).vm.view = 'floor'
    await flushPromises()
    expect(wrapper.find('[data-test="floor-empty"]').exists()).toBe(true)
  })

  it('hides creation for view-only users', async () => {
    const wrapper = await mountWithQuasar(Host, { pinia: setup(['booking.view']) })
    await flushPromises()
    expect(wrapper.find('[data-test="board-new-booking"]').exists()).toBe(false)
  })
})
