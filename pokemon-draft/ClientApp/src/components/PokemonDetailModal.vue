<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { formatPokemonName, TYPE_COLORS } from '@/utils/format'
import type { Pokemon } from '@/types'

const props = withDefaults(
  defineProps<{
    pokemon: Pokemon
    pointValue?: number
    canDraft: boolean
    isPicked: boolean
    actionLabel?: string
    draftDisabledReason?: string
    showDraftAction?: boolean
    canCommissionerDraft?: boolean
    commissionerActionLabel?: string
  }>(),
  {
    showDraftAction: true,
  },
)

const emit = defineEmits<{
  close: []
  draft: [pokemonId: number]
  draftFor: [pokemonId: number]
}>()

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
  megaForms?: ApiMegaForm[]
}
interface ApiMegaForm {
  name: string
  label: string
  sprite: string | null
  types: string[]
  stats: ApiStat[]
  abilities: ApiAbility[]
}

interface StatEntry {
  label: string
  value: number
}

interface AbilityEntry {
  name: string
  isHidden: boolean
  description?: string
}

interface MoveEntry {
  id: string
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

interface MegaForm {
  name: string
  label: string
  sprite: string | null
  types: string[]
  stats: StatEntry[]
  abilities: AbilityEntry[]
  error: string | null
}

interface CachedDetail {
  detail: PokemonDetail
  megaForms: MegaForm[]
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
const detailCache = new Map<number, CachedDetail>()
const abilityDescriptionCache = new Map<string, string>()

const isDialogOpen = ref(true)
const detail = ref<PokemonDetail | null>(null)
const megaForms = ref<MegaForm[]>([])
const isLoading = ref(true)
const fetchError = ref<string | null>(null)
const moveFilter = ref('')
const activeTab = ref('base')

const tabs = computed(() => [
  { value: 'base', label: 'Base' },
  ...megaForms.value.map((form) => ({ value: form.name, label: form.label })),
])

const activeMegaForm = computed(() =>
  activeTab.value !== 'base'
    ? (megaForms.value.find((form) => form.name === activeTab.value) ?? null)
    : null,
)

const filteredMoves = computed(() => {
  const moves = detail.value?.moves ?? []
  const query = moveFilter.value.trim().toLowerCase()
  if (!query) return moves
  return moves.filter((move) => move.name.toLowerCase().includes(query))
})

const moveHeaders = [
  { title: 'Type', key: 'type', width: 92 },
  { title: 'Move', key: 'name' },
  { title: 'Cat', key: 'category', width: 84 },
  { title: 'Pow', key: 'power', align: 'end' as const, width: 70 },
  { title: 'PP', key: 'pp', align: 'end' as const, width: 70 },
]

function closeModal() {
  isDialogOpen.value = false
  emit('close')
}

function handleDraft() {
  emit('draft', props.pokemon.id)
}

function handleCommissionerDraft() {
  emit('draftFor', props.pokemon.id)
}

function statColor(value: number): string {
  if (value < 50) return '#ef4444'
  if (value < 80) return '#f97316'
  if (value < 100) return '#eab308'
  if (value < 120) return '#84cc16'
  return '#22c55e'
}

function parseMegaLabel(name: string): string {
  const index = name.indexOf('-mega')
  if (index === -1) return formatPokemonName(name)
  return name
    .slice(index + 1)
    .replace(/-/g, ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase())
}

function parseDetail(raw: ApiPokemonDetail): PokemonDetail {
  return {
    stats: raw.stats.map((stat) => ({
      label: STAT_LABELS[stat.name] ?? stat.name,
      value: stat.baseStat,
    })),
    abilities: raw.abilities.map((ability) => ({
      name: formatPokemonName(ability.name),
      isHidden: ability.isHidden,
    })),
    moves: raw.moves.map((move) => ({
      id: `${move.name}-${move.type}-${move.category}-${move.power ?? 'none'}-${move.pp ?? 'none'}`,
      name: formatPokemonName(move.name),
      type: move.type,
      power: move.power,
      pp: move.pp,
      category: move.category,
    })),
  }
}

function parseMegaForms(raw: ApiMegaForm[] | undefined): MegaForm[] {
  return (raw ?? []).map((form) => ({
    name: form.name,
    label: form.label,
    sprite: form.sprite,
    types: form.types,
    stats: form.stats.map((stat) => ({
      label: STAT_LABELS[stat.name] ?? stat.name,
      value: stat.baseStat,
    })),
    abilities: form.abilities.map((ability) => ({
      name: formatPokemonName(ability.name),
      isHidden: ability.isHidden,
    })),
    error: null,
  }))
}

async function fetchAbilityDescription(abilityName: string): Promise<string | undefined> {
  if (abilityDescriptionCache.has(abilityName)) return abilityDescriptionCache.get(abilityName)

  try {
    const slug = abilityName.toLowerCase().replace(/\s+/g, '-')
    const res = await fetch(`https://pokeapi.co/api/v2/ability/${slug}`)
    if (!res.ok) return undefined

    const json = await res.json()
    const entry = (
      json.flavor_text_entries as { flavor_text: string; language: { name: string } }[]
    ).find((item) => item.language.name === 'en')

    const description = entry?.flavor_text.replace(/\n|\f/g, ' ')
    if (description) abilityDescriptionCache.set(abilityName, description)
    return description
  } catch {
    return undefined
  }
}

async function hydrateAbilityDescriptions(abilityGroups: AbilityEntry[][]) {
  const abilities = abilityGroups
    .flat()
    .filter((ability, index, all) => all.findIndex((item) => item.name === ability.name) === index)

  await Promise.all(
    abilities.map(async (ability) => {
      ability.description = await fetchAbilityDescription(ability.name)
    }),
  )
}

async function fetchMegaNames(): Promise<string[]> {
  try {
    const res = await fetch(`https://pokeapi.co/api/v2/pokemon-species/${props.pokemon.speciesId}`)
    if (!res.ok) return []

    const json = await res.json()
    return (json.varieties as { pokemon: { name: string } }[])
      .map((variety) => variety.pokemon.name)
      .filter((name) => name.includes('-mega'))
  } catch {
    return []
  }
}

async function fetchMegaForm(name: string): Promise<MegaForm> {
  const form: MegaForm = {
    name,
    label: parseMegaLabel(name),
    sprite: null,
    types: [],
    stats: [],
    abilities: [],
    error: null,
  }

  try {
    const res = await fetch(`https://pokeapi.co/api/v2/pokemon/${name}`)
    if (!res.ok) throw new Error(`HTTP ${res.status}`)

    const json = await res.json()
    form.sprite =
      json.sprites.other?.home?.front_default ??
      json.sprites.other?.['official-artwork']?.front_default ??
      json.sprites.front_default
    form.types = (json.types as { type: { name: string } }[]).map((type) => type.type.name)
    form.stats = (json.stats as { stat: { name: string }; base_stat: number }[]).map((stat) => ({
      label: STAT_LABELS[stat.stat.name] ?? stat.stat.name,
      value: stat.base_stat,
    }))
    form.abilities = (json.abilities as { ability: { name: string }; is_hidden: boolean }[]).map(
      (ability) => ({
        name: formatPokemonName(ability.ability.name),
        isHidden: ability.is_hidden,
      }),
    )
  } catch {
    form.error = 'Failed to load mega evolution data.'
  }

  return form
}

async function loadModalData() {
  const cached = detailCache.get(props.pokemon.id)
  if (cached) {
    detail.value = cached.detail
    megaForms.value = cached.megaForms
    isLoading.value = false
    return
  }

  isLoading.value = true
  fetchError.value = null

  try {
    const detailResponse = await fetch(`/api/pokemon/${props.pokemon.id}/detail`)
    if (!detailResponse.ok) throw new Error(`HTTP ${detailResponse.status}`)

    const rawDetail = (await detailResponse.json()) as ApiPokemonDetail
    const parsedDetail = parseDetail(rawDetail)
    const cachedForms = parseMegaForms(rawDetail.megaForms)
    const forms =
      cachedForms.length > 0
        ? cachedForms
        : await Promise.all((await fetchMegaNames()).map(fetchMegaForm))

    await hydrateAbilityDescriptions([
      parsedDetail.abilities,
      ...forms.map((form) => form.abilities),
    ])

    detailCache.set(props.pokemon.id, { detail: parsedDetail, megaForms: forms })
    detail.value = parsedDetail
    megaForms.value = forms
  } catch (error) {
    console.error('PokemonDetailModal fetch error:', error)
    fetchError.value = 'Failed to load Pokemon details. Check your connection and try again.'
  } finally {
    isLoading.value = false
  }
}

onMounted(loadModalData)
</script>

<template>
  <v-dialog
    :model-value="isDialogOpen"
    max-width="550"
    scrollable
    @update:model-value="(value) => !value && closeModal()"
  >
    <v-card class="detail-card">
      <v-card-title class="detail-header">
        <img :src="pokemon.spriteUrl" :alt="formatPokemonName(pokemon.name)" class="modal-sprite" />
        <div class="modal-title">
          <span class="modal-id">#{{ String(pokemon.id).padStart(3, '0') }}</span>
          <span class="modal-name">{{ formatPokemonName(pokemon.name) }}</span>
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
        <v-btn icon="mdi-close" variant="text" density="comfortable" @click="closeModal" />
      </v-card-title>

