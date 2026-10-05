<template>
  <div class="q-gutter-md" data-test="forgot-password">
    <div class="text-subtitle1 text-center text-grey-8">Reset your password</div>

    <template v-if="sent">
      <q-banner rounded class="bg-green-1 text-positive" role="status" data-test="forgot-sent">
        If an account exists for <strong>{{ email }}</strong
        >, we've sent a link to reset its password. The link works once and expires in 30 minutes.
      </q-banner>
      <div class="text-caption text-grey-7">
        No email after a few minutes? Check your spam folder or ask your administrator.
      </div>
    </template>

    <q-form v-else class="q-gutter-md" data-test="forgot-form" @submit.prevent="submit">
      <div class="text-body2 text-grey-8">
        Enter the email you sign in with and we'll send you a reset link.
      </div>
      <q-banner
        v-if="error && !fieldError('email')"
        dense
        rounded
        class="bg-red-1 text-negative"
        role="alert"
        data-test="forgot-error"
      >
        {{ errorMessage }}
      </q-banner>
      <q-input
        v-model.trim="email"
        type="email"
        label="Email"
        autocomplete="username"
        outlined
        autofocus
        :rules="[(v) => !!v || 'Email is required']"
        lazy-rules
        :error="!!fieldError('email')"
        :error-message="fieldError('email')"
        data-test="forgot-email"
      />
      <q-btn
        type="submit"
        color="primary"
        label="Send reset link"
        class="full-width"
        unelevated
        :loading="loading"
        data-test="forgot-submit"
      />
    </q-form>

    <div class="text-center">
      <router-link :to="{ name: RouteNames.LOGIN }" data-test="back-to-login">
        Back to sign in
      </router-link>
    </div>
  </div>
</template>

<script setup>
import { computed, ref } from 'vue'
import { authService } from '@/services/authService.js'
import { useApiAction } from '@/composables/useApiAction.js'
import { ErrorCodes } from '@/constants/errorCodes.js'
import { RouteNames } from '@/constants/navigation.js'

defineOptions({ name: 'ForgotPasswordPage' })

const email = ref('')
const sent = ref(false)

const { loading, error, fieldError, run } = useApiAction((value) =>
  authService.forgotPassword(value).then(() => true),
)

const errorMessage = computed(() =>
  error.value?.code === ErrorCodes.RATE_LIMITED
    ? 'Too many reset requests. Please wait a few minutes and try again.'
    : (error.value?.message ?? ''),
)

async function submit() {
  if (await run(email.value)) {
    sent.value = true
  }
}
</script>
