<template>
  <q-page padding>
    <div class="text-h5 q-mb-md">Account</div>

    <q-card flat bordered class="account-card">
      <q-card-section>
        <div class="text-subtitle1">{{ auth.me?.displayName }}</div>
        <div class="text-grey-7">{{ auth.me?.email }}</div>
      </q-card-section>
      <q-separator />
      <q-card-section>
        <div class="text-subtitle2 q-mb-sm">Change password</div>
        <q-form class="q-gutter-sm" data-test="change-password-form" @submit.prevent="submit">
          <q-banner
            v-if="error && !Object.keys(fieldErrors).length"
            dense
            rounded
            class="bg-red-1 text-negative"
          >
            {{ error.message }}
          </q-banner>
          <q-input
            v-model="currentPassword"
            type="password"
            label="Current password"
            autocomplete="current-password"
            outlined
            :error="!!fieldError('currentPassword')"
            :error-message="fieldError('currentPassword')"
          />
          <q-input
            v-model="newPassword"
            type="password"
            label="New password"
            hint="At least 12 characters. A memorable passphrase works well."
            autocomplete="new-password"
            outlined
            :error="!!fieldError('newPassword')"
            :error-message="fieldError('newPassword')"
          />
          <q-input
            v-model="confirmPassword"
            type="password"
            label="Confirm new password"
            autocomplete="new-password"
            outlined
            :rules="[(v) => v === newPassword || 'Passwords do not match']"
            lazy-rules
          />
          <q-btn
            type="submit"
            color="primary"
            unelevated
            label="Update password"
            :loading="loading"
          />
          <div class="text-caption text-grey-7">Other signed-in devices will be signed out.</div>
        </q-form>
      </q-card-section>
    </q-card>
  </q-page>
</template>

<script setup>
import { ref } from 'vue'
import { useQuasar } from 'quasar'
import { useAuthStore } from '@/stores/auth.js'
import { useApiAction } from '@/composables/useApiAction.js'

defineOptions({ name: 'AccountPage' })

const auth = useAuthStore()
const $q = useQuasar()
const currentPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')

const { loading, error, fieldErrors, fieldError, run } = useApiAction((c, n) =>
  auth.changePassword(c, n).then(() => true),
)

async function submit() {
  if (newPassword.value !== confirmPassword.value) {
    return
  }
  if (await run(currentPassword.value, newPassword.value)) {
    currentPassword.value = ''
    newPassword.value = ''
    confirmPassword.value = ''
    $q.notify?.({ type: 'positive', message: 'Password updated.' })
  }
}
</script>

<style scoped>
.account-card {
  max-width: 520px;
}
</style>
