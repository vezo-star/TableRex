<template>
  <q-card flat bordered class="q-mt-lg" data-test="combinations-panel">
    <q-card-section class="row items-center">
      <div class="col">
        <div class="text-subtitle1">Combinations</div>
        <div class="text-caption text-grey-7">
          Resources booked together for larger parties, e.g. two tables pushed together.
        </div>
      </div>
      <q-btn
        v-if="canCreate"
        outline
        color="primary"
        icon="add"
        label="Add combination"
        :disable="!venueId || resources.length < 2"
        data-test="add-combination"
        @click="openEdit(null)"
      />
    </q-card-section>
    <q-separator />
    <q-banner v-if="error" dense class="bg-red-1 text-negative">{{ error.message }}</q-banner>
    <q-list v-else separator>
      <q-item v-for="combination in combinations" :key="combination.id">
        <q-item-section>
          <q-item-label>
            {{ combination.name }}
            <q-badge v-if="!combination.isActive" color="grey" label="Inactive" class="q-ml-xs" />
            <q-badge
              v-if="!combination.isBookableOnline"
              color="orange"
              label="Staff only"
              class="q-ml-xs"
            />
          </q-item-label>
          <q-item-label caption>
            {{ memberLabel(combination) }} · party {{ combination.minCapacity }}–{{
              combination.capacity
            }}
          </q-item-label>
        </q-item-section>
        <q-item-section side>
          <div class="row no-wrap">
            <q-btn
              v-if="canUpdate"
              flat
              dense
              icon="edit"
              aria-label="Edit combination"
              @click="openEdit(combination)"
            />
            <q-btn
              v-if="canDelete"
              flat
              dense
              icon="delete"
              color="negative"
              aria-label="Delete combination"
              @click="remove(combination)"
            />
          </div>
        </q-item-section>
      </q-item>
      <q-item v-if="!loading && combinations.length === 0">
        <q-item-section class="text-grey-7">No combinations yet.</q-item-section>
      </q-item>
    </q-list>

    <CombinationDialog
      v-model="editOpen"
      :combination="editing"
      :venue-id="venueId"
      :resources="resources"
      @saved="onSaved"
    />
  </q-card>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { useQuasar } from 'quasar'
import CombinationDialog from '@/components/resources/CombinationDialog.vue'
import { venueService } from '@/services/venueService.js'
import { useAuthStore } from '@/stores/auth.js'
import { Permissions } from '@/constants/permissions.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'CombinationsPanel' })

const props = defineProps({
  venueId: { type: String, default: null },
  resources: { type: Array, default: () => [] },
})

const auth = useAuthStore()
const $q = useQuasar()
const canCreate = computed(() => auth.hasPermission(Permissions.RESOURCE_CREATE))
const canUpdate = computed(() => auth.hasPermission(Permissions.RESOURCE_UPDATE))
const canDelete = computed(() => auth.hasPermission(Permissions.RESOURCE_DELETE))

const combinations = ref([])
const loading = ref(false)
const error = ref(null)
const editOpen = ref(false)
const editing = ref(null)

function memberLabel(combination) {
  return combination.members
    .map((m) => {
      const name = props.resources.find((r) => r.id === m.resourceId)?.name ?? '?'
      return m.isRequired ? name : `${name} (optional)`
    })
    .join(' + ')
}

async function load() {
  if (!props.venueId) {
    combinations.value = []
    return
  }
  loading.value = true
  error.value = null
  try {
    combinations.value = await venueService.listCombinations(props.venueId)
  } catch (e) {
    error.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

function openEdit(combination) {
  editing.value = combination
  editOpen.value = true
}

function onSaved() {
  editOpen.value = false
  load()
}

function remove(combination) {
  $q.dialog({
    title: 'Delete combination',
    message: `Delete ${combination.name}? Combinations with booking history are deactivated instead.`,
    cancel: true,
    persistent: true,
    ok: { label: 'Delete', color: 'negative', unelevated: true },
  }).onOk(async () => {
    try {
      const result = await venueService.deleteCombination(combination.id)
      $q.notify?.({
        type: 'positive',
        message: result.deleted
          ? 'Combination deleted.'
          : 'Combination is in use, so it was deactivated.',
      })
      load()
    } catch (e) {
      $q.notify?.({ type: 'negative', message: normaliseError(e).message })
    }
  })
}

watch(() => props.venueId, load, { immediate: true })
</script>
