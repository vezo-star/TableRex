<template>
  <q-page class="public-page q-pa-md">
    <div class="public-column">
      <q-banner rounded class="bg-amber-2 text-dark q-mb-md" data-test="simulated-banner">
        <template #avatar><q-icon name="science" /></template>
        Test checkout. No real money moves: this page stands in for a payment provider in
        development.
      </q-banner>

      <ErrorState
        v-if="!checkout"
        message="This checkout link is incomplete."
        data-test="checkout-invalid"
      />

      <q-card v-else flat bordered data-test="simulated-checkout">
        <q-card-section>
          <div class="text-caption text-grey-7">Amount due</div>
          <div class="text-h5" data-test="checkout-amount">
            {{ formatMoney(checkout.amountMinor, checkout.currency) }}
          </div>
        </q-card-section>
        <q-card-section v-if="error" class="q-pt-none">
          <q-banner dense rounded class="bg-red-1 text-negative" data-test="checkout-error">
            {{ guestErrorMessage(error) }}
          </q-banner>
        </q-card-section>
        <q-card-actions vertical class="q-px-md q-pb-md q-gutter-y-sm">
          <q-btn
            color="positive"
            unelevated
            no-caps
            size="lg"
            label="Pay now"
            :loading="busy === 'SUCCEEDED'"
            :disable="!!busy"
            data-test="simulate-pay"
            @click="complete('SUCCEEDED')"
          />
          <q-btn
            outline
            color="negative"
            no-caps
            label="Simulate a declined card"
            :loading="busy === 'FAILED'"
            :disable="!!busy"
            data-test="simulate-decline"
            @click="complete('FAILED')"
          />
          <q-btn
            flat
            no-caps
            color="grey-8"
            label="Cancel and go back"
            :loading="busy === 'CANCELLED'"
            :disable="!!busy"
            data-test="simulate-cancel"
            @click="complete('CANCELLED')"
          />
        </q-card-actions>
      </q-card>
    </div>
  </q-page>
</template>

<script setup>
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ErrorState from '@/components/common/ErrorState.vue'
import { publicBookingService } from '@/services/publicBookingService.js'
import { normaliseError } from '@/utils/problemDetails.js'
import { guestErrorMessage } from '@/utils/publicBooking.js'
import { formatMoney, parseSimulatedCheckout, safeReturnUrl } from '@/utils/payments.js'
import { RouteNames } from '@/constants/navigation.js'

defineOptions({ name: 'SimulatedCheckoutPage' })

const route = useRoute()
const router = useRouter()
const checkout = computed(() => parseSimulatedCheckout(route.hash))
const busy = ref(null)
const error = ref(null)

async function complete(outcome) {
  busy.value = outcome
  error.value = null
  try {
    await publicBookingService.completeSimulatedCheckout({
      paymentId: checkout.value.paymentId,
      token: checkout.value.token,
      outcome,
    })
    const target = safeReturnUrl(checkout.value.returnUrl)
    await router.replace(target ?? { name: RouteNames.PAYMENT_RETURN })
  } catch (e) {
    error.value = normaliseError(e)
    busy.value = null
  }
}
</script>

<style scoped>
.public-column {
  max-width: 480px;
  margin: 0 auto;
}
</style>
