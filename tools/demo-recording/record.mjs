/**
 * Playwright recordings of the real TableRex UI. No product behaviour is simulated.
 *
 *   node tools/demo-recording/record.mjs
 *
 * Requires the API on :5080, the web app on :9000, and state from seed.mjs.
 * Videos land in /tmp/tablerex-demo/raw plus a manifest of in-clip timestamps.
 */
import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'

const require = createRequire('/workspace/Frontend/BookingPlatform.Web/package.json')
const { chromium, selectors } = require('@playwright/test')
selectors.setTestIdAttribute('data-test')

const state = JSON.parse(readFileSync(process.env.DEMO_STATE ?? '/tmp/tablerex-demo/state.json', 'utf8'))
const password = process.env.DEV_ADMIN_PASSWORD
if (!password) {
  throw new Error('DEV_ADMIN_PASSWORD is not set')
}

const OUT = process.env.DEMO_RAW ?? '/tmp/tablerex-demo/raw'
mkdirSync(OUT, { recursive: true })

const HIDE_CSS = `
  .q-notifications, .q-notification { display: none !important; }
  #vite-error-overlay, vite-error-overlay { display: none !important; }
  [data-test="public-booking-link"] { display: none !important; }
`

function clipPath(name) {
  return join(OUT, `${name}.webm`)
}

async function launch() {
  return chromium.launch({
    headless: true,
    args: ['--hide-scrollbars'],
  })
}

async function prep(context) {
  await context.addInitScript((css) => {
    const apply = () => {
      if (!document.getElementById('demo-recording-css')) {
        const style = document.createElement('style')
        style.id = 'demo-recording-css'
        style.textContent = css
        document.documentElement.appendChild(style)
      }
      if (!document.getElementById('demo-cursor')) {
        const cursor = document.createElement('div')
        cursor.id = 'demo-cursor'
        cursor.style.cssText = [
          'position:fixed',
          'z-index:100000',
          'width:16px',
          'height:16px',
          'margin:-2px 0 0 -2px',
          'border:2px solid #12232a',
          'border-radius:50%',
          'background:#e06c4f',
          'box-shadow:0 1px 4px rgba(18,35,42,.35)',
          'pointer-events:none',
          'left:0',
          'top:0',
        ].join(';')
        document.documentElement.appendChild(cursor)
      }
    }
    apply()
    document.addEventListener(
      'mousemove',
      (event) => {
        const cursor = document.getElementById('demo-cursor')
        if (!cursor) {
          return
        }
        cursor.style.left = `${event.clientX}px`
        cursor.style.top = `${event.clientY}px`
      },
      true,
    )
    new MutationObserver(apply).observe(document.documentElement, { childList: true, subtree: true })
  }, HIDE_CSS)
}

async function openClip(browser, name, { viewport, storageState, mobile = false }) {
  const context = await browser.newContext({
    viewport,
    storageState,
    locale: 'en-ZA',
    timezoneId: 'Africa/Johannesburg',
    colorScheme: 'light',
    deviceScaleFactor: mobile ? 2 : 1,
    isMobile: mobile,
    hasTouch: mobile,
    recordVideo: { dir: OUT, size: viewport },
  })
  await prep(context)
  const page = await context.newPage()
  const started = Date.now()
  const marks = []
  const mark = (label) => {
    const t = (Date.now() - started) / 1000
    marks.push({ label, t: Math.round(t * 100) / 100 })
    console.log(`  ${name} @ ${t.toFixed(1)}s  ${label}`)
  }
  return { context, page, mark, marks, name }
}

async function finish(clip, storagePath) {
  if (storagePath) {
    await clip.context.storageState({ path: storagePath })
  }
  const video = clip.page.video()
  await clip.context.close()
  const src = await video.path()
  const dest = clipPath(clip.name)
  const { renameSync } = await import('node:fs')
  renameSync(src, dest)
  return { file: dest, marks: clip.marks }
}

const pause = (page, ms) => page.waitForTimeout(ms)

async function humanClick(page, locator) {
  await locator.scrollIntoViewIfNeeded()
  const box = await locator.boundingBox()
  if (!box) {
    throw new Error('nothing to click')
  }
  const x = box.x + Math.min(box.width / 2, 80)
  const y = box.y + box.height / 2
  await page.mouse.move(x, y, { steps: 20 })
  await pause(page, 160)
  await page.mouse.click(x, y)
}

