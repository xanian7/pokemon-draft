<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import { formatPokemonName, TYPE_COLORS } from '@/utils/format'
import type { Pokemon } from '@/types'
import { mdiClose } from '@mdi/js'

const props = defineProps<{
  pokemon: Pokemon
  pointValue?: number
  canDraft: boolean
  isPicked: boolean
  /** Label for the primary action button. Defaults to 'Draft'. */
  actionLabel?: string
  /** Hide the draft/picked action entirely (e.g. on the Point Values page). Defaults to true. */
  showDraftAction?: boolean
}>()

const emit = defineEmits<{
  close: []
  draft: [pokemonId: number]
}>()

// ── GraphQL types ─────────────────────────────────────────────────────────────

interface GqlStat {
  base_stat: number
  pokemon_v2_stat: { name: string }
}

interface GqlAbility {
  is_hidden: boolean
  pokemon_v2_ability: { name: string }
}

interface GqlMove {
  pokemon_v2_move: {
    name: string
    power: number | null
    pp: number | null
    pokemon_v2_type: { name: string } | null
    pokemon_v2_movedamageclass: { name: string } | null
  }
}

interface GqlPokemon {
  pokemon_v2_pokemonstats: GqlStat[]
  pokemon_v2_pokemonabilities: GqlAbility[]
  pokemon_v2_pokemonmoves: GqlMove[]
}

interface GqlResponse {
  errors?: { message: string }[]
  data: {
    pokemon_v2_pokemon_by_pk: GqlPokemon | null
  }
}

// ── Parsed detail ─────────────────────────────────────────────────────────────

interface StatEntry {
  label: string
  value: number
}

interface AbilityEntry {
  name: string
  isHidden: boolean
}

interface MoveEntry {
  name: string
  type: string
  power: number | null
  pp: number | null
  category: string
}

interface PokemonDetail {
  stats: StatEntry[]
  abilities: AbilityEntry[]
  moves: MoveEntry[]
}

const STAT_LABELS: Record<string, string> = {
  hp: 'HP',
  attack: 'Atk',
  defense: 'Def',
  'special-attack': 'Sp.Atk',
  'special-defense': 'Sp.Def',
  speed: 'Speed',
}

const CATEGORY_LABEL: Record<string, string> = {
  physical: 'Phys',
  special: 'Spec',
  status: 'Status',
}

const CATEGORY_COLOR: Record<string, string> = {
  physical: '#c2410c',
  special: '#4f46e5',
  status: '#64748b',
}

const GRAPHQL_QUERY = `
  query PokemonDetail($id: Int!) {
    pokemon_v2_pokemon_by_pk(id: $id) {
      pokemon_v2_pokemonstats {
        base_stat
        pokemon_v2_stat { name }
      }
      pokemon_v2_pokemonabilities {
        is_hidden
        pokemon_v2_ability { name }
      }
      pokemon_v2_pokemonmoves(distinct_on: move_id) {
        pokemon_v2_move {
          name
          power
          pp
          pokemon_v2_type { name }
          pokemon_v2_movedamageclass { name }
        }
      }
    }
  }
`

const STAT_MAX = 255

// Module-level cache — persists across modal open/close without re-fetching
const detailCache = new Map<number, PokemonDetail>()

const detail = ref<PokemonDetail | null>(null)
const isLoading = ref(true)
const fetchError = ref<string | null>(null)
const moveFilter = ref('')
const abilityDescriptions = ref(new Map<string, string>())
const abilityFetching = new Set<string>()

const tooltip = ref<{ text: string; x: number; y: number } | null>(null)

async function fetchAbilityDescription(abilityName: string) {
  if (abilityFetching.has(abilityName)) return
  abilityFetching.add(abilityName)
  try {
    const slug = abilityName.toLowerCase().replace(/\s+/g, '-')
    const res = await fetch(`https://pokeapi.co/api/v2/ability/${slug}`)
    if (!res.ok) return
    const json = await res.json()
    const entry = (json.flavor_text_entries as { flavor_text: string; language: { name: string } }[])
      .find((e) => e.language.name === 'en')
    if (entry) {
      abilityDescriptions.value = new Map(abilityDescriptions.value).set(
        abilityName,
        entry.flavor_text.replace(/\n|\f/g, ' '),
      )
    }
  } catch {
    // silently ignore
  }
}

function showTooltip(e: MouseEvent, abilityName: string) {
  fetchAbilityDescription(abilityName)
  const desc = abilityDescriptions.value.get(abilityName)
  if (!desc) {
    // wait for fetch then show
    const unwatch = setInterval(() => {
      const d = abilityDescriptions.value.get(abilityName)
      if (d) {
        clearInterval(unwatch)
        const rect = (e.target as HTMLElement).getBoundingClientRect()
        tooltip.value = { text: d, x: rect.left + rect.width / 2, y: rect.top }
      }
    }, 100)
    return
  }
  const rect = (e.target as HTMLElement).getBoundingClientRect()
  tooltip.value = { text: desc, x: rect.left + rect.width / 2, y: rect.top }
}

