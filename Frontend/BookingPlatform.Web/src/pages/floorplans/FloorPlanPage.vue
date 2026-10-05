<template>
  <q-page padding>
    <div class="row items-center q-mb-md q-gutter-sm">
      <div class="text-h5 col">
        Floor plans
        <span v-if="venues.currentVenue" class="text-subtitle1 text-grey-7">
          · {{ venues.currentVenue.name }}
        </span>
      </div>
      <template v-if="canManage && venueId">
        <q-btn
          outline
          color="primary"
          icon="add"
          label="New plan"
          data-test="add-plan"
          @click="openPlanDialog(null)"
        />
      </template>
    </div>

    <NoVenueState v-if="!venueId && !venues.loading" />
    <ErrorState
      v-else-if="loadError"
      :message="loadError.message"
      :trace-id="loadError.traceId"
      retryable
      @retry="load"
    />
    <div v-else-if="loading" class="flex flex-center q-pa-xl"><q-spinner size="lg" /></div>
    <div
      v-else-if="plans.length === 0"
      class="column items-center q-pa-xl text-grey-7"
      data-test="no-plans"
    >
      <q-icon name="grid_view" size="48px" class="q-mb-sm" />
      <div>No floor plans for this venue yet.</div>
    </div>

    <template v-else>
      <q-tabs
        :model-value="planId"
        dense
        align="left"
        active-color="primary"
        indicator-color="primary"
        class="q-mb-sm"
        @update:model-value="switchPlan"
      >
        <q-tab v-for="plan in plans" :key="plan.id" :name="plan.id" :label="plan.name" no-caps />
      </q-tabs>

      <div v-if="currentPlan" class="row q-col-gutter-md">
        <div class="col-12 col-md-9">
          <q-card flat bordered class="q-pa-sm">
            <div class="row items-center q-gutter-sm q-mb-sm">
              <div class="text-caption text-grey-7 col">
                {{ currentPlan.width }} × {{ currentPlan.height }}
                <template v-if="canManage"
                  >· drag to move, drag the corner to resize, arrow keys nudge</template
                >
              </div>
              <template v-if="canManage">
                <q-btn
                  flat
                  dense
                  icon="tune"
                  label="Plan settings"
                  @click="openPlanDialog(currentPlan)"
                />
                <q-btn flat dense icon="undo" label="Discard" :disable="!dirty" @click="discard" />
                <q-btn
                  color="primary"
                  unelevated
                  dense
                  icon="save"
                  label="Save layout"
                  :disable="!dirty"
                  :loading="saving"
                  data-test="save-layout"
                  @click="saveLayout"
                />
              </template>
            </div>
            <FloorPlanCanvas
              :plan="currentPlan"
              :positions="positions"
              :resources="resourceMap"
              :selected-id="selectedId"
              :editable="canManage"
              @select="selectedId = $event"
              @update="updatePosition"
            />
          </q-card>
        </div>

        <div class="col-12 col-md-3">
          <q-card v-if="selected" flat bordered class="q-pa-md q-mb-md" data-test="shape-inspector">
            <div class="text-subtitle1">
              {{ resourceMap[selected.resourceId]?.name ?? 'Resource' }}
            </div>
            <div class="text-caption text-grey-7 q-mb-sm">
              Party {{ resourceMap[selected.resourceId]?.minCapacity }}–{{
                resourceMap[selected.resourceId]?.capacity
              }}
            </div>
            <template v-if="canManage">
              <div class="text-caption">Rotation: {{ selected.rotation }}°</div>
              <q-slider
                :model-value="selected.rotation"
                :min="0"
                :max="345"
                :step="15"
                markers
                @update:model-value="
                  (v) => updatePosition({ ...selected, rotation: normaliseRotation(v) })
                "
              />
              <q-btn-toggle
                :model-value="selected.shape"
                spread
                no-caps
                dense
                unelevated
                toggle-color="primary"
                :options="[
                  { label: 'Rectangle', value: 'RECT' },
                  { label: 'Round', value: 'ROUND' },
                ]"
                class="q-mb-sm"
                @update:model-value="(v) => updatePosition({ ...selected, shape: v })"
              />
              <q-btn
                flat
                dense
                color="negative"
                icon="remove_circle_outline"
                label="Remove from plan"
                @click="removeSelected"
              />
            </template>
          </q-card>

          <q-card v-if="canManage" flat bordered class="q-pa-sm">
            <div class="text-subtitle2 q-pa-xs">Not on this plan</div>
            <q-list dense data-test="unplaced-list">
              <q-item
                v-for="resource in unplaced"
                :key="resource.id"
                clickable
                @click="place(resource)"
              >
                <q-item-section>
                  <q-item-label>{{ resource.code }} · {{ resource.name }}</q-item-label>
                  <q-item-label caption
                    >{{ resource.minCapacity }}–{{ resource.capacity }} guests</q-item-label
                  >
                </q-item-section>
                <q-item-section side><q-icon name="add_location_alt" /></q-item-section>
              </q-item>
              <q-item v-if="unplaced.length === 0">
                <q-item-section class="text-grey-7 text-caption"
                  >Every resource is placed.</q-item-section
                >
              </q-item>
            </q-list>
          </q-card>
        </div>
      </div>
    </template>

    <q-dialog v-model="planDialogOpen" persistent>
      <q-card style="min-width: 340px">
        <q-form @submit.prevent="savePlan">
          <q-card-section class="text-h6">{{
            editingPlan ? 'Plan settings' : 'New floor plan'
          }}</q-card-section>
          <q-card-section class="q-gutter-sm">
            <q-banner
              v-if="planAction.error.value && !Object.keys(planAction.fieldErrors.value).length"
              dense
              rounded
              class="bg-red-1 text-negative"
            >
              {{ planAction.error.value.message }}
            </q-banner>
            <q-input
              v-model.trim="planForm.name"
              label="Name"
              outlined
              dense
              autofocus
              :error="!!planAction.fieldError('name')"
              :error-message="planAction.fieldError('name')"
            />
            <div class="row q-col-gutter-sm">
              <q-input
                v-model.number="planForm.width"
                class="col"
                type="number"
                label="Width"
                outlined
                dense
                :error="!!planAction.fieldError('width')"
                :error-message="planAction.fieldError('width')"
              />
              <q-input
                v-model.number="planForm.height"
                class="col"
                type="number"
                label="Height"
                outlined
                dense
                :error="!!planAction.fieldError('height')"
                :error-message="planAction.fieldError('height')"
              />
            </div>
          </q-card-section>
          <q-card-actions align="right">
            <q-btn
              v-if="editingPlan"
              flat
              color="negative"
              label="Delete plan"
              @click="deletePlan"
            />
            <q-space />
            <q-btn v-close-popup flat label="Cancel" />
            <q-btn
              type="submit"
              color="primary"
              unelevated
              label="Save"
              :loading="planAction.loading.value"
            />
          </q-card-actions>
        </q-form>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { useQuasar } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import NoVenueState from '@/components/venues/NoVenueState.vue'
