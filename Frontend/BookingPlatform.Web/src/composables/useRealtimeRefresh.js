import { computed, onBeforeUnmount } from 'vue'
import { useRealtimeStore } from '@/stores/realtime.js'

/**
 * Calls `refresh(events)` (debounced) when realtime events pass `filter` or after a resync. `events` holds the batched
 * events; it contains `{ type: 'resync' }` when the client reconnected or woke up and must reload everything.
 * Returns `live`, true while updates are flowing (pages poll only when it is false).
 */
export function useRealtimeRefresh(refresh, { filter = () => true, debounceMs = 300 } = {}) {
  const realtime = useRealtimeStore()
  let pending = []
  let timer = null

  const flush = () => {
    const events = pending
    pending = []
    timer = null
    refresh(events)
  }
  const queue = (event) => {
    pending.push(event)
    clearTimeout(timer)
    timer = setTimeout(flush, debounceMs)
  }

  const unsubscribe = realtime.subscribe({
    onEvent: (event) => {
      if (filter(event)) {
        queue(event)
      }
    },
    onResync: () => queue({ type: 'resync' }),
  })

  onBeforeUnmount(() => {
    unsubscribe()
    clearTimeout(timer)
  })

  return { live: computed(() => realtime.isLive) }
}
