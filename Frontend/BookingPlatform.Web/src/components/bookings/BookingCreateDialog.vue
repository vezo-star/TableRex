<template>
  <q-dialog
    :model-value="modelValue"
    persistent
    @update:model-value="$emit('update:modelValue', $event)"
    @hide="onHide"
  >
    <q-card class="create-dialog" data-test="booking-create-dialog">
      <q-card-section class="row items-center q-pb-none">
        <div class="text-h6 col">New booking</div>
        <div v-if="hold" class="text-caption text-grey-7 q-mr-sm" data-test="hold-info">
          Held until {{ formatTime(hold.expiresAt, venue.timeZone) }}
        </div>
        <q-btn flat round dense icon="close" aria-label="Close" @click="close" />
      </q-card-section>

      <q-stepper
        v-model="step"
        flat
        animated
        color="primary"
        active-color="primary"
        done-color="primary"
        inactive-color="grey-8"
        class="q-pt-none"
      >
        <q-step name="when" title="When" icon="event" :done="step !== 'when'">
          <div class="row q-col-gutter-sm">
            <q-input
              v-model="form.date"
              class="col-12 col-sm-4"
              type="date"
              label="Date"
              outlined
              dense
              data-test="create-date"
            />
            <q-input
              v-model.number="form.partySize"
              class="col-6 col-sm-4"
              type="number"
              min="1"
              label="Party size"
              outlined
              dense
              data-test="create-party"
            />
            <q-select
              v-model="form.durationMinutes"
              class="col-6 col-sm-4"
              :options="durations"
              emit-value
              map-options
              label="Duration"
              outlined
              dense
            />
          </div>
          <div class="q-mt-sm">
            <q-btn
              color="primary"
              outline
              no-caps
              icon="search"
              label="Check availability"
              :loading="searching"
              data-test="create-search"
              @click="search"
            />
          </div>

          <div v-if="availability" class="q-mt-md">
            <div v-if="availability.isClosed" class="text-grey-7">
              The venue is closed on this date.
            </div>
            <div v-else-if="!availability.slots.length" class="text-grey-7">
              No times on this date.
            </div>
            <div v-else class="row q-gutter-xs" data-test="create-slots">
              <q-btn
                v-for="slot in availability.slots"
                :key="slot.startsAt"
                :label="slot.time"
                :color="
                  selectedSlot?.startsAt === slot.startsAt
                    ? 'primary'
                    : slot.available
                      ? 'teal-1'
                      : 'grey-3'
                "
                :text-color="
                  selectedSlot?.startsAt === slot.startsAt
                    ? 'white'
                    : slot.available
                      ? 'teal-10'
                      : 'grey-6'
                "
                :disable="!slot.available"
                unelevated
                dense
                no-caps
                class="q-px-sm"
                :data-test="`slot-${slot.time}`"
                @click="selectSlot(slot)"
              />
            </div>

            <div v-if="selectedSlot" class="q-mt-md">
              <div class="text-subtitle2 q-mb-xs">Table</div>
              <q-option-group
                v-model="optionKey"
                :options="optionChoices"
                type="radio"
                dense
                data-test="create-options"
              />
            </div>
          </div>

          <q-banner
            v-if="error && step === 'when'"
            rounded
            class="bg-red-1 text-negative q-mt-md"
            data-test="create-error"
          >
            {{ error.message }}
          </q-banner>
          <q-stepper-navigation>
            <q-btn
              color="primary"
              unelevated
              no-caps
              label="Continue"
              :disable="!selectedSlot"
              :loading="holding"
              data-test="create-next-1"
              @click="placeHold"
            />
          </q-stepper-navigation>
        </q-step>

        <q-step name="customer" title="Customer" icon="person" :done="step === 'details'">
          <q-btn-toggle
            v-model="form.source"
            :options="sourceOptions"
            no-caps
            unelevated
            toggle-color="primary"
            class="q-mb-md"
            data-test="create-source"
          />
          <q-option-group
            v-model="customerMode"
            :options="customerModes"
            type="radio"
            inline
            dense
            class="q-mb-sm"
            data-test="create-customer-mode"
          />
          <CustomerPicker v-if="customerMode === 'existing'" v-model="existingCustomer" />
          <div v-else-if="customerMode === 'new'" class="row q-col-gutter-sm">
            <q-input
              v-model="newCustomer.firstName"
              class="col-6"
              label="First name *"
              outlined
              dense
              maxlength="100"
              :error="!!fieldError('customer.firstName')"
              :error-message="fieldError('customer.firstName')"
              data-test="create-first-name"
            />
            <q-input
              v-model="newCustomer.lastName"
              class="col-6"
              label="Last name"
              outlined
              dense
              maxlength="100"
            />
            <q-input
              v-model="newCustomer.phone"
              class="col-6"
              label="Phone"
              outlined
              dense
              maxlength="40"
              :error="!!fieldError('customer.phone')"
              :error-message="fieldError('customer.phone')"
              data-test="create-phone"
            />
            <q-input
              v-model="newCustomer.email"
              class="col-6"
              type="email"
              label="Email"
              outlined
              dense
              maxlength="254"
              hint="An existing customer with this email is reused"
              :error="!!fieldError('customer.email')"
              :error-message="fieldError('customer.email')"
            />
          </div>
          <div v-else class="text-grey-7">Walk-in without customer details.</div>
          <q-stepper-navigation>
            <q-btn
              color="primary"
              unelevated
              no-caps
              label="Continue"
              :disable="!customerReady"
              data-test="create-next-2"
              @click="step = 'details'"
            />
            <q-btn flat no-caps label="Back" class="q-ml-sm" @click="backToWhen" />
          </q-stepper-navigation>
        </q-step>

        <q-step name="details" title="Details" icon="notes">
          <div class="text-body2 q-mb-md" data-test="create-summary">
            {{ formatDate(form.date) }} at {{ selectedSlot?.time }} · {{ form.partySize }} guests ·
            {{ selectedOptionLabel }}
          </div>
          <q-select
            v-model="form.status"
            :options="statusOptions"
            emit-value
            map-options
            label="Status"
            outlined
            dense
            class="q-mb-sm"
          />
          <q-input
            v-model="form.specialRequests"
            type="textarea"
            autogrow
            label="Special requests"
            outlined
            dense
            maxlength="2000"
            class="q-mb-sm"
          />
          <q-input
            v-model="form.internalNotes"
            type="textarea"
            autogrow
            label="Internal notes"
            outlined
            dense
            maxlength="4000"
          />
          <q-banner
            v-if="error && step !== 'when'"
            rounded
            class="bg-red-1 text-negative q-mt-md"
            data-test="create-error"
          >
            {{ error.message }}
          </q-banner>
          <q-stepper-navigation>
            <q-btn
              color="primary"
              unelevated
              no-caps
              label="Create booking"
              :loading="submitting"
              data-test="create-submit"
              @click="submit"
            />
            <q-btn flat no-caps label="Back" class="q-ml-sm" @click="step = 'customer'" />
          </q-stepper-navigation>
        </q-step>
      </q-stepper>
    </q-card>
  </q-dialog>
