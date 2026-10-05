import axios from 'axios'
import { HttpHeaders } from '@/constants/httpHeaders.js'
import { normaliseError } from '@/utils/problemDetails.js'

export const DEFAULT_BASE_URL = '/api'
export const DEFAULT_TIMEOUT_MS = 15000

/** 32 hex chars; satisfies the API's correlation id format `[A-Za-z0-9._-]{1,64}`. */
export function newCorrelationId() {
  return crypto.randomUUID().replaceAll('-', '')
}

export function createHttpClient({
  baseURL = import.meta.env.VITE_API_BASE_URL || DEFAULT_BASE_URL,
  timeout = DEFAULT_TIMEOUT_MS,
  adapter,
} = {}) {
  const client = axios.create({
    baseURL,
    timeout,
    headers: { Accept: 'application/json' },
    ...(adapter ? { adapter } : {}),
  })

  client.interceptors.request.use((config) => {
    if (!config.headers.has(HttpHeaders.CORRELATION_ID)) {
      config.headers.set(HttpHeaders.CORRELATION_ID, newCorrelationId())
    }
    return config
  })

  client.interceptors.response.use(
    (response) => response,
    (error) => Promise.reject(axios.isCancel(error) ? error : normaliseError(error)),
  )

  return client
}

/** The single Axios instance. Components use services, never this directly. */
export const http = createHttpClient()
