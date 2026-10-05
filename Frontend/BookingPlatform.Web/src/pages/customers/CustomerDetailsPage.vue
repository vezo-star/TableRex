<template>
  <q-page padding>
    <div class="q-mb-md">
      <q-btn
        flat
        dense
        no-caps
        icon="arrow_back"
        label="Customers"
        :to="{ name: RouteNames.CUSTOMERS }"
      />
    </div>

    <ErrorState
      v-if="error"
      :message="error.message"
      :trace-id="error.traceId"
      retryable
      @retry="load"
    />
    <q-inner-loading :showing="loading && !customer" />

    <template v-if="customer">
      <div class="row items-center q-mb-md q-gutter-sm">
        <div class="text-h5 col" data-test="customer-name">
          {{ fullName }}
          <q-badge v-if="customer.isAnonymized" color="grey-6" class="q-ml-sm" label="Anonymised" />
        </div>
        <q-btn
          v-if="canManage && !customer.isAnonymized"
          outline
          no-caps
          icon="edit"
          label="Edit"
          data-test="customer-edit"
          @click="dialogOpen = true"
        />
        <q-btn
          v-if="canManage && !customer.isAnonymized"
          outline
          no-caps
          color="negative"
          icon="person_remove"
          label="Anonymise"
          data-test="customer-anonymize"
          @click="confirmAnonymize"
        />
      </div>

      <div class="row q-col-gutter-md">
        <div class="col-12 col-md-4">
          <q-card flat bordered>
            <q-list dense>
              <q-item>
                <q-item-section avatar><q-icon name="mail" /></q-item-section>
                <q-item-section data-test="customer-email">{{
                  customer.email || '—'
                }}</q-item-section>
              </q-item>
              <q-item>
                <q-item-section avatar><q-icon name="phone" /></q-item-section>
                <q-item-section data-test="customer-phone">{{
                  customer.phone || '—'
                }}</q-item-section>
              </q-item>
              <q-item>
                <q-item-section avatar><q-icon name="campaign" /></q-item-section>
                <q-item-section>
                  {{ customer.marketingConsent ? 'Agreed to marketing' : 'No marketing consent' }}
                </q-item-section>
              </q-item>
              <q-item>
                <q-item-section avatar><q-icon name="event" /></q-item-section>
                <q-item-section>{{ customer.bookingCount }} bookings</q-item-section>
              </q-item>
              <q-item>
                <q-item-section avatar><q-icon name="schedule" /></q-item-section>
                <q-item-section
                  >Customer since {{ formatInstant(customer.createdAt) }}</q-item-section
                >
              </q-item>
              <q-item v-if="customer.anonymizedAt">
                <q-item-section avatar><q-icon name="lock" /></q-item-section>
                <q-item-section
                  >Anonymised {{ formatInstant(customer.anonymizedAt) }}</q-item-section
                >
              </q-item>
            </q-list>
            <template v-if="customer.notes">
              <q-separator />
              <q-card-section>
                <div class="text-caption text-grey-7">Notes</div>
                <div class="pre-wrap">{{ customer.notes }}</div>
              </q-card-section>
            </template>
          </q-card>
        </div>

        <div class="col-12 col-md-8">
          <div class="text-subtitle1 q-mb-sm">Bookings</div>
          <div v-if="!canViewBookings" class="text-grey-7">You do not have access to bookings.</div>
          <q-table
            v-else
            v-model:pagination="pagination"
            :rows="bookings"
            :columns="columns"
            row-key="id"
            flat
            bordered
            dense
            :loading="bookingsLoading"
            :rows-per-page-options="[10, 25, 50]"
            no-data-label="No bookings at your venues"
            data-test="customer-bookings"
            @request="onRequest"
          >
            <template #body-cell-status="scope">
              <q-td :props="scope"><BookingStatusChip :status="scope.row.status" /></q-td>
            </template>
          </q-table>
        </div>
      </div>
    </template>

    <CustomerDialog v-model="dialogOpen" :customer="customer" @saved="onSaved" />
  </q-page>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { useQuasar } from 'quasar'
