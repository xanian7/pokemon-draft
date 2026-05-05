<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { usePokemonStore } from '@/stores/pokemon'
import { useAuthStore } from '@/stores/auth'
import { formatPokemonName } from '@/utils/format'
import { REGULATIONS, getRegulation } from '@/data/regulations'
import PokemonCard from '@/components/PokemonCard.vue'

const pokemonStore = usePokemonStore()
const authStore = useAuthStore()

const API_BASE = 'http://localhost:5050/api'

const searchQuery = ref('')
const selectedRegulation = ref('national')
const selectedType = ref('')
const showOnlyValued = ref(false)
const legalIds = ref<Set<number> | null>(null)
const regulationLoading = ref(false)
const regulationError = ref('')
const PAGE_SIZE = 60
const page = ref(1)

const saving = ref(false)
const saveError = ref('')
const saveSuccess = ref(false)

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
    if (q && !p.name.includes(q) && !formatPokemonName(p.name).toLowerCase().includes(q)) return false
    if (selectedRegulation.value !== 'national') {
      const reg = getRegulation(selectedRegulation.value)
      if (reg.isLegal) {
        if (!reg.isLegal(p.id)) return false
      } else if (legalIds.value) {
        if (!legalIds.value.has(p.id)) return false
      }
    }
    if (selectedType.value && !p.types.includes(selectedType.value)) return false
    if (showOnlyValued.value && p.pointValue <= 0) return false
    return true
  })
})

const paginated = computed(() => filtered.value.slice(0, page.value * PAGE_SIZE))
const hasMore = computed(() => paginated.value.length < filtered.value.length)
const valuedCount = computed(() => Object.keys(pokemonStore.pointValues).length)

function onSearch() {
  page.value = 1
}

