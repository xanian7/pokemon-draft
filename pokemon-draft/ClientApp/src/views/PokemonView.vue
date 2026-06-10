<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import PokemonCard from '@/components/PokemonCard.vue'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import PageHeader from '@/components/PageHeader.vue'
import FormField from '@/components/FormField.vue'
import { REGULATIONS, getRegulation } from '@/data/regulations'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import { API_BASE } from '@/services/signalr'
import { formatPokemonName } from '@/utils/format'
import { useDisplay } from 'vuetify'

const pokemonStore = usePokemonStore()
const authStore = useAuthStore()
const { xs } = useDisplay()

const searchQuery = ref('')
const selectedRegulation = ref('national')
const selectedType = ref('')
const showOnlyValued = ref(false)
const legalIds = ref<Set<number> | null>(null)
const regulationLoading = ref(false)
const regulationError = ref('')

const saving = ref(false)
const saveError = ref('')
const saveSuccess = ref(false)

// ── Detail modal ──────────────────────────────────────────────────────────────
const detailPokemon = ref<(typeof filtered.value)[0] | null>(null)

function openDetail(p: (typeof filtered.value)[0]) {
  detailPokemon.value = p
}

function closeDetail() {
  detailPokemon.value = null
}

onMounted(async () => {
  await pokemonStore.fetchAllPokemon()

  if (authStore.leagueCode) {
    try {
      const res = await fetch(`${API_BASE}/leagues/${authStore.leagueCode}`)
      if (res.ok) {
        const state = await res.json()
        selectedRegulation.value = state.regulationSet ?? 'national'
        for (const [id, pts] of Object.entries(state.pointValues as Record<string, number>)) {
          pokemonStore.setPointValue(Number(id), pts)
        }
      }
    } catch {
      // Leave local defaults in place when league data is unavailable.
    }
  }

  await onRegulationChange()
})

const filtered = computed(() => {
  const q = searchQuery.value.toLowerCase()
  return pokemonStore.pokemonWithPoints.filter((p) => {
    if (q && !p.name.includes(q) && !formatPokemonName(p.name).toLowerCase().includes(q))
      return false
    if (selectedRegulation.value !== 'national') {
      const reg = getRegulation(selectedRegulation.value)
      if (reg.isLegal) {
        if (!reg.isLegal(p.speciesId)) return false
      } else if (legalIds.value) {
        if (!legalIds.value.has(p.speciesId)) return false
      }
    }
    if (selectedType.value && !p.types.includes(selectedType.value)) return false
    if (showOnlyValued.value && p.pointValue <= 0) return false
    return true
  })
})

const valuedCount = computed(() => Object.keys(pokemonStore.pointValues).length)

const viewMode = ref<'grid' | 'tier'>('grid')

const tierGroups = computed(() => {
  const groups = new Map<number, typeof filtered.value>()
  for (const p of filtered.value) {
    if (!groups.has(p.pointValue)) groups.set(p.pointValue, [])
    groups.get(p.pointValue)!.push(p)
  }
  return Array.from(groups.entries())
    .sort((a, b) => b[0] - a[0])
    .map(([pts, pokemon]) => ({ pts, pokemon }))
})

async function onRegulationChange() {
  legalIds.value = null
  regulationError.value = ''

  const regulation = getRegulation(selectedRegulation.value)
  if (!regulation.fetchLegalIds) return

  regulationLoading.value = true
  try {
    legalIds.value = await regulation.fetchLegalIds()
  } catch (error) {
    regulationError.value =
      error instanceof Error ? error.message : 'Failed to load regulation set.'
  } finally {
    regulationLoading.value = false
  }
}

function onPointInput(pokemonId: number, event: Event) {
  const val = Number((event.target as HTMLInputElement).value)
  pokemonStore.setPointValue(pokemonId, isNaN(val) ? 0 : val)
}

function applyDefaults() {
  const regulation = getRegulation(selectedRegulation.value)
  const currentLegalIds =
    selectedRegulation.value === 'national'
      ? undefined
      : regulation.isLegal
        ? pokemonStore.allPokemon
            .filter((pokemon) => regulation.isLegal?.(pokemon.speciesId))
            .map((pokemon) => pokemon.id)
        : legalIds.value
          ? pokemonStore.allPokemon
              .filter((pokemon) => legalIds.value!.has(pokemon.speciesId))
              .map((pokemon) => pokemon.id)
          : []

  pokemonStore.applyDefaultPoints(currentLegalIds)
}

