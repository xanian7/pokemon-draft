<script setup lang="ts">
withDefaults(
  defineProps<{
    eyebrow?: string
    title: string
    subtitle?: string
    icon?: string
  }>(),
  {
    eyebrow: '',
    subtitle: '',
    icon: '',
  },
)
</script>

<template>
  <header class="section-header">
    <div class="section-header__copy">
      <span v-if="eyebrow" class="section-header__eyebrow">{{ eyebrow }}</span>
      <h2>{{ title }}</h2>
      <p v-if="subtitle">{{ subtitle }}</p>
    </div>
    <div v-if="icon || $slots.actions" class="section-header__aside">
      <slot name="actions" />
      <v-icon v-if="icon" :icon="icon" size="20" />
    </div>
  </header>
</template>

<style scoped>
.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  min-height: 58px;
  padding: 10px 14px;
  border-bottom: 1px solid var(--border-color);
}

.section-header__copy {
  min-width: 0;
}

.section-header__eyebrow {
  display: block;
  color: var(--text-muted);
  font-size: 0.66rem;
  font-weight: 700;
  text-transform: uppercase;
}

h2 {
  overflow: hidden;
  font-size: 1rem;
  font-weight: 800;
  text-overflow: ellipsis;
  white-space: nowrap;
}

p {
  margin-top: 2px;
  color: var(--text-muted);
  font-size: 0.74rem;
}

.section-header__aside {
  display: flex;
  align-items: center;
  flex: 0 0 auto;
  gap: 8px;
  color: var(--text-muted);
}

@media (max-width: 520px) {
  .section-header {
    align-items: flex-start;
  }

  p {
    font-size: 0.7rem;
  }
}
</style>