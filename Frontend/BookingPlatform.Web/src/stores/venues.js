import { defineStore } from 'pinia'
import { venueService } from '@/services/venueService.js'

const STORAGE_PREFIX = 'bp-venue:'

function storageKey(tenantId) {
  return `${STORAGE_PREFIX}${tenantId}`
}

function readSelection(tenantId) {
  try {
    return tenantId ? globalThis.localStorage?.getItem(storageKey(tenantId)) : null
  } catch {
    return null
  }
}

function writeSelection(tenantId, venueId) {
  try {
    if (tenantId && venueId) {
      globalThis.localStorage?.setItem(storageKey(tenantId), venueId)
    }
  } catch {
    // Storage may be unavailable (private mode); selection then lasts for the session only.
  }
}

/** Venues the signed-in user may access in the current tenant, plus the venue selected in the header. */
export const useVenuesStore = defineStore('venues', {
  state: () => ({
    tenantId: null,
    venues: [],
    currentVenueId: null,
    loading: false,
    loaded: false,
  }),

  getters: {
    currentVenue: (state) => state.venues.find((v) => v.id === state.currentVenueId) ?? null,
    activeVenues: (state) => state.venues.filter((v) => v.status === 'ACTIVE'),
  },

  actions: {
    /** Loads venues for `tenantId` once; call again with `force` after creating or editing a venue. */
    async ensureLoaded(tenantId, { force = false } = {}) {
      if (!tenantId) {
        this.reset()
        return []
      }
      if (!force && this.loaded && this.tenantId === tenantId) {
        return this.venues
      }
      this.loading = true
      try {
        const venues = await venueService.listVenues()
        this.tenantId = tenantId
        this.venues = venues
        this.loaded = true
        this.pickDefault()
        return venues
      } finally {
        this.loading = false
      }
    },

    pickDefault() {
      const stored = readSelection(this.tenantId)
      const candidates = [this.currentVenueId, stored]
      const match = candidates.find((id) => id && this.venues.some((v) => v.id === id))
      const fallback = this.activeVenues[0] ?? this.venues[0] ?? null
      this.currentVenueId = match ?? fallback?.id ?? null
      writeSelection(this.tenantId, this.currentVenueId)
    },

    select(venueId) {
      if (!this.venues.some((v) => v.id === venueId)) {
        return
      }
      this.currentVenueId = venueId
      writeSelection(this.tenantId, venueId)
    },

    upsert(venue) {
      const index = this.venues.findIndex((v) => v.id === venue.id)
      if (index >= 0) {
        this.venues.splice(index, 1, venue)
      } else {
        this.venues.push(venue)
        this.venues.sort((a, b) => a.name.localeCompare(b.name))
      }
      if (!this.currentVenueId) {
        this.select(venue.id)
      }
    },

    reset() {
      this.tenantId = null
      this.venues = []
      this.currentVenueId = null
      this.loaded = false
    },
  },
})
