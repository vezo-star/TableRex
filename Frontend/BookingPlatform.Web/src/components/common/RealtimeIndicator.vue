<template>
  <q-chip
    dense
    square
    :clickable="state.retryable"
    :icon="state.icon"
    :color="state.color"
    text-color="white"
    class="q-mr-sm"
    role="status"
    :aria-label="state.description"
    data-test="realtime-indicator"
    :data-status="realtime.status"
    @click="state.retryable && realtime.reconnectNow()"
  >
    {{ state.label }}
    <q-tooltip>{{ state.description }}</q-tooltip>
  </q-chip>
</template>

<script setup>
import { computed } from 'vue'
import { RealtimeStatus } from '@/constants/realtimeEvents.js'
import { useRealtimeStore } from '@/stores/realtime.js'

defineOptions({ name: 'RealtimeIndicator' })

const realtime = useRealtimeStore()

const state = computed(() => {
  if (realtime.isLive) {
    return {
      label: 'Live',
      icon: 'sensors',
      color: 'positive',
      description: 'Live updates are on: changes made elsewhere appear automatically.',
    }
  }
  if (
    realtime.status === RealtimeStatus.CONNECTING ||
    realtime.status === RealtimeStatus.RECONNECTING
  ) {
    return {
      label: 'Reconnecting…',
      icon: 'sync',
      color: 'warning',
      description: 'Live updates are paused while reconnecting; the page refreshes once connected.',
    }
  }
  return {
    label: 'Live updates paused',
    icon: 'sensors_off',
    color: 'grey-7',
    description:
      'Live updates are paused. Pages refresh every minute; click to try reconnecting now.',
    retryable: realtime.status === RealtimeStatus.DISCONNECTED,
  }
})
</script>