</template>

<script setup>
import { computed, reactive, ref, watch } from 'vue'
import { useQuasar } from 'quasar'
import CustomerPicker from '@/components/customers/CustomerPicker.vue'
import { ErrorCodes } from '@/constants/errorCodes.js'
import { availabilityService } from '@/services/availabilityService.js'
import { bookingService, holdService } from '@/services/bookingService.js'
import { createIdempotencyKeeper, isRetryableFailure } from '@/services/idempotency.js'
import { durationOptions, optionResourceLabel } from '@/utils/availability.js'
import { formatDate, formatTime, todayIso } from '@/utils/dates.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'BookingCreateDialog' })

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  venue: { type: Object, required: true },
  resources: { type: Array, default: () => [] },
  /** Optional `{ date, time, resourceId }` from the board. */
  prefill: { type: Object, default: null },
})
const emit = defineEmits(['update:modelValue', 'created'])

const $q = useQuasar()

const AUTO = 'auto'
const sourceOptions = [
  { label: 'Phone', value: 'PHONE' },
  { label: 'Walk-in', value: 'WALK_IN' },
  { label: 'Staff', value: 'STAFF' },
]
const statusOptions = [
  { label: 'Confirmed', value: 'CONFIRMED' },
  { label: 'Pending', value: 'PENDING' },
  { label: 'Seated now', value: 'SEATED' },
]

