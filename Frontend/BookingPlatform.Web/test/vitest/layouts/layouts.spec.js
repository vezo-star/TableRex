import { describe, expect, it } from 'vitest'
import { mountWithQuasar } from '../helpers/mountWithQuasar.js'
import AuthLayout from '@/layouts/AuthLayout.vue'
import DashboardLayout from '@/layouts/DashboardLayout.vue'
import PublicLayout from '@/layouts/PublicLayout.vue'

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

  it('DashboardLayout shows the product title and navigation toggle', async () => {
    const wrapper = await mountWithQuasar(DashboardLayout)

    expect(wrapper.text()).toContain('Booking Platform')
    expect(wrapper.find('[data-test="drawer-toggle"]').exists()).toBe(true)
  })
})
