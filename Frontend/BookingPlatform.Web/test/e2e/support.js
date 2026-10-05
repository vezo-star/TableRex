import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

export const statePath = fileURLToPath(new URL('../../.e2e-state/run.json', import.meta.url))

export function baseUrl() {
  return (process.env.E2E_BASE_URL ?? 'http://localhost:8088').replace(/\/$/, '')
}

/** The tenant, venue and owner provisioned by global-setup for this run. */
export function readState() {
  return JSON.parse(readFileSync(statePath, 'utf8'))
}

export function addDays(isoDate, days) {
  const date = new Date(`${isoDate}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

/** Minimal JSON client for the internal API; throws on unexpected status codes. */
export function createApi(token = null) {
  let accessToken = token
  async function call(method, path, body, { expect = [200, 201, 204], headers = {} } = {}) {
    const response = await fetch(`${baseUrl()}${path}`, {
      method,
      headers: {
        ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
        ...(body ? { 'Content-Type': 'application/json' } : {}),
        ...headers,
      },
      body: body ? JSON.stringify(body) : undefined,
    })
    const text = await response.text()
    const json = text ? JSON.parse(text) : null
    if (!expect.includes(response.status)) {
      const code = json?.code ?? json?.title ?? ''
      throw new Error(`${method} ${path} returned ${response.status} ${code}`.trim())
    }
    return json
  }
  return {
    call,
    async login(email, password) {
      const auth = await call('POST', '/api/v1/auth/login', { email, password })
      accessToken = auth.accessToken
      if (auth.requiresTenantSelection && auth.memberships.length) {
        const switched = await call('POST', '/api/v1/auth/switch-tenant', {
          tenantId: auth.memberships[0].tenantId,
        })
        accessToken = switched.accessToken
      }
      return auth
    },
  }
}

export async function signIn(page, { email, password }) {
  await page.goto('/login')
  await page.getByTestId('login-email').fill(email)
  await page.getByTestId('login-password').fill(password)
  await page.getByTestId('login-submit').click()
  await page.waitForURL((url) => !url.pathname.startsWith('/login'))
}
