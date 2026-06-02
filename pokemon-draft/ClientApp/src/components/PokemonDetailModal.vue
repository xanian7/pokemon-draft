<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import { formatPokemonName, TYPE_COLORS } from '@/utils/format'
import type { Pokemon } from '@/types'
import { mdiClose } from '@mdi/js'

const props = withDefaults(
  defineProps<{
    pokemon: Pokemon
    pointValue?: number
    canDraft: boolean
    isPicked: boolean
    /** Label for the primary action button. Defaults to 'Draft'. */
    actionLabel?: string
    /** Show the draft/picked action footer. Set false on non-draft screens. */
    showDraftAction?: boolean
  }>(),
  {
    showDraftAction: true,
  },
)

const emit = defineEmits<{
  close: []
  draft: [pokemonId: number]
}>()

// ── Backend DTO shapes ────────────────────────────────────────────────────────

interface ApiStat {
  name: string
  baseStat: number
}
interface ApiAbility {
  name: string
  isHidden: boolean
}
interface ApiMove {
  name: string
  type: string
  power: number | null
  pp: number | null
  category: string
}
interface ApiPokemonDetail {
  stats: ApiStat[]
  abilities: ApiAbility[]
  moves: ApiMove[]
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
    const entry = (
      json.flavor_text_entries as { flavor_text: string; language: { name: string } }[]
    ).find((e) => e.language.name === 'en')
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
  fetchMegaForms()
  if (detailCache.has(props.pokemon.id)) {
    detail.value = detailCache.get(props.pokemon.id)!
    isLoading.value = false
    return
  }

