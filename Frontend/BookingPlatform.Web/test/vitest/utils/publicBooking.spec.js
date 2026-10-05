import { describe, expect, it } from 'vitest'
import {
  buildPublicBookingPayload,
  formatLongDate,
  formatMinutes,
  guestErrorMessage,
  isSlotGone,
  partySizeOptions,
} from '@/utils/publicBooking.js'

describe('public booking helpers', () => {
  it('lists party sizes between the venue limits', () => {
    expect(partySizeOptions({ minPartySize: 2, maxPartySize: 5 })).toEqual([2, 3, 4, 5])
    expect(partySizeOptions({ minPartySize: 4, maxPartySize: 2 })).toEqual([4])
    expect(partySizeOptions(null)).toEqual([])
  })

  it('trims guest input, nulls blanks and keeps the honeypot', () => {
    const payload = buildPublicBookingPayload(
      { date: '2026-10-09', time: '19:00', partySize: 2 },
      {
        firstName: '  Ada ',
        lastName: ' ',
        email: ' ada@example.test ',
        phone: '',
        specialRequests: ' Window seat ',
        marketingConsent: 1,
        website: '',
      },
    )
    expect(payload).toEqual({
      date: '2026-10-09',
      time: '19:00',
      partySize: 2,
      firstName: 'Ada',
      lastName: null,
      email: 'ada@example.test',
      phone: null,
      specialRequests: 'Window seat',
      marketingConsent: true,
      website: null,
    })
  })

  it('maps API errors to guest messages and spots taken slots', () => {
    const taken = { code: 'RESOURCE_NOT_AVAILABLE', message: 'server text' }
    expect(guestErrorMessage(taken)).toContain('just taken')
    expect(isSlotGone(taken)).toBe(true)
    expect(isSlotGone({ code: 'VALIDATION_ERROR' })).toBe(false)
    expect(guestErrorMessage({ code: 'SOMETHING', message: 'From server' })).toBe('From server')
    expect(guestErrorMessage(null)).toContain('Something went wrong')
  })

  it('formats dates and durations for guests', () => {
    expect(formatLongDate('2026-10-06')).toBe('Tuesday, 6 October 2026')
    expect(formatMinutes(120)).toBe('2 hours')
    expect(formatMinutes(1530)).toBe('1 day 1 hour 30 minutes')
    expect(formatMinutes(0)).toBe('until the booking starts')
  })
})
