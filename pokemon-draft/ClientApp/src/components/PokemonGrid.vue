<script setup lang="ts">
import { computed, ref } from 'vue'
import { usePokemonStore } from '@/stores/pokemon'
import { useDraftStore } from '@/stores/draft'
import { useAuthStore } from '@/stores/auth'
import { useRegulationFilter } from '@/composables/useRegulationFilter'
import PokemonCard from './PokemonCard.vue'
import PokemonDetailModal from './PokemonDetailModal.vue'
import AppIcon from './AppIcon.vue'
import FormField from './FormField.vue'
import type { Pokemon } from '@/types'
import { mdiViewGrid, mdiViewColumn } from '@mdi/js'
import { formatPokemonName } from '@/utils/format'
import { useDisplay } from 'vuetify'
import { enqueueSnackbar } from '@/services/snackbar'

const props = withDefaults(
  defineProps<{
    mode?: 'draft' | 'select'
    regulationSet?: string
    pickedPokemonIds?: ReadonlySet<number>
    hidePickedDefault?: boolean
    actionLabel?: string
    canSelect?: (pokemon: Pokemon) => boolean
    disabledReason?: (pokemon: Pokemon) => string
  }>(),
  {
    mode: 'draft',
    hidePickedDefault: false,
    actionLabel: 'Select',
  },
)

const emit = defineEmits<{
  select: [pokemon: Pokemon]
}>()

const pokemonStore = usePokemonStore()
const draftStore = useDraftStore()
const authStore = useAuthStore()
const { xs } = useDisplay()

// ── Filter state ─────────────────────────────────────────────────────────────
const searchQuery = ref('')
const selectedType = ref<string | null>(null)
const hidePicked = ref(props.hidePickedDefault)
const viewMode = ref<'grid' | 'tier'>('grid')

// ── Regulation filter (respects league's regulation set automatically) ────────
const { isLegalPokemon, regulationLoading } = useRegulationFilter(
  computed(() => props.regulationSet ?? draftStore.regulationSet),
)
const effectivePickedPokemonIds = computed(
  () => props.pickedPokemonIds ?? draftStore.pickedPokemonIds,
)