const step = ref('when')
const form = reactive({
  date: '',
  partySize: 2,
  durationMinutes: null,
  source: 'PHONE',
  status: 'CONFIRMED',
  specialRequests: '',
  internalNotes: '',
})
const availability = ref(null)
const selectedSlot = ref(null)
const optionKey = ref(AUTO)
const searching = ref(false)
const holding = ref(false)
const submitting = ref(false)
const error = ref(null)
const hold = ref(null)
const customerMode = ref('existing')
const existingCustomer = ref(null)
const newCustomer = reactive({ firstName: '', lastName: '', phone: '', email: '' })
let created = false

const holdKeeper = createIdempotencyKeeper()
const bookingKeeper = createIdempotencyKeeper()

const durations = computed(() => durationOptions(props.venue?.rules))
const resourcesById = computed(() => new Map(props.resources.map((r) => [r.id, r])))

const optionChoices = computed(() => [
  { label: 'Best available table', value: AUTO },
  ...(selectedSlot.value?.options ?? []).map((o) => ({
    label: `${optionResourceLabel(o, resourcesById.value)} · seats ${o.capacity}`,
    value: `${o.kind}:${o.id}`,
  })),
])

const selectedOption = computed(() =>
  optionKey.value === AUTO
    ? null
    : (selectedSlot.value?.options ?? []).find((o) => `${o.kind}:${o.id}` === optionKey.value),
)

const selectedOptionLabel = computed(() => {
  if (hold.value?.resourceIds?.length) {
    return hold.value.resourceIds.map((id) => resourcesById.value.get(id)?.name ?? '?').join(' + ')
  }
  return selectedOption.value
    ? optionResourceLabel(selectedOption.value, resourcesById.value)
    : 'Best available'
})

const customerModes = computed(() => {
  const modes = [
    { label: 'Existing customer', value: 'existing' },
    { label: 'New customer', value: 'new' },
  ]
  if (form.source === 'WALK_IN') {
    modes.push({ label: 'No details', value: 'none' })
  }
  return modes
})

const customerReady = computed(() => {
  if (customerMode.value === 'existing') {
    return !!existingCustomer.value
  }
  if (customerMode.value === 'new') {
    return newCustomer.firstName.trim().length > 0
  }
  return form.source === 'WALK_IN'
})

function fieldError(field) {
  return error.value?.errors?.[field]?.[0] ?? null
}

function allocationFor(option) {
  if (!option) {
    return { mode: 'AUTO' }
  }
  return option.kind === 'COMBINATION'
    ? { mode: 'COMBINATION', combinationId: option.id }
    : { mode: 'RESOURCES', resourceIds: option.resourceIds }
}

async function search() {
  error.value = null
  searching.value = true
  await releaseHold()
  try {
    availability.value = await availabilityService.search({
      venueId: props.venue.id,
      date: form.date,
      partySize: form.partySize,
      durationMinutes: form.durationMinutes,
    })
    const wanted = props.prefill?.time
    const match = wanted
      ? availability.value.slots.find((s) => s.time === wanted && s.available)
      : null
    selectedSlot.value = match ?? null
    if (match) {
      selectSlot(match)
    }
  } catch (e) {
    error.value = normaliseError(e)
    availability.value = null
  } finally {
    searching.value = false
  }
}

function selectSlot(slot) {
  selectedSlot.value = slot
  const resourceId = props.prefill?.resourceId
  const preferred = resourceId
    ? slot.options.find(
        (o) =>
          o.kind === 'RESOURCE' && o.resourceIds.length === 1 && o.resourceIds[0] === resourceId,
      )
    : null
  optionKey.value = preferred ? `${preferred.kind}:${preferred.id}` : AUTO
}

