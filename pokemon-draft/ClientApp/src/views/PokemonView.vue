<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import PokemonCard from '@/components/PokemonCard.vue'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import { REGULATIONS, getRegulation } from '@/data/regulations'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import { API_BASE } from '@/services/signalr'
import { formatPokemonName } from '@/utils/format'
import {
  mdiClipboardList,
  mdiFlash,
  mdiContentSave,
  mdiCheck,
  mdiViewGrid,
  mdiViewColumn,
} from '@mdi/js'
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
  <main class="pokemon-view page">
    <!-- Top bar -->
    <div class="view-header page-header">
      <span class="view-title">
        <AppIcon :path="mdiClipboardList" :size="20" />
        {{ authStore.isAdmin ? 'Pokémon Point Values' : 'Free Agents' }}
      </span>
      <span class="meta">{{ valuedCount }} valued · {{ filtered.length }} shown</span>

      <div v-if="authStore.isAdmin" class="admin-actions">
        <button
          class="btn btn-sm"
          style="background: var(--secondary); color: #fff"
          :disabled="pokemonStore.isLoading || regulationLoading"
          @click="applyDefaults"
        >
          <AppIcon :path="mdiFlash" :size="16" />
          Apply Defaults
        </button>
        <button class="btn btn-primary btn-sm" :disabled="saving" @click="saveToServer">
          <template v-if="saving">Saving…</template>
          <template v-else>
            <AppIcon :path="mdiContentSave" :size="16" />
            Save to League
          </template>
        </button>
        <span v-if="saveSuccess" class="save-ok">
          <AppIcon :path="mdiCheck" :size="14" /> Saved!
        </span>
        <span v-if="saveError" class="save-err">{{ saveError }}</span>
      </div>
    </div>

    <!-- Filter bar -->
    <div class="filter-bar">
      <input v-model="searchQuery" type="text" placeholder="Search by name…" class="search-input" />
      <select v-model="selectedRegulation" @change="onRegulationChange">
        <option v-for="reg in REGULATIONS" :key="reg.id" :value="reg.id">{{ reg.label }}</option>
      </select>
      <select v-model="selectedType">
        <option value="">All Types</option>
        <option v-for="type in pokemonStore.allTypes" :key="type" :value="type">
          {{ type.charAt(0).toUpperCase() + type.slice(1) }}
        </option>
      </select>
      <label class="toggle">
        <input v-model="showOnlyValued" type="checkbox" />
        Valued only
      </label>
      <span v-if="regulationLoading" class="filter-status">Loading regulation…</span>
      <span v-else-if="regulationError" class="filter-status filter-error">{{
        regulationError
      }}</span>
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

    <!-- Grid -->
    <div v-if="pokemonStore.isLoading" class="loading">
      <PokeballLoader variant="page" label="Loading Pokémon data…" />
    </div>
    <div v-else-if="pokemonStore.error" class="loading">
      {{ pokemonStore.error }}
      <button @click="pokemonStore.fetchAllPokemon()">Retry</button>
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
              class="pts-input"
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
          class="pts-input"
          @change="onPointInput(pokemon.id, $event)"
        />
      </div>
    </div>
  </main>

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
/* ── Top bar ─────────────────────────────────────────────────────────────── */
.view-title {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.95rem;
  font-weight: 700;
}

.meta {
  font-size: 0.8rem;
  color: var(--text-muted);
}

.admin-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-left: auto;
}

.save-ok {
  color: #4ade80;
  font-size: 0.82rem;
  display: flex;
  align-items: center;
  gap: 0.3rem;
}
.save-err {
  color: #f87171;
  font-size: 0.82rem;
}

/* ── Filter bar ──────────────────────────────────────────────────────────── */
.filter-bar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
  flex-wrap: wrap;
  background: var(--bg);
}

.search-input {
  flex: 1;
  min-width: 140px;
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
.filter-error {
  color: #f87171;
}

/* ── Grid ────────────────────────────────────────────────────────────────── */
.loading {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 2rem;
  color: var(--text-muted);
}

.loading {
  display: flex;
  justify-content: center;
  padding: 2rem;
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

/* ── View toggle ─────────────────────────────────────────────────────────── */
.view-toggle {
  display: flex;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  overflow: hidden;
  margin-left: auto;
  flex-shrink: 0;
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
  }

  .view-header,
  .filter-bar {
    align-items: stretch;
    gap: 8px;
  }

  .filter-bar > * {
    min-width: 0;
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