function hideTooltip() {
  tooltip.value = null
}

function statColor(value: number): string {
  if (value < 50) return '#ef4444'
  if (value < 80) return '#f97316'
  if (value < 100) return '#eab308'
  if (value < 120) return '#84cc16'
  return '#22c55e'
}

onMounted(async () => {
  if (detailCache.has(props.pokemon.id)) {
    detail.value = detailCache.get(props.pokemon.id)!
    isLoading.value = false
    return
  }

  try {
    const res = await fetch('https://beta.pokeapi.co/graphql/v1beta', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ query: GRAPHQL_QUERY, variables: { id: props.pokemon.id } }),
    })
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    const json: GqlResponse = await res.json()
    if (json.errors) throw new Error(JSON.stringify(json.errors))
    const raw = json.data?.pokemon_v2_pokemon_by_pk
    if (!raw) throw new Error('Not found')

    const parsed: PokemonDetail = {
      stats: raw.pokemon_v2_pokemonstats.map((s) => ({
        label: STAT_LABELS[s.pokemon_v2_stat.name] ?? s.pokemon_v2_stat.name,
        value: s.base_stat,
      })),
      abilities: raw.pokemon_v2_pokemonabilities.map((a) => ({
        name: formatPokemonName(a.pokemon_v2_ability.name),
        isHidden: a.is_hidden,
      })),
      moves: raw.pokemon_v2_pokemonmoves
        .map((m) => ({
          name: formatPokemonName(m.pokemon_v2_move.name),
          type: m.pokemon_v2_move.pokemon_v2_type?.name ?? 'normal',
          power: m.pokemon_v2_move.power,
          pp: m.pokemon_v2_move.pp,
          category: m.pokemon_v2_move.pokemon_v2_movedamageclass?.name ?? 'status',
        }))
        .sort((a, b) => a.name.localeCompare(b.name)),
    }

    detailCache.set(props.pokemon.id, parsed)
    detail.value = parsed
  } catch (err) {
    console.error('PokemonDetailModal fetch error:', err)
    fetchError.value = 'Failed to load Pokémon details. Check your connection and try again.'
  } finally {
    isLoading.value = false
  }
})

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') emit('close')
}

onMounted(() => window.addEventListener('keydown', handleKeydown))
onUnmounted(() => window.removeEventListener('keydown', handleKeydown))

function handleDraft() {
  emit('draft', props.pokemon.id)
  emit('close')
}

function filteredMoves(moves: MoveEntry[]): MoveEntry[] {
  const q = moveFilter.value.toLowerCase()
  return q ? moves.filter((m) => m.name.toLowerCase().includes(q)) : moves
}
</script>

