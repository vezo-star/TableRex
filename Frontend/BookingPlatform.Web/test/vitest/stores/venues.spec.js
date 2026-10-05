import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

vi.mock('@/services/venueService.js', () => ({
  venueService: { listVenues: vi.fn() },
}))

const { venueService } = await import('@/services/venueService.js')
const { useVenuesStore } = await import('@/stores/venues.js')

const venues = [
  { id: 'v1', name: 'Alpha', status: 'INACTIVE' },
  { id: 'v2', name: 'Bravo', status: 'ACTIVE' },
  { id: 'v3', name: 'Charlie', status: 'ACTIVE' },
]

describe('venues store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    vi.clearAllMocks()
    venueService.listVenues.mockResolvedValue(venues)
  })

  it('loads once per tenant and defaults to the first active venue', async () => {
    const store = useVenuesStore()

    await store.ensureLoaded('t1')
    await store.ensureLoaded('t1')

    expect(venueService.listVenues).toHaveBeenCalledTimes(1)
    expect(store.currentVenueId).toBe('v2')
    expect(store.currentVenue.name).toBe('Bravo')
  })

  it('remembers the selection per tenant', async () => {
    const store = useVenuesStore()
    await store.ensureLoaded('t1')
    store.select('v3')

    setActivePinia(createPinia())
    const fresh = useVenuesStore()
    await fresh.ensureLoaded('t1')

    expect(fresh.currentVenueId).toBe('v3')
  })

  it('ignores a stored venue the user can no longer access', async () => {
    localStorage.setItem('bp-venue:t1', 'gone')
    const store = useVenuesStore()

    await store.ensureLoaded('t1')

    expect(store.currentVenueId).toBe('v2')
  })

  it('reloads when the tenant changes and ignores unknown selections', async () => {
    const store = useVenuesStore()
    await store.ensureLoaded('t1')
    venueService.listVenues.mockResolvedValue([{ id: 'x1', name: 'Other', status: 'ACTIVE' }])

    await store.ensureLoaded('t2')
    store.select('v2')

    expect(venueService.listVenues).toHaveBeenCalledTimes(2)
    expect(store.currentVenueId).toBe('x1')
  })

  it('adds created venues and selects the first one', async () => {
    venueService.listVenues.mockResolvedValue([])
    const store = useVenuesStore()
    await store.ensureLoaded('t1')

    store.upsert({ id: 'n1', name: 'New', status: 'ACTIVE' })

    expect(store.venues).toHaveLength(1)
    expect(store.currentVenueId).toBe('n1')
  })

  it('resets without a tenant', async () => {
    const store = useVenuesStore()
    await store.ensureLoaded('t1')

    await store.ensureLoaded(null)

    expect(store.venues).toEqual([])
    expect(store.currentVenueId).toBeNull()
  })
})
