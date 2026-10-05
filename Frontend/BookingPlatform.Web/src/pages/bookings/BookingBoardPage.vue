<template>
  <q-page padding>
    <div class="row items-center q-mb-md q-gutter-sm">
      <div class="text-h5 col-auto">
        Bookings
        <span v-if="venue" class="text-subtitle1 text-grey-7">· {{ venue.name }}</span>
      </div>
      <q-space />
      <q-btn
        v-if="venue && can(Permissions.RESOURCE_UPDATE)"
        outline
        no-caps
        icon="block"
        label="Block"
        data-test="board-block"
        @click="openBlock({ date })"
      />
      <q-btn
        v-if="venue && can(Permissions.BOOKING_CREATE)"
        color="primary"
        unelevated
        no-caps
        icon="add"
        label="New booking"
        data-test="board-new-booking"
        @click="openCreate({ date })"
      />
    </div>

    <NoVenueState v-if="!venueId && !venues.loading" />
    <template v-else-if="venue">
      <div class="row items-center q-col-gutter-sm q-mb-md" data-test="board-toolbar">
        <div class="col-auto row no-wrap items-center">
          <q-btn
            flat
            round
            dense
            icon="chevron_left"
            aria-label="Previous day"
            @click="shiftDay(-1)"
          />
          <q-input
            v-model="date"
            type="date"
            dense
            outlined
            style="width: 170px"
            data-test="board-date"
          />
          <q-btn flat round dense icon="chevron_right" aria-label="Next day" @click="shiftDay(1)" />
          <q-btn flat dense no-caps label="Today" :disable="date === today" @click="date = today" />
        </div>
        <div class="col-auto">
          <q-btn-toggle
            v-model="view"
            :options="viewOptions"
            no-caps
            unelevated
            dense
            toggle-color="primary"
            data-test="board-view"
          />
        </div>
        <div class="col-auto">
          <q-toggle v-model="showCancelled" dense label="Show cancelled & no-shows" />
        </div>
        <div v-if="view === 'timeline'" class="col-auto row items-center no-wrap">
          <q-icon name="zoom_in" class="q-mr-xs" />
          <q-btn-toggle
            v-model="zoom"
            :options="zoomOptions"
            dense
            flat
            no-caps
            toggle-color="primary"
          />
        </div>
        <q-space />
        <div v-if="summary" class="col-auto text-body2 text-grey-8" data-test="board-summary">
          {{ summary.bookings }} bookings · {{ summary.covers }} covers ·
          {{ summary.seatedCovers }} seated
        </div>
        <div class="col-auto">
          <q-btn
            flat
            round
            dense
            icon="refresh"
            aria-label="Refresh"
            :loading="loading"
            @click="load()"
          />
        </div>
      </div>

      <ErrorState
        v-if="error"
        :message="error.message"
        :trace-id="error.traceId"
        retryable
        @retry="load()"
      />
      <q-banner v-if="board?.truncated" rounded class="bg-amber-1 q-mb-md">
        This day has more bookings than the board can show; use the list filters to narrow it.
      </q-banner>

      <div v-if="board" class="relative-position">
        <template v-if="view === 'timeline'">
          <div v-if="!range" class="q-pa-lg text-center text-grey-7" data-test="board-closed">
            The venue is closed on {{ formatDate(board.date) }} and nothing is booked.
          </div>
          <div v-else-if="!groups.length" class="q-pa-lg text-center text-grey-7">
            This venue has no active tables or resources yet.
          </div>
          <BookingTimeline
            v-else
            :board="board"
            :range="range"
            :groups="groups"
            :px-per-minute="zoom"
            :can="can"
            :show-cancelled="showCancelled"
            @select="openBooking"
            @select-block="showBlock"
            @move="confirmMove"
            @create="onTimelineCreate"
          />
        </template>
        <BookingFloorView
          v-else-if="view === 'floor'"
          :board="board"
          :resources="resources"
          :plans="plans"
          :can-create="can(Permissions.BOOKING_CREATE)"
          @select="openBooking"
          @select-block="showBlock"
          @create="onTimelineCreate"
        />
        <BookingListView v-else :bookings="listBookings" @select="openBooking" />
      </div>
      <q-inner-loading :showing="loading && !board" />
    </template>

    <BookingDrawer
      v-if="venue"
      v-model="drawerOpen"
      :booking-id="selectedBookingId"
      :venue="venue"
      :resources="resources"
      @changed="load({ silent: true })"
    />
    <BookingCreateDialog
      v-if="venue"
      v-model="createOpen"
      :venue="venue"
      :resources="resources"
      :prefill="createPrefill"
      @created="onCreated"
    />
    <BlockDialog
      v-if="venue"
      v-model="blockOpen"
      :venue="venue"
      :resources="resources"
      :prefill="blockPrefill"
      @created="onBlockCreated"
    />
  </q-page>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useQuasar } from 'quasar'
