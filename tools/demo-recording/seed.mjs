/**
 * Local-only demo data for marketing recordings.
 * Creates "Demo Grill Umhlanga" with fictional guests. Never calls a production API.
 *
 *   node tools/demo-recording/seed.mjs
 * Reads ConnectionStrings and DEV_ADMIN_PASSWORD from the environment (see repo .env).
 */
import { execFileSync } from 'node:child_process'
import { mkdirSync, writeFileSync } from 'node:fs'

const API = process.env.DEMO_API ?? 'http://127.0.0.1:5080'
const OUT = process.env.DEMO_STATE ?? '/tmp/tablerex-demo/state.json'
const SLUG = 'demo-grill-umhlanga'
const TENANT_SLUG = 'demo-hospitality'

const password = process.env.DEV_ADMIN_PASSWORD
if (!password) {
  console.error('DEV_ADMIN_PASSWORD is not set')
  process.exit(1)
}

function serviceDate() {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Africa/Johannesburg',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date())
}

async function api(token, method, path, body, { expect = [200, 201, 204], headers = {} } = {}) {
  const response = await fetch(`${API}${path}`, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body ? { 'Content-Type': 'application/json' } : {}),
      ...headers,
    },
    body: body ? JSON.stringify(body) : undefined,
  })
  const text = await response.text()
  const json = text ? JSON.parse(text) : null
  if (!expect.includes(response.status)) {
    const detail = json?.code || json?.title || text.slice(0, 400)
    throw new Error(`${method} ${path} -> ${response.status} ${detail}`)
  }
  return json
}

async function login(email) {
  const auth = await api(null, 'POST', '/api/v1/auth/login', { email, password })
  let token = auth.accessToken
  if (auth.requiresTenantSelection) {
    const membership = auth.memberships.find((m) => m.tenantSlug === TENANT_SLUG)
    if (!membership) {
      throw new Error(`No membership for ${TENANT_SLUG}`)
    }
    const switched = await api(token, 'POST', '/api/v1/auth/switch-tenant', {
      tenantId: membership.tenantId,
    })
    token = switched.accessToken
  }
  return token
}

const rules = {
  slotIntervalMinutes: 15,
  defaultDurationMinutes: 90,
  minDurationMinutes: 60,
  maxDurationMinutes: 180,
  turnoverBufferMinutes: 0,
  minPartySize: 1,
  maxPartySize: 12,
  minLeadTimeMinutes: 0,
  maxAdvanceDays: 60,
  holdTtlSeconds: 300,
  autoConfirmPublicBookings: true,
  publicBookingEnabled: true,
  publicCancellationCutoffMinutes: 120,
}

const tables = [
  { code: 'M1', name: 'Main 1', section: 'Main Floor', min: 1, cap: 2, shape: 'ROUND', x: 40, y: 40, w: 80, h: 80 },
  { code: 'M2', name: 'Main 2', section: 'Main Floor', min: 1, cap: 2, shape: 'ROUND', x: 150, y: 40, w: 80, h: 80 },
  { code: 'M3', name: 'Main 3', section: 'Main Floor', min: 2, cap: 4, shape: 'RECT', x: 40, y: 160, w: 140, h: 90 },
  { code: 'M4', name: 'Main 4', section: 'Main Floor', min: 2, cap: 4, shape: 'RECT', x: 210, y: 160, w: 140, h: 90 },
  { code: 'M5', name: 'Main 5', section: 'Main Floor', min: 2, cap: 4, shape: 'RECT', x: 40, y: 280, w: 140, h: 90 },
  { code: 'M6', name: 'Main 6', section: 'Main Floor', min: 2, cap: 4, shape: 'RECT', x: 210, y: 280, w: 140, h: 90 },
  { code: 'M7', name: 'Main 7', section: 'Main Floor', min: 4, cap: 6, shape: 'RECT', x: 40, y: 400, w: 180, h: 100 },
  { code: 'M8', name: 'Main 8', section: 'Main Floor', min: 6, cap: 8, shape: 'RECT', x: 250, y: 400, w: 200, h: 110 },
  { code: 'T1', name: 'Terrace 1', section: 'Terrace', min: 1, cap: 2, shape: 'ROUND', x: 520, y: 40, w: 80, h: 80 },
  { code: 'T2', name: 'Terrace 2', section: 'Terrace', min: 1, cap: 2, shape: 'ROUND', x: 630, y: 40, w: 80, h: 80 },
  { code: 'T3', name: 'Terrace 3', section: 'Terrace', min: 2, cap: 4, shape: 'RECT', x: 520, y: 180, w: 160, h: 100 },
  { code: 'T4', name: 'Terrace 4', section: 'Terrace', min: 2, cap: 4, shape: 'RECT', x: 710, y: 180, w: 160, h: 100 },
  { code: 'V1', name: 'VIP 1', section: 'VIP Lounge', min: 4, cap: 8, shape: 'RECT', x: 930, y: 40, w: 240, h: 140 },
  { code: 'V2', name: 'VIP 2', section: 'VIP Lounge', min: 4, cap: 8, shape: 'RECT', x: 930, y: 210, w: 240, h: 140 },
  { code: 'V3', name: 'VIP 3', section: 'VIP Lounge', min: 6, cap: 10, shape: 'RECT', x: 930, y: 380, w: 240, h: 150 },
]

