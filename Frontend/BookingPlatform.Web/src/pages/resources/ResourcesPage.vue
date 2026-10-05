<template>
  <q-page padding>
    <div class="row items-center q-mb-md q-gutter-sm">
      <div class="text-h5 col">
        Resources
        <span v-if="venues.currentVenue" class="text-subtitle1 text-grey-7">
          · {{ venues.currentVenue.name }}
        </span>
      </div>
      <q-select
        v-model="sectionFilter"
        :options="sectionOptions"
        emit-value
        map-options
        clearable
        dense
        outlined
        label="Section"
        style="min-width: 180px"
        data-test="section-filter"
      />
      <q-btn
        v-if="canCreate"
        outline
        color="primary"
        icon="category"
        label="Types"
        data-test="manage-types"
        @click="typesOpen = true"
      />
      <q-btn
        v-if="canCreate"
        color="primary"
        unelevated
        icon="add"
        label="Add resource"
        :disable="!venueId"
        data-test="add-resource"
        @click="openEdit(null)"
      />
    </div>

    <NoVenueState v-if="!venueId && !venues.loading" />
    <ErrorState
      v-else-if="loadError"
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
      :rows="filteredRows"
      :columns="columns"
      :loading="loading"
      :pagination="{ rowsPerPage: 50, sortBy: 'sortOrder' }"
      data-test="resources-table"
    >
      <template #body-cell-capacity="props">
        <q-td :props="props">{{ props.row.minCapacity }}–{{ props.row.capacity }}</q-td>
      </template>
      <template #body-cell-flags="props">
        <q-td :props="props">
          <q-badge v-if="!props.row.isActive" color="grey" label="Inactive" class="q-mr-xs" />
          <q-badge v-if="!props.row.isBookableOnline" color="orange" label="Staff only" />
        </q-td>
      </template>
      <template #body-cell-actions="props">
        <q-td :props="props" class="text-right">
          <q-btn
            v-if="canUpdate"
            flat
            dense
            icon="edit"
            aria-label="Edit resource"
            @click="openEdit(props.row)"
          />
          <q-btn
            v-if="canDelete"
            flat
            dense
            icon="delete"
            color="negative"
            aria-label="Delete resource"
            @click="remove(props.row)"
          />
        </q-td>
      </template>
      <template #no-data>
        <div class="full-width text-center text-grey-7 q-pa-md">
          No resources in this venue yet.
        </div>
      </template>
    </q-table>

    <ResourceDialog
      v-model="editOpen"
      :resource="editing"
      :venue-id="venueId"
      :sections="sections"
      :types="types"
      @saved="onSaved"
    />
    <ResourceTypesDialog v-model="typesOpen" :types="types" @changed="loadTypes" />
  </q-page>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { useQuasar } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import NoVenueState from '@/components/venues/NoVenueState.vue'
import ResourceDialog from '@/components/resources/ResourceDialog.vue'
import ResourceTypesDialog from '@/components/resources/ResourceTypesDialog.vue'
import { venueService } from '@/services/venueService.js'
import { useAuthStore } from '@/stores/auth.js'
import { useVenuesStore } from '@/stores/venues.js'
import { Permissions } from '@/constants/permissions.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'ResourcesPage' })

const auth = useAuthStore()
const venues = useVenuesStore()
const $q = useQuasar()
const canCreate = computed(() => auth.hasPermission(Permissions.RESOURCE_CREATE))
const canUpdate = computed(() => auth.hasPermission(Permissions.RESOURCE_UPDATE))
const canDelete = computed(() => auth.hasPermission(Permissions.RESOURCE_DELETE))
const venueId = computed(() => venues.currentVenueId)

const rows = ref([])
const sections = ref([])
const types = ref([])
const loading = ref(false)
const loadError = ref(null)
const sectionFilter = ref(null)

const sectionName = (id) => sections.value.find((s) => s.id === id)?.name ?? '—'
const typeName = (id) => types.value.find((t) => t.id === id)?.name ?? '—'

const columns = [
  { name: 'code', label: 'Code', field: 'code', align: 'left', sortable: true },
  { name: 'name', label: 'Name', field: 'name', align: 'left', sortable: true },
  {
    name: 'type',
    label: 'Type',
    field: (r) => typeName(r.resourceTypeId),
    align: 'left',
    sortable: true,
  },
  {
    name: 'section',
    label: 'Section',
    field: (r) => sectionName(r.sectionId),
    align: 'left',
    sortable: true,
  },
  { name: 'capacity', label: 'Party size', field: 'capacity', align: 'left', sortable: true },
  { name: 'flags', label: '', field: 'isActive', align: 'left' },
  {
    name: 'sortOrder',
    label: 'Order',
    field: 'sortOrder',
    sortable: true,
    classes: 'hidden',
    headerClasses: 'hidden',
  },
  { name: 'actions', label: '', field: 'id', align: 'right' },
]

const sectionOptions = computed(() => sections.value.map((s) => ({ label: s.name, value: s.id })))
const filteredRows = computed(() =>
  sectionFilter.value ? rows.value.filter((r) => r.sectionId === sectionFilter.value) : rows.value,
)

async function loadTypes() {
  types.value = await venueService.listResourceTypes()
}

async function load() {
  if (!venueId.value) {
    rows.value = []
    return
  }
  loading.value = true
  loadError.value = null
  try {
    const [resources, venueSections] = await Promise.all([
      venueService.listAllResources(venueId.value),
      venueService.listSections(venueId.value),
      loadTypes(),
    ])
    rows.value = resources
    sections.value = venueSections
  } catch (e) {
    loadError.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

const editOpen = ref(false)
const editing = ref(null)
const typesOpen = ref(false)

function openEdit(resource) {
  editing.value = resource
  editOpen.value = true
}

function onSaved() {
  editOpen.value = false
  load()
}

function remove(resource) {
  $q.dialog({
    title: 'Delete resource',
    message: `Delete ${resource.name}? Resources with booking history are deactivated instead.`,
    cancel: true,
    persistent: true,
    ok: { label: 'Delete', color: 'negative', unelevated: true },
  }).onOk(async () => {
    try {
      const result = await venueService.deleteResource(resource.id)
      $q.notify?.({
        type: 'positive',
        message: result.deleted
          ? 'Resource deleted.'
          : 'Resource is in use, so it was deactivated.',
      })
      load()
    } catch (e) {
      $q.notify?.({ type: 'negative', message: normaliseError(e).message })
    }
  })
}

watch(
  venueId,
  () => {
    sectionFilter.value = null
    load()
  },
  { immediate: true },
)
</script>
