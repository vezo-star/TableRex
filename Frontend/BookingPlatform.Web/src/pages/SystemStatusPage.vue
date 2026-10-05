<template>
  <q-page padding>
    <div class="page-narrow">
      <div class="row items-center q-mb-md">
        <div class="text-h5 col">System status</div>
        <q-btn
          flat
          icon="refresh"
          label="Refresh"
          :loading="loading"
          data-test="refresh"
          @click="load"
        />
      </div>

      <div v-if="loading && !report" class="flex flex-center q-pa-xl" data-test="loading-state">
        <q-spinner size="lg" color="primary" />
      </div>

      <ErrorState
        v-else-if="error"
        :message="error.message"
        :trace-id="error.traceId"
        retryable
        @retry="load"
      />

      <template v-else-if="report">
        <q-banner
          rounded
          :class="isHealthy(report.status) ? 'bg-green-1' : 'bg-orange-1'"
          class="q-mb-md"
          data-test="overall-status"
        >
          API is <strong>{{ report.status }}</strong>
        </q-banner>

        <div v-if="report.checks.length === 0" class="text-grey-7" data-test="empty-state">
          No dependency checks are registered.
        </div>

        <q-list v-else bordered separator>
          <q-item v-for="check in report.checks" :key="check.name" data-test="check">
            <q-item-section>
              <q-item-label>{{ check.name }}</q-item-label>
              <q-item-label caption>{{ check.durationMs.toFixed(1) }} ms</q-item-label>
            </q-item-section>
            <q-item-section side>
              <q-chip
                dense
                text-color="white"
                :color="isHealthy(check.status) ? 'positive' : 'negative'"
                :label="check.status"
              />
            </q-item-section>
          </q-item>
        </q-list>
      </template>
    </div>
  </q-page>
</template>

<script setup>
import { onBeforeUnmount, onMounted, ref } from 'vue'
import ErrorState from '@/components/common/ErrorState.vue'
import { getReadiness } from '@/services/systemService.js'

defineOptions({ name: 'SystemStatusPage' })

const report = ref(null)
const error = ref(null)
const loading = ref(false)
let controller = null

const isHealthy = (status) => status === 'Healthy'

async function load() {
  controller?.abort()
  controller = new AbortController()
  loading.value = true
  error.value = null
  try {
    report.value = await getReadiness({ signal: controller.signal })
  } catch (e) {
    if (e?.name !== 'CanceledError') {
      report.value = null
      error.value = e
    }
  } finally {
    loading.value = false
  }
}

onMounted(load)
onBeforeUnmount(() => controller?.abort())
</script>