async function saveToServer() {
  if (!authStore.leagueCode || !authStore.isAdmin) return
  saving.value = true
  saveError.value = ''
  saveSuccess.value = false
  try {
    const points: Record<string, number> = {}
    for (const [id, val] of Object.entries(pokemonStore.pointValues)) {
      points[id] = val
    }
    const res = await fetch(`${API_BASE}/leagues/${authStore.leagueCode}/points`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ values: points }),
    })
    if (!res.ok) throw new Error(await res.text())
    saveSuccess.value = true
    setTimeout(() => (saveSuccess.value = false), 3000)
  } catch (e: any) {
    saveError.value = e.message ?? 'Failed to save'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <v-container fluid class="pokemon-view">
    <PageHeader
      class="page-hero"
      :eyebrow="authStore.isAdmin ? 'Commissioner tools' : 'League pool'"
      :title="authStore.isAdmin ? 'Pokémon Point Values' : 'Free Agents'"
      :subtitle="`${valuedCount} valued · ${filtered.length} currently shown`"
    >
      <template #actions>
        <div v-if="authStore.isAdmin" class="admin-actions">
          <v-btn
            variant="tonal"
            prepend-icon="mdi-lightning-bolt-outline"
            :disabled="pokemonStore.isLoading || regulationLoading"
            @click="applyDefaults"
          >
            Apply defaults
          </v-btn>
          <v-btn
            color="primary"
            variant="flat"
            prepend-icon="mdi-content-save-outline"
            :loading="saving"
            @click="saveToServer"
          >
            Save changes
          </v-btn>
        </div>
      </template>
    </PageHeader>

    <v-alert v-if="saveSuccess" type="success" variant="tonal" density="compact" class="mb-3">
      Point values saved to the league.
    </v-alert>
    <v-alert v-if="saveError" type="error" variant="tonal" density="compact" class="mb-3">
      {{ saveError }}
    </v-alert>

    <v-card variant="outlined" class="filter-card">
      <v-card-text class="filter-grid">
        <FormField label="Search Pokémon">
          <v-text-field
            v-model="searchQuery"
            placeholder="Name or form"
            prepend-inner-icon="mdi-magnify"
            hide-details
            clearable
          />
        </FormField>
        <FormField label="Regulation">
          <v-select
            v-model="selectedRegulation"
            :items="REGULATIONS"
            item-title="label"
            item-value="id"
            hide-details
            @update:model-value="onRegulationChange"
          />
        </FormField>
        <FormField label="Type">
          <v-select
            v-model="selectedType"
            :items="pokemonStore.allTypes"
            placeholder="All types"
            hide-details
            clearable
          />
        </FormField>
        <v-switch v-model="showOnlyValued" label="Valued only" color="primary" hide-details />
        <v-btn-toggle v-model="viewMode" mandatory density="comfortable" variant="outlined">
          <v-btn value="grid" icon="mdi-view-grid-outline" aria-label="Grid view" />
          <v-btn value="tier" icon="mdi-view-column-outline" aria-label="Tier view" />
        </v-btn-toggle>
      </v-card-text>
      <v-progress-linear v-if="regulationLoading" indeterminate color="primary" />
      <v-alert v-else-if="regulationError" type="error" variant="tonal" density="compact">
        {{ regulationError }}
      </v-alert>
    </v-card>

    <!-- Grid -->
    <div v-if="pokemonStore.isLoading" class="loading">
      <PokeballLoader variant="page" label="Loading Pokémon data…" />
    </div>
    <div v-else-if="pokemonStore.error" class="loading">
      {{ pokemonStore.error }}
      <v-btn variant="tonal" @click="pokemonStore.fetchAllPokemon()">Retry</v-btn>
    </div>
    <div v-else-if="filtered.length === 0" class="loading">No Pokémon match your filters.</div>

    <!-- Tier view -->
    <div v-else-if="viewMode === 'tier'" class="tier-view">
      <div v-for="group in tierGroups" :key="group.pts" class="tier-col">
        <div class="tier-col-header">
          <span class="tier-badge">{{ group.pts }} pts</span>
          <span class="tier-count">{{ group.pokemon.length }}</span>
        </div>
        <div class="tier-col-body">
          <div v-for="pokemon in group.pokemon" :key="pokemon.id" class="pokemon-entry">
            <PokemonCard
              :pokemon="pokemon"
              :point-value="pokemon.pointValue"
              mode="draft"
              :show-sprite="!xs"
              @click="openDetail(pokemon)"
            />
            <input
              v-if="authStore.isAdmin"
              type="number"
              min="0"
              :value="pokemon.pointValue || ''"
              placeholder="pts"
              class="pts-input native-field"
              @change="onPointInput(pokemon.id, $event)"
            />
          </div>
        </div>
      </div>
    </div>

    <!-- Grid view -->
    <div v-else class="pokemon-grid">
      <div v-for="pokemon in filtered" :key="pokemon.id" class="pokemon-entry">
        <PokemonCard
          :pokemon="pokemon"
          :point-value="pokemon.pointValue"
          mode="draft"
          :show-sprite="!xs"
          @click="openDetail(pokemon)"
        />
        <input
          v-if="authStore.isAdmin"
          type="number"
          min="0"
          :value="pokemon.pointValue || ''"
          placeholder="pts"
          class="pts-input native-field"
          @change="onPointInput(pokemon.id, $event)"
        />
      </div>
    </div>
  </v-container>

  <PokemonDetailModal
    v-if="detailPokemon"
    :pokemon="detailPokemon"
    :point-value="detailPokemon.pointValue"
    :can-draft="false"
    :is-picked="false"
    :show-draft-action="false"
    @close="closeDetail"
    @draft="closeDetail"
  />
</template>

<style scoped>
.pokemon-view {
  height: 100%;
  display: flex;
  flex-direction: column;
  padding: clamp(1rem, 2vw, 2rem);
}
.page-hero {
  margin-bottom: 12px;
  flex: 0 0 auto;
}
.hero-content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}
.hero-content h1 {
  margin-top: 3px;
  font-size: clamp(1.4rem, 3vw, 2rem);
  font-weight: 800;
}
.hero-content p {
  color: var(--text-muted);
  font-size: 0.82rem;
}

