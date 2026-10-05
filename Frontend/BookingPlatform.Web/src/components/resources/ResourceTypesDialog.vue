<template>
  <q-dialog :model-value="modelValue" @update:model-value="$emit('update:modelValue', $event)">
    <q-card style="width: 480px; max-width: 95vw">
      <q-card-section class="text-h6">Resource types</q-card-section>
      <q-card-section>
        <q-list bordered separator>
          <q-item v-for="type in types" :key="type.id">
            <q-item-section>
              <q-item-label>{{ type.name }}</q-item-label>
              <q-item-label caption>{{ type.code }}</q-item-label>
            </q-item-section>
            <q-item-section side>
              <q-toggle
                :model-value="type.isActive"
                :label="type.isActive ? 'Active' : 'Inactive'"
                left-label
                dense
                @update:model-value="(value) => toggle(type, value)"
              />
            </q-item-section>
          </q-item>
          <q-item v-if="types.length === 0">
            <q-item-section class="text-grey-7">No types yet.</q-item-section>
          </q-item>
        </q-list>

        <q-form class="row q-col-gutter-sm q-mt-sm items-start" @submit.prevent="create">
          <q-input
            v-model.trim="form.code"
            class="col-5"
            dense
            outlined
            label="Code"
            hint="e.g. TABLE"
            :error="!!action.fieldError('code') || action.error.value?.code === 'CODE_TAKEN'"
            :error-message="action.fieldError('code') ?? action.error.value?.message"
            @update:model-value="
              form.code = (form.code ?? '').toUpperCase().replace(/[^A-Z0-9_]/g, '_')
            "
          />
          <q-input
            v-model.trim="form.name"
            class="col-5"
            dense
            outlined
            label="Name"
            :error="!!action.fieldError('name')"
            :error-message="action.fieldError('name')"
          />
          <div class="col-2">
            <q-btn
              type="submit"
              color="primary"
              unelevated
              icon="add"
              aria-label="Add type"
              :loading="action.loading.value"
            />
          </div>
        </q-form>
      </q-card-section>
      <q-card-actions align="right">
        <q-btn v-close-popup flat label="Close" />
      </q-card-actions>
    </q-card>
  </q-dialog>
</template>

<script setup>
import { reactive } from 'vue'
import { useQuasar } from 'quasar'
import { useApiAction } from '@/composables/useApiAction.js'
import { venueService } from '@/services/venueService.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'ResourceTypesDialog' })

defineProps({
  modelValue: { type: Boolean, default: false },
  types: { type: Array, default: () => [] },
})
const emit = defineEmits(['update:modelValue', 'changed'])

const $q = useQuasar()
const form = reactive({ code: '', name: '' })
const action = useApiAction((payload) => venueService.createResourceType(payload))

async function create() {
  if (await action.run({ ...form })) {
    Object.assign(form, { code: '', name: '' })
    emit('changed')
  }
}

async function toggle(type, isActive) {
  try {
    await venueService.updateResourceType(type.id, { name: type.name, isActive })
    emit('changed')
  } catch (e) {
    $q.notify?.({ type: 'negative', message: normaliseError(e).message })
  }
}
</script>
