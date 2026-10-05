import { h } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { mountWithQuasar, BlankComponent } from '../helpers/mountWithQuasar.js'

vi.mock('@/services/publicBookingService.js', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    publicBookingService: {
      venue: vi.fn(),
      availability: vi.fn(),
      create: vi.fn(),
      getBooking: vi.fn(),
      cancel: vi.fn(),
    },
  }
})

const { publicBookingService } = await import('@/services/publicBookingService.js')
const { default: PublicBookingPage } = await import('@/pages/public/PublicBookingPage.vue')
const { default: ManageBookingPage } = await import('@/pages/public/ManageBookingPage.vue')
const { QLayout, QPageContainer } = await import('quasar')

const host = (component, props = {}) => ({
  render: () => h(QLayout, null, () => h(QPageContainer, null, () => h(component, props))),
})

function router() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: BlankComponent },
      { path: '/book/:tenantSlug/:venueSlug', name: 'public-booking', component: BlankComponent },
      { path: '/booking/manage', name: 'public-manage-booking', component: BlankComponent },
    ],
  })
}

const venue = {
  tenantSlug: 'demo',
  tenantName: 'Demo Hospitality',
  slug: 'bistro',
  name: 'Bistro',
  description: 'Garden terrace',
  addressLine1: '1 Main Rd',
  city: 'Cape Town',
  phone: '+27 21 000 0000',
  timeZone: 'Africa/Johannesburg',
  minPartySize: 1,
  maxPartySize: 8,
  durationMinutes: 120,
  minLeadTimeMinutes: 60,
  today: '2026-10-09',
  lastBookableDate: '2027-01-07',
  cancellationCutoffMinutes: 120,
  autoConfirm: true,
}

const times = (...values) => ({
  date: '2026-10-09',
  timeZone: 'Africa/Johannesburg',
  partySize: 2,
  durationMinutes: 120,
  isClosed: false,
  times: values.map((time) => ({ time, startsAt: `2026-10-09T${time}:00+02:00` })),
})

const settle = async () => {
  await flushPromises()
  await new Promise((resolve) => setTimeout(resolve, 50))
  await flushPromises()
}

async function mountBooking() {
  const wrapper = await mountWithQuasar(
    host(PublicBookingPage, { tenantSlug: 'demo', venueSlug: 'bistro' }),
    { router: router(), pinia: createPinia() },
  )
  await settle()
  return wrapper
}

async function fillGuest(wrapper) {
  await wrapper.find('input[data-test="first-name-input"]').setValue('Ada')
  await wrapper.find('input[data-test="email-input"]').setValue('ada@example.test')
}

describe('PublicBookingPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.scrollTo = vi.fn()
    publicBookingService.venue.mockResolvedValue(venue)
  })

  it('shows the venue and loads times for today and two guests', async () => {
    publicBookingService.availability.mockResolvedValue(times('18:00', '18:15'))
    const wrapper = await mountBooking()

    expect(publicBookingService.venue).toHaveBeenCalledWith('demo', 'bistro')
    expect(publicBookingService.availability).toHaveBeenCalledWith(
      'demo',
      'bistro',
      { date: '2026-10-09', partySize: 2 },
      expect.objectContaining({ signal: expect.any(Object) }),
    )
    expect(wrapper.find('[data-test="venue-header"]').text()).toContain('Bistro')
    expect(wrapper.findAll('[data-test="time-slot"]').map((b) => b.text())).toEqual([
      '18:00',
      '18:15',
    ])
    expect(wrapper.find('[data-test="guest-form-card"]').exists()).toBe(false)
  })

  it('shows closed and empty states', async () => {
    publicBookingService.availability.mockResolvedValueOnce({ ...times(), isClosed: true })
    const wrapper = await mountBooking()
    expect(wrapper.find('[data-test="times-closed"]').exists()).toBe(true)

    publicBookingService.availability.mockResolvedValueOnce(times())
    await wrapper.find('input[data-test="date-input"]').setValue('2026-10-10')
    await settle()
    expect(wrapper.find('[data-test="times-empty"]').exists()).toBe(true)
  })

  it('books a time and shows the confirmation with a private manage link', async () => {
    publicBookingService.availability.mockResolvedValue(times('18:00'))
    publicBookingService.create.mockResolvedValue({
      booking: {
        reference: 'BK-123',
        status: 'CONFIRMED',
        date: '2026-10-09',
        time: '18:00',
        partySize: 2,
        manageToken: 'tok_123',
      },
      replayed: false,
    })
    const wrapper = await mountBooking()

    await wrapper.find('[data-test="time-slot"]').trigger('click')
    await fillGuest(wrapper)
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(publicBookingService.create).toHaveBeenCalledTimes(1)
    const [tenant, slug, payload, key] = publicBookingService.create.mock.calls[0]
    expect([tenant, slug]).toEqual(['demo', 'bistro'])
    expect(payload).toMatchObject({
      date: '2026-10-09',
      time: '18:00',
      partySize: 2,
      firstName: 'Ada',
      email: 'ada@example.test',
      website: null,
    })
    expect(key).toMatch(/[0-9a-f-]{36}/)

    expect(wrapper.find('[data-test="booking-reference"]').text()).toBe('BK-123')
    expect(wrapper.find('[data-test="manage-link"]').attributes('href')).toMatch(
      /\/booking\/manage#tok_123$/,
    )
  })

  it('reloads times and asks for another one when the slot was just taken', async () => {
    publicBookingService.availability
      .mockResolvedValueOnce(times('18:00', '18:15'))
      .mockResolvedValueOnce(times('18:15'))
    publicBookingService.create.mockRejectedValue({
      response: { status: 409, data: { code: 'RESOURCE_NOT_AVAILABLE', detail: 'Taken.' } },
    })
    const wrapper = await mountBooking()

    await wrapper.find('[data-test="time-slot"]').trigger('click')
    await fillGuest(wrapper)
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(publicBookingService.availability).toHaveBeenCalledTimes(2)
    expect(wrapper.findAll('[data-test="time-slot"]').map((b) => b.text())).toEqual(['18:15'])
    expect(wrapper.find('[data-test="guest-form-card"]').exists()).toBe(false)
  })

  it('reuses the idempotency key when a submission fails without an answer', async () => {
    publicBookingService.availability.mockResolvedValue(times('18:00'))
    publicBookingService.create
      .mockRejectedValueOnce({ code: 'ERR_NETWORK' })
      .mockResolvedValueOnce({ booking: { reference: 'BK-1', status: 'PENDING' } })
    const wrapper = await mountBooking()

    await wrapper.find('[data-test="time-slot"]').trigger('click')
    await fillGuest(wrapper)
    await wrapper.find('form').trigger('submit')
    await settle()
    expect(wrapper.find('[data-test="submit-error"]').exists()).toBe(true)

    await wrapper.find('form').trigger('submit')
    await settle()

    const keys = publicBookingService.create.mock.calls.map((call) => call[3])
    expect(keys).toHaveLength(2)
    expect(keys[0]).toBe(keys[1])
    expect(wrapper.find('[data-test="booking-confirmation"]').text()).toContain('Request received')
  })

  it('explains when the venue is not taking bookings', async () => {
    publicBookingService.venue.mockRejectedValue({
      response: { status: 404, data: { code: 'VENUE_NOT_FOUND', detail: 'Venue not found.' } },
    })
    const wrapper = await mountBooking()
    expect(wrapper.find('[data-test="error-state"]').text()).toContain('not taking online bookings')
    expect(wrapper.find('[data-test="error-retry"]').exists()).toBe(false)
  })
})

