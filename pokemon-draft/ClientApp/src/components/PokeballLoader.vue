<script setup lang="ts">
import { computed } from 'vue'
import { mdiPokeball } from '@mdi/js'

const props = defineProps<{
  size?: number
  label?: string
  variant?: 'inline' | 'page'
}>()

const variant = computed(() => props.variant ?? 'inline')
const svgSize = computed(() => props.size ?? (variant.value === 'page' ? 48 : 18))
</script>

<template>
  <!-- Inline: single-colour spinning pokeball for use inside buttons -->
  <svg
    v-if="variant === 'inline'"
    class="pokeball-inline"
    :width="svgSize"
    :height="svgSize"
    viewBox="0 0 24 24"
    fill="currentColor"
    aria-hidden="true"
  >
    <path :d="mdiPokeball" />
  </svg>

  <!-- Page: full-colour bouncing + spinning pokeball for loading screens -->
  <div v-else class="pokeball-page">
    <div class="pokeball-bounce">
      <svg
        class="pokeball-spin"
        :width="svgSize"
        :height="svgSize"
        viewBox="0 0 24 24"
        aria-hidden="true"
        role="status"
      >
        <!-- white base -->
        <circle cx="12" cy="12" r="10" fill="#efefef" />
        <!-- red top half -->
        <path d="M 2 12 A 10 10 0 0 1 22 12 Z" fill="#cc0000" />
        <!-- black divider bar -->
        <rect x="2" y="11.1" width="20" height="1.8" fill="#111118" />
        <!-- centre button -->
        <circle cx="12" cy="12" r="3" fill="#efefef" stroke="#111118" stroke-width="1.5" />
        <!-- outer ring -->
        <circle cx="12" cy="12" r="10" fill="none" stroke="#111118" stroke-width="1.5" />
      </svg>
    </div>
    <p v-if="label" class="pokeball-label">{{ label }}</p>
  </div>
</template>

<style scoped>
/* ── Inline (button) ──────────────────────────────────────────────────────── */
.pokeball-inline {
  display: inline-block;
  vertical-align: middle;
  flex-shrink: 0;
  animation: pokeball-spin-anim 0.7s linear infinite;
}

/* ── Page (loading screen) ───────────────────────────────────────────────── */
.pokeball-page {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
}

.pokeball-bounce {
  animation: pokeball-bounce-anim 0.55s ease-in-out infinite alternate;
}

.pokeball-spin {
  display: block;
  animation: pokeball-spin-anim 0.55s linear infinite;
  filter: drop-shadow(0 4px 12px rgba(204, 0, 0, 0.35));
}

.pokeball-label {
  color: var(--text-muted);
  font-size: 0.9rem;
}

/* ── Keyframes ────────────────────────────────────────────────────────────── */
@keyframes pokeball-spin-anim {
  to {
    transform: rotate(360deg);
  }
}

@keyframes pokeball-bounce-anim {
  from {
    transform: translateY(0);
  }
  to {
    transform: translateY(-14px);
  }
}
</style>
