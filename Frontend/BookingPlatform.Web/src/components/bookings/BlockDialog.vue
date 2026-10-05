<template>
  <q-dialog :model-value="modelValue" @update:model-value="$emit('update:modelValue', $event)">
    <q-card class="block-dialog" data-test="block-dialog">
      <q-form @submit.prevent="save">
        <q-card-section class="text-h6">Block tables</q-card-section>
        <q-card-section class="q-gutter-sm q-pt-none">
          <q-select
            v-model="form.resourceIds"
            :options="resourceOptions"
            emit-value
            map-options
            multiple
            use-chips
            label="Tables / resources *"
            outlined
            dense
            :error="!!fieldError('resourceIds')"
            :error-message="fieldError('resourceIds')"
            data-test="block-resources"
          />
          <div class="row q-col-gutter-sm">
            <q-input
              v-model="form.date"
              class="col-12 col-sm-4"
              type="date"
              label="Date"
              outlined
              dense
            />
            <q-input
              v-model="form.start"
              class="col-6 col-sm-4"
              type="time"
              label="From"
              outlined
              dense
              data-test="block-start"
            />
            <q-input
              v-model="form.end"
              class="col-6 col-sm-4"
              type="time"
              label="To"
              outlined
              dense
              data-test="block-end"
            />
          </div>
          <div class="text-caption text-grey-7">
            Times are in {{ venue.timeZone }}. An end time before the start runs past midnight.
          </div>
          <q-select
            v-model="form.reason"
            :options="reasonOptions"
            emit-value
            map-options
            label="Reason"
            outlined
            dense
          />
          <q-input v-model="form.notes" label="Notes" outlined dense maxlength="1000" />
          <q-banner v-if="error" rounded class="bg-red-1 text-negative" data-test="block-error">
            {{ error.message }}
          </q-banner>
        </q-card-section>
        <q-card-actions align="right">
          <q-btn v-close-popup flat no-caps label="Cancel" />
          <q-btn
            type="submit"
            color="primary"
            unelevated
            no-caps
            label="Block"
            :loading="saving"
            :disable="!form.resourceIds.length || !form.start || !form.end"
            data-test="block-save"
          />
        </q-card-actions>
      </q-form>
    </q-card>
  </q-dialog>
</template>

<script setup>
import { computed, reactive, ref, watch } from 'vue'
import { blockService } from '@/services/bookingService.js'
import { BlockReasonLabels } from '@/utils/bookings.js'
import { addDays, todayIso, zonedTimeToUtc } from '@/utils/dates.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'BlockDialog' })

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  venue: { type: Object, required: true },
  resources: { type: Array, default: () => [] },
  /** Optional `{ date, start, resourceId }`. */
  prefill: { type: Object, default: null },
})
const emit = defineEmits(['update:modelValue', 'created'])

const form = reactive({
  resourceIds: [],
  date: '',
  start: '',
  end: '',
  reason: 'UNAVAILABLE',
  notes: '',
})
const saving = ref(false)
const error = ref(null)

const resourceOptions = computed(() =>
  props.resources.filter((r) => r.isActive).map((r) => ({ label: r.name, value: r.id })),
)
const reasonOptions = Object.entries(BlockReasonLabels).map(([value, label]) => ({ value, label }))

function fieldError(field) {
  return error.value?.errors?.[field]?.[0] ?? null
}

async function save() {
  error.value = null
  const tz = props.venue.timeZone
  const startMs = zonedTimeToUtc(form.date, form.start, tz)
  const endDate = form.end <= form.start ? addDays(form.date, 1) : form.date
  const endMs = zonedTimeToUtc(endDate, form.end, tz)
  saving.value = true
  try {
    const block = await blockService.create({
      venueId: props.venue.id,
      resourceIds: form.resourceIds,
      startsAt: new Date(startMs).toISOString(),
      endsAt: new Date(endMs).toISOString(),
      reason: form.reason,
      notes: form.notes.trim() || null,
    })
    emit('created', block)
    emit('update:modelValue', false)
  } catch (e) {
    error.value = normaliseError(e)
  } finally {
    saving.value = false
  }
}

watch(
  () => props.modelValue,
  (open) => {
    if (!open) {
      return
    }
    error.value = null
    form.resourceIds = props.prefill?.resourceId ? [props.prefill.resourceId] : []
    form.date = props.prefill?.date ?? todayIso(props.venue.timeZone)
    form.start = props.prefill?.start ?? ''
    form.end = ''
    form.reason = 'UNAVAILABLE'
    form.notes = ''
  },
  { immediate: true },
)
</script>

<style scoped>
.block-dialog {
  width: 520px;
  max-width: 95vw;
}
</style>