import { useRoute, useRouter } from 'vue-router'
import BlockDialog from '@/components/bookings/BlockDialog.vue'
import BookingCreateDialog from '@/components/bookings/BookingCreateDialog.vue'
import BookingDrawer from '@/components/bookings/BookingDrawer.vue'
import BookingFloorView from '@/components/bookings/BookingFloorView.vue'
import BookingListView from '@/components/bookings/BookingListView.vue'
import BookingTimeline from '@/components/bookings/BookingTimeline.vue'
import ErrorState from '@/components/common/ErrorState.vue'
import NoVenueState from '@/components/venues/NoVenueState.vue'
import { Permissions } from '@/constants/permissions.js'
import { blockService, bookingService } from '@/services/bookingService.js'
import { venueService } from '@/services/venueService.js'
import { useAuthStore } from '@/stores/auth.js'
import { useVenuesStore } from '@/stores/venues.js'
import {
  BlockReasonLabels,
  boardWindow,
  customerName,
  groupResources,
  isActiveStatus,
  localTime,
  summariseDay,
} from '@/utils/bookings.js'
import { addDays, formatDate, formatTime, todayIso } from '@/utils/dates.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'BookingBoardPage' })

const REFRESH_MS = 60_000
const VIEWS = ['timeline', 'floor', 'list']

const $q = useQuasar()
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const venues = useVenuesStore()
const venueId = computed(() => venues.currentVenueId)
const venue = computed(() => venues.currentVenue)
const can = (permission) => auth.hasPermission(permission)

const viewOptions = [
  { label: 'Timeline', value: 'timeline', icon: 'view_timeline' },
  { label: 'Floor', value: 'floor', icon: 'grid_view' },
  { label: 'List', value: 'list', icon: 'list' },
]
const zoomOptions = [
  { label: 'S', value: 1 },
  { label: 'M', value: 2 },
  { label: 'L', value: 3 },
]

const date = ref('')
const view = ref(VIEWS.includes(route.query.view) ? route.query.view : 'timeline')
const zoom = ref(2)
const showCancelled = ref(false)
const board = ref(null)
const resources = ref([])
const sections = ref([])
const plans = ref([])
const loading = ref(false)
const error = ref(null)

const drawerOpen = ref(false)
const selectedBookingId = ref(null)
const createOpen = ref(false)
const createPrefill = ref(null)
const blockOpen = ref(false)
const blockPrefill = ref(null)

const today = computed(() => (venue.value ? todayIso(venue.value.timeZone) : ''))
const range = computed(() => boardWindow(board.value))
const groups = computed(() => groupResources(resources.value, sections.value))
const summary = computed(() => (board.value ? summariseDay(board.value.bookings) : null))
const listBookings = computed(() =>
  (board.value?.bookings ?? []).filter(
    (b) => showCancelled.value || isActiveStatus(b.status) || b.status === 'COMPLETED',
  ),
)

function shiftDay(days) {
  date.value = addDays(date.value, days)
}

let controller = null

async function load({ silent = false } = {}) {
  if (!venueId.value || !date.value) {
    return
  }
  controller?.abort()
  controller = new AbortController()
  const { signal } = controller
  if (!silent) {
    loading.value = true
  }
  try {
    board.value = await bookingService.board(venueId.value, date.value, { signal })
    error.value = null
  } catch (e) {
    if (!signal.aborted) {
      error.value = normaliseError(e)
    }
  } finally {
    if (!signal.aborted) {
      loading.value = false
    }
  }
}

