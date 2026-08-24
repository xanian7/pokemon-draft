<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { API_BASE, useSignalR } from '@/services/signalr'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import PokemonCard from '@/components/PokemonCard.vue'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import type { DraftPick, LeaguePlayer, Pokemon } from '@/types'
import { enqueueSnackbar } from '@/services/snackbar'

interface LeagueState {
  code: string
  name: string
  pointLimit: number
  rounds: number
  regulationSet?: string
  players: LeaguePlayer[]
  pointValues: Record<number, number>
  draft: {
    status: string
    currentPickNumber: number
    totalPicks: number
    currentPickerId: string | null
    currentPickerName: string | null
    picks: DraftPick[]
  }
}

interface TeamEntry {
  pokemonId: number
  pickNumber: number
  pokemon?: Pokemon
  points: number
}

interface ApiStat {
  name: string
  baseStat: number
}

interface ApiPokemonDetail {
  stats: ApiStat[]
}

interface StatEntry {
  key: string
  label: string
  value: number
}

const STAT_ORDER = [
  { key: 'hp', label: 'HP' },
  { key: 'attack', label: 'Atk' },
  { key: 'defense', label: 'Def' },
  { key: 'special-attack', label: 'SpA' },
  { key: 'special-defense', label: 'SpD' },
  { key: 'speed', label: 'Spe' },
]

const router = useRouter()
const authStore = useAuthStore()
const pokemonStore = usePokemonStore()
const { subscribe, unsubscribe } = useSignalR()

if (!authStore.isAuthenticated) router.replace('/join')

const leagueCode = computed(() => authStore.leagueCode ?? '')
const currentPlayerId = computed(() => authStore.playerId ?? '')
const league = ref<LeagueState | null>(null)
const isLoading = ref(true)
const teamAvatarError = ref(false)
const selectedPokemon = ref<Pokemon | null>(null)
const statSpreads = ref<Record<number, StatEntry[] | null>>({})

const displayTeamName = computed(() =>
  authStore.teamName?.trim() ? authStore.teamName : authStore.playerName + "'s Team",
)
const heroInitials = computed(() => getInitials(displayTeamName.value))
const selectedPokemonPoints = computed(() =>
  selectedPokemon.value ? getPointValue(selectedPokemon.value.id) : 0,
)

function getInitials(name: string) {
  return name
    .split(' ')
    .map((word) => word[0])
    .join('')
    .toUpperCase()
    .slice(0, 2)
}

function getPointValue(pokemonId: number) {
  return Number(
    league.value?.pointValues?.[pokemonId] ?? pokemonStore.getPointValue(pokemonId) ?? 0,
  )
}

function applyState(state: LeagueState) {
  league.value = state
  for (const [id, points] of Object.entries(state.pointValues ?? {})) {
    pokemonStore.setPointValue(Number(id), Number(points))
  }
}

async function fetchLeagueState() {
  const res = await fetch(API_BASE + '/leagues/' + leagueCode.value)
  if (!res.ok) throw new Error('Failed to load league state.')
  applyState((await res.json()) as LeagueState)
}

function getTeamEntries(playerId: string) {
  if (!league.value || !playerId) return [] as TeamEntry[]

  return league.value.draft.picks
    .filter((pick) => pick.playerId === playerId)
    .sort((a, b) => a.pickNumber - b.pickNumber)
    .map((pick) => ({
      pokemonId: pick.pokemonId,
      pickNumber: pick.pickNumber,
      pokemon: pokemonStore.getPokemonById(pick.pokemonId),
      points: getPointValue(pick.pokemonId),
    }))
}

const myTeam = computed(() => getTeamEntries(currentPlayerId.value))

async function fetchRosterStats() {
  const pokemonIds = myTeam.value
    .map((entry) => entry.pokemonId)
    .filter((pokemonId) => statSpreads.value[pokemonId] === undefined)

  if (!pokemonIds.length) return

  const results = await Promise.all(
    pokemonIds.map(async (pokemonId) => {
      try {
        const response = await fetch(API_BASE + '/pokemon/' + pokemonId + '/detail')
        if (!response.ok) throw new Error('HTTP ' + response.status)

        const detail = (await response.json()) as ApiPokemonDetail
        const values = new Map(detail.stats.map((stat) => [stat.name, stat.baseStat]))
        const stats = STAT_ORDER.map((stat) => ({
          ...stat,
          value: values.get(stat.key) ?? 0,
        }))
        return [pokemonId, stats] as const
      } catch (error) {
        console.error('Unable to load stats for Pokemon #' + pokemonId + '.', error)
        return [pokemonId, null] as const
      }
    }),
  )

  statSpreads.value = {
    ...statSpreads.value,
    ...Object.fromEntries(results),
  }
}

async function loadPage() {
  if (!leagueCode.value) {
    isLoading.value = false
    return
  }

  isLoading.value = true
  try {
    await Promise.all([pokemonStore.fetchAllPokemon(), fetchLeagueState()])
    await fetchRosterStats()
  } catch (error) {
    console.error(error)
    enqueueSnackbar('Unable to load your team right now.', 'error')
  } finally {
    isLoading.value = false
  }
}

function getStats(pokemonId: number) {
  return statSpreads.value[pokemonId] ?? []
}

function statTotal(stats: StatEntry[]) {
  return stats.reduce((total, stat) => total + stat.value, 0)
}