async function humanType(page, locator, text) {
  await humanClick(page, locator)
  await pause(page, 120)
  await locator.pressSequentially(text, { delay: 75 })
}

async function signIn(browser, email, file) {
  const context = await browser.newContext({
    locale: 'en-ZA',
    timezoneId: 'Africa/Johannesburg',
    viewport: { width: 1280, height: 800 },
  })
  await prep(context)
  const page = await context.newPage()
  await page.goto(`${state.web}/login`, { waitUntil: 'domcontentloaded' })
  await page.getByTestId('login-email').fill(email)
  await page.getByTestId('login-password').fill(password)
  await page.getByTestId('login-submit').click()
  await page.waitForURL((url) => !url.pathname.startsWith('/login'), { timeout: 20000 })
  if (page.url().includes('/select-tenant')) {
    await page.getByTestId(`tenant-${state.tenantSlug}`).click()
    await page.waitForURL((url) => !url.pathname.includes('select-tenant'), { timeout: 20000 })
  }
  await page.waitForTimeout(500)
  await context.storageState({ path: file })
  await context.close()
}

async function recordGuest(browser) {
  const clip = await openClip(browser, '01-guest-mobile', {
    viewport: { width: 390, height: 844 },
    mobile: true,
  })
  const { page, mark } = clip
  try {
    await page.goto(`${state.web}${state.publicPath}`, { waitUntil: 'domcontentloaded' })
    await page.getByRole('heading', { name: state.venueName }).waitFor()
    mark('venue')
    await pause(page, 900)

    const date = page.getByTestId('date-input')
    await humanClick(page, date)
    await pause(page, 400)
    await date.fill(state.date)
    await date.blur()
    mark('date')
    await pause(page, 500)

    await humanClick(page, page.getByTestId('party-select'))
    await pause(page, 350)
    await page.getByRole('option', { name: '4', exact: true }).click()
    mark('party')
    await page.getByTestId('time-slot').first().waitFor({ timeout: 15000 })
    await pause(page, 600)

    const slot = page.getByTestId('time-slot').filter({ hasText: state.guestTime })
    await humanClick(page, slot)
    mark('time')
    await pause(page, 500)

    const form = page.getByTestId('guest-form-card')
    for (let i = 0; i < 6; i++) {
      const box = await form.boundingBox()
      if (box && box.y < 520) {
        break
      }
      await page.mouse.wheel(0, 260)
      await pause(page, 160)
    }
    await form.scrollIntoViewIfNeeded()
    mark('form')

    await humanType(page, page.getByTestId('first-name-input'), 'Demo')
    await humanType(page, page.getByLabel('Last name'), 'Guest')
    await humanType(page, page.getByTestId('email-input'), 'demo.guest@example.test')
    await humanType(page, page.getByLabel('Phone'), '000 000 0000')
    await humanType(page, page.getByLabel('Special requests'), 'A quieter table if one is free')
    mark('typed')
    await pause(page, 400)
    await humanClick(page, page.getByTestId('submit-booking'))
    mark('submit')
    await page.getByTestId('booking-confirmation').waitFor({ timeout: 20000 })
    await page.evaluate(() => window.scrollTo({ top: 0, behavior: 'smooth' }))
    await pause(page, 400)
    await page.getByTestId('confirmation-title').waitFor()
    mark('confirmed')
    await pause(page, 2800)
    return finish(clip)
  } catch (error) {
    await clip.page.screenshot({ path: join(OUT, 'fail-guest.png') }).catch(() => {})
    await clip.context.close().catch(() => {})
    throw error
  }
}

async function recordStaffLive(browser, storageState) {
  const clip = await openClip(browser, '02-staff-list', {
    viewport: { width: 1920, height: 1080 },
    storageState,
  })
  const { page, mark } = clip
  try {
    await page.goto(`${state.web}/bookings?view=list&date=${state.date}`, { waitUntil: 'domcontentloaded' })
    await page.getByTestId('booking-list').waitFor()
    await page.getByText('Demo Lerato').first().waitFor()
    await page.getByTestId('realtime-indicator').filter({ hasText: 'Live' }).waitFor({ timeout: 20000 })
    mark('list')
    await pause(page, 1200)
    return { clip, mark }
  } catch (error) {
    await clip.page.screenshot({ path: join(OUT, 'fail-staff.png') }).catch(() => {})
    await clip.context.close().catch(() => {})
    throw error
  }
}

