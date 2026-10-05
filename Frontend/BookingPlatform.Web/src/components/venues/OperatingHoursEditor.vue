<template>
  <div>
    <div class="text-body2 text-grey-8 q-mb-md">
      Times are in the venue's time zone ({{ venue.timeZone }}). A closing time at or before the
      opening time runs past midnight and belongs to the day it opens.
    </div>

    <ErrorState v-if="loadError" :message="loadError.message" retryable @retry="load" />
    <template v-else>
      <q-banner
        v-if="error"
        dense
        rounded
        class="bg-red-1 text-negative q-mb-sm"
        data-test="hours-error"
      >
        {{ error }}
      </q-banner>

      <q-list bordered separator data-test="hours-editor">
        <q-item v-for="day in days" :key="day.value">
          <q-item-section style="max-width: 120px">
            <q-item-label class="text-weight-medium">{{ day.label }}</q-item-label>
            <q-item-label v-if="day.periods.length === 0" caption>Closed</q-item-label>
          </q-item-section>
          <q-item-section>
            <div class="column q-gutter-y-xs">
              <div
                v-for="(period, index) in day.periods"
                :key="index"
                class="row items-center q-gutter-sm no-wrap"
              >
                <q-input
                  v-model="period.openTime"
                  type="time"
                  dense
                  outlined
                  :readonly="!canManage"
                  style="width: 130px"
                  :aria-label="`${day.label} opens`"
                />
                <span>to</span>
                <q-input
                  v-model="period.closeTime"
                  type="time"
                  dense
                  outlined
                  :readonly="!canManage"
                  style="width: 130px"
                  :aria-label="`${day.label} closes`"
                />
                <q-badge v-if="isOvernightPeriod(period)" color="deep-purple-4" label="+1 day" />
                <q-btn
                  v-if="canManage"
                  flat
                  dense
                  round
                  icon="close"
                  aria-label="Remove period"
                  @click="day.periods.splice(index, 1)"
                />
              </div>
            </div>
          </q-item-section>
          <q-item-section v-if="canManage" side>
            <div class="row q-gutter-xs">
              <q-btn flat dense icon="add" aria-label="Add period" @click="addPeriod(day)">
                <q-tooltip>Add period</q-tooltip>
              </q-btn>
              <q-btn
                flat
                dense
                icon="content_copy"
                aria-label="Copy to all days"
                @click="copyToAll(day)"
              >
                <q-tooltip>Copy to all days</q-tooltip>
              </q-btn>
            </div>
          </q-item-section>
        </q-item>
      </q-list>

      <div v-if="canManage" class="row justify-end q-mt-md">
        <q-btn
          color="primary"
          unelevated
          label="Save opening hours"
          :loading="saving"
          data-test="save-hours"
          @click="save"
        />
      </div>

      <div class="text-subtitle1 q-mt-lg q-mb-sm">Next 14 days</div>
      <div class="row q-col-gutter-sm" data-test="schedule-preview">
        <div v-for="d in schedule" :key="d.date" class="col-6 col-sm-4 col-md-3">
          <q-card flat bordered class="q-pa-sm">
            <div class="text-weight-medium">
              {{ formatDate(d.date) }}
              <q-badge
                v-if="d.isSpecial"
                color="orange"
                text-color="black"
                label="Special"
                class="q-ml-xs"
              />
            </div>
            <div v-if="d.isClosed" class="text-grey-7">Closed</div>
            <div v-for="p in d.periods" :key="p.startsAt" class="text-body2">
              {{ describePeriod(p) }}
            </div>
          </q-card>
        </div>
      </div>
    </template>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { useQuasar } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import { venueService } from '@/services/venueService.js'
import { normaliseError } from '@/utils/problemDetails.js'
import {
  describePeriod,
  flattenDays,
  groupByDay,
  isOvernight,
  isTime,
  validateWeek,
} from '@/utils/operatingHours.js'
import { addDays, formatDate, todayIso } from '@/utils/dates.js'

defineOptions({ name: 'OperatingHoursEditor' })

const props = defineProps({
  venue: { type: Object, required: true },
  canManage: { type: Boolean, default: false },
})

const $q = useQuasar()
const days = ref(groupByDay([]))
const schedule = ref([])
const loadError = ref(null)
const error = ref(null)
const saving = ref(false)

const isOvernightPeriod = (p) => isTime(p.openTime) && isTime(p.closeTime) && isOvernight(p)

async function loadSchedule() {
  const from = todayIso(props.venue.timeZone)
  const result = await venueService.getSchedule(props.venue.id, from, addDays(from, 13))
  schedule.value = result.days
}

async function load() {
  loadError.value = null
  try {
    days.value = groupByDay(await venueService.getOperatingHours(props.venue.id))
    await loadSchedule()
  } catch (e) {
    loadError.value = normaliseError(e)
  }
}

function addPeriod(day) {
  const last = day.periods.at(-1)
  day.periods.push(
    last
      ? { openTime: last.closeTime, closeTime: '23:00' }
      : { openTime: '09:00', closeTime: '17:00' },
  )
}

function copyToAll(source) {
  for (const day of days.value) {
    if (day.value !== source.value) {
      day.periods = source.periods.map((p) => ({ ...p }))
    }
  }
}

async function save() {
  const periods = flattenDays(days.value)
  error.value = validateWeek(periods)
  if (error.value) {
    return
  }
  saving.value = true
  try {
    days.value = groupByDay(await venueService.replaceOperatingHours(props.venue.id, periods))
    await loadSchedule()
    $q.notify?.({ type: 'positive', message: 'Opening hours saved.' })
  } catch (e) {
    const apiError = normaliseError(e)
    error.value = apiError.errors?.periods?.[0] ?? apiError.message
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>