      <v-divider />

      <v-tabs v-if="tabs.length > 1" v-model="activeTab" density="compact">
        <v-tab v-for="tab in tabs" :key="tab.value" :value="tab.value">
          {{ tab.label }}
        </v-tab>
      </v-tabs>
      <v-divider />

      <v-card-text class="detail-body">
        <div v-if="isLoading" class="state-message"></div>
        <div v-else-if="fetchError" class="state-message error-state">{{ fetchError }}</div>

        <template v-else-if="detail">
          <template v-if="activeTab === 'base'">
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

            <section class="detail-section">
              <h3>Abilities</h3>
              <div class="abilities">
                <v-tooltip
                  v-for="ability in detail.abilities"
                  :key="ability.name"
                  :text="ability.description || 'No description available.'"
                  location="top"
                >
                  <template #activator="{ props: tooltipProps }">
                    <v-chip
                      v-bind="tooltipProps"
                      :variant="ability.isHidden ? 'outlined' : 'tonal'"
                      size="small"
                    >
                      {{ ability.name }}
                      <em v-if="ability.isHidden">&nbsp;(Hidden)</em>
                    </v-chip>
                  </template>
                </v-tooltip>
              </div>
            </section>

            <section class="detail-section moves-section">
              <div class="moves-header">
                <h3>
                  Learnable Moves
                  <span class="move-count">({{ detail.moves.length }})</span>
                </h3>
                <v-text-field
                  v-model="moveFilter"
                  class="move-search"
                  variant="outlined"
                  density="compact"
                  hide-details
                  clearable
                  placeholder="Filter moves"
                />
              </div>

