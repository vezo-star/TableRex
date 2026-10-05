import { markRaw } from 'vue'
import { defineStore } from 'pinia'
import { REALTIME_EVENT_NAMES, RealtimeStatus } from '@/constants/realtimeEvents.js'
import { createVenueConnection } from '@/services/realtimeService.js'
import { useAuthStore } from '@/stores/auth.js'

/** Manual retry schedule once SignalR's automatic reconnect gives up (or the first start fails). */
export const RETRY_DELAYS_MS = Object.freeze([2_000, 5_000, 10_000, 30_000, 60_000])
/** A tab hidden at least this long resynchronises when shown, even if the socket reports connected. */
export const RESYNC_AFTER_HIDDEN_MS = 60_000
const TOKEN_REFRESH_LEAD_MS = 30_000
const SEEN_LIMIT = 500

/**
 * Owns the venue hub connection (REALTIME.md §6). Events only say *what* changed; listeners refetch through the API.
 * After every (re)connect the current venue is re-joined and listeners are told to resync, because events sent while
 * disconnected are lost.
 *
 * Listeners: `subscribe({ onEvent(event), onResync() })` returns an unsubscribe function.
 */
export const useRealtimeStore = defineStore('realtime', {
  state: () => ({
    status: RealtimeStatus.DISCONNECTED,
    venueId: null,
    joinedVenueId: null,
    lastEventAt: null,
    lastResyncAt: null,
  }),

  getters: {
    /** True when updates for the selected venue are flowing. */
    isLive: (state) =>
      state.status === RealtimeStatus.CONNECTED &&
      (!state.venueId || state.joinedVenueId === state.venueId),
  },

  actions: {
    subscribe(listener) {
      this._listeners ??= markRaw(new Set())
      this._listeners.add(listener)
      return () => this._listeners.delete(listener)
    },

    notify(method, argument) {
      for (const listener of [...(this._listeners ?? [])]) {
        try {
          listener[method]?.(argument)
        } catch (e) {
          console.error('Realtime listener failed', e)
        }
      }
    },

    async start() {
      const auth = useAuthStore()
      if (this._connection || !auth.isAuthenticated || !auth.hasTenant) {
        return
      }
      this._stopped = false
      this._attempt = 0
      const connection = createVenueConnection({ accessTokenFactory: () => this.accessToken() })
      for (const name of REALTIME_EVENT_NAMES) {
        connection.on(name, (payload) => this.receive(name, payload))
      }
      connection.onreconnecting(() => {
        this.status = RealtimeStatus.RECONNECTING
        this.joinedVenueId = null
      })
      connection.onreconnected(() => this.afterConnect())
      connection.onclose(() => {
        if (this._connection !== connection) {
          return
        }
        this.status = RealtimeStatus.DISCONNECTED
        this.joinedVenueId = null
        if (!this._stopped) {
          this.scheduleRetry()
        }
      })
      // Raw: the store is reactive, and a proxied connection would never equal the one its callbacks close over.
      this._connection = markRaw(connection)
      await this.connect()
    },

    async accessToken() {
      const auth = useAuthStore()
      if (auth.expiresAt && auth.expiresAt - Date.now() < TOKEN_REFRESH_LEAD_MS) {
        await auth.refresh()
      }
      return auth.accessToken ?? ''
    },

    async connect() {
      const connection = this._connection
      if (!connection || this._stopped) {
        return
      }
      clearTimeout(this._retryTimer)
      this.status = RealtimeStatus.CONNECTING
      try {
        await connection.start()
      } catch {
        if (this._connection === connection && !this._stopped) {
          this.status = RealtimeStatus.DISCONNECTED
          this.scheduleRetry()
        }
        return
      }
      if (this._connection === connection) {
        await this.afterConnect()
      }
    },

    async afterConnect() {
      this._attempt = 0
      await this.join(this.venueId)
      this.status = RealtimeStatus.CONNECTED
      this.resync()
    },

    async join(venueId) {
      const connection = this._connection
      if (!connection || !venueId) {
        return
      }
      try {
        await connection.invoke('JoinVenue', venueId)
        this.joinedVenueId = venueId
      } catch {
        // Denied or unavailable: the indicator shows live updates as paused and pages keep polling.
        this.joinedVenueId = null
      }
    },

    async setVenue(venueId) {
      const next = venueId ?? null
      if (next === this.venueId) {
        return
      }
      const previous = this.joinedVenueId
      this.venueId = next
      if (this.status !== RealtimeStatus.CONNECTED) {
        return
      }
      if (previous) {
        this.joinedVenueId = null
        try {
          await this._connection.invoke('LeaveVenue', previous)
        } catch {
          // The connection is going away anyway; the server drops its groups with it.
        }
      }
      await this.join(next)
    },

    scheduleRetry() {
      clearTimeout(this._retryTimer)
      const attempt = this._attempt ?? 0
      const delay = RETRY_DELAYS_MS[Math.min(attempt, RETRY_DELAYS_MS.length - 1)]
      this._attempt = attempt + 1
      this._retryTimer = setTimeout(() => this.connect(), delay)
    },

    reconnectNow() {
      if (this._connection && this.status === RealtimeStatus.DISCONNECTED) {
        this._attempt = 0
        this.connect()
      }
    },

    async stop() {
      this._stopped = true
      clearTimeout(this._retryTimer)
      const connection = this._connection
      this._connection = null
      this.status = RealtimeStatus.DISCONNECTED
      this.joinedVenueId = null
      if (connection) {
        try {
          await connection.stop()
        } catch {
          // Already closed.
        }
      }
    },

    async restart() {
      await this.stop()
      await this.start()
    },

    receive(type, payload) {
      if (!payload || payload.venueId !== this.venueId) {
        return
      }
      if (payload.eventId) {
        this._seen ??= markRaw(new Set())
        if (this._seen.has(payload.eventId)) {
          return
        }
        this._seen.add(payload.eventId)
        if (this._seen.size > SEEN_LIMIT) {
          this._seen.delete(this._seen.values().next().value)
        }
      }
      this.lastEventAt = Date.now()
      this.notify('onEvent', { ...payload, type })
    },

    resync() {
      this.lastResyncAt = Date.now()
      this.notify('onResync')
    },

    visibilityChanged(hidden) {
      if (hidden) {
        this._hiddenAt = Date.now()
        return
      }
      const hiddenFor = this._hiddenAt ? Date.now() - this._hiddenAt : 0
      this._hiddenAt = null
      if (this.status === RealtimeStatus.DISCONNECTED && this._connection) {
        this.reconnectNow()
      } else if (hiddenFor >= RESYNC_AFTER_HIDDEN_MS) {
        this.resync()
      }
    },
  },
})
