<template>
  <div class="timeline" data-test="booking-timeline">
    <div ref="scroller" class="timeline-scroll">
      <div class="timeline-grid" :style="{ width: `${LABEL_WIDTH + trackWidth}px` }">
        <div class="timeline-header row no-wrap">
          <div class="timeline-label header-label">Resource</div>
          <div class="timeline-track" :style="{ width: `${trackWidth}px` }">
            <div
              v-for="tick in ticks"
              :key="tick.at"
              class="tick"
              :style="{ left: `${tick.left}px` }"
            >
              {{ tick.label }}
            </div>
            <div v-if="nowLeft !== null" class="now-marker" :style="{ left: `${nowLeft}px` }" />
          </div>
        </div>

        <template v-for="group in groups" :key="group.id">
          <div class="timeline-section text-caption text-weight-medium text-grey-8">
            <span class="section-name">{{ group.name }}</span>
          </div>
          <div
            v-for="resource in group.resources"
            :key="resource.id"
            class="timeline-row row no-wrap"
            :class="{ 'drop-target': drag && drag.targetId === resource.id }"
            :data-resource-id="resource.id"
          >
            <div class="timeline-label">
              <div class="text-body2 ellipsis">{{ resource.name }}</div>
              <div class="text-caption text-grey-7">
                {{ resource.minCapacity }}–{{ resource.capacity }}
              </div>
            </div>
            <div
              class="timeline-track"
              :style="{ width: `${trackWidth}px` }"
              @dblclick.self="onEmptyDoubleClick($event, resource)"
            >
              <div
                v-for="closed in closedBands"
                :key="closed.left"
                class="closed-band"
                :style="{ left: `${closed.left}px`, width: `${closed.width}px` }"
              />
              <div
                v-for="tick in ticks"
                :key="tick.at"
                class="grid-line"
                :style="{ left: `${tick.left}px` }"
              />
              <template v-for="item in rowItems(resource.id)" :key="itemKey(item)">
                <div
                  v-if="item.kind === 'block'"
                  class="item block"
                  :style="placeStyle(item.block.startsAt, item.block.endsAt)"
                  :title="`Blocked: ${blockLabel(item.block)}`"
                  role="button"
                  tabindex="0"
                  :aria-label="`Blocked: ${blockLabel(item.block)}`"
                  data-test="timeline-block"
                  @click="$emit('select-block', item.block)"
                  @keydown.enter="$emit('select-block', item.block)"
                >
                  <q-icon name="block" size="14px" class="q-mr-xs" />{{ blockLabel(item.block) }}
                </div>
                <div
                  v-else
                  class="item booking"
                  :class="[
                    `status-${item.booking.status}`,
                    { dragging: drag && drag.booking.id === item.booking.id },
                  ]"
                  :style="bookingStyle(item.booking, resource.id)"
                  :title="tooltip(item.booking)"
                  role="button"
                  tabindex="0"
                  :aria-label="`${resource.name}: ${tooltip(item.booking)}`"
                  :data-test="`timeline-booking-${item.booking.id}`"
                  @pointerdown="startDrag($event, item.booking, resource.id)"
                  @keydown.enter="$emit('select', item.booking)"
                >
                  <span class="text-weight-medium">{{ item.booking.partySize }}</span>
                  <span class="q-ml-xs ellipsis">{{ customerName(item.booking.customer) }}</span>
                </div>
              </template>
            </div>
          </div>
        </template>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { formatTime } from '@/utils/dates.js'
import {
  BlockReasonLabels,
  canMove,
  customerName,
  hourTicks,
  isActiveStatus,
  itemsByResource,
  placeInterval,
  snapStart,
  statusMeta,
} from '@/utils/bookings.js'

defineOptions({ name: 'BookingTimeline' })

const props = defineProps({
  board: { type: Object, required: true },
  range: { type: Object, required: true },
  groups: { type: Array, required: true },
  pxPerMinute: { type: Number, default: 2 },
  can: { type: Function, required: true },
  showCancelled: { type: Boolean, default: false },
})
const emit = defineEmits(['select', 'select-block', 'move', 'create'])

const LABEL_WIDTH = 140
const DRAG_THRESHOLD_PX = 5

const now = ref(Date.now())
const scroller = ref(null)
let clock = null
onMounted(() => {
  clock = setInterval(() => (now.value = Date.now()), 60_000)
})

