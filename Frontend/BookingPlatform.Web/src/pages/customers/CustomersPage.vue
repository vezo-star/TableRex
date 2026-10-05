<template>
  <q-page padding>
    <div class="row items-center q-mb-md q-gutter-sm">
      <div class="text-h5 col">Customers</div>
      <q-btn
        v-if="canManage"
        color="primary"
        unelevated
        no-caps
        icon="person_add"
        label="New customer"
        data-test="customers-new"
        @click="dialogOpen = true"
      />
    </div>

    <q-input
      v-model="search"
      dense
      outlined
      clearable
      debounce="300"
      placeholder="Search by name, email or phone"
      class="q-mb-md"
      style="max-width: 420px"
      data-test="customers-search"
    >
      <template #prepend><q-icon name="search" /></template>
    </q-input>

    <ErrorState
      v-if="error"
      :message="error.message"
      :trace-id="error.traceId"
      retryable
      @retry="load"
    />

    <q-table
      v-model:pagination="pagination"
      :rows="rows"
      :columns="columns"
      row-key="id"
      flat
      bordered
      :loading="loading"
      :rows-per-page-options="[25, 50, 100]"
      binary-state-sort
      no-data-label="No customers found"
      data-test="customers-table"
      @request="onRequest"
      @row-click="(_, row) => openCustomer(row)"
    >
      <template #body-cell-name="scope">
        <q-td :props="scope">
          <span :class="{ 'text-grey-8': scope.row.isAnonymized }">{{ scope.value }}</span>
          <q-badge
            v-if="scope.row.isAnonymized"
            color="grey-8"
            class="q-ml-sm"
            label="Anonymised"
          />
        </q-td>
      </template>
    </q-table>

    <CustomerDialog v-model="dialogOpen" @saved="onSaved" />
  </q-page>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import ErrorState from '@/components/common/ErrorState.vue'
import CustomerDialog from '@/components/customers/CustomerDialog.vue'
import { RouteNames } from '@/constants/navigation.js'
import { Permissions } from '@/constants/permissions.js'
import { customerService } from '@/services/customerService.js'
import { useAuthStore } from '@/stores/auth.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'CustomersPage' })

const router = useRouter()
const auth = useAuthStore()
const canManage = computed(() => auth.hasPermission(Permissions.CUSTOMER_MANAGE))

const search = ref('')
const rows = ref([])
const loading = ref(false)
const error = ref(null)
const dialogOpen = ref(false)
const pagination = ref({ page: 1, rowsPerPage: 25, rowsNumber: 0 })

const dateFormat = new Intl.DateTimeFormat('en-GB', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
})

const columns = [
  {
    name: 'name',
    label: 'Name',
    field: (row) => [row.firstName, row.lastName].filter(Boolean).join(' '),
    align: 'left',
  },
  { name: 'email', label: 'Email', field: 'email', align: 'left' },
  { name: 'phone', label: 'Phone', field: 'phone', align: 'left' },
  { name: 'bookingCount', label: 'Bookings', field: 'bookingCount' },
  {
    name: 'lastBookingAt',
    label: 'Last booking',
    field: 'lastBookingAt',
    format: (v) => (v ? dateFormat.format(new Date(v)) : '—'),
    align: 'left',
  },
]

let controller = null

async function load() {
  controller?.abort()
  controller = new AbortController()
  const { signal } = controller
  loading.value = true
  try {
    const page = await customerService.list(
      {
        q: search.value?.trim(),
        page: pagination.value.page,
        pageSize: pagination.value.rowsPerPage,
      },
      { signal },
    )
    rows.value = page.items
    pagination.value = { ...pagination.value, rowsNumber: page.totalCount }
    error.value = null
  } catch (e) {
    if (!signal.aborted) {
      error.value = normaliseError(e)
    }
  } finally {
    if (!signal.aborted) {
      loading.value = false
    }
  }
}

function onRequest({ pagination: next }) {
  pagination.value = { ...pagination.value, page: next.page, rowsPerPage: next.rowsPerPage }
  load()
}

function openCustomer(row) {
  router.push({ name: RouteNames.CUSTOMER_DETAILS, params: { customerId: row.id } })
}

function onSaved(customer) {
  openCustomer(customer)
}

watch(search, () => {
  pagination.value = { ...pagination.value, page: 1 }
  load()
})

load()
</script>
