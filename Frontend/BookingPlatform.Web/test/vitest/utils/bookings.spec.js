import { describe, expect, it } from 'vitest'
import {
  availableActions,
  boardWindow,
  canMove,
  customerName,
  groupResources,
  hourTicks,
  itemsByResource,
  localTime,
  occupancyAt,
  placeInterval,
  snapStart,
  summariseDay,
} from '@/utils/bookings.js'
import { zonedTimeToUtc } from '@/utils/dates.js'

const TZ = 'Africa/Johannesburg'
const all = () => true

function booking(overrides = {}) {
  return {
    id: 'b1',
    status: 'CONFIRMED',
    partySize: 2,
    startsAt: '2026-10-09T16:00:00Z',
    endsAt: '2026-10-09T18:00:00Z',
    time: '18:00',
    customer: { id: 'c1', firstName: 'Ada', lastName: 'Lovelace' },
    resources: [{ resourceId: 'r1', name: 'T1' }],
    ...overrides,
  }
}

const board = (overrides = {}) => ({
  venueId: 'v1',
  date: '2026-10-09',
  timeZone: TZ,
  periods: [{ startsAt: '2026-10-09T15:00:00Z', endsAt: '2026-10-09T21:00:00Z' }],
  slotIntervalMinutes: 15,
  bookings: [],
  blocks: [],
  ...overrides,
})

describe('availableActions', () => {
  it('offers the transitions allowed from the current status and permission', () => {
    const keys = availableActions(booking(), all, Date.parse('2026-10-09T15:00:00Z')).map(
      (a) => a.key,
    )
    expect(keys).toEqual(['checkIn', 'seat', 'cancel'])
  })

  it('allows no-show only after the start time', () => {
    const keys = availableActions(booking(), all, Date.parse('2026-10-09T16:30:00Z')).map(
      (a) => a.key,
    )
    expect(keys).toContain('noShow')
  })

  it('filters by permission and returns nothing for finished bookings', () => {
    const onlyCancel = (p) => p === 'booking.cancel'
    expect(availableActions(booking(), onlyCancel).map((a) => a.key)).toEqual(['cancel'])
    expect(availableActions(booking({ status: 'COMPLETED' }), all)).toEqual([])
    expect(availableActions(booking({ status: 'SEATED' }), all).map((a) => a.key)).toEqual([
      'complete',
    ])
  })

  it('only moves active bookings with the move permission', () => {
    expect(canMove(booking(), all)).toBe(true)
    expect(canMove(booking({ status: 'CANCELLED' }), all)).toBe(false)
    expect(canMove(booking(), () => false)).toBe(false)
  })
})

describe('customerName', () => {
  it('joins names and labels missing customers as walk-ins', () => {
    expect(customerName({ firstName: 'Ada', lastName: null })).toBe('Ada')
    expect(customerName(null)).toBe('Walk-in')
  })
})

describe('summariseDay', () => {
  it('counts bookings, covers, seated and late arrivals', () => {
    const now = Date.parse('2026-10-09T16:10:00Z')
    const summary = summariseDay(
      [
        booking({ id: 'a', partySize: 2 }),
        booking({ id: 'b', status: 'SEATED', partySize: 4 }),
        booking({ id: 'c', status: 'CANCELLED', partySize: 6 }),
        booking({
          id: 'd',
          partySize: 3,
          startsAt: '2026-10-09T17:00:00Z',
          endsAt: '2026-10-09T19:00:00Z',
        }),
      ],
      now,
    )
    expect(summary.bookings).toBe(3)
    expect(summary.covers).toBe(9)
    expect(summary.seatedCovers).toBe(4)
    expect(summary.byStatus.CANCELLED).toBe(1)
    expect(summary.upcoming.map((b) => b.id)).toEqual(['a', 'd'])
    expect(summary.late.map((b) => b.id)).toEqual(['a'])
  })
})

