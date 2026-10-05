// Mirrors BookingPlatform.Application.Common.Security.Permissions / Roles (seeded by V027).
export const Permissions = Object.freeze({
  BOOKING_VIEW: 'booking.view',
  BOOKING_CREATE: 'booking.create',
  BOOKING_UPDATE: 'booking.update',
  BOOKING_CANCEL: 'booking.cancel',
  BOOKING_MOVE: 'booking.move',
  BOOKING_CHECKIN: 'booking.checkin',
  BOOKING_SEAT: 'booking.seat',
  RESOURCE_VIEW: 'resource.view',
  RESOURCE_CREATE: 'resource.create',
  RESOURCE_UPDATE: 'resource.update',
  RESOURCE_DELETE: 'resource.delete',
  VENUE_VIEW: 'venue.view',
  VENUE_MANAGE: 'venue.manage',
  FLOORPLAN_VIEW: 'floorplan.view',
  FLOORPLAN_MANAGE: 'floorplan.manage',
  CUSTOMER_VIEW: 'customer.view',
  CUSTOMER_MANAGE: 'customer.manage',
  STAFF_VIEW: 'staff.view',
  STAFF_MANAGE: 'staff.manage',
  REPORTS_VIEW: 'reports.view',
  SETTINGS_MANAGE: 'settings.manage',
})

export const Roles = Object.freeze({
  TENANT_OWNER: 'TENANT_OWNER',
  TENANT_ADMIN: 'TENANT_ADMIN',
  MANAGER: 'MANAGER',
  HOST: 'HOST',
  STAFF: 'STAFF',
  VIEW_ONLY: 'VIEW_ONLY',
})

export const RoleLabels = Object.freeze({
  TENANT_OWNER: 'Owner',
  TENANT_ADMIN: 'Admin',
  MANAGER: 'Manager',
  HOST: 'Host',
  STAFF: 'Staff',
  VIEW_ONLY: 'View only',
})
