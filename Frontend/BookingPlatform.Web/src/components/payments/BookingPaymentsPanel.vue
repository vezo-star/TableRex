<template>
  <div data-test="booking-payments">
    <q-inner-loading :showing="loading" />
    <ErrorState
      v-if="loadError"
      :message="loadError.message"
      :trace-id="loadError.traceId"
      retryable
      @retry="load"
    />
    <div
      v-else-if="payments && !payments.length"
      class="text-body2 text-grey-7"
      data-test="no-payments"
    >
      No payments for this booking.
    </div>

    <q-card
      v-for="payment in payments ?? []"
      :key="payment.id"
      flat
      bordered
      class="q-mb-md"
      data-test="payment-card"
    >
      <q-card-section class="q-pb-sm">
        <div class="row items-center no-wrap">
          <div class="col">
            <div class="text-subtitle1">
              {{ formatMoney(payment.amountMinor, payment.currency) }}
            </div>
            <div class="text-caption text-grey-7">
              {{ payment.purpose === 'DEPOSIT' ? 'Deposit' : payment.purpose }} ·
              {{ payment.provider }}
            </div>
          </div>
          <q-chip
            dense
            square
            :color="PaymentStatusColor[payment.status] ?? 'grey-7'"
            text-color="white"
            data-test="payment-status"
          >
            {{ PaymentStatusText[payment.status] ?? payment.status }}
          </q-chip>
        </div>
        <q-banner
          v-if="payment.requiresAttention"
          dense
          rounded
          class="bg-amber-1 text-body2 q-mt-sm"
          data-test="payment-attention"
        >
          <template #avatar><q-icon name="warning" color="warning" /></template>
          {{ payment.failureReason || 'This payment needs attention.' }}
        </q-banner>
        <div
          v-else-if="payment.failureReason && payment.status !== 'SUCCEEDED'"
          class="text-caption text-grey-8 q-mt-xs"
        >
          {{ payment.failureReason }}
        </div>
        <div
          v-if="payment.refundedAmountMinor"
          class="text-body2 q-mt-xs"
          data-test="payment-refunded"
        >
          Refunded {{ formatMoney(payment.refundedAmountMinor, payment.currency) }}
        </div>
      </q-card-section>

      <q-list v-if="payment.refunds.length" dense separator class="q-px-sm">
        <q-item v-for="refund in payment.refunds" :key="refund.id" data-test="refund-row">
          <q-item-section>
            <q-item-label>{{ formatMoney(refund.amountMinor, payment.currency) }}</q-item-label>
            <q-item-label caption>{{ refund.reason || 'No reason given' }}</q-item-label>
          </q-item-section>
          <q-item-section side>
            <q-item-label :class="refund.status === 'FAILED' ? 'text-negative' : ''">
              {{ RefundStatusText[refund.status] ?? refund.status }}
            </q-item-label>
          </q-item-section>
        </q-item>
      </q-list>

      <q-card-actions v-if="canRefund && payment.refundableAmountMinor > 0" align="right">
        <q-btn
          flat
          no-caps
          color="negative"
          icon="undo"
          label="Refund"
          data-test="refund-open"
          @click="openRefund(payment)"
        />
      </q-card-actions>
    </q-card>

    <q-dialog v-model="refundOpen" persistent>
      <q-card style="min-width: 300px; max-width: 420px" data-test="refund-dialog">
        <q-form @submit.prevent="submitRefund">
          <q-card-section>
            <div class="text-h6">Refund</div>
            <div class="text-body2 text-grey-8">
              Up to
              {{ formatMoney(refundTarget?.refundableAmountMinor, refundTarget?.currency) }} can be
              refunded.
            </div>
          </q-card-section>
          <q-card-section class="q-pt-none q-gutter-y-sm">
            <q-banner
              v-if="refundError"
              dense
              rounded
              class="bg-red-1 text-negative"
              data-test="refund-error"
            >
              {{ refundError.message }}
            </q-banner>
            <q-input
              v-model="refundForm.amount"
              label="Amount"
              inputmode="decimal"
              outlined
              dense
              :prefix="refundTarget?.currency"
              :rules="[validAmount]"
              data-test="refund-amount"
            />
            <q-input
              v-model="refundForm.reason"
              label="Reason (optional)"
              outlined
              dense
              maxlength="500"
              data-test="refund-reason"
            />
          </q-card-section>
          <q-card-actions align="right">
            <q-btn v-close-popup flat no-caps label="Cancel" :disable="refunding" />
            <q-btn
              type="submit"
              color="negative"
              unelevated
              no-caps
              label="Refund"
              :loading="refunding"
              data-test="refund-submit"
            />
          </q-card-actions>
        </q-form>
      </q-card>
    </q-dialog>
  </div>
