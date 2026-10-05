import { h } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { BlankComponent, mountWithQuasar } from '../helpers/mountWithQuasar.js'
import { useAuthStore } from '@/stores/auth.js'
import { useVenuesStore } from '@/stores/venues.js'

vi.mock('@/services/saasService.js', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    saasService: {
      listPlans: vi.fn(),
      getSubscription: vi.fn(),
      getBranding: vi.fn(),
      updateBranding: vi.fn(),
      getTenantSubscription: vi.fn(),
      updateTenantSubscription: vi.fn(),
      bookingsSummary: vi.fn(),
      bookingsCsv: vi.fn(),
    },
  }
})
vi.mock('@/services/publicBookingService.js', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    publicBookingService: { tenant: vi.fn(), venue: vi.fn(), availability: vi.fn() },
  }
})
vi.mock('@/services/tenancyService.js', () => ({
  tenancyService: { listPlatformTenants: vi.fn(), createPlatformTenant: vi.fn() },
}))

const { saasService } = await import('@/services/saasService.js')
const { publicBookingService } = await import('@/services/publicBookingService.js')
const { tenancyService } = await import('@/services/tenancyService.js')
const { default: ReportsPage } = await import('@/pages/reports/ReportsPage.vue')
const { default: PlanUsageCard } = await import('@/components/settings/PlanUsageCard.vue')
const { default: BrandingSettingsCard } =
  await import('@/components/settings/BrandingSettingsCard.vue')
const { default: PublicTenantPage } = await import('@/pages/public/PublicTenantPage.vue')
const { default: PublicBookingPage } = await import('@/pages/public/PublicBookingPage.vue')
const { default: PlatformTenantsPage } = await import('@/pages/platform/PlatformTenantsPage.vue')
const { QLayout, QPageContainer } = await import('quasar')

const settle = async () => {
  await flushPromises()
  await new Promise((resolve) => setTimeout(resolve, 30))
  await flushPromises()
}

const inLayout = (component, props = {}) => ({
  render: () => h(QLayout, null, () => h(QPageContainer, null, () => h(component, props))),
})

function router() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: BlankComponent },
      { path: '/book/:tenantSlug', name: 'public-tenant', component: BlankComponent },
      { path: '/book/:tenantSlug/:venueSlug', name: 'public-booking', component: BlankComponent },
    ],
  })
}

function pinia(permissions = []) {
  const p = createPinia()
  setActivePinia(p)
  const auth = useAuthStore()
  auth.accessToken = 'token'
  auth.tenantId = 't1'
  auth.me = { displayName: 'Ada', permissions, roles: [] }
  return p
}

const summary = {
  venueId: 'v1',
  venueName: 'Bistro',
  timeZone: 'Africa/Johannesburg',
  from: '2026-10-01',
  to: '2026-10-02',
  totalBookings: 3,
  bookings: 2,
  covers: 6,
  cancelled: 1,
  noShows: 1,
  cancellationRate: 0.3333,
  noShowRate: 0.5,
  averagePartySize: 3,
  byStatus: [
    { key: 'CANCELLED', bookings: 1, covers: 3 },
    { key: 'CONFIRMED', bookings: 1, covers: 4 },
    { key: 'NO_SHOW', bookings: 1, covers: 2 },
  ],
  bySource: [
    { key: 'PHONE', bookings: 1, covers: 4 },
    { key: 'PUBLIC', bookings: 1, covers: 2 },
  ],
  daily: [
    { date: '2026-10-01', bookings: 0, covers: 0, cancelled: 0, noShows: 0 },
    { date: '2026-10-02', bookings: 2, covers: 6, cancelled: 1, noShows: 1 },
  ],
  byHour: [
    { hour: 19, bookings: 1, covers: 2 },
    { hour: 21, bookings: 1, covers: 4 },
  ],
  deposits: [{ currency: 'ZAR', paidCount: 1, collectedMinor: 10000, refundedMinor: 4000 }],
}

