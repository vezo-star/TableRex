<template>
  <q-page padding>
    <div class="row items-center q-mb-md q-gutter-sm">
      <q-btn
        flat
        round
        dense
        icon="arrow_back"
        aria-label="Back to venues"
        :to="{ name: RouteNames.VENUES }"
      />
      <div class="text-h5 col">{{ venue?.name ?? 'Venue' }}</div>
      <q-badge v-if="venue" :color="venue.status === 'ACTIVE' ? 'positive' : 'grey-8'">
        {{ venue.status }}
      </q-badge>
    </div>

    <ErrorState
      v-if="loadError"
      :message="loadError.message"
      :trace-id="loadError.traceId"
      retryable
      @retry="load"
    />
    <div v-else-if="!venue" class="flex flex-center q-pa-xl"><q-spinner size="lg" /></div>

    <q-banner
      v-if="venue && publicUrl"
      dense
      rounded
      class="bg-blue-1 q-mb-md"
      data-test="public-booking-link"
    >
      <template #avatar><q-icon name="public" color="primary" /></template>
      <template v-if="publicBookingOpen">
        Guests book online at
        <a :href="publicUrl" target="_blank" rel="noopener" class="text-weight-medium">{{
          publicUrl
        }}</a>
      </template>
      <template v-else>
        Online booking is off for this venue. Turn on "Accept online bookings" and set the venue to
        ACTIVE to share {{ publicUrl }}.
      </template>
      <template v-if="publicBookingOpen" #action>
        <q-btn flat dense no-caps color="blue-9" icon="content_copy" label="Copy" @click="copy" />
      </template>
    </q-banner>

    <q-card v-if="venue" flat bordered>
      <q-tabs v-model="tab" align="left" dense active-color="primary" indicator-color="primary">
        <q-tab name="details" label="Details" />
        <q-tab name="sections" label="Sections" />
        <q-tab name="hours" label="Opening hours" />
        <q-tab name="special" label="Special days" />
        <q-tab name="deposits" label="Deposits" data-test="tab-deposits" />
      </q-tabs>
      <q-separator />
      <q-tab-panels v-model="tab" animated>
        <q-tab-panel name="details">
          <VenueForm
            :key="venue.updatedAt"
            :venue="venue"
            :readonly="!canManage"
            @saved="onSaved"
          />
        </q-tab-panel>
        <q-tab-panel name="sections">
          <SectionsPanel :venue-id="venue.id" :can-manage="canManage" />
        </q-tab-panel>
        <q-tab-panel name="hours">
          <OperatingHoursEditor :venue="venue" :can-manage="canManage" />
        </q-tab-panel>
        <q-tab-panel name="special">
          <SpecialHoursPanel :venue="venue" :can-manage="canManage" />
        </q-tab-panel>
        <q-tab-panel name="deposits">
          <DepositSettingsPanel :venue-id="venue.id" :can-manage="canManage" />
        </q-tab-panel>
      </q-tab-panels>
    </q-card>
  </q-page>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { copyToClipboard, useQuasar } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import VenueForm from '@/components/venues/VenueForm.vue'
import SectionsPanel from '@/components/venues/SectionsPanel.vue'
import OperatingHoursEditor from '@/components/venues/OperatingHoursEditor.vue'
import SpecialHoursPanel from '@/components/venues/SpecialHoursPanel.vue'
import DepositSettingsPanel from '@/components/payments/DepositSettingsPanel.vue'
import { venueService } from '@/services/venueService.js'
import { useAuthStore } from '@/stores/auth.js'
import { useVenuesStore } from '@/stores/venues.js'
import { Permissions } from '@/constants/permissions.js'
import { RouteNames } from '@/constants/navigation.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'VenueSettingsPage' })

const props = defineProps({ venueId: { type: String, required: true } })

const auth = useAuthStore()
const venues = useVenuesStore()
const $q = useQuasar()
const canManage = computed(() => auth.hasPermission(Permissions.VENUE_MANAGE))

const venue = ref(null)
const loadError = ref(null)
const tab = ref('details')

const publicUrl = computed(() => {
  const tenantSlug = auth.currentTenant?.tenantSlug
  return tenantSlug && venue.value
    ? `${window.location.origin}/book/${tenantSlug}/${venue.value.slug}`
    : null
})
const publicBookingOpen = computed(
  () => venue.value?.status === 'ACTIVE' && venue.value?.rules?.publicBookingEnabled !== false,
)

async function copy() {
  try {
    await copyToClipboard(publicUrl.value)
    $q.notify?.({ type: 'positive', message: 'Link copied.' })
  } catch {
    $q.notify?.({ type: 'warning', message: 'Copy failed.' })
  }
}

async function load() {
  loadError.value = null
  venue.value = null
  try {
    venue.value = await venueService.getVenue(props.venueId)
  } catch (e) {
    loadError.value = normaliseError(e)
  }
}

function onSaved(saved) {
  venue.value = saved
  venues.upsert(saved)
  $q.notify?.({ type: 'positive', message: 'Venue saved.' })
}

watch(() => props.venueId, load, { immediate: true })
</script>
