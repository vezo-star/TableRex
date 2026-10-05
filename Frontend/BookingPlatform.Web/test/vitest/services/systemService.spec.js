import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@/utils/problemDetails.js'
import { ErrorCodes } from '@/constants/errorCodes.js'

vi.mock('@/services/http.js', () => ({ http: { get: vi.fn() } }))

const { http } = await import('@/services/http.js')
const { getReadiness } = await import('@/services/systemService.js')

const report = {
  status: 'Unhealthy',
  checks: [
    { name: 'postgres', status: 'Healthy', durationMs: 2.1 },
    { name: 'redis', status: 'Unhealthy', durationMs: 1.4 },
  ],
}

describe('systemService.getReadiness', () => {
  beforeEach(() => {
    http.get.mockReset()
  })

  it('requests /health/ready outside the /api base and accepts 200 and 503', async () => {
    http.get.mockResolvedValue({ status: 503, data: report })

    await expect(getReadiness()).resolves.toEqual(report)

    const [path, config] = http.get.mock.calls[0]
    expect(path).toBe('/health/ready')
    expect(config.baseURL).toBe('/')
    expect(config.validateStatus(200)).toBe(true)
    expect(config.validateStatus(503)).toBe(true)
    expect(config.validateStatus(500)).toBe(false)
  })

  it.each([
    ['an HTML page (SPA fallback)', '<!doctype html><html></html>'],
    ['an empty body', ''],
    ['a report without checks', { status: 'Healthy' }],
    ['a malformed check', { status: 'Healthy', checks: [{ name: 'postgres' }] }],
  ])('rejects %s with INVALID_RESPONSE', async (_label, data) => {
    http.get.mockResolvedValue({ status: 200, data })

    const error = await getReadiness().catch((e) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error.code).toBe(ErrorCodes.INVALID_RESPONSE)
    expect(error.status).toBe(200)
  })
})
