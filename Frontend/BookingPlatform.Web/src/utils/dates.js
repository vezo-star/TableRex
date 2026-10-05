/** Today's date (YYYY-MM-DD) in an IANA time zone, falling back to the browser zone. */
export function todayIso(timeZone, now = new Date()) {
  const options = { year: 'numeric', month: '2-digit', day: '2-digit' }
  try {
    return new Intl.DateTimeFormat('en-CA', { ...options, timeZone }).format(now)
  } catch {
    return new Intl.DateTimeFormat('en-CA', options).format(now)
  }
}

/** Adds whole days to an ISO date string without time-zone drift. */
export function addDays(iso, days) {
  const [y, m, d] = iso.split('-').map(Number)
  const date = new Date(Date.UTC(y, m - 1, d + days))
  return date.toISOString().slice(0, 10)
}

/** Short label for an ISO date, e.g. "Fri 9 Oct". */
export function formatDate(iso) {
  const [y, m, d] = iso.split('-').map(Number)
  return new Intl.DateTimeFormat('en-GB', {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    timeZone: 'UTC',
  }).format(new Date(Date.UTC(y, m - 1, d)))
}

/** Formats an instant as HH:mm in the given time zone. */
export function formatTime(instant, timeZone) {
  const options = { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }
  try {
    return new Intl.DateTimeFormat('en-GB', { ...options, timeZone }).format(new Date(instant))
  } catch {
    return new Intl.DateTimeFormat('en-GB', options).format(new Date(instant))
  }
}
