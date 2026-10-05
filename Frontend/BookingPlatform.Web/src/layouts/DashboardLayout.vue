<template>
  <q-layout view="hHh lpR fFf">
    <q-header elevated class="bg-primary text-white">
      <q-toolbar>
        <q-btn
          v-if="auth.isAuthenticated"
          flat
          dense
          round
          icon="menu"
          aria-label="Toggle navigation"
          data-test="drawer-toggle"
          @click="drawerOpen = !drawerOpen"
        />
        <q-toolbar-title>Booking Platform</q-toolbar-title>

        <template v-if="auth.isAuthenticated">
          <q-btn-dropdown
            v-if="auth.memberships.length > 1"
            flat
            no-caps
            :label="auth.currentTenant?.tenantName ?? 'Choose workspace'"
            icon="apartment"
            data-test="tenant-switcher"
          >
            <q-list>
              <q-item
                v-for="membership in auth.memberships"
                :key="membership.tenantId"
                v-close-popup
                clickable
                :active="membership.tenantId === auth.tenantId"
                @click="switchTenant(membership.tenantId)"
              >
                <q-item-section>{{ membership.tenantName }}</q-item-section>
              </q-item>
            </q-list>
          </q-btn-dropdown>
          <div v-else-if="auth.currentTenant" class="q-mr-md text-body2" data-test="tenant-name">
            {{ auth.currentTenant.tenantName }}
          </div>

          <q-btn-dropdown
            flat
            no-caps
            icon="account_circle"
            :label="auth.displayName"
            data-test="user-menu"
          >
            <q-list style="min-width: 200px">
              <q-item v-close-popup clickable :to="{ name: RouteNames.ACCOUNT }">
                <q-item-section avatar><q-icon name="key" /></q-item-section>
                <q-item-section>Account</q-item-section>
              </q-item>
              <q-item
                v-if="auth.isPlatformAdmin"
                v-close-popup
                clickable
                :to="{ name: RouteNames.PLATFORM_TENANTS }"
              >
                <q-item-section avatar><q-icon name="admin_panel_settings" /></q-item-section>
                <q-item-section>Platform</q-item-section>
              </q-item>
              <q-separator />
              <q-item v-close-popup clickable data-test="logout" @click="logout">
                <q-item-section avatar><q-icon name="logout" /></q-item-section>
                <q-item-section>Sign out</q-item-section>
              </q-item>
            </q-list>
          </q-btn-dropdown>
        </template>
        <q-btn v-else flat no-caps label="Sign in" :to="{ name: RouteNames.LOGIN }" />
      </q-toolbar>
    </q-header>

    <q-drawer v-if="auth.isAuthenticated" v-model="drawerOpen" show-if-above bordered :width="240">
      <q-list padding>
        <q-item v-for="item in navigation" :key="item.label" v-ripple :to="item.to" exact clickable>
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
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useQuasar } from 'quasar'
import { dashboardNavigation, RouteNames, visibleNavigation } from '@/constants/navigation.js'
import { useAuthStore } from '@/stores/auth.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'DashboardLayout' })

const auth = useAuthStore()
const router = useRouter()
const $q = useQuasar()
const drawerOpen = ref(false)

const navigation = computed(() => visibleNavigation(dashboardNavigation, auth))

async function switchTenant(tenantId) {
  if (tenantId === auth.tenantId) {
    return
  }
  try {
    await auth.switchTenant(tenantId)
    await router.replace({ name: RouteNames.DASHBOARD })
  } catch (e) {
    $q.notify?.({ type: 'negative', message: normaliseError(e).message })
  }
}

async function logout() {
  await auth.logout()
  await router.replace({ name: RouteNames.LOGIN })
}
</script>
