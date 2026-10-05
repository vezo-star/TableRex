import { describe, expect, it } from 'vitest'
import { ApiError, normaliseError } from '@/utils/problemDetails.js'
import { ErrorCodes } from '@/constants/errorCodes.js'

const responseError = (status, data) => ({ response: { status, data } })

describe('normaliseError', () => {
  it('maps an RFC 7807 ProblemDetails body', () => {
    const error = normaliseError(
      responseError(409, {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.10',
        title: 'Conflict',
        status: 409,
        detail: 'The table is already booked for that time.',
        code: 'BOOKING_CONFLICT',
        traceId: '00-abc-def-01',
        correlationId: 'c0ffee',
      }),
    )

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({
      status: 409,
      code: 'BOOKING_CONFLICT',
      message: 'The table is already booked for that time.',
      traceId: '00-abc-def-01',
      errors: {},
    })
  })

  it('keeps validation field errors as arrays of strings', () => {
    const error = normaliseError(
      responseError(400, {
        title: 'One or more validation errors occurred.',
        code: ErrorCodes.VALIDATION_ERROR,
        errors: { partySize: ['Must be between 1 and 100.'], name: 'Required.' },
      }),
    )

    expect(error.code).toBe(ErrorCodes.VALIDATION_ERROR)
    expect(error.message).toBe('One or more validation errors occurred.')
    expect(error.errors).toEqual({
      partySize: ['Must be between 1 and 100.'],
      name: ['Required.'],
    })
  })

  it('falls back to a status-based code and message when the body is not ProblemDetails', () => {
    const error = normaliseError(responseError(404, '<html>Not Found</html>'))

    expect(error).toMatchObject({
      status: 404,
      code: ErrorCodes.NOT_FOUND,
      message: 'The requested item could not be found.',
      traceId: null,
    })
  })

  it('treats unknown 5xx responses as INTERNAL_ERROR', () => {
    const error = normaliseError(responseError(500, null))

    expect(error.status).toBe(500)
    expect(error.code).toBe(ErrorCodes.INTERNAL_ERROR)
  })

  it.each([502, 503, 504])(
    'reports a bodiless %i from a proxy as temporarily unavailable',
    (status) => {
      const error = normaliseError(responseError(status, '<html>Bad Gateway</html>'))

      expect(error).toMatchObject({
        status,
        code: ErrorCodes.SERVICE_UNAVAILABLE,
        message: 'The service is temporarily unavailable. Please try again in a moment.',
      })
    },
  )

  it('reports a network failure with status 0', () => {
    const error = normaliseError({ code: 'ERR_NETWORK', message: 'Network Error' })

    expect(error.status).toBe(0)
    expect(error.code).toBe(ErrorCodes.NETWORK_ERROR)
  })

  it('reports a timeout distinctly', () => {
    const error = normaliseError({ code: 'ECONNABORTED', message: 'timeout of 15000ms exceeded' })

    expect(error.status).toBe(0)
    expect(error.code).toBe(ErrorCodes.TIMEOUT)
  })

  it('returns an already-normalised error unchanged', () => {
    const original = new ApiError({ status: 403, code: ErrorCodes.FORBIDDEN, message: 'No.' })

    expect(normaliseError(original)).toBe(original)
  })
})
