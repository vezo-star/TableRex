<template>
  <q-page class="public-page q-pa-md">
    <div class="public-column">
      <h1 class="text-h5 q-mt-none q-mb-md">Payment</h1>
      <q-card flat bordered data-test="payment-return">
        <q-card-section class="text-body2">
          We could not find your booking in this browser tab. Open the booking link from your
          confirmation to see whether the deposit was received.
        </q-card-section>
      </q-card>
    </div>
  </q-page>
</template>

<script setup>
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { RouteNames } from '@/constants/navigation.js'
import { takePaymentToken } from '@/utils/payments.js'

defineOptions({ name: 'PaymentReturnPage' })

const router = useRouter()

// Providers send the guest back here without the manage token; it was parked in this tab before leaving.
onMounted(() => {
  const token = takePaymentToken()
  if (token) {
    router.replace({ name: RouteNames.PUBLIC_MANAGE_BOOKING, hash: `#${encodeURIComponent(token)}` })
  }
})
</script>

<style scoped>
.public-column {
  max-width: 560px;
  margin: 0 auto;
}
</style>
