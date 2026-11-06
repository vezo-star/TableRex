/**
 * Records real TableRex UI scenes for the marketing demo.
 * Run after seed-lounge.mjs, with the API and Quasar dev server up.
 *
 *   node scripts/demo/record-scenes.mjs
 *
 * Raw WebM files land in /tmp/tablerex-demo-raw. Passwords stay in the state file.
 */
import { createRequire } from 'node:module'
import { mkdir, rename } from 'node:fs/promises'
import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const require = createRequire(
  new URL('../../Frontend/BookingPlatform.Web/package.json', import.meta.url),
)
const { chromium, selectors } = require('playwright')
selectors.setTestIdAttribute('data-test')

const here = dirname(fileURLToPath(import.meta.url))
const state = JSON.parse(readFileSync(process.env.DEMO_STATE_PATH ?? '/tmp/tablerex-demo-state.json', 'utf8'))
const rawDir = process.env.DEMO_RAW_DIR ?? '/tmp/tablerex-demo-raw'
const shotDir = '/tmp/tablerex-demo-shots'
const web = state.webBase.replace(/\/$/, '')

const pause = (page, ms) => page.waitForTimeout(ms)

async function shoot(browser, name, { width, height, scale = 1 }, run) {
  const context = await browser.newContext({
    viewport: { width, height },
    deviceScaleFactor: scale,
    locale: 'en-GB',
    timezoneId: 'Africa/Johannesburg',
    recordVideo: { dir: rawDir, size: { width: width * scale, height: height * scale } },
    reducedMotion: 'no-preference',
  })
  const page = await context.newPage()
  page.setDefaultTimeout(45000)
  const extras = []
  try {
    await run({
      page,
      context,
      async extra() {
        const next = await context.newPage()
        extras.push(next)
        return next
      },
    })
  } catch (error) {
    await page.screenshot({ path: join(shotDir, `fail-${name}.png`), fullPage: true }).catch(() => {})
    throw error
  } finally {
    const videos = [[name, page.video()]]
    extras.forEach((extra, index) => videos.push([`${name}__p${index + 1}`, extra.video()]))
    await context.close()
    for (const [label, video] of videos) {
      if (!video) continue
      await rename(await video.path(), join(rawDir, `${label}.webm`))
    }
  }
  console.log('recorded', name)
}

async function signIn(page) {
  await page.goto(`${web}/login`)
  await page.getByTestId('login-email').fill(state.owner.email)
  await page.getByTestId('login-password').fill(state.owner.password)
  await page.getByTestId('login-submit').click()
  await page.waitForURL((url) => !url.pathname.startsWith('/login'), { timeout: 20000 })
  if (page.url().includes('select-tenant')) {
    await page.getByTestId('tenant-demo-lounge-durban').click()
    await page.waitForURL((url) => !url.pathname.includes('select-tenant'))
  }
  await page.getByTestId('tablerex-logo').first().waitFor()
}

async function closeDrawerIfCovering(page, width) {
  if (width < 1400) {
    const toggle = page.getByTestId('drawer-toggle')
    if (await toggle.isVisible()) {
      const drawer = page.locator('.q-drawer')
      const box = await drawer.boundingBox().catch(() => null)
      if (box && box.width > 40) {
        await toggle.click()
        await pause(page, 400)
      }
    }
  }
}

async function openBoard(page, date, view, width) {
  await page.goto(`${web}/bookings?date=${date}&view=${view}`)
  await page.getByTestId('board-toolbar').waitFor({ timeout: 20000 })
  await closeDrawerIfCovering(page, width)
  await page.getByTestId('floor-plan-canvas').waitFor({ timeout: 20000 }).catch(() => {})
}

async function setParty(page, size) {
  await page.getByTestId('party-select').click()
  const option = page.getByRole('option', { name: String(size), exact: true })
  if (await option.count()) {
    await option.click()
  } else {
    await page.locator('.q-menu .q-item').filter({ hasText: new RegExp(`^${size}$`) }).first().click()
  }
}

