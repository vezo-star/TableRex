import { HttpHeaders } from '@/constants/httpHeaders.js'
import { http } from '@/services/http.js'

const id = (value) => encodeURIComponent(value)

function compact(params) {
  return Object.fromEntries(
    Object.entries(params).filter(
      ([, value]) =>
        value !== null &&
        value !== undefined &&
        value !== '' &&
        !(Array.isArray(value) && value.length === 0),
    ),
  )
}

/** Status command endpoints keyed by the action name used in the UI. */
export const BookingCommands = Object.freeze({
  confirm: 'confirm',
  checkIn: 'check-in',
  seat: 'seat',
  complete: 'complete',
  noShow: 'no-show',
})

export const bookingService = {
  /** One venue-local business date for the board: periods, all bookings and active blocks. */
  async board(venueId, date, { signal } = {}) {
    const { data } = await http.get('/v1/bookings/board', { params: { venueId, date }, signal })
    return data
  },

  async list(query = {}, { signal } = {}) {
    const { status, ...rest } = query
    const params = compact({ ...rest, status: Array.isArray(status) ? status.join(',') : status })
    const { data } = await http.get('/v1/bookings', { params, signal })
    return data
  },

  async get(bookingId) {
    const { data } = await http.get(`/v1/bookings/${id(bookingId)}`)
    return data
  },

  async history(bookingId) {
    const { data } = await http.get(`/v1/bookings/${id(bookingId)}/history`)
    return data
  },

  /** Returns `{ booking, replayed }`; `idempotencyKey` must be reused when retrying the same submission. */
  async create(payload, idempotencyKey) {
    const response = await http.post('/v1/bookings', payload, {
      headers: { [HttpHeaders.IDEMPOTENCY_KEY]: idempotencyKey },
    })
    return {
      booking: response.data,
      replayed: response.headers?.['idempotent-replayed'] === 'true',
    }
  },

  async update(bookingId, payload) {
    const { data } = await http.put(`/v1/bookings/${id(bookingId)}`, payload)
    return data
  },

  async move(bookingId, payload) {
    const { data } = await http.post(`/v1/bookings/${id(bookingId)}/move`, payload)
    return data
  },

  async command(bookingId, command, version) {
    const path = BookingCommands[command]
    if (!path) {
      throw new Error(`Unknown booking command: ${command}`)
    }
    const { data } = await http.post(`/v1/bookings/${id(bookingId)}/${path}`, { version })
    return data
  },

  async cancel(bookingId, { reason, version } = {}) {
    const { data } = await http.post(`/v1/bookings/${id(bookingId)}/cancel`, {
      reason: reason || null,
      version,
    })
    return data
  },
}

export const holdService = {
  /** The token is only present in this first response; keep it to convert the hold into a booking. */
  async create(payload, idempotencyKey) {
    const { data } = await http.post('/v1/holds', payload, {
      headers: { [HttpHeaders.IDEMPOTENCY_KEY]: idempotencyKey },
    })
    return data
  },

  async release(holdId) {
    await http.delete(`/v1/holds/${id(holdId)}`)
  },
}

export const blockService = {
  async list(venueId, { from, to, includeCancelled } = {}) {
    const { data } = await http.get('/v1/resource-blocks', {
      params: compact({ venueId, from, to, includeCancelled }),
    })
    return data
  },

  async create(payload) {
    const { data } = await http.post('/v1/resource-blocks', payload)
    return data
  },

  async cancel(blockId) {
    const { data } = await http.post(`/v1/resource-blocks/${id(blockId)}/cancel`)
    return data
  },
}
