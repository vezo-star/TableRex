import { describe, expect, it } from 'vitest'
import {
  durationOptions,
  formatDuration,
  optionResourceLabel,
  summariseSlots,
} from '@/utils/availability.js'

describe('availability utils', () => {
  it('summarises available slots', () => {
    expect(
      summariseSlots([{ available: true }, { available: false }, { available: true }]),
    ).toEqual({
      available: 2,
      total: 3,
    })
    expect(summariseSlots()).toEqual({ available: 0, total: 0 })
  })

  it('builds duration options between min and max and always includes the default', () => {
    const options = durationOptions({
      slotIntervalMinutes: 15,
      minDurationMinutes: 60,
      maxDurationMinutes: 120,
      defaultDurationMinutes: 100,
    })
    expect(options.map((o) => o.value)).toEqual([60, 75, 90, 100, 105, 120])
    expect(options[0].label).toBe('1h')
    expect(durationOptions(null)).toEqual([])
  })

  it('steps by at least 15 minutes to keep the list short', () => {
    const options = durationOptions({
      slotIntervalMinutes: 5,
      minDurationMinutes: 30,
      maxDurationMinutes: 60,
      defaultDurationMinutes: 45,
    })
    expect(options.map((o) => o.value)).toEqual([30, 45, 60])
  })

  it('formats durations', () => {
    expect(formatDuration(45)).toBe('45m')
    expect(formatDuration(120)).toBe('2h')
    expect(formatDuration(150)).toBe('2h 30m')
  })

  it('labels options with their resource names', () => {
    const byId = new Map([
      ['a', { name: 'T5' }],
      ['b', { name: 'T6' }],
    ])
    expect(optionResourceLabel({ name: 'Combo', resourceIds: ['a', 'b'] }, byId)).toBe('T5 + T6')
    expect(optionResourceLabel({ name: 'Unknown', resourceIds: ['x'] }, byId)).toBe('Unknown')
  })
})
