<template>
  <q-form class="q-gutter-y-sm" data-test="venue-form" @submit.prevent="submit">
    <q-banner
      v-if="action.error.value && !Object.keys(action.fieldErrors.value).length"
      dense
      rounded
      class="bg-red-1 text-negative"
    >
      {{ action.error.value.message }}
    </q-banner>

    <div class="row q-col-gutter-sm">
      <q-input
        v-model.trim="form.name"
        class="col-12 col-sm-6"
        label="Name"
        outlined
        dense
        :readonly="readonly"
        :error="!!action.fieldError('name')"
        :error-message="action.fieldError('name')"
        data-test="venue-name-input"
        @update:model-value="suggestSlug"
      />
      <q-input
        v-model.trim="form.slug"
        class="col-12 col-sm-6"
        label="Slug"
        hint="Used in public booking links"
        outlined
        dense
        :readonly="!isCreate || readonly"
        :error="!!action.fieldError('slug')"
        :error-message="action.fieldError('slug')"
        data-test="venue-slug-input"
        @update:model-value="slugEdited = true"
      />
      <q-select
        v-model="form.timeZone"
        class="col-12 col-sm-6"
        :options="zoneOptions"
        label="Time zone"
        outlined
        dense
        use-input
        input-debounce="0"
        :readonly="readonly"
        :error="!!action.fieldError('timeZone')"
        :error-message="action.fieldError('timeZone')"
        @filter="filterZones"
      />
      <q-select
        v-if="!isCreate"
        v-model="form.status"
        class="col-12 col-sm-6"
        :options="['ACTIVE', 'INACTIVE']"
        label="Status"
        outlined
        dense
        :readonly="readonly"
      />
      <q-input
        v-model="form.description"
        class="col-12"
        label="Description"
        type="textarea"
        autogrow
        outlined
        dense
        :readonly="readonly"
        :error="!!action.fieldError('description')"
        :error-message="action.fieldError('description')"
      />
      <q-input
        v-model.trim="form.addressLine1"
        class="col-12 col-sm-6"
        label="Address"
        outlined
        dense
        :readonly="readonly"
      />
      <q-input
        v-model.trim="form.addressLine2"
        class="col-12 col-sm-6"
        label="Address line 2"
        outlined
        dense
        :readonly="readonly"
      />
      <q-input
        v-model.trim="form.city"
        class="col-6 col-sm-3"
        label="City"
        outlined
        dense
        :readonly="readonly"
      />
      <q-input
        v-model.trim="form.region"
        class="col-6 col-sm-3"
        label="Region"
        outlined
        dense
        :readonly="readonly"
      />
      <q-input
        v-model.trim="form.postalCode"
        class="col-6 col-sm-3"
        label="Postal code"
        outlined
        dense
        :readonly="readonly"
      />
      <q-input
        v-model.trim="form.countryCode"
        class="col-6 col-sm-3"
        label="Country (ISO)"
        maxlength="2"
        outlined
        dense
        :readonly="readonly"
        :error="!!action.fieldError('countryCode')"
        :error-message="action.fieldError('countryCode')"
        @update:model-value="form.countryCode = (form.countryCode ?? '').toUpperCase()"
      />
      <q-input
        v-model.trim="form.phone"
        class="col-12 col-sm-6"
        label="Phone"
        outlined
        dense
        :readonly="readonly"
      />
      <q-input
        v-model.trim="form.email"
        class="col-12 col-sm-6"
        label="Email"
        type="email"
        outlined
        dense
        :readonly="readonly"
        :error="!!action.fieldError('email')"
        :error-message="action.fieldError('email')"
      />
    </div>

    <q-expansion-item
      :default-opened="!isCreate"
      dense
      icon="rule"
      label="Booking rules"
      header-class="text-subtitle2"
    >
      <div class="row q-col-gutter-sm q-pt-sm">
        <q-select
          v-model="form.rules.slotIntervalMinutes"
          class="col-6 col-sm-4"
          :options="[5, 10, 15, 20, 30, 60]"
          label="Slot interval (min)"
          outlined
          dense
          :readonly="readonly"
        />
        <q-input
          v-for="field in numberFields"
          :key="field.key"
          v-model.number="form.rules[field.key]"
          class="col-6 col-sm-4"
          type="number"
          :label="field.label"
          outlined
          dense
          :readonly="readonly"
          :error="!!ruleError(field.key)"
          :error-message="ruleError(field.key)"
        />
        <q-toggle
          v-model="form.rules.publicBookingEnabled"
          class="col-12 col-sm-6"
          label="Accept online bookings"
          :disable="readonly"
          data-test="public-booking-toggle"
        />
        <q-toggle
          v-model="form.rules.autoConfirmPublicBookings"
          class="col-12 col-sm-6"
          label="Auto-confirm public bookings"
          :disable="readonly"
        />
      </div>
    </q-expansion-item>

    <div v-if="!readonly" class="row justify-end q-gutter-sm q-pt-sm">
      <q-btn v-if="cancellable" flat label="Cancel" @click="$emit('cancel')" />
      <q-btn
        type="submit"
        color="primary"
        unelevated
        :label="isCreate ? 'Create venue' : 'Save'"
        :loading="action.loading.value"
        data-test="venue-submit"
      />
    </div>
  </q-form>
