import { ErrorCodes } from '@/constants/errorCodes.js'

/** Party sizes a guest may pick online, from the venue's min to max. */
export function partySizeOptions(venue) {
  if (!venue) {
    return []
  }
  const min = Math.max(1, venue.minPartySize ?? 1)
  const max = Math.max(min, venue.maxPartySize ?? min)
  return Array.from({ length: max - min + 1 }, (_, i) => min + i)
}

const blankToNull = (value) => {
  const trimmed = typeof value === 'string' ? value.trim() : value
  return trimmed === '' || trimmed === undefined ? null : trimmed
}

/** Request body for POST /public/{tenant}/{venue}/bookings. `website` is the honeypot. */
export function buildPublicBookingPayload({ date, time, partySize }, guest) {
  return {
    date,
    time,
    partySize,
    firstName: (guest.firstName ?? '').trim(),
    lastName: blankToNull(guest.lastName),
    email: (guest.email ?? '').trim(),
    phone: blankToNull(guest.phone),
    specialRequests: blankToNull(guest.specialRequests),
    marketingConsent: !!guest.marketingConsent,
    website: blankToNull(guest.website),
  }
}

const GUEST_MESSAGES = Object.freeze({
  [ErrorCodes.RESOURCE_NOT_AVAILABLE]: 'Sorry, that time was just taken. Please pick another time.',
  [ErrorCodes.PARTY_SIZE_NOT_SUPPORTED]:
    'There is no table for that party size at this time. Please try another time.',
  [ErrorCodes.OUTSIDE_OPERATING_HOURS]: 'The venue is not open at that time.',
  [ErrorCodes.OUTSIDE_BOOKING_WINDOW]: 'That time is too soon to book online.',
  [ErrorCodes.CONTACT_BOOKING_LIMIT]:
    'You already have the maximum number of upcoming bookings at this venue.',
  [ErrorCodes.RATE_LIMITED]: 'Too many attempts. Please wait a few minutes and try again.',
  [ErrorCodes.VENUE_NOT_FOUND]: 'This venue is not taking online bookings.',
  [ErrorCodes.BOOKING_NOT_FOUND]:
    'We could not find this booking. Check that you opened the full link from your confirmation.',
})

/** A guest-friendly message for an API error; falls back to the server's message. */
export function guestErrorMessage(error) {
  return GUEST_MESSAGES[error?.code] ?? error?.message ?? 'Something went wrong. Please try again.'
}

/** Errors that mean the chosen time is no longer bookable, so times should be reloaded. */
export function isSlotGone(error) {
  return [
    ErrorCodes.RESOURCE_NOT_AVAILABLE,
    ErrorCodes.PARTY_SIZE_NOT_SUPPORTED,
    ErrorCodes.OUTSIDE_BOOKING_WINDOW,
    ErrorCodes.OUTSIDE_OPERATING_HOURS,
  ].includes(error?.code)
}

/** e.g. "Tuesday 6 October 2026" for an ISO date. */
export function formatLongDate(iso) {
  if (!iso) {
    return ''
  }
  const [y, m, d] = iso.split('-').map(Number)
  return new Intl.DateTimeFormat('en-GB', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(Date.UTC(y, m - 1, d)))
}

/** An instant shown as "Tue 6 Oct, 18:00" in the venue's zone. */
export function formatInstant(instant, timeZone) {
  if (!instant) {
    return ''
  }
  const options = {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }
  try {
    return new Intl.DateTimeFormat('en-GB', { ...options, timeZone }).format(new Date(instant))
  } catch {
    return new Intl.DateTimeFormat('en-GB', options).format(new Date(instant))
  }
}

/** Human duration for whole minutes: "45 minutes", "2 hours", "1 day 2 hours". */
export function formatMinutes(total) {
  const minutes = Math.max(0, Math.round(total ?? 0))
  if (minutes === 0) {
    return 'until the booking starts'
  }
  const days = Math.floor(minutes / 1440)
  const hours = Math.floor((minutes % 1440) / 60)
  const mins = minutes % 60
  const part = (n, unit) => (n ? `${n} ${unit}${n === 1 ? '' : 's'}` : null)
  return [part(days, 'day'), part(hours, 'hour'), part(mins, 'minute')].filter(Boolean).join(' ')
}

export const BookingStatusText = Object.freeze({
  PENDING: 'Requested — the venue will confirm',
  HELD: 'Held',
  CONFIRMED: 'Confirmed',
  CHECKED_IN: 'Checked in',
  SEATED: 'Seated',
  COMPLETED: 'Completed',
  CANCELLED: 'Cancelled',
  NO_SHOW: 'No-show',
})
