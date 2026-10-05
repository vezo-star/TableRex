import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/services/http.js', () => ({
  http: { get: vi.fn(), post: vi.fn() },
}))

const { http } = await import('@/services/http.js')
const { publicBookingService, manageBookingUrl, tokenFromHash } =
  await import('@/services/publicBookingService.js')

describe('publicBookingService', () => {
  beforeEach(() => vi.clearAllMocks())

  it('loads venue details and availability anonymously by slug', async () => {
    http.get.mockResolvedValue({ data: { name: 'Bistro' } })
    await publicBookingService.venue('demo co', 'bistro')
    expect(http.get).toHaveBeenCalledWith('/v1/public/demo%20co/bistro', {
      skipAuth: true,
      skipAuthRefresh: true,
      signal: undefined,
    })

    await publicBookingService.availability('demo', 'bistro', { date: '2026-10-09', partySize: 2 })
    expect(http.get).toHaveBeenLastCalledWith('/v1/public/demo/bistro/availability', {
      skipAuth: true,
      skipAuthRefresh: true,
      params: { date: '2026-10-09', partySize: 2 },
      signal: undefined,
    })
  })

  it('creates bookings with an Idempotency-Key and reports replays', async () => {
    http.post.mockResolvedValue({
      data: { reference: 'ABC' },
      headers: { 'idempotent-replayed': 'true' },
    })
    const result = await publicBookingService.create('demo', 'bistro', { partySize: 2 }, 'key-1')
    expect(http.post).toHaveBeenCalledWith(
      '/v1/public/demo/bistro/bookings',
      { partySize: 2 },
      { skipAuth: true, skipAuthRefresh: true, headers: { 'Idempotency-Key': 'key-1' } },
    )
    expect(result).toEqual({ booking: { reference: 'ABC' }, replayed: true })
  })

  it('sends the manage token in the X-Booking-Token header, never in the URL', async () => {
    http.get.mockResolvedValue({ data: {} })
    http.post.mockResolvedValue({ data: {} })
    await publicBookingService.getBooking('secret-token')
    await publicBookingService.cancel('secret-token')

    expect(http.get).toHaveBeenCalledWith('/v1/public/booking', {
      skipAuth: true,
      skipAuthRefresh: true,
      headers: { 'X-Booking-Token': 'secret-token' },
      signal: undefined,
    })
    expect(http.post).toHaveBeenCalledWith('/v1/public/booking/cancel', null, {
      skipAuth: true,
      skipAuthRefresh: true,
      headers: { 'X-Booking-Token': 'secret-token' },
    })
    for (const call of [...http.get.mock.calls, ...http.post.mock.calls]) {
      expect(call[0]).not.toContain('secret-token')
    }
  })
})

describe('manage links', () => {
  it('keeps the token in the fragment and reads it back', () => {
    const url = manageBookingUrl('a_b-c', 'https://book.example')
    expect(url).toBe('https://book.example/booking/manage#a_b-c')
    expect(tokenFromHash(new URL(url).hash)).toBe('a_b-c')
  })

  it('treats a missing or malformed fragment as no token', () => {
    expect(tokenFromHash('')).toBe('')
    expect(tokenFromHash('#')).toBe('')
    expect(tokenFromHash(undefined)).toBe('')
    expect(tokenFromHash('#%E0%A4%A')).toBe('')
  })
})
