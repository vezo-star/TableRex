<template>
  <q-dialog
    :model-value="modelValue"
    persistent
    @update:model-value="$emit('update:modelValue', $event)"
  >
    <q-card style="width: 560px; max-width: 95vw">
      <q-form data-test="resource-form" @submit.prevent="save">
        <q-card-section class="text-h6">{{
          resource ? 'Edit resource' : 'Add resource'
        }}</q-card-section>
        <q-card-section class="row q-col-gutter-sm">
          <div
            v-if="action.error.value && !Object.keys(action.fieldErrors.value).length"
            class="col-12"
          >
            <q-banner dense rounded class="bg-red-1 text-negative">{{
              action.error.value.message
            }}</q-banner>
          </div>
          <q-input
            v-model.trim="form.name"
            class="col-12 col-sm-8"
            label="Name"
            outlined
            dense
            :error="!!action.fieldError('name')"
            :error-message="action.fieldError('name')"
          />
          <q-input
            v-model.trim="form.code"
            class="col-12 col-sm-4"
            label="Code"
            hint="Unique in the venue, e.g. T12"
            outlined
            dense
            :error="!!action.fieldError('code')"
            :error-message="action.fieldError('code')"
          />
          <q-select
            v-model="form.resourceTypeId"
            class="col-12 col-sm-6"
            :options="typeOptions"
            emit-value
            map-options
            label="Type"
            outlined
            dense
            :error="!!action.fieldError('resourceTypeId')"
            :error-message="action.fieldError('resourceTypeId')"
          />
          <q-select
            v-model="form.sectionId"
            class="col-12 col-sm-6"
            :options="sectionOptions"
            emit-value
            map-options
            clearable
            label="Section"
            outlined
            dense
            :error="!!action.fieldError('sectionId')"
            :error-message="action.fieldError('sectionId')"
          />
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
          <q-toggle
            v-model="form.isBookableOnline"
            class="col-12 col-sm-6"
            label="Bookable online"
          />
          <q-toggle
            v-if="resource"
            v-model="form.isActive"
            class="col-12 col-sm-6"
            label="Active"
          />

          <div class="col-12 text-subtitle2 q-mt-sm">Attributes</div>
          <div
            v-for="(attr, index) in form.attributes"
            :key="index"
            class="col-12 row q-col-gutter-sm items-center"
          >
            <q-input v-model.trim="attr.key" class="col-5" dense outlined placeholder="e.g. view" />
            <q-input
              v-model.trim="attr.value"
              class="col-6"
              dense
              outlined
              placeholder="e.g. window"
            />
            <div class="col-1">
              <q-btn
                flat
                dense
                round
                icon="close"
                aria-label="Remove attribute"
                @click="form.attributes.splice(index, 1)"
              />
            </div>
          </div>
          <div class="col-12">
            <q-btn
              flat
              dense
              icon="add"
              label="Add attribute"
              :disable="form.attributes.length >= 20"
              @click="form.attributes.push({ key: '', value: '' })"
            />
            <div v-if="action.fieldError('attributes')" class="text-negative text-caption">
              {{ action.fieldError('attributes') }}
            </div>
          </div>
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

defineOptions({ name: 'ResourceDialog' })

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  resource: { type: Object, default: null },
  venueId: { type: String, default: null },
  sections: { type: Array, default: () => [] },
  types: { type: Array, default: () => [] },
})
const emit = defineEmits(['update:modelValue', 'saved'])

const form = reactive({
  name: '',
  code: '',
  resourceTypeId: null,
  sectionId: null,
  minCapacity: 1,
  capacity: 2,
  sortOrder: 0,
  isBookableOnline: true,
  isActive: true,
  attributes: [],
})

const typeOptions = computed(() =>
  props.types
    .filter((t) => t.isActive || t.id === props.resource?.resourceTypeId)
    .map((t) => ({ label: t.name, value: t.id })),
)
const sectionOptions = computed(() =>
  props.sections.map((s) => ({ label: s.isActive ? s.name : `${s.name} (inactive)`, value: s.id })),
)

watch(
  () => props.modelValue,
  (open) => {
    if (!open) {
      return
    }
    const r = props.resource
    Object.assign(form, {
      name: r?.name ?? '',
      code: r?.code ?? '',
      resourceTypeId: r?.resourceTypeId ?? typeOptions.value[0]?.value ?? null,
      sectionId: r?.sectionId ?? null,
      minCapacity: r?.minCapacity ?? 1,
      capacity: r?.capacity ?? 2,
      sortOrder: r?.sortOrder ?? 0,
      isBookableOnline: r?.isBookableOnline ?? true,
      isActive: r?.isActive ?? true,
      attributes: Object.entries(r?.attributes ?? {}).map(([key, value]) => ({ key, value })),
    })
    action.reset()
  },
)

const action = useApiAction((payload) =>
  props.resource
    ? venueService.updateResource(props.resource.id, payload)
    : venueService.createResource({ ...payload, venueId: props.venueId }),
)

async function save() {
  const attributes = Object.fromEntries(
    form.attributes.filter((a) => a.key).map((a) => [a.key, a.value ?? '']),
  )
  const payload = {
    name: form.name,
    code: form.code,
    resourceTypeId: form.resourceTypeId,
    sectionId: form.sectionId,
    minCapacity: form.minCapacity,
    capacity: form.capacity,
    sortOrder: form.sortOrder ?? 0,
    isBookableOnline: form.isBookableOnline,
    attributes,
  }
  if (props.resource) {
    payload.isActive = form.isActive
  }
  const saved = await action.run(payload)
  if (saved) {
    emit('saved', saved)
  }
}
</script>
