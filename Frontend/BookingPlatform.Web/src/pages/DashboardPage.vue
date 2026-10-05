<template>
  <q-page padding>
    <div class="text-h5 q-mb-xs">Welcome, {{ auth.displayName }}</div>
    <div class="text-grey-7 q-mb-lg">{{ auth.currentTenant?.tenantName }} · {{ roleText }}</div>

    <template v-if="canViewBookings && venue">
      <div class="row items-center q-mb-sm">
        <div class="text-subtitle1 col">
          Today at {{ venue.name }}
          <span class="text-grey-7">· {{ formatDate(today) }}</span>
        </div>
        <q-btn
          flat
          dense
          round
          icon="refresh"
          aria-label="Refresh"
          :loading="loading"
          @click="load"
        />
      </div>
      <ErrorState
        v-if="error"
        :message="error.message"
        :trace-id="error.traceId"
        retryable
        @retry="load"
      />
      <div v-if="summary" class="row q-col-gutter-md q-mb-lg" data-test="dashboard-summary">
        <div v-for="stat in stats" :key="stat.label" class="col-6 col-md-3">
          <q-card flat bordered>
            <q-card-section>
              <div class="text-caption text-grey-7">{{ stat.label }}</div>
              <div class="text-h4" :data-test="`stat-${stat.key}`">{{ stat.value }}</div>
            </q-card-section>
          </q-card>
        </div>
      </div>
      <div v-if="summary" class="row q-col-gutter-md q-mb-lg">
        <div class="col-12 col-md-7">
          <q-card flat bordered>
            <q-card-section class="row items-center q-pb-none">
              <div class="text-subtitle1 col">Upcoming arrivals</div>
              <q-btn flat dense no-caps label="Open board" :to="{ name: RouteNames.BOOKINGS }" />
            </q-card-section>
            <q-list separator data-test="dashboard-upcoming">
              <q-item v-if="!summary.upcoming.length">
                <q-item-section class="text-grey-7">No more arrivals today.</q-item-section>
              </q-item>
              <q-item v-for="b in summary.upcoming.slice(0, 8)" :key="b.id">
                <q-item-section avatar class="text-weight-medium">{{ b.time }}</q-item-section>
                <q-item-section>
                  <q-item-label
                    >{{ customerName(b.customer) }} · {{ b.partySize }} guests</q-item-label
                  >
                  <q-item-label caption>{{ resourceNames(b) }}</q-item-label>
                </q-item-section>
                <q-item-section side>
                  <q-badge v-if="isLate(b)" color="negative" label="Late" />
                  <BookingStatusChip v-else :status="b.status" />
                </q-item-section>
              </q-item>
            </q-list>
          </q-card>
        </div>
        <div class="col-12 col-md-5">
          <q-card flat bordered>
            <q-card-section class="text-subtitle1 q-pb-none">By status</q-card-section>
            <q-list dense>
              <q-item v-if="!statusRows.length">
                <q-item-section class="text-grey-7">No bookings today.</q-item-section>
              </q-item>
              <q-item v-for="row in statusRows" :key="row.status">
                <q-item-section><BookingStatusChip :status="row.status" /></q-item-section>
                <q-item-section side class="text-body1">{{ row.count }}</q-item-section>
              </q-item>
            </q-list>
          </q-card>
        </div>
      </div>
    </template>
    <NoVenueState
      v-else-if="canViewBookings && !venues.loading && !venues.currentVenueId"
      class="q-mb-lg"
    />

    <div class="row q-col-gutter-md">
      <div v-for="card in cards" :key="card.label" class="col-12 col-sm-6 col-md-4">
        <q-card flat bordered>
          <q-item clickable :to="card.to">
            <q-item-section avatar>
              <q-icon :name="card.icon" color="primary" size="md" />
            </q-item-section>
            <q-item-section>
              <q-item-label class="text-subtitle1">{{ card.label }}</q-item-label>
              <q-item-label caption>{{ card.caption }}</q-item-label>
            </q-item-section>
          </q-item>
        </q-card>
      </div>
    </div>
  </q-page>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import BookingStatusChip from '@/components/bookings/BookingStatusChip.vue'
