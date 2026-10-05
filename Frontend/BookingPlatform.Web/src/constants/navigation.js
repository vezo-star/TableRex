export const RouteNames = Object.freeze({
  SYSTEM_STATUS: 'system-status',
  NOT_FOUND: 'not-found',
})

export const dashboardNavigation = Object.freeze([
  { label: 'System status', icon: 'monitor_heart', to: { name: RouteNames.SYSTEM_STATUS } },
])
