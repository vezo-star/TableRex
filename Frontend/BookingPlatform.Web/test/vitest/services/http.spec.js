import { AxiosError } from 'axios'
import { describe, expect, it } from 'vitest'
import { DEFAULT_BASE_URL, createHttpClient, http } from '@/services/http.js'
import { ApiError } from '@/utils/problemDetails.js'
import { HttpHeaders } from '@/constants/httpHeaders.js'
import { ErrorCodes } from '@/constants/errorCodes.js'

function recordingAdapter(respond = (config) => ({ status: 200, data: {}, config })) {
  const requests = []
  const adapter = async (config) => {
    requests.push(config)
    const { status, data } = respond(config)
    const response = { status, statusText: '', data, headers: {}, config, request: {} }
    if (status >= 400) {
      throw new AxiosError('Request failed', AxiosError.ERR_BAD_RESPONSE, config, {}, response)
    }
    return response
  }
  return { adapter, requests }
}

describe('http client', () => {
  it('defaults the base URL to /api', () => {
    expect(http.defaults.baseURL).toBe(DEFAULT_BASE_URL)
    expect(DEFAULT_BASE_URL).toBe('/api')
  })

  it('sets a timeout', () => {
    expect(http.defaults.timeout).toBeGreaterThan(0)
  })

  it('adds a unique, API-compatible X-Correlation-Id to every request', async () => {
    const { adapter, requests } = recordingAdapter()
    const client = createHttpClient({ adapter })

    await client.get('/v1/one')
    await client.get('/v1/two')

    const ids = requests.map((r) => r.headers.get(HttpHeaders.CORRELATION_ID))
    for (const id of ids) {
      expect(id).toMatch(/^[A-Za-z0-9._-]{1,64}$/)
    }
    expect(ids[0]).not.toBe(ids[1])
  })

  it('keeps a correlation id supplied by the caller', async () => {
    const { adapter, requests } = recordingAdapter()
    const client = createHttpClient({ adapter })

    await client.get('/v1/one', { headers: { [HttpHeaders.CORRELATION_ID]: 'caller-id' } })

    expect(requests[0].headers.get(HttpHeaders.CORRELATION_ID)).toBe('caller-id')
  })

  it('rejects with a normalised ApiError for ProblemDetails responses', async () => {
    const { adapter } = recordingAdapter(() => ({
      status: 404,
      data: { title: 'Not Found', status: 404, code: 'VENUE_NOT_FOUND', traceId: 't-1' },
    }))
    const client = createHttpClient({ adapter })

    const error = await client.get('/v1/venues/x').catch((e) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({
      status: 404,
      code: 'VENUE_NOT_FOUND',
      message: 'Not Found',
      traceId: 't-1',
      errors: {},
    })
  })

  it('rejects with NETWORK_ERROR when no response is received', async () => {
    const client = createHttpClient({
      adapter: async (config) => {
        throw new AxiosError('Network Error', AxiosError.ERR_NETWORK, config, {})
      },
    })

    const error = await client.get('/v1/anything').catch((e) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 0, code: ErrorCodes.NETWORK_ERROR })
  })

  it('passes cancellations through untouched', async () => {
    const controller = new AbortController()
    controller.abort()
    const { adapter } = recordingAdapter()
    const client = createHttpClient({ adapter })

    const error = await client.get('/v1/anything', { signal: controller.signal }).catch((e) => e)

    expect(error).not.toBeInstanceOf(ApiError)
    expect(error.name).toBe('CanceledError')
  })
})