import ErrorState from '@/components/common/ErrorState.vue'
import NoVenueState from '@/components/venues/NoVenueState.vue'
import { useRealtimeRefresh } from '@/composables/useRealtimeRefresh.js'
import { RouteNames } from '@/constants/navigation.js'
import { Permissions, RoleLabels } from '@/constants/permissions.js'
import { BOOKING_EVENTS } from '@/constants/realtimeEvents.js'
import { bookingService } from '@/services/bookingService.js'
import { useAuthStore } from '@/stores/auth.js'
import { useVenuesStore } from '@/stores/venues.js'
import { customerName, resourceNames, summariseDay } from '@/utils/bookings.js'
import { formatDate, todayIso } from '@/utils/dates.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'DashboardPage' })

const auth = useAuthStore()
const venues = useVenuesStore()
const venue = computed(() => venues.currentVenue)
const canViewBookings = computed(() => auth.hasPermission(Permissions.BOOKING_VIEW))

const roleText = computed(() => auth.roles.map((r) => RoleLabels[r] ?? r).join(', '))

const board = ref(null)
const loading = ref(false)
const error = ref(null)
const now = ref(Date.now())

const today = computed(() => (venue.value ? todayIso(venue.value.timeZone) : ''))
const summary = computed(() => (board.value ? summariseDay(board.value.bookings, now.value) : null))
const stats = computed(() => [
  { key: 'bookings', label: 'Bookings', value: summary.value.bookings },
  { key: 'covers', label: 'Covers', value: summary.value.covers },
  { key: 'seated', label: 'Seated now', value: summary.value.seatedCovers },
  { key: 'late', label: 'Late arrivals', value: summary.value.late.length },
])
const statusRows = computed(() =>
  Object.entries(summary.value?.byStatus ?? {})
    .filter(([, count]) => count > 0)
    .map(([status, count]) => ({ status, count })),
)

function isLate(booking) {
  return new Date(booking.startsAt).getTime() < now.value
}

async function load({ silent = false } = {}) {
  if (!canViewBookings.value || !venue.value) {
    board.value = null
    return
  }
  loading.value = !silent
  try {
    board.value = await bookingService.board(venue.value.id, today.value)
    now.value = Date.now()
    error.value = null
  } catch (e) {
    error.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

const allCards = [
  {
    label: 'Bookings',
    caption: 'Timeline, floor and list',
    icon: 'event_note',
    to: { name: RouteNames.BOOKINGS },
    permission: Permissions.BOOKING_VIEW,
  },
  {
    label: 'Customers',
    caption: 'Guest records and history',
    icon: 'people',
    to: { name: RouteNames.CUSTOMERS },
    permission: Permissions.CUSTOMER_VIEW,
  },
  {
    label: 'Staff',
    caption: 'People and roles',
    icon: 'badge',
    to: { name: RouteNames.STAFF },
    permission: Permissions.STAFF_VIEW,
  },
  {
    label: 'Settings',
    caption: 'Workspace settings',
    icon: 'settings',
    to: { name: RouteNames.SETTINGS },
    permission: Permissions.SETTINGS_MANAGE,
  },
  {
    label: 'Account',
    caption: 'Change your password',
    icon: 'key',
    to: { name: RouteNames.ACCOUNT },
  },
]

const cards = computed(() =>
  allCards.filter((c) => !c.permission || auth.hasPermission(c.permission)),
)

watch(
  () => venue.value?.id,
  () => load(),
  { immediate: true },
)

const { live } = useRealtimeRefresh(() => load({ silent: true }), {
  filter: (event) =>
    BOOKING_EVENTS.includes(event.type) &&
    (!event.businessDate || event.businessDate === today.value),
})

// Keeps "upcoming" current; refetches only while live updates are unavailable.
let timer = null
onMounted(() => {
  timer = setInterval(() => {
    now.value = Date.now()
    if (!live.value && !document.hidden) {
      load({ silent: true })
    }
  }, 60_000)
})
onBeforeUnmount(() => clearInterval(timer))
</script>
