import { http } from '@/services/http.js'

const id = (value) => encodeURIComponent(value)

/** Plans, limits, usage, branding (tenant + platform admin) and booking reports. */
export const saasService = {
  async listPlans() {
    const { data } = await http.get('/v1/plans')
    return data
  },

  async getSubscription() {
    const { data } = await http.get('/v1/tenants/current/subscription')
    return data
  },

  async getBranding() {
    const { data } = await http.get('/v1/tenants/current/branding')
    return data
  },

  async updateBranding(payload) {
    const { data } = await http.put('/v1/tenants/current/branding', payload)
    return data
  },

  async getTenantSubscription(tenantId) {
    const { data } = await http.get(`/v1/platform/tenants/${id(tenantId)}/subscription`)
    return data
  },

  async updateTenantSubscription(tenantId, payload) {
    const { data } = await http.put(`/v1/platform/tenants/${id(tenantId)}/subscription`, payload)
    return data
  },

  async bookingsSummary({ venueId, from, to }, { signal } = {}) {
    const { data } = await http.get('/v1/reports/bookings-summary', {
      params: { venueId, from, to },
      signal,
    })
    return data
  },

  /** Returns `{ blob, fileName }` for the bookings CSV. */
  async bookingsCsv({ venueId, from, to }) {
    const response = await http.get('/v1/reports/bookings.csv', {
      params: { venueId, from, to },
      responseType: 'blob',
    })
    return {
      blob: response.data,
      fileName:
        fileNameFrom(response.headers?.['content-disposition']) ?? `bookings-${from}-${to}.csv`,
    }
  },
}

/** Extracts the file name from a Content-Disposition header; null when absent. */
export function fileNameFrom(header) {
  if (typeof header !== 'string') {
    return null
  }
  const star = /filename\*=UTF-8''([^;]+)/i.exec(header)
  if (star) {
    try {
      return decodeURIComponent(star[1].trim())
    } catch {
      // Fall through to the plain parameter.
    }
  }
  const plain = /filename="?([^";]+)"?/i.exec(header)
  return plain ? plain[1].trim() : null
}
