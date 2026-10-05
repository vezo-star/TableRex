import { h } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { mountWithQuasar } from '../helpers/mountWithQuasar.js'
import { useAuthStore } from '@/stores/auth.js'
import { useVenuesStore } from '@/stores/venues.js'

vi.mock('@/services/venueService.js', () => ({
  venueService: {
    listVenues: vi.fn(),
    listSections: vi.fn(),
    listAllResources: vi.fn(),
    listCombinations: vi.fn(),
    createCombination: vi.fn(),
    updateCombination: vi.fn(),
  },
}))
vi.mock('@/services/availabilityService.js', () => ({
  availabilityService: { search: vi.fn() },
}))

const { venueService } = await import('@/services/venueService.js')
const { availabilityService } = await import('@/services/availabilityService.js')
const { default: AvailabilityPage } = await import('@/pages/availability/AvailabilityPage.vue')
const { default: CombinationDialog } = await import('@/components/resources/CombinationDialog.vue')
const { QLayout, QPageContainer } = await import('quasar')

const Host = {
  render: () => h(QLayout, null, () => h(QPageContainer, null, () => h(AvailabilityPage))),
}

const rules = {
  slotIntervalMinutes: 15,
  defaultDurationMinutes: 120,
  minDurationMinutes: 30,
  maxDurationMinutes: 240,
}
const venue = { id: 'v1', name: 'Bistro', timeZone: 'Africa/Johannesburg', status: 'ACTIVE', rules }
const resources = [
  { id: 'r1', name: 'T1', capacity: 4, sectionId: 's1', isActive: true },
  { id: 'r2', name: 'T2', capacity: 4, sectionId: null, isActive: true },
]

function setup(permissions = ['booking.view']) {
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

function response(overrides = {}) {
  return {
    venueId: 'v1',
    date: '2026-10-09',
    timeZone: 'Africa/Johannesburg',
    partySize: 2,
    durationMinutes: 120,
    turnoverMinutes: 0,
    isClosed: false,
    slots: [
      {
        time: '18:00',
        startsAt: '2026-10-09T16:00:00Z',
        endsAt: '2026-10-09T18:00:00Z',
        available: false,
        options: [],
      },
      {
        time: '18:15',
        startsAt: '2026-10-09T16:15:00Z',
        endsAt: '2026-10-09T18:15:00Z',
        available: true,
        options: [
          { kind: 'RESOURCE', id: 'r1', name: 'T1', capacity: 4, resourceIds: ['r1'] },
          { kind: 'COMBINATION', id: 'c1', name: 'Big', capacity: 8, resourceIds: ['r1', 'r2'] },
        ],
      },
    ],
    ...overrides,
  }
}

async function settle() {
  await flushPromises()
  await new Promise((resolve) => setTimeout(resolve, 300))
  await flushPromises()
}

describe('AvailabilityPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    venueService.listSections.mockResolvedValue([{ id: 's1', name: 'Patio', isActive: true }])
    venueService.listAllResources.mockResolvedValue(resources)
  })

  it('searches with venue defaults and shows slots with the first free time selected', async () => {
    availabilityService.search.mockResolvedValue(response())
    const wrapper = await mountWithQuasar(Host, { pinia: setup() })
    await settle()

    expect(availabilityService.search).toHaveBeenCalledTimes(1)
    const [query] = availabilityService.search.mock.calls[0]
    expect(query).toMatchObject({ venueId: 'v1', partySize: 2, durationMinutes: 120 })
    expect(query.date).toMatch(/^\d{4}-\d{2}-\d{2}$/)

    expect(wrapper.find('[data-test="availability-summary"]').text()).toContain('1 of 2')
    const options = wrapper.find('[data-test="availability-options"]').text()
    expect(options).toContain('18:15')
    expect(options).toContain('Best fit')
    expect(options).toContain('Patio')
    expect(options).toContain('T1 + T2')
  })

  it('shows the closed state', async () => {
    availabilityService.search.mockResolvedValue(response({ isClosed: true, slots: [] }))
    const wrapper = await mountWithQuasar(Host, { pinia: setup() })
    await settle()

    expect(wrapper.find('[data-test="availability-closed"]').exists()).toBe(true)
  })

  it('re-searches when the party size changes and shows field errors from the API', async () => {
    availabilityService.search.mockResolvedValueOnce(response())
    availabilityService.search.mockRejectedValueOnce({
      response: {
        status: 400,
        data: { code: 'VALIDATION_ERROR', errors: { partySize: ['Too many guests.'] } },
      },
    })
    const wrapper = await mountWithQuasar(Host, { pinia: setup() })
    await settle()

    await wrapper.find('input[data-test="availability-party"]').setValue('2000')
    await settle()

    expect(availabilityService.search).toHaveBeenCalledTimes(2)
    expect(availabilityService.search.mock.calls[1][0].partySize).toBe(2000)
    expect(wrapper.text()).toContain('Too many guests.')
  })
})

describe('CombinationDialog', () => {
  beforeEach(() => vi.clearAllMocks())

  it('creates a combination with required flags per member', async () => {
    venueService.createCombination.mockResolvedValue({ id: 'c1' })
    const wrapper = await mountWithQuasar(CombinationDialog, {
      pinia: setup(['resource.create']),
      props: { modelValue: false, venueId: 'v1', resources },
    })
    await wrapper.setProps({ modelValue: true })
    await flushPromises()

    const vm = wrapper.vm
    vm.form.name = 'T1 + T2'
    vm.form.resourceIds = ['r1', 'r2']
    await flushPromises()
    vm.form.required.r2 = false
    vm.form.minCapacity = 5
    vm.form.capacity = 8
    await vm.save()
    await flushPromises()

    expect(venueService.createCombination).toHaveBeenCalledWith({
      venueId: 'v1',
      name: 'T1 + T2',
      minCapacity: 5,
      capacity: 8,
      sortOrder: 0,
      isBookableOnline: true,
      members: [
        { resourceId: 'r1', isRequired: true },
        { resourceId: 'r2', isRequired: false },
      ],
    })
    expect(wrapper.emitted('saved')).toBeTruthy()
  })
})
