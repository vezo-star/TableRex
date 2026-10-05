<template>
  <div class="column items-center q-pa-xl text-grey-7" data-test="no-venue">
    <q-icon name="storefront" size="48px" class="q-mb-sm" />
    <div class="text-subtitle1">No venue selected</div>
    <div class="text-body2 q-mb-md">
      {{
        canManage
          ? 'Create a venue to get started.'
          : 'Ask an administrator to assign you to a venue.'
      }}
    </div>
    <q-btn
      v-if="canManage"
      color="primary"
      unelevated
      label="Go to venues"
      :to="{ name: RouteNames.VENUES }"
    />
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { useAuthStore } from '@/stores/auth.js'
import { Permissions } from '@/constants/permissions.js'
import { RouteNames } from '@/constants/navigation.js'

defineOptions({ name: 'NoVenueState' })

const auth = useAuthStore()
const canManage = computed(() => auth.hasPermission(Permissions.VENUE_MANAGE))
</script>
