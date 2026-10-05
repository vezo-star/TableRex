<template>
  <q-page padding>
    <div class="text-h5 q-mb-md">Audit log</div>

    <div class="row q-col-gutter-sm q-mb-md" data-test="audit-filters">
      <q-select
        v-model="filters.entityType"
        class="col-12 col-sm-4 col-md-3"
        :options="entityOptions"
        emit-value
        map-options
        clearable
        label="Entity"
        outlined
        dense
        data-test="audit-entity"
      />
      <q-input
        v-model="filters.action"
        class="col-12 col-sm-4 col-md-3"
        label="Action (e.g. updated)"
        outlined
        dense
        clearable
        debounce="400"
        maxlength="50"
      />
      <q-input
        v-model="filters.entityId"
        class="col-12 col-sm-4 col-md-4"
        label="Entity id"
        outlined
        dense
        clearable
        debounce="400"
        :error="!!filterError"
        :error-message="filterError"
      />
    </div>

    <ErrorState
      v-if="error"
      :message="error.message"
      :trace-id="error.traceId"
      retryable
      @retry="reload"
    />

    <q-list bordered separator class="rounded-borders" data-test="audit-list">
      <q-item v-if="!items.length && !loading">
        <q-item-section class="text-grey-7">No audit entries match these filters.</q-item-section>
      </q-item>
      <q-expansion-item v-for="item in items" :key="item.id" dense :data-test="`audit-${item.id}`">
        <template #header>
          <q-item-section avatar>
            <q-icon :name="iconFor(item.action)" color="grey-8" />
          </q-item-section>
          <q-item-section>
            <q-item-label>
              <span class="text-weight-medium">{{ labelFor(item.entityType) }}</span>
              {{ actionLabel(item.action) }}
            </q-item-label>
            <q-item-label caption>
              {{ item.userDisplayName || item.userEmail || 'System' }}
              <template v-if="item.ip"> · {{ item.ip }}</template>
            </q-item-label>
          </q-item-section>
          <q-item-section side>
            <q-item-label caption>{{ formatInstant(item.createdAt) }}</q-item-label>
          </q-item-section>
        </template>
        <q-card flat class="bg-grey-1">
          <q-card-section class="q-py-sm text-caption">
            <div v-if="item.entityId">
              Entity id: <code>{{ item.entityId }}</code>
            </div>
            <div v-if="item.correlationId">
              Correlation id: <code>{{ item.correlationId }}</code>
            </div>
            <div v-if="item.oldValues" class="q-mt-sm">
              <div class="text-weight-medium">Before</div>
              <pre class="audit-json">{{ pretty(item.oldValues) }}</pre>
            </div>
            <div v-if="item.newValues" class="q-mt-sm">
              <div class="text-weight-medium">After</div>
              <pre class="audit-json">{{ pretty(item.newValues) }}</pre>
            </div>
          </q-card-section>
        </q-card>
      </q-expansion-item>
    </q-list>

    <div class="q-mt-md text-center">
      <q-btn
        v-if="nextCursor"
        outline
        no-caps
        label="Load more"
        :loading="loading"
        data-test="audit-more"
        @click="loadMore"
      />
      <q-spinner v-else-if="loading" size="md" />
    </div>
  </q-page>
</template>

<script setup>
import { computed, reactive, ref, watch } from 'vue'
import ErrorState from '@/components/common/ErrorState.vue'
import { auditService } from '@/services/customerService.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'AuditLogPage' })

const ENTITY_LABELS = {
  booking: 'Booking',
  customer: 'Customer',
  resource_block: 'Block',
  resource_hold: 'Hold',
  venue: 'Venue',
  venue_section: 'Section',
  special_operating_hours: 'Special hours',
  bookable_resource: 'Resource',
  resource_type: 'Resource type',
  resource_combination: 'Combination',
  floor_plan: 'Floor plan',
  tenant: 'Organisation',
  tenant_user: 'Staff member',
  user: 'User',
}
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

const entityOptions = Object.entries(ENTITY_LABELS).map(([value, label]) => ({ value, label }))

const filters = reactive({ entityType: null, action: '', entityId: '' })
const items = ref([])
const nextCursor = ref(null)
const loading = ref(false)
const error = ref(null)

const filterError = computed(() =>
  filters.entityId && !UUID.test(filters.entityId.trim()) ? 'Enter a full id' : null,
)

const instantFormat = new Intl.DateTimeFormat('en-GB', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
})

function formatInstant(instant) {
  return instantFormat.format(new Date(instant))
}

function labelFor(entityType) {
  return ENTITY_LABELS[entityType] ?? entityType
}

function actionLabel(action) {
  return action.replace(/^auth\./, '').replace(/[._]/g, ' ')
}

function iconFor(action) {
  if (action.includes('created')) {
    return 'add_circle_outline'
  }
  if (action.includes('deleted') || action.includes('cancelled') || action.includes('anonymized')) {
    return 'remove_circle_outline'
  }
  if (action.includes('login') || action.includes('logout') || action.includes('locked')) {
    return 'login'
  }
  return 'edit'
}

function pretty(value) {
  return JSON.stringify(value, null, 2)
}

async function fetchPage(before) {
  if (filterError.value) {
    return
  }
  loading.value = true
  try {
    const page = await auditService.list({
      entityType: filters.entityType,
      entityId: filters.entityId?.trim() || null,
      action: filters.action?.trim() || null,
      before,
      limit: 50,
    })
    items.value = before ? [...items.value, ...page.items] : page.items
    nextCursor.value = page.nextCursor
    error.value = null
  } catch (e) {
    error.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

function reload() {
  items.value = []
  nextCursor.value = null
  fetchPage(null)
}

function loadMore() {
  fetchPage(nextCursor.value)
}

watch(() => [filters.entityType, filters.action, filters.entityId], reload, { immediate: true })
</script>

<style scoped>
.audit-json {
  margin: 4px 0 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-size: 12px;
}
</style>
