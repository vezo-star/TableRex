import { Permissions } from '@/constants/permissions.js'
import { formatTime } from '@/utils/dates.js'

/** Mirrors BookingPlatform.Domain.Bookings.BookingStatus. */
export const BookingStatus = Object.freeze({
  PENDING: 'PENDING',
  HELD: 'HELD',
  CONFIRMED: 'CONFIRMED',
  CHECKED_IN: 'CHECKED_IN',
  SEATED: 'SEATED',
  COMPLETED: 'COMPLETED',
  CANCELLED: 'CANCELLED',
  NO_SHOW: 'NO_SHOW',
})

export const ACTIVE_STATUSES = Object.freeze([
  BookingStatus.PENDING,
  BookingStatus.HELD,
  BookingStatus.CONFIRMED,
  BookingStatus.CHECKED_IN,
  BookingStatus.SEATED,
])

/** Chip colours keep the label at WCAG AA contrast (4.5:1) against `textColor` (white unless set). */
export const StatusMeta = Object.freeze({
  PENDING: {
    label: 'Pending',
    color: 'amber-8',
    textColor: 'black',
    fill: '#fff8e1',
    icon: 'hourglass_empty',
  },
  HELD: { label: 'Held', color: 'deep-orange-10', fill: '#fbe9e7', icon: 'pause_circle' },
  CONFIRMED: { label: 'Confirmed', color: 'primary', fill: '#e3f2fd', icon: 'event_available' },
  CHECKED_IN: { label: 'Checked in', color: 'teal-8', fill: '#e0f2f1', icon: 'how_to_reg' },
  SEATED: { label: 'Seated', color: 'positive', fill: '#e8f5e9', icon: 'event_seat' },
  COMPLETED: { label: 'Completed', color: 'blue-grey-7', fill: '#f5f5f5', icon: 'task_alt' },
  CANCELLED: { label: 'Cancelled', color: 'grey-7', fill: '#fafafa', icon: 'cancel' },
  NO_SHOW: { label: 'No-show', color: 'negative', fill: '#ffebee', icon: 'person_off' },
})

export const SourceLabels = Object.freeze({
  STAFF: 'Staff',
  PHONE: 'Phone',
  WALK_IN: 'Walk-in',
  WEBSITE: 'Website',
  WIDGET: 'Widget',
  API: 'API',
})

export const BlockReasonLabels = Object.freeze({
  MAINTENANCE: 'Maintenance',
  PRIVATE_EVENT: 'Private event',
  CLEANING: 'Cleaning',
  MANAGEMENT: 'Management',
  UNAVAILABLE: 'Unavailable',
  STAFF_USE: 'Staff use',
})

export function statusMeta(status) {
  return StatusMeta[status] ?? { label: status, color: 'grey-8', fill: '#ffffff', icon: 'help' }
}

export function isActiveStatus(status) {
  return ACTIVE_STATUSES.includes(status)
}

/**
 * Status actions in display order. `from` mirrors BookingStatusMachine; `afterStart` mirrors the no-show rule;
 * `confirm` asks for confirmation in the UI.
 */
export const StatusActions = Object.freeze([
  {
    key: 'confirm',
    label: 'Confirm',
    icon: 'check',
    from: [BookingStatus.PENDING, BookingStatus.HELD],
    permission: Permissions.BOOKING_UPDATE,
  },
  {
    key: 'checkIn',
    label: 'Check in',
    icon: 'how_to_reg',
    from: [BookingStatus.CONFIRMED],
    permission: Permissions.BOOKING_CHECKIN,
  },
  {
    key: 'seat',
    label: 'Seat',
    icon: 'event_seat',
    from: [BookingStatus.CONFIRMED, BookingStatus.CHECKED_IN],
    permission: Permissions.BOOKING_SEAT,
  },
  {
    key: 'complete',
    label: 'Complete',
    icon: 'task_alt',
    from: [BookingStatus.SEATED],
    permission: Permissions.BOOKING_SEAT,
  },
  {
    key: 'noShow',
    label: 'No-show',
    icon: 'person_off',
    from: [BookingStatus.CONFIRMED],
    permission: Permissions.BOOKING_UPDATE,
    afterStart: true,
    confirm: 'Mark this booking as a no-show? The table is released.',
  },
  {
    key: 'cancel',
    label: 'Cancel',
    icon: 'cancel',
    from: [
      BookingStatus.PENDING,
      BookingStatus.HELD,
      BookingStatus.CONFIRMED,
      BookingStatus.CHECKED_IN,
    ],
    permission: Permissions.BOOKING_CANCEL,
    confirm: 'Cancel this booking? The table is released immediately.',
  },
])

