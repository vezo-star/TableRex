<template>
  <q-page padding>
    <div class="row items-center q-mb-md">
      <div class="text-h5 col">Tenants</div>
      <q-btn
        color="primary"
        unelevated
        icon="add_business"
        label="New tenant"
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
      v-model:pagination="pagination"
      flat
      bordered
      row-key="id"
      :rows="rows"
      :columns="columns"
      :loading="loading"
      @request="onRequest"
    />

    <q-dialog v-model="createOpen" persistent>
      <q-card style="min-width: 360px; max-width: 520px">
        <q-form @submit.prevent="submit">
          <q-card-section class="text-h6">New tenant</q-card-section>
          <q-card-section class="q-gutter-sm">
            <q-banner
              v-if="action.error.value && !Object.keys(action.fieldErrors.value).length"
              dense
              rounded
              class="bg-red-1 text-negative"
            >
              {{ action.error.value.message }}
            </q-banner>
            <q-input
              v-model.trim="form.name"
              label="Business name"
              outlined
              dense
              :error="!!action.fieldError('name')"
              :error-message="action.fieldError('name')"
            />
            <q-input
              v-model.trim="form.slug"
              label="Slug"
              hint="lowercase-with-hyphens"
              outlined
              dense
              :error="!!action.fieldError('slug')"
              :error-message="action.fieldError('slug')"
            />
            <q-select
              v-model="form.defaultTimeZone"
              :options="zones"
              label="Time zone"
              outlined
              dense
            />
            <div class="text-subtitle2 q-mt-md">First owner</div>
            <q-input
              v-model.trim="form.owner.email"
              type="email"
              label="Owner email"
              outlined
              dense
              :error="!!action.fieldError('owner.email')"
              :error-message="action.fieldError('owner.email')"
            />
            <q-input
              v-model.trim="form.owner.displayName"
              label="Owner name"
              outlined
              dense
              :error="!!action.fieldError('owner.displayName')"
              :error-message="action.fieldError('owner.displayName')"
            />
            <q-input
              v-model="form.owner.password"
              type="password"
              label="Initial password (new accounts)"
              autocomplete="new-password"
              outlined
              dense
              :error="!!action.fieldError('owner.password')"
              :error-message="action.fieldError('owner.password')"
            />
          </q-card-section>
          <q-card-actions align="right">
            <q-btn v-close-popup flat label="Cancel" />
            <q-btn
              type="submit"
              color="primary"
              unelevated
              label="Create"
              :loading="action.loading.value"
            />
          </q-card-actions>
        </q-form>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import ErrorState from '@/components/common/ErrorState.vue'
import { tenancyService } from '@/services/tenancyService.js'
import { useApiAction } from '@/composables/useApiAction.js'
import { normaliseError } from '@/utils/problemDetails.js'
import { timeZoneOptions } from '@/utils/timeZones.js'

defineOptions({ name: 'PlatformTenantsPage' })

const columns = [
  { name: 'name', label: 'Name', field: 'name', align: 'left' },
  { name: 'slug', label: 'Slug', field: 'slug', align: 'left' },
  { name: 'status', label: 'Status', field: 'status', align: 'left' },
  { name: 'defaultTimeZone', label: 'Time zone', field: 'defaultTimeZone', align: 'left' },
]
const zones = timeZoneOptions()
const rows = ref([])
const loading = ref(false)
const loadError = ref(null)
const pagination = ref({ page: 1, rowsPerPage: 25, rowsNumber: 0 })

async function load() {
  loading.value = true
  loadError.value = null
  try {
    const result = await tenancyService.listPlatformTenants({
      page: pagination.value.page,
      pageSize: pagination.value.rowsPerPage,
    })
    rows.value = result.items
    pagination.value = { ...pagination.value, rowsNumber: result.totalCount }
  } catch (e) {
    loadError.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

function onRequest({ pagination: next }) {
  pagination.value = { ...pagination.value, page: next.page, rowsPerPage: next.rowsPerPage }
  load()
}

const createOpen = ref(false)
const emptyForm = () => ({
  name: '',
  slug: '',
  defaultTimeZone: 'Africa/Johannesburg',
  owner: { email: '', displayName: '', password: '' },
})
const form = reactive(emptyForm())
const action = useApiAction((payload) => tenancyService.createPlatformTenant(payload))

function openCreate() {
  Object.assign(form, emptyForm())
  action.reset()
  createOpen.value = true
}

async function submit() {
  const created = await action.run({
    ...form,
    owner: { ...form.owner, password: form.owner.password || null },
  })
  if (created) {
    createOpen.value = false
    load()
  }
}

onMounted(load)
</script>
