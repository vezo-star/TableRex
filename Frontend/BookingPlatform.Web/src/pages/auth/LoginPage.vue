<template>
  <q-form class="q-gutter-md" data-test="login-form" @submit.prevent="submit">
    <div class="text-subtitle1 text-center text-grey-8">Sign in to your workspace</div>

    <q-banner
      v-if="error"
      dense
      rounded
      class="bg-red-1 text-negative"
      role="alert"
      data-test="login-error"
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
      data-test="login-email"
    />
    <q-input
      v-model="password"
      :type="showPassword ? 'text' : 'password'"
      label="Password"
      autocomplete="current-password"
      outlined
      :rules="[(v) => !!v || 'Password is required']"
      lazy-rules
      data-test="login-password"
    >
      <template #append>
        <q-icon
          :name="showPassword ? 'visibility_off' : 'visibility'"
          class="cursor-pointer"
          :aria-label="showPassword ? 'Hide password' : 'Show password'"
          @click="showPassword = !showPassword"
        />
      </template>
    </q-input>

    <q-btn
      type="submit"
      color="primary"
      label="Sign in"
      class="full-width"
      unelevated
      :loading="loading"
      data-test="login-submit"
    />
  </q-form>
</template>

<script setup>
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth.js'
import { useApiAction } from '@/composables/useApiAction.js'
import { homeFor, permittedRedirect, safeRedirect } from '@/router/guards.js'
import { ErrorCodes } from '@/constants/errorCodes.js'
import { RouteNames } from '@/constants/navigation.js'

defineOptions({ name: 'LoginPage' })

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const email = ref('')
const password = ref('')
const showPassword = ref(false)

const { loading, error, run } = useApiAction((e, p) => auth.login(e, p))

const errorMessage = computed(() => {
  switch (error.value?.code) {
    case ErrorCodes.INVALID_CREDENTIALS:
      return 'The email or password is incorrect.'
    case ErrorCodes.ACCOUNT_LOCKED:
      return 'Too many failed attempts. Your account is temporarily locked; try again later.'
    case ErrorCodes.RATE_LIMITED:
      return 'Too many sign-in attempts. Please wait a moment and try again.'
    default:
      return error.value?.message ?? ''
  }
})

async function submit() {
  const session = await run(email.value, password.value)
  if (!session) {
    password.value = ''
    return
  }
  if (auth.requiresTenantSelection) {
    const redirect = safeRedirect(route.query.redirect)
    await router.replace({
      name: RouteNames.SELECT_TENANT,
      query: redirect ? { redirect } : {},
    })
    return
  }
  await router.replace(permittedRedirect(router, auth, route.query.redirect) ?? homeFor(auth))
}
</script>