// ── Filtered list ─────────────────────────────────────────────────────────────
const filteredPokemon = computed(() => {
  const q = searchQuery.value.toLowerCase()
  return pokemonStore.pokemonWithPoints.filter((p) => {
    if (
      q &&
      !p.name.toLowerCase().includes(q) &&
      !formatPokemonName(p.name).toLowerCase().includes(q)
    )
      return false
    if (!isLegalPokemon(p)) return false
    if (selectedType.value && !p.types.includes(selectedType.value)) return false
    if (hidePicked.value && effectivePickedPokemonIds.value.has(p.id)) return false
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
const canDraft = computed(
  () => !draftStore.isDraftComplete && draftStore.playerCanDraft(authStore.playerId ?? ''),
)
const pointsRemaining = computed(() =>
  authStore.playerId ? draftStore.getPlayerPointsRemaining(authStore.playerId) : 0,
)

const selectedPokemon = ref<Pokemon | null>(null)
const selectedPokemonPointValue = computed(() =>
  selectedPokemon.value ? pokemonStore.getPointValue(selectedPokemon.value.id) : 0,
)
const selectedPokemonIsOverBudget = computed(
  () => selectedPokemonPointValue.value > pointsRemaining.value,
)
const selectedCanDraft = computed(() => canDraft.value && !selectedPokemonIsOverBudget.value)
const selectedCanAct = computed(() => {
  if (!selectedPokemon.value) return false
  if (props.mode === 'select') return props.canSelect?.(selectedPokemon.value) ?? true
  return selectedCanDraft.value
})
const selectedDisabledReason = computed(() => {
  if (props.mode === 'select' && selectedPokemon.value) {
    return props.disabledReason?.(selectedPokemon.value) ?? 'Unavailable'
  }
  if (selectedPokemonIsOverBudget.value) return 'Over Point Limit'
  return 'Not Your Turn'
})

function selectPokemon(pokemon: Pokemon) {
  if (effectivePickedPokemonIds.value.has(pokemon.id)) return
  selectedPokemon.value = pokemon
}

function isPokemonDisabled(pokemon: Pokemon) {
  if (props.mode === 'select') return !(props.canSelect?.(pokemon) ?? true)
  return canDraft.value && !draftStore.playerCanAffordPokemon(authStore.playerId ?? '', pokemon.id)
}

function getDisabledLabel(pokemon: Pokemon) {
  if (props.mode === 'select') return props.disabledReason?.(pokemon) ?? 'Unavailable'
  return 'Over budget'
}

async function handleAction(pokemonId: number) {
  if (props.mode === 'select') {
    const pokemon = pokemonStore.getPokemonById(pokemonId)
    if (pokemon) emit('select', pokemon)
    selectedPokemon.value = null
    return
  }

  if (authStore.playerId && !draftStore.playerCanAffordPokemon(authStore.playerId, pokemonId)) {
    const pointValue = pokemonStore.getPointValue(pokemonId)
    enqueueSnackbar(
      `That Pokémon costs ${pointValue} points, but you only have ${pointsRemaining.value} points remaining.`,
      'error',
    )
    return
  }

  const error = await draftStore.makePick(pokemonId)
  if (error) {
    console.error('Draft pick failed:', error)
    enqueueSnackbar(error, 'error')
    return
  }
  selectedPokemon.value = null
}
</script>

<template>
  <v-container fluid class="remove-left-right-padding pokemon-grid-shell">
    <v-card class="grid-container">
      <!-- ── Filter bar ─────────────────────────────────────────────────────── -->
      <div class="pokemon-grid-header">
        <v-row>
          <v-col>
            <FormField label="Search Pokémon">
              <v-text-field
                v-model="searchQuery"
                placeholder="Name or form"
                clearable
                hide-details
              />
            </FormField>
          </v-col>
        </v-row>
        <div class="filter-bar">
          <FormField label="Type" class="type-filter">
            <v-select
              v-model="selectedType"
              :items="pokemonStore.allTypes"
              clearable
              placeholder="All types"
              hide-details
            />
          </FormField>

          <v-checkbox
            v-model="hidePicked"
            label="Hide picked"
            class="toggle"
            hide-details
          ></v-checkbox>

          <span v-if="regulationLoading" class="filter-status">Loading...</span>

          <div class="view-toggle">
            <button
              :class="{ active: viewMode === 'grid' }"
              title="Grid view"
              @click="viewMode = 'grid'"
            >
              <AppIcon :path="mdiViewGrid" :size="16" />
            </button>
            <button
              :class="{ active: viewMode === 'tier' }"
              title="Tier view"
              @click="viewMode = 'tier'"
            >
              <AppIcon :path="mdiViewColumn" :size="16" />
            </button>
          </div>
        </div>
        <v-divider></v-divider>
      </div>

      <!-- ── Tier view ──────────────────────────────────────────────────────── -->
      <div class="grid-view-container">
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
                :is-picked="effectivePickedPokemonIds.has(pokemon.id)"
                :is-disabled="isPokemonDisabled(pokemon)"
                :disabled-label="getDisabledLabel(pokemon)"
                mode="draft"
                :show-sprite="!xs"
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
            :is-picked="effectivePickedPokemonIds.has(pokemon.id)"
            :is-disabled="isPokemonDisabled(pokemon)"
            :disabled-label="getDisabledLabel(pokemon)"
            mode="draft"
            :show-sprite="!xs"
            :point-value="pokemonStore.getPointValue(pokemon.id)"
            @click="selectPokemon(pokemon)"
          />
        </div>
      </div>
    </v-card>

    <!-- ── Detail modal ───────────────────────────────────────────────────── -->
    <pokemon-detail-modal
      v-if="selectedPokemon"
      :pokemon="selectedPokemon"
      :can-draft="selectedCanAct"
      :is-picked="effectivePickedPokemonIds.has(selectedPokemon.id)"
      :point-value="pokemonStore.getPointValue(selectedPokemon.id)"
      :action-label="props.mode === 'select' ? props.actionLabel : undefined"
      :draft-disabled-reason="selectedDisabledReason"
      @close="selectedPokemon = null"
      @draft="handleAction"
    />
  </v-container>
</template>

<style scoped>
.pokemon-grid-header {
  flex: 0 0 auto;
  flex-shrink: 0;
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



.toggle {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  font-size: 0.8rem;
  color: var(--primary);
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
  transition:
    background 0.12s,
    color 0.12s;
}

.view-toggle button:first-child {
  border-right: 1px solid var(--border-color);
}
.view-toggle button:hover {
  background: var(--input-bg);
  color: var(--text);
}
.view-toggle button.active {
  background: var(--input-bg);
  color: var(--text);
}

/* Grid view */
.pokemon-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  gap: 0.4rem;
  width: 100%;
  align-content: start;
  overflow: hidden;
}

/* Tier view */
.tier-view {
  display: flex;
  flex-direction: row;
  gap: 0.65rem;
  padding-right: 8px;
  align-items: flex-start;
  min-width: max-content;
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

.pokemon-grid-shell {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  width: 100%;
  height: 100%;
  max-height: 100%;
  min-height: 0;
  overflow: hidden;
  padding-bottom: 0;
  padding-top: 0;
}

.grid-container {
  border: 1px solid var(--border-color);
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  height: 100%;
  max-height: 100%;
  min-height: 0;
  overflow: hidden;
  padding: 8px;
  width: 100%;
}

.grid-view-container {
  border: 1px solid var(--border-color);
  border-radius: 6px;
  display: block;
  flex: 1 1 auto;
  height: 0;
  min-height: 0;
  padding: 8px;
  margin-top: 8px;
  overflow: auto;
  background: var(--bg);
}

.type-filter {
  max-width: 200px;
}

@media (max-width: 720px) {
  .pokemon-grid-shell {
    height: auto;
    max-height: none;
    min-height: 0;
    overflow: visible;
  }

  .grid-container {
    border-left: 0;
    border-right: 0;
    border-radius: 0;
    height: auto;
    max-height: none;
    overflow: visible;
    padding: 6px;
  }

  .pokemon-grid-header {
    position: sticky;
    top: 0;
    z-index: 5;
    background: var(--card-bg);
    padding-bottom: 0px;
  }

  .filter-bar {
    align-items: stretch;
  }

  .type-filter {
    flex: 1 1 160px;
    max-width: none;
  }

  .view-toggle {
    margin-left: 0;
  }

  .grid-view-container {
    border-left: 0;
    border-right: 0;
    height: auto;
    margin-top: 6px;
    overflow: visible;
    padding: 6px 0;
  }

  .pokemon-grid {
    grid-template-columns: repeat(auto-fill, minmax(96px, 1fr));
    gap: 0.35rem;
  }

  .tier-view {
    min-width: 0;
    overflow-x: auto;
    padding-bottom: 8px;
  }

  .tier-col {
    width: 104px;
  }
}
</style>
