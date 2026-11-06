/**
 * Records real TableRex UI scenes for the marketing demo.
 * Run after seed-lounge.mjs, with the API and Quasar dev server up.
 *
 *   node scripts/demo/record-scenes.mjs
 *
 * Raw WebM files land in /tmp/tablerex-demo-raw. Passwords stay in the state file.
 */
import { createRequire } from 'node:module'
import { mkdir, rename, writeFile } from 'node:fs/promises'
import { readFileSync, writeFileSync } from 'node:fs'
import { spawn } from 'node:child_process'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const require = createRequire(
  new URL('../../Frontend/BookingPlatform.Web/package.json', import.meta.url),
)
const { chromium, selectors } = require('playwright')
selectors.setTestIdAttribute('data-test')

const here = dirname(fileURLToPath(import.meta.url))
const state = JSON.parse(readFileSync(process.env.DEMO_STATE_PATH ?? '/tmp/tablerex-demo-state.json', 'utf8'))
const rawDir = process.env.DEMO_RAW_DIR ?? '/tmp/tablerex-demo-raw-4k'
const HD = { width: 1920, height: 1080, scale: 2 }
const shotDir = '/tmp/tablerex-demo-shots'
const web = state.webBase.replace(/\/$/, '')

let activeGrabber = null

async function pause(page, ms) {
  const end = Date.now() + ms
  if (!activeGrabber) {
    await page.waitForTimeout(ms)
    return
  }
  while (Date.now() < end) {
    await activeGrabber.snap(page)
  }
}

const JPEG_QUALITY = 78

/** Grabs device-pixel screenshots (viewport × deviceScaleFactor) and encodes a 30fps mezzanine. */
class SharpGrabber {
  constructor() {
    this.tracks = new Map()
    this.active = null
    this.running = false
  }

  async attach(name, page) {
    const cdp = await page.context().newCDPSession(page)
    const dir = join(rawDir, `${name}-frames`)
    await mkdir(dir, { recursive: true })
    this.tracks.set(name, { page, cdp, dir, frames: [] })
  }

  focus(name) {
    this.active = name
  }

  start() {
    activeGrabber = this
  }

  async snap(page) {
    const name = [...this.tracks.entries()].find(([, track]) => track.page === page)?.[0]
    const track = name ? this.tracks.get(name) : null
    if (!track) return
    const started = Date.now()
    try {
      const data = await page.screenshot({
        type: 'jpeg',
        quality: JPEG_QUALITY,
        timeout: 2500,
        animations: 'disabled',
      })
      const index = track.frames.length
      const file = join(track.dir, `f${String(index).padStart(5, '0')}.jpg`)
      writeFileSync(file, data)
      if (track.t0 == null) track.t0 = started
      track.frames.push({ file, t: started - track.t0 })
      if (index === 0) console.error('first frame', name, data.length)
    } catch (error) {
      console.error('snap', name, String(error.message).split('\n')[0])
    }
  }

  async stop() {
    activeGrabber = null
    for (const [name, track] of this.tracks) {
      if (!track.frames.length) continue
      const dest = join(rawDir, `${name}.mp4`)
      await encodeFrames(track, dest)
      console.log('recorded', name, track.frames.length, 'frames')
    }
  }
}

async function encodeFrames(track, dest) {
  const list = join(track.dir, 'frames.txt')
  const lines = ['ffconcat version 1.0']
  const frames = track.frames
  for (let i = 0; i < frames.length; i++) {
    const next = frames[i + 1]?.t ?? frames[i].t + 400
    const dur = Math.max(0.04, (next - frames[i].t) / 1000)
    lines.push(`file '${frames[i].file}'`, `duration ${dur.toFixed(4)}`)
  }
  lines.push(`file '${frames[frames.length - 1].file}'`)
  writeFileSync(list, lines.join('\n'))
  await runFfmpeg([
    '-f', 'concat', '-safe', '0', '-i', list,
    '-vf', 'fps=30,format=yuv420p',
    '-c:v', 'libx264', '-profile:v', 'high', '-preset', 'medium', '-crf', '16',
    '-pix_fmt', 'yuv420p', '-an', '-movflags', '+faststart', dest,
  ])
}

function runFfmpeg(args) {
  return new Promise((resolve, reject) => {
    const child = spawn('ffmpeg', ['-y', '-hide_banner', '-loglevel', 'error', ...args])
    let err = ''
    child.stderr.on('data', (chunk) => { err += chunk })
    child.on('close', (code) => (code === 0 ? resolve() : reject(new Error(err || `ffmpeg ${code}`))))
  })
}

async function newSharpPage(browser, { width, height, scale }) {
  const context = await browser.newContext({
    viewport: { width, height },
    deviceScaleFactor: scale,
    locale: 'en-GB',
    timezoneId: 'Africa/Johannesburg',
    reducedMotion: 'no-preference',
  })
  const page = await context.newPage()
  page.setDefaultTimeout(45000)
  return { context, page }
}