describe('ReportsPage', () => {
  beforeEach(() => vi.clearAllMocks())

  async function mountReports() {
    const p = pinia(['reports.view'])
    const venues = useVenuesStore()
    venues.$patch({
      tenantId: 't1',
      venues: [{ id: 'v1', name: 'Bistro', status: 'ACTIVE', timeZone: 'Africa/Johannesburg' }],
      currentVenueId: 'v1',
      loaded: true,
    })
    const wrapper = await mountWithQuasar(inLayout(ReportsPage), { router: router(), pinia: p })
    await settle()
    return wrapper
  }

  it('loads the last 30 days for the current venue and renders KPIs and charts', async () => {
    saasService.bookingsSummary.mockResolvedValue(summary)
    const wrapper = await mountReports()

    const [query] = saasService.bookingsSummary.mock.calls[0]
    expect(query.venueId).toBe('v1')
    expect(
      (Date.parse(`${query.to}T00:00:00Z`) - Date.parse(`${query.from}T00:00:00Z`)) / 86_400_000,
    ).toBe(29)
    expect(wrapper.find('[data-test="kpi-bookings"]').text()).toContain('2')
    expect(wrapper.find('[data-test="kpi-cancellation"]').text()).toContain('33.3%')
    expect(wrapper.find('[data-test="kpi-noshow"]').text()).toContain('50%')
    expect(wrapper.findAll('[data-test="hour-row"]')).toHaveLength(3)
    expect(wrapper.find('[data-test="status-table"]').text()).toContain('No-show')
    expect(wrapper.findAll('[data-test="source-row"]')[1].text()).toContain('Online')
    expect(wrapper.find('[data-test="deposits-card"]').text()).toContain('collected')
  })

  it('blocks invalid ranges and downloads the CSV', async () => {
    saasService.bookingsSummary.mockResolvedValue(summary)
    saasService.bookingsCsv.mockResolvedValue({ blob: new Blob(['x']), fileName: 'b.csv' })
    globalThis.URL.createObjectURL = vi.fn(() => 'blob:x')
    globalThis.URL.revokeObjectURL = vi.fn()
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    const wrapper = await mountReports()

    await wrapper.find('[data-test="report-csv"]').trigger('click')
    await settle()
    expect(saasService.bookingsCsv).toHaveBeenCalledWith(expect.objectContaining({ venueId: 'v1' }))
    expect(click).toHaveBeenCalled()

    const from = wrapper.find('input[data-test="report-from"]')
    await from.setValue('2030-01-01')
    expect(wrapper.find('[data-test="range-error"]').exists()).toBe(true)
    click.mockRestore()
  })

  it('shows an empty state when there are no bookings', async () => {
    saasService.bookingsSummary.mockResolvedValue({
      ...summary,
      bookings: 0,
      totalBookings: 0,
      byStatus: [],
      bySource: [],
      byHour: [],
      deposits: [],
    })
    const wrapper = await mountReports()
    expect(wrapper.find('[data-test="daily-empty"]').exists()).toBe(true)
  })
})

describe('Settings cards', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows plan usage against limits', async () => {
    saasService.getSubscription.mockResolvedValue({
      plan: { code: 'STARTER', name: 'Starter', description: 'One venue', customBranding: false },
      limits: { maxVenues: 1, maxActiveStaff: 5, maxResourcesPerVenue: null },
      overrides: { maxVenues: null, maxActiveStaff: null, maxResourcesPerVenue: null },
      usage: { venues: 1, activeStaff: 2, maxResourcesInAVenue: 12, bookingsThisMonth: 40 },
    })
    const wrapper = await mountWithQuasar(PlanUsageCard, { router: router(), pinia: pinia() })
    await settle()

    expect(wrapper.find('[data-test="plan-name"]').text()).toBe('Starter')
    expect(wrapper.find('[data-test="usage-venues"]').text()).toContain('1 of 1')
    expect(wrapper.find('[data-test="usage-resources"]').text()).toContain('12 (unlimited)')
    expect(wrapper.find('[data-test="usage-bookings"]').text()).toContain('40')
  })

  it('disables branding when the plan lacks it', async () => {
    saasService.getBranding.mockResolvedValue({ enabled: false, primaryColor: null })
    const wrapper = await mountWithQuasar(BrandingSettingsCard, {
      router: router(),
      pinia: pinia(),
      props: { tenantSlug: 'demo' },
    })
    await settle()

    expect(wrapper.find('[data-test="branding-upgrade"]').exists()).toBe(true)
    expect(wrapper.find('input[data-test="brand-color"]').attributes('disabled')).toBeDefined()
  })

  it('saves branding with blanks sent as null', async () => {
    saasService.getBranding.mockResolvedValue({
      enabled: true,
      primaryColor: '#1a73e8',
      logoUrl: null,
    })
    saasService.updateBranding.mockImplementation(async (payload) => ({
      ...payload,
      enabled: true,
    }))
    const wrapper = await mountWithQuasar(BrandingSettingsCard, {
      router: router(),
      pinia: pinia(),
      props: { tenantSlug: 'demo' },
    })
    await settle()

    await wrapper.find('input[data-test="brand-logo-url"]').setValue('https://cdn.test/logo.png')
    await wrapper.find('textarea[data-test="brand-intro"]').setValue('  Welcome  ')
    await wrapper.find('[data-test="branding-form"]').trigger('submit')
    await settle()

    expect(saasService.updateBranding).toHaveBeenCalledWith({
      primaryColor: '#1a73e8',
      logoUrl: 'https://cdn.test/logo.png',
      bookingPageIntro: 'Welcome',
      bookingTerms: null,
    })
  })
})