async function recordFloor(browser, storageState) {
  const clip = await openClip(browser, '03-floor-checkin', {
    viewport: { width: 1920, height: 1080 },
    storageState,
  })
  const { page, mark } = clip
  try {
    await page.goto(`${state.web}/bookings?view=floor&date=${state.date}`, { waitUntil: 'domcontentloaded' })
    await page.getByTestId('booking-floor').waitFor()
    await page.getByTestId('floor-plan-canvas').waitFor()
    mark('floor')
    await pause(page, 800)
    await scrubFloor(page, '18:30')
    mark('scrubbed')
    await pause(page, 900)

    const shape = page.locator(`[data-test="shape-${state.checkInResourceId}"]`)
    await humanClick(page, shape)
    mark('table')
    await pause(page, 700)
    await humanClick(page, page.getByRole('button', { name: 'Open booking' }))
    await page.getByTestId('booking-drawer').waitFor()
    await page.getByText('Demo Lerato').first().waitFor()
    mark('drawer')
    await pause(page, 700)
    await humanClick(page, page.getByTestId('action-checkIn'))
    await page.getByTestId('status-CHECKED_IN').waitFor({ timeout: 15000 })
    mark('checked-in')
    await pause(page, 1600)
    await humanClick(page, page.getByTestId('booking-drawer').getByRole('button', { name: 'Close' }))
    await pause(page, 1200)
    mark('floor-after')

    await humanClick(page, page.getByTestId('board-view').getByText('Timeline', { exact: true }))
    await page.getByTestId('booking-timeline').waitFor()
    await page.getByText('Main Floor', { exact: true }).waitFor()
    await page.getByText('Terrace', { exact: true }).waitFor()
    await page.getByText('VIP Lounge', { exact: true }).waitFor()
    mark('sections')
    await pause(page, 2200)
    return finish(clip, storageState)
  } catch (error) {
    await clip.page.screenshot({ path: join(OUT, 'fail-floor.png') }).catch(() => {})
    await clip.context.close().catch(() => {})
    throw error
  }
}

async function scrubFloor(page, target) {
  const slider = page.getByTestId('floor-slider')
  const box = await slider.boundingBox()
  if (!box) {
    throw new Error('floor slider missing')
  }
  let lo = box.x + 12
  let hi = box.x + box.width - 12
  let label = ''
  for (let i = 0; i < 16; i++) {
    const mid = (lo + hi) / 2
    await page.mouse.move(mid, box.y + box.height / 2, { steps: 12 })
    await page.mouse.click(mid, box.y + box.height / 2)
    await pause(page, 180)
    label = (await page.getByTestId('floor-time').innerText()).trim()
    if (label === target) {
      return
    }
    if (label < target) {
      lo = mid
    } else {
      hi = mid
    }
  }
  throw new Error(`floor slider stopped at ${label}, wanted ${target}`)
}

