<script setup lang="ts">
import { computed, ref } from 'vue'
import { usePokemonStore } from '@/stores/pokemon'
import { useDraftStore } from '@/stores/draft'
import { useAuthStore } from '@/stores/auth'
import { useRegulationFilter } from '@/composables/useRegulationFilter'
import PokemonCard from './PokemonCard.vue'
import PokemonDetailModal from './PokemonDetailModal.vue'
import SearchBox from './SearchBox.vue'
import AppIcon from './AppIcon.vue'
import type { Pokemon } from '@/types'
import { mdiViewGrid, mdiViewColumn } from '@mdi/js'
import { formatPokemonName } from '@/utils/format'

const pokemonStore = usePokemonStore()
const draftStore = useDraftStore()
const authStore = useAuthStore()

// ── Filter state ─────────────────────────────────────────────────────────────
const searchQuery = ref('')
const selectedType = ref('')
const hidePicked = ref(false)
const viewMode = ref<'grid' | 'tier'>('grid')

// ── Regulation filter (respects league's regulation set automatically) ────────
const { isLegalPokemon, regulationLoading } = useRegulationFilter(
  computed(() => draftStore.regulationSet)
)

// ── Filtered list ─────────────────────────────────────────────────────────────
const filteredPokemon = computed(() => {
  const q = searchQuery.value.toLowerCase()
  return pokemonStore.pokemonWithPoints.filter((p) => {
    if (q && !p.name.toLowerCase().includes(q) && !formatPokemonName(p.name).toLowerCase().includes(q))
      return false
    if (!isLegalPokemon(p)) return false
    if (selectedType.value && !p.types.includes(selectedType.value)) return false
    if (hidePicked.value && draftStore.pickedPokemonIds.has(p.id)) return false
    return true
  })
})

// ── Tier grouping ─────────────────────────────────────────────────────────────
const tierGroups = computed(() => {
  const groups = new Map<number, typeof filteredPokemon.value>()
  for (const p of filteredPokemon.value) {
    if (!groups.has(p.pointValue)) groups.set(p.pointValue, [])
    groups.get(p.pointValue)!.push(p)
  }
  return Array.from(groups.entries())
    .sort((a, b) => b[0] - a[0])
    .map(([pts, pokemon]) => ({ pts, pokemon }))
})

// ── Draft action ──────────────────────────────────────────────────────────────
const canDraft = computed(() =>
  !draftStore.isDraftComplete && draftStore.playerCanDraft(authStore.playerId ?? '')
)

const selectedPokemon = ref<Pokemon | null>(null)

function selectPokemon(pokemon: Pokemon) {
  selectedPokemon.value = pokemon
}

async function handleDraft(pokemonId: number) {
  await draftStore.makePick(pokemonId)
  selectedPokemon.value = null
}
</script>

