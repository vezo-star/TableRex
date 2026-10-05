/** ISO day of week (1 = Monday … 7 = Sunday), matching the API. */
export const Days = Object.freeze([
  { value: 1, label: 'Monday', short: 'Mon' },
  { value: 2, label: 'Tuesday', short: 'Tue' },
  { value: 3, label: 'Wednesday', short: 'Wed' },
  { value: 4, label: 'Thursday', short: 'Thu' },
  { value: 5, label: 'Friday', short: 'Fri' },
  { value: 6, label: 'Saturday', short: 'Sat' },
  { value: 7, label: 'Sunday', short: 'Sun' },
])

const TIME = /^([01]\d|2[0-3]):([0-5]\d)$/

export function isTime(value) {
  return TIME.test(value ?? '')
}

export function toMinutes(time) {
  const [h, m] = time.split(':').map(Number)
  return h * 60 + m
}

/** A period whose close time is at or before its open time runs past midnight. */
export function isOvernight(period) {
  return toMinutes(period.closeTime) <= toMinutes(period.openTime)
}

/** Groups a flat period list into 7 day buckets, each sorted by open time. */
export function groupByDay(periods) {
  return Days.map((day) => ({
    ...day,
    periods: periods
      .filter((p) => p.dayOfWeek === day.value)
      .map((p) => ({ openTime: p.openTime, closeTime: p.closeTime }))
      .sort((a, b) => toMinutes(a.openTime) - toMinutes(b.openTime)),
  }))
}

/** Flattens day buckets back to the API shape. */
export function flattenDays(days) {
  return days.flatMap((day) =>
    day.periods.map((p) => ({
      dayOfWeek: day.value,
      openTime: p.openTime,
      closeTime: p.closeTime,
    })),
  )
}

/**
 * Client-side pre-check mirroring the API: periods must be valid HH:mm, differ, and not overlap across the week
 * (an overnight period may not run into the next day's first period). Returns an error message or null.
 */
export function validateWeek(periods) {
  const week = 7 * 1440
  const intervals = []
  for (const p of periods) {
    if (!isTime(p.openTime) || !isTime(p.closeTime)) {
      return 'Use HH:mm times, for example 18:00.'
    }
    if (p.openTime === p.closeTime) {
      return 'Opening and closing times must differ.'
    }
    const open = toMinutes(p.openTime)
    const close = toMinutes(p.closeTime)
    const start = (p.dayOfWeek - 1) * 1440 + open
    const end = start + (close > open ? close - open : 1440 - open + close)
    if (end <= week) {
      intervals.push([start, end])
    } else {
      intervals.push([start, week], [0, end - week])
    }
  }
  intervals.sort((a, b) => a[0] - b[0])
  for (let i = 1; i < intervals.length; i++) {
    if (intervals[i][0] < intervals[i - 1][1]) {
      return 'Opening periods overlap. Check overnight periods against the next day.'
    }
  }
  return null
}

export function describePeriod(period) {
  return `${period.openTime}–${period.closeTime}${isOvernight(period) ? ' (+1)' : ''}`
}
