<template>
  <q-page class="public-page q-pa-md">
    <div class="public-column">
      <div v-if="loading" class="flex flex-center q-pa-xl" data-test="venue-loading">
        <q-spinner size="lg" color="primary" />
      </div>

      <ErrorState
        v-else-if="loadError"
        :message="guestErrorMessage(loadError)"
        :retryable="loadError.status !== 404"
        @retry="loadVenue"
      />

      <template v-else-if="venue">
        <header class="q-mb-md" data-test="venue-header">
          <div class="text-caption text-grey-7">{{ venue.tenantName }}</div>
          <h1 class="text-h5 q-my-xs">{{ venue.name }}</h1>
          <div v-if="addressLine" class="text-body2 text-grey-8">
            <q-icon name="place" size="xs" /> {{ addressLine }}
          </div>
          <p v-if="venue.description" class="text-body2 q-mt-sm q-mb-none">
            {{ venue.description }}
          </p>
        </header>

        <!-- Confirmation -->
        <q-card v-if="confirmation" flat bordered data-test="booking-confirmation">
          <q-card-section class="text-center">
            <q-icon
              :name="confirmation.status === 'CONFIRMED' ? 'check_circle' : 'schedule'"
              :color="confirmation.status === 'CONFIRMED' ? 'positive' : 'warning'"
              size="56px"
            />
            <div class="text-h6 q-mt-sm" data-test="confirmation-title">
              {{ confirmationTitle }}
            </div>
            <div v-if="confirmation.status === 'PENDING'" class="text-body2 text-grey-8">
              The venue will confirm your booking shortly.
            </div>
            <div v-else-if="confirmation.status === 'HELD'" class="text-body2 text-grey-8">
              Pay the deposit below to confirm it.
            </div>
          </q-card-section>
          <q-separator />
          <q-list dense class="q-py-sm">
            <q-item>
              <q-item-section>Reference</q-item-section>
              <q-item-section side class="text-weight-bold text-dark" data-test="booking-reference">
                {{ confirmation.reference }}
              </q-item-section>
            </q-item>
            <q-item>
              <q-item-section>Date</q-item-section>
              <q-item-section side class="text-dark">
                {{ formatLongDate(confirmation.date) }}
              </q-item-section>
            </q-item>
            <q-item>
              <q-item-section>Time</q-item-section>
              <q-item-section side class="text-dark">{{ confirmation.time }}</q-item-section>
            </q-item>
            <q-item>
              <q-item-section>Guests</q-item-section>
              <q-item-section side class="text-dark">{{ confirmation.partySize }}</q-item-section>
            </q-item>
          </q-list>
          <template v-if="confirmation.payment && confirmation.manageToken">
            <q-separator />
            <DepositPayCard
              :payment="confirmation.payment"
              :manage-token="confirmation.manageToken"
              :time-zone="confirmation.timeZone"
            />
          </template>
          <q-separator />
          <q-card-section v-if="manageUrl">
            <div class="text-subtitle2">Manage your booking</div>
            <p class="text-body2 text-grey-8 q-mb-sm">
              Keep this private link to view or cancel your booking. Anyone with the link can manage
              it.
            </p>
            <div class="column q-gutter-sm">
              <q-btn
                color="primary"
                unelevated
                no-caps
                icon="open_in_new"
                label="View my booking"
                :href="manageUrl"
                data-test="manage-link"
              />
              <q-btn
                outline
                color="primary"
                no-caps
                icon="content_copy"
                label="Copy link"
                data-test="copy-manage-link"
                @click="copyManageLink"
              />
            </div>
          </q-card-section>
          <q-card-actions align="center">
            <q-btn flat no-caps color="primary" label="Make another booking" @click="startOver" />
          </q-card-actions>
        </q-card>

        <!-- Search -->
        <template v-else>
          <q-card flat bordered class="q-mb-md">
            <q-card-section class="q-gutter-y-sm">
              <div class="row q-col-gutter-sm">
                <q-input
                  v-model="date"
                  class="col-7"
                  type="date"
                  label="Date"
                  outlined
                  dense
                  stack-label
                  :min="venue.today"
                  :max="venue.lastBookableDate"
                  data-test="date-input"
                />
                <q-select
                  v-model="partySize"
                  class="col-5"
                  :options="partyOptions"
                  label="Guests"
                  outlined
                  dense
                  emit-value
                  data-test="party-select"
                />
              </div>
              <div class="text-caption text-grey-7">
                Tables are held for {{ formatMinutes(venue.durationMinutes) }}.
                <span v-if="venue.phone">
                  Larger group? Call <a :href="`tel:${venue.phone}`">{{ venue.phone }}</a
                  >.
                </span>
              </div>
            </q-card-section>
          </q-card>

          <section class="q-mb-md" aria-live="polite">
            <div class="text-subtitle1 q-mb-sm">Available times</div>
            <div v-if="timesLoading" class="row q-gutter-sm" data-test="times-loading">
              <q-skeleton v-for="n in 6" :key="n" type="QBtn" width="72px" />
            </div>
            <q-banner
              v-else-if="timesError"
              dense
              rounded
              class="bg-red-1 text-negative"
              data-test="times-error"
            >
              {{ guestErrorMessage(timesError) }}
              <template #action>
                <q-btn flat dense no-caps label="Try again" @click="loadTimes" />
              </template>
            </q-banner>
            <div
              v-else-if="availability?.isClosed"
              class="text-body2 text-grey-8"
              data-test="times-closed"
            >
              The venue is closed on {{ formatLongDate(date) }}. Please choose another date.
            </div>
            <div
              v-else-if="availability && !availability.times.length"
              class="text-body2 text-grey-8"
              data-test="times-empty"
            >
              No tables are free for {{ partySize }} on {{ formatLongDate(date) }}. Try another date
              or party size.
            </div>
            <div v-else-if="availability" class="time-grid" data-test="times">
              <q-btn
                v-for="slot in availability.times"
                :key="slot.time"
                no-caps
                unelevated
                :outline="selectedTime !== slot.time"
                color="primary"
                :label="slot.time"
                :aria-pressed="selectedTime === slot.time"
                data-test="time-slot"
                @click="selectTime(slot.time)"
              />
            </div>
          </section>

          <q-card v-if="selectedTime" flat bordered data-test="guest-form-card">
            <q-card-section>
              <div class="text-subtitle1">Your details</div>
              <div class="text-body2 text-grey-8">
                {{ formatLongDate(date) }} at {{ selectedTime }} for {{ partySize }}
                {{ partySize === 1 ? 'guest' : 'guests' }}
              </div>
            </q-card-section>
            <q-card-section class="q-pt-none">
              <q-form class="q-gutter-y-sm" data-test="guest-form" @submit.prevent="submit">
                <q-banner
                  v-if="submitError"
                  dense
                  rounded
                  class="bg-red-1 text-negative"
                  data-test="submit-error"
                >
                  {{ guestErrorMessage(submitError) }}
                </q-banner>
                <div class="row q-col-gutter-sm">
                  <q-input
                    v-model="guest.firstName"
                    class="col-12 col-sm-6"
                    label="First name *"
                    autocomplete="given-name"
                    outlined
                    dense
                    maxlength="100"
                    :rules="[(v) => !!(v && v.trim()) || 'First name is required']"
                    :error="!!fieldError('firstName')"
                    :error-message="fieldError('firstName')"
                    data-test="first-name-input"
                  />
                  <q-input
                    v-model="guest.lastName"
                    class="col-12 col-sm-6"
                    label="Last name"
                    autocomplete="family-name"
                    outlined
                    dense
                    maxlength="100"
                    :error="!!fieldError('lastName')"
                    :error-message="fieldError('lastName')"
                  />
                </div>
                <q-input
                  v-model="guest.email"
                  label="Email *"
                  type="email"
                  autocomplete="email"
                  outlined
                  dense
                  maxlength="254"
                  :rules="[(v) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v ?? '') || 'Enter a valid email']"
                  :error="!!fieldError('email')"
                  :error-message="fieldError('email')"
                  data-test="email-input"
                />
                <q-input
                  v-model="guest.phone"
                  label="Phone"
                  type="tel"
                  autocomplete="tel"
                  outlined
                  dense
                  maxlength="40"
                  :error="!!fieldError('phone')"
                  :error-message="fieldError('phone')"
                />
                <q-input
                  v-model="guest.specialRequests"
                  label="Special requests"
                  type="textarea"
                  autogrow
                  outlined
                  dense
                  maxlength="1000"
                  :error="!!fieldError('specialRequests')"
                  :error-message="fieldError('specialRequests')"
                />
                <div class="hp-field" aria-hidden="true">
                  <label for="website">Website</label>
                  <input
                    id="website"
                    v-model="guest.website"
                    name="website"
                    type="text"
                    tabindex="-1"
                    autocomplete="off"
                    data-test="honeypot"
                  />
                </div>
                <q-checkbox
                  v-model="guest.marketingConsent"
                  dense
                  label="Send me news and offers from this venue"
                />
                <div v-if="venue.deposit" class="text-body2 text-weight-medium" data-test="deposit-notice">
                  {{ depositNotice(venue.deposit, partySize) }}
                  You will have {{ formatMinutes(venue.deposit.paymentWindowMinutes) }} to pay.
                </div>
                <div class="text-caption text-grey-7">
                  <template v-if="venue.cancellationCutoffMinutes > 0">
                    You can cancel online up to
                    {{ formatMinutes(venue.cancellationCutoffMinutes) }} before your booking.
                  </template>
                  <template v-else>You can cancel online until your booking starts.</template>
                </div>
                <q-btn
                  type="submit"
                  color="primary"
                  unelevated
                  no-caps
                  class="full-width"
                  size="lg"
                  :label="submitLabel"
                  :loading="submitting"
                  data-test="submit-booking"
                />
              </q-form>
            </q-card-section>
          </q-card>
        </template>
      </template>
    </div>
  </q-page>