function statColor(value: number) {
  if (value < 50) return 'error'
  if (value < 80) return 'warning'
  if (value < 100) return 'yellow-darken-1'
  if (value < 120) return 'light-green'
  return 'success'
}

function openDetail(pokemon: Pokemon | undefined) {
  if (pokemon) selectedPokemon.value = pokemon
}

function handleLeagueState(state: LeagueState) {
  applyState(state)
  void fetchRosterStats()
}

onMounted(async () => {
  await loadPage()
  if (leagueCode.value) await subscribe(leagueCode.value, handleLeagueState)
})

onUnmounted(() => unsubscribe(handleLeagueState))
</script>

<template>
  <v-container fluid class="page-card-small my-team-page">
    <template v-if="!isLoading && league">
      <header class="team-identity">
        <v-avatar size="64" color="primary" class="team-avatar">
          <v-img
            v-if="authStore.teamImageUrl && !teamAvatarError"
            :src="authStore.teamImageUrl"
            :alt="displayTeamName"
            cover
            @error="teamAvatarError = true"
          />
          <span v-else class="text-h6 font-weight-bold">{{ heroInitials }}</span>
        </v-avatar>
        <h1>{{ displayTeamName }}</h1>
      </header>

      <div v-if="myTeam.length" class="roster-list">
        <article v-for="entry in myTeam" :key="entry.pokemonId" class="roster-row">
          <PokemonCard
            :pokemon="entry.pokemon!"
            :point-value="entry.points"
            mode="team"
            stacked
            :show-sprite="true"
            class="roster-pokemon-card"
            @click="openDetail(entry.pokemon)"
          />

          <div class="stat-spread">
            <template v-if="getStats(entry.pokemonId).length">
              <div class="stat-spread-heading">
                <span>Base stats</span>
                <strong>BST {{ statTotal(getStats(entry.pokemonId)) }}</strong>
              </div>
              <div class="stat-grid">
                <div
                  v-for="stat in getStats(entry.pokemonId)"
                  :key="stat.key"
                  class="stat-cell"
                >
                  <span>{{ stat.label }}</span>
                  <strong>{{ stat.value }}</strong>
                  <v-progress-linear
                    :model-value="stat.value"
                    :max="180"
                    :color="statColor(stat.value)"
                    height="4"
                    rounded
                  />
                </div>
              </div>
            </template>
            <span v-else-if="statSpreads[entry.pokemonId] === null" class="stat-error">
              Stats unavailable
            </span>
          </div>
        </article>
      </div>

      <p v-else class="empty-roster">No Pokemon on your roster yet.</p>
    </template>
  </v-container>

  <PokemonDetailModal
    v-if="selectedPokemon"
    :pokemon="selectedPokemon"
    :point-value="selectedPokemonPoints"
    :can-draft="false"
    :is-picked="true"
    @close="selectedPokemon = null"
  />
</template>

<style scoped>
.team-identity {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 4px 0 16px;
  border-bottom: 1px solid var(--border-color);
}

.team-avatar {
  flex: 0 0 auto;
  border: 2px solid color-mix(in srgb, var(--primary) 45%, transparent);
}

.team-identity h1 {
  min-width: 0;
  margin: 0;
  overflow-wrap: anywhere;
  color: var(--text);
  font-size: 1.5rem;
  font-weight: 800;
  line-height: 1.15;
}

.roster-list {
  display: flex;
  flex-direction: column;
}

.roster-row {
  display: grid;
  grid-template-columns: 150px minmax(0, 1fr);
  align-items: center;
  gap: 18px;
  padding: 14px 0;
  border-bottom: 1px solid var(--border-color);
}

.roster-pokemon-card {
  width: 100%;
}

.roster-pokemon-card :deep(.sprite) {
  display: block;
  flex: 0 0 78px;
  width: 78px;
  height: 78px;
}

.stat-spread {
  min-width: 0;
}

.stat-spread-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 8px;
  color: var(--text-muted);
  font-size: 0.72rem;
  text-transform: uppercase;
}

.stat-spread-heading strong {
  color: var(--text);
  font-size: 0.75rem;
}

.stat-grid {
  display: grid;
  grid-template-columns: repeat(6, minmax(54px, 1fr));
  gap: 8px;
}

.stat-cell {
  min-width: 0;
  padding: 8px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: var(--card-bg);
}

.stat-cell span,
.stat-cell strong {
  display: block;
}

.stat-cell span {
  color: var(--text-muted);
  font-size: 0.66rem;
  font-weight: 700;
  text-transform: uppercase;
}

.stat-cell strong {
  margin: 2px 0 6px;
  color: var(--text);
  font-size: 1rem;
}

.stat-error,
.empty-roster {
  color: var(--text-muted);
}

.empty-roster {
  margin: 18px 0 0;
}

@media (max-width: 720px) {
  .team-identity {
    padding-bottom: 12px;
  }

  .team-identity h1 {
    font-size: 1.2rem;
  }

  .roster-row {
    grid-template-columns: 118px minmax(0, 1fr);
    gap: 10px;
    padding: 12px 0;
  }

  .roster-pokemon-card :deep(.sprite) {
    flex-basis: 58px;
    width: 58px;
    height: 58px;
  }

  .stat-spread-heading {
    margin-bottom: 6px;
  }

  .stat-grid {
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 5px;
  }

  .stat-cell {
    padding: 6px;
  }

  .stat-cell strong {
    margin-bottom: 4px;
    font-size: 0.85rem;
  }
}
</style>