import { defineComponent, h } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { useRealtimeRefresh } from '@/composables/useRealtimeRefresh.js'
import { useRealtimeStore } from '@/stores/realtime.js'

function mountWith(refresh, options) {
  const pinia = createPinia()
  setActivePinia(pinia)
  let api
  const Probe = defineComponent({
    setup() {
      api = useRealtimeRefresh(refresh, options)
      return () => h('div')
    },
  })
  const wrapper = mount(Probe, { global: { plugins: [pinia] } })
  return { wrapper, api, realtime: useRealtimeStore() }
}

describe('useRealtimeRefresh', () => {
  afterEach(() => vi.useRealTimers())

  it('batches matching events and resyncs into one debounced refresh', async () => {
    vi.useFakeTimers()
    const refresh = vi.fn()
    const { realtime } = mountWith(refresh, {
      filter: (e) => e.type === 'BookingCreated',
      debounceMs: 200,
    })
    realtime.venueId = 'v1'

    realtime.receive('BookingCreated', { eventId: 'e1', venueId: 'v1' })
    realtime.receive('HoldCreated', { eventId: 'e2', venueId: 'v1' })
    realtime.resync()
    vi.advanceTimersByTime(199)
    expect(refresh).not.toHaveBeenCalled()

    vi.advanceTimersByTime(1)
    expect(refresh).toHaveBeenCalledOnce()
    expect(refresh.mock.calls[0][0].map((e) => e.type)).toEqual(['BookingCreated', 'resync'])
  })

  it('stops listening when the component unmounts and exposes the live flag', () => {
    vi.useFakeTimers()
    const refresh = vi.fn()
    const { wrapper, api, realtime } = mountWith(refresh)
    expect(api.live.value).toBe(false)
    realtime.status = 'connected'
    expect(api.live.value).toBe(true)
    realtime.status = 'reconnecting'
    expect(api.live.value).toBe(false)

    wrapper.unmount()
    realtime.resync()
    vi.runAllTimers()
    expect(refresh).not.toHaveBeenCalled()
  })
})
