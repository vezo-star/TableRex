import { defineStore } from 'pinia'
import { authService } from '@/services/authService.js'

const REFRESH_LOCK = 'bp-auth-refresh'
const CHANNEL = 'bp-auth'
// Refresh this long before the access token expires.
const REFRESH_LEAD_MS = 60_000

/**
 * Serialises refreshes across tabs: rotation makes a refresh token single-use, so two tabs refreshing with the same
 * cookie would look like token theft and revoke the session. Falls back to no lock where Web Locks are unavailable.
 */
function withRefreshLock(task) {
  if (typeof navigator !== 'undefined' && navigator.locks?.request) {
    return navigator.locks.request(REFRESH_LOCK, task)
  }
  return task()
}

function createChannel() {
  return typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(CHANNEL)
}

export const useAuthStore = defineStore('auth', {
  state: () => ({
    accessToken: null,
    expiresAt: null,
    userId: null,
    tenantId: null,
    memberships: [],
    me: null,
    initialized: false,
  }),

  getters: {
    isAuthenticated: (state) => Boolean(state.accessToken),
    hasTenant: (state) => Boolean(state.tenantId),
    requiresTenantSelection: (state) =>
      Boolean(state.accessToken) && !state.tenantId && state.memberships.length > 0,
    permissions: (state) => new Set(state.me?.permissions ?? []),
    roles: (state) => state.me?.roles ?? [],
    currentTenant: (state) => state.memberships.find((m) => m.tenantId === state.tenantId) ?? null,
    displayName: (state) => state.me?.displayName ?? '',
    isPlatformAdmin: (state) => Boolean(state.me?.isPlatformAdmin),
    hasAllVenueAccess: (state) => Boolean(state.me?.hasAllVenueAccess),
  },

  actions: {
    hasPermission(permission) {
      return this.permissions.has(permission)
    },

    hasAnyPermission(permissions) {
      return permissions.some((p) => this.permissions.has(p))
    },

    applySession(session) {
      this.accessToken = session.accessToken
      this.expiresAt = session.expiresAt ? Date.parse(session.expiresAt) : null
      this.userId = session.userId
      this.tenantId = session.tenantId ?? null
      this.memberships = session.memberships ?? []
      this.scheduleRefresh()
    },

    async loadMe() {
      this.me = await authService.me()
      return this.me
    },

    async login(email, password) {
      const session = await authService.login(email, password)
      this.applySession(session)
      await this.loadMe()
      this.initialized = true
      return session
    },

    /** Resolves a fresh access token or null; never throws. Concurrent callers share one request. */
    refresh() {
      this._refreshing ??= withRefreshLock(async () => {
        try {
          const session = await authService.refresh()
          this.applySession(session)
          return session.accessToken
        } catch {
          this.clear()
          return null
        }
      }).finally(() => {
        this._refreshing = null
      })
      return this._refreshing
    },

    /** Silent sign-in from the refresh cookie on first navigation. */
    async initialize() {
      if (this.initialized) {
        return
      }
      this._initializing ??= (async () => {
        this.listen()
        const token = await this.refresh()
        if (token) {
          await this.loadMe().catch(() => this.clear())
        }
        this.initialized = true
      })()
      await this._initializing
    },

    async switchTenant(tenantId) {
      const session = await authService.switchTenant(tenantId)
      this.applySession(session)
      await this.loadMe()
      this.broadcast('tenant-switched')
    },

    async changePassword(currentPassword, newPassword) {
      const session = await authService.changePassword(currentPassword, newPassword)
      this.applySession(session)
    },

    async logout() {
      try {
        await authService.logout()
      } catch {
        // The session is cleared locally even if the server is unreachable.
      }
      this.clear()
      this.broadcast('logout')
    },

    clear() {
      clearTimeout(this._refreshTimer)
      this.accessToken = null
      this.expiresAt = null
      this.userId = null
      this.tenantId = null
      this.memberships = []
      this.me = null
    },

    scheduleRefresh() {
      clearTimeout(this._refreshTimer)
      if (!this.expiresAt) {
        return
      }
      const delay = Math.max(this.expiresAt - Date.now() - REFRESH_LEAD_MS, 5_000)
      this._refreshTimer = setTimeout(() => this.refresh(), delay)
    },

    listen() {
      if (this._channel !== undefined) {
        return
      }
      this._channel = createChannel()
      this._channel?.addEventListener('message', (event) => {
        if (event.data === 'logout') {
          this.clear()
          this.onSignedOut?.()
        } else if (event.data === 'tenant-switched') {
          // Another tab changed tenant; adopt it on the next refresh so both tabs agree.
          this.refresh().then((token) => (token ? this.loadMe() : this.onSignedOut?.()))
        }
      })
    },

    broadcast(message) {
      this._channel?.postMessage(message)
    },
  },
})
