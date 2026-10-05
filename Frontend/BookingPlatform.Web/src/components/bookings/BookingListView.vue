<template>
  <q-table
    :rows="rows"
    :columns="columns"
    row-key="id"
    flat
    bordered
    dense
    :pagination="{ rowsPerPage: 50, sortBy: 'startsAt' }"
    :rows-per-page-options="[25, 50, 100, 0]"
    no-data-label="No bookings for this day"
    data-test="booking-list"
    @row-click="(_, row) => $emit('select', row)"
  >
    <template #body-cell-status="scope">
      <q-td :props="scope"><BookingStatusChip :status="scope.row.status" /></q-td>
    </template>
  </q-table>
</template>

<script setup>
import { computed } from 'vue'
import BookingStatusChip from '@/components/bookings/BookingStatusChip.vue'
import { formatDuration } from '@/utils/availability.js'
import { customerName, resourceNames, SourceLabels } from '@/utils/bookings.js'

defineOptions({ name: 'BookingListView' })

const props = defineProps({
  bookings: { type: Array, default: () => [] },
})
defineEmits(['select'])

const rows = computed(() => props.bookings)

const columns = [
  {
    name: 'startsAt',
    label: 'Time',
    field: 'startsAt',
    format: (_, row) => row.time,
    sortable: true,
    sort: (a, b) => new Date(a) - new Date(b),
    align: 'left',
  },
  { name: 'reference', label: 'Ref', field: 'reference', align: 'left' },
  {
    name: 'customer',
    label: 'Customer',
    field: (row) => customerName(row.customer),
    sortable: true,
    align: 'left',
  },
  { name: 'phone', label: 'Phone', field: (row) => row.customer?.phone ?? '', align: 'left' },
  { name: 'partySize', label: 'Guests', field: 'partySize', sortable: true },
  {
    name: 'duration',
    label: 'Duration',
    field: 'durationMinutes',
    format: (v) => formatDuration(v),
  },
  { name: 'resources', label: 'Tables', field: (row) => resourceNames(row), align: 'left' },
  { name: 'status', label: 'Status', field: 'status', sortable: true, align: 'left' },
  {
    name: 'source',
    label: 'Source',
    field: (row) => SourceLabels[row.source] ?? row.source,
    align: 'left',
  },
]
</script>