describe('Public branding and locations', () => {
  beforeEach(() => vi.clearAllMocks())

  const branding = {
    primaryColor: '#1a73e8',
    logoUrl: 'https://cdn.test/logo.png',
    bookingPageIntro: 'Welcome to our places',
    bookingTerms: 'No refunds.',
  }

  it('lists locations with branding applied', async () => {
    publicBookingService.tenant.mockResolvedValue({
      slug: 'demo',
      name: 'Demo Group',
      branding,
      venues: [
        { slug: 'alpha', name: 'Alpha', city: 'Cape Town' },
        { slug: 'bravo', name: 'Bravo', city: null },
      ],
    })
    const wrapper = await mountWithQuasar(inLayout(PublicTenantPage, { tenantSlug: 'demo' }), {
      router: router(),
      pinia: pinia(),
    })
    await settle()

    expect(wrapper.find('[data-test="tenant-name"]').text()).toBe('Demo Group')
    expect(wrapper.find('[data-test="brand-logo"]').attributes('src')).toBe(
      'https://cdn.test/logo.png',
    )
    expect(wrapper.find('[data-test="brand-intro"]').text()).toBe('Welcome to our places')
    const locations = wrapper.findAll('[data-test="location"]')
    expect(locations).toHaveLength(2)
    expect(locations[0].attributes('href')).toBe('/book/demo/alpha')
    expect(wrapper.find('.q-page').attributes('style')).toContain('--q-primary: #1a73e8')
  })

  it('never renders an unsafe logo URL', async () => {
    publicBookingService.tenant.mockResolvedValue({
      slug: 'demo',
      name: 'Demo',
      branding: { ...branding, logoUrl: 'javascript:alert(1)', primaryColor: 'red' },
      venues: [],
    })
    const wrapper = await mountWithQuasar(inLayout(PublicTenantPage, { tenantSlug: 'demo' }), {
      router: router(),
      pinia: pinia(),
    })
    await settle()

    expect(wrapper.find('[data-test="brand-logo"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="no-locations"]').exists()).toBe(true)
    expect(wrapper.find('.q-page').attributes('style') ?? '').not.toContain('--q-primary')
  })

  it('brands the venue booking page and links back to all locations', async () => {
    publicBookingService.venue.mockResolvedValue({
      tenantSlug: 'demo',
      tenantName: 'Demo Group',
      slug: 'alpha',
      name: 'Alpha',
      timeZone: 'Africa/Johannesburg',
      minPartySize: 1,
      maxPartySize: 6,
      durationMinutes: 90,
      today: '2026-10-05',
      lastBookableDate: '2026-12-31',
      cancellationCutoffMinutes: 60,
      autoConfirm: true,
      deposit: null,
      branding,
    })
    publicBookingService.availability.mockResolvedValue({
      date: '2026-10-05',
      isClosed: false,
      times: [{ time: '19:00', startsAt: '2026-10-05T17:00:00Z' }],
    })
    const wrapper = await mountWithQuasar(
      inLayout(PublicBookingPage, { tenantSlug: 'demo', venueSlug: 'alpha' }),
      { router: router(), pinia: pinia() },
    )
    await settle()

    expect(wrapper.find('[data-test="tenant-link"]').attributes('href')).toBe('/book/demo')
    expect(wrapper.find('[data-test="brand-intro"]').exists()).toBe(true)
    await wrapper.find('[data-test="time-slot"]').trigger('click')
    expect(wrapper.find('[data-test="booking-terms"]').text()).toBe('No refunds.')
  })
})

describe('PlatformTenantsPage plans', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows plans and saves a plan change with overrides', async () => {
    saasService.listPlans.mockResolvedValue([
      { code: 'STARTER', name: 'Starter', maxVenues: 1 },
      { code: 'PROFESSIONAL', name: 'Professional', maxVenues: 5 },
    ])
    tenancyService.listPlatformTenants.mockResolvedValue({
      items: [
        {
          id: 't9',
          name: 'Fresh',
          slug: 'fresh',
          status: 'ACTIVE',
          defaultTimeZone: 'UTC',
          planCode: 'STARTER',
        },
      ],
      totalCount: 1,
    })
    saasService.getTenantSubscription.mockResolvedValue({
      plan: { code: 'STARTER', name: 'Starter' },
      overrides: { maxVenues: null, maxActiveStaff: null, maxResourcesPerVenue: null },
      usage: { venues: 1, activeStaff: 1, maxResourcesInAVenue: 0 },
    })
    saasService.updateTenantSubscription.mockResolvedValue({
      plan: { code: 'STARTER', name: 'Starter' },
    })
    const wrapper = await mountWithQuasar(inLayout(PlatformTenantsPage), {
      router: router(),
      pinia: pinia(),
      attachTo: document.body,
    })
    await settle()

    expect(wrapper.find('[data-test="plan-fresh"]').text()).toBe('Starter')
    await wrapper.find('[data-test="change-plan-fresh"]').trigger('click')
    await settle()
    expect(document.body.querySelector('[data-test="plan-usage"]').textContent).toContain(
      '1 venues',
    )

    const venues = document.body.querySelector('input[data-test="override-venues"]')
    venues.value = '3'
    venues.dispatchEvent(new Event('input'))
    document.body.querySelector('[data-test="plan-save"]').click()
    await settle()

    expect(saasService.updateTenantSubscription).toHaveBeenCalledWith('t9', {
      planCode: 'STARTER',
      maxVenuesOverride: 3,
      maxActiveStaffOverride: null,
      maxResourcesPerVenueOverride: null,
    })
    wrapper.unmount()
  })
})
