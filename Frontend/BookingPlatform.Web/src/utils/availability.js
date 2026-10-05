/** Counts of available and total slots in an availability response. */
export function summariseSlots(slots = []) {
  const available = slots.filter((s) => s.available).length
  return { available, total: slots.length }
}

/** Duration choices between the venue's min and max, stepping by the slot interval (always includes the default). */
export function durationOptions(rules) {
  if (!rules) {
    return []
  }
  const step = Math.max(rules.slotIntervalMinutes || 15, 15)
  const values = new Set([rules.defaultDurationMinutes])
  for (let m = rules.minDurationMinutes; m <= rules.maxDurationMinutes; m += step) {
    values.add(m)
  }
  return [...values]
    .filter((m) => m >= rules.minDurationMinutes && m <= rules.maxDurationMinutes)
    .sort((a, b) => a - b)
    .map((m) => ({ label: formatDuration(m), value: m }))
}

/** 90 -> "1h 30m", 120 -> "2h", 45 -> "45m". */
export function formatDuration(minutes) {
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  if (h === 0) {
    return `${m}m`
  }
  return m === 0 ? `${h}h` : `${h}h ${m}m`
}

/** Resource names for an option ("T5 + T6"), falling back to the option name. */
export function optionResourceLabel(option, resourcesById) {
  const names = option.resourceIds.map((id) => resourcesById.get(id)?.name).filter(Boolean)
  return names.length ? names.join(' + ') : option.name
}
