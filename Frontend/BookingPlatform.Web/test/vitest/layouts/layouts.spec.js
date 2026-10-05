import { describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { mountWithQuasar } from '../helpers/mountWithQuasar.js'
import AuthLayout from '@/layouts/AuthLayout.vue'
import DashboardLayout from '@/layouts/DashboardLayout.vue'
import PublicLayout from '@/layouts/PublicLayout.vue'
import { useAuthStore } from '@/stores/auth.js'

function signedIn(permissions = []) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore()
  auth.accessToken = 'token'
  auth.tenantId = 't1'
  auth.memberships = [
    { tenantId: 't1', tenantName: 'Alpha Bistro', tenantSlug: 'alpha', roles: ['MANAGER'] },
  ]
  auth.me = { displayName: 'Ada', permissions, roles: ['MANAGER'] }
  return pinia
}

describe('layouts', () => {
  it.each([
    ['AuthLayout', AuthLayout],
    ['DashboardLayout', DashboardLayout],
    ['PublicLayout', PublicLayout],
  ])('%s mounts and renders the routed page', async (_name, layout) => {
    const wrapper = await mountWithQuasar(layout)

    expect(wrapper.find('.q-layout').exists()).toBe(true)
    expect(wrapper.find('[data-test="routed-page"]').exists()).toBe(true)
  })

  it('DashboardLayout shows the product title, tenant and navigation toggle when signed in', async () => {
    const wrapper = await mountWithQuasar(DashboardLayout, { pinia: signedIn() })

    expect(wrapper.text()).toContain('Booking Platform')
    expect(wrapper.find('[data-test="drawer-toggle"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="tenant-name"]').text()).toBe('Alpha Bistro')
  })

  it('DashboardLayout hides navigation for anonymous visitors', async () => {
    const wrapper = await mountWithQuasar(DashboardLayout)

    expect(wrapper.find('[data-test="drawer-toggle"]').exists()).toBe(false)
  })
})