</template>

<script setup>
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useQuasar, copyToClipboard } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import DepositPayCard from '@/components/payments/DepositPayCard.vue'
import { publicBookingService, manageBookingUrl } from '@/services/publicBookingService.js'
import { depositNotice } from '@/utils/payments.js'
import { createIdempotencyKeeper, isRetryableFailure } from '@/services/idempotency.js'
import { normaliseError } from '@/utils/problemDetails.js'
import {
  buildPublicBookingPayload,
  formatLongDate,
  formatMinutes,
  guestErrorMessage,
  isSlotGone,
  partySizeOptions,
} from '@/utils/publicBooking.js'

defineOptions({ name: 'PublicBookingPage' })

const props = defineProps({
  tenantSlug: { type: String, required: true },
  venueSlug: { type: String, required: true },
})

const $q = useQuasar()

const venue = ref(null)
const loading = ref(false)
const loadError = ref(null)

const date = ref('')
const partySize = ref(2)
const availability = ref(null)
const timesLoading = ref(false)
const timesError = ref(null)
const selectedTime = ref(null)

const guest = reactive(blankGuest())
const submitting = ref(false)
const submitError = ref(null)
const confirmation = ref(null)
const keeper = createIdempotencyKeeper()

let timesController = null

const partyOptions = computed(() => partySizeOptions(venue.value))

