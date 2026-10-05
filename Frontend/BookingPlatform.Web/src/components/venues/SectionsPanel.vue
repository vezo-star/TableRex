<template>
  <div>
    <div class="row items-center q-mb-sm">
      <div class="text-subtitle1 col">
        Sections group resources, for example "Terrace" or "Bar".
      </div>
      <q-btn
        v-if="canManage"
        color="primary"
        unelevated
        dense
        icon="add"
        label="Add section"
        data-test="add-section"
        @click="openEdit(null)"
      />
    </div>

    <ErrorState v-if="loadError" :message="loadError.message" retryable @retry="load" />
    <q-list v-else bordered separator data-test="sections-list">
      <q-item v-for="section in sections" :key="section.id">
        <q-item-section>
          <q-item-label>{{ section.name }}</q-item-label>
          <q-item-label v-if="section.description" caption>{{ section.description }}</q-item-label>
        </q-item-section>
        <q-item-section side>
          <div class="row items-center q-gutter-xs">
            <q-badge v-if="!section.isActive" color="grey-8" label="Inactive" />
            <template v-if="canManage">
              <q-btn flat dense icon="edit" aria-label="Edit section" @click="openEdit(section)" />
              <q-btn
                flat
                dense
                icon="delete"
                color="negative"
                aria-label="Delete section"
                @click="remove(section)"
              />
            </template>
          </div>
        </q-item-section>
      </q-item>
      <q-item v-if="!loading && sections.length === 0">
        <q-item-section class="text-grey-7">No sections yet.</q-item-section>
      </q-item>
    </q-list>

    <q-dialog v-model="editOpen" persistent>
      <q-card style="min-width: 340px">
        <q-form @submit.prevent="save">
          <q-card-section class="text-h6">{{
            editing ? 'Edit section' : 'Add section'
          }}</q-card-section>
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
              label="Name"
              outlined
              dense
              autofocus
              :error="!!action.fieldError('name')"
              :error-message="action.fieldError('name')"
            />
            <q-input v-model="form.description" label="Description" outlined dense />
            <q-input
              v-model.number="form.sortOrder"
              type="number"
              label="Sort order"
              outlined
              dense
            />
            <q-toggle v-if="editing" v-model="form.isActive" label="Active" />
          </q-card-section>
          <q-card-actions align="right">
            <q-btn v-close-popup flat label="Cancel" />
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
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { useQuasar } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import { useApiAction } from '@/composables/useApiAction.js'
import { venueService } from '@/services/venueService.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'SectionsPanel' })

const props = defineProps({
  venueId: { type: String, required: true },
  canManage: { type: Boolean, default: false },
})

const $q = useQuasar()
const sections = ref([])
const loading = ref(false)
const loadError = ref(null)

async function load() {
  loading.value = true
  loadError.value = null
  try {
    sections.value = await venueService.listSections(props.venueId)
  } catch (e) {
    loadError.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

const editOpen = ref(false)
const editing = ref(null)
const form = reactive({ name: '', description: '', sortOrder: 0, isActive: true })
const action = useApiAction((payload) =>
  editing.value
    ? venueService.updateSection(props.venueId, editing.value.id, payload)
    : venueService.createSection(props.venueId, payload),
)

function openEdit(section) {
  editing.value = section
  Object.assign(form, {
    name: section?.name ?? '',
    description: section?.description ?? '',
    sortOrder: section?.sortOrder ?? sections.value.length,
    isActive: section?.isActive ?? true,
  })
  action.reset()
  editOpen.value = true
}

async function save() {
  const result = await action.run({ ...form, description: form.description || null })
  if (result) {
    editOpen.value = false
    load()
  }
}

function remove(section) {
  $q.dialog({
    title: 'Delete section',
    message: `Delete ${section.name}? Sections still used by resources are deactivated instead.`,
    cancel: true,
    persistent: true,
    ok: { label: 'Delete', color: 'negative', unelevated: true },
  }).onOk(async () => {
    try {
      const result = await venueService.deleteSection(props.venueId, section.id)
      $q.notify?.({
        type: 'positive',
        message: result.deleted ? 'Section deleted.' : 'Section is in use, so it was deactivated.',
      })
      load()
    } catch (e) {
      $q.notify?.({ type: 'negative', message: normaliseError(e).message })
    }
  })
}

onMounted(load)
</script>