<template>
  <Teleport to="body">
    <div class="modal-backdrop" @click.self="emit('close')">
      <div class="modal" role="dialog" :aria-label="`${formatPokemonName(pokemon.name)} details`">
        <!-- Header -->
        <div class="modal-header">
          <img
            :src="pokemon.spriteUrl"
            :alt="formatPokemonName(pokemon.name)"
            class="modal-sprite"
          />
          <div class="modal-title">
            <span class="modal-id">#{{ String(pokemon.id).padStart(3, '0') }}</span>
            <h2>{{ formatPokemonName(pokemon.name) }}</h2>
            <div class="modal-types">
              <span
                v-for="type in pokemon.types"
                :key="type"
                class="type-badge"
                :style="{ backgroundColor: TYPE_COLORS[type] ?? '#888' }"
              >
                {{ type }}
              </span>
            </div>
            <span v-if="pointValue !== undefined" class="modal-pts">{{ pointValue }} pts</span>
          </div>
          <button class="close-btn" aria-label="Close" @click="emit('close')">
            <AppIcon :path="mdiClose" :size="20" />
          </button>
        </div>

        <!-- Body -->
        <div class="modal-body">
          <div v-if="isLoading" class="loading-state">
            <PokeballLoader variant="page" label="Loading details…" />
          </div>
          <div v-else-if="fetchError" class="error-state">{{ fetchError }}</div>
          <template v-else-if="detail">
            <!-- Base Stats -->
            <section class="detail-section">
              <h3>Base Stats</h3>
              <div class="stats-grid">
                <template v-for="stat in detail.stats" :key="stat.label">
                  <span class="stat-label">{{ stat.label }}</span>
                  <span class="stat-value">{{ stat.value }}</span>
                  <div class="stat-bar-track">
                    <div
                      class="stat-bar-fill"
                      :style="{
                        width: `${(stat.value / STAT_MAX) * 100}%`,
                        backgroundColor: statColor(stat.value),
                      }"
                    />
                  </div>
                </template>
              </div>
            </section>

            <!-- Abilities -->
            <section class="detail-section">
              <h3>Abilities</h3>
              <div class="abilities">
                <span
                  v-for="ability in detail.abilities"
                  :key="ability.name"
                  class="ability-chip"
                  :class="{ hidden: ability.isHidden }"
                  @mouseenter="showTooltip($event, ability.name)"
                  @mouseleave="hideTooltip"
                >
                  {{ ability.name }}
                  <em v-if="ability.isHidden">(Hidden)</em>
                </span>
              </div>
            </section>

            <!-- Moves -->
            <section class="detail-section">
              <div class="moves-header">
                <h3>
                  Learnable Moves
                  <span class="move-count">({{ detail.moves.length }})</span>
                </h3>
                <input
                  v-model="moveFilter"
                  class="move-search"
                  type="text"
                  placeholder="Filter moves…"
                />
              </div>
              <div class="moves-table">
                <div class="moves-table-header">
                  <span>Type</span>
                  <span>Move</span>
                  <span>Cat</span>
                  <span class="col-right">Pow</span>
                  <span class="col-right">PP</span>
                </div>
                <div class="moves-table-rows">
                  <div
                    v-for="move in filteredMoves(detail.moves)"
                    :key="move.name"
                    class="moves-table-row"
                  >
                    <span
                      class="type-badge"
                      :style="{ backgroundColor: TYPE_COLORS[move.type] ?? '#888' }"
                    >
                      {{ move.type }}
                    </span>
                    <span class="move-name">{{ move.name }}</span>
                    <span
                      class="category-badge"
                      :style="{ backgroundColor: CATEGORY_COLOR[move.category] ?? '#64748b' }"
                    >
                      {{ CATEGORY_LABEL[move.category] ?? move.category }}
                    </span>
                    <span class="col-right stat-val">{{ move.power ?? '—' }}</span>
                    <span class="col-right stat-val">{{ move.pp ?? '—' }}</span>
                  </div>
                  <div v-if="filteredMoves(detail.moves).length === 0" class="no-moves">
                    No moves match "{{ moveFilter }}"
                  </div>
                </div>
              </div>
            </section>
          </template>
        </div>

        <!-- Footer -->
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="emit('close')">Close</button>
          <template v-if="props.showDraftAction !== false">
            <span v-if="isPicked" class="already-drafted">Already Drafted</span>
            <button v-else class="btn btn-primary" :disabled="!canDraft" @click="handleDraft">
              {{ canDraft ? (actionLabel ?? 'Draft') : 'Not Your Turn' }}
            </button>
          </template>
        </div>
      </div>
    </div>

    <!-- Ability tooltip rendered at body level to avoid overflow clipping -->
    <Transition name="tooltip-fade">
      <div
        v-if="tooltip"
        class="ability-tooltip"
        :style="{ left: `${tooltip.x}px`, top: `${tooltip.y}px` }"
      >
        {{ tooltip.text }}
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.modal-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.65);
  backdrop-filter: blur(3px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 1rem;
}

.modal {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 14px;
  width: 100%;
  max-width: 540px;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  box-shadow: 0 24px 64px rgba(0, 0, 0, 0.6);
}

/* ── Header ── */
.modal-header {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 1.1rem 1.25rem;
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}

.modal-sprite {
  width: 96px;
  height: 96px;
  image-rendering: pixelated;
  flex-shrink: 0;
}

.modal-title {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.modal-id {
  font-size: 0.72rem;
  color: var(--text-muted);
}

.modal-title h2 {
  font-size: 1.3rem;
  font-weight: 800;
  color: var(--text);
  margin: 0;
}

.modal-types {
  display: flex;
  gap: 5px;
  flex-wrap: wrap;
}

.type-badge {
  font-size: 0.65rem;
  color: #fff;
  padding: 2px 7px;
  border-radius: 4px;
  text-transform: capitalize;
  font-weight: 700;
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.4);
}

.modal-pts {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--primary);
}

.close-btn {
  background: none;
  border: none;
  color: var(--text-muted);
  cursor: pointer;
  padding: 4px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  flex-shrink: 0;
  transition: color 0.15s;
  align-self: flex-start;
}

.close-btn:hover {
  color: var(--text);
}

