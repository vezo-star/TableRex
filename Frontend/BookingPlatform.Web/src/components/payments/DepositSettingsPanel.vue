<template>
  <div data-test="deposit-settings">
    <ErrorState
      v-if="loadError"
      :message="loadError.message"
      :trace-id="loadError.traceId"
      retryable
      @retry="load"
    />
    <div v-else-if="!settings" class="flex flex-center q-pa-lg"><q-spinner size="md" /></div>

    <q-form v-else class="q-gutter-md deposit-form" @submit.prevent="save">
      <q-banner
        v-if="!settings.onlinePaymentsAvailable"
        dense
        rounded
        class="bg-amber-1 text-body2"
        data-test="payments-unavailable"
      >
        <template #avatar><q-icon name="warning" color="warning" /></template>
        Online payments are not configured for this platform. While a deposit is required, guests
        cannot book online; staff bookings are unaffected.
      </q-banner>

      <q-option-group
        v-model="form.depositPolicy"
        :options="DepositPolicyOptions"
        :disable="!canManage"
        data-test="deposit-policy"
      />

      <template v-if="form.depositPolicy !== DepositPolicies.NONE">
        <q-input
          v-model="form.amount"
          :label="form.depositPolicy === DepositPolicies.PER_GUEST ? 'Amount per guest' : 'Amount'"
          inputmode="decimal"
          outlined
          dense
          :prefix="form.currency"
          :readonly="!canManage"
          :error="!!fieldError('depositAmountMinor')"
          :error-message="fieldError('depositAmountMinor')"
          :rules="[validAmount]"
          data-test="deposit-amount"
        />
        <q-input
          v-model.number="form.paymentWindowMinutes"
          type="number"
          min="5"
          max="1440"
          label="Time to pay (minutes)"
          hint="Unpaid bookings are released after this long"
          outlined
          dense
          :readonly="!canManage"
          :error="!!fieldError('paymentWindowMinutes')"
          :error-message="fieldError('paymentWindowMinutes')"
          data-test="deposit-window"
        />
      </template>

      <q-banner v-if="saveError && !saveError.errors" dense rounded class="bg-red-1 text-negative">
        {{ saveError.message }}
      </q-banner>

      <div v-if="canManage">
        <q-btn
          type="submit"
          color="primary"
          unelevated
          no-caps
          label="Save deposit settings"
          :loading="saving"
          data-test="deposit-save"
        />
      </div>
    </q-form>
  </div>
</template>

<script setup>
import { reactive, ref, watch } from 'vue'
import { useQuasar } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import { paymentService } from '@/services/paymentService.js'
import { normaliseError } from '@/utils/problemDetails.js'
import {
  DepositPolicies,
  DepositPolicyOptions,
  toMajorText,
  toMinorUnits,
} from '@/utils/payments.js'

defineOptions({ name: 'DepositSettingsPanel' })

const props = defineProps({
  venueId: { type: String, required: true },
  canManage: { type: Boolean, default: false },
})

const $q = useQuasar()
const settings = ref(null)
const loadError = ref(null)
const saving = ref(false)
const saveError = ref(null)
const form = reactive({
  depositPolicy: DepositPolicies.NONE,
  amount: '',
  currency: 'ZAR',
  paymentWindowMinutes: 30,
})

function fieldError(field) {
  return saveError.value?.errors?.[field]?.[0] ?? null
}

function validAmount(value) {
  const minor = toMinorUnits(value)
  if (minor === null) {
    return 'Enter an amount such as 100.00'
  }
  return (minor >= 100 && minor <= 10_000_000) || 'Between 1.00 and 100 000.00'
}

function fill(value) {
  settings.value = value
  form.depositPolicy = value.depositPolicy
  form.amount = toMajorText(value.depositAmountMinor)
  form.currency = value.currency
  form.paymentWindowMinutes = value.paymentWindowMinutes
}

async function load() {
  loadError.value = null
  settings.value = null
  try {
    fill(await paymentService.getSettings(props.venueId))
  } catch (e) {
    loadError.value = normaliseError(e)
  }
}

async function save() {
  const none = form.depositPolicy === DepositPolicies.NONE
  if (!none && validAmount(form.amount) !== true) {
    return
  }
  saving.value = true
  saveError.value = null
  try {
    fill(
      await paymentService.updateSettings(props.venueId, {
        depositPolicy: form.depositPolicy,
        depositAmountMinor: none ? null : toMinorUnits(form.amount),
        currency: form.currency,
        paymentWindowMinutes: Number(form.paymentWindowMinutes),
      }),
    )
    $q.notify?.({ type: 'positive', message: 'Deposit settings saved.' })
  } catch (e) {
    saveError.value = normaliseError(e)
  } finally {
    saving.value = false
  }
}

watch(() => props.venueId, load, { immediate: true })
</script>

<style scoped>
.deposit-form {
  max-width: 420px;
}
</style>
