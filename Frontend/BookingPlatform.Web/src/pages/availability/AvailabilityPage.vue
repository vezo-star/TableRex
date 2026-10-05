<template>
  <q-page padding>
    <div class="row items-center q-mb-md">
      <div class="text-h5 col">
        Availability
        <span v-if="venue" class="text-subtitle1 text-grey-7">· {{ venue.name }}</span>
      </div>
    </div>

    <NoVenueState v-if="!venueId && !venues.loading" />
    <template v-else-if="venue">
      <div class="row q-col-gutter-sm items-start q-mb-md" data-test="availability-filters">
        <div class="col-12 col-md-4 row no-wrap items-center">
          <q-btn
            flat
            round
            dense
            icon="chevron_left"
            aria-label="Previous day"
            @click="shiftDay(-1)"
          />
          <q-input
            v-model="filters.date"
            class="col"
            type="date"
            label="Date"
            outlined
            dense
            data-test="availability-date"
          />
          <q-btn flat round dense icon="chevron_right" aria-label="Next day" @click="shiftDay(1)" />
        </div>
        <q-input
          v-model.number="filters.partySize"
          class="col-6 col-md-2"
          type="number"
          min="1"
          max="1000"
          label="Party size"
          outlined
          dense
          data-test="availability-party"
          :error="!!fieldError('partySize')"
          :error-message="fieldError('partySize')"
        />
        <q-select
          v-model="filters.durationMinutes"
          class="col-6 col-md-2"
          :options="durations"
          emit-value
          map-options
          label="Duration"
          outlined
          dense
          :error="!!fieldError('durationMinutes')"
          :error-message="fieldError('durationMinutes')"
        />
        <q-select
          v-model="filters.sectionId"
          class="col-6 col-md-2"
          :options="sectionOptions"
          emit-value
          map-options
          clearable
          label="Section"
          outlined
          dense
        />
        <q-input
          v-model="filters.time"
          class="col-6 col-md-2"
          type="time"
          label="Preferred time"
          clearable
          outlined
          dense
          hint="Shows ±90 min"
        />
      </div>

      <ErrorState
        v-if="error && !fieldError('partySize') && !fieldError('durationMinutes')"
        :message="error.message"
        :trace-id="error.traceId"
        retryable
        @retry="search"
      />
      <div v-else-if="result" class="row q-col-gutter-md">
        <div class="col-12 col-md-7">
          <div class="row items-center q-mb-sm">
            <div class="text-subtitle1 col">
              {{ formatDate(result.date) }}
              <span class="text-grey-7 text-body2"
                >· party of {{ result.partySize }} ·
                {{ formatDuration(result.durationMinutes) }}</span
              >
            </div>
            <div v-if="!result.isClosed" class="text-body2" data-test="availability-summary">
              {{ summary.available }} of {{ summary.total }} times available
            </div>
          </div>
          <q-banner
            v-if="result.isClosed"
            rounded
            class="bg-grey-2"
            data-test="availability-closed"
          >
            The venue is closed on this date.
          </q-banner>
          <q-banner
            v-else-if="result.slots.length === 0"
            rounded
            class="bg-grey-2"
            data-test="availability-empty"
          >
            No bookable times remain for this date.
          </q-banner>
          <div v-else class="row q-gutter-sm" data-test="availability-slots">
            <q-btn
              v-for="slot in result.slots"
              :key="slot.startsAt"
              :label="slot.time"
              :outline="selected?.startsAt !== slot.startsAt"
              :unelevated="selected?.startsAt === slot.startsAt"
              :color="slot.available ? 'positive' : 'grey-6'"
              :class="{ 'slot-unavailable': !slot.available }"
              no-caps
              dense
              class="slot-btn"
              :aria-label="`${slot.time} ${slot.available ? 'available' : 'unavailable'}`"
              @click="selected = slot"
            />
          </div>
          <q-inner-loading :showing="loading" />
        </div>

        <div class="col-12 col-md-5">
          <q-card flat bordered data-test="availability-options">
            <q-card-section v-if="!selected" class="text-grey-7">
              Select a time to see which tables or resources are free.
            </q-card-section>
            <template v-else>
              <q-card-section class="text-subtitle1">
                {{ selected.time }}–{{ formatTime(selected.endsAt, venue.timeZone) }}
              </q-card-section>
              <q-separator />
              <q-list v-if="selected.options.length" separator>
                <q-item v-for="(option, index) in selected.options" :key="option.id">
                  <q-item-section avatar>
                    <q-icon
                      :name="option.kind === 'COMBINATION' ? 'join_inner' : 'table_restaurant'"
                      :color="index === 0 ? 'primary' : 'grey-7'"
                    />
                  </q-item-section>
                  <q-item-section>
                    <q-item-label>
                      {{ optionResourceLabel(option, resourcesById) }}
                      <q-badge
                        v-if="index === 0"
                        color="primary"
                        label="Best fit"
                        class="q-ml-xs"
                      />
                    </q-item-label>
                    <q-item-label caption>
                      {{ option.kind === 'COMBINATION' ? `${option.name} · ` : '' }}seats
                      {{ option.capacity }}
                      <template v-if="sectionOf(option)"> · {{ sectionOf(option) }}</template>
                    </q-item-label>
                  </q-item-section>
                </q-item>
              </q-list>
              <q-card-section v-else class="text-grey-7">
                Nothing is free for this party at {{ selected.time }}.
              </q-card-section>
            </template>
          </q-card>
        </div>
      </div>
      <q-inner-loading :showing="loading && !result" />
    </template>
  </q-page>
