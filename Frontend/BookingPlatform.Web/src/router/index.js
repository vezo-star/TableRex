import { defineRouter } from '#q-app'
import {
  createMemoryHistory,
  createRouter,
  createWebHashHistory,
  createWebHistory,
} from 'vue-router'

import routes from './routes.js'

export default defineRouter(() => {
  const createHistory = import.meta.env.QUASAR_SERVER
    ? createMemoryHistory
    : import.meta.env.QUASAR_VUE_ROUTER_MODE === 'history'
      ? createWebHistory
      : createWebHashHistory

  return createRouter({
    scrollBehavior: () => ({ left: 0, top: 0 }),
    routes,
    // Configure via quasar.config.js > build > vueRouterMode / publicPath.
    history: createHistory(import.meta.env.QUASAR_VUE_ROUTER_BASE),
  })
})