/** Actions the user may apply to `booking` now. `can(permission)` comes from the auth store. */
export function availableActions(booking, can, now = Date.now()) {
  if (!booking) {
    return []
  }
  return StatusActions.filter(
    (action) =>
      action.from.includes(booking.status) &&
      can(action.permission) &&
      (!action.afterStart || new Date(booking.startsAt).getTime() <= now),
  )
}

export function canEdit(booking, can) {
  return !!booking && isActiveStatus(booking.status) && can(Permissions.BOOKING_UPDATE)
}

export function canMove(booking, can) {
  return !!booking && isActiveStatus(booking.status) && can(Permissions.BOOKING_MOVE)
}

export function customerName(customer) {
  if (!customer) {
    return 'Walk-in'
  }
  return [customer.firstName, customer.lastName].filter(Boolean).join(' ')
}

export function resourceNames(booking) {
  return (booking?.resources ?? []).map((r) => r.name).join(' + ')
}

/** Today's numbers for the dashboard. Cancelled and no-show bookings are counted separately. */
export function summariseDay(bookings = [], now = Date.now()) {
  const active = bookings.filter((b) => isActiveStatus(b.status) || b.status === 'COMPLETED')
  const byStatus = Object.fromEntries(Object.keys(StatusMeta).map((s) => [s, 0]))
  for (const b of bookings) {
    byStatus[b.status] = (byStatus[b.status] ?? 0) + 1
  }
  const upcoming = bookings
    .filter(
      (b) =>
        [BookingStatus.PENDING, BookingStatus.HELD, BookingStatus.CONFIRMED].includes(b.status) &&
        new Date(b.endsAt).getTime() > now,
    )
    .sort((a, b) => new Date(a.startsAt) - new Date(b.startsAt))
  return {
    bookings: active.length,
    covers: active.reduce((sum, b) => sum + b.partySize, 0),
    seatedCovers: bookings
      .filter((b) => b.status === BookingStatus.SEATED)
      .reduce((sum, b) => sum + b.partySize, 0),
    byStatus,
    upcoming,
    late: upcoming.filter((b) => new Date(b.startsAt).getTime() < now),
  }
}

/** Active resources ordered by section then sort order, as `{ section, resources }` groups. */
export function groupResources(resources = [], sections = []) {
  const sectionById = new Map(sections.map((s) => [s.id, s]))
  const groups = new Map()
  const sorted = [...resources]
    .filter((r) => r.isActive)
    .sort((a, b) => {
      const sa = sectionById.get(a.sectionId)?.sortOrder ?? Number.MAX_SAFE_INTEGER
      const sb = sectionById.get(b.sectionId)?.sortOrder ?? Number.MAX_SAFE_INTEGER
      return sa - sb || a.sortOrder - b.sortOrder || a.name.localeCompare(b.name)
    })
  for (const resource of sorted) {
    const key = resource.sectionId ?? 'none'
    if (!groups.has(key)) {
      groups.set(key, {
        id: key,
        name: sectionById.get(resource.sectionId)?.name ?? 'Other',
        resources: [],
      })
    }
    groups.get(key).resources.push(resource)
  }
  return [...groups.values()]
}

const MINUTE = 60_000

/**
 * The visible time window of the board: the operating periods, widened to include every booking and block,
 * rounded out to whole hours. Returns null for a closed day with nothing on it.
 */