</template>

<script setup>
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import ErrorState from '@/components/common/ErrorState.vue'
import NoVenueState from '@/components/venues/NoVenueState.vue'
import { availabilityService } from '@/services/availabilityService.js'
import { venueService } from '@/services/venueService.js'
import { useVenuesStore } from '@/stores/venues.js'
import { addDays, formatDate, formatTime, todayIso } from '@/utils/dates.js'
import {
  durationOptions,
  formatDuration,
  optionResourceLabel,
  summariseSlots,
} from '@/utils/availability.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'AvailabilityPage' })

const venues = useVenuesStore()
const venueId = computed(() => venues.currentVenueId)
const venue = computed(() => venues.currentVenue)

const filters = reactive({
  date: '',
  partySize: 2,
  durationMinutes: null,
  sectionId: null,
  time: null,
})
const result = ref(null)
const selected = ref(null)
const loading = ref(false)
const error = ref(null)
const sections = ref([])
const resources = ref([])

const durations = computed(() => durationOptions(venue.value?.rules))
const sectionOptions = computed(() =>
  sections.value.filter((s) => s.isActive).map((s) => ({ label: s.name, value: s.id })),
)
const resourcesById = computed(() => new Map(resources.value.map((r) => [r.id, r])))
const summary = computed(() => summariseSlots(result.value?.slots))

function fieldError(field) {
  return error.value?.errors?.[field]?.[0] ?? null
}

function sectionOf(option) {
  if (option.kind !== 'RESOURCE') {
    return null
  }
  const sectionId = resourcesById.value.get(option.id)?.sectionId
  return sections.value.find((s) => s.id === sectionId)?.name ?? null
}

function shiftDay(days) {
  filters.date = addDays(filters.date, days)
}

let controller = null
let timer = null

async function search() {
  if (!venueId.value || !filters.date || !filters.partySize) {
    return
  }
  controller?.abort()
  controller = new AbortController()
  const { signal } = controller
  loading.value = true
  error.value = null
  try {
    const data = await availabilityService.search(
      {
        venueId: venueId.value,
        date: filters.date,
        partySize: filters.partySize,
        durationMinutes: filters.durationMinutes,
        sectionId: filters.sectionId,
        time: filters.time,
      },
      { signal },
    )
    result.value = data
    selected.value =
      data.slots.find((s) => s.startsAt === selected.value?.startsAt) ??
      data.slots.find((s) => s.available) ??
      null
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

function scheduleSearch() {
  clearTimeout(timer)
  timer = setTimeout(search, 250)
}

async function loadVenueData() {
  result.value = null
  selected.value = null
  sections.value = []
  resources.value = []
  if (!venueId.value || !venue.value) {
    return
  }
  filters.date = todayIso(venue.value.timeZone)
  filters.durationMinutes = venue.value.rules.defaultDurationMinutes
  filters.sectionId = null
  filters.time = null
  try {
    const [venueSections, venueResources] = await Promise.all([
      venueService.listSections(venueId.value),
      venueService.listAllResources(venueId.value),
    ])
    sections.value = venueSections
    resources.value = venueResources
  } catch (e) {
    error.value = normaliseError(e)
  }
}

watch(() => venue.value?.id, loadVenueData, { immediate: true })
watch(
  () => [
    venueId.value,
    filters.date,
    filters.partySize,
    filters.durationMinutes,
    filters.sectionId,
    filters.time,
  ],
  scheduleSearch,
  { immediate: true },
)

onBeforeUnmount(() => {
  clearTimeout(timer)
  controller?.abort()
})
</script>

<style scoped>
.slot-btn {
  min-width: 64px;
}

.slot-unavailable {
  text-decoration: line-through;
}
</style>
