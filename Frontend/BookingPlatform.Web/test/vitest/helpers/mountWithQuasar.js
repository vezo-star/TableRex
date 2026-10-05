import { h } from 'vue'
import { mount } from '@vue/test-utils'
import { Quasar } from 'quasar'
import { createMemoryHistory, createRouter } from 'vue-router'

const Blank = { render: () => h('div', { 'data-test': 'routed-page' }, 'routed page') }

export function createTestRouter(
  routes = [{ path: '/', name: 'system-status', component: Blank }],
) {
  return createRouter({ history: createMemoryHistory(), routes })
}

export async function mountWithQuasar(component, { router = createTestRouter(), ...options } = {}) {
  router.push('/')
  await router.isReady()
  return mount(component, {
    ...options,
    global: { plugins: [[Quasar, {}], router], ...options.global },
  })
}
