<template>
  <q-layout view="hHh lpR fFf">
    <q-header elevated class="bg-primary text-white">
      <q-toolbar>
        <q-btn
          flat
          dense
          round
          icon="menu"
          aria-label="Toggle navigation"
          data-test="drawer-toggle"
          @click="drawerOpen = !drawerOpen"
        />
        <q-toolbar-title>Booking Platform</q-toolbar-title>
      </q-toolbar>
    </q-header>

    <q-drawer v-model="drawerOpen" show-if-above bordered :width="240">
      <q-list padding>
        <q-item
          v-for="item in dashboardNavigation"
          :key="item.label"
          v-ripple
          :to="item.to"
          exact
          clickable
        >
          <q-item-section avatar>
            <q-icon :name="item.icon" />
          </q-item-section>
          <q-item-section>{{ item.label }}</q-item-section>
        </q-item>
      </q-list>
    </q-drawer>

    <q-page-container>
      <router-view />
    </q-page-container>
  </q-layout>
</template>

<script setup>
import { ref } from 'vue'
import { dashboardNavigation } from '@/constants/navigation.js'

defineOptions({ name: 'DashboardLayout' })

const drawerOpen = ref(false)
</script>