async function recordDoubleBook(browser, storageState) {
  const clip = await openClip(browser, '04-double-book', {
    viewport: { width: 1920, height: 1080 },
    storageState,
  })
  const { page, mark } = clip
  try {
    await page.goto(`${state.web}/bookings?view=list&date=${state.date}`, { waitUntil: 'domcontentloaded' })
    await page.getByTestId('board-new-booking').waitFor()
    await pause(page, 500)
    await humanClick(page, page.getByTestId('board-new-booking'))
    const dialog = page.getByTestId('booking-create-dialog')
    await dialog.waitFor()
    mark('dialog')
    await dialog.getByTestId('create-party').fill('4')
    await pause(page, 300)
    await humanClick(page, dialog.getByTestId('create-search'))
    await dialog.getByTestId('create-slots').waitFor({ timeout: 15000 })
    mark('slots')
    await pause(page, 800)

    const blocked = dialog.getByTestId(`slot-${state.blockedTime}`)
    const next = dialog.getByTestId(`slot-${state.nextTime}`)
    await blocked.scrollIntoViewIfNeeded()
    if (!(await blocked.isDisabled())) {
      throw new Error(`${state.blockedTime} was bookable; the double-book setup is wrong`)
    }
    if (await next.isDisabled()) {
      throw new Error(`${state.nextTime} was not offered`)
    }
    const blockedBox = await blocked.boundingBox()
    await page.mouse.move(blockedBox.x + blockedBox.width / 2, blockedBox.y + blockedBox.height / 2, {
      steps: 24,
    })
    mark('blocked')
    await pause(page, 1400)
    await humanClick(page, next)
    mark('next-slot')
    await dialog.getByTestId('create-options').waitFor()
    await pause(page, 1800)
    await humanClick(page, dialog.getByRole('button', { name: 'Close' }))
    await pause(page, 600)
    return finish(clip, storageState)
  } catch (error) {
    await clip.page.screenshot({ path: join(OUT, 'fail-double.png') }).catch(() => {})
    await clip.context.close().catch(() => {})
    throw error
  }
}

async function recordPhone(browser, storageState) {
  const clip = await openClip(browser, '05-phone-booking', {
    viewport: { width: 1920, height: 1080 },
    storageState,
  })
  const { page, mark } = clip
  try {
    await page.goto(`${state.web}/bookings?view=list&date=${state.date}`, { waitUntil: 'domcontentloaded' })
    await humanClick(page, page.getByTestId('board-new-booking'))
    const dialog = page.getByTestId('booking-create-dialog')
    await dialog.waitFor()
    await dialog.getByTestId('create-party').fill('10')
    await humanClick(page, dialog.getByTestId('create-search'))
    await dialog.getByTestId(`slot-${state.phoneTime}`).waitFor({ timeout: 15000 })
    mark('slots')
    const slot = dialog.getByTestId(`slot-${state.phoneTime}`)
    if (await slot.isDisabled()) {
      throw new Error(`${state.phoneTime} was not available for a party of 10`)
    }
    await humanClick(page, slot)
    const combo = dialog.getByText('VIP 1 + VIP 2')
    await combo.waitFor()
    await humanClick(page, combo)
    mark('combined')
    await pause(page, 600)
    await humanClick(page, dialog.getByTestId('create-next-1'))
    await dialog.getByTestId('hold-info').waitFor({ timeout: 15000 })
    mark('held')
    await humanClick(page, dialog.getByTestId('create-source').getByText('Phone', { exact: true }))
    await humanClick(page, dialog.getByText('New customer', { exact: true }))
    await humanType(page, dialog.getByTestId('create-first-name'), 'Demo')
    await humanType(page, dialog.getByLabel('Last name'), 'Caller')
    await humanType(page, dialog.getByTestId('create-phone'), '000 000 0000')
    mark('customer')
    await humanClick(page, dialog.getByTestId('create-next-2'))
    await dialog.getByTestId('create-summary').waitFor()
    await humanType(page, dialog.getByLabel('Internal notes'), 'Taken on WhatsApp')
    mark('notes')
    await pause(page, 500)
    await humanClick(page, dialog.getByTestId('create-submit'))
    await dialog.waitFor({ state: 'hidden', timeout: 20000 })
    mark('created')
    await page.getByText('Demo Caller').first().waitFor()
    await humanClick(page, page.getByText('Demo Caller').first())
    await page.getByTestId('booking-drawer').waitFor()
    await page.getByText('Taken on WhatsApp').waitFor()
    mark('drawer')
    await pause(page, 2200)
    await humanClick(page, page.getByTestId('booking-drawer').getByRole('button', { name: 'Close' }))
    await pause(page, 800)
    return finish(clip, storageState)
  } catch (error) {
    await clip.page.screenshot({ path: join(OUT, 'fail-phone.png') }).catch(() => {})
    await clip.context.close().catch(() => {})
    throw error
  }
}

