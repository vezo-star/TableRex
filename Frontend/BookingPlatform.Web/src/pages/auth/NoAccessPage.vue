<template>
  <div class="q-gutter-md text-center">
    <q-icon name="lock_person" size="48px" color="grey-6" />
    <div class="text-subtitle1">No workspace access</div>
    <p class="text-grey-7">
      Your account is not an active member of any workspace. Ask an owner or administrator to add
      you.
    </p>
    <q-btn
      v-if="auth.isPlatformAdmin"
      color="primary"
      unelevated
      label="Platform administration"
      :to="{ name: RouteNames.PLATFORM_TENANTS }"
    />
    <q-btn flat label="Sign out" @click="signOut" />
  </div>
</template>

<script setup>
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth.js'
import { RouteNames } from '@/constants/navigation.js'

defineOptions({ name: 'NoAccessPage' })

const auth = useAuthStore()
const router = useRouter()

async function signOut() {
  await auth.logout()
  await router.replace({ name: RouteNames.LOGIN })
}
</script>