async function onRegulationChange() {
  onSearch()
  legalIds.value = null
  regulationError.value = ''

  const regulation = getRegulation(selectedRegulation.value)
  if (!regulation.fetchLegalIds) return

  regulationLoading.value = true
  try {
    legalIds.value = await regulation.fetchLegalIds()
  } catch (error) {
    regulationError.value = error instanceof Error ? error.message : 'Failed to load regulation set.'
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
        ? pokemonStore.allPokemon.filter((pokemon) => regulation.isLegal?.(pokemon.id)).map((pokemon) => pokemon.id)
        : legalIds.value
          ? Array.from(legalIds.value)
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
  <main class="pokemon-view">
    <div class="header">
      <h1>📋 Pokémon Point Values</h1>
      <span class="meta">{{ valuedCount }} / {{ pokemonStore.allPokemon.length }} valued</span>
      <div v-if="authStore.isAdmin" class="save-row">
        <button class="btn-defaults" :disabled="pokemonStore.isLoading || regulationLoading" @click="applyDefaults">
          ⚡ Apply Defaults
        </button>
        <button class="btn-save" :disabled="saving" @click="saveToServer">
          {{ saving ? 'Saving…' : '💾 Save to League' }}
        </button>
        <span v-if="saveSuccess" class="save-ok">✓ Saved!</span>
        <span v-if="saveError" class="save-err">{{ saveError }}</span>
      </div>
    </div>

    <div v-if="pokemonStore.isLoading" class="loading">
      <span class="spinner" />
      Loading Pokémon data…
    </div>

    <div v-else-if="pokemonStore.error" class="error">
      {{ pokemonStore.error }}
      <button @click="pokemonStore.fetchAllPokemon()">Retry</button>
    </div>

    <template v-else>
      <div class="filters">
        <input
          v-model="searchQuery"
          type="text"
          placeholder="Search by name…"
          class="search-input"
          @input="onSearch"
        />
        <select v-model="selectedRegulation" @change="onRegulationChange">
          <option v-for="reg in REGULATIONS" :key="reg.id" :value="reg.id">{{ reg.label }}</option>
        </select>
        <select v-model="selectedType" @change="onSearch">
          <option value="">All Types</option>
          <option v-for="type in pokemonStore.allTypes" :key="type" :value="type">
            {{ type.charAt(0).toUpperCase() + type.slice(1) }}
          </option>
        </select>
        <label class="toggle">
          <input v-model="showOnlyValued" type="checkbox" @change="onSearch" />
          Valued only
        </label>
        <span v-if="regulationLoading" class="filter-status">Loading regulation…</span>
        <span v-else-if="regulationError" class="filter-status filter-error">{{ regulationError }}</span>
        <span class="result-count">{{ filtered.length }} Pokémon</span>
      </div>

      <div class="pokemon-grid">
        <div
          v-for="pokemon in paginated"
          :key="pokemon.id"
          class="pokemon-entry"
        >
          <PokemonCard :pokemon="pokemon" :point-value="pokemon.pointValue" mode="browse" />
          <div class="point-input-row">
            <label :for="`pts-${pokemon.id}`">Points</label>
            <input
              :id="`pts-${pokemon.id}`"
              type="number"
              min="0"
              :value="pokemon.pointValue || ''"
              placeholder="0"
              @change="onPointInput(pokemon.id, $event)"
            />
          </div>
        </div>
      </div>

      <div v-if="hasMore" class="load-more">
        <button @click="page++">Load More ({{ filtered.length - paginated.length }} remaining)</button>
      </div>

      <div v-if="filtered.length === 0" class="empty">
        No Pokémon match your filters.
      </div>
    </template>
  </main>
</template>

<style scoped>
.pokemon-view {
  max-width: 1100px;
  margin: 0 auto;
  padding: 2rem 1rem;
}

.header {
  display: flex;
  align-items: baseline;
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.header h1 {
  font-size: 1.6rem;
  font-weight: 800;
  margin: 0;
}

.meta {
  color: var(--text-muted);
  font-size: 0.9rem;
}

.loading,
.error {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 2rem;
  color: var(--text-muted);
}

.spinner {
  width: 24px;
  height: 24px;
  border: 3px solid var(--border-color);
  border-top-color: var(--primary);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
  flex-shrink: 0;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

.filters {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
  align-items: center;
  margin-bottom: 1.25rem;
}

.search-input {
  flex: 1;
  min-width: 180px;
}

input[type='text'],
input[type='number'],
select {
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  color: var(--text);
  border-radius: 6px;
  padding: 0.4rem 0.65rem;
  font-size: 0.9rem;
}

.toggle {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.88rem;
  color: var(--text-muted);
  cursor: pointer;
}

.filter-status {
  font-size: 0.82rem;
  color: var(--text-muted);
}

.filter-error {
  color: #f87171;
}

.result-count {
  font-size: 0.85rem;
  color: var(--text-muted);
  margin-left: auto;
}

.pokemon-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  gap: 0.85rem;
}

.pokemon-entry {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.point-input-row {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.point-input-row label {
  font-size: 0.72rem;
  color: var(--text-muted);
  flex-shrink: 0;
}

.point-input-row input {
  width: 100%;
  text-align: center;
  padding: 0.25rem 0.4rem;
  font-size: 0.85rem;
}

.load-more {
  margin-top: 1.5rem;
  text-align: center;
}

.load-more button {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  color: var(--text);
  border-radius: 8px;
  padding: 0.6rem 1.5rem;
  cursor: pointer;
  font-size: 0.9rem;
}

.load-more button:hover {
  border-color: var(--primary);
}

.empty {
  text-align: center;
  color: var(--text-muted);
  padding: 3rem;
}

.save-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-left: auto;
}

.btn-save,
.btn-defaults {
  color: white;
  border: none;
  border-radius: 6px;
  padding: 0.45rem 1.1rem;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-save {
  background: var(--primary);
}

.btn-defaults {
  background: var(--secondary);
}

.btn-save:disabled,
.btn-defaults:disabled { opacity: 0.5; cursor: not-allowed; }

.save-ok { color: #4ade80; font-size: 0.88rem; }
.save-err { color: #f87171; font-size: 0.88rem; }
</style>
