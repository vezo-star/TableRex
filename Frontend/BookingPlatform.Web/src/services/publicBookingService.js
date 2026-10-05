import { HttpHeaders } from '@/constants/httpHeaders.js'
import { http } from '@/services/http.js'

const slug = (value) => encodeURIComponent(value)

/** Guest calls never carry a staff session, even when staff are signed in on the same browser. */
const ANONYMOUS = Object.freeze({ skipAuth: true, skipAuthRefresh: true })

const venuePath = (tenantSlug, venueSlug) => `/v1/public/${slug(tenantSlug)}/${slug(venueSlug)}`

const tokenHeaders = (token) => ({ [HttpHeaders.BOOKING_TOKEN]: token })

export const publicBookingService = {
  async venue(tenantSlug, venueSlug, { signal } = {}) {
    const { data } = await http.get(venuePath(tenantSlug, venueSlug), { ...ANONYMOUS, signal })
    return data
  },

  async availability(tenantSlug, venueSlug, { date, partySize }, { signal } = {}) {
    const { data } = await http.get(`${venuePath(tenantSlug, venueSlug)}/availability`, {
      ...ANONYMOUS,
      params: { date, partySize },
      signal,
    })
    return data
  },

  /** Returns `{ booking, replayed }`; reuse `idempotencyKey` when retrying the same submission. */
  async create(tenantSlug, venueSlug, payload, idempotencyKey) {
    const response = await http.post(`${venuePath(tenantSlug, venueSlug)}/bookings`, payload, {
      ...ANONYMOUS,
      headers: { [HttpHeaders.IDEMPOTENCY_KEY]: idempotencyKey },
    })
    return {
      booking: response.data,
      replayed: response.headers?.['idempotent-replayed'] === 'true',
    }
  },

  async getBooking(token, { signal } = {}) {
    const { data } = await http.get('/v1/public/booking', {
      ...ANONYMOUS,
      headers: tokenHeaders(token),
      signal,
    })
    return data
  },

  async cancel(token) {
    const { data } = await http.post('/v1/public/booking/cancel', null, {
      ...ANONYMOUS,
      headers: tokenHeaders(token),
    })
    return data
  },
}

/**
 * The manage link keeps the token in the URL fragment: browsers never send fragments to servers or in
 * Referer headers, so the token stays out of access logs.
 */
export function manageBookingUrl(token, origin = window.location.origin) {
  return `${origin}/booking/manage#${encodeURIComponent(token)}`
}

/** Reads the token from a route hash such as `#abc`; empty when missing. */
export function tokenFromHash(hash) {
  if (typeof hash !== 'string' || hash.length < 2) {
    return ''
  }
  try {
    return decodeURIComponent(hash.slice(1)).trim()
  } catch {
    return ''
  }
}
