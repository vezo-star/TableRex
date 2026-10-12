<template>
  <span
    class="tr-logo"
    :class="{ 'tr-logo--on-dark': onDark }"
    :style="{ '--tr-logo-size': `${size}px` }"
    data-test="tablerex-logo"
  >
    <span class="tr-logo__mark">
      <svg viewBox="0 0 100 100" aria-hidden="true" focusable="false">
        <g transform="translate(4,2) scale(0.92)">
          <rect x="26" y="28" width="46" height="52" rx="20" fill="#E06C4F" />
          <rect x="50" y="34" width="40" height="28" rx="11" fill="#E06C4F" />
          <path d="M58,56 L88,56" stroke="#12232A" stroke-width="2.4" stroke-linecap="round" />
          <path
            d="M61,56 l2.6,5 l2.6,-5 z M67.5,56 l2.6,5 l2.6,-5 z M74,56 l2.6,5 l2.6,-5 z M80.5,56 l2.6,5 l2.6,-5 z"
            fill="#F7F1E8"
          />
          <circle cx="58" cy="43" r="5.5" fill="#F7F1E8" />
          <circle cx="59.5" cy="43" r="2.6" fill="#12232A" />
          <circle cx="85" cy="41" r="1.8" fill="#12232A" />
          <rect x="8" y="70" width="84" height="9" rx="4.5" fill="#F7F1E8" />
          <rect
            x="38"
            y="61"
            width="16"
            height="9"
            rx="4.5"
            fill="#E06C4F"
            stroke="#12232A"
            stroke-width="1.8"
          />
          <circle cx="51" cy="72.5" r="2.4" fill="#E06C4F" />
          <circle cx="45.5" cy="72.5" r="2.4" fill="#E06C4F" />
          <rect x="20" y="78" width="6" height="14" rx="2" fill="#F7F1E8" />
          <rect x="74" y="78" width="6" height="14" rx="2" fill="#F7F1E8" />
        </g>
      </svg>
    </span>
    <span v-if="showWordmark || showTagline" class="tr-logo__text">
      <span v-if="showWordmark" class="tr-logo__wordmark">Table<b>Rex</b></span>
      <span v-if="showTagline" class="tr-logo__tagline">Every seat. Booked right.</span>
    </span>
  </span>
</template>

<script setup>
defineOptions({ name: 'TableRexLogo' })

defineProps({
  size: { type: Number, default: 36 },
  showWordmark: { type: Boolean, default: true },
  showTagline: { type: Boolean, default: false },
  onDark: { type: Boolean, default: false },
})
</script>

<style scoped>
.tr-logo {
  display: inline-flex;
  align-items: center;
  gap: calc(var(--tr-logo-size) * 0.28);
  color: var(--tr-dark);
  text-decoration: none;
}
.tr-logo__mark {
  flex: none;
  display: grid;
  place-items: center;
  width: var(--tr-logo-size);
  height: var(--tr-logo-size);
  border-radius: calc(var(--tr-logo-size) * 0.28);
  background: var(--tr-dark);
}
.tr-logo--on-dark .tr-logo__mark {
  background: var(--tr-dark-2);
}
.tr-logo__mark svg {
  width: 86%;
  height: 86%;
}
.tr-logo__text {
  display: inline-flex;
  flex-direction: column;
  line-height: 1;
}
.tr-logo__wordmark {
  font-family: var(--tr-serif);
  font-size: calc(var(--tr-logo-size) * 0.62);
  font-weight: 400;
  letter-spacing: -0.01em;
}
.tr-logo__wordmark b {
  font-weight: 700;
  color: var(--tr-coral-ink);
}
.tr-logo__tagline {
  margin-top: 6px;
  font-family: var(--tr-sans);
  font-size: 11px;
  font-weight: 500;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--tr-muted);
}
.tr-logo--on-dark {
  color: var(--tr-cream);
}
.tr-logo--on-dark .tr-logo__wordmark b {
  color: var(--tr-coral);
}
.tr-logo--on-dark .tr-logo__tagline {
  color: var(--tr-light);
}
</style>
