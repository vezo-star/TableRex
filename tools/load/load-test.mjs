#!/usr/bin/env node
// Load and concurrency check against a running deployment (no dependencies; Node 20+).
//
//   LOAD_BASE_URL=http://localhost:8088 LOAD_EMAIL=owner@example.test LOAD_PASSWORD=... node tools/load/load-test.mjs
//
// Optional: LOAD_VENUE_SLUG, LOAD_DATE (yyyy-MM-dd, default today + 14), LOAD_REQUESTS (500), LOAD_CONCURRENCY (25),
// LOAD_RACERS (30), LOAD_BURST (40). Creates and then cancels test bookings on LOAD_DATE: never point it at a
// production tenant with real guests. Credentials are read from the environment and never printed.
// Exit code 1 when an invariant fails (more than one winner for one table, a table double-allocated, 5xx responses).

const base = (process.env.LOAD_BASE_URL ?? 'http://localhost:8088').replace(/\/$/, '')
const email = process.env.LOAD_EMAIL
const password = process.env.LOAD_PASSWORD
if (!email || !password) {
  console.error('Set LOAD_EMAIL and LOAD_PASSWORD (a staff account with booking.create and booking.cancel).')
  process.exit(2)
}
const requests = Number(process.env.LOAD_REQUESTS ?? 500)
const concurrency = Number(process.env.LOAD_CONCURRENCY ?? 25)
const racers = Number(process.env.LOAD_RACERS ?? 30)
const burst = Number(process.env.LOAD_BURST ?? 40)
const date =
  process.env.LOAD_DATE ?? new Date(Date.now() + 14 * 86_400_000).toISOString().slice(0, 10)

let token = null
const failures = []

async function api(method, path, body, headers = {}) {
  const started = performance.now()
  const response = await fetch(`${base}${path}`, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body ? { 'Content-Type': 'application/json' } : {}),
      ...headers,
    },
    body: body ? JSON.stringify(body) : undefined,
  })
  const text = await response.text()
  let json = null
  try {
    json = text ? JSON.parse(text) : null
  } catch {
    json = null
  }
  return { status: response.status, json, ms: performance.now() - started }
}

function percentile(sorted, p) {
  if (!sorted.length) return 0
  return sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))]
}

async function pool(total, size, task) {
  const results = []
  let next = 0
  async function worker() {
    while (next < total) {
      const index = next++
      results[index] = await task(index)
    }
  }
  await Promise.all(Array.from({ length: Math.min(size, total) }, worker))
  return results
}

function summarise(name, results, wallMs) {
  const latencies = results.map((r) => r.ms).sort((a, b) => a - b)
  const statuses = {}
  for (const r of results) statuses[r.status] = (statuses[r.status] ?? 0) + 1
  const serverErrors = results.filter((r) => r.status >= 500).length
  if (serverErrors) failures.push(`${name}: ${serverErrors} responses were 5xx`)
  return {
    name,
    requests: results.length,
    statuses,
    rps: Math.round((results.length / wallMs) * 1000),
    p50: Math.round(percentile(latencies, 50)),
    p95: Math.round(percentile(latencies, 95)),
    p99: Math.round(percentile(latencies, 99)),
    max: Math.round(latencies.at(-1) ?? 0),
  }
}

function booking(venueId, time, allocation, n) {
  return {
    venueId,
    date,
    time,
    partySize: 2,
    allocation,
    source: 'PHONE',
    customer: { customerId: null, firstName: 'Load', lastName: `Test ${n}`, email: `load-${n}@example.test` },
  }
}

