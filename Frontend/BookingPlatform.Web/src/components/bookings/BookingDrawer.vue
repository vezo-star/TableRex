<template>
  <q-dialog
    :model-value="modelValue"
    position="right"
    full-height
    @update:model-value="$emit('update:modelValue', $event)"
  >
    <q-card class="booking-drawer column no-wrap" data-test="booking-drawer">
      <q-card-section class="row items-center q-pb-sm">
        <div class="col">
          <div class="text-h6" data-test="drawer-reference">{{ booking?.reference ?? '…' }}</div>
          <div v-if="booking" class="text-caption text-grey-7">
            {{ SourceLabels[booking.source] ?? booking.source }} · created
            {{ formatInstant(booking.createdAt) }}
          </div>
        </div>
        <BookingStatusChip v-if="booking" :status="booking.status" class="q-mr-sm" />
        <q-btn v-close-popup flat round dense icon="close" aria-label="Close" />
      </q-card-section>
      <q-separator />

      <q-banner
        v-if="remoteChange"
        dense
        class="bg-amber-1 text-body2"
        data-test="drawer-remote-change"
      >
        This booking was changed elsewhere. Reload to see the latest version (unsaved changes here
        are discarded).
        <template #action>
          <q-btn flat dense no-caps color="primary" label="Reload" @click="discardAndReload" />
        </template>
      </q-banner>

      <div v-if="loadError" class="q-pa-md">
        <ErrorState
          :message="loadError.message"
          :trace-id="loadError.traceId"
          retryable
          @retry="load"
        />
      </div>
      <q-inner-loading :showing="loading && !booking" />

      <template v-if="booking">
        <q-tabs v-model="tab" dense align="left" class="text-grey-8" active-color="primary" no-caps>
          <q-tab name="details" label="Details" />
          <q-tab name="history" label="History" data-test="tab-history" />
        </q-tabs>
        <q-separator />

        <q-scroll-area class="col">
          <q-tab-panels v-model="tab" animated>
            <q-tab-panel name="details" class="q-pa-md">
              <div v-if="actions.length" class="row q-gutter-sm q-mb-md" data-test="status-actions">
                <q-btn
                  v-for="action in actions"
                  :key="action.key"
                  :label="action.label"
                  :icon="action.icon"
                  :color="
                    action.key === 'cancel' || action.key === 'noShow' ? 'negative' : 'primary'
                  "
                  :outline="action.key === 'cancel' || action.key === 'noShow'"
                  unelevated
                  no-caps
                  dense
                  class="q-px-sm"
                  :loading="busy === action.key"
                  :disable="!!busy"
                  :data-test="`action-${action.key}`"
                  @click="runAction(action)"
                />
              </div>

              <template v-if="mode === 'view'">
                <q-list dense class="q-mb-md">
                  <q-item>
                    <q-item-section avatar><q-icon name="schedule" /></q-item-section>
                    <q-item-section>
                      <q-item-label>
                        {{ formatDate(booking.businessDate) }} · {{ booking.time }}–{{
                          formatTime(booking.endsAt, booking.timeZone)
                        }}
                      </q-item-label>
                      <q-item-label caption>{{
                        formatDuration(booking.durationMinutes)
                      }}</q-item-label>
                    </q-item-section>
                  </q-item>
                  <q-item>
                    <q-item-section avatar><q-icon name="group" /></q-item-section>
                    <q-item-section>Party of {{ booking.partySize }}</q-item-section>
                  </q-item>
                  <q-item>
                    <q-item-section avatar><q-icon name="table_restaurant" /></q-item-section>
                    <q-item-section data-test="drawer-resources">{{
                      resourceNames(booking)
                    }}</q-item-section>
                  </q-item>
                  <q-item
                    :clickable="!!booking.customer && canViewCustomers"
                    :to="
                      booking.customer && canViewCustomers
                        ? {
                            name: RouteNames.CUSTOMER_DETAILS,
                            params: { customerId: booking.customer.id },
                          }
                        : undefined
                    "
                  >
                    <q-item-section avatar><q-icon name="person" /></q-item-section>
                    <q-item-section>
                      <q-item-label data-test="drawer-customer">{{
                        customerName(booking.customer)
                      }}</q-item-label>
                      <q-item-label v-if="booking.customer?.phone" caption>
                        {{ booking.customer.phone }}
                      </q-item-label>
                      <q-item-label v-if="booking.customer?.email" caption>
                        {{ booking.customer.email }}
                      </q-item-label>
                    </q-item-section>
                  </q-item>
                  <q-item v-if="booking.specialRequests">
                    <q-item-section avatar><q-icon name="star_outline" /></q-item-section>
                    <q-item-section>
                      <q-item-label caption>Special requests</q-item-label>
                      <q-item-label class="pre-wrap">{{ booking.specialRequests }}</q-item-label>
                    </q-item-section>
                  </q-item>
                  <q-item v-if="booking.internalNotes">
                    <q-item-section avatar><q-icon name="sticky_note_2" /></q-item-section>
                    <q-item-section>
                      <q-item-label caption>Internal notes</q-item-label>
                      <q-item-label class="pre-wrap">{{ booking.internalNotes }}</q-item-label>
                    </q-item-section>
                  </q-item>
                  <q-item v-if="booking.cancellationReason">
                    <q-item-section avatar><q-icon name="info" /></q-item-section>
                    <q-item-section>
                      <q-item-label caption>Cancellation reason</q-item-label>
                      <q-item-label>{{ booking.cancellationReason }}</q-item-label>
                    </q-item-section>
                  </q-item>
                </q-list>
                <div class="row q-gutter-sm">
                  <q-btn
                    v-if="editable"
                    outline
                    no-caps
                    icon="edit"
                    label="Edit"
                    data-test="drawer-edit"
                    @click="startEdit"
                  />
                  <q-btn
                    v-if="movable"
                    outline
                    no-caps
                    icon="open_with"
                    label="Move"
                    data-test="drawer-move"
                    @click="startMove"
                  />
                </div>
              </template>

              <q-form v-else-if="mode === 'edit'" class="q-gutter-sm" @submit.prevent="saveEdit">
                <div class="text-subtitle2">Edit booking</div>
                <q-input
                  v-model.number="editForm.partySize"
                  type="number"
                  min="1"
                  label="Party size"
                  outlined
                  dense
                  :error="!!fieldError('partySize')"
                  :error-message="fieldError('partySize')"
                  data-test="edit-party"
                />
                <q-input
                  v-model="editForm.specialRequests"
                  type="textarea"
                  autogrow
                  label="Special requests"
                  outlined
                  dense
                  maxlength="2000"
                />
                <q-input
                  v-model="editForm.internalNotes"
                  type="textarea"
                  autogrow
                  label="Internal notes (staff only)"
                  outlined
                  dense
                  maxlength="4000"
                />
                <div class="row q-gutter-sm">
                  <q-btn
                    type="submit"
                    color="primary"
                    unelevated
                    no-caps
                    label="Save"
                    :loading="saving"
                    data-test="edit-save"
                  />
                  <q-btn flat no-caps label="Back" @click="mode = 'view'" />
                </div>
              </q-form>

              <q-form v-else-if="mode === 'move'" class="q-gutter-sm" @submit.prevent="saveMove">
                <div class="text-subtitle2">Move booking</div>
                <div class="row q-col-gutter-sm">
                  <q-input
                    v-model="moveForm.date"
                    class="col-6"
                    type="date"
                    label="Date"
                    outlined
                    dense
                  />
                  <q-input
                    v-model="moveForm.time"
                    class="col-6"
                    type="time"
                    label="Time"
                    outlined
                    dense
                    data-test="move-time"
                  />
                </div>
                <q-select
                  v-model="moveForm.durationMinutes"
                  :options="durations"
                  emit-value
                  map-options
                  label="Duration"
                  outlined
                  dense
                  :error="!!fieldError('durationMinutes')"
                  :error-message="fieldError('durationMinutes')"
                />
                <q-select
                  v-model="moveForm.resourceIds"
                  :options="resourceOptions"
                  emit-value
                  map-options
                  multiple
                  use-chips
                  label="Tables / resources"
                  hint="Leave unchanged to keep the current resources"
                  outlined
                  dense
                  data-test="move-resources"
                />
                <div class="row q-gutter-sm q-mt-sm">
                  <q-btn
                    type="submit"
                    color="primary"
                    unelevated
                    no-caps
                    label="Move"
                    :loading="saving"
                    data-test="move-save"
                  />
                  <q-btn flat no-caps label="Back" @click="mode = 'view'" />
                </div>
              </q-form>

              <q-banner
                v-if="actionError"
                rounded
                class="bg-red-1 text-negative q-mt-md"
                data-test="drawer-error"
              >
                {{ actionError.message }}
              </q-banner>
            </q-tab-panel>

            <q-tab-panel name="history" class="q-pa-md">
              <q-inner-loading :showing="historyLoading" />
              <q-timeline v-if="history" color="primary" layout="dense" data-test="booking-history">
                <q-timeline-entry
                  v-for="entry in timelineEntries"
                  :key="entry.key"
                  :title="entry.title"
                  :subtitle="formatInstant(entry.at)"
                  :icon="entry.icon"
                >
                  <div v-if="entry.body" class="text-body2 text-grey-8">{{ entry.body }}</div>
                </q-timeline-entry>
              </q-timeline>
            </q-tab-panel>
          </q-tab-panels>
        </q-scroll-area>
      </template>
    </q-card>
  </q-dialog>
