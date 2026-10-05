import { h } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { BlankComponent, mountWithQuasar } from '../helpers/mountWithQuasar.js'
import { useAuthStore } from '@/stores/auth.js'

vi.mock('@/services/paymentService.js', () => ({
  paymentService: {
    listForBooking: vi.fn(),
    refund: vi.fn(),
    getSettings: vi.fn(),
    updateSettings: vi.fn(),
  },
}))
vi.mock('@/services/publicBookingService.js', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    publicBookingService: { startPayment: vi.fn(), completeSimulatedCheckout: vi.fn() },
  }
})

const { paymentService } = await import('@/services/paymentService.js')
const { publicBookingService } = await import('@/services/publicBookingService.js')
const { default: DepositPayCard } = await import('@/components/payments/DepositPayCard.vue')
const { default: BookingPaymentsPanel } =
  await import('@/components/payments/BookingPaymentsPanel.vue')
const { default: DepositSettingsPanel } =
  await import('@/components/payments/DepositSettingsPanel.vue')
const { default: PaymentReturnPage } = await import('@/pages/public/PaymentReturnPage.vue')
const { default: SimulatedCheckoutPage } = await import('@/pages/public/SimulatedCheckoutPage.vue')
const { QLayout, QPageContainer } = await import('quasar')

const settle = async () => {
  await flushPromises()
  await new Promise((resolve) => setTimeout(resolve, 30))
  await flushPromises()
}

const page = (component) => ({
  render: () => h(QLayout, null, () => h(QPageContainer, null, () => h(component))),
})

function router() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: BlankComponent },
      { path: '/booking/manage', name: 'public-manage-booking', component: BlankComponent },
      { path: '/booking/payment-return', name: 'payment-return', component: BlankComponent },
      { path: '/pay/simulated', name: 'simulated-checkout', component: BlankComponent },
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

const payable = {
  status: 'CREATED',
  amountMinor: 10000,
  currency: 'ZAR',
  payBy: '2026-10-09T16:30:00Z',
  canPay: true,
}

describe('DepositPayCard', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sessionStorage.clear()
  })

  it('starts checkout, parks the manage token in this tab and leaves for the provider', async () => {
    publicBookingService.startPayment.mockResolvedValue({ redirectUrl: 'https://pay.test/x' })
    const navigate = vi.fn()
    const wrapper = await mountWithQuasar(DepositPayCard, {
      props: { payment: payable, manageToken: 'tok_1', timeZone: 'UTC', navigate },
    })

    expect(wrapper.find('[data-test="deposit-amount"]').text()).toContain('100')
    await wrapper.find('[data-test="pay-deposit"]').trigger('click')
    await settle()

    expect(publicBookingService.startPayment).toHaveBeenCalledWith('tok_1')
    expect(sessionStorage.getItem('bp.payment.manageToken')).toBe('tok_1')
    expect(navigate).toHaveBeenCalledWith('https://pay.test/x')
  })

  it('shows why checkout could not start and stays on the page', async () => {
    publicBookingService.startPayment.mockRejectedValue({
      response: { status: 503, data: { code: 'PAYMENT_UNAVAILABLE', detail: 'Down.' } },
    })
    const navigate = vi.fn()
    const wrapper = await mountWithQuasar(DepositPayCard, {
      props: { payment: payable, manageToken: 'tok_1', navigate },
    })
    await wrapper.find('[data-test="pay-deposit"]').trigger('click')
    await settle()

    expect(wrapper.find('[data-test="deposit-error"]').exists()).toBe(true)
    expect(navigate).not.toHaveBeenCalled()
  })

  it('has no pay button once the deposit is paid or expired', async () => {
    const paid = await mountWithQuasar(DepositPayCard, {
      props: { payment: { ...payable, status: 'SUCCEEDED', canPay: false }, manageToken: 't' },
    })
    expect(paid.find('[data-test="pay-deposit"]').exists()).toBe(false)
    expect(paid.text()).toContain('deposit has been received')

    const expired = await mountWithQuasar(DepositPayCard, {
      props: { payment: { ...payable, status: 'EXPIRED', canPay: false }, manageToken: 't' },
    })
    expect(expired.text()).toContain('not paid in time')
  })
})

