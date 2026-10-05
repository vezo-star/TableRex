<template>
  <div class="q-gutter-md">
    <div class="text-subtitle1 text-center text-grey-8">Choose a workspace</div>

    <q-banner v-if="error" dense rounded class="bg-red-1 text-negative" role="alert">
      {{ error.message }}
    </q-banner>

    <q-list bordered separator class="rounded-borders" data-test="tenant-list">
      <q-item
        v-for="membership in auth.memberships"
        :key="membership.tenantId"
        v-ripple
        clickable
        :disable="loading"
        :data-test="`tenant-${membership.tenantSlug}`"
        @click="choose(membership.tenantId)"
      >
        <q-item-section>
          <q-item-label>{{ membership.tenantName }}</q-item-label>
          <q-item-label caption>{{ roleLabels(membership.roles) }}</q-item-label>
        </q-item-section>
        <q-item-section side>
          <q-icon name="chevron_right" />
        </q-item-section>
      </q-item>
    </q-list>

    <q-btn flat class="full-width" label="Sign out" :disable="loading" @click="signOut" />
  </div>
</template>

<script setup>
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth.js'
import { useApiAction } from '@/composables/useApiAction.js'
import { RoleLabels } from '@/constants/permissions.js'
import { RouteNames } from '@/constants/navigation.js'
import { permittedRedirect } from '@/router/guards.js'

defineOptions({ name: 'SelectTenantPage' })

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const { loading, error, run } = useApiAction((tenantId) =>
  auth.switchTenant(tenantId).then(() => true),
)

function roleLabels(roles) {
  return roles.map((r) => RoleLabels[r] ?? r).join(', ')
}

async function choose(tenantId) {
  if (await run(tenantId)) {
    await router.replace(
      permittedRedirect(router, auth, route.query.redirect) ?? { name: RouteNames.DASHBOARD },
    )
  }
}

async function signOut() {
  await auth.logout()
  await router.replace({ name: RouteNames.LOGIN })
}
</script>