</template>

<script setup>
import { computed, reactive, ref, watch } from 'vue'
import { useQuasar } from 'quasar'
import BookingStatusChip from '@/components/bookings/BookingStatusChip.vue'
import ErrorState from '@/components/common/ErrorState.vue'
import { ErrorCodes } from '@/constants/errorCodes.js'
import { RouteNames } from '@/constants/navigation.js'
import { useRealtimeRefresh } from '@/composables/useRealtimeRefresh.js'
import { Permissions } from '@/constants/permissions.js'
import { bookingService } from '@/services/bookingService.js'
import { useAuthStore } from '@/stores/auth.js'
import { durationOptions, formatDuration } from '@/utils/availability.js'
import {
  availableActions,
  canEdit,
  canMove,
  customerName,
  resourceNames,
  SourceLabels,
  statusMeta,
} from '@/utils/bookings.js'
import { formatDate, formatTime } from '@/utils/dates.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'BookingDrawer' })

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  bookingId: { type: String, default: null },
  venue: { type: Object, default: null },
  resources: { type: Array, default: () => [] },
})
const emit = defineEmits(['update:modelValue', 'changed'])

const $q = useQuasar()
const auth = useAuthStore()
const can = (permission) => auth.hasPermission(permission)

const booking = ref(null)
const loading = ref(false)
const loadError = ref(null)
const tab = ref('details')
const mode = ref('view')
const busy = ref(null)
const saving = ref(false)
const actionError = ref(null)
const history = ref(null)
const historyLoading = ref(false)
const remoteChange = ref(false)

