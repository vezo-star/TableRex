<template>
  <q-card-section data-test="deposit-section">
    <div class="row items-center no-wrap q-mb-xs">
      <div class="text-subtitle2 col">Deposit</div>
      <q-chip
        dense
        square
        :color="PaymentStatusColor[payment.status] ?? 'grey-7'"
        text-color="white"
        data-test="deposit-status"
      >
        {{ PaymentStatusText[payment.status] ?? payment.status }}
      </q-chip>
    </div>
    <div class="text-h6" data-test="deposit-amount">
      {{ formatMoney(payment.amountMinor, payment.currency) }}
    </div>

    <template v-if="payment.canPay">
      <p class="text-body2 text-grey-8 q-my-sm">
        Your table is held until
        <strong>{{ formatInstant(payment.payBy, timeZone) }}</strong
        >. Pay the deposit to confirm the booking; unpaid bookings are released automatically.
      </p>
      <q-banner
        v-if="error"
        dense
        rounded
        class="bg-red-1 text-negative q-mb-sm"
        data-test="deposit-error"
      >
        {{ guestErrorMessage(error) }}
      </q-banner>
      <q-btn
        color="primary"
        unelevated
        no-caps
        size="lg"
        class="full-width"
        icon="lock"
        :label="`Pay ${formatMoney(payment.amountMinor, payment.currency)}`"
        :loading="starting"
        data-test="pay-deposit"
        @click="pay"
      />
    </template>
    <p v-else-if="payment.status === 'SUCCEEDED'" class="text-body2 text-grey-8 q-my-sm">
      Thank you, your deposit has been received.
    </p>
    <p v-else-if="payment.status === 'EXPIRED'" class="text-body2 text-grey-8 q-my-sm">
      The deposit was not paid in time, so the table was released.
    </p>
  </q-card-section>
</template>

<script setup>
import { ref } from 'vue'
import { publicBookingService } from '@/services/publicBookingService.js'
import { normaliseError } from '@/utils/problemDetails.js'
import { formatInstant, guestErrorMessage } from '@/utils/publicBooking.js'
import {
  formatMoney,
  PaymentStatusColor,
  PaymentStatusText,
  rememberPaymentToken,
} from '@/utils/payments.js'

defineOptions({ name: 'DepositPayCard' })

const props = defineProps({
  payment: { type: Object, required: true },
  manageToken: { type: String, required: true },
  timeZone: { type: String, default: undefined },
  /** Overridable for tests; defaults to a full-page navigation to the provider. */
  navigate: { type: Function, default: (url) => window.location.assign(url) },
})

const starting = ref(false)
const error = ref(null)

async function pay() {
  starting.value = true
  error.value = null
  try {
    const { redirectUrl } = await publicBookingService.startPayment(props.manageToken)
    rememberPaymentToken(props.manageToken)
    props.navigate(redirectUrl)
  } catch (e) {
    error.value = normaliseError(e)
    starting.value = false
  }
}
</script>
