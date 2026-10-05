/** SignalR server → client method names (REALTIME.md §3); mirror `RealtimeEvents` in the Api. */
export const RealtimeEvents = Object.freeze({
  BOOKING_CREATED: 'BookingCreated',
  BOOKING_UPDATED: 'BookingUpdated',
  BOOKING_CANCELLED: 'BookingCancelled',
  BOOKING_MOVED: 'BookingMoved',
  BOOKING_STATUS_CHANGED: 'BookingStatusChanged',
  RESOURCE_UPDATED: 'ResourceUpdated',
  RESOURCE_BLOCKED: 'ResourceBlocked',
  HOLD_CREATED: 'HoldCreated',
  HOLD_EXPIRED: 'HoldExpired',
  FLOOR_PLAN_UPDATED: 'FloorPlanUpdated',
})

export const REALTIME_EVENT_NAMES = Object.freeze(Object.values(RealtimeEvents))

export const BOOKING_EVENTS = Object.freeze([
  RealtimeEvents.BOOKING_CREATED,
  RealtimeEvents.BOOKING_UPDATED,
  RealtimeEvents.BOOKING_CANCELLED,
  RealtimeEvents.BOOKING_MOVED,
  RealtimeEvents.BOOKING_STATUS_CHANGED,
])

export const RealtimeStatus = Object.freeze({
  DISCONNECTED: 'disconnected',
  CONNECTING: 'connecting',
  CONNECTED: 'connected',
  RECONNECTING: 'reconnecting',
})

export const VENUE_HUB_PATH = '/hubs/venue'
