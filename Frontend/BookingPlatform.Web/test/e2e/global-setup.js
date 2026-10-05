import { mkdirSync, writeFileSync } from 'node:fs'
import { dirname } from 'node:path'
import { randomBytes } from 'node:crypto'
import { addDays, createApi, statePath } from './support.js'

// Provisions a fresh tenant per run so journeys never touch existing data:
// platform admin -> tenant + owner -> venue, opening hours, table type and four tables.
export default async function globalSetup() {
  const adminEmail = process.env.E2E_ADMIN_EMAIL
  const adminPassword = process.env.E2E_ADMIN_PASSWORD
  if (!adminEmail || !adminPassword) {
    throw new Error(
      'Set E2E_ADMIN_EMAIL and E2E_ADMIN_PASSWORD (a platform admin of the target deployment).',
    )
  }

  const run = Date.now().toString(36)
  const tenantSlug = `e2e-${run}`
  const owner = {
    email: `owner-${run}@e2e.test`,
    password: `Harbour ${randomBytes(12).toString('base64url')} lantern`,
  }

  const admin = createApi()
  await admin.login(adminEmail, adminPassword)
  await admin.call('POST', '/api/v1/platform/tenants', {
    name: `E2E Bistro ${run}`,
    slug: tenantSlug,
    defaultTimeZone: 'Africa/Johannesburg',
    owner: { email: owner.email, displayName: 'E2E Owner', password: owner.password },
  })

  const api = createApi()
  await api.login(owner.email, owner.password)
  const venue = await api.call('POST', '/api/v1/venues', {
    name: 'Harbour Room',
    slug: 'harbour-room',
    timeZone: 'Africa/Johannesburg',
    description: 'Seaside dining for end-to-end tests.',
    city: 'Cape Town',
    countryCode: 'ZA',
    phone: '+27 21 555 0100',
  })
  await api.call('PUT', `/api/v1/venues/${venue.id}/operating-hours`, {
    periods: [1, 2, 3, 4, 5, 6, 7].map((dayOfWeek) => ({
      dayOfWeek,
      openTime: '08:00',
      closeTime: '23:00',
    })),
  })
  const type = await api.call('POST', '/api/v1/resource-types', { code: 'TABLE', name: 'Table' })
  for (const [index, capacity] of [2, 2, 4, 6].entries()) {
    await api.call('POST', '/api/v1/resources', {
      venueId: venue.id,
      sectionId: null,
      resourceTypeId: type.id,
      name: `Table ${index + 1}`,
      code: `T${index + 1}`,
      minCapacity: 1,
      capacity,
      isBookableOnline: true,
      sortOrder: index,
    })
  }

  const today = new Date().toISOString().slice(0, 10)
  const state = {
    tenantSlug,
    venueSlug: venue.slug,
    venueId: venue.id,
    owner,
    baseDate: addDays(today, 3),
  }
  mkdirSync(dirname(statePath), { recursive: true })
  writeFileSync(statePath, JSON.stringify(state), { encoding: 'utf8', mode: 0o600 })
}
