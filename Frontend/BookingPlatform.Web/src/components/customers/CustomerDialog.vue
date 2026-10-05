<template>
  <q-dialog :model-value="modelValue" @update:model-value="$emit('update:modelValue', $event)">
    <q-card class="customer-dialog" data-test="customer-dialog">
      <q-form @submit.prevent="save">
        <q-card-section class="text-h6">{{
          customer ? 'Edit customer' : 'New customer'
        }}</q-card-section>
        <q-card-section class="q-pt-none">
          <div class="row q-col-gutter-sm">
            <q-input
              v-model="form.firstName"
              class="col-6"
              label="First name *"
              outlined
              dense
              maxlength="100"
              :error="!!fieldError('firstName')"
              :error-message="fieldError('firstName')"
              data-test="customer-first-name"
            />
            <q-input
              v-model="form.lastName"
              class="col-6"
              label="Last name"
              outlined
              dense
              maxlength="100"
              :error="!!fieldError('lastName')"
              :error-message="fieldError('lastName')"
            />
            <q-input
              v-model="form.email"
              class="col-12 col-sm-6"
              type="email"
              label="Email"
              outlined
              dense
              maxlength="254"
              :error="!!fieldError('email')"
              :error-message="fieldError('email')"
              data-test="customer-email"
            />
            <q-input
              v-model="form.phone"
              class="col-12 col-sm-6"
              label="Phone"
              outlined
              dense
              maxlength="40"
              :error="!!fieldError('phone')"
              :error-message="fieldError('phone')"
              data-test="customer-phone"
            />
            <q-input
              v-model="form.notes"
              class="col-12"
              type="textarea"
              autogrow
              label="Notes (staff only)"
              outlined
              dense
              maxlength="4000"
              :error="!!fieldError('notes')"
              :error-message="fieldError('notes')"
            />
            <q-checkbox
              v-model="form.marketingConsent"
              class="col-12"
              label="Agreed to receive marketing"
              dense
            />
          </div>
          <q-banner
            v-if="error && !hasFieldErrors"
            rounded
            class="bg-red-1 text-negative q-mt-sm"
            data-test="customer-error"
          >
            {{ error.message }}
            <template v-if="duplicateId" #action>
              <q-btn
                flat
                no-caps
                color="negative"
                label="Open existing customer"
                :to="{ name: RouteNames.CUSTOMER_DETAILS, params: { customerId: duplicateId } }"
              />
            </template>
          </q-banner>
        </q-card-section>
        <q-card-actions align="right">
          <q-btn v-close-popup flat no-caps label="Cancel" />
          <q-btn
            type="submit"
            color="primary"
            unelevated
            no-caps
            label="Save"
            :loading="saving"
            data-test="customer-save"
          />
        </q-card-actions>
      </q-form>
    </q-card>
  </q-dialog>
</template>

<script setup>
import { computed, reactive, ref, watch } from 'vue'
import { ErrorCodes } from '@/constants/errorCodes.js'
import { RouteNames } from '@/constants/navigation.js'
import { customerService } from '@/services/customerService.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'CustomerDialog' })

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  /** Existing customer to edit; null creates a new one. */
  customer: { type: Object, default: null },
})
const emit = defineEmits(['update:modelValue', 'saved'])

const form = reactive({
  firstName: '',
  lastName: '',
  email: '',
  phone: '',
  notes: '',
  marketingConsent: false,
})
const saving = ref(false)
const error = ref(null)

const hasFieldErrors = computed(() => Object.keys(error.value?.errors ?? {}).length > 0)
const duplicateId = computed(() =>
  error.value?.code === ErrorCodes.EMAIL_TAKEN
    ? (error.value.extensions?.customerId ?? null)
    : null,
)

function fieldError(field) {
  return error.value?.errors?.[field]?.[0] ?? null
}

async function save() {
  error.value = null
  const payload = {
    firstName: form.firstName.trim(),
    lastName: form.lastName.trim() || null,
    email: form.email.trim() || null,
    phone: form.phone.trim() || null,
    notes: form.notes.trim() || null,
    marketingConsent: form.marketingConsent,
  }
  saving.value = true
  try {
    const saved = props.customer
      ? await customerService.update(props.customer.id, payload)
      : await customerService.create(payload)
    emit('saved', saved)
    emit('update:modelValue', false)
  } catch (e) {
    error.value = normaliseError(e)
  } finally {
    saving.value = false
  }
}

watch(
  () => props.modelValue,
  (open) => {
    if (!open) {
      return
    }
    error.value = null
    const c = props.customer
    Object.assign(form, {
      firstName: c?.firstName ?? '',
      lastName: c?.lastName ?? '',
      email: c?.email ?? '',
      phone: c?.phone ?? '',
      notes: c?.notes ?? '',
      marketingConsent: c?.marketingConsent ?? false,
    })
  },
  { immediate: true },
)
</script>

<style scoped>
.customer-dialog {
  width: 560px;
  max-width: 95vw;
}
</style>
