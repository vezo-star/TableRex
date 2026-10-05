const FALLBACK_ZONES = [
  'Africa/Johannesburg',
  'Africa/Lagos',
  'Africa/Nairobi',
  'America/New_York',
  'America/Chicago',
  'America/Denver',
  'America/Los_Angeles',
  'America/Sao_Paulo',
  'Asia/Dubai',
  'Asia/Kolkata',
  'Asia/Singapore',
  'Asia/Tokyo',
  'Australia/Sydney',
  'Europe/Berlin',
  'Europe/London',
  'Europe/Paris',
  'Pacific/Auckland',
  'UTC',
]

/** IANA zone ids from the browser when supported; the server validates against its own TZDB. */
export function timeZoneOptions() {
  try {
    const zones = Intl.supportedValuesOf?.('timeZone')
    if (Array.isArray(zones) && zones.length > 0) {
      return zones.includes('UTC') ? zones : [...zones, 'UTC']
    }
  } catch {
    // Older browsers: use the fallback list.
  }
  return FALLBACK_ZONES
}
