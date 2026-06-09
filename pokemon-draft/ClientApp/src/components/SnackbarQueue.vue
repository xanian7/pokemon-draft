<script setup lang="ts">
import { computed } from 'vue'
import { useSnackbarQueue } from '@/services/snackbar'

const { queue, dismissSnackbar } = useSnackbarQueue()
const currentMessage = computed(() => queue.value[0] ?? null)

function handleVisibility(visible: boolean) {
  if (!visible && currentMessage.value) {
    dismissSnackbar(currentMessage.value.id)
  }
}
</script>

<template>
  <v-snackbar
    v-if="currentMessage"
    :key="currentMessage.id"
    :model-value="true"
    :color="currentMessage.color"
    :timeout="currentMessage.timeout"
    location="bottom end"
    @update:model-value="handleVisibility"
  >
    {{ currentMessage.text }}
    <template #actions>
      <v-btn variant="text" @click="dismissSnackbar(currentMessage.id)">Dismiss</v-btn>
    </template>
  </v-snackbar>
</template>