const editForm = reactive({ partySize: 1, specialRequests: '', internalNotes: '' })
const moveForm = reactive({ date: '', time: '', durationMinutes: null, resourceIds: [] })

const actions = computed(() => availableActions(booking.value, can))
const editable = computed(() => canEdit(booking.value, can))
const movable = computed(() => canMove(booking.value, can))
const canViewCustomers = computed(() => can(Permissions.CUSTOMER_VIEW))
const durations = computed(() => durationOptions(props.venue?.rules))
const resourceOptions = computed(() =>
  props.resources
    .filter((r) => r.isActive)
    .map((r) => ({ label: `${r.name} (${r.minCapacity}–${r.capacity})`, value: r.id })),
)

function fieldError(field) {
  return actionError.value?.errors?.[field]?.[0] ?? null
}

function formatInstant(instant) {
  const tz = booking.value?.timeZone ?? props.venue?.timeZone
  const date = new Intl.DateTimeFormat('en-CA', { timeZone: tz }).format(new Date(instant))
  return `${formatDate(date)} ${formatTime(instant, tz)}`
}

async function load() {
  if (!props.bookingId) {
    return
  }
  loading.value = true
  loadError.value = null
  try {
    booking.value = await bookingService.get(props.bookingId)
  } catch (e) {
    loadError.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

async function loadHistory() {
  if (!booking.value) {
    return
  }
  historyLoading.value = true
  try {
    history.value = await bookingService.history(booking.value.id)
  } catch (e) {
    $q.notify?.({ type: 'negative', message: normaliseError(e).message })
  } finally {
    historyLoading.value = false
  }
}

const EVENT_TITLES = {
  CREATED: 'Created',
  UPDATED: 'Details updated',
  MOVED: 'Moved',
  STATUS_CHANGED: 'Status changed',
  CANCELLED: 'Cancelled',
  NOTE_ADDED: 'Note added',
}

const timelineEntries = computed(() => {
  if (!history.value) {
    return []
  }
  const statuses = history.value.statusHistory.map((h) => ({
    key: `s-${h.id}`,
    at: h.changedAt,
    icon: statusMeta(h.toStatus).icon,
    title: h.fromStatus
      ? `${statusMeta(h.fromStatus).label} → ${statusMeta(h.toStatus).label}`
      : `Created as ${statusMeta(h.toStatus).label}`,
    body: h.reason ?? '',
  }))
  const events = history.value.events
    .filter((e) => e.eventType === 'UPDATED' || e.eventType === 'MOVED')
    .map((e) => ({
      key: `e-${e.id}`,
      at: e.occurredAt,
      icon: e.eventType === 'MOVED' ? 'open_with' : 'edit',
      title: EVENT_TITLES[e.eventType] ?? e.eventType,
      body: describeEvent(e),
    }))
  return [...statuses, ...events].sort((a, b) => new Date(b.at) - new Date(a.at))
})

function describeEvent(event) {
  const data = event.data ?? {}
  if (Array.isArray(data.changed)) {
    return `Changed: ${data.changed.join(', ')}`
  }
  if (data.from?.startsAt && data.to?.startsAt) {
    const sameResources =
      JSON.stringify([...(data.from.resourceIds ?? [])].sort()) ===
      JSON.stringify([...(data.to.resourceIds ?? [])].sort())
    const times = `${formatInstant(data.from.startsAt)} → ${formatInstant(data.to.startsAt)}`
    return sameResources ? times : `${times}, resources changed`
  }
  return ''
}

function applyUpdate(updated) {
  booking.value = updated
  history.value = null
  emit('changed', updated)
  if (tab.value === 'history') {
    loadHistory()
  }
}

async function handleError(e) {
  const apiError = normaliseError(e)
  actionError.value = apiError
  if (apiError.code === ErrorCodes.CONCURRENCY_CONFLICT) {
    $q.notify?.({
      type: 'warning',
      message: 'This booking was changed by someone else. The latest version has been loaded.',
    })
    await load()
    emit('changed', booking.value)
  }
}

async function runAction(action) {
  if (action.confirm) {
    const confirmed = await confirmDialog(action)
    if (!confirmed) {
      return
    }
    if (action.key === 'cancel') {
      return cancelBooking(confirmed.reason)
    }
  }
  busy.value = action.key
  actionError.value = null
  try {
    applyUpdate(await bookingService.command(booking.value.id, action.key, booking.value.version))
    $q.notify?.({ type: 'positive', message: `${action.label}: ${booking.value.reference}` })
  } catch (e) {
    await handleError(e)
  } finally {
    busy.value = null
  }
}

function confirmDialog(action) {
  return new Promise((resolve) => {
    const options = {
      title: action.label,
      message: action.confirm,
      cancel: { label: 'Keep', flat: true, noCaps: true },
      ok: { label: action.label, color: 'negative', unelevated: true, noCaps: true },
      persistent: true,
    }
    if (action.key === 'cancel') {
      options.prompt = { model: '', type: 'text', label: 'Reason (optional)', maxlength: 500 }
    }
    $q.dialog(options)
      .onOk((value) => resolve({ reason: typeof value === 'string' ? value : '' }))
      .onCancel(() => resolve(null))
      .onDismiss(() => resolve(null))
  })
}

async function cancelBooking(reason) {
  busy.value = 'cancel'
  actionError.value = null
  try {
    applyUpdate(
      await bookingService.cancel(booking.value.id, { reason, version: booking.value.version }),
    )
    $q.notify?.({ type: 'positive', message: `Cancelled ${booking.value.reference}` })
  } catch (e) {
    await handleError(e)
  } finally {
    busy.value = null
  }
}

function startEdit() {
  editForm.partySize = booking.value.partySize
  editForm.specialRequests = booking.value.specialRequests ?? ''
  editForm.internalNotes = booking.value.internalNotes ?? ''
  actionError.value = null
  mode.value = 'edit'
}

async function saveEdit() {
  saving.value = true
  actionError.value = null
  try {
    applyUpdate(
      await bookingService.update(booking.value.id, {
        partySize: editForm.partySize,
        specialRequests: editForm.specialRequests || null,
        internalNotes: editForm.internalNotes || null,
        version: booking.value.version,
      }),
    )
    mode.value = 'view'
    $q.notify?.({ type: 'positive', message: 'Booking updated' })
  } catch (e) {
    await handleError(e)
  } finally {
    saving.value = false
  }
}

function startMove() {
  moveForm.date = booking.value.businessDate
  moveForm.time = booking.value.time
  moveForm.durationMinutes = booking.value.durationMinutes
  moveForm.resourceIds = booking.value.resources.map((r) => r.resourceId)
  actionError.value = null
  mode.value = 'move'
}

async function saveMove() {
  const current = booking.value.resources.map((r) => r.resourceId).sort()
  const chosen = [...moveForm.resourceIds].sort()
  const resourcesChanged = JSON.stringify(current) !== JSON.stringify(chosen)
  const payload = {
    date: moveForm.date !== booking.value.businessDate ? moveForm.date : null,
    time: moveForm.time !== booking.value.time ? moveForm.time : null,
    durationMinutes:
      moveForm.durationMinutes !== booking.value.durationMinutes ? moveForm.durationMinutes : null,
    allocation:
      resourcesChanged && chosen.length ? { mode: 'RESOURCES', resourceIds: chosen } : null,
    version: booking.value.version,
  }
  if (!payload.date && !payload.time && !payload.durationMinutes && !payload.allocation) {
    mode.value = 'view'
    return
  }
  saving.value = true
  actionError.value = null
  try {
    applyUpdate(await bookingService.move(booking.value.id, payload))
    mode.value = 'view'
    $q.notify?.({ type: 'positive', message: 'Booking moved' })
  } catch (e) {
    await handleError(e)
  } finally {
    saving.value = false
  }
}

watch(
  () => [props.modelValue, props.bookingId],
  ([open, bookingId]) => {
    if (open && bookingId) {
      if (booking.value?.id !== bookingId) {
        booking.value = null
        history.value = null
      }
      tab.value = 'details'
      mode.value = 'view'
      actionError.value = null
      remoteChange.value = false
      load()
    }
  },
  { immediate: true },
)

watch(tab, (value) => {
  if (value === 'history' && !history.value) {
    loadHistory()
  }
})

async function refreshFromServer() {
  remoteChange.value = false
  history.value = null
  await load()
  if (tab.value === 'history') {
    loadHistory()
  }
}

function discardAndReload() {
  mode.value = 'view'
  actionError.value = null
  refreshFromServer()
}

// Another user (or tab) changed the open booking: reload, unless that would discard an edit in progress.
useRealtimeRefresh(
  () => {
    if (!props.modelValue || !booking.value) {
      return
    }
    if (mode.value !== 'view' || busy.value || saving.value) {
      remoteChange.value = true
      return
    }
    refreshFromServer()
  },
  {
    filter: (event) =>
      props.modelValue &&
      event.entityId === props.bookingId &&
      (event.version == null || !booking.value || event.version > booking.value.version),
  },
)

defineExpose({ reload: load })
</script>

<style scoped>
.booking-drawer {
  width: 440px;
  max-width: 100vw;
}
.pre-wrap {
  white-space: pre-wrap;
}
</style>