              <v-data-table
                :headers="moveHeaders"
                :items="filteredMoves"
                :items-per-page="-1"
                class="moves-table"
                density="compact"
                fixed-header
                height="300"
                hide-default-footer
                item-value="id"
              >
                <template #item.type="{ item }">
                  <span
                    class="type-badge"
                    :style="{ backgroundColor: TYPE_COLORS[item.type] ?? '#888' }"
                  >
                    {{ item.type }}
                  </span>
                </template>
                <template #item.category="{ item }">
                  <span
                    class="category-badge"
                    :style="{ backgroundColor: CATEGORY_COLOR[item.category] ?? '#64748b' }"
                  >
                    {{ CATEGORY_LABEL[item.category] ?? item.category }}
                  </span>
                </template>
                <template #item.power="{ item }">
                  {{ item.power ?? '-' }}
                </template>
                <template #item.pp="{ item }">
                  {{ item.pp ?? '-' }}
                </template>
                <template #no-data>
                  <div class="no-moves">No moves match the current filter.</div>
                </template>
              </v-data-table>
            </section>
          </template>

          <template v-else-if="activeMegaForm">
            <div v-if="activeMegaForm.error" class="state-message error-state">
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

              <section class="detail-section">
                <h3>Abilities</h3>
                <div class="abilities">
                  <v-tooltip
                    v-for="ability in activeMegaForm.abilities"
                    :key="ability.name"
                    :text="ability.description || 'No description available.'"
                    location="top"
                  >
                    <template #activator="{ props: tooltipProps }">
                      <v-chip
                        v-bind="tooltipProps"
                        :variant="ability.isHidden ? 'outlined' : 'tonal'"
                        size="small"
                      >
                        {{ ability.name }}
                        <em v-if="ability.isHidden">&nbsp;(Hidden)</em>
                      </v-chip>
                    </template>
                  </v-tooltip>
                </div>
              </section>
            </template>
          </template>
        </template>
      </v-card-text>