describe('ManageBookingPage', () => {
  const booking = {
    reference: 'BK-123',
    status: 'CONFIRMED',
    tenantSlug: 'demo',
    venueSlug: 'bistro',
    venueName: 'Bistro',
    venuePhone: '+27 21 000 0000',
    venueEmail: null,
    date: '2026-10-09',
    time: '18:00',
    durationMinutes: 120,
    partySize: 2,
    timeZone: 'Africa/Johannesburg',
    startsAt: '2026-10-09T16:00:00Z',
    guestFirstName: 'Ada',
    specialRequests: null,
    canCancel: true,
    cancelUntil: '2026-10-09T14:00:00Z',
    cancelledAt: null,
  }

  beforeEach(() => vi.clearAllMocks())

  async function mountManage(path) {
    const wrapper = await mountWithQuasar(host(ManageBookingPage), {
      router: router(),
      pinia: createPinia(),
      initialPath: path,
      attachTo: document.body,
    })
    await settle()
    return wrapper
  }

  it('asks for the full link when the fragment is missing', async () => {
    const wrapper = await mountManage('/booking/manage')
    expect(wrapper.find('[data-test="missing-token"]').exists()).toBe(true)
    expect(publicBookingService.getBooking).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('loads the booking with the fragment token and cancels after confirming', async () => {
    publicBookingService.getBooking.mockResolvedValue(booking)
    publicBookingService.cancel.mockResolvedValue({
      ...booking,
      status: 'CANCELLED',
      canCancel: false,
      cancelledAt: '2026-10-08T10:00:00Z',
    })
    const wrapper = await mountManage('/booking/manage#tok_123')

    expect(publicBookingService.getBooking).toHaveBeenCalledWith('tok_123')
    expect(wrapper.find('[data-test="manage-booking"]').text()).toContain('BK-123')

    await wrapper.find('[data-test="cancel-booking"]').trigger('click')
    await settle()
    const ok = [...document.querySelectorAll('.q-dialog button')].find((b) =>
      b.textContent.includes('Cancel booking'),
    )
    ok.click()
    await settle()

    expect(publicBookingService.cancel).toHaveBeenCalledWith('tok_123')
    expect(wrapper.find('[data-test="cancelled-note"]').exists()).toBe(true)
    wrapper.unmount()
  })

  it('tells the guest to contact the venue once online changes are closed', async () => {
    publicBookingService.getBooking.mockResolvedValue({ ...booking, canCancel: false })
    const wrapper = await mountManage('/booking/manage#tok_123')
    expect(wrapper.find('[data-test="contact-venue"]').text()).toContain('+27 21 000 0000')
    expect(wrapper.find('[data-test="cancel-booking"]').exists()).toBe(false)
    wrapper.unmount()
  })
})
