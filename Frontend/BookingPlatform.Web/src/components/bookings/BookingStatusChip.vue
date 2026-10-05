<template>
  <q-chip
    dense
    square
    :color="meta.color"
    text-color="white"
    :icon="meta.icon"
    :label="meta.label"
    class="q-ma-none"
    :data-test="`status-${status}`"
  />
</template>

<script setup>
import { computed } from 'vue'
import { statusMeta } from '@/utils/bookings.js'

defineOptions({ name: 'BookingStatusChip' })

const props = defineProps({ status: { type: String, required: true } })
const meta = computed(() => statusMeta(props.status))
</script>
