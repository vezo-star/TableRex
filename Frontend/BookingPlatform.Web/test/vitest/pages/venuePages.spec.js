import { h } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { BlankComponent, createTestRouter, mountWithQuasar } from '../helpers/mountWithQuasar.js'
import { useAuthStore } from '@/stores/auth.js'

vi.mock('@/services/venueService.js', () => ({
  venueService: {
    listVenues: vi.fn(),
    getOperatingHours: vi.fn(),
    replaceOperatingHours: vi.fn(),
    getSchedule: vi.fn(),
  },
}))

const { venueService } = await import('@/services/venueService.js')
const { default: OperatingHoursEditor } =
  await import('@/components/venues/OperatingHoursEditor.vue')
const { default: FloorPlanCanvas } = await import('@/components/floorplans/FloorPlanCanvas.vue')
const { default: VenuesPage } = await import('@/pages/venues/VenuesPage.vue')
const { QLayout, QPageContainer } = await import('quasar')

// QPage must be rendered inside QLayout > QPageContainer.
const VenuesHost = {
  render: () => h(QLayout, null, () => h(QPageContainer, null, () => h(VenuesPage))),
}

function signedIn(permissions) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore()
  auth.accessToken = 'token'
  auth.tenantId = 't1'
  auth.me = { displayName: 'Ada', permissions, roles: [] }
  return pinia
}

const venue = { id: 'v1', name: 'Bistro', timeZone: 'Africa/Johannesburg', status: 'ACTIVE' }

describe('OperatingHoursEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    venueService.getOperatingHours.mockResolvedValue([
      { dayOfWeek: 5, openTime: '18:00', closeTime: '02:00' },
    ])
    venueService.getSchedule.mockResolvedValue({ days: [] })
  })

  it('shows overnight periods and saves the week', async () => {
    venueService.replaceOperatingHours.mockImplementation(async (_id, periods) => periods)
    const wrapper = await mountWithQuasar(OperatingHoursEditor, {
      pinia: signedIn([]),
      props: { venue, canManage: true },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('+1 day')
    await wrapper.find('[data-test="save-hours"]').trigger('click')
    await flushPromises()

    expect(venueService.replaceOperatingHours).toHaveBeenCalledWith('v1', [
      { dayOfWeek: 5, openTime: '18:00', closeTime: '02:00' },
    ])
  })

  it('blocks overlapping periods before calling the API', async () => {
    venueService.getOperatingHours.mockResolvedValue([
      { dayOfWeek: 5, openTime: '18:00', closeTime: '02:00' },
      { dayOfWeek: 6, openTime: '01:00', closeTime: '10:00' },
    ])
    const wrapper = await mountWithQuasar(OperatingHoursEditor, {
      pinia: signedIn([]),
      props: { venue, canManage: true },
    })
    await flushPromises()

    await wrapper.find('[data-test="save-hours"]').trigger('click')
    await flushPromises()

    expect(venueService.replaceOperatingHours).not.toHaveBeenCalled()
    expect(wrapper.find('[data-test="hours-error"]').text()).toContain('overlap')
  })

  it('is read-only without venue.manage', async () => {
    const wrapper = await mountWithQuasar(OperatingHoursEditor, {
      pinia: signedIn([]),
      props: { venue, canManage: false },
    })
    await flushPromises()

    expect(wrapper.find('[data-test="save-hours"]').exists()).toBe(false)
  })
})

describe('FloorPlanCanvas', () => {
  const plan = { id: 'p1', name: 'Main', width: 1000, height: 800 }
  const positions = [
    { resourceId: 'r1', x: 10, y: 10, width: 80, height: 80, rotation: 45, shape: 'ROUND' },
    { resourceId: 'r2', x: 200, y: 10, width: 120, height: 100, rotation: 0, shape: 'RECT' },
  ]
  const resources = {
    r1: { id: 'r1', code: 'T1', minCapacity: 1, capacity: 2, isActive: true },
    r2: { id: 'r2', code: 'T2', minCapacity: 2, capacity: 4, isActive: true },
  }

  it('renders each placed resource with its code and rotation', async () => {
    const wrapper = await mountWithQuasar(FloorPlanCanvas, {
      props: { plan, positions, resources, editable: false },
    })

    expect(wrapper.find('[data-test="shape-r1"]').attributes('transform')).toContain('rotate(45')
    expect(wrapper.text()).toContain('T1')
    expect(wrapper.text()).toContain('2–4')
    expect(wrapper.find('ellipse').exists()).toBe(true)
  })

  it('selects a shape on pointer down and shows the resize handle only when editable', async () => {
    const wrapper = await mountWithQuasar(FloorPlanCanvas, {
      props: { plan, positions, resources, editable: true, selectedId: 'r2' },
    })

    await wrapper.find('[data-test="shape-r1"]').trigger('pointerdown')

    expect(wrapper.emitted('select')[0]).toEqual(['r1'])
    expect(wrapper.find('[data-test="resize-handle"]').exists()).toBe(true)

    await wrapper.setProps({ editable: false })
    expect(wrapper.find('[data-test="resize-handle"]').exists()).toBe(false)
  })
})

describe('VenuesPage', () => {
  beforeEach(() => vi.clearAllMocks())

  const router = () =>
    createTestRouter([
      { path: '/venues', name: 'venues', component: BlankComponent },
      { path: '/venues/:venueId', name: 'venue-settings', component: BlankComponent },
    ])

  it('lists venues and hides the add button without venue.manage', async () => {
    venueService.listVenues.mockResolvedValue([venue])
    const wrapper = await mountWithQuasar(VenuesHost, {
      pinia: signedIn(['venue.view']),
      router: router(),
      initialPath: '/venues',
    })
    await flushPromises()

    expect(wrapper.text()).toContain('Bistro')
    expect(wrapper.find('[data-test="add-venue"]').exists()).toBe(false)
  })

  it('offers venue creation to managers of venues', async () => {
    venueService.listVenues.mockResolvedValue([])
    const wrapper = await mountWithQuasar(VenuesHost, {
      pinia: signedIn(['venue.view', 'venue.manage']),
      router: router(),
      initialPath: '/venues',
    })
    await flushPromises()

    expect(wrapper.find('[data-test="add-venue"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('No venues yet')
  })
})
