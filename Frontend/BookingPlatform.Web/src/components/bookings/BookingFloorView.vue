<template>
  <div data-test="booking-floor">
    <div v-if="!plans.length" class="q-pa-lg text-center text-grey-7" data-test="floor-empty">
      No floor plan for this venue yet. Create one under Floor plans to use this view.
    </div>
    <div v-else-if="!range" class="q-pa-lg text-center text-grey-7">
      The venue is closed on this date.
    </div>
    <div v-else class="row q-col-gutter-md">
      <div class="col-12 col-md-8">
        <div class="row items-center q-gutter-sm q-mb-sm">
          <q-select
            v-if="plans.length > 1"
            v-model="planId"
            :options="plans.map((p) => ({ label: p.name, value: p.id }))"
            emit-value
            map-options
            dense
            outlined
            style="min-width: 160px"
          />
          <div class="text-subtitle2" data-test="floor-time">
            {{ localTime(at, board.timeZone) }}
          </div>
          <q-slider
            v-model="at"
            class="col"
            :min="range.start"
            :max="range.end - stepMs"
            :step="stepMs"
            :label-value="localTime(at, board.timeZone)"
            label
            data-test="floor-slider"
          />
          <q-btn
            flat
            dense
            no-caps
            icon="schedule"
            label="Now"
            :disable="!nowInRange"
            @click="at = snappedNow"
          />
        </div>
        <div class="floor-canvas">
          <FloorPlanCanvas
            v-if="plan"
            :plan="plan"
            :positions="positions"
            :resources="resourceMap"
            :selected-id="selectedResourceId"
            :fills="fills"
            @select="selectedResourceId = $event"
          />
        </div>
        <div class="row q-gutter-md q-mt-sm text-caption">
          <div v-for="item in legend" :key="item.label" class="row items-center no-wrap">
            <span class="legend-swatch" :style="{ background: item.fill }" />{{ item.label }}
          </div>
        </div>
      </div>

      <div class="col-12 col-md-4">
        <q-card v-if="selectedResource" flat bordered data-test="floor-details">
          <q-card-section>
            <div class="text-subtitle1">{{ selectedResource.name }}</div>
            <div class="text-caption text-grey-7">
              Seats {{ selectedResource.minCapacity }}–{{ selectedResource.capacity }}
            </div>
          </q-card-section>
          <q-separator />
          <q-card-section>
            <template v-if="occupant?.kind === 'booking'">
              <div class="text-caption text-grey-7">At {{ localTime(at, board.timeZone) }}</div>
              <div class="row items-center q-gutter-sm">
                <div class="text-body1">{{ customerName(occupant.booking.customer) }}</div>
                <BookingStatusChip :status="occupant.booking.status" />
              </div>
              <div class="text-body2">
                {{ occupant.booking.time }}–{{
                  localTime(new Date(occupant.booking.endsAt).getTime(), board.timeZone)
                }}
                · {{ occupant.booking.partySize }} guests
              </div>
              <q-btn
                class="q-mt-sm"
                color="primary"
                unelevated
                no-caps
                label="Open booking"
                @click="$emit('select', occupant.booking)"
              />
            </template>
            <template v-else-if="occupant?.kind === 'block'">
              <div class="text-body1">
                Blocked: {{ BlockReasonLabels[occupant.block.reason] ?? occupant.block.reason }}
              </div>
              <q-btn
                class="q-mt-sm"
                flat
                no-caps
                label="Details"
                @click="$emit('select-block', occupant.block)"
              />
            </template>
            <template v-else>
              <div class="text-body1 text-positive">
                Free at {{ localTime(at, board.timeZone) }}
              </div>
              <q-btn
                v-if="canCreate"
                class="q-mt-sm"
                color="primary"
                unelevated
                no-caps
                icon="add"
                label="New booking here"
                data-test="floor-new-booking"
                @click="$emit('create', { resourceId: selectedResource.id, startMs: at })"
              />
            </template>
          </q-card-section>
          <q-separator />
          <q-list dense>
            <q-item-label header>Today on this table</q-item-label>
            <q-item v-if="!tableDay.length">
              <q-item-section class="text-grey-7">Nothing booked</q-item-section>
            </q-item>
            <q-item v-for="b in tableDay" :key="b.id" clickable @click="$emit('select', b)">
              <q-item-section>
                <q-item-label>{{ b.time }} · {{ customerName(b.customer) }}</q-item-label>
                <q-item-label caption>{{ b.partySize }} guests</q-item-label>
              </q-item-section>
              <q-item-section side><BookingStatusChip :status="b.status" /></q-item-section>
            </q-item>
          </q-list>
        </q-card>
        <div v-else class="text-grey-7 q-pa-md">Select a table to see who is there.</div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import BookingStatusChip from '@/components/bookings/BookingStatusChip.vue'
