<template>
  <q-layout view="hHh lpR fFf">
    <q-header class="tr-public-header">
      <q-toolbar>
        <q-icon name="event_available" color="secondary" size="sm" class="q-mr-sm" />
        <q-toolbar-title class="text-subtitle1 public-title">Online booking</q-toolbar-title>
      </q-toolbar>
    </q-header>

    <q-page-container>
      <router-view />
    </q-page-container>

    <q-footer class="tr-public-footer">
      <div class="q-pa-sm text-caption row items-center justify-center no-wrap">
        <span class="q-mr-sm">Powered by</span>
        <TableRexLogo :size="20" />
      </div>
    </q-footer>
  </q-layout>
</template>

<script setup>
import TableRexLogo from '@/components/brand/TableRexLogo.vue'

defineOptions({ name: 'PublicLayout' })
</script>

<style scoped>
.public-title {
  font-family: var(--tr-serif);
  font-weight: 500;
}
</style>
