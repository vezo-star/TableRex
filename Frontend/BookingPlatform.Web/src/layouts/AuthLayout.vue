<template>
  <q-layout view="hHh lpR fFf">
    <q-page-container>
      <q-page class="flex flex-center bg-grey-2">
        <q-card flat bordered class="auth-card q-pa-lg">
          <div class="text-h6 text-center q-mb-md">Booking Platform</div>
          <router-view />
        </q-card>
      </q-page>
    </q-page-container>
  </q-layout>
</template>

<script setup>
defineOptions({ name: 'AuthLayout' })
</script>

<style scoped>
.auth-card {
  width: 100%;
  max-width: 400px;
}
</style>
