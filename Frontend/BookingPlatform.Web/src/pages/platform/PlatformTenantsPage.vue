<template>
  <q-page padding>
    <div class="row items-center q-mb-md">
      <div class="text-h5 col">Tenants</div>
      <q-btn
        color="primary"
        unelevated
        icon="add_business"
        label="New tenant"
        @click="openCreate"
      />
    </div>

    <ErrorState
      v-if="loadError"
      :message="loadError.message"
      :trace-id="loadError.traceId"
      retryable
      @retry="load"
    />

    <q-table
      v-else
      v-model:pagination="pagination"
      flat
      bordered
      row-key="id"
      :rows="rows"
      :columns="columns"
      :loading="loading"
      @request="onRequest"
    >
      <template #body-cell-planCode="props">
        <q-td :props="props">
          <q-chip dense square :data-test="`plan-${props.row.slug}`">
            {{ planName(props.row.planCode) }}
          </q-chip>
        </q-td>
      </template>
      <template #body-cell-actions="props">
        <q-td :props="props">
          <q-btn
            flat
            dense
            no-caps
            color="primary"
            icon="tune"
            label="Plan"
            :data-test="`change-plan-${props.row.slug}`"
            @click="openPlan(props.row)"
          />
        </q-td>
      </template>
    </q-table>

    <q-dialog v-model="planOpen">
      <q-card style="min-width: 340px; max-width: 480px" data-test="plan-dialog">
        <q-form @submit.prevent="savePlan">
          <q-card-section>
            <div class="text-h6">Plan for {{ planTenant?.name }}</div>
            <div class="text-caption text-grey-7">
              Lowering a limit never removes data; it only blocks new additions.
            </div>
          </q-card-section>
          <q-card-section class="q-gutter-sm">
            <q-banner
              v-if="planAction.error.value && !Object.keys(planAction.fieldErrors.value).length"
              dense
              rounded
              class="bg-red-1 text-negative"
            >
              {{ planAction.error.value.message }}
            </q-banner>
            <div v-if="planUsage" class="text-body2" data-test="plan-usage">
              Using {{ planUsage.venues }} venues, {{ planUsage.activeStaff }} active staff and up
              to {{ planUsage.maxResourcesInAVenue }} resources in one venue.
            </div>
            <q-select
              v-model="planForm.planCode"
              :options="planOptions"
              label="Plan"
              outlined
              dense
              emit-value
              map-options
              data-test="plan-select"
            />
            <div class="text-subtitle2 q-mt-sm">
              Overrides (leave blank to use the plan's limit)
            </div>
            <q-input
              v-model.number="planForm.maxVenuesOverride"
              type="number"
              min="1"
              label="Max venues"
              outlined
              dense
              :hint="limitHint('maxVenues')"
              :error="!!planAction.fieldError('maxVenuesOverride')"
              :error-message="planAction.fieldError('maxVenuesOverride')"
              data-test="override-venues"
            />
            <q-input
              v-model.number="planForm.maxActiveStaffOverride"
              type="number"
              min="1"
              label="Max active staff"
              outlined
              dense
              :hint="limitHint('maxActiveStaff')"
              :error="!!planAction.fieldError('maxActiveStaffOverride')"
              :error-message="planAction.fieldError('maxActiveStaffOverride')"
            />
            <q-input
              v-model.number="planForm.maxResourcesPerVenueOverride"
              type="number"
              min="1"
              label="Max resources per venue"
              outlined
              dense
              :hint="limitHint('maxResourcesPerVenue')"
              :error="!!planAction.fieldError('maxResourcesPerVenueOverride')"
              :error-message="planAction.fieldError('maxResourcesPerVenueOverride')"
            />
          </q-card-section>
          <q-card-actions align="right">
            <q-btn v-close-popup flat label="Cancel" />
            <q-btn
              type="submit"
              color="primary"
              unelevated
              label="Save plan"
              :loading="planAction.loading.value"
              data-test="plan-save"
            />
          </q-card-actions>
        </q-form>
      </q-card>
    </q-dialog>

    <q-dialog v-model="createOpen" persistent>
      <q-card style="min-width: 360px; max-width: 520px">
        <q-form @submit.prevent="submit">
          <q-card-section class="text-h6">New tenant</q-card-section>
          <q-card-section class="q-gutter-sm">
            <q-banner
              v-if="action.error.value && !Object.keys(action.fieldErrors.value).length"
              dense
              rounded
              class="bg-red-1 text-negative"
            >
              {{ action.error.value.message }}
            </q-banner>
            <q-input
              v-model.trim="form.name"
              label="Business name"
              outlined
              dense
              :error="!!action.fieldError('name')"
              :error-message="action.fieldError('name')"
            />
            <q-input
              v-model.trim="form.slug"
              label="Slug"
              hint="lowercase-with-hyphens"
              outlined
              dense
              :error="!!action.fieldError('slug')"
              :error-message="action.fieldError('slug')"
            />
            <q-select
              v-model="form.defaultTimeZone"
              :options="zones"
              label="Time zone"
              outlined
              dense
            />
            <q-select
              v-model="form.planCode"
              :options="planOptions"
              label="Plan"
              outlined
              dense
              emit-value
              map-options
              :error="!!action.fieldError('planCode')"
              :error-message="action.fieldError('planCode')"
              data-test="create-plan"
            />
            <div class="text-subtitle2 q-mt-md">First owner</div>
            <q-input
              v-model.trim="form.owner.email"
              type="email"
              label="Owner email"
              outlined
              dense
              :error="!!action.fieldError('owner.email')"
              :error-message="action.fieldError('owner.email')"
            />
            <q-input
              v-model.trim="form.owner.displayName"
              label="Owner name"
              outlined
              dense
              :error="!!action.fieldError('owner.displayName')"
              :error-message="action.fieldError('owner.displayName')"
            />
            <q-input
              v-model="form.owner.password"
              type="password"
              label="Initial password (new accounts)"
              autocomplete="new-password"
              outlined
              dense
              :error="!!action.fieldError('owner.password')"
              :error-message="action.fieldError('owner.password')"
            />
          </q-card-section>
          <q-card-actions align="right">
            <q-btn v-close-popup flat label="Cancel" />
            <q-btn
              type="submit"
              color="primary"
              unelevated
              label="Create"
              :loading="action.loading.value"
            />
          </q-card-actions>
        </q-form>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { useQuasar } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import { saasService } from '@/services/saasService.js'
import { tenancyService } from '@/services/tenancyService.js'
import { useApiAction } from '@/composables/useApiAction.js'
import { normaliseError } from '@/utils/problemDetails.js'
import { timeZoneOptions } from '@/utils/timeZones.js'

defineOptions({ name: 'PlatformTenantsPage' })

const columns = [
  { name: 'name', label: 'Name', field: 'name', align: 'left' },
  { name: 'slug', label: 'Slug', field: 'slug', align: 'left' },
  { name: 'status', label: 'Status', field: 'status', align: 'left' },
  { name: 'defaultTimeZone', label: 'Time zone', field: 'defaultTimeZone', align: 'left' },
  { name: 'planCode', label: 'Plan', field: 'planCode', align: 'left' },
  { name: 'actions', label: '', field: 'id', align: 'right' },
]
const $q = useQuasar()
const plans = ref([])
const planOptions = computed(() => plans.value.map((p) => ({ label: p.name, value: p.code })))

function planName(code) {
  return plans.value.find((p) => p.code === code)?.name ?? code
}

async function loadPlans() {
  try {
    plans.value = await saasService.listPlans()
  } catch {
    plans.value = []
  }
}
const zones = timeZoneOptions()
const rows = ref([])
const loading = ref(false)
const loadError = ref(null)
const pagination = ref({ page: 1, rowsPerPage: 25, rowsNumber: 0 })

async function load() {
  loading.value = true
  loadError.value = null
  try {
    const result = await tenancyService.listPlatformTenants({
      page: pagination.value.page,
      pageSize: pagination.value.rowsPerPage,
    })
    rows.value = result.items
    pagination.value = { ...pagination.value, rowsNumber: result.totalCount }
  } catch (e) {
    loadError.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

function onRequest({ pagination: next }) {
  pagination.value = { ...pagination.value, page: next.page, rowsPerPage: next.rowsPerPage }
  load()
}

const createOpen = ref(false)
const emptyForm = () => ({
  name: '',
  slug: '',
  defaultTimeZone: 'Africa/Johannesburg',
  planCode: 'STARTER',
  owner: { email: '', displayName: '', password: '' },
})
const form = reactive(emptyForm())
const action = useApiAction((payload) => tenancyService.createPlatformTenant(payload))

function openCreate() {
  Object.assign(form, emptyForm())
  action.reset()
  createOpen.value = true
}

async function submit() {
  const created = await action.run({
    ...form,
    owner: { ...form.owner, password: form.owner.password || null },
  })
  if (created) {
    createOpen.value = false
    load()
  }
}

const planOpen = ref(false)
const planTenant = ref(null)
const planUsage = ref(null)
const planForm = reactive({
  planCode: '',
  maxVenuesOverride: null,
  maxActiveStaffOverride: null,
  maxResourcesPerVenueOverride: null,
})
const planAction = useApiAction((payload) =>
  saasService.updateTenantSubscription(planTenant.value.id, payload),
)

function limitHint(key) {
  const plan = plans.value.find((p) => p.code === planForm.planCode)
  if (!plan) {
    return ''
  }
  return plan[key] == null ? 'Plan limit: unlimited' : `Plan limit: ${plan[key]}`
}

async function openPlan(tenant) {
  planTenant.value = tenant
  planUsage.value = null
  planAction.reset()
  Object.assign(planForm, {
    planCode: tenant.planCode,
    maxVenuesOverride: null,
    maxActiveStaffOverride: null,
    maxResourcesPerVenueOverride: null,
  })
  planOpen.value = true
  try {
    const subscription = await saasService.getTenantSubscription(tenant.id)
    planUsage.value = subscription.usage
    Object.assign(planForm, {
      planCode: subscription.plan.code,
      maxVenuesOverride: subscription.overrides.maxVenues,
      maxActiveStaffOverride: subscription.overrides.maxActiveStaff,
      maxResourcesPerVenueOverride: subscription.overrides.maxResourcesPerVenue,
    })
  } catch {
    // The dialog still works with the plan from the list; overrides start blank.
  }
}

const blankToNull = (value) => (value === '' || value == null ? null : Number(value))

async function savePlan() {
  const saved = await planAction.run({
    planCode: planForm.planCode,
    maxVenuesOverride: blankToNull(planForm.maxVenuesOverride),
    maxActiveStaffOverride: blankToNull(planForm.maxActiveStaffOverride),
    maxResourcesPerVenueOverride: blankToNull(planForm.maxResourcesPerVenueOverride),
  })
  if (saved) {
    planOpen.value = false
    $q.notify?.({
      type: 'positive',
      message: `${planTenant.value.name} is now on ${saved.plan.name}.`,
    })
    load()
  }
}

onMounted(() => {
  loadPlans()
  load()
})
</script>
