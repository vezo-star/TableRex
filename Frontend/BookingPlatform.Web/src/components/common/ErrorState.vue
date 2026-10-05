<template>
  <q-banner rounded class="bg-red-1 text-red-10" data-test="error-state">
    <template #avatar>
      <q-icon name="error_outline" color="negative" />
    </template>
    <div>{{ message }}</div>
    <div v-if="traceId" class="text-caption q-mt-xs" data-test="error-trace-id">
      Reference: {{ traceId }}
    </div>
    <template v-if="retryable" #action>
      <q-btn
        flat
        color="negative"
        label="Try again"
        data-test="error-retry"
        @click="emit('retry')"
      />
    </template>
  </q-banner>
</template>

<script setup>
defineOptions({ name: 'ErrorState' })

defineProps({
  message: { type: String, required: true },
  traceId: { type: String, default: null },
  retryable: { type: Boolean, default: false },
})

const emit = defineEmits(['retry'])
</script>