async function prepareGuest(page, date, email) {
  await page.goto(`${web}${state.publicPath}`)
  await page.getByRole('heading', { name: 'Demo Lounge Durban' }).waitFor()
  await page.getByTestId('date-input').fill(date)
  await setParty(page, 4)
  await page.getByTestId('time-slot').filter({ hasText: '19:00' }).waitFor()
  await pause(page, 600)
  await page.getByTestId('time-slot').filter({ hasText: '19:00' }).click()
  await page.getByTestId('guest-form').waitFor()
  await page.getByTestId('first-name-input').pressSequentially('Demo', { delay: 50 })
  await page.getByLabel('Last name').fill('Guest')
  await page.getByTestId('email-input').fill(email)
  await page.getByLabel('Phone').fill('0315550190')
  await pause(page, 500)
}

async function scrub(page) {
  const slider = page.getByTestId('floor-slider')
  await slider.scrollIntoViewIfNeeded()
  const box = await slider.boundingBox()
  if (!box) return
  const y = box.y + box.height / 2
  await page.mouse.move(box.x + 20, y)
  await page.mouse.down()
  await page.mouse.move(box.x + box.width * 0.72, y, { steps: 28 })
  await pause(page, 400)
  await page.mouse.move(box.x + 24, y, { steps: 28 })
  await page.mouse.up()
  await pause(page, 400)
}

async function selectHero(page) {
  const shape = page.locator(`[data-test="shape-${state.resources.M4}"]`)
  await shape.waitFor()
  const box = await shape.boundingBox()
  if (!box) throw new Error('Main 4 is not on the floor plan')
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2)
  await page.getByTestId('floor-details').waitFor()
}

async function openPages(browser, specs) {
  const opened = []
  for (const spec of specs) {
    const context = await browser.newContext({
      viewport: { width: spec.width, height: spec.height },
      deviceScaleFactor: spec.scale ?? 1,
      locale: 'en-GB',
      timezoneId: 'Africa/Johannesburg',
      recordVideo: {
        dir: rawDir,
        size: { width: spec.width * (spec.scale ?? 1), height: spec.height * (spec.scale ?? 1) },
      },
    })
    const page = await context.newPage()
    page.setDefaultTimeout(45000)
    opened.push({ ...spec, context, page })
  }
  return opened
}

async function closePages(opened) {
  for (const item of opened) {
    const video = item.page.video()
    await item.context.close()
    await rename(await video.path(), join(rawDir, `${item.name}.webm`))
    console.log('recorded', item.name)
  }
}

async function linkedBooking(browser, prefix, date, staffSize) {
  const phone = prefix === 'v'
    ? { width: 430, height: 932, scale: 2 }
    : { width: 1920, height: 1080, scale: 1 }
  const opened = await openPages(browser, [
    { name: `${prefix}_guest`, ...phone },
    { name: `${prefix}_floor`, ...staffSize },
    { name: `${prefix}_rival`, ...phone },
  ])
  const byName = Object.fromEntries(opened.map((item) => [item.name, item.page]))
  const guest = byName[`${prefix}_guest`]
  const staff = byName[`${prefix}_floor`]
  const rival = byName[`${prefix}_rival`]
  try {
    await signIn(staff)
    await openBoard(staff, date, 'floor', staffSize.width)
    await staff.addStyleTag({
      content: '.floor-canvas{height:74vh !important;min-height:520px !important;}',
    })
    await prepareGuest(guest, date, 'test@example.com')
    await prepareGuest(rival, date, 'test.second@example.com')
    await guest.screenshot({ path: join(shotDir, `${prefix}-guest-form.png`) })
    await selectHero(staff)
    await staff.screenshot({ path: join(shotDir, `${prefix}-floor-free.png`) })
    await scrub(staff)
    await selectHero(staff)
    await pause(staff, 500)
    await guest.getByTestId('submit-booking').scrollIntoViewIfNeeded()
    await guest.getByTestId('submit-booking').click()
    await guest.getByTestId('booking-confirmation').waitFor({ timeout: 20000 })
    await guest.getByTestId('booking-confirmation').scrollIntoViewIfNeeded()
    await guest.screenshot({ path: join(shotDir, `${prefix}-confirmation.png`) })
    await pause(guest, 2000)
    await staff.getByText('Free at').waitFor({ state: 'detached', timeout: 15000 }).catch(() => {})
    await pause(staff, 900)
    await staff.screenshot({ path: join(shotDir, `${prefix}-floor-taken.png`) })
    await staff.getByRole('button', { name: 'Open booking' }).click()
    await staff.getByTestId('action-checkIn').click()
    await pause(staff, 1100)
    await staff.screenshot({ path: join(shotDir, `${prefix}-checked-in.png`) })
    await rival.getByTestId('submit-booking').scrollIntoViewIfNeeded()
    await rival.getByTestId('submit-booking').click()
    // The form unmounts once the slot is gone, so the message is the toast, not the inline banner.
    const taken = rival.locator('.q-notification').filter({ hasText: 'just taken' })
    await taken.waitFor({ timeout: 20000 })
    await rival.screenshot({ path: join(shotDir, `${prefix}-taken.png`) })
    await pause(rival, 1800)
    await rival.getByTestId('time-slot').filter({ hasText: '21:00' }).click()
    await rival.getByText('21:00').first().scrollIntoViewIfNeeded()
    await pause(rival, 1600)
    await rival.screenshot({ path: join(shotDir, `${prefix}-next-slot.png`) })
  } catch (error) {
    await guest.screenshot({ path: join(shotDir, `fail-${prefix}-guest.png`) }).catch(() => {})
    await staff.screenshot({ path: join(shotDir, `fail-${prefix}-floor.png`) }).catch(() => {})
    await rival.screenshot({ path: join(shotDir, `fail-${prefix}-rival.png`) }).catch(() => {})
    throw error
  } finally {
    await closePages(opened)
  }
}