/** Brings the current time (today) or the first active booking into view when the day changes. */
function scrollToFocus() {
  if (!scroller.value) {
    return
  }
  const firstBooking = props.board.bookings
    .filter((b) => isActiveStatus(b.status))
    .map((b) => new Date(b.startsAt).getTime())
    .sort((a, b) => a - b)[0]
  const inRange = now.value >= props.range.start && now.value <= props.range.end
  const focus = inRange ? now.value : firstBooking
  if (focus === undefined) {
    scroller.value.scrollLeft = 0
    return
  }
  const left = ((focus - props.range.start) / 60_000) * props.pxPerMinute - 60 * props.pxPerMinute
  scroller.value.scrollLeft = Math.max(left, 0)
}

watch(
  () => [props.board.venueId, props.board.date, props.pxPerMinute],
  () => nextTick(scrollToFocus),
  { immediate: true },
)

const trackWidth = computed(() => props.range.minutes * props.pxPerMinute)
const ticks = computed(() => hourTicks(props.range, props.pxPerMinute, props.board.timeZone))
const rows = computed(() => itemsByResource(props.board, { showInactive: props.showCancelled }))
const nowLeft = computed(() =>
  now.value >= props.range.start && now.value <= props.range.end
    ? ((now.value - props.range.start) / 60_000) * props.pxPerMinute
    : null,
)

/** Shaded gaps outside the operating periods. */
const closedBands = computed(() => {
  const periods = [...props.board.periods].sort(
    (a, b) => new Date(a.startsAt) - new Date(b.startsAt),
  )
  const bands = []
  let cursor = props.range.start
  for (const p of periods) {
    const s = new Date(p.startsAt).getTime()
    if (s > cursor) {
      bands.push(placeInterval(cursor, s, props.range, props.pxPerMinute))
    }
    cursor = Math.max(cursor, new Date(p.endsAt).getTime())
  }
  if (cursor < props.range.end) {
    bands.push(placeInterval(cursor, props.range.end, props.range, props.pxPerMinute))
  }
  return bands
})

function rowItems(resourceId) {
  return rows.value.get(resourceId) ?? []
}

function itemKey(item) {
  return item.kind === 'block' ? `b-${item.block.id}` : `k-${item.booking.id}`
}

function placeStyle(startsAt, endsAt) {
  const { left, width } = placeInterval(startsAt, endsAt, props.range, props.pxPerMinute)
  return { left: `${left}px`, width: `${width}px` }
}

function bookingStyle(booking, resourceId) {
  const base = placeStyle(booking.startsAt, booking.endsAt)
  const style = { ...base, background: statusMeta(booking.status).fill }
  if (drag.value && drag.value.moved && drag.value.booking.id === booking.id) {
    if (drag.value.originResourceId === resourceId) {
      style.transform = `translate(${drag.value.snappedDx}px, ${drag.value.dy}px)`
      style.zIndex = 5
    } else {
      style.opacity = 0.4
    }
  }
  return style
}

function blockLabel(block) {
  return BlockReasonLabels[block.reason] ?? block.reason
}

function tooltip(booking) {
  const tz = props.board.timeZone
  return `${formatTime(booking.startsAt, tz)}–${formatTime(booking.endsAt, tz)} · ${customerName(
    booking.customer,
  )} · party of ${booking.partySize} · ${statusMeta(booking.status).label}`
}

function onEmptyDoubleClick(event, resource) {
  const rect = event.currentTarget.getBoundingClientRect()
  const minutes = (event.clientX - rect.left) / props.pxPerMinute
  const slot = props.board.slotIntervalMinutes || 15
  const startMs = props.range.start + Math.floor(minutes / slot) * slot * 60_000
  emit('create', { resourceId: resource.id, startMs })
}

const drag = ref(null)

function rowAt(clientX, clientY) {
  const element = document.elementFromPoint(clientX, clientY)
  return element?.closest?.('[data-resource-id]')?.getAttribute('data-resource-id') ?? null
}

function startDrag(event, booking, resourceId) {
  if (event.button > 0) {
    return
  }
  const movable = canMove(booking, props.can) && booking.resources.length === 1
  drag.value = reactive({
    booking,
    originResourceId: resourceId,
    targetId: resourceId,
    startX: event.clientX,
    startY: event.clientY,
    dy: 0,
    snappedDx: 0,
    moved: false,
    movable,
  })
  window.addEventListener('pointermove', onMove)
  window.addEventListener('pointerup', endDrag, { once: true })
}

