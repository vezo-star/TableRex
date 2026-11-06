/**
 * Creates a fake "Demo Lounge Durban" tenant for marketing recordings.
 * Development only. Guests are made-up (Demo Guest, *@example.com). No prices.
 *
 * Requires the API on DEMO_API_BASE (default http://localhost:5080) and a platform
 * admin from `seed-dev` (DEV_ADMIN_EMAIL / DEV_ADMIN_PASSWORD).
 *
 * Writes /tmp/tablerex-demo-state.json (passwords included; do not commit).
 */
import { randomBytes, randomUUID } from 'node:crypto'
import { writeFileSync } from 'node:fs'

const apiBase = (process.env.DEMO_API_BASE ?? 'http://localhost:5080').replace(/\/$/, '')
const adminEmail = process.env.DEV_ADMIN_EMAIL ?? 'admin@demo.local'
const adminPassword = process.env.DEV_ADMIN_PASSWORD
if (!adminPassword) {
  console.error('Set DEV_ADMIN_PASSWORD (the local seed-dev password).')
  process.exit(1)
}

const statePath = process.env.DEMO_STATE_PATH ?? '/tmp/tablerex-demo-state.json'

function password() {
  return `Lounge-${randomBytes(12).toString('base64url')}`
}

async function call(token, method, path, body, { expect = [200, 201, 204], headers = {} } = {}) {
  const response = await fetch(`${apiBase}${path}`, {
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
    const detail = json?.code ?? json?.title ?? text?.slice(0, 400)
    throw new Error(`${method} ${path} -> ${response.status} ${detail}`)
  }
  return json
}

async function login(email, passwordValue) {
  const auth = await call(null, 'POST', '/api/v1/auth/login', {
    email,
    password: passwordValue,
  })
  let token = auth.accessToken
  if (auth.requiresTenantSelection && auth.memberships?.length) {
    const switched = await call(token, 'POST', '/api/v1/auth/switch-tenant', {
      tenantId: auth.memberships[0].tenantId,
    })
    token = switched.accessToken
  }
  return token
}

function johannesburgToday() {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Africa/Johannesburg',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date())
}

function addDays(iso, days) {
  const [y, m, d] = iso.split('-').map(Number)
  const date = new Date(Date.UTC(y, m - 1, d + days))
  return date.toISOString().slice(0, 10)
}

const rules = {
  slotIntervalMinutes: 30,
  defaultDurationMinutes: 90,
  minDurationMinutes: 60,
  maxDurationMinutes: 180,
  turnoverBufferMinutes: 15,
  minPartySize: 1,
  maxPartySize: 12,
  minLeadTimeMinutes: 0,
  maxAdvanceDays: 90,
  holdTtlSeconds: 300,
  autoConfirmPublicBookings: true,
  publicBookingEnabled: true,
  publicCancellationCutoffMinutes: 120,
}

const owner = {
  email: 'owner@example.com',
  displayName: 'Demo Owner',
  password: password(),
}
const staff = [
  { email: 'manager@example.com', displayName: 'Demo Manager', role: 'MANAGER', password: password() },
  { email: 'host@example.com', displayName: 'Demo Host', role: 'HOST', password: password() },
  { email: 'viewer@example.com', displayName: 'Demo Viewer', role: 'VIEW_ONLY', password: password() },
]

async function book(token, venueId, resourceId, date, time, partySize, guest, source) {
  return call(
    token,
    'POST',
    '/api/v1/bookings',
    {
      venueId,
      customer: {
        firstName: guest.firstName,
        lastName: guest.lastName,
        email: guest.email,
        phone: guest.phone,
      },
      date,
      time,
      durationMinutes: 90,
      partySize,
      allocation: { mode: 'RESOURCES', resourceIds: [resourceId] },
      source,
      status: 'CONFIRMED',
      specialRequests: source === 'PHONE' ? 'Phone booking' : null,
    },
    { headers: { 'Idempotency-Key': randomUUID() } },
  )
}

async function main() {
  const admin = await login(adminEmail, adminPassword)
  const existing = await call(admin, 'GET', '/api/v1/platform/tenants?page=1&pageSize=100')
  if (existing.items?.some((t) => t.slug === 'demo-lounge-durban')) {
    console.error('Tenant demo-lounge-durban already exists. Drop the local database before re-seeding.')
    process.exit(1)
  }

  await call(admin, 'POST', '/api/v1/platform/tenants', {
    name: 'Demo Lounge Durban',
    slug: 'demo-lounge-durban',
    defaultTimeZone: 'Africa/Johannesburg',
    planCode: 'PROFESSIONAL',
    owner: { email: owner.email, displayName: owner.displayName, password: owner.password },
  })

  const token = await login(owner.email, owner.password)
  const venue = await call(token, 'POST', '/api/v1/venues', {
    name: 'Demo Lounge Durban',
    slug: 'durban',
    timeZone: 'Africa/Johannesburg',
    description: 'Evening lounge on the Durban promenade.',
    addressLine1: '1 Demo Wharf',
    city: 'Durban',
    region: 'KwaZulu-Natal',
    postalCode: '4001',
    countryCode: 'ZA',
    phone: '031 555 0100',
    email: 'bookings@example.com',
    rules,
  })

  await call(token, 'PUT', `/api/v1/venues/${venue.id}/operating-hours`, {
    periods: [1, 2, 3, 4, 5, 6, 7].map((dayOfWeek) => ({
      dayOfWeek,
      openTime: '17:00',
      closeTime: '23:00',
    })),
  })

  const today = johannesburgToday()
  const dates = [today, addDays(today, 1)]
  // Demo evenings open at 19:00 so the first bookable slot is the one the
  // recording takes, and the next offered slot is 21:00 after a 90-minute turn.
  for (const date of dates) {
    await call(token, 'POST', `/api/v1/venues/${venue.id}/special-hours`, {
      date,
      name: 'Demo evening service',
      isClosed: false,
      openTime: '19:00',
      closeTime: '23:00',
    })
  }
  const specialDate = addDays(today, 21)
  await call(token, 'POST', `/api/v1/venues/${venue.id}/special-hours`, {
    date: specialDate,
    name: 'Demo tasting night',
    isClosed: false,
    openTime: '18:00',
    closeTime: '23:30',
  })

  const sectionNames = ['Terrace', 'Main', 'VIP']
  const sections = {}
  for (const [index, name] of sectionNames.entries()) {
    const section = await call(token, 'POST', `/api/v1/venues/${venue.id}/sections`, {
      name,
      description: null,
      sortOrder: index,
      isActive: true,
    })
    sections[name] = section.id
  }

  const type = await call(token, 'POST', '/api/v1/resource-types', { code: 'TABLE', name: 'Table' })

  const tables = [
    { code: 'TR1', name: 'Terrace 1', section: 'Terrace', min: 1, capacity: 2, shape: 'ROUND', x: 40, y: 50, w: 100, h: 100 },
    { code: 'TR2', name: 'Terrace 2', section: 'Terrace', min: 1, capacity: 2, shape: 'ROUND', x: 140, y: 50, w: 100, h: 100 },
    { code: 'TR3', name: 'Terrace 3', section: 'Terrace', min: 1, capacity: 2, shape: 'ROUND', x: 70, y: 200, w: 110, h: 110 },
    { code: 'M2', name: 'Main 2', section: 'Main', min: 1, capacity: 2, shape: 'ROUND', x: 340, y: 50, w: 100, h: 100 },
    { code: 'M4', name: 'Main 4', section: 'Main', min: 2, capacity: 4, shape: 'RECT', x: 480, y: 160, w: 170, h: 120 },
    { code: 'M6', name: 'Main 6', section: 'Main', min: 5, capacity: 6, shape: 'RECT', x: 340, y: 340, w: 200, h: 110 },
    { code: 'V1', name: 'VIP 1', section: 'VIP', min: 6, capacity: 8, shape: 'RECT', x: 760, y: 40, w: 240, h: 150 },
    { code: 'V2', name: 'VIP 2', section: 'VIP', min: 6, capacity: 8, shape: 'RECT', x: 760, y: 240, w: 220, h: 140 },
  ]

  const ids = {}
  for (const [index, table] of tables.entries()) {
    const created = await call(token, 'POST', '/api/v1/resources', {
      venueId: venue.id,
      sectionId: sections[table.section],
      resourceTypeId: type.id,
      name: table.name,
      code: table.code,
      minCapacity: table.min,
      capacity: table.capacity,
      isBookableOnline: true,
      sortOrder: index,
    })
    ids[table.code] = created.id
  }

  // Pushed-together terrace pair. Not bookable online, so a party of 4 online
  // can only take Main 4 — that is what the double-booking scene relies on.
  // Staff availability still sees it; the seed books both members at 19:00.
  await call(token, 'POST', '/api/v1/resource-combinations', {
    venueId: venue.id,
    name: 'Terrace 1 + 2',
    minCapacity: 3,
    capacity: 4,
    members: [
      { resourceId: ids.TR1, isRequired: true },
      { resourceId: ids.TR2, isRequired: true },
    ],
    isBookableOnline: false,
    sortOrder: 0,
  })

  const plan = await call(token, 'POST', '/api/v1/floor-plans', {
    venueId: venue.id,
    name: 'Lounge floor',
    width: 1100,
    height: 520,
    sortOrder: 0,
  })
  await call(token, 'PUT', `/api/v1/floor-plans/${plan.id}/resources`, {
    positions: tables.map((table) => ({
      resourceId: ids[table.code],
      x: table.x,
      y: table.y,
      width: table.w,
      height: table.h,
      rotation: 0,
      shape: table.shape,
    })),
  })

  await call(token, 'PUT', '/api/v1/tenants/current/branding', {
    primaryColor: '#12232a',
    logoUrl: null,
    bookingPageIntro: 'Reserve a table online, any evening.',
    bookingTerms: null,
  })

  const createdStaff = []
  for (const member of staff) {
    const result = await call(token, 'POST', '/api/v1/staff', {
      email: member.email,
      displayName: member.displayName,
      password: member.password,
      roles: [member.role],
    })
    const tenantUserId = result.staff?.tenantUserId ?? result.tenantUserId
    if (member.role !== 'TENANT_OWNER') {
      await call(token, 'PUT', `/api/v1/staff/${tenantUserId}/venues`, { venueIds: [venue.id] })
    }
    createdStaff.push({ ...member, tenantUserId })
  }

  const guests = {
    terrace: { firstName: 'Demo', lastName: 'Guest', email: 'test.terrace@example.com', phone: '0315550101' },
    terrace2: { firstName: 'Demo', lastName: 'Guest', email: 'test.terrace2@example.com', phone: '0315550102' },
    main: { firstName: 'Demo', lastName: 'Guest', email: 'test.main@example.com', phone: '0315550103' },
    vip: { firstName: 'Demo', lastName: 'Guest', email: 'test.vip@example.com', phone: '0315550104' },
  }
  for (const date of dates) {
    await book(token, venue.id, ids.TR1, date, '19:00', 2, guests.terrace, 'PHONE')
    await book(token, venue.id, ids.TR2, date, '19:00', 2, guests.terrace2, 'WALK_IN')
    await book(token, venue.id, ids.M2, date, '19:00', 2, guests.main, 'PHONE')
    await book(token, venue.id, ids.V1, date, '19:00', 6, guests.vip, 'PHONE')
    await book(token, venue.id, ids.TR3, date, '20:00', 2, guests.terrace, 'WALK_IN')
  }

  const publicPath = `/book/demo-lounge-durban/durban`
  const state = {
    apiBase,
    webBase: process.env.DEMO_WEB_BASE ?? 'http://localhost:9000',
    tenantSlug: 'demo-lounge-durban',
    venueSlug: 'durban',
    venueId: venue.id,
    venueName: venue.name,
    publicPath,
    dates: { vertical: dates[0], horizontal: dates[1], special: specialDate },
    resources: ids,
    owner: { email: owner.email, password: owner.password, displayName: owner.displayName },
    staff: createdStaff.map(({ email, password: pw, displayName, role }) => ({
      email,
      password: pw,
      displayName,
      role,
    })),
    heroTable: 'M4',
    heroTime: '19:00',
    nextTime: '21:00',
  }
  writeFileSync(statePath, JSON.stringify(state, null, 2), { mode: 0o600 })
  console.log(`Seeded Demo Lounge Durban. State: ${statePath}`)
  console.log(`Public page: ${state.webBase}${publicPath}`)
  console.log(`Dates: vertical ${dates[0]}, horizontal ${dates[1]}`)
}

main().catch((error) => {
  console.error(error)
  process.exit(1)
})
