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

/** The zone's UTC offset in milliseconds at an instant. */
function zoneOffsetMs(ms, timeZone) {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat('en-US', {
      timeZone,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
      .formatToParts(new Date(ms))
      .map((p) => [p.type, p.value]),
  )
  const asUtc = Date.UTC(
    +parts.year,
    +parts.month - 1,
    +parts.day,
    +parts.hour,
    +parts.minute,
    +parts.second,
  )
  return asUtc - Math.floor(ms / 1000) * 1000
}

/**
 * The UTC instant (ms) of a wall-clock date and time in a zone. Times skipped by a DST gap move forward;
 * ambiguous times resolve to the earlier instant.
 */
export function zonedTimeToUtc(dateIso, time, timeZone) {
  const [y, m, d] = dateIso.split('-').map(Number)
  const [hh, mm] = time.split(':').map(Number)
  const wall = Date.UTC(y, m - 1, d, hh, mm)
  const halfDay = 12 * 3600 * 1000
  const before = wall - zoneOffsetMs(wall - halfDay, timeZone)
  const after = wall - zoneOffsetMs(wall + halfDay, timeZone)
  const valid = [before, after].filter((c) => c + zoneOffsetMs(c, timeZone) === wall)
  return valid.length ? Math.min(...valid) : before
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
