import { test, expect } from '@playwright/test'
import { addDays, createApi, readState } from './support.js'

test.describe('public booking (mobile)', () => {
  test('guest books, a double tap creates one booking, then manages and cancels it', async ({
    page,
  }) => {
    const state = readState()
    const date = addDays(state.baseDate, 2)

    await page.goto(`/book/${state.tenantSlug}/${state.venueSlug}`)
    await expect(page.getByRole('heading', { name: 'Harbour Room' })).toBeVisible()
    await page.getByTestId('date-input').fill(date)
    await page.getByTestId('time-slot').filter({ hasText: '18:00' }).click()

    await page.getByTestId('first-name-input').fill('Thandi')
    await page.getByLabel('Last name').fill('Mokoena')
    await page.getByTestId('email-input').fill('thandi.guest@example.test')
    await page.getByTestId('submit-booking').dblclick()

    await expect(page.getByTestId('booking-confirmation')).toBeVisible()
    const reference = (await page.getByTestId('booking-reference').innerText()).trim()
    expect(reference).toMatch(/^BK-\d{8}-\d{6}$/)

    const api = createApi()
    await api.login(state.owner.email, state.owner.password)
    const board = await api.call(
      'GET',
      `/api/v1/bookings/board?venueId=${state.venueId}&date=${date}`,
    )
    expect(board.bookings.map((b) => b.reference)).toEqual([reference])

    await page.getByTestId('manage-link').click()
    await expect(page).toHaveURL(/\/booking\/manage/)
    await expect(page.getByTestId('manage-booking')).toContainText(reference)
    await expect(page.getByTestId('booking-status')).toBeVisible()

    await page.getByTestId('cancel-booking').click()
    await page.getByRole('dialog').getByRole('button', { name: 'Cancel booking' }).click()
    await expect(page.getByTestId('cancelled-note')).toBeVisible()
    await expect(page.getByTestId('cancel-booking')).toHaveCount(0)

    const after = await api.call(
      'GET',
      `/api/v1/bookings/board?venueId=${state.venueId}&date=${date}`,
    )
    expect(after.bookings.find((b) => b.reference === reference)?.status).toBe('CANCELLED')
  })

  test('an unknown venue explains it is not taking bookings', async ({ page }) => {
    const state = readState()
    await page.goto(`/book/${state.tenantSlug}/no-such-venue`)
    await expect(page.getByRole('alert')).toContainText('This venue is not taking online bookings.')
  })
})