  try {
    const res = await fetch(`/api/pokemon/${props.pokemon.id}/detail`)
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    const raw: ApiPokemonDetail = await res.json()

    const parsed: PokemonDetail = {
      stats: raw.stats.map((s) => ({
        label: STAT_LABELS[s.name] ?? s.name,
        value: s.baseStat,
      })),
      abilities: raw.abilities.map((a) => ({
        name: formatPokemonName(a.name),
        isHidden: a.isHidden,
      })),
      moves: raw.moves.map((m) => ({
        name: formatPokemonName(m.name),
        type: m.type,
        power: m.power,
        pp: m.pp,
        category: m.category,
      })),
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
}

function filteredMoves(moves: MoveEntry[]): MoveEntry[] {
  const q = moveFilter.value.toLowerCase()
  return q ? moves.filter((m) => m.name.toLowerCase().includes(q)) : moves
}

// ── Mega evolution ─────────────────────────────────────────────────────────────

interface MegaForm {
  name: string
  label: string
  sprite: string | null
  types: string[]
  stats: StatEntry[]
  abilities: AbilityEntry[]
  isLoading: boolean
  error: string | null
}

const megaForms = ref<MegaForm[]>([])
const activeTab = ref<'base' | string>('base')

const activeMegaForm = computed(() =>
  activeTab.value !== 'base'
    ? (megaForms.value.find((f) => f.name === activeTab.value) ?? null)
    : null,
)

function parseMegaLabel(name: string): string {
  const idx = name.indexOf('-mega')
  if (idx === -1) return name
  return name
    .slice(idx + 1)
    .replace(/-/g, ' ')
    .replace(/\b\w/g, (c) => c.toUpperCase())
}

async function fetchMegaForms() {
  try {
    const res = await fetch(`https://pokeapi.co/api/v2/pokemon-species/${props.pokemon.speciesId}`)
    if (!res.ok) return
    const json = await res.json()
    const megaNames = (json.varieties as any[])
      .map((v: any) => v.pokemon.name as string)
      .filter((n: string) => n.includes('-mega'))

    megaForms.value = megaNames.map((name) => ({
      name,
      label: parseMegaLabel(name),
      sprite: null,
      types: [],
      stats: [],
      abilities: [],
      isLoading: false,
      error: null,
    }))
  } catch {
    // No megas available, tab won't show
  }
}

async function fetchMegaData(form: MegaForm) {
  if (form.stats.length > 0 || form.isLoading) return
  form.isLoading = true
  try {
    const res = await fetch(`https://pokeapi.co/api/v2/pokemon/${form.name}`)
    if (!res.ok) throw new Error()
    const json = await res.json()
    form.sprite = json.sprites.front_default
    form.types = (json.types as any[]).map((t: any) => t.type.name as string)
    form.stats = (json.stats as any[]).map((s: any) => ({
      label: STAT_LABELS[s.stat.name] ?? s.stat.name,
      value: s.base_stat as number,
    }))
    form.abilities = (json.abilities as any[]).map((a: any) => ({
      name: formatPokemonName(a.ability.name),
      isHidden: a.is_hidden as boolean,
    }))
  } catch {
    form.error = 'Failed to load mega evolution data.'
  } finally {
    form.isLoading = false
  }
}

function switchTab(tab: 'base' | string) {
  activeTab.value = tab
  if (tab !== 'base') {
    const form = megaForms.value.find((f) => f.name === tab)
    if (form) fetchMegaData(form)
  }
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

        <!-- Tabs (shown only when mega forms exist) -->
        <div v-if="megaForms.length > 0" class="modal-tabs">
          <button
            class="modal-tab"
            :class="{ 'modal-tab--active': activeTab === 'base' }"
            @click="switchTab('base')"
          >
            Base
          </button>
          <button
            v-for="form in megaForms"
            :key="form.name"
            class="modal-tab"
            :class="{ 'modal-tab--active': activeTab === form.name }"
            @click="switchTab(form.name)"
          >
            {{ form.label }}
          </button>
        </div>

        <!-- Body -->
        <div class="modal-body">
          <!-- Base tab -->
          <template v-if="activeTab === 'base'">
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
          </template>

          <!-- Mega tab -->
          <template v-else-if="activeMegaForm">
            <div v-if="activeMegaForm.isLoading" class="loading-state">
              <PokeballLoader variant="page" label="Loading mega data…" />
            </div>
            <div v-else-if="activeMegaForm.error" class="error-state">
              {{ activeMegaForm.error }}
            </div>
            <template v-else>
              <div class="mega-header">
                <img
                  v-if="activeMegaForm.sprite"
                  :src="activeMegaForm.sprite"
                  :alt="activeMegaForm.label"
                  class="mega-sprite"
                />
                <div class="modal-types">
                  <span
                    v-for="type in activeMegaForm.types"
                    :key="type"
                    class="type-badge"
                    :style="{ backgroundColor: TYPE_COLORS[type] ?? '#888' }"
                  >
                    {{ type }}
                  </span>
                </div>
              </div>

              <!-- Mega Stats -->
              <section class="detail-section">
                <h3>Base Stats</h3>
                <div class="stats-grid">
                  <template v-for="stat in activeMegaForm.stats" :key="stat.label">
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

              <!-- Mega Ability -->
              <section class="detail-section">
                <h3>Ability</h3>
                <div class="abilities">
                  <span
                    v-for="ability in activeMegaForm.abilities"
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
            </template>
          </template>
        </div>

        <!-- Footer -->
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="emit('close')">Close</button>
          <template v-if="props.showDraftAction">
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

/* ── Tabs ── */
.modal-tabs {
  display: flex;
  border-bottom: 1px solid var(--border-color);
  padding: 0 1.25rem;
  flex-shrink: 0;
  background: var(--card-bg);
}

.modal-tab {
  padding: 0.45rem 1rem;
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  color: var(--text-muted);
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
  transition:
    color 0.15s,
    border-color 0.15s;
  margin-bottom: -1px;
}

.modal-tab:hover {
  color: var(--text);
}

.modal-tab--active {
  color: var(--primary);
  border-bottom-color: var(--primary);
}

/* ── Mega tab content ── */
.mega-header {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-shrink: 0;
}

.mega-sprite {
  width: 96px;
  height: 96px;
  image-rendering: pixelated;
  flex-shrink: 0;
}
</style>
