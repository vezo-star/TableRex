import { addDays } from '@/utils/dates.js'

export const MAX_REPORT_DAYS = 366

/** The last `days` days ending today (inclusive). */
export function defaultRange(today, days = 30) {
  return { from: addDays(today, -(days - 1)), to: today }
}

/** Validation message for a report range, or null when it is valid. */
export function rangeError(from, to) {
  if (!from || !to) {
    return 'Choose a start and end date.'
  }
  if (to < from) {
    return 'The end date must be on or after the start date.'
  }
  const days = (Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000 + 1
  return days > MAX_REPORT_DAYS ? `Reports cover at most ${MAX_REPORT_DAYS} days.` : null
}

/** A 0-1 fraction as a percentage, e.g. 0.3333 → "33.3%". */
export function formatPercent(rate) {
  const value = Number(rate)
  return Number.isFinite(value) ? `${(value * 100).toFixed(1).replace(/\.0$/, '')}%` : '—'
}

/** Bar length in percent of the largest value (0 when there is nothing to show). */
export function barPercent(value, max) {
  return max > 0 ? Math.round((value / max) * 1000) / 10 : 0
}

/** All 24 hours with zero-filled counts, for an even hourly chart. */
export function fillHours(byHour = []) {
  const map = new Map(byHour.map((h) => [h.hour, h]))
  return Array.from({ length: 24 }, (_, hour) => map.get(hour) ?? { hour, bookings: 0, covers: 0 })
}

/** The hours worth charting: from the first to the last hour that has bookings. */
export function activeHours(byHour = []) {
  const hours = fillHours(byHour)
  const used = hours.filter((h) => h.bookings > 0).map((h) => h.hour)
  if (!used.length) {
    return []
  }
  return hours.slice(Math.min(...used), Math.max(...used) + 1)
}

export const StatusLabels = Object.freeze({
  PENDING: 'Pending',
  HELD: 'Held (deposit)',
  CONFIRMED: 'Confirmed',
  CHECKED_IN: 'Checked in',
  SEATED: 'Seated',
  COMPLETED: 'Completed',
  CANCELLED: 'Cancelled',
  NO_SHOW: 'No-show',
})

export const SourceLabels = Object.freeze({
  STAFF: 'Staff',
  PHONE: 'Phone',
  WALK_IN: 'Walk-in',
  PUBLIC: 'Online',
  API: 'API',
})

/** Triggers a browser download for a Blob. */
export function downloadBlob(blob, fileName, doc = globalThis.document) {
  const url = URL.createObjectURL(blob)
  try {
    const link = doc.createElement('a')
    link.href = url
    link.download = fileName
    link.rel = 'noopener'
    doc.body.appendChild(link)
    link.click()
    link.remove()
  } finally {
    setTimeout(() => URL.revokeObjectURL(url), 0)
  }
}

/** "3 of 5" style usage text; unlimited limits read "3 (unlimited)". */
export function usageText(used, limit) {
  return limit == null ? `${used} (unlimited)` : `${used} of ${limit}`
}

/** 0-1 usage ratio for a progress bar; 0 when unlimited. */
export function usageRatio(used, limit) {
  return limit == null || limit <= 0 ? 0 : Math.min(1, used / limit)
}
