import { http } from '@/services/http.js'

// Auth endpoints handle their own 401s; they must never trigger the refresh-and-retry loop.
const noRefresh = { skipAuthRefresh: true }

export const authService = {
  async login(email, password) {
    const { data } = await http.post(
      '/v1/auth/login',
      { email, password },
      { ...noRefresh, skipAuth: true },
    )
    return data
  },

  async refresh() {
    const { data } = await http.post('/v1/auth/refresh', null, { ...noRefresh, skipAuth: true })
    return data
  },

  async logout() {
    await http.post('/v1/auth/logout', null, noRefresh)
  },

  async me() {
    const { data } = await http.get('/v1/auth/me')
    return data
  },

  async switchTenant(tenantId) {
    const { data } = await http.post('/v1/auth/switch-tenant', { tenantId })
    return data
  },

  async changePassword(currentPassword, newPassword) {
    const { data } = await http.post('/v1/auth/change-password', { currentPassword, newPassword })
    return data
  },

  /** Always resolves for a well-formed address: the server never reveals whether an account exists. */
  async forgotPassword(email) {
    await http.post('/v1/auth/password/forgot', { email }, { ...noRefresh, skipAuth: true })
  },

  async resetPassword(token, newPassword) {
    await http.post(
      '/v1/auth/password/reset',
      { token, newPassword },
      { ...noRefresh, skipAuth: true },
    )
  },
}
