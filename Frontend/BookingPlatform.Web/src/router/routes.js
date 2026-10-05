import { RouteNames } from '@/constants/navigation.js'
import { Permissions } from '@/constants/permissions.js'

const routes = [
  {
    path: '/',
    component: () => import('@/layouts/DashboardLayout.vue'),
    meta: { requiresTenant: true },
    children: [
      {
        path: '',
        name: RouteNames.DASHBOARD,
        component: () => import('@/pages/DashboardPage.vue'),
      },
      {
        path: 'availability',
        name: RouteNames.AVAILABILITY,
        component: () => import('@/pages/availability/AvailabilityPage.vue'),
        meta: { permission: Permissions.BOOKING_VIEW },
      },
      {
        path: 'bookings',
        name: RouteNames.BOOKINGS,
        component: () => import('@/pages/bookings/BookingBoardPage.vue'),
        meta: { permission: Permissions.BOOKING_VIEW },
      },
      {
        path: 'customers',
        name: RouteNames.CUSTOMERS,
        component: () => import('@/pages/customers/CustomersPage.vue'),
        meta: { permission: Permissions.CUSTOMER_VIEW },
      },
      {
        path: 'customers/:customerId',
        name: RouteNames.CUSTOMER_DETAILS,
        component: () => import('@/pages/customers/CustomerDetailsPage.vue'),
        meta: { permission: Permissions.CUSTOMER_VIEW },
        props: true,
      },
      {
        path: 'audit-log',
        name: RouteNames.AUDIT_LOG,
        component: () => import('@/pages/settings/AuditLogPage.vue'),
        meta: { permission: Permissions.SETTINGS_MANAGE },
      },
      {
        path: 'staff',
        name: RouteNames.STAFF,
        component: () => import('@/pages/staff/StaffPage.vue'),
        meta: { permission: Permissions.STAFF_VIEW },
      },
      {
        path: 'venues',
        name: RouteNames.VENUES,
        component: () => import('@/pages/venues/VenuesPage.vue'),
        meta: { permission: Permissions.VENUE_VIEW },
      },
      {
        path: 'venues/:venueId',
        name: RouteNames.VENUE_SETTINGS,
        component: () => import('@/pages/venues/VenueSettingsPage.vue'),
        meta: { permission: Permissions.VENUE_VIEW },
        props: true,
      },
      {
        path: 'resources',
        name: RouteNames.RESOURCES,
        component: () => import('@/pages/resources/ResourcesPage.vue'),
        meta: { permission: Permissions.RESOURCE_VIEW },
      },
      {
        path: 'floor-plans',
        name: RouteNames.FLOOR_PLANS,
        component: () => import('@/pages/floorplans/FloorPlanPage.vue'),
        meta: { permission: Permissions.FLOORPLAN_VIEW },
      },
      {
        path: 'settings',
        name: RouteNames.SETTINGS,
        component: () => import('@/pages/settings/TenantSettingsPage.vue'),
        meta: { permission: Permissions.SETTINGS_MANAGE },
      },
      {
        path: 'forbidden',
        name: RouteNames.FORBIDDEN,
        component: () => import('@/pages/ForbiddenPage.vue'),
      },
    ],
  },

  {
    path: '/',
    component: () => import('@/layouts/DashboardLayout.vue'),
    meta: { requiresAuth: true },
    children: [
      {
        path: 'account',
        name: RouteNames.ACCOUNT,
        component: () => import('@/pages/AccountPage.vue'),
      },
      {
        path: 'platform/tenants',
        name: RouteNames.PLATFORM_TENANTS,
        component: () => import('@/pages/platform/PlatformTenantsPage.vue'),
        meta: { platformAdmin: true },
      },
    ],
  },

  {
    path: '/',
    component: () => import('@/layouts/DashboardLayout.vue'),
    meta: { public: true },
    children: [
      {
        path: 'system-status',
        name: RouteNames.SYSTEM_STATUS,
        component: () => import('@/pages/SystemStatusPage.vue'),
      },
    ],
  },

  {
    path: '/',
    component: () => import('@/layouts/AuthLayout.vue'),
    children: [
      {
        path: 'login',
        name: RouteNames.LOGIN,
        component: () => import('@/pages/auth/LoginPage.vue'),
        meta: { guestOnly: true },
      },
      {
        path: 'select-tenant',
        name: RouteNames.SELECT_TENANT,
        component: () => import('@/pages/auth/SelectTenantPage.vue'),
        meta: { requiresAuth: true },
      },
      {
        path: 'no-access',
        name: RouteNames.NO_ACCESS,
        component: () => import('@/pages/auth/NoAccessPage.vue'),
        meta: { requiresAuth: true },
      },
    ],
  },

  {
    path: '/:catchAll(.*)*',
    name: RouteNames.NOT_FOUND,
    component: () => import('@/pages/ErrorNotFound.vue'),
    meta: { public: true },
  },
]

export default routes