import FloorPlanCanvas from '@/components/floorplans/FloorPlanCanvas.vue'
import { useApiAction } from '@/composables/useApiAction.js'
import { venueService } from '@/services/venueService.js'
import { useAuthStore } from '@/stores/auth.js'
import { useVenuesStore } from '@/stores/venues.js'
import { Permissions } from '@/constants/permissions.js'
import { normaliseError } from '@/utils/problemDetails.js'
import {
  defaultFootprint,
  findFreeSpot,
  GRID,
  moveWithin,
  normaliseRotation,
} from '@/utils/floorPlanGeometry.js'

defineOptions({ name: 'FloorPlanPage' })

const auth = useAuthStore()
const venues = useVenuesStore()
const $q = useQuasar()
const canManage = computed(() => auth.hasPermission(Permissions.FLOORPLAN_MANAGE))
const venueId = computed(() => venues.currentVenueId)

const plans = ref([])
const resources = ref([])
const planId = ref(null)
const positions = ref([])
const selectedId = ref(null)
const loading = ref(false)
const loadError = ref(null)
const saving = ref(false)
const savedSnapshot = ref('[]')

const currentPlan = computed(() => plans.value.find((p) => p.id === planId.value) ?? null)
const resourceMap = computed(() => Object.fromEntries(resources.value.map((r) => [r.id, r])))
const selected = computed(
  () => positions.value.find((p) => p.resourceId === selectedId.value) ?? null,
)
const unplaced = computed(() => {
  const placed = new Set(positions.value.map((p) => p.resourceId))
  return resources.value.filter((r) => r.isActive && !placed.has(r.id))
})
const dirty = computed(() => JSON.stringify(positions.value) !== savedSnapshot.value)

function adopt(plan) {
  positions.value = plan.positions.map((p) => ({
    resourceId: p.resourceId,
    x: Number(p.x),
    y: Number(p.y),
    width: Number(p.width),
    height: Number(p.height),
    rotation: Number(p.rotation),
    shape: p.shape,
  }))
  savedSnapshot.value = JSON.stringify(positions.value)
  selectedId.value = null
}

