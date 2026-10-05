import { h } from 'vue'
import { mount } from '@vue/test-utils'
import { Quasar, Notify, Dialog } from 'quasar'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'

const Blank = { render: () => h('div', { 'data-test': 'routed-page' }, 'routed page') }

export const BlankComponent = Blank

export function createTestRouter(
  routes = [{ path: '/', name: 'system-status', component: Blank }],
) {
  return createRouter({ history: createMemoryHistory(), routes })
}

export async function mountWithQuasar(
  component,
  { router = createTestRouter(), pinia = createPinia(), initialPath = '/', ...options } = {},
) {
  setActivePinia(pinia)
  router.push(initialPath)
  await router.isReady()
  return mount(component, {
    ...options,
    global: {
      plugins: [[Quasar, { plugins: { Notify, Dialog } }], router, pinia],
      ...options.global,
    },
  })
}