async function loadVenueData() {
  board.value = null
  resources.value = []
  sections.value = []
  plans.value = []
  if (!venueId.value || !venue.value) {
    return
  }
  const queryDate =
    typeof route.query.date === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(route.query.date)
  date.value = queryDate ? route.query.date : today.value
  try {
    const [venueResources, venueSections, venuePlans] = await Promise.all([
      venueService.listAllResources(venueId.value),
      venueService.listSections(venueId.value),
      can(Permissions.FLOORPLAN_VIEW)
        ? venueService.listFloorPlans(venueId.value)
        : Promise.resolve([]),
    ])
    resources.value = venueResources
    sections.value = venueSections
    plans.value = venuePlans
  } catch (e) {
    error.value = normaliseError(e)
  }
}

function openBooking(booking) {
  selectedBookingId.value = booking.id
  drawerOpen.value = true
}

function openCreate(prefill = null) {
  createPrefill.value = prefill
  createOpen.value = true
}

function openBlock(prefill = null) {
  blockPrefill.value = prefill
  blockOpen.value = true
}

function onTimelineCreate({ resourceId, startMs }) {
  if (!can(Permissions.BOOKING_CREATE)) {
    return
  }
  openCreate({ date: board.value.date, time: localTime(startMs, board.value.timeZone), resourceId })
}

function onCreated(booking) {
  load({ silent: true })
  if (booking.businessDate !== date.value) {
    date.value = booking.businessDate
  }
}

function onBlockCreated() {
  $q.notify?.({ type: 'positive', message: 'Tables blocked' })
  load({ silent: true })
}

function showBlock(block) {
  const tz = board.value.timeZone
  const names = block.resourceIds
    .map((id) => resources.value.find((r) => r.id === id)?.name ?? '?')
    .join(', ')
  const options = {
    title: `Blocked: ${BlockReasonLabels[block.reason] ?? block.reason}`,
    message: `${names} · ${formatTime(block.startsAt, tz)}–${formatTime(block.endsAt, tz)}${
      block.notes ? ` · ${block.notes}` : ''
    }`,
    ok: { label: 'Close', flat: true, noCaps: true },
  }
  if (can(Permissions.RESOURCE_UPDATE)) {
    options.cancel = { label: 'Remove block', color: 'negative', flat: true, noCaps: true }
  }
  $q.dialog(options).onCancel(async () => {
    try {
      await blockService.cancel(block.id)
      $q.notify?.({ type: 'positive', message: 'Block removed' })
      load({ silent: true })
    } catch (e) {
      $q.notify?.({ type: 'negative', message: normaliseError(e).message })
    }
  })
}

function confirmMove({ booking, startMs, resourceId, resourceChanged, timeChanged }) {
  const tz = board.value.timeZone
  const target = resources.value.find((r) => r.id === resourceId)
  const parts = []
  if (timeChanged) {
    parts.push(`to ${localTime(startMs, tz)}`)
  }
  if (resourceChanged) {
    parts.push(`to ${target?.name ?? 'another table'}`)
  }
  $q.dialog({
    title: 'Move booking',
    message: `Move ${customerName(booking.customer)} (${booking.time}, party of ${booking.partySize}) ${parts.join(' and ')}?`,
    cancel: { label: 'Cancel', flat: true, noCaps: true },
    ok: { label: 'Move', color: 'primary', unelevated: true, noCaps: true },
  }).onOk(async () => {
    try {
      await bookingService.move(booking.id, {
        date: timeChanged ? board.value.date : null,
        time: timeChanged ? localTime(startMs, tz) : null,
        allocation: resourceChanged ? { mode: 'RESOURCES', resourceIds: [resourceId] } : null,
        version: booking.version,
      })
      $q.notify?.({ type: 'positive', message: 'Booking moved' })
    } catch (e) {
      $q.notify?.({ type: 'negative', message: normaliseError(e).message })
    } finally {
      load({ silent: true })
    }
  })
}

watch(() => venue.value?.id, loadVenueData, { immediate: true })
watch([venueId, date], () => load(), { immediate: true })
watch([date, view], () => {
  const query = {
    ...route.query,
    date: date.value || undefined,
    view: view.value === 'timeline' ? undefined : view.value,
  }
  if (query.date !== route.query.date || query.view !== route.query.view) {
    router.replace({ query })
  }
})

let timer = null
onMounted(() => {
  timer = setInterval(() => {
    if (!document.hidden && !drawerOpen.value && !createOpen.value) {
      load({ silent: true })
    }
  }, REFRESH_MS)
})
onBeforeUnmount(() => {
  clearInterval(timer)
  controller?.abort()
})
</script>