async function placeHold() {
  error.value = null
  await releaseHold()
  const payload = {
    venueId: props.venue.id,
    date: form.date,
    time: selectedSlot.value.time,
    durationMinutes: form.durationMinutes,
    partySize: form.partySize,
    allocation: allocationFor(selectedOption.value),
  }
  holding.value = true
  try {
    hold.value = await holdService.create(payload, holdKeeper.keyFor(payload))
    holdKeeper.reset()
    step.value = 'customer'
  } catch (e) {
    error.value = normaliseError(e)
    if (!isRetryableFailure(error.value)) {
      holdKeeper.reset()
    }
    if (error.value.code === ErrorCodes.RESOURCE_NOT_AVAILABLE) {
      error.value.message =
        'That time was just taken. Availability has been refreshed; please pick again.'
      const message = error.value
      await search()
      error.value = message
    }
  } finally {
    holding.value = false
  }
}

async function releaseHold() {
  const current = hold.value
  hold.value = null
  if (current && !created) {
    try {
      await holdService.release(current.id)
    } catch {
      // The hold expires on its own; nothing else to do.
    }
  }
}

async function backToWhen() {
  await releaseHold()
  step.value = 'when'
}

function customerPayload() {
  if (customerMode.value === 'existing' && existingCustomer.value) {
    return { customerId: existingCustomer.value.id }
  }
  if (customerMode.value === 'new') {
    return {
      customerId: null,
      firstName: newCustomer.firstName.trim(),
      lastName: newCustomer.lastName.trim() || null,
      email: newCustomer.email.trim() || null,
      phone: newCustomer.phone.trim() || null,
    }
  }
  return null
}

async function submit() {
  error.value = null
  const payload = {
    venueId: props.venue.id,
    customer: customerPayload(),
    date: form.date,
    time: selectedSlot.value.time,
    durationMinutes: form.durationMinutes,
    partySize: form.partySize,
    holdToken: hold.value?.token ?? null,
    allocation: hold.value?.token ? null : allocationFor(selectedOption.value),
    source: form.source,
    status: form.status,
    specialRequests: form.specialRequests.trim() || null,
    internalNotes: form.internalNotes.trim() || null,
  }
  submitting.value = true
  try {
    const { booking } = await bookingService.create(payload, bookingKeeper.keyFor(payload))
    bookingKeeper.reset()
    created = true
    hold.value = null
    $q.notify?.({ type: 'positive', message: `Booking ${booking.reference} created` })
    emit('created', booking)
    emit('update:modelValue', false)
  } catch (e) {
    error.value = normaliseError(e)
    if (!isRetryableFailure(error.value)) {
      bookingKeeper.reset()
    }
    if (
      error.value.code === ErrorCodes.HOLD_EXPIRED ||
      error.value.code === ErrorCodes.HOLD_NOT_FOUND
    ) {
      hold.value = null
      error.value.message = 'The table hold expired. Go back and choose the time again.'
    }
  } finally {
    submitting.value = false
  }
}

function close() {
  emit('update:modelValue', false)
}

async function onHide() {
  await releaseHold()
}

function reset() {
  created = false
  step.value = 'when'
  error.value = null
  availability.value = null
  selectedSlot.value = null
  optionKey.value = AUTO
  hold.value = null
  form.date = props.prefill?.date ?? todayIso(props.venue.timeZone)
  form.partySize = 2
  form.durationMinutes = props.venue.rules?.defaultDurationMinutes ?? null
  form.source = 'PHONE'
  form.status = 'CONFIRMED'
  form.specialRequests = ''
  form.internalNotes = ''
  customerMode.value = 'existing'
  existingCustomer.value = null
  Object.assign(newCustomer, { firstName: '', lastName: '', phone: '', email: '' })
  holdKeeper.reset()
  bookingKeeper.reset()
}

watch(
  () => form.source,
  (source) => {
    if (source !== 'WALK_IN' && customerMode.value === 'none') {
      customerMode.value = 'existing'
    }
  },
)

watch(
  () => props.modelValue,
  (open) => {
    if (open) {
      reset()
      if (props.prefill?.time) {
        search()
      }
    }
  },
  { immediate: true },
)
</script>

<style scoped>
.create-dialog {
  width: 640px;
  max-width: 95vw;
}
</style>
