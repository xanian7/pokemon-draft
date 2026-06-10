<script setup lang="ts">
withDefaults(
  defineProps<{
    eyebrow?: string
    title: string
    subtitle?: string
  }>(),
  {
    eyebrow: '',
    subtitle: '',
  },
)
</script>

<template>
  <header class="page-header">
    <div v-if="$slots.leading" class="page-header__leading">
      <slot name="leading" />
    </div>
    <div class="page-header__copy">
      <div v-if="eyebrow" class="eyebrow">{{ eyebrow }}</div>
      <h1>{{ title }}</h1>
      <p v-if="subtitle">{{ subtitle }}</p>
      <slot name="meta" />
    </div>
    <div v-if="$slots.actions" class="page-header__actions">
      <slot name="actions" />
    </div>
  </header>
</template>

<style scoped>
.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: clamp(1rem, 2vw, 1.5rem);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  background: linear-gradient(135deg, rgba(var(--primary-rgb), 0.14), transparent);
}

.page-header__copy {
  flex: 1;
  min-width: 0;
}

.page-header__leading {
  flex: 0 0 auto;
}

h1 {
  margin-top: 3px;
  font-size: clamp(1.5rem, 3vw, 2.1rem);
  font-weight: 800;
  line-height: 1.15;
}

p {
  margin-top: 4px;
  color: var(--text-muted);
  font-size: 0.86rem;
}

.page-header__actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 10px;
  flex-wrap: wrap;
}

@media (max-width: 620px) {
  .page-header {
    align-items: flex-start;
    flex-direction: column;
  }

  .page-header__actions {
    width: 100%;
    justify-content: flex-start;
  }
}
</style>
