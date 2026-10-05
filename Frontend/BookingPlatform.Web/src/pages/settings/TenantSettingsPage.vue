<template>
  <q-page padding>
    <div class="text-h5 q-mb-md">Workspace settings</div>

    <ErrorState
      v-if="loadError"
      :message="loadError.message"
      :trace-id="loadError.traceId"
      retryable
      @retry="load"
    />

    <q-card v-else flat bordered class="settings-card">
      <q-inner-loading :showing="loading" />
      <q-form
        v-if="tenant"
        class="q-pa-md q-gutter-sm"
        data-test="tenant-settings-form"
        @submit.prevent="save"
      >
        <q-banner
          v-if="saveAction.error.value && !Object.keys(saveAction.fieldErrors.value).length"
          dense
          rounded
          class="bg-red-1 text-negative"
        >
          {{ saveAction.error.value.message }}
        </q-banner>
        <q-input
          v-model.trim="form.name"
          label="Name"
          outlined
          :error="!!saveAction.fieldError('name')"
          :error-message="saveAction.fieldError('name')"
        />
        <q-input
          :model-value="tenant.slug"
          label="Public slug"
          outlined
          readonly
          hint="Used in public booking links; cannot be changed."
        />
        <q-select
          v-model="form.defaultTimeZone"
          :options="filteredZones"
          label="Default time zone"
          outlined
          use-input
          input-debounce="0"
          :error="!!saveAction.fieldError('defaultTimeZone')"
          :error-message="saveAction.fieldError('defaultTimeZone')"
          @filter="filterZones"
        />
        <div>
          <q-btn
            type="submit"
            color="primary"
            unelevated
            label="Save"
            :loading="saveAction.loading.value"
          />
        </div>
      </q-form>
    </q-card>

    <div v-if="tenant" class="settings-grid q-mt-md">
      <PlanUsageCard />
      <BrandingSettingsCard :tenant-slug="tenant.slug" />
    </div>
  </q-page>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { useQuasar } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import BrandingSettingsCard from '@/components/settings/BrandingSettingsCard.vue'
import PlanUsageCard from '@/components/settings/PlanUsageCard.vue'
import { tenancyService } from '@/services/tenancyService.js'
import { useApiAction } from '@/composables/useApiAction.js'
import { normaliseError } from '@/utils/problemDetails.js'
import { timeZoneOptions } from '@/utils/timeZones.js'

defineOptions({ name: 'TenantSettingsPage' })

const $q = useQuasar()
const tenant = ref(null)
const loading = ref(false)
const loadError = ref(null)
const form = reactive({ name: '', defaultTimeZone: '' })
const zones = timeZoneOptions()
const filteredZones = ref(zones)

function filterZones(value, update) {
  update(() => {
    const needle = value.toLowerCase()
    filteredZones.value = needle ? zones.filter((z) => z.toLowerCase().includes(needle)) : zones
  })
}

async function load() {
  loading.value = true
  loadError.value = null
  try {
    tenant.value = await tenancyService.getCurrentTenant()
    Object.assign(form, { name: tenant.value.name, defaultTimeZone: tenant.value.defaultTimeZone })
  } catch (e) {
    loadError.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

const saveAction = useApiAction((payload) => tenancyService.updateCurrentTenant(payload))

async function save() {
  const updated = await saveAction.run({ ...form })
  if (updated) {
    tenant.value = updated
    $q.notify?.({ type: 'positive', message: 'Settings saved.' })
  }
}

onMounted(load)
</script>

<style scoped>
.settings-card {
  max-width: 560px;
  position: relative;
}
.settings-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 16px;
  align-items: start;
  max-width: 1140px;
}
</style>
