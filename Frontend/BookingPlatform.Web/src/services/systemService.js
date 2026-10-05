import { http } from '@/services/http.js'
import { ApiError } from '@/utils/problemDetails.js'
import { ErrorCodes } from '@/constants/errorCodes.js'

const READY_PATH = '/health/ready'

function isReadinessReport(data) {
  return (
    data !== null &&
    typeof data === 'object' &&
    typeof data.status === 'string' &&
    Array.isArray(data.checks) &&
    data.checks.every(
      (check) =>
        check !== null &&
        typeof check === 'object' &&
        typeof check.name === 'string' &&
        typeof check.status === 'string' &&
        typeof check.durationMs === 'number',
    )
  )
}

/**
 * Reads the API readiness report. A 503 still carries the per-check report,
 * so both 200 and 503 are treated as a successful read.
 * @returns {Promise<{ status: string, checks: Array<{ name: string, status: string, durationMs: number }> }>}
 */
export async function getReadiness({ signal } = {}) {
  const response = await http.get(READY_PATH, {
    baseURL: '/',
    signal,
    validateStatus: (status) => status === 200 || status === 503,
  })

  if (!isReadinessReport(response.data)) {
    throw new ApiError({
      status: response.status,
      code: ErrorCodes.INVALID_RESPONSE,
      message: 'The API returned an unexpected response. It may be unavailable.',
    })
  }

  return response.data
}
