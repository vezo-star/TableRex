<template>
  <q-card flat bordered class="relative-position" data-test="branding-card">
    <q-card-section>
      <div class="text-subtitle1">Booking page branding</div>
      <div class="text-caption text-grey-7">
        Shown on your public booking pages.
        <router-link
          v-if="tenantSlug"
          :to="{ name: RouteNames.PUBLIC_TENANT, params: { tenantSlug } }"
          target="_blank"
          rel="noopener"
          data-test="public-page-link"
        >
          Open your booking page
        </router-link>
      </div>
    </q-card-section>

    <q-inner-loading :showing="loading" />

    <q-card-section v-if="loadError" class="q-pt-none">
      <ErrorState
        :message="loadError.message"
        :trace-id="loadError.traceId"
        retryable
        @retry="load"
      />
    </q-card-section>

    <q-form
      v-else-if="branding"
      class="q-px-md q-pb-md q-gutter-y-sm"
      data-test="branding-form"
      @submit.prevent="save"
    >
      <q-banner
        v-if="!branding.enabled"
        dense
        rounded
        class="bg-amber-1 text-dark"
        data-test="branding-upgrade"
      >
        Custom branding is included in the Professional and Enterprise plans.
      </q-banner>
      <q-banner
        v-if="action.error.value && !Object.keys(action.fieldErrors.value).length"
        dense
        rounded
        class="bg-red-1 text-negative"
      >
        {{ action.error.value.message }}
      </q-banner>

      <q-input
        v-model.trim="form.primaryColor"
        label="Brand colour"
        placeholder="#1a73e8"
        outlined
        dense
        maxlength="7"
        :disable="!branding.enabled"
        :error="!!action.fieldError('primaryColor')"
        :error-message="action.fieldError('primaryColor')"
        :hint="colorHint"
        data-test="brand-color"
      >
        <template #append>
          <input
            type="color"
            class="color-swatch"
            aria-label="Pick a brand colour"
            :value="safeBrandColor(form.primaryColor) ?? '#12232a'"
            :disabled="!branding.enabled"
            @input="form.primaryColor = $event.target.value"
          />
        </template>
      </q-input>
      <q-input
        v-model.trim="form.logoUrl"
        label="Logo URL"
        placeholder="https://…/logo.png"
        outlined
        dense
        maxlength="500"
        hint="An https:// image address, shown at the top of your booking pages."
        :disable="!branding.enabled"
        :error="!!action.fieldError('logoUrl')"
        :error-message="action.fieldError('logoUrl')"
        data-test="brand-logo-url"
      />
      <q-input
        v-model="form.bookingPageIntro"
        label="Welcome message"
        type="textarea"
        autogrow
        outlined
        dense
        maxlength="1000"
        counter
        :disable="!branding.enabled"
        :error="!!action.fieldError('bookingPageIntro')"
        :error-message="action.fieldError('bookingPageIntro')"
        data-test="brand-intro"
      />
      <q-input
        v-model="form.bookingTerms"
        label="Booking terms"
        type="textarea"
        autogrow
        outlined
        dense
        maxlength="2000"
        counter
        hint="Shown next to the booking button, for example your deposit or late-arrival policy."
        :disable="!branding.enabled"
        :error="!!action.fieldError('bookingTerms')"
        :error-message="action.fieldError('bookingTerms')"
        data-test="brand-terms"
      />
      <div>
        <q-btn
          type="submit"
          color="primary"
          unelevated
          label="Save branding"
          :disable="!branding.enabled"
          :loading="action.loading.value"
          data-test="brand-save"
        />
      </div>
    </q-form>
  </q-card>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { useQuasar } from 'quasar'
import ErrorState from '@/components/common/ErrorState.vue'
import { RouteNames } from '@/constants/navigation.js'
import { useApiAction } from '@/composables/useApiAction.js'
import { saasService } from '@/services/saasService.js'
import { contrastWithWhite, safeBrandColor } from '@/utils/branding.js'
import { normaliseError } from '@/utils/problemDetails.js'

defineOptions({ name: 'BrandingSettingsCard' })

defineProps({
  tenantSlug: { type: String, default: '' },
})

const $q = useQuasar()
const branding = ref(null)
const loading = ref(false)
const loadError = ref(null)
const form = reactive({ primaryColor: '', logoUrl: '', bookingPageIntro: '', bookingTerms: '' })
const action = useApiAction((payload) => saasService.updateBranding(payload))

const colorHint = computed(() => {
  const color = safeBrandColor(form.primaryColor)
  if (color && contrastWithWhite(color) < 3) {
    return 'This colour is too light for buttons with white text; it will be used as an accent only.'
  }
  return 'Used for buttons and accents on your booking pages.'
})

function fill(data) {
  branding.value = data
  Object.assign(form, {
    primaryColor: data.primaryColor ?? '',
    logoUrl: data.logoUrl ?? '',
    bookingPageIntro: data.bookingPageIntro ?? '',
    bookingTerms: data.bookingTerms ?? '',
  })
}

async function load() {
  loading.value = true
  loadError.value = null
  try {
    fill(await saasService.getBranding())
  } catch (e) {
    loadError.value = normaliseError(e)
  } finally {
    loading.value = false
  }
}

async function save() {
  const blank = (v) => (v && v.trim() ? v.trim() : null)
  const saved = await action.run({
    primaryColor: blank(form.primaryColor),
    logoUrl: blank(form.logoUrl),
    bookingPageIntro: blank(form.bookingPageIntro),
    bookingTerms: blank(form.bookingTerms),
  })
  if (saved) {
    fill(saved)
    $q.notify?.({ type: 'positive', message: 'Branding saved.' })
  }
}

onMounted(load)
</script>

<style scoped>
.color-swatch {
  width: 32px;
  height: 28px;
  border: none;
  padding: 0;
  background: none;
  cursor: pointer;
}
</style>