import FloorPlanCanvas from '@/components/floorplans/FloorPlanCanvas.vue'
import {
  BlockReasonLabels,
  boardWindow,
  customerName,
  isActiveStatus,
  localTime,
  occupancyAt,
  StatusMeta,
} from '@/utils/bookings.js'

defineOptions({ name: 'BookingFloorView' })

const props = defineProps({
  board: { type: Object, required: true },
  resources: { type: Array, default: () => [] },
  plans: { type: Array, default: () => [] },
  canCreate: { type: Boolean, default: false },
  now: { type: Number, default: () => Date.now() },
})
defineEmits(['select', 'select-block', 'create'])

const FREE_FILL = '#ffffff'
const BLOCK_FILL = '#bdbdbd'
const FLOOR_FILLS = {
  PENDING: '#ffe082',
  HELD: '#ffccbc',
  CONFIRMED: '#90caf9',
  CHECKED_IN: '#80cbc4',
  SEATED: '#a5d6a7',
}

const planId = ref(null)
const selectedResourceId = ref(null)
const at = ref(0)

const range = computed(() => boardWindow(props.board))
const stepMs = computed(() => (props.board.slotIntervalMinutes || 15) * 60_000)
const plan = computed(
  () => props.plans.find((p) => p.id === planId.value) ?? props.plans[0] ?? null,
)
const positions = computed(() =>
  (plan.value?.positions ?? []).map((p) => ({
    resourceId: p.resourceId,
    x: Number(p.x),
    y: Number(p.y),
    width: Number(p.width),
    height: Number(p.height),
    rotation: Number(p.rotation),
    shape: p.shape,
  })),
)
const resourceMap = computed(() => Object.fromEntries(props.resources.map((r) => [r.id, r])))
const occupancy = computed(() => occupancyAt(props.board, at.value))
const fills = computed(() => {
  const result = {}
  for (const [resourceId, item] of occupancy.value) {
    result[resourceId] =
      item.kind === 'block' ? BLOCK_FILL : (FLOOR_FILLS[item.booking.status] ?? FREE_FILL)
  }
  return result
})
const selectedResource = computed(() => resourceMap.value[selectedResourceId.value] ?? null)
const occupant = computed(() => occupancy.value.get(selectedResourceId.value) ?? null)
const tableDay = computed(() =>
  props.board.bookings
    .filter(
      (b) =>
        (isActiveStatus(b.status) || b.status === 'COMPLETED') &&
        b.resources.some((r) => r.resourceId === selectedResourceId.value),
    )
    .sort((a, b) => new Date(a.startsAt) - new Date(b.startsAt)),
)
const snappedNow = computed(() => {
  if (!range.value) {
    return 0
  }
  const offset = Math.floor((props.now - range.value.start) / stepMs.value) * stepMs.value
  return range.value.start + offset
})
const nowInRange = computed(
  () => !!range.value && props.now >= range.value.start && props.now < range.value.end,
)
const legend = [
  { label: 'Free', fill: FREE_FILL },
  ...['CONFIRMED', 'CHECKED_IN', 'SEATED', 'PENDING'].map((s) => ({
    label: StatusMeta[s].label,
    fill: FLOOR_FILLS[s],
  })),
  { label: 'Blocked', fill: BLOCK_FILL },
]

watch(
  () => [props.board.date, props.board.venueId],
  () => {
    if (!range.value) {
      return
    }
    const firstOpen = props.board.periods[0]
      ? new Date(props.board.periods[0].startsAt).getTime()
      : range.value.start
    at.value = nowInRange.value ? snappedNow.value : Math.max(firstOpen, range.value.start)
  },
  { immediate: true },
)
</script>

<style scoped>
.floor-canvas {
  border: 1px solid #e0e0e0;
  border-radius: 4px;
  background: #fafafa;
  height: 60vh;
  min-height: 320px;
}
.floor-canvas :deep(svg) {
  width: 100%;
  height: 100%;
}
.legend-swatch {
  display: inline-block;
  width: 14px;
  height: 14px;
  border: 1px solid #9e9e9e;
  border-radius: 3px;
  margin-right: 4px;
}
</style>
