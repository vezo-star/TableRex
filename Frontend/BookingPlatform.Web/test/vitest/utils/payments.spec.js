import { describe, expect, it } from 'vitest'
import {
  depositNotice,
  formatMoney,
  parseSimulatedCheckout,
  rememberPaymentToken,
  safeReturnUrl,
  takePaymentToken,
  toMajorText,
  toMinorUnits,
} from '@/utils/payments.js'

describe('payments utils', () => {
  it('formats minor units as money', () => {
    expect(formatMoney(12345, 'ZAR')).toMatch(/R\s?123[,.]45/)
    expect(formatMoney(100, 'XXX-bad')).toBe('XXX-bad 1.00')
  })

  it('parses typed amounts into minor units without floating point drift', () => {
    expect(toMinorUnits('123.45')).toBe(12345)
    expect(toMinorUnits('0,1')).toBe(10)
    expect(toMinorUnits(' 1 000 ')).toBe(100000)
    expect(toMinorUnits('19.99')).toBe(1999)
    expect(toMinorUnits('0')).toBeNull()
    expect(toMinorUnits('1.234')).toBeNull()
    expect(toMinorUnits('-5')).toBeNull()
    expect(toMinorUnits('abc')).toBeNull()
    expect(toMajorText(1999)).toBe('19.99')
    expect(toMajorText(null)).toBe('')
  })

  it('describes the deposit for the chosen party size', () => {
    expect(depositNotice(null, 2)).toBe('')
    expect(depositNotice({ policy: 'PER_GUEST', amountMinor: 5000, currency: 'ZAR' }, 3)).toMatch(
      /per guest .*150.* for 3 guests/,
    )
    expect(
      depositNotice({ policy: 'PER_BOOKING', amountMinor: 5000, currency: 'ZAR' }, 3),
    ).not.toContain('per guest')
  })

  it('parks the manage token once', () => {
    sessionStorage.clear()
    rememberPaymentToken('tok')
    expect(takePaymentToken()).toBe('tok')
    expect(takePaymentToken()).toBe('')
  })

  it('parses the simulated checkout fragment', () => {
    expect(parseSimulatedCheckout('#p=1&t=x&a=500&c=USD&r=%2Fback')).toEqual({
      paymentId: '1',
      token: 'x',
      amountMinor: 500,
      currency: 'USD',
      returnUrl: '/back',
    })
    expect(parseSimulatedCheckout('#p=1&t=x')).toBeNull()
    expect(parseSimulatedCheckout('')).toBeNull()
  })

  it('only follows same-origin return URLs', () => {
    const origin = 'https://app.test'
    expect(safeReturnUrl('https://app.test/booking/payment-return?x=1', origin)).toBe(
      '/booking/payment-return?x=1',
    )
    expect(safeReturnUrl('/relative', origin)).toBe('/relative')
    expect(safeReturnUrl('https://evil.test/x', origin)).toBeNull()
    expect(safeReturnUrl('//evil.test/x', origin)).toBeNull()
    expect(safeReturnUrl('', origin)).toBeNull()
  })
})
