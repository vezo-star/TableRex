<template>
  <q-select
    :model-value="modelValue"
    :options="options"
    :option-label="optionLabel"
    option-value="id"
    use-input
    clearable
    input-debounce="300"
    label="Search customers"
    hint="Name, email or phone"
    outlined
    dense
    :loading="loading"
    data-test="customer-picker"
    @filter="onFilter"
    @update:model-value="$emit('update:modelValue', $event)"
  >
    <template #option="scope">
      <q-item v-bind="scope.itemProps">
        <q-item-section>
          <q-item-label>{{ fullName(scope.opt) }}</q-item-label>
          <q-item-label caption>{{ contact(scope.opt) }}</q-item-label>
        </q-item-section>
        <q-item-section side>
          <q-item-label caption>{{ scope.opt.bookingCount }} bookings</q-item-label>
        </q-item-section>
      </q-item>
    </template>
    <template #no-option>
      <q-item>
        <q-item-section class="text-grey-7">
          {{ searched ? 'No matching customers' : 'Type to search' }}
        </q-item-section>
      </q-item>
    </template>
  </q-select>
</template>

<script setup>
import { ref } from 'vue'
import { customerService } from '@/services/customerService.js'

defineOptions({ name: 'CustomerPicker' })

defineProps({
  modelValue: { type: Object, default: null },
})
defineEmits(['update:modelValue'])

const options = ref([])
const loading = ref(false)
const searched = ref(false)

function fullName(c) {
  return [c.firstName, c.lastName].filter(Boolean).join(' ')
}

function contact(c) {
  return [c.phone, c.email].filter(Boolean).join(' · ') || 'No contact details'
}

function optionLabel(c) {
  return c ? `${fullName(c)}${c.phone || c.email ? ` (${c.phone || c.email})` : ''}` : ''
}

async function onFilter(value, update, abort) {
  const q = value.trim()
  if (q.length < 2) {
    update(() => {
      options.value = []
      searched.value = false
    })
    return
  }
  loading.value = true
  try {
    const page = await customerService.list({ q, pageSize: 20 })
    update(() => {
      options.value = page.items.filter((c) => !c.isAnonymized)
      searched.value = true
    })
  } catch {
    abort()
  } finally {
    loading.value = false
  }
}
</script>