const addressLine = computed(() =>
  [venue.value?.addressLine1, venue.value?.city].filter(Boolean).join(', '),
)

const manageUrl = computed(() =>
  confirmation.value?.manageToken ? manageBookingUrl(confirmation.value.manageToken) : null,
)

const confirmationTitle = computed(() => {
  switch (confirmation.value?.status) {
    case 'CONFIRMED':
      return 'Your booking is confirmed'
    case 'HELD':
      return 'Your table is held'
    default:
      return 'Request received'
  }
})

const submitLabel = computed(() => {
  if (venue.value?.deposit) {
    return 'Continue to deposit'
  }
  return venue.value?.autoConfirm ? 'Confirm booking' : 'Request booking'
})

function blankGuest() {
  return {
    firstName: '',
    lastName: '',
    email: '',
    phone: '',
    specialRequests: '',
    marketingConsent: false,
    website: '',
  }
}

function fieldError(name) {
  const messages = submitError.value?.errors?.[name]
  return messages?.length ? messages[0] : null
}

async function loadVenue() {
  loading.value = true
  loadError.value = null
  try {
    const data = await publicBookingService.venue(props.tenantSlug, props.venueSlug)
    venue.value = data
    const sizes = partySizeOptions(data)
    if (!sizes.includes(partySize.value)) {
      partySize.value = sizes.includes(2) ? 2 : sizes[0]
    }
    date.value = data.today
  } catch (e) {
    loadError.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

async function loadTimes() {
  if (!venue.value || !date.value || !partySize.value) {
    return
  }
  timesController?.abort()
  const controller = new AbortController()
  timesController = controller
  timesLoading.value = true
  timesError.value = null
  try {
    const data = await publicBookingService.availability(
      props.tenantSlug,
      props.venueSlug,
      { date: date.value, partySize: partySize.value },
      { signal: controller.signal },
    )
    if (timesController === controller) {
      availability.value = data
      if (selectedTime.value && !data.times.some((t) => t.time === selectedTime.value)) {
        selectedTime.value = null
      }
    }
  } catch (e) {
    if (controller.signal.aborted) {
      return
    }
    timesError.value = normaliseError(e)
    availability.value = null
  } finally {
    if (timesController === controller) {
      timesLoading.value = false
    }
  }
}

function selectTime(time) {
  selectedTime.value = time
  submitError.value = null
}

async function submit() {
  if (!selectedTime.value || submitting.value) {
    return
  }
  const payload = buildPublicBookingPayload(
    { date: date.value, time: selectedTime.value, partySize: partySize.value },
    guest,
  )
  submitting.value = true
  submitError.value = null
  try {
    const { booking } = await publicBookingService.create(
      props.tenantSlug,
      props.venueSlug,
      payload,
      keeper.keyFor(payload),
    )
    keeper.reset()
    confirmation.value = booking
    window.scrollTo?.({ top: 0, behavior: 'smooth' })
  } catch (e) {
    const error = normaliseError(e)
    submitError.value = error
    if (!isRetryableFailure(error)) {
      keeper.reset()
    }
    if (isSlotGone(error)) {
      selectedTime.value = null
      await loadTimes()
      $q.notify?.({ type: 'warning', message: guestErrorMessage(error) })
    }
  } finally {
    submitting.value = false
  }
}

async function copyManageLink() {
  try {
    await copyToClipboard(manageUrl.value)
    $q.notify?.({ type: 'positive', message: 'Link copied.' })
  } catch {
    $q.notify?.({ type: 'warning', message: 'Copy failed. Use "View my booking" and bookmark it.' })
  }
}

function startOver() {
  confirmation.value = null
  selectedTime.value = null
  submitError.value = null
  Object.assign(guest, blankGuest())
  loadTimes()
}

watch([date, partySize], loadTimes)
watch(
  () => [props.tenantSlug, props.venueSlug],
  async () => {
    confirmation.value = null
    availability.value = null
    await loadVenue()
  },
  { immediate: true },
)

onBeforeUnmount(() => timesController?.abort())
</script>

<style scoped>
.public-column {
  max-width: 560px;
  margin: 0 auto;
}
.time-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(76px, 1fr));
  gap: 8px;
}
.hp-field {
  position: absolute;
  left: -10000px;
  width: 1px;
  height: 1px;
  overflow: hidden;
}
</style>
