import { describe, expect, it, vi } from 'vitest'
import {
  brandAccent,
  brandStyle,
  contrastWithWhite,
  safeBrandColor,
  safeLogoUrl,
} from '@/utils/branding.js'
import {
  activeHours,
  barPercent,
  defaultRange,
  downloadBlob,
  fillHours,
  formatPercent,
  rangeError,
  usageRatio,
  usageText,
} from '@/utils/reports.js'
import { fileNameFrom } from '@/services/saasService.js'

describe('branding utils', () => {
  it.each([
    ['#1A73E8', '#1a73e8'],
    ['#000000', '#000000'],
    ['red', null],
    ['#fff', null],
    ['#1a73e8; background:url(x)', null],
    [null, null],
  ])('safeBrandColor(%s) → %s', (input, expected) => {
    expect(safeBrandColor(input)).toBe(expected)
  })

  it.each([
    ['https://cdn.example.test/logo.png', 'https://cdn.example.test/logo.png'],
    ['http://cdn.example.test/logo.png', null],
    ['javascript:alert(1)', null],
    ['https://user:pw@cdn.example.test/x.png', null],
    ['not a url', null],
    [undefined, null],
  ])('safeLogoUrl(%s) → %s', (input, expected) => {
    expect(safeLogoUrl(input)).toBe(expected)
  })

  it('re-themes primary only when white text keeps 3:1 contrast', () => {
    expect(contrastWithWhite('#000000')).toBeCloseTo(21, 0)
    expect(brandStyle({ primaryColor: '#1a73e8' })).toEqual({ '--q-primary': '#1a73e8' })
    expect(brandStyle({ primaryColor: '#ffff66' })).toEqual({})
    expect(brandStyle(null)).toEqual({})
    expect(brandAccent({ primaryColor: '#ffff66' })).toBe('#ffff66')
  })
})

describe('report utils', () => {
  it('builds a default range ending today', () => {
    expect(defaultRange('2026-10-05')).toEqual({ from: '2026-09-06', to: '2026-10-05' })
    expect(defaultRange('2026-10-05', 7)).toEqual({ from: '2026-09-29', to: '2026-10-05' })
  })

  it('validates ranges like the API', () => {
    expect(rangeError('2026-01-01', '2026-12-31')).toBeNull()
    expect(rangeError('2026-01-01', '2027-01-01')).toBeNull()
    expect(rangeError('2026-01-01', '2027-01-02')).toMatch(/366/)
    expect(rangeError('2026-02-01', '2026-01-01')).toMatch(/on or after/)
    expect(rangeError('', '2026-01-01')).toMatch(/Choose/)
  })

  it('formats rates and bars', () => {
    expect(formatPercent(0.3333)).toBe('33.3%')
    expect(formatPercent(0.5)).toBe('50%')
    expect(formatPercent('x')).toBe('—')
    expect(barPercent(1, 3)).toBe(33.3)
    expect(barPercent(5, 0)).toBe(0)
  })

  it('fills and trims hours', () => {
    expect(fillHours([{ hour: 19, bookings: 2, covers: 4 }])).toHaveLength(24)
    const hours = activeHours([
      { hour: 18, bookings: 1, covers: 2 },
      { hour: 21, bookings: 3, covers: 9 },
    ])
    expect(hours.map((h) => h.hour)).toEqual([18, 19, 20, 21])
    expect(hours[1].bookings).toBe(0)
    expect(activeHours([])).toEqual([])
  })

  it('describes usage against limits', () => {
    expect(usageText(2, 5)).toBe('2 of 5')
    expect(usageText(7, null)).toBe('7 (unlimited)')
    expect(usageRatio(6, 5)).toBe(1)
    expect(usageRatio(3, null)).toBe(0)
  })

  it('downloads a blob through a temporary link', () => {
    const create = vi.fn(() => 'blob:report')
    const revoke = vi.fn()
    globalThis.URL.createObjectURL = create
    globalThis.URL.revokeObjectURL = revoke
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

    downloadBlob(new Blob(['a']), 'bookings.csv')

    expect(create).toHaveBeenCalled()
    expect(click).toHaveBeenCalledTimes(1)
    expect(document.querySelector('a[download]')).toBeNull()
    click.mockRestore()
  })

  it.each([
    [
      "attachment; filename=bookings-bistro-20261001-20261005.csv; filename*=UTF-8''bookings-bistro-20261001-20261005.csv",
      'bookings-bistro-20261001-20261005.csv',
    ],
    ['attachment; filename="report.csv"', 'report.csv'],
    [undefined, null],
  ])('reads the file name from %s', (header, expected) => {
    expect(fileNameFrom(header)).toBe(expected)
  })
})