import BookingStatusChip from '@/components/bookings/BookingStatusChip.vue'
import ErrorState from '@/components/common/ErrorState.vue'
import CustomerDialog from '@/components/customers/CustomerDialog.vue'
import { RouteNames } from '@/constants/navigation.js'
import { Permissions } from '@/constants/permissions.js'
import { customerService } from '@/services/customerService.js'
import { useAuthStore } from '@/stores/auth.js'
import { resourceNames } from '@/utils/bookings.js'
import { formatDate } from '@/utils/dates.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'CustomerDetailsPage' })

const props = defineProps({
  customerId: { type: String, required: true },
})

const $q = useQuasar()
const auth = useAuthStore()
const canManage = computed(() => auth.hasPermission(Permissions.CUSTOMER_MANAGE))
const canViewBookings = computed(() => auth.hasPermission(Permissions.BOOKING_VIEW))

const customer = ref(null)
const loading = ref(false)
const error = ref(null)
const dialogOpen = ref(false)
const bookings = ref([])
const bookingsLoading = ref(false)
const pagination = ref({ page: 1, rowsPerPage: 10, rowsNumber: 0 })

const fullName = computed(() =>
  [customer.value?.firstName, customer.value?.lastName].filter(Boolean).join(' '),
)

const dateFormat = new Intl.DateTimeFormat('en-GB', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
})

function formatInstant(instant) {
  return dateFormat.format(new Date(instant))
}

const columns = [
  {
    name: 'date',
    label: 'Date',
    field: 'businessDate',
    format: (v) => formatDate(v),
    align: 'left',
  },
  { name: 'time', label: 'Time', field: 'time', align: 'left' },
  { name: 'reference', label: 'Ref', field: 'reference', align: 'left' },
  { name: 'partySize', label: 'Guests', field: 'partySize' },
  { name: 'resources', label: 'Tables', field: (row) => resourceNames(row), align: 'left' },
  { name: 'status', label: 'Status', field: 'status', align: 'left' },
]

async function load() {
  loading.value = true
  error.value = null
  try {
    customer.value = await customerService.get(props.customerId)
  } catch (e) {
    error.value = normaliseError(e)
    customer.value = null
  } finally {
    loading.value = false
  }
  if (customer.value && canViewBookings.value) {
    loadBookings()
  }
}

async function loadBookings() {
  bookingsLoading.value = true
  try {
    const page = await customerService.bookings(props.customerId, {
      page: pagination.value.page,
      pageSize: pagination.value.rowsPerPage,
    })
    bookings.value = page.items
    pagination.value = { ...pagination.value, rowsNumber: page.totalCount }
  } catch (e) {
    $q.notify?.({ type: 'negative', message: normaliseError(e).message })
  } finally {
    bookingsLoading.value = false
  }
}

function onRequest({ pagination: next }) {
  pagination.value = { ...pagination.value, page: next.page, rowsPerPage: next.rowsPerPage }
  loadBookings()
}

function onSaved(saved) {
  customer.value = saved
  $q.notify?.({ type: 'positive', message: 'Customer saved' })
}

function confirmAnonymize() {
  $q.dialog({
    title: 'Anonymise customer',
    message:
      'This permanently removes the name, email, phone and notes of this customer and clears notes on their bookings. ' +
      'Booking records are kept for reporting. This cannot be undone.',
    cancel: { label: 'Keep', flat: true, noCaps: true },
    ok: { label: 'Anonymise', color: 'negative', unelevated: true, noCaps: true },
    persistent: true,
  }).onOk(async () => {
    try {
      customer.value = await customerService.anonymize(props.customerId)
      $q.notify?.({ type: 'positive', message: 'Customer anonymised' })
      loadBookings()
    } catch (e) {
      $q.notify?.({ type: 'negative', message: normaliseError(e).message })
    }
  })
}

watch(() => props.customerId, load, { immediate: true })
</script>

<style scoped>
.pre-wrap {
  white-space: pre-wrap;
}
</style>
