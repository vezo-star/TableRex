import { HttpHeaders } from '@/constants/httpHeaders.js'
import { http } from '@/services/http.js'

const id = (value) => encodeURIComponent(value)

export const paymentService = {
  async listForBooking(bookingId) {
    const { data } = await http.get(`/v1/bookings/${id(bookingId)}/payments`)
    return data
  },

  /** Refunds `amountMinor` (or everything still refundable when null). Reuse `idempotencyKey` when retrying. */
  async refund(bookingId, paymentId, { amountMinor = null, reason = null } = {}, idempotencyKey) {
    const { data } = await http.post(
      `/v1/bookings/${id(bookingId)}/payments/${id(paymentId)}/refunds`,
      { amountMinor, reason: reason || null },
      { headers: { [HttpHeaders.IDEMPOTENCY_KEY]: idempotencyKey } },
    )
    return data
  },

  async getSettings(venueId) {
    const { data } = await http.get(`/v1/venues/${id(venueId)}/payment-settings`)
    return data
  },

  async updateSettings(venueId, payload) {
    const { data } = await http.put(`/v1/venues/${id(venueId)}/payment-settings`, payload)
    return data
  },
}
