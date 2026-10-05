<template>
  <q-page class="public-page q-pa-md">
    <div class="public-column">
      <h1 class="text-h5 q-mt-none q-mb-md">Your booking</h1>

      <ErrorState
        v-if="!token"
        message="This link is incomplete. Open the full link from your booking confirmation."
        data-test="missing-token"
      />

      <div v-else-if="loading" class="flex flex-center q-pa-xl" data-test="booking-loading">
        <q-spinner size="lg" color="primary" />
      </div>

      <ErrorState
        v-else-if="loadError"
        :message="guestErrorMessage(loadError)"
        :retryable="loadError.status !== 404"
        @retry="load"
      />

      <q-card v-else-if="booking" flat bordered data-test="manage-booking">
        <q-card-section>
          <div class="text-caption text-grey-7">Booking at</div>
          <div class="text-h6">{{ booking.venueName }}</div>
          <q-chip
            dense
            square
            :color="statusColor"
            text-color="white"
            class="q-ml-none"
            data-test="booking-status"
          >
            {{ BookingStatusText[booking.status] ?? booking.status }}
          </q-chip>
        </q-card-section>
        <q-separator />
        <q-list dense class="q-py-sm">
          <q-item>
            <q-item-section>Reference</q-item-section>
            <q-item-section side class="text-weight-bold text-dark">
              {{ booking.reference }}
            </q-item-section>
          </q-item>
          <q-item>
            <q-item-section>Name</q-item-section>
            <q-item-section side class="text-dark">{{ booking.guestFirstName }}</q-item-section>
          </q-item>
          <q-item>
            <q-item-section>Date</q-item-section>
            <q-item-section side class="text-dark">{{ formatLongDate(booking.date) }}</q-item-section>
          </q-item>
          <q-item>
            <q-item-section>Time</q-item-section>
            <q-item-section side class="text-dark">{{ booking.time }}</q-item-section>
          </q-item>
          <q-item>
            <q-item-section>Guests</q-item-section>
            <q-item-section side class="text-dark">{{ booking.partySize }}</q-item-section>
          </q-item>
          <q-item v-if="booking.specialRequests">
            <q-item-section>
              <q-item-label caption>Special requests</q-item-label>
              <q-item-label class="pre-wrap">{{ booking.specialRequests }}</q-item-label>
            </q-item-section>
          </q-item>
        </q-list>
        <template v-if="booking.payment">
          <q-separator />
          <DepositPayCard
            :payment="booking.payment"
            :manage-token="token"
            :time-zone="booking.timeZone"
          />
        </template>
        <q-separator />

        <q-card-section v-if="booking.status === 'CANCELLED'" data-test="cancelled-note">
          This booking was cancelled
          <span v-if="booking.cancelledAt">
            on {{ formatInstant(booking.cancelledAt, booking.timeZone) }}</span
          >.
          <div class="q-mt-sm">
            <q-btn
              flat
              no-caps
              color="primary"
              label="Book again"
              :to="{
                name: RouteNames.PUBLIC_BOOKING,
                params: { tenantSlug: booking.tenantSlug, venueSlug: booking.venueSlug },
              }"
            />
          </div>
        </q-card-section>

        <q-card-section v-else-if="booking.canCancel">
          <div class="text-body2 text-grey-8 q-mb-sm">
            You can cancel online until
            {{ formatInstant(booking.cancelUntil, booking.timeZone) }}.
          </div>
          <q-banner
            v-if="cancelError"
            dense
            rounded
            class="bg-red-1 text-negative q-mb-sm"
            data-test="cancel-error"
          >
            {{ guestErrorMessage(cancelError) }}
          </q-banner>
          <q-btn
            outline
            color="negative"
            no-caps
            class="full-width"
            label="Cancel booking"
            :loading="cancelling"
            data-test="cancel-booking"
            @click="confirmCancel"
          />
        </q-card-section>

        <q-card-section v-else class="text-body2 text-grey-8" data-test="contact-venue">
          This booking can no longer be changed online.
          <template v-if="booking.venuePhone || booking.venueEmail">
            Please contact the venue:
            <a v-if="booking.venuePhone" :href="`tel:${booking.venuePhone}`">{{
              booking.venuePhone
            }}</a>
            <span v-if="booking.venuePhone && booking.venueEmail"> · </span>
            <a v-if="booking.venueEmail" :href="`mailto:${booking.venueEmail}`">{{
              booking.venueEmail
            }}</a>
          </template>
        </q-card-section>
      </q-card>
    </div>
  </q-page>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useQuasar } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import DepositPayCard from '@/components/payments/DepositPayCard.vue'
import { publicBookingService, tokenFromHash } from '@/services/publicBookingService.js'
import { RouteNames } from '@/constants/navigation.js'
import { normaliseError } from '@/utils/problemDetails.js'
import {
  BookingStatusText,
  formatInstant,
  formatLongDate,
  guestErrorMessage,
} from '@/utils/publicBooking.js'

defineOptions({ name: 'ManageBookingPage' })

const route = useRoute()
const $q = useQuasar()

const token = computed(() => tokenFromHash(route.hash))
const booking = ref(null)
const loading = ref(false)
const loadError = ref(null)
const cancelling = ref(false)
const cancelError = ref(null)

const statusColor = computed(() => {
  switch (booking.value?.status) {
    case 'CONFIRMED':
      return 'positive'
    case 'PENDING':
    case 'HELD':
      return 'warning'
    case 'CANCELLED':
    case 'NO_SHOW':
      return 'grey-7'
    default:
      return 'primary'
  }
})

async function load() {
  if (!token.value) {
    return
  }
  loading.value = true
  loadError.value = null
  try {
    booking.value = await publicBookingService.getBooking(token.value)
  } catch (e) {
    loadError.value = normaliseError(e)
    booking.value = null
  } finally {
    loading.value = false
  }
}

function confirmCancel() {
  $q.dialog({
    title: 'Cancel booking?',
    message: `Your booking on ${formatLongDate(booking.value.date)} at ${booking.value.time} will be cancelled.`,
    cancel: { label: 'Keep booking', flat: true, noCaps: true },
    ok: { label: 'Cancel booking', color: 'negative', unelevated: true, noCaps: true },
    persistent: true,
  }).onOk(cancel)
}

async function cancel() {
  cancelling.value = true
  cancelError.value = null
  try {
    booking.value = await publicBookingService.cancel(token.value)
    $q.notify?.({ type: 'positive', message: 'Your booking has been cancelled.' })
  } catch (e) {
    cancelError.value = normaliseError(e)
  } finally {
    cancelling.value = false
  }
}

watch(token, load, { immediate: true })
</script>

<style scoped>
.public-column {
  max-width: 560px;
  margin: 0 auto;
}
.pre-wrap {
  white-space: pre-wrap;
}
</style>
