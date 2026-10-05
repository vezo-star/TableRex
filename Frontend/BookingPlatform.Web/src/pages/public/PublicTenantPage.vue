<template>
  <q-page class="public-page q-pa-md" :style="themeStyle">
    <div class="public-column">
      <div v-if="loading" class="flex flex-center q-pa-xl" data-test="tenant-loading">
        <q-spinner size="lg" color="primary" />
      </div>

      <ErrorState
        v-else-if="loadError"
        :message="guestErrorMessage(loadError)"
        :retryable="loadError.status !== 404"
        @retry="load"
      />

      <template v-else-if="tenant">
        <BrandHeader :branding="tenant.branding" :business-name="tenant.name">
          <h1 class="text-h5 q-my-xs" data-test="tenant-name">{{ tenant.name }}</h1>
          <div class="text-body2 text-grey-8">Choose a location to book a table.</div>
        </BrandHeader>

        <div v-if="!tenant.venues.length" class="text-body2 text-grey-8" data-test="no-locations">
          Online booking is not available right now. Please contact the business directly.
        </div>

        <q-list v-else bordered separator class="rounded-borders bg-white" data-test="locations">
          <q-item
            v-for="venue in tenant.venues"
            :key="venue.slug"
            clickable
            :to="{ name: RouteNames.PUBLIC_BOOKING, params: { tenantSlug, venueSlug: venue.slug } }"
            data-test="location"
          >
            <q-item-section avatar>
              <q-icon name="storefront" color="primary" />
            </q-item-section>
            <q-item-section>
              <q-item-label class="text-weight-medium">{{ venue.name }}</q-item-label>
              <q-item-label v-if="venue.city || venue.description" caption lines="2">
                {{ [venue.city, venue.description].filter(Boolean).join(' · ') }}
              </q-item-label>
            </q-item-section>
            <q-item-section side>
              <q-icon name="chevron_right" />
            </q-item-section>
          </q-item>
        </q-list>
      </template>
    </div>
  </q-page>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import ErrorState from '@/components/common/ErrorState.vue'
import BrandHeader from '@/components/public/BrandHeader.vue'
import { RouteNames } from '@/constants/navigation.js'
import { publicBookingService } from '@/services/publicBookingService.js'
import { brandStyle } from '@/utils/branding.js'
import { normaliseError } from '@/utils/problemDetails.js'
import { guestErrorMessage } from '@/utils/publicBooking.js'

defineOptions({ name: 'PublicTenantPage' })

const props = defineProps({
  tenantSlug: { type: String, required: true },
})

const tenant = ref(null)
const loading = ref(false)
const loadError = ref(null)
const themeStyle = computed(() => brandStyle(tenant.value?.branding))

async function load() {
  loading.value = true
  loadError.value = null
  try {
    tenant.value = await publicBookingService.tenant(props.tenantSlug)
    if (typeof document !== 'undefined') {
      document.title = `Book at ${tenant.value.name}`
    }
  } catch (e) {
    tenant.value = null
    loadError.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

watch(() => props.tenantSlug, load, { immediate: true })
</script>

<style scoped>
.public-column {
  max-width: 560px;
  margin: 0 auto;
}
</style>