function onMove(event) {
  const d = drag.value
  if (!d) {
    return
  }
  const dx = event.clientX - d.startX
  const dy = event.clientY - d.startY
  if (!d.moved && Math.hypot(dx, dy) < DRAG_THRESHOLD_PX) {
    return
  }
  if (!d.movable) {
    return
  }
  d.moved = true
  const slot = props.board.slotIntervalMinutes || 15
  const original = new Date(d.booking.startsAt).getTime()
  const next = snapStart(original, dx, props.pxPerMinute, slot)
  d.snappedDx = ((next - original) / 60_000) * props.pxPerMinute
  d.dy = dy
  d.targetId = rowAt(event.clientX, event.clientY) ?? d.originResourceId
}

function endDrag() {
  window.removeEventListener('pointermove', onMove)
  const d = drag.value
  drag.value = null
  if (!d) {
    return
  }
  if (!d.moved) {
    emit('select', d.booking)
    return
  }
  const original = new Date(d.booking.startsAt).getTime()
  const startMs = original + (d.snappedDx / props.pxPerMinute) * 60_000
  if (startMs === original && d.targetId === d.originResourceId) {
    return
  }
  emit('move', {
    booking: d.booking,
    startMs,
    resourceId: d.targetId,
    resourceChanged: d.targetId !== d.originResourceId,
    timeChanged: startMs !== original,
  })
}

onBeforeUnmount(() => {
  clearInterval(clock)
  window.removeEventListener('pointermove', onMove)
})
</script>

<style scoped>
.timeline {
  border: 1px solid #e0e0e0;
  border-radius: 4px;
  background: white;
}
.timeline-scroll {
  overflow-x: auto;
  max-height: 72vh;
  overflow-y: auto;
}
.timeline-grid {
  position: relative;
}
.timeline-header {
  position: sticky;
  top: 0;
  z-index: 3;
  background: #fafafa;
  border-bottom: 1px solid #e0e0e0;
  height: 32px;
}
.timeline-label {
  width: 140px;
  min-width: 140px;
  padding: 4px 8px;
  border-right: 1px solid #e0e0e0;
  position: sticky;
  left: 0;
  background: white;
  z-index: 2;
}
.header-label {
  background: #fafafa;
  font-size: 12px;
  color: #616161;
  line-height: 24px;
}
.timeline-track {
  position: relative;
  min-height: 44px;
}
.timeline-header .timeline-track {
  min-height: 32px;
}
.tick {
  position: absolute;
  top: 8px;
  font-size: 12px;
  color: #616161;
  transform: translateX(-50%);
  white-space: nowrap;
}
.now-marker {
  position: absolute;
  top: 0;
  bottom: -2000px;
  width: 2px;
  background: #e53935;
  z-index: 4;
  pointer-events: none;
}
.timeline-section {
  padding: 4px 0;
  background: #f5f5f5;
  border-bottom: 1px solid #eeeeee;
}
.section-name {
  display: inline-block;
  position: sticky;
  left: 8px;
  padding: 0 8px;
}
.timeline-header .timeline-track .tick:first-child {
  transform: none;
  padding-left: 4px;
}
.timeline-row {
  border-bottom: 1px solid #eeeeee;
}
.timeline-row.drop-target .timeline-track {
  background: #e3f2fd;
}
.grid-line {
  position: absolute;
  top: 0;
  bottom: 0;
  width: 1px;
  background: #f0f0f0;
  pointer-events: none;
}
.closed-band {
  position: absolute;
  top: 0;
  bottom: 0;
  background: repeating-linear-gradient(45deg, #fafafa, #fafafa 6px, #f0f0f0 6px, #f0f0f0 12px);
  pointer-events: none;
}
.item {
  position: absolute;
  top: 6px;
  height: 32px;
  border-radius: 4px;
  padding: 0 6px;
  display: flex;
  align-items: center;
  font-size: 12px;
  overflow: hidden;
  white-space: nowrap;
  cursor: pointer;
  user-select: none;
  touch-action: none;
}
.booking {
  border: 1px solid #90caf9;
  border-left-width: 4px;
}
.booking.status-SEATED {
  border-color: #66bb6a;
}
.booking.status-CHECKED_IN {
  border-color: #26a69a;
}
.booking.status-PENDING,
.booking.status-HELD {
  border-color: #ffb300;
  border-style: dashed;
}
.booking.status-COMPLETED,
.booking.status-CANCELLED,
.booking.status-NO_SHOW {
  border-color: #bdbdbd;
  color: #757575;
}
.booking.status-CANCELLED {
  text-decoration: line-through;
}
.booking.dragging {
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.25);
}
.block {
  background: repeating-linear-gradient(-45deg, #eceff1, #eceff1 6px, #cfd8dc 6px, #cfd8dc 12px);
  border: 1px solid #90a4ae;
  color: #455a64;
}
</style>
