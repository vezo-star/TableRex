<template>
  <div class="q-gutter-md" data-test="reset-password">
    <div class="text-subtitle1 text-center text-grey-8">Choose a new password</div>

    <template v-if="done">
      <q-banner rounded class="bg-green-1 text-positive" role="status" data-test="reset-done">
        Your password has been changed and every device has been signed out. Sign in with your new
        password.
      </q-banner>
      <q-btn
        color="primary"
        unelevated
        class="full-width"
        label="Sign in"
        :to="{ name: RouteNames.LOGIN }"
        data-test="reset-sign-in"
      />
    </template>

    <template v-else-if="!token || tokenRejected">
      <q-banner rounded class="bg-orange-1 text-warning" role="alert" data-test="reset-invalid">
        This reset link is invalid, has expired or was already used. Request a new one.
      </q-banner>
      <q-btn
        color="primary"
        unelevated
        class="full-width"
        label="Request a new link"
        :to="{ name: RouteNames.FORGOT_PASSWORD }"
        data-test="reset-request-new"
      />
    </template>

    <q-form v-else class="q-gutter-md" data-test="reset-form" @submit.prevent="submit">
      <q-banner
        v-if="error && !fieldError('newPassword')"
        dense
        rounded
        class="bg-red-1 text-negative"
        role="alert"
        data-test="reset-error"
      >
        {{ errorMessage }}
      </q-banner>
      <q-input
        v-model="newPassword"
        type="password"
        label="New password"
        hint="At least 12 characters. A memorable passphrase works well."
        autocomplete="new-password"
        outlined
        autofocus
        :rules="[(v) => !!v || 'Password is required']"
        lazy-rules
        :error="!!fieldError('newPassword')"
        :error-message="fieldError('newPassword')"
        data-test="reset-new"
      />
      <q-input
        v-model="confirmPassword"
        type="password"
        label="Confirm new password"
        autocomplete="new-password"
        outlined
        :rules="[(v) => v === newPassword || 'Passwords do not match']"
        lazy-rules
        data-test="reset-confirm"
      />
      <q-btn
        type="submit"
        color="primary"
        label="Set new password"
        class="full-width"
        unelevated
        :loading="loading"
        data-test="reset-submit"
      />
    </q-form>

    <div class="text-center">
      <router-link :to="{ name: RouteNames.LOGIN }">Back to sign in</router-link>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { authService } from '@/services/authService.js'
import { tokenFromHash } from '@/services/publicBookingService.js'
import { useApiAction } from '@/composables/useApiAction.js'
import { ErrorCodes } from '@/constants/errorCodes.js'
import { RouteNames } from '@/constants/navigation.js'

defineOptions({ name: 'ResetPasswordPage' })

const route = useRoute()
const router = useRouter()

// The token arrives in the URL fragment (never sent to servers); keep it in memory only.
const token = ref(tokenFromHash(route.hash))
const newPassword = ref('')
const confirmPassword = ref('')
const done = ref(false)

const { loading, error, fieldError, run } = useApiAction((t, p) =>
  authService.resetPassword(t, p).then(() => true),
)

const tokenRejected = computed(() => error.value?.code === ErrorCodes.INVALID_RESET_TOKEN)

const errorMessage = computed(() =>
  error.value?.code === ErrorCodes.RATE_LIMITED
    ? 'Too many attempts. Please wait a few minutes and try again.'
    : (error.value?.message ?? ''),
)

onMounted(() => {
  if (route.hash) {
    router.replace({ name: RouteNames.RESET_PASSWORD, hash: '' })
  }
})

async function submit() {
  if (newPassword.value !== confirmPassword.value) {
    return
  }
  if (await run(token.value, newPassword.value)) {
    token.value = ''
    newPassword.value = ''
    confirmPassword.value = ''
    done.value = true
  }
}
</script>