async function card(browser, name, layout, which, ms) {
  const size = layout === 'vertical' ? { width: 1080, height: 1920 } : { width: 1920, height: 1080 }
  await shoot(browser, name, size, async ({ page }) => {
    const url = `file://${join(here, 'cards.html')}?card=${which}&layout=${layout}`
    await page.goto(url)
    await pause(page, ms)
  })
}

async function sectionsAndSettings(browser, prefix, { width, height, scale }) {
  await shoot(browser, `${prefix}_sections`, { width, height, scale }, async ({ page }) => {
    await signIn(page)
    await page.goto(`${web}/venues/${state.venueId}`)
    await page.getByRole('tab', { name: 'Sections' }).click()
    await page.getByTestId('sections-list').waitFor()
    await pause(page, 1600)
    await page.screenshot({ path: join(shotDir, `${prefix}-sections.png`) })
    await page.getByRole('tab', { name: 'Opening hours' }).click()
    await page.getByTestId('hours-editor').waitFor()
    await pause(page, 1400)
    await page.getByRole('tab', { name: 'Special days' }).click()
    await page.getByTestId('special-list').waitFor()
    await pause(page, 1400)
    await page.getByRole('tab', { name: 'Details' }).click()
    await page.getByText('Booking rules').scrollIntoViewIfNeeded()
    await pause(page, 1600)
    await page.screenshot({ path: join(shotDir, `${prefix}-rules.png`) })
  })
}

async function staffAndPhone(browser, prefix, date, { width, height, scale }) {
  await shoot(browser, `${prefix}_staff`, { width, height, scale }, async ({ page }) => {
    await signIn(page)
    await page.goto(`${web}/staff`)
    await page.getByTestId('staff-table').waitFor()
    await pause(page, 1800)
    await page.screenshot({ path: join(shotDir, `${prefix}-staff.png`) })
    await page.goto(`${web}/bookings?date=${date}&view=list`)
    await page.getByTestId('board-toolbar').waitFor()
    await closeDrawerIfCovering(page, width)
    await pause(page, 1200)
    await page.getByTestId('board-new-booking').click()
    await page.getByTestId('booking-create-dialog').waitFor()
    await page.getByTestId('create-date').fill(date)
    await page.getByTestId('create-party').fill('2')
    await page.getByTestId('create-search').click()
    await page.getByTestId('slot-21:30').click()
    await pause(page, 700)
    await page.getByTestId('create-next-1').click()
    await page.getByTestId('create-source').getByText('Phone', { exact: true }).click()
    await page.getByLabel('New customer').click()
    await page.getByTestId('create-first-name').fill('Demo')
    await page.getByLabel('Last name').fill('Guest')
    await page.getByLabel('Email').fill('test.phone@example.com')
    await page.getByTestId('create-phone').fill('0315550199')
    await pause(page, 500)
    await page.getByTestId('create-next-2').click()
    await page.getByTestId('create-summary').waitFor()
    await pause(page, 700)
    await page.getByTestId('create-submit').click()
    await page.getByTestId('booking-create-dialog').waitFor({ state: 'hidden', timeout: 20000 })
    await pause(page, 800)
    await page.screenshot({ path: join(shotDir, `${prefix}-phone-list.png`) })
    await page.getByTestId('board-view').getByText('Floor', { exact: true }).click()
    await page.getByTestId('floor-plan-canvas').waitFor()
    await pause(page, 1500)
  })
}