</template>

<script setup>
import { computed, reactive, ref } from 'vue'
import { useApiAction } from '@/composables/useApiAction.js'
import { venueService } from '@/services/venueService.js'
import { slugify } from '@/utils/slug.js'
import { timeZoneOptions } from '@/utils/timeZones.js'

defineOptions({ name: 'VenueForm' })

const props = defineProps({
  venue: { type: Object, default: null },
  defaultTimeZone: { type: String, default: 'Africa/Johannesburg' },
  readonly: { type: Boolean, default: false },
  cancellable: { type: Boolean, default: false },
})
const emit = defineEmits(['saved', 'cancel'])

const DEFAULT_RULES = Object.freeze({
  slotIntervalMinutes: 15,
  defaultDurationMinutes: 120,
  minDurationMinutes: 30,
  maxDurationMinutes: 480,
  turnoverBufferMinutes: 0,
  minPartySize: 1,
  maxPartySize: 12,
  minLeadTimeMinutes: 60,
  maxAdvanceDays: 90,
  holdTtlSeconds: 300,
  autoConfirmPublicBookings: true,
  publicBookingEnabled: true,
  publicCancellationCutoffMinutes: 120,
})

const numberFields = [
  { key: 'defaultDurationMinutes', label: 'Default duration (min)' },
  { key: 'minDurationMinutes', label: 'Min duration (min)' },
  { key: 'maxDurationMinutes', label: 'Max duration (min)' },
  { key: 'turnoverBufferMinutes', label: 'Turnover buffer (min)' },
  { key: 'minPartySize', label: 'Min party size' },
  { key: 'maxPartySize', label: 'Max party size' },
  { key: 'minLeadTimeMinutes', label: 'Min lead time (min)' },
  { key: 'maxAdvanceDays', label: 'Book ahead (days)' },
  { key: 'holdTtlSeconds', label: 'Hold time (sec)' },
  { key: 'publicCancellationCutoffMinutes', label: 'Online cancel cutoff (min)' },
]

const isCreate = computed(() => !props.venue)
const slugEdited = ref(!!props.venue)

const form = reactive({
  name: props.venue?.name ?? '',
  slug: props.venue?.slug ?? '',
  timeZone: props.venue?.timeZone ?? props.defaultTimeZone,
  status: props.venue?.status ?? 'ACTIVE',
  description: props.venue?.description ?? '',
  addressLine1: props.venue?.addressLine1 ?? '',
  addressLine2: props.venue?.addressLine2 ?? '',
  city: props.venue?.city ?? '',
  region: props.venue?.region ?? '',
  postalCode: props.venue?.postalCode ?? '',
  countryCode: props.venue?.countryCode ?? '',
  phone: props.venue?.phone ?? '',
  email: props.venue?.email ?? '',
  rules: { ...DEFAULT_RULES, ...(props.venue?.rules ?? {}) },
})

const allZones = timeZoneOptions()
const zoneOptions = ref(allZones)

function filterZones(value, update) {
  update(() => {
    const needle = value.toLowerCase()
    zoneOptions.value = needle ? allZones.filter((z) => z.toLowerCase().includes(needle)) : allZones
  })
}

function suggestSlug() {
  if (isCreate.value && !slugEdited.value) {
    form.slug = slugify(form.name)
  }
}

const action = useApiAction((payload) =>
  isCreate.value
    ? venueService.createVenue(payload)
    : venueService.updateVenue(props.venue.id, payload),
)

function ruleError(key) {
  return action.fieldError(`rules.${key}`)
}

const blankToNull = (value) => (value === '' || value === undefined ? null : value)

async function submit() {
  const payload = {
    name: form.name,
    timeZone: form.timeZone,
    description: blankToNull(form.description),
    addressLine1: blankToNull(form.addressLine1),
    addressLine2: blankToNull(form.addressLine2),
    city: blankToNull(form.city),
    region: blankToNull(form.region),
    postalCode: blankToNull(form.postalCode),
    countryCode: blankToNull(form.countryCode),
    phone: blankToNull(form.phone),
    email: blankToNull(form.email),
    rules: { ...form.rules },
  }
  if (isCreate.value) {
    payload.slug = form.slug
  } else {
    payload.status = form.status
  }
  const saved = await action.run(payload)
  if (saved) {
    emit('saved', saved)
  }
}
</script>