/** Evening service. Terrace 3 is left open for the on-camera guest booking. */
const seededBookings = [
  // Holds Terrace 3 until the on-camera guest at 18:30, so earlier party-of-4 slots are full.
  { code: 'T3', time: '17:00', party: 2, last: 'Early' },
  { code: 'M1', time: '18:00', party: 2, last: 'Amahle' },
  { code: 'M2', time: '18:00', party: 2, last: 'Sipho' },
  { code: 'M3', time: '18:00', party: 4, last: 'Lerato', checkInTarget: true },
  { code: 'M4', time: '18:00', party: 3, last: 'Johan' },
  { code: 'M5', time: '18:00', party: 4, last: 'Priya' },
  { code: 'M6', time: '18:00', party: 2, last: 'Naledi' },
  { code: 'M7', time: '18:00', party: 6, last: 'Thabo' },
  { code: 'M8', time: '18:00', party: 8, last: 'Zanele', seat: true },
  { code: 'T1', time: '18:00', party: 2, last: 'Pieter' },
  { code: 'T2', time: '18:00', party: 2, last: 'Fatima' },
  { code: 'T4', time: '18:00', party: 4, last: 'Kyle' },
  { code: 'V1', time: '18:00', party: 6, last: 'Anika' },
  { code: 'V2', time: '18:00', party: 7, last: 'Sibusiso' },
  { code: 'V3', time: '20:00', party: 8, last: 'Kagiso' },
]

async function ensureVenue(token) {
  const venues = await api(token, 'GET', '/api/v1/venues')
  const existing = venues.find((v) => v.slug === SLUG)
  if (existing) {
    return existing
  }
  return api(token, 'POST', '/api/v1/venues', {
    name: 'Demo Grill Umhlanga',
    slug: SLUG,
    timeZone: 'Africa/Johannesburg',
    description: 'Garden grill with a terrace and a small VIP lounge.',
    addressLine1: 'Demo Lane',
    addressLine2: null,
    city: 'Umhlanga',
    region: 'KwaZulu-Natal',
    postalCode: '4320',
    countryCode: 'ZA',
    phone: null,
    email: null,
    rules,
  })
}

