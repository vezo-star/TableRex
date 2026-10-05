<template>
  <q-page padding>
    <div class="row items-center q-mb-md q-gutter-sm">
      <div class="text-h5 col">Staff</div>
      <q-input
        v-model="search"
        dense
        outlined
        debounce="300"
        placeholder="Search name or email"
        clearable
        class="col-12 col-sm-auto"
        data-test="staff-search"
      >
        <template #prepend><q-icon name="search" /></template>
      </q-input>
      <q-btn
        v-if="canManage"
        color="primary"
        unelevated
        icon="person_add"
        label="Add staff"
        data-test="add-staff"
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
      row-key="tenantUserId"
      :rows="rows"
      :columns="columns"
      :loading="loading"
      :rows-per-page-options="[10, 25, 50, 100]"
      binary-state-sort
      data-test="staff-table"
      @request="onRequest"
    >
      <template #body-cell-roles="props">
        <q-td :props="props">
          <q-chip
            v-for="role in props.row.roles"
            :key="role"
            dense
            square
            color="blue-1"
            text-color="blue-9"
          >
            {{ RoleLabels[role] ?? role }}
          </q-chip>
        </q-td>
      </template>
      <template #body-cell-venues="props">
        <q-td :props="props">
          <span v-if="hasAllAccess(props.row)" class="text-grey-7">All venues</span>
          <template v-else>
            <q-chip
              v-for="venueId in props.row.venueIds"
              :key="venueId"
              dense
              square
              color="green-1"
              text-color="green-9"
            >
              {{ venueName(venueId) }}
            </q-chip>
            <span v-if="!props.row.venueIds?.length" class="text-negative text-caption">None</span>
          </template>
        </q-td>
      </template>
      <template #body-cell-status="props">
        <q-td :props="props">
          <q-badge :color="props.row.status === 'ACTIVE' ? 'positive' : 'grey-8'">{{
            props.row.status
          }}</q-badge>
        </q-td>
      </template>
      <template #body-cell-actions="props">
        <q-td :props="props" class="text-right">
          <template v-if="canManage">
            <q-btn
              flat
              dense
              icon="manage_accounts"
              aria-label="Edit roles"
              @click="openRoles(props.row)"
            >
              <q-tooltip>Edit roles</q-tooltip>
            </q-btn>
            <q-btn
              v-if="!hasAllAccess(props.row)"
              flat
              dense
              icon="storefront"
              aria-label="Assign venues"
              data-test="assign-venues"
              @click="openVenues(props.row)"
            >
              <q-tooltip>Assign venues</q-tooltip>
            </q-btn>
            <q-btn
              v-if="props.row.status === 'ACTIVE'"
              flat
              dense
              icon="person_off"
              color="negative"
              aria-label="Deactivate"
              :disable="props.row.userId === auth.userId"
              @click="confirmDeactivate(props.row)"
            >
              <q-tooltip>Deactivate</q-tooltip>
            </q-btn>
            <q-btn
              v-else
              flat
              dense
              icon="person"
              color="positive"
              aria-label="Activate"
              @click="activate(props.row)"
            >
              <q-tooltip>Activate</q-tooltip>
            </q-btn>
          </template>
        </q-td>
      </template>
      <template #no-data>
        <div class="full-width text-center text-grey-7 q-pa-md">No staff found.</div>
      </template>
    </q-table>

    <q-dialog v-model="createOpen" persistent>
      <q-card style="min-width: 360px; max-width: 480px">
        <q-form @submit.prevent="submitCreate">
          <q-card-section class="text-h6">Add staff member</q-card-section>
          <q-card-section class="q-gutter-sm">
            <q-banner
              v-if="createAction.error.value && !Object.keys(createAction.fieldErrors.value).length"
              dense
              rounded
              class="bg-red-1 text-negative"
            >
              {{ createAction.error.value.message }}
            </q-banner>
            <q-input
              v-model.trim="form.email"
              type="email"
              label="Email"
              outlined
              dense
              :error="!!createAction.fieldError('email')"
              :error-message="createAction.fieldError('email')"
            />
            <q-input
              v-model.trim="form.displayName"
              label="Name"
              outlined
              dense
              :error="!!createAction.fieldError('displayName')"
              :error-message="createAction.fieldError('displayName')"
            />
            <q-input
              v-model="form.password"
              type="password"
              label="Initial password"
              hint="Required for new accounts (min 12 characters). Existing accounts keep their password."
              autocomplete="new-password"
              outlined
              dense
              :error="!!createAction.fieldError('password')"
              :error-message="createAction.fieldError('password')"
            />
            <q-select
              v-model="form.roles"
              :options="roleOptions"
              label="Roles"
              multiple
              emit-value
              map-options
              use-chips
              outlined
              dense
              :error="!!createAction.fieldError('roles')"
              :error-message="createAction.fieldError('roles')"
            />
          </q-card-section>
          <q-card-actions align="right">
            <q-btn v-close-popup flat label="Cancel" />
            <q-btn
              type="submit"
              color="primary"
              unelevated
              label="Add"
              :loading="createAction.loading.value"
            />
          </q-card-actions>
        </q-form>
      </q-card>
    </q-dialog>

    <q-dialog v-model="rolesOpen" persistent>
      <q-card style="min-width: 340px">
        <q-card-section class="text-h6">Roles for {{ editing?.displayName }}</q-card-section>
        <q-card-section>
          <q-banner
            v-if="rolesAction.error.value"
            dense
            rounded
            class="bg-red-1 text-negative q-mb-sm"
          >
            {{ rolesAction.error.value.message }}
          </q-banner>
          <q-option-group v-model="editRoles" :options="roleOptions" type="checkbox" />
        </q-card-section>
        <q-card-actions align="right">
          <q-btn v-close-popup flat label="Cancel" />
          <q-btn
            color="primary"
            unelevated
            label="Save"
            :loading="rolesAction.loading.value"
            @click="submitRoles"
          />
        </q-card-actions>
      </q-card>
    </q-dialog>

    <q-dialog v-model="venuesOpen" persistent>
      <q-card style="min-width: 340px">
        <q-card-section class="text-h6">Venues for {{ editing?.displayName }}</q-card-section>
        <q-card-section>
          <q-banner
            v-if="venuesAction.error.value"
            dense
            rounded
            class="bg-red-1 text-negative q-mb-sm"
          >
            {{ venuesAction.error.value.message }}
          </q-banner>
          <q-option-group
            v-if="venueOptions.length"
            v-model="editVenues"
            :options="venueOptions"
            type="checkbox"
          />
          <div v-else class="text-grey-7">No venues exist yet.</div>
        </q-card-section>
        <q-card-actions align="right">
          <q-btn v-close-popup flat label="Cancel" />
          <q-btn
            color="primary"
            unelevated
            label="Save"
            :loading="venuesAction.loading.value"
            @click="submitVenues"
          />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useQuasar } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import { tenancyService } from '@/services/tenancyService.js'