describe('PaymentReturnPage', () => {
  beforeEach(() => sessionStorage.clear())

  it('sends the guest back to their booking with the parked token, then forgets it', async () => {
    sessionStorage.setItem('bp.payment.manageToken', 'tok_9')
    const r = router()
    await mountWithQuasar(page(PaymentReturnPage), {
      router: r,
      pinia: createPinia(),
      initialPath: '/booking/payment-return',
    })
    await settle()

    expect(r.currentRoute.value.name).toBe('public-manage-booking')
    expect(r.currentRoute.value.hash).toBe('#tok_9')
    expect(sessionStorage.getItem('bp.payment.manageToken')).toBeNull()
  })

  it('explains what to do when the token is not in this tab', async () => {
    const r = router()
    const wrapper = await mountWithQuasar(page(PaymentReturnPage), {
      router: r,
      pinia: createPinia(),
      initialPath: '/booking/payment-return',
    })
    await settle()
    expect(r.currentRoute.value.name).toBe('payment-return')
    expect(wrapper.find('[data-test="payment-return"]').exists()).toBe(true)
  })
})

describe('SimulatedCheckoutPage', () => {
  beforeEach(() => vi.clearAllMocks())

  async function mountCheckout(hash) {
    const r = router()
    const wrapper = await mountWithQuasar(page(SimulatedCheckoutPage), {
      router: r,
      pinia: createPinia(),
      initialPath: `/pay/simulated${hash}`,
    })
    await settle()
    return { wrapper, r }
  }

  it('completes the payment and follows a same-origin return URL', async () => {
    publicBookingService.completeSimulatedCheckout.mockResolvedValue(undefined)
    const ret = encodeURIComponent(`${window.location.origin}/booking/payment-return`)
    const { wrapper, r } = await mountCheckout(`#p=pay1&t=sig&a=5000&c=ZAR&r=${ret}`)

    expect(wrapper.find('[data-test="checkout-amount"]').text()).toContain('50')
    await wrapper.find('[data-test="simulate-pay"]').trigger('click')
    await settle()

    expect(publicBookingService.completeSimulatedCheckout).toHaveBeenCalledWith({
      paymentId: 'pay1',
      token: 'sig',
      outcome: 'SUCCEEDED',
    })
    expect(r.currentRoute.value.path).toBe('/booking/payment-return')
  })

  it('never follows a return URL on another origin', async () => {
    publicBookingService.completeSimulatedCheckout.mockResolvedValue(undefined)
    const ret = encodeURIComponent('https://evil.example/steal')
    const { wrapper, r } = await mountCheckout(`#p=pay1&t=sig&a=5000&c=ZAR&r=${ret}`)
    await wrapper.find('[data-test="simulate-decline"]').trigger('click')
    await settle()

    expect(publicBookingService.completeSimulatedCheckout).toHaveBeenCalledWith(
      expect.objectContaining({ outcome: 'FAILED' }),
    )
    expect(r.currentRoute.value.name).toBe('payment-return')
  })

  it('rejects an incomplete checkout link', async () => {
    const { wrapper } = await mountCheckout('#p=pay1')
    expect(wrapper.find('[data-test="checkout-invalid"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="simulate-pay"]').exists()).toBe(false)
  })
})

