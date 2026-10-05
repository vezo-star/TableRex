<template>
  <header class="brand-header q-mb-md" :style="accentStyle" data-test="brand-header">
    <img
      v-if="logo && !logoFailed"
      :src="logo"
      :alt="`${businessName} logo`"
      class="brand-logo q-mb-sm"
      referrerpolicy="no-referrer"
      loading="lazy"
      data-test="brand-logo"
      @error="logoFailed = true"
    />
    <slot />
    <p
      v-if="showIntro && branding?.bookingPageIntro"
      class="text-body2 q-mt-sm q-mb-none brand-intro"
      data-test="brand-intro"
    >
      {{ branding.bookingPageIntro }}
    </p>
  </header>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { brandAccent, safeLogoUrl } from '@/utils/branding.js'

defineOptions({ name: 'BrandHeader' })

const props = defineProps({
  branding: { type: Object, default: null },
  businessName: { type: String, required: true },
  showIntro: { type: Boolean, default: true },
})

const logoFailed = ref(false)
const logo = computed(() => safeLogoUrl(props.branding?.logoUrl))
const accentStyle = computed(() => {
  const accent = brandAccent(props.branding)
  return accent ? { borderTop: `4px solid ${accent}`, paddingTop: '12px' } : {}
})

watch(logo, () => {
  logoFailed.value = false
})
</script>

<style scoped>
.brand-logo {
  display: block;
  max-height: 64px;
  max-width: 220px;
  object-fit: contain;
}
.brand-intro {
  white-space: pre-line;
}
</style>