async function load() {
  if (!venueId.value) {
    plans.value = []
    return
  }
  loading.value = true
  loadError.value = null
  try {
    const [venuePlans, venueResources] = await Promise.all([
      venueService.listFloorPlans(venueId.value),
      venueService.listAllResources(venueId.value),
    ])
    plans.value = venuePlans
    resources.value = venueResources
    const keep = venuePlans.find((p) => p.id === planId.value) ?? venuePlans[0] ?? null
    planId.value = keep?.id ?? null
    if (keep) {
      adopt(keep)
    }
  } catch (e) {
    loadError.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

function confirmDiscard() {
  return new Promise((resolve) => {
    if (!dirty.value) {
      resolve(true)
      return
    }
    $q.dialog({
      title: 'Unsaved layout',
      message: 'Discard your unsaved layout changes?',
      cancel: true,
      persistent: true,
      ok: { label: 'Discard', color: 'negative', unelevated: true },
    })
      .onOk(() => resolve(true))
      .onCancel(() => resolve(false))
  })
}

async function switchPlan(id) {
  if (id === planId.value || !(await confirmDiscard())) {
    return
  }
  planId.value = id
  adopt(currentPlan.value)
}

function updatePosition(next) {
  const index = positions.value.findIndex((p) => p.resourceId === next.resourceId)
  if (index >= 0) {
    positions.value.splice(index, 1, next)
  }
}

function place(resource) {
  const footprint = defaultFootprint(resource.capacity)
  const spot = findFreeSpot(positions.value, footprint, currentPlan.value)
  positions.value.push({
    resourceId: resource.id,
    ...spot,
    width: Math.min(footprint.width, currentPlan.value.width),
    height: Math.min(footprint.height, currentPlan.value.height),
    rotation: 0,
    shape: footprint.shape,
  })
  selectedId.value = resource.id
}

function removeSelected() {
  positions.value = positions.value.filter((p) => p.resourceId !== selectedId.value)
  selectedId.value = null
}

function discard() {
  adopt(currentPlan.value)
}

async function saveLayout() {
  saving.value = true
  try {
    const saved = await venueService.replaceFloorPlanPositions(planId.value, positions.value)
    plans.value.splice(
      plans.value.findIndex((p) => p.id === saved.id),
      1,
      saved,
    )
    adopt(saved)
    $q.notify?.({ type: 'positive', message: 'Layout saved.' })
  } catch (e) {
    const apiError = normaliseError(e)
    $q.notify?.({ type: 'negative', message: apiError.errors?.positions?.[0] ?? apiError.message })
  } finally {
    saving.value = false
  }
}

function onKeydown(event) {
  if (!canManage.value || !selected.value || event.target?.closest?.('input, textarea')) {
    return
  }
  const step = event.shiftKey ? GRID * 5 : GRID
  const moves = {
    ArrowLeft: [-step, 0],
    ArrowRight: [step, 0],
    ArrowUp: [0, -step],
    ArrowDown: [0, step],
  }
  if (moves[event.key]) {
    event.preventDefault()
    updatePosition(moveWithin(selected.value, ...moves[event.key], currentPlan.value))
  } else if (event.key === 'Delete' || event.key === 'Backspace') {
    event.preventDefault()
    removeSelected()
  }
}

const planDialogOpen = ref(false)
const editingPlan = ref(null)
const planForm = reactive({ name: '', width: 1200, height: 800 })
const planAction = useApiAction((payload) =>
  editingPlan.value
    ? venueService.updateFloorPlan(editingPlan.value.id, {
        ...payload,
        sortOrder: editingPlan.value.sortOrder,
        isActive: editingPlan.value.isActive,
      })
    : venueService.createFloorPlan({ ...payload, venueId: venueId.value }),
)

function openPlanDialog(plan) {
  editingPlan.value = plan
  Object.assign(planForm, {
    name: plan?.name ?? '',
    width: plan?.width ?? 1200,
    height: plan?.height ?? 800,
  })
  planAction.reset()
  planDialogOpen.value = true
}

async function savePlan() {
  const saved = await planAction.run({ ...planForm })
  if (saved) {
    planDialogOpen.value = false
    planId.value = saved.id
    await load()
  }
}

function deletePlan() {
  $q.dialog({
    title: 'Delete floor plan',
    message: `Delete ${editingPlan.value.name}? Resources are not deleted.`,
    cancel: true,
    persistent: true,
    ok: { label: 'Delete', color: 'negative', unelevated: true },
  }).onOk(async () => {
    try {
      await venueService.deleteFloorPlan(editingPlan.value.id)
      planDialogOpen.value = false
      planId.value = null
      await load()
    } catch (e) {
      $q.notify?.({ type: 'negative', message: normaliseError(e).message })
    }
  })
}

watch(venueId, load, { immediate: true })
onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
onBeforeRouteLeave(() => confirmDiscard())
</script>