async function listAndTimeline(browser, prefix, date, { width, height, scale }) {
  await shoot(browser, `${prefix}_list`, { width, height, scale }, async ({ page }) => {
    await signIn(page)
    await openBoard(page, date, 'list', width)
    await page.getByTestId('board-summary').waitFor()
    await pause(page, 1800)
    await page.screenshot({ path: join(shotDir, `${prefix}-list.png`) })
    await page.getByTestId('board-view').getByText('Timeline', { exact: true }).click()
    await page.getByTestId('booking-timeline').waitFor()
    await pause(page, 1600)
    await page.screenshot({ path: join(shotDir, `${prefix}-timeline.png`) })
    const booking = page.locator('[data-test^="timeline-booking-"]').first()
    const box = await booking.boundingBox()
    if (box) {
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
      await page.mouse.down()
      await page.mouse.move(box.x + box.width / 2 + 80, box.y + 36, { steps: 18 })
      await page.mouse.up()
      const dialog = page.getByRole('dialog')
      if (await dialog.count()) {
        await pause(page, 600)
        await page.getByRole('button', { name: 'Cancel', exact: true }).click()
      }
    }
    await pause(page, 800)
    await page.goto(`${web}/resources`)
    await page.getByTestId('combinations-panel').scrollIntoViewIfNeeded()
    await pause(page, 1600)
    await page.screenshot({ path: join(shotDir, `${prefix}-combine.png`) })
  })
}

async function mail(browser) {
  await shoot(browser, 'h_email', { width: 1920, height: 1080 }, async ({ page }) => {
    await page.goto('http://127.0.0.1:8025')
    await page.getByText('Your booking at Demo Lounge Durban is confirmed').first().click({ timeout: 15000 })
    await pause(page, 1800)
    await page.screenshot({ path: join(shotDir, 'email.png') })
  })
}

async function main() {
  await mkdir(rawDir, { recursive: true })
  await mkdir(shotDir, { recursive: true })
  const browser = await chromium.launch({
    headless: true,
    args: ['--hide-scrollbars', '--disable-dev-shm-usage'],
  })
  try {
    const only = process.argv[2]
    const want = (name) => !only || only === name
    if (want('cards')) {
      await card(browser, 'v_hook', 'vertical', 'hook', 3200)
      await card(browser, 'v_deposits', 'vertical', 'deposits', 4200)
      await card(browser, 'v_end', 'vertical', 'end', 5200)
      await card(browser, 'h_title', 'horizontal', 'title', 5200)
      await card(browser, 'h_deposits', 'horizontal', 'deposits', 5200)
      await card(browser, 'h_end', 'horizontal', 'end', 6200)
    }
    if (want('vertical') || want('all') || !only) {
      await linkedBooking(browser, 'v', state.dates.vertical, { width: 1080, height: 1920, scale: 1 })
      await sectionsAndSettings(browser, 'v', { width: 1080, height: 1920, scale: 1 })
    }
    if (want('horizontal') || want('all') || !only) {
      await linkedBooking(browser, 'h', state.dates.horizontal, { width: 1920, height: 1080, scale: 1 })
      await listAndTimeline(browser, 'h', state.dates.horizontal, { width: 1920, height: 1080, scale: 1 })
      await sectionsAndSettings(browser, 'h', { width: 1920, height: 1080, scale: 1 })
      await staffAndPhone(browser, 'h', state.dates.horizontal, { width: 1920, height: 1080, scale: 1 })
      await mail(browser).catch((error) => console.warn('email scene skipped:', error.message))
    }
  } finally {
    await browser.close()
  }
}

main().catch((error) => {
  console.error(error)
  process.exit(1)
})
