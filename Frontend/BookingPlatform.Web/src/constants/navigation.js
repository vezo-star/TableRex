import { Permissions } from '@/constants/permissions.js'

export const RouteNames = Object.freeze({
  LOGIN: 'login',
  SELECT_TENANT: 'select-tenant',
  NO_ACCESS: 'no-access',
  FORBIDDEN: 'forbidden',
  DASHBOARD: 'dashboard',
  ACCOUNT: 'account',
  STAFF: 'staff',
  SETTINGS: 'settings',
  PLATFORM_TENANTS: 'platform-tenants',
  SYSTEM_STATUS: 'system-status',
  NOT_FOUND: 'not-found',
})

/** Drawer entries; each is shown only when the user holds `permission` (or any of `anyPermission`). */
export const dashboardNavigation = Object.freeze([
  { label: 'Dashboard', icon: 'dashboard', to: { name: RouteNames.DASHBOARD } },
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
  { label: 'System status', icon: 'monitor_heart', to: { name: RouteNames.SYSTEM_STATUS } },
])

export function visibleNavigation(items, auth) {
  return items.filter(
    (item) =>
      (!item.permission || auth.hasPermission(item.permission)) &&
      (!item.anyPermission || auth.hasAnyPermission(item.anyPermission)),
  )
}
