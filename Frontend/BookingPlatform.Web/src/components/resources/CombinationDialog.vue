<template>
  <q-dialog
    :model-value="modelValue"
    persistent
    @update:model-value="$emit('update:modelValue', $event)"
  >
    <q-card style="width: 560px; max-width: 95vw">
      <q-form data-test="combination-form" @submit.prevent="save">
        <q-card-section class="text-h6">{{
          combination ? 'Edit combination' : 'Add combination'
        }}</q-card-section>
        <q-card-section class="row q-col-gutter-sm">
          <div v-if="generalError" class="col-12">
            <q-banner dense rounded class="bg-red-1 text-negative">{{ generalError }}</q-banner>
          </div>
          <q-input
            v-model.trim="form.name"
            class="col-12"
            label="Name"
            hint="e.g. Table 5 + 6"
            outlined
            dense
            :error="!!action.fieldError('name')"
            :error-message="action.fieldError('name')"
          />
          <q-select
            v-model="form.resourceIds"
            class="col-12"
            :options="resourceOptions"
            emit-value
            map-options
            multiple
            use-chips
            label="Resources"
            outlined
            dense
            data-test="combination-members"
            :error="!!action.fieldError('members')"
            :error-message="action.fieldError('members')"
          />
          <div v-if="form.resourceIds.length" class="col-12">
            <div class="text-caption text-grey-7 q-mb-xs">
              Optional members are added only when free; required members must all be free.
            </div>
            <div class="row q-gutter-sm">
              <q-checkbox
                v-for="resourceId in form.resourceIds"
                :key="resourceId"
                v-model="form.required[resourceId]"
                dense
                :label="`${resourceName(resourceId)} required`"
              />
            </div>
          </div>
          <q-input
            v-model.number="form.minCapacity"
            class="col-4"
            type="number"
            min="1"
            label="Min party"
            outlined
            dense
            :error="!!action.fieldError('minCapacity')"
            :error-message="action.fieldError('minCapacity')"
          />
          <q-input
            v-model.number="form.capacity"
            class="col-4"
            type="number"
            min="1"
            label="Capacity"
            :hint="suggestedCapacity ? `Members seat ${suggestedCapacity}` : ''"
            outlined
            dense
            :error="!!action.fieldError('capacity')"
            :error-message="action.fieldError('capacity')"
          />
          <q-input
            v-model.number="form.sortOrder"
            class="col-4"
            type="number"
            label="Sort order"
            outlined
            dense
          />
          <q-toggle v-model="form.isBookableOnline" class="col-6" label="Bookable online" />
          <q-toggle v-if="combination" v-model="form.isActive" class="col-6" label="Active" />
        </q-card-section>
        <q-card-actions align="right">
          <q-btn flat label="Cancel" @click="$emit('update:modelValue', false)" />
          <q-btn
            type="submit"
            color="primary"
            unelevated
            label="Save"
            :loading="action.loading.value"
          />
        </q-card-actions>
      </q-form>
    </q-card>
  </q-dialog>
</template>

<script setup>
import { computed, reactive, watch } from 'vue'
import { useApiAction } from '@/composables/useApiAction.js'
import { venueService } from '@/services/venueService.js'

defineOptions({ name: 'CombinationDialog' })

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  combination: { type: Object, default: null },
  venueId: { type: String, default: null },
  resources: { type: Array, default: () => [] },
})
const emit = defineEmits(['update:modelValue', 'saved'])

const form = reactive({
  name: '',
  resourceIds: [],
  required: {},
  minCapacity: 1,
  capacity: 2,
  sortOrder: 0,
  isBookableOnline: true,
  isActive: true,
})

const resourceOptions = computed(() =>
  props.resources
    .filter((r) => r.isActive || form.resourceIds.includes(r.id))
    .map((r) => ({ label: `${r.name} (${r.capacity})`, value: r.id })),
)
const resourceName = (resourceId) => props.resources.find((r) => r.id === resourceId)?.name ?? '?'
const suggestedCapacity = computed(() =>
  form.resourceIds.reduce(
    (sum, resourceId) => sum + (props.resources.find((r) => r.id === resourceId)?.capacity ?? 0),
    0,
  ),
)

watch(
  () => form.resourceIds,
  (ids) => {
    for (const resourceId of ids) {
      if (form.required[resourceId] === undefined) {
        form.required[resourceId] = true
      }
    }
  },
)

watch(
  () => props.modelValue,
  (open) => {
    if (!open) {
      return
    }
    const c = props.combination
    Object.assign(form, {
      name: c?.name ?? '',
      resourceIds: c?.members.map((m) => m.resourceId) ?? [],
      required: Object.fromEntries((c?.members ?? []).map((m) => [m.resourceId, m.isRequired])),
      minCapacity: c?.minCapacity ?? 1,
      capacity: c?.capacity ?? 2,
      sortOrder: c?.sortOrder ?? 0,
      isBookableOnline: c?.isBookableOnline ?? true,
      isActive: c?.isActive ?? true,
    })
    action.reset()
  },
)

const action = useApiAction((payload) =>
  props.combination
    ? venueService.updateCombination(props.combination.id, payload)
    : venueService.createCombination({ ...payload, venueId: props.venueId }),
)

const generalError = computed(() =>
  action.error.value && !Object.keys(action.fieldErrors.value).length
    ? action.error.value.message
    : null,
)

async function save() {
  const payload = {
    name: form.name,
    minCapacity: form.minCapacity,
    capacity: form.capacity,
    sortOrder: form.sortOrder ?? 0,
    isBookableOnline: form.isBookableOnline,
    members: form.resourceIds.map((resourceId) => ({
      resourceId,
      isRequired: form.required[resourceId] !== false,
    })),
  }
  if (props.combination) {
    payload.isActive = form.isActive
  }
  const saved = await action.run(payload)
  if (saved) {
    emit('saved', saved)
  }
}
</script>
