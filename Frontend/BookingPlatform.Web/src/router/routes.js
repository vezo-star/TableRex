import { RouteNames } from '@/constants/navigation.js'

// AuthLayout (Phase 2) and PublicLayout (Phase 8) are not routed yet.
const routes = [
  {
    path: '/',
    component: () => import('@/layouts/DashboardLayout.vue'),
    children: [
      {
        path: '',
        name: RouteNames.SYSTEM_STATUS,
        component: () => import('@/pages/SystemStatusPage.vue'),
      },
    ],
  },

  {
    path: '/:catchAll(.*)*',
    name: RouteNames.NOT_FOUND,
    component: () => import('@/pages/ErrorNotFound.vue'),
  },
]

export default routes
