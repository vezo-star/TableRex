import { ErrorCodes } from '@/constants/errorCodes.js'

const STATUS_CODES = Object.freeze({
  400: ErrorCodes.BAD_REQUEST,
  401: ErrorCodes.UNAUTHORIZED,
  403: ErrorCodes.FORBIDDEN,
  404: ErrorCodes.NOT_FOUND,
  405: ErrorCodes.METHOD_NOT_ALLOWED,
  409: ErrorCodes.CONFLICT,
  429: ErrorCodes.RATE_LIMITED,
})

const DEFAULT_MESSAGES = Object.freeze({
  [ErrorCodes.NETWORK_ERROR]:
    'The server could not be reached. Check your connection and try again.',
  [ErrorCodes.TIMEOUT]: 'The server took too long to respond. Please try again.',
  [ErrorCodes.UNAUTHORIZED]: 'Your session has expired. Please sign in again.',
  [ErrorCodes.FORBIDDEN]: 'You do not have permission to perform this action.',
  [ErrorCodes.NOT_FOUND]: 'The requested item could not be found.',
  [ErrorCodes.RATE_LIMITED]: 'Too many requests. Please wait a moment and try again.',
  [ErrorCodes.INTERNAL_ERROR]: 'An unexpected error occurred. Please try again.',
})

const GENERIC_MESSAGE = 'The request could not be completed.'

/**
 * Normalised API error: `{ status, code, message, traceId, errors }`.
 * `status` is 0 when no HTTP response was received.
 */
export class ApiError extends Error {
  constructor({ status, code, message, traceId = null, errors = {}, extensions = {} }) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.traceId = traceId
    this.errors = errors
    this.extensions = extensions
  }
}

const STANDARD_MEMBERS = new Set([
  'type',
  'title',
  'status',
  'detail',
  'instance',
  'code',
  'traceId',
  'errors',
])

/** Extra ProblemDetails members (e.g. `customerId` on EMAIL_TAKEN). */
function extensionMembers(body) {
  return Object.fromEntries(Object.entries(body).filter(([key]) => !STANDARD_MEMBERS.has(key)))
}

function codeForStatus(status) {
  if (STATUS_CODES[status]) {
    return STATUS_CODES[status]
  }
  return status >= 500 ? ErrorCodes.INTERNAL_ERROR : ErrorCodes.UNKNOWN_ERROR
}

function normaliseFieldErrors(errors) {
  if (!errors || typeof errors !== 'object' || Array.isArray(errors)) {
    return {}
  }
  return Object.fromEntries(
    Object.entries(errors).map(([field, messages]) => [
      field,
      Array.isArray(messages) ? messages.map(String) : [String(messages)],
    ]),
  )
}

function fromNoResponse(error) {
  const code =
    error?.code === 'ECONNABORTED' || error?.code === 'ETIMEDOUT'
      ? ErrorCodes.TIMEOUT
      : ErrorCodes.NETWORK_ERROR
  return new ApiError({ status: 0, code, message: DEFAULT_MESSAGES[code] })
}

/** Converts an Axios error (or anything thrown) into an {@link ApiError}. */
export function normaliseError(error) {
  if (error instanceof ApiError) {
    return error
  }

  const response = error?.response
  if (!response) {
    return fromNoResponse(error)
  }

  const status = response.status
  const body = response.data && typeof response.data === 'object' ? response.data : {}
  const code = typeof body.code === 'string' && body.code ? body.code : codeForStatus(status)
  const message =
    (typeof body.detail === 'string' && body.detail) ||
    (typeof body.title === 'string' && body.title) ||
    DEFAULT_MESSAGES[code] ||
    GENERIC_MESSAGE

  return new ApiError({
    status,
    code,
    message,
    traceId: typeof body.traceId === 'string' ? body.traceId : null,
    errors: normaliseFieldErrors(body.errors),
    extensions: extensionMembers(body),
  })
}
