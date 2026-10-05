import { describe, expect, it } from 'vitest'
import {
  clamp,
  defaultFootprint,
  findFreeSpot,
  fitsInside,
  moveWithin,
  normaliseRotation,
  overlaps,
  resizeWithin,
  snap,
} from '@/utils/floorPlanGeometry.js'
import {
  describePeriod,
  flattenDays,
  groupByDay,
  isOvernight,
  validateWeek,
} from '@/utils/operatingHours.js'
import { slugify } from '@/utils/slug.js'
import { addDays, formatDate, formatTime, todayIso } from '@/utils/dates.js'

const plan = { width: 1000, height: 800 }
const shape = {
  resourceId: 'r1',
  x: 100,
  y: 100,
  width: 80,
  height: 80,
  rotation: 0,
  shape: 'RECT',
}

describe('floorPlanGeometry', () => {
  it('snaps and clamps values', () => {
    expect(snap(14)).toBe(10)
    expect(snap(15)).toBe(20)
    expect(clamp(-5, 0, 10)).toBe(0)
    expect(clamp(50, 0, 10)).toBe(10)
  })

  it.each([
    [0, 0],
    [360, 0],
    [-90, 270],
    [450, 90],
    ['abc', 0],
  ])('normalises rotation %s to %s', (input, expected) => {
    expect(normaliseRotation(input)).toBe(expected)
  })

  it('moves on the grid and keeps the shape inside the plan', () => {
    expect(moveWithin(shape, 23, 7, plan)).toMatchObject({ x: 120, y: 110 })
    expect(moveWithin(shape, 5000, 5000, plan)).toMatchObject({ x: 920, y: 720 })
    expect(moveWithin(shape, -5000, -5000, plan)).toMatchObject({ x: 0, y: 0 })
  })

  it('resizes with a minimum size and within the plan', () => {
    expect(resizeWithin(shape, 40, 20, plan)).toMatchObject({ width: 120, height: 100 })
    expect(resizeWithin(shape, -500, -500, plan)).toMatchObject({ width: 20, height: 20 })
    expect(resizeWithin(shape, 5000, 5000, plan)).toMatchObject({ width: 900, height: 700 })
  })

  it('scales the default footprint by capacity', () => {
    expect(defaultFootprint(1).shape).toBe('ROUND')
    expect(defaultFootprint(4)).toMatchObject({ width: 120, shape: 'RECT' })
    expect(defaultFootprint(100).width).toBe(320)
  })

  it('finds a free spot that does not overlap existing shapes', () => {
    const spot = findFreeSpot(
      [{ x: 20, y: 20, width: 200, height: 200 }],
      { width: 80, height: 80 },
      plan,
    )
    const candidate = { ...spot, width: 80, height: 80 }

    expect(overlaps(candidate, { x: 20, y: 20, width: 200, height: 200 })).toBe(false)
    expect(fitsInside(candidate, plan)).toBe(true)
  })

  it('detects shapes outside the plan', () => {
    expect(fitsInside({ x: 950, y: 0, width: 80, height: 80 }, plan)).toBe(false)
    expect(fitsInside({ x: 0, y: 0, width: 1000, height: 800 }, plan)).toBe(true)
  })
})

describe('operatingHours', () => {
  it('groups by ISO day and flattens back', () => {
    const periods = [
      { dayOfWeek: 5, openTime: '18:00', closeTime: '02:00' },
      { dayOfWeek: 5, openTime: '11:00', closeTime: '15:00' },
      { dayOfWeek: 1, openTime: '09:00', closeTime: '17:00' },
    ]

    const days = groupByDay(periods)

    expect(days).toHaveLength(7)
    expect(days[4].periods.map((p) => p.openTime)).toEqual(['11:00', '18:00'])
    expect(days[6].periods).toEqual([])
    expect(flattenDays(days)).toHaveLength(3)
  })

  it('flags overnight periods', () => {
    expect(isOvernight({ openTime: '18:00', closeTime: '02:00' })).toBe(true)
    expect(isOvernight({ openTime: '09:00', closeTime: '17:00' })).toBe(false)
    expect(describePeriod({ openTime: '18:00', closeTime: '02:00' })).toBe('18:00–02:00 (+1)')
  })

  it('accepts back-to-back periods', () => {
    expect(
      validateWeek([
        { dayOfWeek: 1, openTime: '10:00', closeTime: '14:00' },
        { dayOfWeek: 1, openTime: '14:00', closeTime: '18:00' },
      ]),
    ).toBeNull()
  })

  it.each([
    [
      'same-day overlap',
      [
        { dayOfWeek: 1, openTime: '10:00', closeTime: '15:00' },
        { dayOfWeek: 1, openTime: '14:00', closeTime: '18:00' },
      ],
    ],
    [
      'overnight into the next day',
      [
        { dayOfWeek: 5, openTime: '18:00', closeTime: '02:00' },
        { dayOfWeek: 6, openTime: '01:00', closeTime: '10:00' },
      ],
    ],
    [
      'Sunday overnight into Monday',
      [
        { dayOfWeek: 7, openTime: '20:00', closeTime: '04:00' },
        { dayOfWeek: 1, openTime: '03:00', closeTime: '10:00' },
      ],
    ],
    ['equal open and close', [{ dayOfWeek: 1, openTime: '10:00', closeTime: '10:00' }]],
    ['bad format', [{ dayOfWeek: 1, openTime: '25:00', closeTime: '10:00' }]],
  ])('rejects %s', (_name, periods) => {
    expect(validateWeek(periods)).not.toBeNull()
  })
})

describe('slug and dates', () => {
  it.each([
    ['Demo Restaurant', 'demo-restaurant'],
    ['  Café  Olé!! ', 'cafe-ole'],
    ['---', ''],
  ])('slugifies %s', (input, expected) => {
    expect(slugify(input)).toBe(expected)
  })

  it('computes dates without time zone drift', () => {
    expect(addDays('2026-03-28', 1)).toBe('2026-03-29')
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01')
    expect(formatDate('2026-10-09')).toBe('Fri 9 Oct')
  })

  it('resolves today and times in the venue time zone', () => {
    const instant = new Date('2026-10-09T23:30:00Z')

    expect(todayIso('Africa/Johannesburg', instant)).toBe('2026-10-10')
    expect(todayIso('America/New_York', instant)).toBe('2026-10-09')
    expect(formatTime(instant, 'Africa/Johannesburg')).toBe('01:30')
  })
})
