<template>
  <q-page padding>
    <div class="row items-center q-mb-md q-gutter-sm">
      <div class="text-h5 col">Venues</div>
      <q-btn
        v-if="canManage"
        color="primary"
        unelevated
        icon="add_business"
        label="Add venue"
        data-test="add-venue"
        @click="openCreate"
      />
    </div>

    <ErrorState
      v-if="loadError"
      :message="loadError.message"
      :trace-id="loadError.traceId"
      retryable
      @retry="load"
    />

    <q-table
      v-else
      flat
      bordered
      row-key="id"
      :rows="venues.venues"
      :columns="columns"
      :loading="venues.loading"
      :pagination="{ rowsPerPage: 0 }"
      hide-pagination
      data-test="venues-table"
      @row-click="(_e, row) => open(row)"
    >
      <template #body-cell-status="props">
        <q-td :props="props">
          <q-badge :color="props.row.status === 'ACTIVE' ? 'positive' : 'grey-8'">
            {{ props.row.status }}
          </q-badge>
        </q-td>
      </template>
      <template #body-cell-actions="props">
        <q-td :props="props" class="text-right">
          <q-btn
            flat
            dense
            :icon="canManage ? 'settings' : 'visibility'"
            :aria-label="canManage ? 'Venue settings' : 'View venue'"
            @click.stop="open(props.row)"
          >
            <q-tooltip>{{ canManage ? 'Settings' : 'View' }}</q-tooltip>
          </q-btn>
        </q-td>
      </template>
      <template #no-data>
        <div class="full-width text-center text-grey-7 q-pa-md">
          {{
            canManage ? 'No venues yet. Add your first venue.' : 'No venues are assigned to you.'
          }}
        </div>
      </template>
    </q-table>

    <q-dialog v-model="createOpen" persistent>
      <q-card style="width: 720px; max-width: 95vw">
        <q-card-section class="text-h6">Add venue</q-card-section>
        <q-card-section>
          <VenueForm
            :default-time-zone="defaultTimeZone"
            cancellable
            @cancel="createOpen = false"
            @saved="onCreated"
          />
        </q-card-section>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useQuasar } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import VenueForm from '@/components/venues/VenueForm.vue'
import { useAuthStore } from '@/stores/auth.js'
import { useVenuesStore } from '@/stores/venues.js'
import { tenancyService } from '@/services/tenancyService.js'
import { Permissions } from '@/constants/permissions.js'
import { RouteNames } from '@/constants/navigation.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'VenuesPage' })

const auth = useAuthStore()
const venues = useVenuesStore()
const router = useRouter()
const $q = useQuasar()
const canManage = computed(() => auth.hasPermission(Permissions.VENUE_MANAGE))
const defaultTimeZone = ref('Africa/Johannesburg')

const columns = [
  { name: 'name', label: 'Name', field: 'name', align: 'left', sortable: true },
  { name: 'slug', label: 'Slug', field: 'slug', align: 'left' },
  { name: 'city', label: 'City', field: 'city', align: 'left' },
  { name: 'timeZone', label: 'Time zone', field: 'timeZone', align: 'left' },
  { name: 'status', label: 'Status', field: 'status', align: 'left' },
  { name: 'actions', label: '', field: 'id', align: 'right' },
]

const loadError = ref(null)
const createOpen = ref(false)

async function load() {
  loadError.value = null
  try {
    await venues.ensureLoaded(auth.tenantId, { force: true })
  } catch (e) {
    loadError.value = normaliseError(e)
  }
}

async function openCreate() {
  try {
    const tenant = await tenancyService.getCurrentTenant()
    defaultTimeZone.value = tenant.defaultTimeZone ?? defaultTimeZone.value
  } catch {
    // Keep the fallback zone; the form still validates on submit.
  }
  createOpen.value = true
}

function open(row) {
  router.push({ name: RouteNames.VENUE_SETTINGS, params: { venueId: row.id } })
}

function onCreated(venue) {
  createOpen.value = false
  venues.upsert(venue)
  $q.notify?.({ type: 'positive', message: `${venue.name} created.` })
  open(venue)
}

onMounted(load)
</script>
