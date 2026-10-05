import { http } from '@/services/http.js'

export const availabilityService = {
  /**
   * Slots for a venue-local business date with ranked options.
   * @param {{ venueId: string, date: string, partySize: number, durationMinutes?: number, time?: string, sectionId?: string, resourceTypeId?: string }} query
   */
  async search(query, { signal } = {}) {
    const params = Object.fromEntries(
      Object.entries(query).filter(
        ([, value]) => value !== null && value !== undefined && value !== '',
      ),
    )
    const { data } = await http.get('/v1/availability', { params, signal })
    return data
  },
}
