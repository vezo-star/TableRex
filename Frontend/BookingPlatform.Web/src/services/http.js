import axios from 'axios'
import { HttpHeaders } from '@/constants/httpHeaders.js'
import { normaliseError } from '@/utils/problemDetails.js'

export const DEFAULT_BASE_URL = '/api'
export const DEFAULT_TIMEOUT_MS = 15000

/** 32 hex chars; satisfies the API's correlation id format `[A-Za-z0-9._-]{1,64}`. */
export function newCorrelationId() {
  return crypto.randomUUID().replaceAll('-', '')
}

/**
 * Auth hooks wired by boot/auth.js (keeps this module free of store imports).
 * - getAccessToken(): current in-memory access token or null
 * - refreshAccessToken(): resolves a fresh token or null (never throws)
 * - onUnauthorized(): called when a 401 cannot be recovered by refreshing
 */
export function createAuthBridge() {
  return {
    getAccessToken: () => null,
    refreshAccessToken: async () => null,
    onUnauthorized: () => {},
  }
}

export function createHttpClient({
  baseURL = import.meta.env.VITE_API_BASE_URL || DEFAULT_BASE_URL,
  timeout = DEFAULT_TIMEOUT_MS,
  adapter,
  auth = createAuthBridge(),
} = {}) {
  const client = axios.create({
    baseURL,
    timeout,
    withCredentials: true,
    headers: { Accept: 'application/json' },
    ...(adapter ? { adapter } : {}),
  })

  let refreshInFlight = null
  const refreshOnce = () => {
    refreshInFlight ??= Promise.resolve()
      .then(() => auth.refreshAccessToken())
      .catch(() => null)
      .finally(() => {
        refreshInFlight = null
      })
    return refreshInFlight
  }

  client.interceptors.request.use((config) => {
    if (!config.headers.has(HttpHeaders.CORRELATION_ID)) {
      config.headers.set(HttpHeaders.CORRELATION_ID, newCorrelationId())
    }
    const token = config.skipAuth ? null : auth.getAccessToken()
    if (token && !config.headers.has('Authorization')) {
      config.headers.set('Authorization', `Bearer ${token}`)
    }
    return config
  })

  client.interceptors.response.use(
    (response) => response,
    async (error) => {
      if (axios.isCancel(error)) {
        return Promise.reject(error)
      }

      const config = error?.config
      const canRefresh =
        error?.response?.status === 401 && config && !config.skipAuthRefresh && !config._retried

      if (canRefresh) {
        const token = await refreshOnce()
        if (token) {
          config._retried = true
          config.headers.set('Authorization', `Bearer ${token}`)
          return client.request(config)
        }
        auth.onUnauthorized()
      }

      return Promise.reject(normaliseError(error))
    },
  )

  return client
}

/** Shared bridge mutated by boot/auth.js. */
export const authBridge = createAuthBridge()

/** The single Axios instance. Components use services, never this directly. */
export const http = createHttpClient({ auth: authBridge })
