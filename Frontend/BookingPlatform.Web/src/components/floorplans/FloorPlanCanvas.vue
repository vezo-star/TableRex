<template>
  <svg
    ref="svg"
    class="floor-plan-canvas"
    :viewBox="`0 0 ${plan.width} ${plan.height}`"
    preserveAspectRatio="xMidYMid meet"
    role="img"
    :aria-label="`Floor plan ${plan.name}`"
    data-test="floor-plan-canvas"
    @pointerdown.self="$emit('select', null)"
  >
    <defs>
      <pattern id="fp-grid" :width="GRID * 5" :height="GRID * 5" patternUnits="userSpaceOnUse">
        <path
          :d="`M ${GRID * 5} 0 L 0 0 0 ${GRID * 5}`"
          fill="none"
          stroke="#e0e0e0"
          stroke-width="1"
        />
      </pattern>
    </defs>
    <rect
      x="0"
      y="0"
      :width="plan.width"
      :height="plan.height"
      fill="url(#fp-grid)"
      stroke="#bdbdbd"
      @pointerdown="$emit('select', null)"
    />

    <g
      v-for="position in positions"
      :key="position.resourceId"
      :transform="transformFor(position)"
      class="fp-shape"
      :class="{ selected: position.resourceId === selectedId, editable }"
      :data-test="`shape-${position.resourceId}`"
      @pointerdown.stop="startDrag($event, position, 'move')"
    >
      <ellipse
        v-if="position.shape === 'ROUND'"
        :cx="position.width / 2"
        :cy="position.height / 2"
        :rx="position.width / 2"
        :ry="position.height / 2"
        :fill="fillFor(position)"
        :stroke="position.resourceId === selectedId ? '#1976d2' : '#757575'"
        :stroke-width="position.resourceId === selectedId ? 3 : 1.5"
      />
      <rect
        v-else
        :width="position.width"
        :height="position.height"
        rx="6"
        :fill="fillFor(position)"
        :stroke="position.resourceId === selectedId ? '#1976d2' : '#757575'"
        :stroke-width="position.resourceId === selectedId ? 3 : 1.5"
      />
      <text
        :x="position.width / 2"
        :y="position.height / 2"
        text-anchor="middle"
        dominant-baseline="middle"
        class="fp-label"
        :transform="`rotate(${-position.rotation} ${position.width / 2} ${position.height / 2})`"
      >
        <tspan :x="position.width / 2" dy="-0.4em">{{ labelFor(position).code }}</tspan>
        <tspan :x="position.width / 2" dy="1.2em" class="fp-capacity">
          {{ labelFor(position).capacity }}
        </tspan>
      </text>
      <rect
        v-if="editable && position.resourceId === selectedId"
        :x="position.width - 7"
        :y="position.height - 7"
        width="14"
        height="14"
        fill="#1976d2"
        class="fp-resize"
        data-test="resize-handle"
        @pointerdown.stop="startDrag($event, position, 'resize')"
      />
    </g>
  </svg>
</template>

<script setup>
import { onBeforeUnmount, ref } from 'vue'
import { GRID, moveWithin, resizeWithin } from '@/utils/floorPlanGeometry.js'

defineOptions({ name: 'FloorPlanCanvas' })

const props = defineProps({
  plan: { type: Object, required: true },
  positions: { type: Array, required: true },
  resources: { type: Object, default: () => ({}) },
  selectedId: { type: String, default: null },
  editable: { type: Boolean, default: false },
})
const emit = defineEmits(['select', 'update'])

const svg = ref(null)
let drag = null

function transformFor(p) {
  return `translate(${p.x} ${p.y}) rotate(${p.rotation} ${p.width / 2} ${p.height / 2})`
}

function labelFor(p) {
  const resource = props.resources[p.resourceId]
  return {
    code: resource?.code ?? '?',
    capacity: resource ? `${resource.minCapacity}–${resource.capacity}` : '',
  }
}

function fillFor(p) {
  const resource = props.resources[p.resourceId]
  if (!resource || !resource.isActive) {
    return '#eeeeee'
  }
  return p.resourceId === props.selectedId ? '#e3f2fd' : '#ffffff'
}

/** SVG user units per screen pixel (the canvas scales to its container). */
function scale() {
  const rect = svg.value?.getBoundingClientRect()
  return rect && rect.width > 0 ? props.plan.width / rect.width : 1
}

function startDrag(event, position, mode) {
  emit('select', position.resourceId)
  if (!props.editable || event.button > 0) {
    return
  }
  drag = {
    mode,
    start: { x: event.clientX, y: event.clientY },
    origin: { ...position },
    factor: scale(),
  }
  window.addEventListener('pointermove', onMove)
  window.addEventListener('pointerup', endDrag, { once: true })
}

function onMove(event) {
  if (!drag) {
    return
  }
  const dx = (event.clientX - drag.start.x) * drag.factor
  const dy = (event.clientY - drag.start.y) * drag.factor
  const next =
    drag.mode === 'move'
      ? moveWithin(drag.origin, dx, dy, props.plan)
      : resizeWithin(drag.origin, dx, dy, props.plan)
  emit('update', next)
}

function endDrag() {
  drag = null
  window.removeEventListener('pointermove', onMove)
}

onBeforeUnmount(endDrag)
</script>

<style scoped>
.floor-plan-canvas {
  width: 100%;
  height: auto;
  max-height: 75vh;
  background: #fafafa;
  touch-action: none;
  user-select: none;
}
.fp-shape.editable {
  cursor: move;
}
.fp-resize {
  cursor: nwse-resize;
}
.fp-label {
  font-size: 16px;
  font-weight: 600;
  fill: #424242;
  pointer-events: none;
}
.fp-capacity {
  font-size: 12px;
  font-weight: 400;
  fill: #757575;
}
</style>
