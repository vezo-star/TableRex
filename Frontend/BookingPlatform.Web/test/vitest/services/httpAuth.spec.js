import { AxiosError } from 'axios'
import { describe, expect, it, vi } from 'vitest'
import { createAuthBridge, createHttpClient } from '@/services/http.js'
import { ApiError } from '@/utils/problemDetails.js'

function adapterWith(handler) {
  const requests = []
  const adapter = async (config) => {
    requests.push({ url: config.url, auth: config.headers.get('Authorization') })
    const { status, data = {} } = await handler(config, requests.length)
    const response = { status, statusText: '', data, headers: {}, config, request: {} }
    if (status >= 400) {
      throw new AxiosError('Request failed', AxiosError.ERR_BAD_RESPONSE, config, {}, response)
    }
    return response
  }
  return { adapter, requests }
}

describe('http client auth', () => {
  it('attaches the in-memory access token', async () => {
    const auth = { ...createAuthBridge(), getAccessToken: () => 'token-1' }
    const { adapter, requests } = adapterWith(() => ({ status: 200 }))

    await createHttpClient({ adapter, auth }).get('/v1/staff')

    expect(requests[0].auth).toBe('Bearer token-1')
  })

  it('refreshes once on 401 and retries with the new token', async () => {
    let token = 'expired'
    const auth = {
      ...createAuthBridge(),
      getAccessToken: () => token,
      refreshAccessToken: vi.fn(async () => {
        token = 'fresh'
        return token
      }),
    }
    const { adapter, requests } = adapterWith((config) =>
      config.headers.get('Authorization') === 'Bearer fresh'
        ? { status: 200, data: { ok: true } }
        : { status: 401 },
    )

    const response = await createHttpClient({ adapter, auth }).get('/v1/staff')

    expect(response.data).toEqual({ ok: true })
    expect(auth.refreshAccessToken).toHaveBeenCalledTimes(1)
    expect(requests.map((r) => r.auth)).toEqual(['Bearer expired', 'Bearer fresh'])
  })

  it('shares one refresh between concurrent 401s', async () => {
    let token = 'expired'
    const auth = {
      ...createAuthBridge(),
      getAccessToken: () => token,
      refreshAccessToken: vi.fn(async () => {
        await new Promise((resolve) => setTimeout(resolve, 10))
        token = 'fresh'
        return token
      }),
    }
    const { adapter } = adapterWith((config) =>
      config.headers.get('Authorization') === 'Bearer fresh' ? { status: 200 } : { status: 401 },
    )
    const client = createHttpClient({ adapter, auth })

    await Promise.all([client.get('/a'), client.get('/b'), client.get('/c')])

    expect(auth.refreshAccessToken).toHaveBeenCalledTimes(1)
  })

  it('signals unauthorized and rejects when refresh fails', async () => {
    const auth = { ...createAuthBridge(), getAccessToken: () => 'expired', onUnauthorized: vi.fn() }
    const { adapter } = adapterWith(() => ({ status: 401, data: { code: 'UNAUTHORIZED' } }))

    const error = await createHttpClient({ adapter, auth })
      .get('/v1/staff')
      .catch((e) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error.code).toBe('UNAUTHORIZED')
    expect(auth.onUnauthorized).toHaveBeenCalledTimes(1)
  })

  it('never refreshes for auth endpoints that opt out', async () => {
    const auth = { ...createAuthBridge(), refreshAccessToken: vi.fn(async () => 'x') }
    const { adapter } = adapterWith(() => ({ status: 401, data: { code: 'INVALID_CREDENTIALS' } }))

    const error = await createHttpClient({ adapter, auth })
      .post('/v1/auth/login', {}, { skipAuthRefresh: true, skipAuth: true })
      .catch((e) => e)

    expect(error.code).toBe('INVALID_CREDENTIALS')
    expect(auth.refreshAccessToken).not.toHaveBeenCalled()
  })

  it('does not retry more than once', async () => {
    const auth = {
      ...createAuthBridge(),
      getAccessToken: () => 't',
      refreshAccessToken: vi.fn(async () => 't2'),
    }
    const { adapter, requests } = adapterWith(() => ({ status: 401 }))

    await createHttpClient({ adapter, auth })
      .get('/v1/staff')
      .catch(() => null)

    expect(requests).toHaveLength(2)
  })
})
