import { Permissions } from '@/constants/permissions.js'

export const RouteNames = Object.freeze({
  LOGIN: 'login',
  SELECT_TENANT: 'select-tenant',
  FORGOT_PASSWORD: 'forgot-password',
  RESET_PASSWORD: 'reset-password',
  NO_ACCESS: 'no-access',
  FORBIDDEN: 'forbidden',
  DASHBOARD: 'dashboard',
  AVAILABILITY: 'availability',
  BOOKINGS: 'bookings',
  CUSTOMERS: 'customers',
  CUSTOMER_DETAILS: 'customer-details',
  AUDIT_LOG: 'audit-log',
  ACCOUNT: 'account',
  STAFF: 'staff',
  VENUES: 'venues',
  VENUE_SETTINGS: 'venue-settings',
  RESOURCES: 'resources',
  FLOOR_PLANS: 'floor-plans',
  SETTINGS: 'settings',
  PLATFORM_TENANTS: 'platform-tenants',
  SYSTEM_STATUS: 'system-status',
  PUBLIC_BOOKING: 'public-booking',
  PUBLIC_MANAGE_BOOKING: 'public-manage-booking',
  PAYMENT_RETURN: 'payment-return',
  SIMULATED_CHECKOUT: 'simulated-checkout',
  NOT_FOUND: 'not-found',
})

/** Drawer entries; each is shown only when the user holds `permission` (or any of `anyPermission`). */
export const dashboardNavigation = Object.freeze([
  { label: 'Dashboard', icon: 'dashboard', to: { name: RouteNames.DASHBOARD } },
  {
    label: 'Bookings',
    icon: 'event_note',
    to: { name: RouteNames.BOOKINGS },
    permission: Permissions.BOOKING_VIEW,
  },
  {
    label: 'Customers',
    icon: 'people',
    to: { name: RouteNames.CUSTOMERS },
    permission: Permissions.CUSTOMER_VIEW,
  },
  {
    label: 'Availability',
    icon: 'event_available',
    to: { name: RouteNames.AVAILABILITY },
    permission: Permissions.BOOKING_VIEW,
  },
  {
    label: 'Floor plans',
    icon: 'grid_view',
    to: { name: RouteNames.FLOOR_PLANS },
    permission: Permissions.FLOORPLAN_VIEW,
  },
  {
    label: 'Resources',
    icon: 'table_restaurant',
    to: { name: RouteNames.RESOURCES },
    permission: Permissions.RESOURCE_VIEW,
  },
  {
    label: 'Venues',
    icon: 'storefront',
    to: { name: RouteNames.VENUES },
    permission: Permissions.VENUE_VIEW,
  },
  {
    label: 'Staff',
    icon: 'badge',
    to: { name: RouteNames.STAFF },
    permission: Permissions.STAFF_VIEW,
  },
  {
    label: 'Settings',
    icon: 'settings',
    to: { name: RouteNames.SETTINGS },
    permission: Permissions.SETTINGS_MANAGE,
  },
  {
    label: 'Audit log',
    icon: 'history',
    to: { name: RouteNames.AUDIT_LOG },
    permission: Permissions.SETTINGS_MANAGE,
  },
  { label: 'System status', icon: 'monitor_heart', to: { name: RouteNames.SYSTEM_STATUS } },
])

export function visibleNavigation(items, auth) {
  return items.filter(
    (item) =>
      (!item.permission || auth.hasPermission(item.permission)) &&
      (!item.anyPermission || auth.hasAnyPermission(item.anyPermission)),
  )
}