export function boardWindow(board) {
  const starts = [
    ...(board?.periods ?? []).map((p) => p.startsAt),
    ...(board?.bookings ?? []).filter((b) => isActiveStatus(b.status)).map((b) => b.startsAt),
  ].map((v) => new Date(v).getTime())
  const ends = [
    ...(board?.periods ?? []).map((p) => p.endsAt),
    ...(board?.bookings ?? []).filter((b) => isActiveStatus(b.status)).map((b) => b.endsAt),
  ].map((v) => new Date(v).getTime())
  if (starts.length === 0) {
    return null
  }
  const hour = 60 * MINUTE
  const start = Math.floor(Math.min(...starts) / hour) * hour
  const end = Math.ceil(Math.max(...ends) / hour) * hour
  return { start, end, minutes: (end - start) / MINUTE }
}

/** Horizontal placement of an interval inside the window, in pixels. */
export function placeInterval(startsAt, endsAt, range, pxPerMinute) {
  const s = Math.max(new Date(startsAt).getTime(), range.start)
  const e = Math.min(new Date(endsAt).getTime(), range.end)
  return {
    left: ((s - range.start) / MINUTE) * pxPerMinute,
    width: Math.max(((e - s) / MINUTE) * pxPerMinute, 4),
  }
}

/** Hour ticks for the timeline header, labelled in the venue time zone. */
export function hourTicks(range, pxPerMinute, timeZone) {
  const ticks = []
  for (let t = range.start; t <= range.end; t += 60 * MINUTE) {
    ticks.push({
      at: t,
      left: ((t - range.start) / MINUTE) * pxPerMinute,
      label: formatTime(new Date(t).toISOString(), timeZone),
    })
  }
  return ticks
}

/** Snaps a pixel offset to the slot grid and returns the new start instant (ms). */
export function snapStart(originalStartMs, deltaPx, pxPerMinute, slotMinutes) {
  const deltaMinutes = deltaPx / pxPerMinute
  const snapped = Math.round(deltaMinutes / slotMinutes) * slotMinutes
  return originalStartMs + snapped * MINUTE
}

/** Bookings, holds and blocks are drawn per resource; a booking on several resources appears on each row. */
export function itemsByResource(board, { showInactive = false } = {}) {
  const rows = new Map()
  const add = (resourceId, item) => {
    if (!rows.has(resourceId)) {
      rows.set(resourceId, [])
    }
    rows.get(resourceId).push(item)
  }
  for (const booking of board?.bookings ?? []) {
    if (!showInactive && !isActiveStatus(booking.status) && booking.status !== 'COMPLETED') {
      continue
    }
    for (const resource of booking.resources) {
      add(resource.resourceId, { kind: 'booking', booking })
    }
  }
  for (const block of board?.blocks ?? []) {
    for (const resourceId of block.resourceIds) {
      add(resourceId, { kind: 'block', block })
    }
  }
  return rows
}

/** Which booking occupies each resource at instant `at` (ms), for the floor view. */
export function occupancyAt(board, at) {
  const result = new Map()
  for (const booking of board?.bookings ?? []) {
    if (!isActiveStatus(booking.status)) {
      continue
    }
    const s = new Date(booking.startsAt).getTime()
    const e = new Date(booking.endsAt).getTime()
    if (s <= at && at < e) {
      for (const r of booking.resources) {
        result.set(r.resourceId, { kind: 'booking', booking })
      }
    }
  }
  for (const block of board?.blocks ?? []) {
    const s = new Date(block.startsAt).getTime()
    const e = new Date(block.endsAt).getTime()
    if (s <= at && at < e) {
      for (const id of block.resourceIds) {
        if (!result.has(id)) {
          result.set(id, { kind: 'block', block })
        }
      }
    }
  }
  return result
}

/** Venue-local HH:mm of an instant, for move requests. */
export function localTime(instantMs, timeZone) {
  return formatTime(new Date(instantMs).toISOString(), timeZone)
}