</template>

<script setup>
import { computed, reactive, ref, watch } from 'vue'
import { useQuasar } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import { Permissions } from '@/constants/permissions.js'
import { paymentService } from '@/services/paymentService.js'
import { createIdempotencyKeeper, isRetryableFailure } from '@/services/idempotency.js'
import { useAuthStore } from '@/stores/auth.js'
import { normaliseError } from '@/utils/problemDetails.js'
import {
  formatMoney,
  PaymentStatusColor,
  PaymentStatusText,
  RefundStatusText,
  toMajorText,
  toMinorUnits,
} from '@/utils/payments.js'

defineOptions({ name: 'BookingPaymentsPanel' })

const props = defineProps({
  bookingId: { type: String, required: true },
})

const $q = useQuasar()
const auth = useAuthStore()
const canRefund = computed(() => auth.hasPermission(Permissions.PAYMENT_REFUND))

const payments = ref(null)
const loading = ref(false)
const loadError = ref(null)

const refundOpen = ref(false)
const refundTarget = ref(null)
const refundForm = reactive({ amount: '', reason: '' })
const refunding = ref(false)
const refundError = ref(null)
const keeper = createIdempotencyKeeper()

async function load() {
  loading.value = true
  loadError.value = null
  try {
    payments.value = await paymentService.listForBooking(props.bookingId)
  } catch (e) {
    loadError.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

function validAmount(value) {
  const minor = toMinorUnits(value)
  if (minor === null) {
    return 'Enter an amount such as 50.00'
  }
  return minor <= (refundTarget.value?.refundableAmountMinor ?? 0) || 'More than can be refunded'
}

function openRefund(payment) {
  refundTarget.value = payment
  refundForm.amount = toMajorText(payment.refundableAmountMinor)
  refundForm.reason = ''
  refundError.value = null
  keeper.reset()
  refundOpen.value = true
}

async function submitRefund() {
  const amountMinor = toMinorUnits(refundForm.amount)
  if (amountMinor === null || validAmount(refundForm.amount) !== true) {
    return
  }
  const payload = { amountMinor, reason: refundForm.reason.trim() || null }
  refunding.value = true
  refundError.value = null
  try {
    const refund = await paymentService.refund(
      props.bookingId,
      refundTarget.value.id,
      payload,
      keeper.keyFor({ paymentId: refundTarget.value.id, ...payload }),
    )
    keeper.reset()
    refundOpen.value = false
    $q.notify?.({
      type: refund.status === 'SUCCEEDED' ? 'positive' : 'warning',
      message:
        refund.status === 'SUCCEEDED'
          ? `Refunded ${formatMoney(refund.amountMinor, refundTarget.value.currency)}`
          : 'The refund was sent but its outcome is not known yet. Check the payment provider.',
    })
    await load()
  } catch (e) {
    const error = normaliseError(e)
    refundError.value = error
    if (!isRetryableFailure(error)) {
      keeper.reset()
    }
  } finally {
    refunding.value = false
  }
}

watch(() => props.bookingId, load, { immediate: true })

defineExpose({ reload: load })
</script>
