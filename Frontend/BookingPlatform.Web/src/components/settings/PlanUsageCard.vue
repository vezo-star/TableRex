<template>
  <q-card flat bordered class="relative-position" data-test="plan-card">
    <q-card-section class="row items-center">
      <div class="col">
        <div class="text-subtitle1">Plan &amp; usage</div>
        <div v-if="subscription" class="text-caption text-grey-7">
          {{ subscription.plan.description }}
        </div>
      </div>
      <q-chip v-if="subscription" color="primary" text-color="white" square data-test="plan-name">
        {{ subscription.plan.name }}
      </q-chip>
    </q-card-section>

    <q-inner-loading :showing="loading" />

    <q-card-section v-if="error" class="q-pt-none">
      <ErrorState :message="error.message" :trace-id="error.traceId" retryable @retry="load" />
    </q-card-section>

    <q-card-section v-else-if="subscription" class="q-pt-none q-gutter-y-md">
      <div v-for="row in rows" :key="row.key" :data-test="`usage-${row.key}`">
        <div class="row items-baseline">
          <div class="col text-body2">{{ row.label }}</div>
          <div class="text-body2 text-weight-medium">{{ usageText(row.used, row.limit) }}</div>
        </div>
        <q-linear-progress
          v-if="row.limit != null"
          rounded
          size="8px"
          class="q-mt-xs"
          :value="usageRatio(row.used, row.limit)"
          :color="row.used >= row.limit ? 'negative' : 'primary'"
          :aria-label="`${row.label}: ${usageText(row.used, row.limit)}`"
        />
      </div>
      <div class="row items-baseline" data-test="usage-bookings">
        <div class="col text-body2">Bookings this month</div>
        <div class="text-body2 text-weight-medium">{{ subscription.usage.bookingsThisMonth }}</div>
      </div>
      <div class="row items-center text-body2">
        <q-icon
          :name="subscription.plan.customBranding ? 'check_circle' : 'block'"
          :color="subscription.plan.customBranding ? 'positive' : 'grey-6'"
          class="q-mr-xs"
        />
        Custom branding {{ subscription.plan.customBranding ? 'included' : 'not included' }}
      </div>
      <div class="text-caption text-grey-7">
        Reaching a limit blocks new additions only; nothing is removed. Contact the platform team to
        change your plan.
      </div>
    </q-card-section>
  </q-card>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import ErrorState from '@/components/common/ErrorState.vue'
import { saasService } from '@/services/saasService.js'
import { normaliseError } from '@/utils/problemDetails.js'
import { usageRatio, usageText } from '@/utils/reports.js'

defineOptions({ name: 'PlanUsageCard' })

const subscription = ref(null)
const loading = ref(false)
const error = ref(null)

const rows = computed(() => {
  const s = subscription.value
  if (!s) {
    return []
  }
  return [
    { key: 'venues', label: 'Venues', used: s.usage.venues, limit: s.limits.maxVenues },
    {
      key: 'staff',
      label: 'Active staff',
      used: s.usage.activeStaff,
      limit: s.limits.maxActiveStaff,
    },
    {
      key: 'resources',
      label: 'Resources in your largest venue',
      used: s.usage.maxResourcesInAVenue,
      limit: s.limits.maxResourcesPerVenue,
    },
  ]
})

async function load() {
  loading.value = true
  error.value = null
  try {
    subscription.value = await saasService.getSubscription()
  } catch (e) {
    error.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

onMounted(load)

defineExpose({ load })
</script>
