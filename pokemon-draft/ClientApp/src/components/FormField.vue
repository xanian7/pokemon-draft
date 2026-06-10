<script setup lang="ts">
withDefaults(
  defineProps<{
    label: string
    hint?: string
  }>(),
  {
    hint: '',
  },
)
</script>

<template>
  <label class="form-field">
    <span class="form-field__label">{{ label }}</span>
    <slot />
    <span v-if="hint" class="form-field__hint">{{ hint }}</span>
  </label>
</template>

<style scoped>
.form-field {
  display: flex;
  flex-direction: column;
  gap: 7px;
  min-width: 0;
}

.form-field__label {
  color: var(--text-muted);
  font-size: 0.74rem;
  font-weight: 750;
  letter-spacing: 0.045em;
  text-transform: uppercase;
}

.form-field__hint {
  color: var(--text-subtle);
  font-size: 0.75rem;
  line-height: 1.4;
}
</style>