async function shoot(browser, name, size, run) {
  const grabber = new SharpGrabber()
  const { context, page } = await newSharpPage(browser, size)
  await grabber.attach(name, page)
  grabber.focus(name)
  grabber.start()
  try {
    await run({ page, context, grabber })
  } catch (error) {
    await page.screenshot({ path: join(shotDir, `fail-${name}.png`) }).catch(() => {})
    throw error
  } finally {
    await grabber.stop()
    await context.close()
  }
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

async function linkedBooking(browser, date) {
  const grabber = new SharpGrabber()
  const opened = []
  for (const name of ['h_guest', 'h_floor', 'h_rival']) {
    const { context, page } = await newSharpPage(browser, HD)
    await grabber.attach(name, page)
    opened.push({ name, context, page })
  }
  const page = Object.fromEntries(opened.map((item) => [item.name, item.page]))
  const guest = page.h_guest
  const staff = page.h_floor
  const rival = page.h_rival
  grabber.start()
  try {
    grabber.focus('h_floor')
    await signIn(staff)
    await openBoard(staff, date, 'floor', HD.width)
    await staff.addStyleTag({
      content: '.floor-canvas{height:74vh !important;min-height:520px !important;}',
    })
    await pause(staff, 600)
    grabber.focus('h_guest')
    await prepareGuest(guest, date, 'test@example.com')
    await pause(guest, 700)
    grabber.focus('h_rival')
    await prepareGuest(rival, date, 'test.second@example.com')
    await pause(rival, 500)
    grabber.focus('h_floor')
    await selectHero(staff)
    await pause(staff, 500)
    await scrub(staff)
    await selectHero(staff)
    await pause(staff, 600)
    grabber.focus('h_guest')
    await guest.getByTestId('submit-booking').scrollIntoViewIfNeeded()
    await guest.getByTestId('submit-booking').click()
    await guest.getByTestId('booking-confirmation').waitFor({ timeout: 20000 })
    await guest.getByTestId('booking-confirmation').scrollIntoViewIfNeeded()
    await pause(guest, 1800)
    grabber.focus('h_floor')
    await staff.getByText('Free at').waitFor({ state: 'detached', timeout: 15000 }).catch(() => {})
    await pause(staff, 900)
    await staff.getByRole('button', { name: 'Open booking' }).click()
    await staff.getByTestId('action-checkIn').click()
    await pause(staff, 1400)
    grabber.focus('h_rival')
    await rival.getByTestId('submit-booking').scrollIntoViewIfNeeded()
    await rival.getByTestId('submit-booking').click()
    const taken = rival.locator('.q-notification').filter({ hasText: 'just taken' })
    await taken.waitFor({ timeout: 20000 })
    await pause(rival, 1600)
    await rival.getByTestId('time-slot').filter({ hasText: '21:00' }).click()
    await pause(rival, 1800)
  } catch (error) {
    await guest.screenshot({ path: join(shotDir, 'fail-h-guest.png') }).catch(() => {})
    await staff.screenshot({ path: join(shotDir, 'fail-h-floor.png') }).catch(() => {})
    await rival.screenshot({ path: join(shotDir, 'fail-h-rival.png') }).catch(() => {})
    throw error
  } finally {
    await grabber.stop()
    for (const item of opened) await item.context.close()
  }
}

async function card(browser, name, layout, which, ms) {
  const size = layout === 'vertical'
    ? { width: 1080, height: 1920, scale: 2 }
    : { ...HD }
  await shoot(browser, name, size, async ({ page }) => {
    const url = `file://${join(here, 'cards.html')}?card=${which}&layout=${layout}`
    await page.goto(url)
    await pause(page, ms)
  })
}

async function sectionsAndSettings(browser, prefix, size) {
  await shoot(browser, `${prefix}_sections`, size, async ({ page }) => {
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

async function staffAndPhone(browser, prefix, date, size) {
  await shoot(browser, `${prefix}_staff`, size, async ({ page }) => {
    await signIn(page)
    await page.goto(`${web}/staff`)
    await page.getByTestId('staff-table').waitFor()
    await pause(page, 1800)
    await page.screenshot({ path: join(shotDir, `${prefix}-staff.png`) })
    await page.goto(`${web}/bookings?date=${date}&view=list`)
    await page.getByTestId('board-toolbar').waitFor()
    await closeDrawerIfCovering(page, size.width)
    await pause(page, 1200)
    await page.getByTestId('board-new-booking').click()
    await page.getByTestId('booking-create-dialog').waitFor()
    await page.getByTestId('create-date').fill(date)
    await page.getByTestId('create-party').fill('2')
    await page.getByTestId('create-search').click()
    const slot = page.getByTestId('slot-21:30')
    if (await slot.isEnabled()) await slot.click()
    else await page.getByTestId('slot-21:00').click()
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

async function listAndTimeline(browser, prefix, date, size) {
  await shoot(browser, `${prefix}_list`, size, async ({ page }) => {
    await signIn(page)
    await openBoard(page, date, 'list', size.width)
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
  await shoot(browser, 'h_email', HD, async ({ page }) => {
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
    if (want('cards') || !only) {
      await card(browser, 'v_hook', 'vertical', 'hook', 3200)
      await card(browser, 'v_deposits', 'vertical', 'deposits', 4200)
      await card(browser, 'v_end', 'vertical', 'end', 5200)
      await card(browser, 'h_title', 'horizontal', 'title', 5200)
      await card(browser, 'h_deposits', 'horizontal', 'deposits', 5200)
      await card(browser, 'h_end', 'horizontal', 'end', 6200)
    }
    if (want('scenes') || !only) {
      await linkedBooking(browser, state.dates.horizontal)
      await listAndTimeline(browser, 'h', state.dates.horizontal, HD)
      await sectionsAndSettings(browser, 'h', HD)
      await staffAndPhone(browser, 'h', state.dates.horizontal, HD)
    }
  } finally {
    await browser.close()
  }
}

main().catch((error) => {
  console.error(error)
  process.exit(1)
})
