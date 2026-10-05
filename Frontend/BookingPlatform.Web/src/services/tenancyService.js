import { http } from '@/services/http.js'

export const tenancyService = {
  async getCurrentTenant() {
    const { data } = await http.get('/v1/tenants/current')
    return data
  },

  async updateCurrentTenant(payload) {
    const { data } = await http.put('/v1/tenants/current', payload)
    return data
  },

  async listStaff({ status, q, page = 1, pageSize = 25 } = {}, { signal } = {}) {
    const { data } = await http.get('/v1/staff', { params: { status, q, page, pageSize }, signal })
    return data
  },

  async createStaff(payload) {
    const { data } = await http.post('/v1/staff', payload)
    return data
  },

  async updateStaffRoles(tenantUserId, roles) {
    const { data } = await http.put(`/v1/staff/${encodeURIComponent(tenantUserId)}/roles`, {
      roles,
    })
    return data
  },

  async deactivateStaff(tenantUserId) {
    const { data } = await http.post(`/v1/staff/${encodeURIComponent(tenantUserId)}/deactivate`)
    return data
  },

  async activateStaff(tenantUserId) {
    const { data } = await http.post(`/v1/staff/${encodeURIComponent(tenantUserId)}/activate`)
    return data
  },

  async listRoles() {
    const { data } = await http.get('/v1/roles')
    return data
  },

  async listPlatformTenants({ page = 1, pageSize = 25 } = {}) {
    const { data } = await http.get('/v1/platform/tenants', { params: { page, pageSize } })
    return data
  },

  async createPlatformTenant(payload) {
    const { data } = await http.post('/v1/platform/tenants', payload)
    return data
  },
}
