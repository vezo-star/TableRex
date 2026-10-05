<template>
  <q-layout view="hHh lpR fFf">
    <q-header bordered class="bg-white text-dark">
      <q-toolbar>
        <q-toolbar-title>Book a table</q-toolbar-title>
      </q-toolbar>
    </q-header>

    <q-page-container>
      <router-view />
    </q-page-container>

    <q-footer bordered class="bg-white text-grey-7">
      <div class="q-pa-sm text-caption text-center">Powered by Booking Platform</div>
    </q-footer>
  </q-layout>
</template>

<script setup>
defineOptions({ name: 'PublicLayout' })
</script>
