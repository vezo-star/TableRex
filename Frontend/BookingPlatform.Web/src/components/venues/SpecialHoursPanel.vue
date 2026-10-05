<template>
  <div>
    <div class="row items-center q-mb-sm">
      <div class="text-body2 text-grey-8 col">
        Special days replace the weekly hours for that date: either closed all day or custom periods
        (add one row per period).
      </div>
      <q-btn
        v-if="canManage"
        color="primary"
        unelevated
        dense
        icon="event"
        label="Add special day"
        data-test="add-special"
        @click="openEdit(null)"
      />
    </div>

    <ErrorState v-if="loadError" :message="loadError.message" retryable @retry="load" />
    <q-list v-else bordered separator data-test="special-list">
      <q-item v-for="entry in entries" :key="entry.id">
        <q-item-section>
          <q-item-label>
            {{ formatDate(entry.date) }} {{ entry.date.slice(0, 4) }}
            <span v-if="entry.name" class="text-grey-7">· {{ entry.name }}</span>
          </q-item-label>
          <q-item-label caption>
            {{ entry.isClosed ? 'Closed all day' : describePeriod(entry) }}
          </q-item-label>
        </q-item-section>
        <q-item-section v-if="canManage" side>
          <div class="row q-gutter-xs">
            <q-btn flat dense icon="edit" aria-label="Edit special day" @click="openEdit(entry)" />
            <q-btn
              flat
              dense
              icon="delete"
              color="negative"
              aria-label="Delete special day"
              @click="remove(entry)"
            />
          </div>
        </q-item-section>
      </q-item>
      <q-item v-if="!loading && entries.length === 0">
        <q-item-section class="text-grey-7">No upcoming special days.</q-item-section>
      </q-item>
    </q-list>

    <q-dialog v-model="editOpen" persistent>
      <q-card style="min-width: 360px">
        <q-form @submit.prevent="save">
          <q-card-section class="text-h6">{{
            editing ? 'Edit special day' : 'Add special day'
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
              v-model="form.date"
              type="date"
              label="Date"
              stack-label
              outlined
              dense
              :error="!!action.fieldError('date')"
              :error-message="action.fieldError('date')"
            />
            <q-input v-model.trim="form.name" label="Label (optional)" outlined dense />
            <q-toggle v-model="form.isClosed" label="Closed all day" />
            <div v-if="!form.isClosed" class="row q-gutter-sm">
              <q-input
                v-model="form.openTime"
                type="time"
                label="Opens"
                stack-label
                outlined
                dense
                class="col"
                :error="!!action.fieldError('openTime')"
                :error-message="action.fieldError('openTime')"
              />
              <q-input
                v-model="form.closeTime"
                type="time"
                label="Closes"
                stack-label
                outlined
                dense
                class="col"
                :error="!!action.fieldError('closeTime')"
                :error-message="action.fieldError('closeTime')"
              />
            </div>
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
import { describePeriod } from '@/utils/operatingHours.js'
import { formatDate, todayIso } from '@/utils/dates.js'

defineOptions({ name: 'SpecialHoursPanel' })

const props = defineProps({
  venue: { type: Object, required: true },
  canManage: { type: Boolean, default: false },
})

const $q = useQuasar()
const entries = ref([])
const loading = ref(false)
const loadError = ref(null)

async function load() {
  loading.value = true
  loadError.value = null
  try {
    entries.value = await venueService.listSpecialHours(props.venue.id, {
      from: todayIso(props.venue.timeZone),
    })
  } catch (e) {
    loadError.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

const editOpen = ref(false)
const editing = ref(null)
const form = reactive({ date: '', name: '', isClosed: true, openTime: '', closeTime: '' })
const action = useApiAction((payload) =>
  editing.value
    ? venueService.updateSpecialHours(props.venue.id, editing.value.id, payload)
    : venueService.createSpecialHours(props.venue.id, payload),
)

function openEdit(entry) {
  editing.value = entry
  Object.assign(form, {
    date: entry?.date ?? todayIso(props.venue.timeZone),
    name: entry?.name ?? '',
    isClosed: entry?.isClosed ?? true,
    openTime: entry?.openTime ?? '10:00',
    closeTime: entry?.closeTime ?? '16:00',
  })
  action.reset()
  editOpen.value = true
}

async function save() {
  const payload = {
    date: form.date,
    name: form.name || null,
    isClosed: form.isClosed,
    openTime: form.isClosed ? null : form.openTime,
    closeTime: form.isClosed ? null : form.closeTime,
  }
  if (await action.run(payload)) {
    editOpen.value = false
    load()
  }
}

function remove(entry) {
  $q.dialog({
    title: 'Delete special day',
    message: `Remove the special hours for ${formatDate(entry.date)}?`,
    cancel: true,
    persistent: true,
    ok: { label: 'Delete', color: 'negative', unelevated: true },
  }).onOk(async () => {
    try {
      await venueService.deleteSpecialHours(props.venue.id, entry.id)
      load()
    } catch (e) {
      $q.notify?.({ type: 'negative', message: normaliseError(e).message })
    }
  })
}

onMounted(load)
</script>
