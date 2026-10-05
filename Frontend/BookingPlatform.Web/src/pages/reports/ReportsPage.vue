<template>
  <q-page padding>
    <div class="row items-center q-mb-md">
      <div class="text-h5 col">Reports</div>
      <div v-if="venue" class="text-subtitle2 text-grey-7">{{ venue.name }}</div>
    </div>

    <NoVenueState v-if="!venueId && !venues.loading" />

    <template v-else-if="venueId">
      <q-card flat bordered class="q-mb-md">
        <q-card-section class="row q-col-gutter-sm items-start">
          <q-input
            v-model="range.from"
            class="col-6 col-sm-3"
            type="date"
            label="From"
            outlined
            dense
            stack-label
            data-test="report-from"
          />
          <q-input
            v-model="range.to"
            class="col-6 col-sm-3"
            type="date"
            label="To"
            outlined
            dense
            stack-label
            data-test="report-to"
          />
          <div class="col-12 col-sm-6 row q-gutter-sm items-center">
            <q-btn-group outline>
              <q-btn
                v-for="preset in presets"
                :key="preset.days"
                outline
                no-caps
                color="primary"
                :label="preset.label"
                :data-test="`report-preset-${preset.days}`"
                @click="applyPreset(preset.days)"
              />
            </q-btn-group>
            <q-btn
              unelevated
              no-caps
              color="primary"
              label="Apply"
              data-test="report-apply"
              :disable="!!rangeMessage"
              @click="load"
            />
            <q-btn
              outline
              no-caps
              color="primary"
              icon="download"
              label="CSV"
              data-test="report-csv"
              :loading="downloading"
              :disable="!!rangeMessage"
              @click="downloadCsv"
            />
          </div>
          <div
            v-if="rangeMessage"
            class="col-12 text-negative text-caption"
            data-test="range-error"
          >
            {{ rangeMessage }}
          </div>
        </q-card-section>
      </q-card>

      <ErrorState
        v-if="loadError"
        :message="loadError.message"
        :trace-id="loadError.traceId"
        retryable
        @retry="load"
      />

      <div v-else class="relative-position" style="min-height: 120px">
        <q-inner-loading :showing="loading" />

        <template v-if="report">
          <div class="kpi-grid q-mb-md">
            <q-card v-for="kpi in kpis" :key="kpi.key" flat bordered :data-test="`kpi-${kpi.key}`">
              <q-card-section>
                <div class="text-caption text-grey-7">{{ kpi.label }}</div>
                <div class="text-h5 text-weight-medium">{{ kpi.value }}</div>
                <div v-if="kpi.hint" class="text-caption text-grey-6">{{ kpi.hint }}</div>
              </q-card-section>
            </q-card>
          </div>

          <q-card flat bordered class="q-mb-md">
            <q-card-section>
              <div class="text-subtitle1">Bookings per day</div>
              <div class="text-caption text-grey-7">
                {{ formatDate(report.from) }} – {{ formatDate(report.to) }} · cancelled bookings
                excluded
              </div>
            </q-card-section>
            <q-card-section class="q-pt-none">
              <div
                v-if="report.bookings === 0"
                class="text-body2 text-grey-7"
                data-test="daily-empty"
              >
                No bookings in this period.
              </div>
              <div
                v-else
                class="daily-chart"
                role="img"
                :aria-label="`Bookings per day, peak ${dailyMax} on one day`"
                data-test="daily-chart"
              >
                <div
                  v-for="point in report.daily"
                  :key="point.date"
                  class="daily-bar-wrap"
                  :title="`${formatDate(point.date)}: ${point.bookings} bookings, ${point.covers} guests`"
                >
                  <div
                    class="daily-bar bg-primary"
                    :style="{ height: `${barPercent(point.bookings, dailyMax)}%` }"
                  />
                </div>
              </div>
            </q-card-section>
          </q-card>

          <div class="row q-col-gutter-md q-mb-md">
            <div class="col-12 col-md-6">
              <q-card flat bordered class="full-height">
                <q-card-section class="text-subtitle1">Busiest hours</q-card-section>
                <q-card-section class="q-pt-none">
                  <div v-if="!hours.length" class="text-body2 text-grey-7">No bookings yet.</div>
                  <div v-for="h in hours" :key="h.hour" class="hbar-row" data-test="hour-row">
                    <div class="hbar-label">{{ String(h.hour).padStart(2, '0') }}:00</div>
                    <div class="hbar-track">
                      <div
                        class="hbar-fill bg-primary"
                        :style="{ width: `${barPercent(h.covers, hourMax)}%` }"
                      />
                    </div>
                    <div class="hbar-value">{{ h.covers }}</div>
                  </div>
                  <div v-if="hours.length" class="text-caption text-grey-6 q-mt-xs">
                    Guests by booking start time ({{ report.timeZone }})
                  </div>
                </q-card-section>
              </q-card>
            </div>
            <div class="col-12 col-md-6">
              <q-card flat bordered class="full-height">
                <q-card-section class="text-subtitle1">By status</q-card-section>
                <q-markup-table flat dense separator="horizontal" data-test="status-table">
                  <thead>
                    <tr>
                      <th class="text-left">Status</th>
                      <th class="text-right">Bookings</th>
                      <th class="text-right">Guests</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="row in report.byStatus" :key="row.key">
                      <td>{{ StatusLabels[row.key] ?? row.key }}</td>
                      <td class="text-right">{{ row.bookings }}</td>
                      <td class="text-right">{{ row.covers }}</td>
                    </tr>
                    <tr v-if="!report.byStatus.length">
                      <td colspan="3" class="text-grey-7">No bookings.</td>
                    </tr>
                  </tbody>
                </q-markup-table>
              </q-card>
            </div>
          </div>

          <div class="row q-col-gutter-md">
            <div class="col-12 col-md-6">
              <q-card flat bordered class="full-height">
                <q-card-section class="text-subtitle1">By channel</q-card-section>
                <q-card-section class="q-pt-none">
                  <div v-if="!report.bySource.length" class="text-body2 text-grey-7">
                    No bookings.
                  </div>
                  <div
                    v-for="row in report.bySource"
                    :key="row.key"
                    class="hbar-row"
                    data-test="source-row"
                  >
                    <div class="hbar-label">{{ SourceLabels[row.key] ?? row.key }}</div>
                    <div class="hbar-track">
                      <div
                        class="hbar-fill bg-secondary"
                        :style="{ width: `${barPercent(row.bookings, sourceMax)}%` }"
                      />
                    </div>
                    <div class="hbar-value">{{ row.bookings }}</div>
                  </div>
                </q-card-section>
              </q-card>
            </div>
            <div class="col-12 col-md-6">
              <q-card flat bordered class="full-height" data-test="deposits-card">
                <q-card-section class="text-subtitle1">Deposits</q-card-section>
                <q-card-section class="q-pt-none">
                  <div v-if="!report.deposits.length" class="text-body2 text-grey-7">
                    No deposits for bookings in this period.
                  </div>
                  <q-list v-else dense>
                    <q-item v-for="d in report.deposits" :key="d.currency">
                      <q-item-section>
                        <q-item-label
                          >{{ formatMoney(d.collectedMinor, d.currency) }} collected</q-item-label
                        >
                        <q-item-label caption>
                          {{ d.paidCount }} paid ·
                          {{ formatMoney(d.refundedMinor, d.currency) }} refunded
                        </q-item-label>
                      </q-item-section>
                    </q-item>
                  </q-list>
                </q-card-section>
              </q-card>
            </div>
          </div>
        </template>
      </div>
    </template>
  </q-page>
