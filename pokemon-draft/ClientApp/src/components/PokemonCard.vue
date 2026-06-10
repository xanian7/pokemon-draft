<script setup lang="ts">
import AppIcon from '@/components/AppIcon.vue'
import type { Pokemon } from '@/types'
import { formatPokemonName, TYPE_COLORS } from '@/utils/format'
import { mdiCheck } from '@mdi/js'

const props = defineProps<{
  pokemon: Pokemon
  pointValue?: number
  isPicked?: boolean
  isDisabled?: boolean
  disabledLabel?: string
  isCurrentPicker?: boolean
  mode?: 'browse' | 'draft' | 'team'
  showSprite?: boolean
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
      'is-disabled': isDisabled,
      'mode-draft': mode === 'draft',
      'mode-team': mode === 'team',
    }"
    :data-tooltip="mode === 'draft' ? formatPokemonName(pokemon.name) : undefined"
    @click="emit('click')"
  >
    <img
      v-if="showSprite !== false"
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
    <div v-else-if="isDisabled" class="picked-overlay">
      {{ disabledLabel ?? 'Unavailable' }}
    </div>
  </div>
</template>

<style scoped>
.pokemon-card {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  background:
    radial-gradient(circle at 50% 18%, rgba(var(--primary-rgb), 0.09), transparent 48%),
    var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 16px;
  padding: 0.7rem;
  cursor: pointer;
  transition:
    transform 0.2s ease,
    box-shadow 0.2s ease,
    border-color 0.2s ease,
    opacity 0.15s;
  text-align: center;
  min-width: 0;
  max-height: fit-content;
}

.pokemon-card:hover:not(.is-picked, .is-disabled) {
  transform: translateY(-4px);
  box-shadow: 0 18px 36px rgba(0, 0, 0, 0.28);
  border-color: rgba(var(--primary-rgb), 0.58);
  z-index: 1;
}

.pokemon-card.is-picked {
  opacity: 0.45;
  cursor: not-allowed;
}

.pokemon-card.is-disabled {
  opacity: 0.5;
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
  width: 78px;
  height: 78px;
  object-fit: contain;
  filter: drop-shadow(0 10px 10px rgba(0, 0, 0, 0.24));
  transition: transform 0.2s ease;
}

.pokemon-card:hover:not(.is-picked, .is-disabled) .sprite {
  transform: scale(1.06);
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
  color: var(--text-subtle);
  font-weight: 700;
}

.pokemon-name {
  font-size: 0.8rem;
  font-weight: 750;
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

.mode-draft .types {
  flex-wrap: nowrap;
  overflow: hidden;
}

.mode-team .types {
  justify-content: flex-start;
}

.type-badge {
  font-size: 0.6rem;
  color: #fff;
  padding: 2px 7px;
  border-radius: 999px;
  text-transform: capitalize;
  font-weight: 600;
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.4);
}

.point-value {
  font-size: 0.7rem;
  font-weight: 700;
  color: var(--primary-bright);
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

.mode-draft[data-tooltip] {
  position: relative;
}

.mode-draft[data-tooltip]::after {
  content: attr(data-tooltip);
  position: absolute;
  top: calc(100% + 6px);
  left: 50%;
  transform: translateX(-50%);
  background: rgba(0, 0, 0, 0.85);
  color: #fff;
  font-size: 0.7rem;
  font-weight: 600;
  white-space: nowrap;
  padding: 3px 8px;
  border-radius: 5px;
  pointer-events: none;
  opacity: 0;
  transition: opacity 0.15s;
  z-index: 1000;
}

.mode-draft[data-tooltip]:hover::after {
  opacity: 1;
}

@media (max-width: 720px) {
  .pokemon-card {
    border-radius: 13px;
    min-height: 82px;
    padding: 0.55rem 0.4rem;
  }

  .pokemon-card:hover:not(.is-picked, .is-disabled) {
    transform: none;
    box-shadow: none;
  }

  .pokemon-id {
    font-size: 0.6rem;
  }

  .pokemon-name {
    font-size: 0.74rem;
  }

  .types {
    gap: 2px;
  }

  .type-badge {
    font-size: 0.55rem;
    padding: 1px 4px;
  }

  .point-value {
    font-size: 0.66rem;
  }

  .mode-draft[data-tooltip]::after {
    display: none;
  }
}
</style>