      <v-divider />

      <v-card-actions class="detail-actions">
        <v-btn class="btn-secondary" @click="closeModal">Close</v-btn>
        <template v-if="props.showDraftAction">
          <span v-if="isPicked" class="already-drafted">Already Drafted</span>
          <v-btn v-else class="btn-primary" :disabled="!canDraft" @click="handleDraft">
            {{ canDraft ? (actionLabel ?? 'Draft') : (draftDisabledReason ?? 'Not Your Turn') }}
          </v-btn>
        </template>
        <v-btn
          v-if="!isPicked && props.canCommissionerDraft"
          class="btn-primary"
          @click="handleCommissionerDraft"
        >
          {{ props.commissionerActionLabel ?? 'Draft for current player' }}
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.detail-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  max-height: 90vh;
}

.detail-header {
  display: flex;
  align-items: center;
  gap: 1rem;
  min-height: 120px;
  padding: 1rem 1.25rem;
}

.modal-sprite,
.mega-sprite {
  width: 96px;
  height: 96px;
  flex-shrink: 0;
}

.modal-title {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.modal-id {
  color: var(--text-muted);
  font-size: 0.72rem;
}

.modal-name {
  color: var(--text);
  font-size: 1.3rem;
  font-weight: 800;
  line-height: 1.2;
}

.modal-types {
  display: flex;
  flex-wrap: wrap;
  gap: 5px;
}

.type-badge,
.category-badge {
  border-radius: 4px;
  color: #fff;
  font-size: 0.65rem;
  font-weight: 700;
  padding: 2px 7px;
  text-transform: capitalize;
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.4);
  white-space: nowrap;
}

.category-badge {
  display: inline-block;
  min-width: 44px;
  text-align: center;
}

.modal-pts {
  color: var(--primary);
  font-size: 0.8rem;
  font-weight: 700;
}

.detail-body {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  min-height: 0;
}

.state-message {
  color: var(--text-muted);
  padding: 2rem 0;
  text-align: center;
}

.error-state {
  color: #f87171;
}

.detail-section h3 {
  color: var(--text-muted);
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.07em;
  margin-bottom: 0.6rem;
  text-transform: uppercase;
}

.stats-grid {
  align-items: center;
  display: grid;
  gap: 4px 8px;
  grid-template-columns: 5rem 2.5rem 1fr;
}

.stat-label {
  color: var(--text-muted);
  font-size: 0.75rem;
  font-weight: 600;
  text-align: right;
}

.stat-value {
  color: var(--text);
  font-size: 0.8rem;
  font-weight: 700;
  text-align: right;
}

.stat-bar-track {
  background: var(--input-bg);
  border-radius: 4px;
  height: 8px;
  overflow: hidden;
}

.stat-bar-fill {
  border-radius: 4px;
  height: 100%;
}

.abilities {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.moves-header {
  align-items: center;
  display: flex;
  gap: 0.75rem;
  justify-content: space-between;
  margin-bottom: 0.6rem;
}

.moves-header h3 {
  margin-bottom: 0;
}

.move-count {
  font-weight: 400;
  letter-spacing: 0;
  text-transform: none;
}

.move-search {
  max-width: 180px;
}

.moves-table {
  background: var(--bg);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  overflow: hidden;
}

.moves-table :deep(th) {
  background: var(--input-bg) !important;
  color: var(--text-muted) !important;
  font-size: 0.68rem;
  font-weight: 800 !important;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}

.moves-table :deep(td) {
  color: var(--text);
  font-size: 0.78rem;
}

.no-moves {
  color: var(--text-muted);
  padding: 0.75rem;
  text-align: center;
}

.mega-header {
  align-items: center;
  display: flex;
  gap: 1rem;
}

.detail-actions {
  justify-content: flex-end;
  padding: 0.85rem 1.25rem;
}

.already-drafted {
  color: var(--text-muted);
  font-size: 0.82rem;
  font-style: italic;
}

@media (max-width: 640px) {
  .detail-header {
    align-items: flex-start;
    gap: 0.75rem;
  }

  .modal-sprite {
    height: 72px;
    width: 72px;
  }

  .stats-grid {
    grid-template-columns: 4rem 2.5rem 1fr;
  }

  .moves-header {
    align-items: stretch;
    flex-direction: column;
  }

  .move-search {
    max-width: none;
  }
}
</style>
