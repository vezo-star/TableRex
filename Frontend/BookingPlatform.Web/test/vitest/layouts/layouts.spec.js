import { describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { mountWithQuasar } from '../helpers/mountWithQuasar.js'
import AuthLayout from '@/layouts/AuthLayout.vue'
import DashboardLayout from '@/layouts/DashboardLayout.vue'
import PublicLayout from '@/layouts/PublicLayout.vue'
import { useAuthStore } from '@/stores/auth.js'
import { useRealtimeStore } from '@/stores/realtime.js'

vi.mock('@/services/realtimeService.js', () => ({
  createVenueConnection: vi.fn(() => ({
    on: vi.fn(),
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
    onclose: vi.fn(),
    start: vi.fn().mockResolvedValue(),
    stop: vi.fn().mockResolvedValue(),
    invoke: vi.fn().mockResolvedValue(),
  })),
}))
const { createVenueConnection } = await import('@/services/realtimeService.js')

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

  it('DashboardLayout starts live updates for a tenant session and shows their state', async () => {
    const pinia = signedIn()
    const wrapper = await mountWithQuasar(DashboardLayout, { pinia })
    await flushPromises()

    expect(createVenueConnection).toHaveBeenCalled()
    const indicator = wrapper.find('[data-test="realtime-indicator"]')
    expect(indicator.attributes('data-status')).toBe('connected')
    expect(indicator.text()).toContain('Live')

    useRealtimeStore(pinia).status = 'reconnecting'
    await flushPromises()
    expect(wrapper.find('[data-test="realtime-indicator"]').text()).toContain('Reconnecting')

    wrapper.unmount()
    expect(useRealtimeStore(pinia).status).toBe('disconnected')
  })

  it('DashboardLayout hides navigation for anonymous visitors', async () => {
    createVenueConnection.mockClear()
    const wrapper = await mountWithQuasar(DashboardLayout)

    expect(wrapper.find('[data-test="drawer-toggle"]').exists()).toBe(false)
    expect(createVenueConnection).not.toHaveBeenCalled()
  })
})