describe('timeline geometry', () => {
  it('spans the periods widened to bookings and rounded to hours', () => {
    const range = boardWindow(
      board({
        bookings: [booking({ startsAt: '2026-10-09T20:30:00Z', endsAt: '2026-10-09T22:10:00Z' })],
      }),
    )
    expect(new Date(range.start).toISOString()).toBe('2026-10-09T15:00:00.000Z')
    expect(new Date(range.end).toISOString()).toBe('2026-10-09T23:00:00.000Z')
    expect(range.minutes).toBe(480)
  })

  it('returns null for a closed day with nothing booked', () => {
    expect(boardWindow(board({ periods: [] }))).toBeNull()
  })

  it('places intervals and hour ticks in pixels', () => {
    const range = boardWindow(board())
    expect(placeInterval('2026-10-09T16:00:00Z', '2026-10-09T18:00:00Z', range, 2)).toEqual({
      left: 120,
      width: 240,
    })
    const ticks = hourTicks(range, 2, TZ)
    expect(ticks[0]).toMatchObject({ left: 0, label: '17:00' })
    expect(ticks).toHaveLength(7)
  })

  it('snaps drags to the slot interval', () => {
    const start = Date.parse('2026-10-09T16:00:00Z')
    expect(snapStart(start, 35, 2, 15) - start).toBe(15 * 60_000)
    expect(snapStart(start, -50, 2, 15) - start).toBe(-30 * 60_000)
    expect(localTime(start, TZ)).toBe('18:00')
  })
})

describe('itemsByResource and occupancyAt', () => {
  const b = board({
    bookings: [
      booking({ resources: [{ resourceId: 'r1' }, { resourceId: 'r2' }] }),
      booking({ id: 'x', status: 'CANCELLED', resources: [{ resourceId: 'r3' }] }),
    ],
    blocks: [
      {
        id: 'k1',
        resourceIds: ['r3'],
        startsAt: '2026-10-09T16:00:00Z',
        endsAt: '2026-10-09T17:00:00Z',
      },
    ],
  })

  it('draws multi-resource bookings on each row and hides cancelled ones by default', () => {
    const rows = itemsByResource(b)
    expect(rows.get('r1')).toHaveLength(1)
    expect(rows.get('r2')).toHaveLength(1)
    expect(rows.get('r3').map((i) => i.kind)).toEqual(['block'])
    expect(itemsByResource(b, { showInactive: true }).get('r3')).toHaveLength(2)
  })

  it('reports who occupies each resource at an instant', () => {
    const at = occupancyAt(b, Date.parse('2026-10-09T16:30:00Z'))
    expect(at.get('r1').booking.id).toBe('b1')
    expect(at.get('r3').kind).toBe('block')
    expect(occupancyAt(b, Date.parse('2026-10-09T18:00:00Z')).size).toBe(0)
  })
})

describe('groupResources', () => {
  it('groups active resources by section order', () => {
    const groups = groupResources(
      [
        { id: 'r1', name: 'T1', sectionId: 's2', sortOrder: 0, isActive: true },
        { id: 'r2', name: 'T2', sectionId: 's1', sortOrder: 1, isActive: true },
        { id: 'r3', name: 'T3', sectionId: null, sortOrder: 0, isActive: true },
        { id: 'r4', name: 'T4', sectionId: 's1', sortOrder: 0, isActive: false },
      ],
      [
        { id: 's1', name: 'Inside', sortOrder: 0 },
        { id: 's2', name: 'Patio', sortOrder: 1 },
      ],
    )
    expect(groups.map((g) => g.name)).toEqual(['Inside', 'Patio', 'Other'])
    expect(groups[0].resources.map((r) => r.id)).toEqual(['r2'])
  })
})

describe('zonedTimeToUtc', () => {
  it('converts a wall-clock time in a fixed-offset zone', () => {
    expect(new Date(zonedTimeToUtc('2026-10-09', '18:00', TZ)).toISOString()).toBe(
      '2026-10-09T16:00:00.000Z',
    )
  })

  it('moves times in a DST gap forward and resolves ambiguous times to the earlier instant', () => {
    expect(new Date(zonedTimeToUtc('2026-03-29', '01:30', 'Europe/London')).toISOString()).toBe(
      '2026-03-29T01:30:00.000Z',
    )
    expect(new Date(zonedTimeToUtc('2026-10-25', '01:30', 'Europe/London')).toISOString()).toBe(
      '2026-10-25T00:30:00.000Z',
    )
    expect(new Date(zonedTimeToUtc('2026-07-01', '09:00', 'America/New_York')).toISOString()).toBe(
      '2026-07-01T13:00:00.000Z',
    )
  })
})