/* ── Body ── */
.modal-body {
  flex: 1;
  overflow: hidden;
  padding: 1.1rem 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.loading-state,
.error-state {
  color: var(--text-muted);
  font-size: 0.9rem;
  text-align: center;
  padding: 2rem 0;
}

.error-state {
  color: #f87171;
}

/* ── Detail sections ── */
.detail-section h3 {
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.07em;
  color: var(--text-muted);
  margin-bottom: 0.6rem;
}

.move-count {
  font-weight: 400;
  text-transform: none;
  letter-spacing: 0;
}

/* Base stats */
.stats-grid {
  display: grid;
  grid-template-columns: 5rem 2.5rem 1fr;
  align-items: center;
  gap: 4px 8px;
}

.stat-label {
  font-size: 0.75rem;
  color: var(--text-muted);
  font-weight: 600;
  text-align: right;
}

.stat-value {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--text);
  text-align: right;
}

.stat-bar-track {
  height: 8px;
  background: var(--input-bg);
  border-radius: 4px;
  overflow: hidden;
}

.stat-bar-fill {
  height: 100%;
  border-radius: 4px;
  transition: width 0.4s ease;
}

/* Abilities */
.abilities {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.ability-chip {
  position: relative;
  font-size: 0.78rem;
  font-weight: 600;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  color: var(--text);
  padding: 3px 10px;
  border-radius: 20px;
  cursor: help;
}

.ability-tooltip {
  position: fixed;
  transform: translate(-50%, calc(-100% - 8px));
  background: rgba(0, 0, 0, 0.92);
  color: #fff;
  font-size: 0.72rem;
  font-weight: 400;
  line-height: 1.45;
  white-space: normal;
  width: 220px;
  padding: 7px 11px;
  border-radius: 7px;
  pointer-events: none;
  z-index: 9999;
  text-align: center;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.5);
}

.tooltip-fade-enter-active,
.tooltip-fade-leave-active {
  transition: opacity 0.15s;
}
.tooltip-fade-enter-from,
.tooltip-fade-leave-to {
  opacity: 0;
}

.ability-chip.hidden {
  border-style: dashed;
  color: var(--text-muted);
}

.ability-chip em {
  font-style: italic;
  font-weight: 400;
  margin-left: 3px;
  color: var(--text-muted);
}

/* Moves */
.moves-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  margin-bottom: 0.6rem;
}

.moves-header h3 {
  margin-bottom: 0;
}

.move-search {
  font-size: 0.75rem;
  padding: 0.25rem 0.6rem;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  color: var(--text);
  border-radius: 6px;
  width: 130px;
}

.move-search:focus {
  outline: none;
  border-color: var(--primary);
}

.moves-table {
  border: 1px solid var(--border-color);
  border-radius: 8px;
  overflow: hidden;
}

.moves-table-rows {
  max-height: 220px;
  overflow-y: auto;
}

.moves-table-header,
.moves-table-row {
  display: grid;
  grid-template-columns: 64px 1fr 52px 36px 36px;
  align-items: center;
  gap: 0.5rem;
  padding: 0.3rem 0.6rem;
  font-size: 0.75rem;
}

.moves-table-header {
  background: var(--input-bg);
  font-size: 0.65rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-muted);
  border-bottom: 1px solid var(--border-color);
}

.moves-table-row {
  border-bottom: 1px solid var(--border-color);
  color: var(--text);
}

.moves-table-row:last-child {
  border-bottom: none;
}

.moves-table-row:nth-child(even) {
  background: rgba(255, 255, 255, 0.02);
}

.category-badge {
  font-size: 0.6rem;
  color: #fff;
  padding: 1px 5px;
  border-radius: 4px;
  font-weight: 700;
  text-align: center;
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.4);
  white-space: nowrap;
}

.move-name {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.col-right {
  text-align: right;
}

.stat-val {
  color: var(--text-muted);
  font-weight: 600;
}

.no-moves {
  padding: 0.75rem;
  text-align: center;
  color: var(--text-muted);
  font-style: italic;
}

/* ── Footer ── */
.modal-footer {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.75rem;
  padding: 0.85rem 1.25rem;
  border-top: 1px solid var(--border-color);
  flex-shrink: 0;
}

.btn {
  font-size: 0.85rem;
  font-weight: 700;
  padding: 0.45rem 1.1rem;
  border-radius: 8px;
  border: none;
  cursor: pointer;
  transition: opacity 0.15s;
}

.btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.btn-secondary {
  background: var(--input-bg);
  color: var(--text);
  border: 1px solid var(--border-color);
}

.btn-secondary:hover:not(:disabled) {
  border-color: var(--text-muted);
}

.btn-primary {
  background: var(--primary);
  color: #fff;
}

.btn-primary:hover:not(:disabled) {
  opacity: 0.85;
}

.already-drafted {
  font-size: 0.82rem;
  color: var(--text-muted);
  font-style: italic;
}

</style>
