import { defineRouter } from '#q-app'
import {
  createMemoryHistory,
  createRouter,
  createWebHashHistory,
  createWebHistory,
} from 'vue-router'

import routes from './routes.js'
import { resolveNavigation } from './guards.js'
import { useAuthStore } from '@/stores/auth.js'

export function installGuards(router, store) {
  router.beforeEach(async (to) => {
    const auth = useAuthStore(store)
    await auth.initialize()
    return resolveNavigation(to, auth)
  })
}

export default defineRouter(({ store }) => {
  const createHistory = import.meta.env.QUASAR_SERVER
    ? createMemoryHistory
    : import.meta.env.QUASAR_VUE_ROUTER_MODE === 'history'
      ? createWebHistory
      : createWebHashHistory

  const router = createRouter({
    scrollBehavior: () => ({ left: 0, top: 0 }),
    routes,
    // Configure via quasar.config.js > build > vueRouterMode / publicPath.
    history: createHistory(import.meta.env.QUASAR_VUE_ROUTER_BASE),
  })

  installGuards(router, store)
  return router
})