</template>

<script setup>
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useQuasar } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import NoVenueState from '@/components/venues/NoVenueState.vue'
import { saasService } from '@/services/saasService.js'
import { useVenuesStore } from '@/stores/venues.js'
import { formatDate, todayIso } from '@/utils/dates.js'
import { formatMoney } from '@/utils/payments.js'
import { normaliseError } from '@/utils/problemDetails.js'
import {
  activeHours,
  barPercent,
  defaultRange,
  downloadBlob,
  formatPercent,
  rangeError,
  SourceLabels,
  StatusLabels,
} from '@/utils/reports.js'

defineOptions({ name: 'ReportsPage' })

const $q = useQuasar()
const venues = useVenuesStore()
const venueId = computed(() => venues.currentVenueId)
const venue = computed(() => venues.currentVenue)

const presets = [
  { days: 7, label: '7 days' },
  { days: 30, label: '30 days' },
  { days: 90, label: '90 days' },
]
const range = reactive({ from: '', to: '' })
const report = ref(null)
const loading = ref(false)
const loadError = ref(null)
const downloading = ref(false)
let controller = null

const rangeMessage = computed(() => rangeError(range.from, range.to))
const hours = computed(() => activeHours(report.value?.byHour))
const dailyMax = computed(() => Math.max(0, ...(report.value?.daily ?? []).map((d) => d.bookings)))
const hourMax = computed(() => Math.max(0, ...hours.value.map((h) => h.covers)))
const sourceMax = computed(() =>
  Math.max(0, ...(report.value?.bySource ?? []).map((s) => s.bookings)),
)

