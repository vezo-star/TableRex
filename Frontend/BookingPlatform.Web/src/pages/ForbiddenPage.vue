<template>
  <q-page class="flex flex-center column q-pa-lg text-center">
    <q-icon name="block" size="56px" color="grey-6" />
    <div class="text-h6 q-mt-md">You don't have access to this page</div>
    <p class="text-grey-7">Ask an owner or administrator if you need this permission.</p>
    <q-btn
      color="primary"
      unelevated
      label="Go to dashboard"
      :to="{ name: RouteNames.DASHBOARD }"
    />
  </q-page>
</template>

<script setup>
import { RouteNames } from '@/constants/navigation.js'

defineOptions({ name: 'ForbiddenPage' })
</script>