async function main() {
  const token = await login('admin@demo.local')
  await api(token, 'PUT', '/api/v1/tenants/current', {
    name: 'Demo Grill',
    defaultTimeZone: 'Africa/Johannesburg',
  })

  const pgPassword = process.env.POSTGRES_PASSWORD
  if (pgPassword) {
    execFileSync(
      'sudo',
      [
        'docker',
        'exec',
        '-e',
        `PGPASSWORD=${pgPassword}`,
        'bookingplatform-postgres-1',
        'psql',
        '-U',
        'bookingplatform',
        '-d',
        'bookingplatform',
        '-v',
        'ON_ERROR_STOP=1',
        '-c',
        "UPDATE users SET display_name = 'Demo Owner' WHERE email = 'admin@demo.local';",
      ],
      { stdio: 'inherit' },
    )
  }

  const venue = await ensureVenue(token)
  const date = serviceDate()

  const sections = await api(token, 'GET', `/api/v1/venues/${venue.id}/sections`)
  const sectionIds = Object.fromEntries(sections.map((s) => [s.name, s.id]))
  for (const [index, name] of ['Main Floor', 'Terrace', 'VIP Lounge'].entries()) {
    if (!sectionIds[name]) {
      const created = await api(token, 'POST', `/api/v1/venues/${venue.id}/sections`, {
        name,
        description: null,
        sortOrder: index,
        isActive: true,
      })
      sectionIds[name] = created.id
    }
  }

  await api(token, 'PUT', `/api/v1/venues/${venue.id}/operating-hours`, {
    periods: [1, 2, 3, 4, 5, 6, 7].map((dayOfWeek) => ({
      dayOfWeek,
      openTime: '17:00',
      closeTime: '22:30',
    })),
  })

  let types = await api(token, 'GET', '/api/v1/resource-types')
  if (!types.some((t) => t.code === 'TABLE')) {
    await api(token, 'POST', '/api/v1/resource-types', { code: 'TABLE', name: 'Table' })
    types = await api(token, 'GET', '/api/v1/resource-types')
  }
  const tableType = types.find((t) => t.code === 'TABLE')

  const listed = await api(token, 'GET', `/api/v1/resources?venueId=${venue.id}&pageSize=100`)
  const resources = [...listed.items]
  for (const [index, table] of tables.entries()) {
    if (resources.some((r) => r.code === table.code)) {
      continue
    }
    const created = await api(token, 'POST', '/api/v1/resources', {
      venueId: venue.id,
      sectionId: sectionIds[table.section],
      resourceTypeId: tableType.id,
      name: table.name,
      code: table.code,
      minCapacity: table.min,
      capacity: table.cap,
      isBookableOnline: true,
      sortOrder: index,
    })
    resources.push(created)
  }
  const byCode = Object.fromEntries(resources.map((r) => [r.code, r]))

  const plans = await api(token, 'GET', `/api/v1/floor-plans?venueId=${venue.id}`)
  let plan = plans[0]
  if (!plan) {
    plan = await api(token, 'POST', '/api/v1/floor-plans', {
      venueId: venue.id,
      name: 'Dining room',
      width: 1200,
      height: 560,
      sortOrder: 0,
    })
  }
  await api(token, 'PUT', `/api/v1/floor-plans/${plan.id}/resources`, {
    positions: tables.map((table) => ({
      resourceId: byCode[table.code].id,
      x: table.x,
      y: table.y,
      width: table.w,
      height: table.h,
      rotation: 0,
      shape: table.shape,
    })),
  })

  const combos = await api(token, 'GET', `/api/v1/resource-combinations?venueId=${venue.id}`)
  if (!combos.some((c) => c.name === 'VIP 1 + VIP 2')) {
    await api(token, 'POST', '/api/v1/resource-combinations', {
      venueId: venue.id,
      name: 'VIP 1 + VIP 2',
      minCapacity: 9,
      capacity: 16,
      members: [
        { resourceId: byCode.V1.id, isRequired: true },
        { resourceId: byCode.V2.id, isRequired: true },
      ],
      isBookableOnline: true,
      sortOrder: 0,
    })
  }

  const staff = await api(token, 'GET', '/api/v1/staff?pageSize=50')
  const host = staff.items.find((s) => s.email === 'host@demo.local')
  if (!host) {
    throw new Error('host@demo.local was not seeded')
  }
  await api(token, 'PUT', `/api/v1/staff/${host.tenantUserId}/venues`, { venueIds: [venue.id] })

  const board = await api(token, 'GET', `/api/v1/bookings/board?venueId=${venue.id}&date=${date}`)
  const hhmm = (time) => String(time).slice(0, 5)
  const already = new Set(
    (board.bookings ?? [])
      .filter((b) => b.status !== 'CANCELLED')
      .flatMap((b) => b.resources.map((r) => `${r.code}@${hhmm(b.time)}`)),
  )

  let checkInBookingId = null
  for (const row of seededBookings) {
    const key = `${row.code}@${row.time}`
    const existing = (board.bookings ?? []).find(
      (b) =>
        hhmm(b.time) === row.time &&
        b.resources.some((r) => r.code === row.code) &&
        b.status !== 'CANCELLED',
    )
    let booking = existing
    if (!already.has(key)) {
      booking = await api(
        token,
        'POST',
        '/api/v1/bookings',
        {
          venueId: venue.id,
          customer: { customerId: null, firstName: 'Demo', lastName: row.last, email: null, phone: null },
          date,
          time: row.time,
          durationMinutes: 90,
          partySize: row.party,
          allocation: { mode: 'RESOURCES', resourceIds: [byCode[row.code].id] },
          source: 'PHONE',
          status: 'CONFIRMED',
        },
        { headers: { 'Idempotency-Key': crypto.randomUUID() } },
      )
      booking = booking.booking ?? booking
    }
    if (row.seat && booking?.status === 'CONFIRMED') {
      await api(token, 'POST', `/api/v1/bookings/${booking.id}/seat`, {})
    }
    if (row.checkInTarget) {
      checkInBookingId = booking.id
    }
  }

  const state = {
    api: API,
    web: process.env.DEMO_WEB ?? 'http://127.0.0.1:9000',
    date,
    tenantSlug: TENANT_SLUG,
    tenantName: 'Demo Grill',
    venueId: venue.id,
    venueSlug: SLUG,
    venueName: 'Demo Grill Umhlanga',
    publicPath: `/book/${TENANT_SLUG}/${SLUG}`,
    checkInBookingId,
    checkInResourceId: byCode.M3.id,
    guestTime: '18:30',
    blockedTime: '19:00',
    nextTime: '19:30',
    phoneTime: '20:30',
    hostEmail: 'host@demo.local',
    adminEmail: 'admin@demo.local',
  }
  mkdirSync('/tmp/tablerex-demo', { recursive: true })
  writeFileSync(OUT, JSON.stringify(state, null, 2))
  console.log(`Seeded ${state.venueName} for ${date}`)
  console.log(OUT)
}

main().catch((error) => {
  console.error(error)
  process.exit(1)
})
