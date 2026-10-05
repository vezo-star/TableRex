<template>
  <q-page padding>
    <div class="text-h5 q-mb-xs">Welcome, {{ auth.displayName }}</div>
    <div class="text-grey-7 q-mb-lg">{{ auth.currentTenant?.tenantName }} · {{ roleText }}</div>

    <div class="row q-col-gutter-md">
      <div v-for="card in cards" :key="card.label" class="col-12 col-sm-6 col-md-4">
        <q-card flat bordered>
          <q-item clickable :to="card.to">
            <q-item-section avatar>
              <q-icon :name="card.icon" color="primary" size="md" />
            </q-item-section>
            <q-item-section>
              <q-item-label class="text-subtitle1">{{ card.label }}</q-item-label>
              <q-item-label caption>{{ card.caption }}</q-item-label>
            </q-item-section>
          </q-item>
        </q-card>
      </div>
    </div>
  </q-page>
</template>

<script setup>
import { computed } from 'vue'
import { useAuthStore } from '@/stores/auth.js'
import { RoleLabels, Permissions } from '@/constants/permissions.js'
import { RouteNames } from '@/constants/navigation.js'

defineOptions({ name: 'DashboardPage' })

const auth = useAuthStore()

const roleText = computed(() => auth.roles.map((r) => RoleLabels[r] ?? r).join(', '))

const allCards = [
  {
    label: 'Staff',
    caption: 'People and roles',
    icon: 'badge',
    to: { name: RouteNames.STAFF },
    permission: Permissions.STAFF_VIEW,
  },
  {
    label: 'Settings',
    caption: 'Workspace settings',
    icon: 'settings',
    to: { name: RouteNames.SETTINGS },
    permission: Permissions.SETTINGS_MANAGE,
  },
  {
    label: 'Account',
    caption: 'Change your password',
    icon: 'key',
    to: { name: RouteNames.ACCOUNT },
  },
]

const cards = computed(() =>
  allCards.filter((c) => !c.permission || auth.hasPermission(c.permission)),
)
</script>