async function recordSettings(browser, storageState) {
  const clip = await openClip(browser, '06-venue-settings', {
    viewport: { width: 1920, height: 1080 },
    storageState,
  })
  const { page, mark } = clip
  try {
    await page.addInitScript((venueId) => {
      const tenantKey = Object.keys(localStorage).find((key) => key.startsWith('bp-venue:'))
      if (tenantKey) {
        localStorage.setItem(tenantKey, venueId)
      }
    }, state.venueId)
    await page.goto(`${state.web}/venues/${state.venueId}`, { waitUntil: 'domcontentloaded' })
    await page.addStyleTag({
      content: '[data-test="public-booking-link"], .bg-blue-1 { display: none !important; }',
    })
    await page.getByRole('tab', { name: 'Details' }).waitFor()
    mark('details')
    await page.getByText('Slot interval (min)').scrollIntoViewIfNeeded()
    await pause(page, 1600)
    await humanClick(page, page.getByRole('tab', { name: 'Opening hours' }))
    await page.getByTestId('hours-editor').waitFor()
    await page.getByText('Times are in the venue\'s time zone').waitFor()
    mark('hours')
    await pause(page, 2200)
    await humanClick(page, page.getByRole('tab', { name: 'Sections' }))
    const sections = page.getByTestId('sections-list')
    await sections.getByText('Main Floor', { exact: true }).waitFor()
    await sections.getByText('Terrace', { exact: true }).waitFor()
    await sections.getByText('VIP Lounge', { exact: true }).waitFor()
    mark('sections')
    await pause(page, 1800)
    return finish(clip, storageState)
  } catch (error) {
    await clip.page.screenshot({ path: join(OUT, 'fail-settings.png') }).catch(() => {})
    await clip.context.close().catch(() => {})
    throw error
  }
}

async function recordStaffAccounts(browser, storageState) {
  const clip = await openClip(browser, '07-staff-accounts', {
    viewport: { width: 1920, height: 1080 },
    storageState,
  })
  const { page, mark } = clip
  try {
    await page.goto(`${state.web}/staff`, { waitUntil: 'domcontentloaded' })
    await page.getByTestId('staff-table').waitFor()
    await page.getByText('Demo Host').waitFor()
    await page.getByText('host@demo.local').waitFor()
    mark('staff')
    await pause(page, 2200)
    return finish(clip, storageState)
  } catch (error) {
    await clip.page.screenshot({ path: join(OUT, 'fail-accounts.png') }).catch(() => {})
    await clip.context.close().catch(() => {})
    throw error
  }
}

async function main() {
  const browser = await launch()
  const hostState = join(OUT, 'auth-host.json')
  const adminState = join(OUT, 'auth-admin.json')
  const wanted = new Set(process.argv.slice(2))
  const run = (name) => wanted.size === 0 || wanted.has(name)
  console.log('Signing in demo staff')
  await signIn(browser, state.hostEmail, hostState)
  if (run('settings') || run('accounts')) {
    await signIn(browser, state.adminEmail, adminState)
  }

  const clips = []
  if (run('guest') || run('staff')) {
    console.log('Recording guest + staff list together')
    const staff = await recordStaffLive(browser, hostState)
    const guestResult = await recordGuest(browser)
    staff.mark('guest-confirmed')
    await staff.clip.page.getByText('Demo Guest').first().waitFor({ timeout: 15000 })
    staff.mark('appeared')
    await pause(staff.clip.page, 2500)
    clips.push(guestResult, await finish(staff.clip, hostState))
  }
  if (run('floor')) {
    console.log('Recording floor')
    clips.push(await recordFloor(browser, hostState))
  }
  if (run('double')) {
    console.log('Recording double-book')
    clips.push(await recordDoubleBook(browser, hostState))
  }
  if (run('phone')) {
    console.log('Recording phone booking')
    clips.push(await recordPhone(browser, hostState))
  }
  if (run('settings')) {
    console.log('Recording settings')
    clips.push(await recordSettings(browser, adminState))
  }
  if (run('accounts')) {
    console.log('Recording staff accounts')
    clips.push(await recordStaffAccounts(browser, adminState))
  }
  await browser.close()

  const manifest = {
    state: {
      date: state.date,
      venue: state.venueName,
      publicPath: state.publicPath,
    },
    clips,
  }
  const manifestPath = join(dirname(OUT), 'manifest.json')
  writeFileSync(manifestPath, JSON.stringify(manifest, null, 2))
  console.log(manifestPath)
}

main().catch((error) => {
  console.error(error)
  process.exit(1)
})
