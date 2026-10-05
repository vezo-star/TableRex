/** Money is always integer minor units (cents) plus an ISO currency, exactly as the API sends it. */
export function formatMoney(amountMinor, currency = 'ZAR') {
  const amount = Number(amountMinor ?? 0) / 100
  try {
    return new Intl.NumberFormat('en-ZA', { style: 'currency', currency }).format(amount)
  } catch {
    return `${currency} ${amount.toFixed(2)}`
  }
}

/** "123.45" (major units, as typed) to 12345 minor units; null when not a positive amount with at most 2 decimals. */
export function toMinorUnits(text) {
  const value = String(text ?? '')
    .trim()
    .replace(/\s/g, '')
    .replace(',', '.')
  if (!/^\d+(\.\d{1,2})?$/.test(value)) {
    return null
  }
  const [whole, fraction = ''] = value.split('.')
  const minor = Number(whole) * 100 + Number(fraction.padEnd(2, '0'))
  return minor > 0 ? minor : null
}

/** 12345 to "123.45" for editing. */
export function toMajorText(amountMinor) {
  return amountMinor == null ? '' : (Number(amountMinor) / 100).toFixed(2)
}

export const DepositPolicies = Object.freeze({
  NONE: 'NONE',
  PER_BOOKING: 'PER_BOOKING',
  PER_GUEST: 'PER_GUEST',
})

export const DepositPolicyOptions = Object.freeze([
  { value: DepositPolicies.NONE, label: 'No deposit' },
  { value: DepositPolicies.PER_BOOKING, label: 'Fixed amount per booking' },
  { value: DepositPolicies.PER_GUEST, label: 'Amount per guest' },
])

/** "A deposit of R 50,00 per guest (R 100,00 for 2 guests) is required to confirm." or '' when none. */
export function depositNotice(deposit, partySize) {
  if (!deposit) {
    return ''
  }
  const each = formatMoney(deposit.amountMinor, deposit.currency)
  if (deposit.policy === DepositPolicies.PER_GUEST) {
    const total = formatMoney(deposit.amountMinor * (partySize || 1), deposit.currency)
    return `A deposit of ${each} per guest (${total} for ${partySize} ${partySize === 1 ? 'guest' : 'guests'}) is required to confirm.`
  }
  return `A deposit of ${each} is required to confirm.`
}

export const PaymentStatusText = Object.freeze({
  CREATED: 'Awaiting payment',
  PENDING: 'Payment started',
  SUCCEEDED: 'Paid',
  FAILED: 'Payment failed',
  CANCELLED: 'Payment cancelled',
  EXPIRED: 'Payment window closed',
})

export const PaymentStatusColor = Object.freeze({
  CREATED: 'warning',
  PENDING: 'warning',
  SUCCEEDED: 'positive',
  FAILED: 'negative',
  CANCELLED: 'grey-7',
  EXPIRED: 'grey-7',
})

export const RefundStatusText = Object.freeze({
  PENDING: 'Refund pending',
  SUCCEEDED: 'Refunded',
  FAILED: 'Refund failed',
})

const PENDING_TOKEN_KEY = 'bp.payment.manageToken'

/**
 * The guest's manage token is parked in sessionStorage while they are at the provider, so the return URL given to
 * the provider never contains it. Only this tab can read it back.
 */
export function rememberPaymentToken(token, storage = globalThis.sessionStorage) {
  try {
    storage?.setItem(PENDING_TOKEN_KEY, token)
  } catch {
    // Storage disabled: the guest can still reopen their manage link.
  }
}

export function takePaymentToken(storage = globalThis.sessionStorage) {
  try {
    const token = storage?.getItem(PENDING_TOKEN_KEY) ?? ''
    storage?.removeItem(PENDING_TOKEN_KEY)
    return token
  } catch {
    return ''
  }
}

/** Parses the Simulated checkout fragment `#p=..&t=..&a=..&c=..&r=..`; null when incomplete. */
export function parseSimulatedCheckout(hash) {
  if (typeof hash !== 'string' || hash.length < 2) {
    return null
  }
  const params = new URLSearchParams(hash.slice(1))
  const paymentId = params.get('p')
  const token = params.get('t')
  const amountMinor = Number(params.get('a'))
  if (!paymentId || !token || !Number.isSafeInteger(amountMinor) || amountMinor <= 0) {
    return null
  }
  return {
    paymentId,
    token,
    amountMinor,
    currency: params.get('c') || 'ZAR',
    returnUrl: params.get('r') || '',
  }
}

/** Only same-origin return URLs are followed, so a crafted checkout link cannot redirect elsewhere. */
export function safeReturnUrl(url, origin = globalThis.location?.origin) {
  if (!url) {
    return null
  }
  try {
    const parsed = new URL(url, origin)
    return parsed.origin === origin ? `${parsed.pathname}${parsed.search}` : null
  } catch {
    return null
  }
}