import { useAuthStore } from '@/stores/auth.js'
import { useVenuesStore } from '@/stores/venues.js'
import { venueService } from '@/services/venueService.js'
import { useApiAction } from '@/composables/useApiAction.js'
import { Permissions, RoleLabels, Roles } from '@/constants/permissions.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'StaffPage' })

const auth = useAuthStore()
const venues = useVenuesStore()
const $q = useQuasar()
const canManage = computed(() => auth.hasPermission(Permissions.STAFF_MANAGE))
const isOwner = computed(() => auth.roles.includes(Roles.TENANT_OWNER))

const columns = [
  { name: 'displayName', label: 'Name', field: 'displayName', align: 'left' },
  { name: 'email', label: 'Email', field: 'email', align: 'left' },
  { name: 'roles', label: 'Roles', field: 'roles', align: 'left' },
  { name: 'venues', label: 'Venues', field: 'venueIds', align: 'left' },
  { name: 'status', label: 'Status', field: 'status', align: 'left' },
  { name: 'actions', label: '', field: 'tenantUserId', align: 'right' },
]

const rows = ref([])
const loading = ref(false)
const loadError = ref(null)
const search = ref('')
const pagination = ref({ page: 1, rowsPerPage: 25, rowsNumber: 0 })

// Only owners may grant or remove the owner role (enforced by the API too).
const roleOptions = computed(() =>
  Object.values(Roles)
    .filter((r) => r !== Roles.TENANT_OWNER || isOwner.value)
    .map((r) => ({ label: RoleLabels[r], value: r })),
)