<template>
  <div class="wrapper">
    <!-- ── Filter bar ─────────────────────────────────────────────────────── -->
    <div class="pokemon-grid-header">
      <search-box v-model="searchQuery" />
      <div class="filter-bar">
        <select v-model="selectedType">
          <option value="">All types</option>
          <option v-for="t in pokemonStore.allTypes" :key="t" :value="t">
            {{ t.charAt(0).toUpperCase() + t.slice(1) }}
          </option>
        </select>

        <label class="toggle">
          <input v-model="hidePicked" type="checkbox" />
          Hide picked
        </label>

        <span v-if="regulationLoading" class="filter-status">Loading...</span>

        <div class="view-toggle">
          <button :class="{ active: viewMode === 'grid' }" title="Grid view" @click="viewMode = 'grid'">
            <AppIcon :path="mdiViewGrid" :size="16" />
          </button>
          <button :class="{ active: viewMode === 'tier' }" title="Tier view" @click="viewMode = 'tier'">
            <AppIcon :path="mdiViewColumn" :size="16" />
          </button>
        </div>
      </div>
    </div>

    <!-- ── Tier view ──────────────────────────────────────────────────────── -->
    <div v-if="viewMode === 'tier'" class="tier-view">
      <div v-for="group in tierGroups" :key="group.pts" class="tier-col">
        <div class="tier-col-header">
          <span class="tier-badge">{{ group.pts }} pts</span>
          <span class="tier-count">{{ group.pokemon.length }}</span>
        </div>
        <div class="tier-col-body">
          <pokemon-card
            v-for="pokemon in group.pokemon"
            :key="pokemon.id"
            :pokemon="pokemon"
            :is-picked="draftStore.pickedPokemonIds.has(pokemon.id)"
            :point-value="pokemonStore.getPointValue(pokemon.id)"
            @click="selectPokemon(pokemon)"
          />
        </div>
      </div>
    </div>

    <!-- ── Grid view ──────────────────────────────────────────────────────── -->
    <div v-else class="pokemon-grid">
      <pokemon-card
        v-for="pokemon in filteredPokemon"
        :key="pokemon.id"
        :pokemon="pokemon"
        :is-picked="draftStore.pickedPokemonIds.has(pokemon.id)"
        :point-value="pokemonStore.getPointValue(pokemon.id)"
        @click="selectPokemon(pokemon)"
      />
    </div>

    <!-- ── Detail modal ───────────────────────────────────────────────────── -->
    <pokemon-detail-modal
      v-if="selectedPokemon"
      :pokemon="selectedPokemon"
      :can-draft="canDraft"
      :is-picked="draftStore.pickedPokemonIds.has(selectedPokemon.id)"
      :point-value="pokemonStore.getPointValue(selectedPokemon.id)"
      @close="selectedPokemon = null"
      @draft="handleDraft"
    />
  </div>
</template>

<style scoped>
.wrapper {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  overflow: hidden;
  padding: 8px 0px 8px 8px;
}

.pokemon-grid-header {
  flex-shrink: 0;
  padding-bottom: 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

/* Filter bar */
.filter-bar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

select {
  padding: 0.3rem 0.55rem;
  font-size: 0.82rem;
  border-radius: 6px;
}

.toggle {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  font-size: 0.8rem;
  color: var(--text-muted);
  cursor: pointer;
  white-space: nowrap;
}

.filter-status {
  font-size: 0.78rem;
  color: var(--text-muted);
}

/* View toggle */
.view-toggle {
  display: flex;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  overflow: hidden;
  margin-left: auto;
}

.view-toggle button {
  display: flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: none;
  color: var(--text-muted);
  padding: 0.3rem 0.5rem;
  cursor: pointer;
  transition: background 0.12s, color 0.12s;
}

.view-toggle button:first-child { border-right: 1px solid var(--border-color); }
.view-toggle button:hover { background: var(--input-bg); color: var(--text); }
.view-toggle button.active { background: var(--input-bg); color: var(--text); }

/* Grid view */
.pokemon-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  gap: 0.4rem;
  padding-right: 8px;
  overflow-y: auto;
  flex: 1;
}

/* Tier view */
.tier-view {
  display: flex;
  flex-direction: row;
  gap: 0.65rem;
  overflow-x: auto;
  overflow-y: auto;
  padding-right: 8px;
  flex: 1;
  align-items: flex-start;
}

.tier-col {
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  width: 120px;
}

.tier-col-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.45rem 0.6rem;
  margin-bottom: 0.5rem;
  position: sticky;
  top: 0;
  z-index: 10;
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-top: 2px solid var(--primary);
  border-radius: 6px;
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.25);
}

.tier-badge {
  font-size: 0.78rem;
  font-weight: 800;
  color: var(--text);
  white-space: nowrap;
}

.tier-count {
  font-size: 0.66rem;
  font-weight: 600;
  color: var(--text-muted);
  background: var(--input-bg);
  border-radius: 10px;
  padding: 0.08rem 0.35rem;
}

.tier-col-body {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}
</style>
