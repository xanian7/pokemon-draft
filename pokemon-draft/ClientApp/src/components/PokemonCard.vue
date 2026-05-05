<script setup lang="ts">
import AppIcon from '@/components/AppIcon.vue'
import type { Pokemon } from '@/types'
import { formatPokemonName, TYPE_COLORS } from '@/utils/format'
import { mdiCheck } from '@mdi/js'

const props = defineProps<{
  pokemon: Pokemon
  pointValue?: number
  isPicked?: boolean
  isCurrentPicker?: boolean
  mode?: 'browse' | 'draft' | 'team'
}>()

const emit = defineEmits<{
  click: []
}>()
</script>

<template>
  <div
    class="pokemon-card"
    :class="{
      'is-picked': isPicked,
      'mode-draft': mode === 'draft',
      'mode-team': mode === 'team',
    }"
    @click="!isPicked && emit('click')"
  >
    <img
      :src="pokemon.spriteUrl"
      :alt="formatPokemonName(pokemon.name)"
      class="sprite"
      loading="lazy"
    />
    <div class="info">
      <span class="pokemon-id">#{{ String(pokemon.id).padStart(3, '0') }}</span>
      <span class="pokemon-name">{{ formatPokemonName(pokemon.name) }}</span>
      <div class="types">
        <span
          v-for="type in pokemon.types"
          :key="type"
          class="type-badge"
          :style="{ backgroundColor: TYPE_COLORS[type] ?? '#888' }"
        >
          {{ type }}
        </span>
      </div>
      <span v-if="pointValue !== undefined" class="point-value">
        {{ pointValue > 0 ? `${pointValue} pts` : 'No value' }}
      </span>
    </div>
    <div v-if="isPicked" class="picked-overlay">
      <AppIcon :path="mdiCheck" :size="14" />
      Drafted
    </div>
  </div>
</template>

<style scoped>
.pokemon-card {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  padding: 0.5rem;
  cursor: pointer;
  transition:
    transform 0.15s,
    box-shadow 0.15s,
    opacity 0.15s;
  text-align: center;
  min-width: 0;
}

.pokemon-card:hover:not(.is-picked) {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.2);
  border-color: var(--primary);
}

.pokemon-card.is-picked {
  opacity: 0.45;
  cursor: not-allowed;
}

.mode-team {
  flex-direction: row;
  gap: 0.5rem;
  align-items: center;
  text-align: left;
  cursor: default;
}

.mode-team:hover:not(.is-picked) {
  transform: none;
  box-shadow: none;
}

.sprite {
  width: 72px;
  height: 72px;
  image-rendering: pixelated;
}

.mode-team .sprite {
  width: 48px;
  height: 48px;
}

.info {
  display: flex;
  flex-direction: column;
  gap: 2px;
  width: 100%;
}

.pokemon-id {
  font-size: 0.65rem;
  color: var(--text-muted);
}

.pokemon-name {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.types {
  display: flex;
  gap: 3px;
  flex-wrap: wrap;
  justify-content: center;
}

.mode-team .types {
  justify-content: flex-start;
}

.type-badge {
  font-size: 0.6rem;
  color: #fff;
  padding: 1px 5px;
  border-radius: 4px;
  text-transform: capitalize;
  font-weight: 600;
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.4);
}

.point-value {
  font-size: 0.7rem;
  font-weight: 700;
  color: var(--primary);
}

.picked-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.35rem;
  font-size: 0.7rem;
  font-weight: 700;
  color: var(--text-muted);
  border-radius: 10px;
}
</style>