async function load() {
  loading.value = true
  loadError.value = null
  try {
    const result = await tenancyService.listStaff({
      q: search.value || undefined,
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

watch(search, () => {
  pagination.value = { ...pagination.value, page: 1 }
  load()
})

onMounted(load)

const createOpen = ref(false)
const form = reactive({ email: '', displayName: '', password: '', roles: [Roles.STAFF] })
const createAction = useApiAction((payload) => tenancyService.createStaff(payload))

function openCreate() {
  Object.assign(form, { email: '', displayName: '', password: '', roles: [Roles.STAFF] })
  createAction.reset()
  createOpen.value = true
}

async function submitCreate() {
  const result = await createAction.run({ ...form, password: form.password || null })
  if (result) {
    createOpen.value = false
    $q.notify?.({
      type: 'positive',
      message: result.accountCreated
        ? 'Staff member added.'
        : 'Existing account added to this workspace.',
    })
    load()
  }
}

const rolesOpen = ref(false)
const editing = ref(null)
const editRoles = ref([])
const rolesAction = useApiAction((id, roles) => tenancyService.updateStaffRoles(id, roles))

function openRoles(row) {
  editing.value = row
  editRoles.value = [...row.roles]
  rolesAction.reset()
  rolesOpen.value = true
}

async function submitRoles() {
  if (await rolesAction.run(editing.value.tenantUserId, editRoles.value)) {
    rolesOpen.value = false
    if (editing.value.userId === auth.userId) {
      await auth.loadMe()
    }
    load()
  }
}

// Owners and admins see every venue (D-018); others need explicit assignments.
const hasAllAccess = (row) =>
  row.roles.includes(Roles.TENANT_OWNER) || row.roles.includes(Roles.TENANT_ADMIN)
const venueName = (id) => venues.venues.find((v) => v.id === id)?.name ?? 'Unknown venue'
const venueOptions = computed(() => venues.venues.map((v) => ({ label: v.name, value: v.id })))

const venuesOpen = ref(false)
const editVenues = ref([])
const venuesAction = useApiAction((id, venueIds) => venueService.updateStaffVenues(id, venueIds))

function openVenues(row) {
  editing.value = row
  editVenues.value = [...(row.venueIds ?? [])]
  venuesAction.reset()
  venuesOpen.value = true
}

async function submitVenues() {
  if (await venuesAction.run(editing.value.tenantUserId, editVenues.value)) {
    venuesOpen.value = false
    if (editing.value.userId === auth.userId) {
      await auth.loadMe()
    }
    load()
  }
}

function confirmDeactivate(row) {
  $q.dialog({
    title: 'Deactivate staff member',
    message: `${row.displayName} will lose access to this workspace immediately.`,
    cancel: true,
    persistent: true,
    ok: { label: 'Deactivate', color: 'negative', unelevated: true },
  }).onOk(async () => {
    try {
      await tenancyService.deactivateStaff(row.tenantUserId)
      load()
    } catch (e) {
      $q.notify?.({ type: 'negative', message: normaliseError(e).message })
    }
  })
}

async function activate(row) {
  try {
    await tenancyService.activateStaff(row.tenantUserId)
    load()
  } catch (e) {
    $q.notify?.({ type: 'negative', message: normaliseError(e).message })
  }
}
</script>
