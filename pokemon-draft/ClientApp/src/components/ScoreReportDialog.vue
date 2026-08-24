<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    modelValue: boolean
    title: string
    subtitle?: string
    leftLabel: string
    rightLabel: string
    leftWins: number
    rightWins: number
    replayUrls: string[]
    error?: string
    loading?: boolean
    submitLabel?: string
  }>(),
  {
    subtitle: '',
    error: '',
    loading: false,
    submitLabel: 'Submit score',
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  'update:leftWins': [value: number]
  'update:rightWins': [value: number]
  'update:replayUrls': [value: string[]]
  submit: []
}>()

function updateReplayUrl(index: number, value: string | null) {
  const replayUrls = [...props.replayUrls]
  replayUrls[index] = value ?? ''
  emit('update:replayUrls', replayUrls)
}
</script>

<template>
  <v-dialog
    :model-value="modelValue"
    max-width="560"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <v-card class="score-dialog">
      <v-card-title>{{ title }}</v-card-title>
      <v-card-subtitle v-if="subtitle">{{ subtitle }}</v-card-subtitle>

      <v-card-text>
        <div class="score-dialog__scores">
          <v-number-input
            :model-value="leftWins"
            :label="leftLabel"
            :min="0"
            :max="2"
            hide-details
            @update:model-value="emit('update:leftWins', Number($event))"
          />
          <v-number-input
            :model-value="rightWins"
            :label="rightLabel"
            :min="0"
            :max="2"
            hide-details
            @update:model-value="emit('update:rightWins', Number($event))"
          />
        </div>

        <div class="score-dialog__replays">
          <v-text-field
            v-for="(replayUrl, index) in replayUrls"
            :key="index"
            :model-value="replayUrl"
            :label="'Replay link ' + (index + 1)"
            placeholder="https://replay.pokemonshowdown.com/..."
            prepend-inner-icon="mdi-link-variant"
            clearable
            hide-details
            @update:model-value="updateReplayUrl(index, $event)"
          />
        </div>

        <v-alert v-if="error" class="score-dialog__error" type="error" variant="tonal" density="compact">
          {{ error }}
        </v-alert>
      </v-card-text>

      <v-card-actions>
        <v-spacer />
        <v-btn variant="text" :disabled="loading" @click="emit('update:modelValue', false)">
          Cancel
        </v-btn>
        <v-btn color="primary" :loading="loading" @click="emit('submit')">
          {{ submitLabel }}
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.score-dialog {
  border-radius: 8px !important;
}

.score-dialog__scores {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  margin-bottom: 14px;
}

.score-dialog__replays {
  display: grid;
  gap: 10px;
}

.score-dialog__error {
  margin-top: 12px;
}

@media (max-width: 420px) {
  .score-dialog__scores {
    gap: 6px;
  }
}
</style>