.admin-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.filter-card {
  flex: 0 0 auto;
  margin-bottom: 12px;
}
.filter-grid {
  display: grid;
  grid-template-columns: minmax(220px, 1.5fr) minmax(170px, 1fr) minmax(130px, 0.7fr) auto auto;
  align-items: center;
  gap: 10px;
}

/* ── Grid ────────────────────────────────────────────────────────────────── */
.loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 2rem;
  color: var(--text-muted);
}

.pokemon-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(95px, 1fr));
  gap: 0.45rem;
  padding: 0.75rem;
  overflow-y: auto;
  flex: 1;
}

.pokemon-entry {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.pts-input {
  width: 100%;
  text-align: center;
  padding: 0.15rem 0.3rem;
  font-size: 0.78rem;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 5px;
  color: var(--text);
}

/* ── Tier view ───────────────────────────────────────────────────────────── */
.tier-view {
  display: flex;
  flex-direction: row;
  gap: 0.65rem;
  overflow-x: auto;
  overflow-y: auto;
  padding: 0.75rem;
  flex: 1;
  align-items: flex-start;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.tier-col {
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  width: 108px;
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
  font-size: 0.82rem;
  font-weight: 800;
  color: var(--text);
  white-space: nowrap;
}

.tier-count {
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--text-muted);
  background: var(--input-bg);
  border-radius: 10px;
  padding: 0.1rem 0.4rem;
}

.tier-col-body {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

@media (max-width: 720px) {
  .pokemon-view {
    min-height: 0;
    padding: 12px;
  }

  .hero-content {
    align-items: stretch;
    flex-direction: column;
    gap: 8px;
  }

  .admin-actions {
    width: 100%;
  }

  .admin-actions .v-btn {
    flex: 1;
  }

  .filter-grid {
    grid-template-columns: 1fr 1fr;
  }

  .filter-grid > :first-child {
    grid-column: span 2;
  }

  .pokemon-grid {
    grid-template-columns: repeat(auto-fill, minmax(96px, 1fr));
    overflow-y: visible;
    padding: 0.5rem 0;
  }

  .tier-view {
    overflow-y: visible;
    padding: 0.5rem 0;
  }

  .pokemon-entry {
    gap: 0.25rem;
  }

  .point-input {
    height: 32px;
    font-size: 0.8rem;
  }
}
</style>