const kpis = computed(() => {
  const r = report.value
  if (!r) {
    return []
  }
  return [
    {
      key: 'bookings',
      label: 'Bookings',
      value: r.bookings,
      hint: `${r.totalBookings} incl. cancelled`,
    },
    { key: 'covers', label: 'Guests', value: r.covers, hint: `${r.averagePartySize} per booking` },
    {
      key: 'cancellation',
      label: 'Cancellation rate',
      value: formatPercent(r.cancellationRate),
      hint: `${r.cancelled} cancelled`,
    },
    {
      key: 'noshow',
      label: 'No-show rate',
      value: formatPercent(r.noShowRate),
      hint: `${r.noShows} no-shows`,
    },
  ]
})

function today() {
  return todayIso(venue.value?.timeZone)
}

function applyPreset(days) {
  Object.assign(range, defaultRange(today(), days))
  load()
}

async function load() {
  if (!venueId.value || rangeMessage.value) {
    return
  }
  controller?.abort()
  const current = new AbortController()
  controller = current
  loading.value = true
  loadError.value = null
  try {
    const data = await saasService.bookingsSummary(
      { venueId: venueId.value, from: range.from, to: range.to },
      { signal: current.signal },
    )
    if (controller === current) {
      report.value = data
    }
  } catch (e) {
    if (!current.signal.aborted) {
      loadError.value = normaliseError(e)
    }
  } finally {
    if (controller === current) {
      loading.value = false
    }
  }
}

async function downloadCsv() {
  if (!venueId.value || rangeMessage.value) {
    return
  }
  downloading.value = true
  try {
    const { blob, fileName } = await saasService.bookingsCsv({
      venueId: venueId.value,
      from: range.from,
      to: range.to,
    })
    downloadBlob(blob, fileName)
  } catch (e) {
    $q.notify?.({ type: 'negative', message: normaliseError(e).message })
  } finally {
    downloading.value = false
  }
}

watch(
  venueId,
  (id) => {
    report.value = null
    if (id) {
      if (!range.from) {
        Object.assign(range, defaultRange(today()))
      }
      load()
    }
  },
  { immediate: true },
)

onBeforeUnmount(() => controller?.abort())
</script>

<style scoped>
.kpi-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: 12px;
}
.daily-chart {
  display: flex;
  align-items: flex-end;
  gap: 2px;
  height: 160px;
  overflow-x: auto;
  border-bottom: 1px solid rgba(0, 0, 0, 0.12);
}
.daily-bar-wrap {
  flex: 1 0 6px;
  height: 100%;
  display: flex;
  align-items: flex-end;
}
.daily-bar {
  width: 100%;
  min-height: 1px;
  border-radius: 2px 2px 0 0;
}
.hbar-row {
  display: grid;
  grid-template-columns: 64px 1fr 40px;
  align-items: center;
  gap: 8px;
  margin-bottom: 4px;
}
.hbar-label {
  font-size: 0.8rem;
  color: rgba(0, 0, 0, 0.7);
}
.hbar-track {
  background: rgba(0, 0, 0, 0.06);
  border-radius: 3px;
  height: 14px;
}
.hbar-fill {
  height: 100%;
  border-radius: 3px;
}
.hbar-value {
  text-align: right;
  font-size: 0.8rem;
}
</style>
