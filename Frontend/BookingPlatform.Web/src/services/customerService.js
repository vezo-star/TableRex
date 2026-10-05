import { http } from '@/services/http.js'

const id = (value) => encodeURIComponent(value)

export const customerService = {
  async list({ q, page = 1, pageSize = 25 } = {}, { signal } = {}) {
    const { data } = await http.get('/v1/customers', {
      params: { q: q || undefined, page, pageSize },
      signal,
    })
    return data
  },

  async get(customerId) {
    const { data } = await http.get(`/v1/customers/${id(customerId)}`)
    return data
  },

  async create(payload) {
    const { data } = await http.post('/v1/customers', payload)
    return data
  },

  async update(customerId, payload) {
    const { data } = await http.put(`/v1/customers/${id(customerId)}`, payload)
    return data
  },

  async bookings(customerId, { page = 1, pageSize = 25 } = {}) {
    const { data } = await http.get(`/v1/customers/${id(customerId)}/bookings`, {
      params: { page, pageSize },
    })
    return data
  },

  async anonymize(customerId) {
    const { data } = await http.post(`/v1/customers/${id(customerId)}/anonymize`)
    return data
  },
}

export const auditService = {
  async list({ entityType, entityId, action, before, limit = 50 } = {}) {
    const params = Object.fromEntries(
      Object.entries({ entityType, entityId, action, before, limit }).filter(
        ([, value]) => value !== null && value !== undefined && value !== '',
      ),
    )
    const { data } = await http.get('/v1/audit-logs', { params })
    return data
  },
}
