<script setup lang="ts">
import { useRouter } from 'vue-router'

withDefaults(
  defineProps<{
    title?: string
    text: string
    actionLabel?: string
  }>(),
  {
    title: 'Draft is still in progress',
    actionLabel: 'Go to Draft Board',
  },
)

const router = useRouter()
</script>

<template>
  <v-card class="draft-gate" variant="outlined">
    <v-card-text class="draft-gate__content">
      <v-avatar color="warning" size="48" variant="tonal">
        <v-icon icon="mdi-progress-clock" size="26" />
      </v-avatar>
      <div class="draft-gate__copy">
        <h2>{{ title }}</h2>
        <p>{{ text }}</p>
      </div>
      <v-btn
        color="primary"
        prepend-icon="mdi-view-dashboard-variant"
        variant="tonal"
        @click="router.push('/league?tab=draft')"
      >
        {{ actionLabel }}
      </v-btn>
    </v-card-text>
  </v-card>
</template>

<style scoped>
.draft-gate {
  background: linear-gradient(135deg, rgba(255, 202, 98, 0.1), rgba(20, 26, 43, 0.7));
  border-color: rgba(255, 202, 98, 0.32);
  border-radius: var(--radius-lg);
}

.draft-gate__content {
  align-items: center;
  display: flex;
  gap: 14px;
  padding: 18px;
}

.draft-gate__copy {
  flex: 1;
  min-width: 0;
}

.draft-gate h2 {
  font-size: 1rem;
  line-height: 1.3;
  margin: 0;
}

.draft-gate p {
  color: var(--text-muted);
  font-size: 0.84rem;
  margin: 3px 0 0;
}

@media (max-width: 620px) {
  .draft-gate__content {
    align-items: flex-start;
    flex-wrap: wrap;
  }

  .draft-gate__copy {
    min-width: calc(100% - 64px);
  }

  .draft-gate .v-btn {
    width: 100%;
  }
}
</style>
