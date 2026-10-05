<template>
  <q-btn-dropdown
    v-if="venues.venues.length > 1"
    flat
    no-caps
    icon="storefront"
    :label="venues.currentVenue?.name ?? 'Choose venue'"
    data-test="venue-switcher"
  >
    <q-list style="min-width: 200px">
      <q-item
        v-for="venue in venues.venues"
        :key="venue.id"
        v-close-popup
        clickable
        :active="venue.id === venues.currentVenueId"
        @click="venues.select(venue.id)"
      >
        <q-item-section>{{ venue.name }}</q-item-section>
        <q-item-section v-if="venue.status !== 'ACTIVE'" side>
          <q-badge color="grey-8" label="Inactive" />
        </q-item-section>
      </q-item>
    </q-list>
  </q-btn-dropdown>
  <div v-else-if="venues.currentVenue" class="q-mr-md text-body2" data-test="venue-name">
    <q-icon name="storefront" class="q-mr-xs" />{{ venues.currentVenue.name }}
  </div>
</template>

<script setup>
import { watch } from 'vue'
import { useAuthStore } from '@/stores/auth.js'
import { useVenuesStore } from '@/stores/venues.js'
import { Permissions } from '@/constants/permissions.js'

defineOptions({ name: 'VenueSwitcher' })

const auth = useAuthStore()
const venues = useVenuesStore()

watch(
  () => [auth.tenantId, auth.hasPermission(Permissions.VENUE_VIEW)],
  async ([tenantId, canView]) => {
    if (!tenantId || !canView) {
      venues.reset()
      return
    }
    try {
      await venues.ensureLoaded(tenantId)
    } catch {
      // Pages that need venues show their own load errors.
    }
  },
  { immediate: true },
)
</script>