describe('BookingPaymentsPanel', () => {
  const payment = {
    id: 'pay1',
    bookingId: 'b1',
    purpose: 'DEPOSIT',
    provider: 'Simulated',
    amountMinor: 20000,
    currency: 'ZAR',
    status: 'SUCCEEDED',
    refundedAmountMinor: 0,
    refundableAmountMinor: 20000,
    failureReason: null,
    requiresAttention: false,
    refunds: [],
  }

  beforeEach(() => vi.clearAllMocks())

  async function mountPanel(permissions) {
    const wrapper = await mountWithQuasar(BookingPaymentsPanel, {
      pinia: pinia(permissions),
      props: { bookingId: 'b1' },
      attachTo: document.body,
    })
    await settle()
    return wrapper
  }

  it('lists payments without a refund action for view-only staff', async () => {
    paymentService.listForBooking.mockResolvedValue([{ ...payment, requiresAttention: true }])
    const wrapper = await mountPanel(['payment.view'])

    expect(paymentService.listForBooking).toHaveBeenCalledWith('b1')
    expect(wrapper.find('[data-test="payment-status"]').text()).toBe('Paid')
    expect(wrapper.find('[data-test="payment-attention"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="refund-open"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('refunds part of a payment with an idempotency key and reloads', async () => {
    paymentService.listForBooking
      .mockResolvedValueOnce([payment])
      .mockResolvedValueOnce([
        { ...payment, refundedAmountMinor: 5000, refundableAmountMinor: 15000 },
      ])
    paymentService.refund.mockResolvedValue({ id: 'r1', amountMinor: 5000, status: 'SUCCEEDED' })
    const wrapper = await mountPanel(['payment.view', 'payment.refund'])

    await wrapper.find('[data-test="refund-open"]').trigger('click')
    await settle()
    const amount = document.querySelector('input[data-test="refund-amount"]')
    expect(amount.value).toBe('200.00')
    amount.value = '50'
    amount.dispatchEvent(new Event('input'))
    await settle()
    document.querySelector('[data-test="refund-submit"]').click()
    await settle()

    expect(paymentService.refund).toHaveBeenCalledWith(
      'b1',
      'pay1',
      { amountMinor: 5000, reason: null },
      expect.stringMatching(/[0-9a-f-]{36}/),
    )
    expect(paymentService.listForBooking).toHaveBeenCalledTimes(2)
    expect(wrapper.find('[data-test="payment-refunded"]').text()).toContain('50')
    wrapper.unmount()
  })
})

describe('DepositSettingsPanel', () => {
  beforeEach(() => vi.clearAllMocks())

  it('saves a per-guest deposit in minor units', async () => {
    paymentService.getSettings.mockResolvedValue({
      venueId: 'v1',
      depositPolicy: 'NONE',
      depositAmountMinor: null,
      currency: 'ZAR',
      paymentWindowMinutes: 30,
      onlinePaymentsAvailable: true,
    })
    paymentService.updateSettings.mockImplementation(async (_, body) => ({
      venueId: 'v1',
      ...body,
      onlinePaymentsAvailable: true,
    }))
    const wrapper = await mountWithQuasar(DepositSettingsPanel, {
      pinia: pinia(['venue.manage']),
      props: { venueId: 'v1', canManage: true },
    })
    await settle()

    expect(wrapper.find('[data-test="deposit-amount"]').exists()).toBe(false)
    await wrapper.findAll('[data-test="deposit-policy"] .q-radio')[2].trigger('click')
    await wrapper.find('input[data-test="deposit-amount"]').setValue('75.50')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(paymentService.updateSettings).toHaveBeenCalledWith('v1', {
      depositPolicy: 'PER_GUEST',
      depositAmountMinor: 7550,
      currency: 'ZAR',
      paymentWindowMinutes: 30,
    })
  })

  it('warns that online booking is blocked when payments are not configured', async () => {
    paymentService.getSettings.mockResolvedValue({
      venueId: 'v1',
      depositPolicy: 'PER_BOOKING',
      depositAmountMinor: 10000,
      currency: 'ZAR',
      paymentWindowMinutes: 30,
      onlinePaymentsAvailable: false,
    })
    const wrapper = await mountWithQuasar(DepositSettingsPanel, {
      pinia: pinia([]),
      props: { venueId: 'v1', canManage: false },
    })
    await settle()
    expect(wrapper.find('[data-test="payments-unavailable"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="deposit-save"]').exists()).toBe(false)
  })
})