async function main() {
  const login = await api('POST', '/api/v1/auth/login', { email, password })
  if (login.status !== 200) throw new Error(`login failed with ${login.status}`)
  token = login.json.accessToken
  if (login.json.requiresTenantSelection) {
    const tenantId = login.json.memberships[0].tenantId
    const switched = await api('POST', '/api/v1/auth/switch-tenant', { tenantId })
    token = switched.json.accessToken
  }

  const venues = (await api('GET', '/api/v1/venues')).json
  const venue = venues.find((v) => !process.env.LOAD_VENUE_SLUG || v.slug === process.env.LOAD_VENUE_SLUG)
  if (!venue) throw new Error('no venue found')
  const resources = (await api('GET', `/api/v1/resources?venueId=${venue.id}&isActive=true&pageSize=100`)).json.items
  const target = resources.find((r) => r.minCapacity <= 2 && r.capacity >= 2)
  if (!target) throw new Error('no table for a party of 2')

  const report = { base, venue: venue.slug, date, tables: resources.length, phases: [] }
  const created = []

  let started = performance.now()
  const reads = await pool(requests, concurrency, (i) =>
    i % 2 === 0
      ? api('GET', `/api/v1/availability?venueId=${venue.id}&date=${date}&partySize=${1 + (i % 4)}`)
      : api('GET', `/api/v1/bookings/board?venueId=${venue.id}&date=${date}`),
  )
  report.phases.push(summarise('availability + board reads', reads, performance.now() - started))

  started = performance.now()
  const race = await Promise.all(
    Array.from({ length: racers }, (_, n) =>
      api('POST', '/api/v1/bookings', booking(venue.id, '13:00', { mode: 'RESOURCES', resourceIds: [target.id] }, n), {
        'Idempotency-Key': crypto.randomUUID(),
      }),
    ),
  )
  const raceSummary = summarise(`${racers} racers for one table at 13:00`, race, performance.now() - started)
  const winners = race.filter((r) => r.status === 201)
  raceSummary.winners = winners.length
  if (winners.length !== 1) failures.push(`one-table race: expected exactly 1 winner, got ${winners.length}`)
  if (race.some((r) => r.status !== 201 && r.status !== 409)) failures.push('one-table race: unexpected status')
  created.push(...winners.map((r) => r.json))
  report.phases.push(raceSummary)

  started = performance.now()
  const autos = await Promise.all(
    Array.from({ length: burst }, (_, n) =>
      api('POST', '/api/v1/bookings', booking(venue.id, '16:00', { mode: 'AUTO' }, 1000 + n), {
        'Idempotency-Key': crypto.randomUUID(),
      }),
    ),
  )
  const autoSummary = summarise(`${burst} auto-allocated bookings at 16:00`, autos, performance.now() - started)
  const autoWinners = autos.filter((r) => r.status === 201).map((r) => r.json)
  const allocated = autoWinners.flatMap((b) => b.resources.map((r) => r.resourceId))
  autoSummary.winners = autoWinners.length
  autoSummary.distinctTables = new Set(allocated).size
  if (new Set(allocated).size !== allocated.length) failures.push('auto burst: a table was allocated twice')
  if (autoWinners.length > resources.length) failures.push('auto burst: more bookings than tables')
  created.push(...autoWinners)
  report.phases.push(autoSummary)

  const board = (await api('GET', `/api/v1/bookings/board?venueId=${venue.id}&date=${date}`)).json
  const overlaps = []
  const active = board.bookings.filter((b) => !['CANCELLED', 'NO_SHOW', 'COMPLETED'].includes(b.status))
  for (const a of active) {
    for (const b of active) {
      if (a.id < b.id && a.startsAt < b.endsAt && b.startsAt < a.endsAt) {
        const shared = a.resources.filter((r) => b.resources.some((s) => s.resourceId === r.resourceId))
        if (shared.length) overlaps.push(`${a.reference}/${b.reference}`)
      }
    }
  }
  report.overlappingAllocations = overlaps.length
  if (overlaps.length) failures.push(`board shows overlapping allocations: ${overlaps.join(', ')}`)

  const cancels = await pool(created.length, 10, (i) =>
    api('POST', `/api/v1/bookings/${created[i].id}/cancel`, { reason: 'Load test clean-up', version: created[i].version }),
  )
  report.cleanedUp = cancels.filter((r) => r.status === 200).length

  console.log(JSON.stringify(report, null, 2))
  if (failures.length) {
    console.error(`FAILED:\n- ${failures.join('\n- ')}`)
    process.exit(1)
  }
  console.log('All invariants held.')
}

main().catch((error) => {
  console.error(error.message)
  process.exit(1)
})
