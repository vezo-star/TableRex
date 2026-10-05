import { test, expect } from '@playwright/test'
import { addDays, createApi, readState, signIn } from './support.js'

test.describe('staff', () => {
  test('signed-out visitors are sent to the login page', async ({ page }) => {
    await page.goto('/bookings')
    await expect(page).toHaveURL(/\/login/)
    await expect(page.getByTestId('login-form')).toBeVisible()
  })

  test('wrong password shows an error and no session', async ({ page }) => {
    const { owner } = readState()
    await page.goto('/login')
    await page.getByTestId('login-email').fill(owner.email)
    await page.getByTestId('login-password').fill('Not-the-right-password-1')
    await page.getByTestId('login-submit').click()
    await expect(page.getByTestId('login-error')).toBeVisible()
    await expect(page).toHaveURL(/\/login/)
  })

  test('creates a phone booking for a new customer and sees it on the board', async ({ page }) => {
    const state = readState()
    const date = state.baseDate
    await signIn(page, state.owner)

    await page.goto('/bookings')
    await page.getByTestId('board-date').fill(date)
    await page.getByTestId('board-new-booking').click()

    const dialog = page.getByTestId('booking-create-dialog')
    await expect(dialog.getByTestId('create-date')).toHaveValue(date)
    await dialog.getByTestId('create-party').fill('3')
    await dialog.getByTestId('create-search').click()
    await dialog.getByTestId('slot-19:00').click()
    await dialog.getByTestId('create-next-1').click()
    await expect(dialog.getByTestId('hold-info')).toBeVisible()

    await dialog.getByText('New customer', { exact: true }).click()
    await dialog.getByTestId('create-first-name').fill('Nomsa')
    await dialog.getByLabel('Last name').fill('Dlamini')
    await dialog.getByTestId('create-phone').fill('+27 82 555 0101')
    await dialog.getByTestId('create-next-2').click()

    await expect(dialog.getByTestId('create-summary')).toContainText('19:00')
    await expect(dialog.getByTestId('create-summary')).toContainText('3 guests')
    await dialog.getByTestId('create-submit').click()
    await expect(dialog).toBeHidden()

    await expect(page.getByTestId('board-summary')).toContainText('1 bookings · 3 covers')
    await page.getByTestId('board-view').getByText('List', { exact: true }).click()
    await expect(page.getByText('Nomsa Dlamini').first()).toBeVisible()
  })

  test('the board updates live when another device books', async ({ page }) => {
    const state = readState()
    const date = addDays(state.baseDate, 1)
    await signIn(page, state.owner)
    await page.goto('/bookings')
    await page.getByTestId('board-date').fill(date)
    await expect(page.getByTestId('board-summary')).toContainText('0 bookings')

    const api = createApi()
    await api.login(state.owner.email, state.owner.password)
    await api.call(
      'POST',
      '/api/v1/bookings',
      {
        venueId: state.venueId,
        date,
        time: '12:30',
        partySize: 2,
        allocation: { mode: 'AUTO' },
        source: 'PHONE',
        customer: { customerId: null, firstName: 'Realtime', lastName: 'Guest', email: null },
      },
      { headers: { 'Idempotency-Key': crypto.randomUUID() } },
    )

    await expect(page.getByTestId('board-summary')).toContainText('1 bookings · 2 covers')
  })
})
