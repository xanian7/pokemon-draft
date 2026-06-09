<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import { apiGet } from '@/services/api'
import { enqueueSnackbar } from '@/services/snackbar'
import { useSignalR } from '@/services/signalr'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import type {
  MatchupResponse,
  Pokemon,
  ScheduleData,
  ServerLeagueResponse,
  StandingRow,
} from '@/types'
import { formatPokemonName, TYPE_COLORS } from '@/utils/format'

interface AvailabilityDay {
  key: string
  enabled: boolean
  start: string
  end: string
}

interface WeeklyAvailability {
  version: number
  days: AvailabilityDay[]
}

interface RosterEntry {
  pokemon: Pokemon
  points: number
}

const router = useRouter()
const authStore = useAuthStore()
const pokemonStore = usePokemonStore()
const { subscribe, unsubscribe } = useSignalR()

if (!authStore.isAuthenticated) router.replace('/join')

const league = ref<ServerLeagueResponse | null>(null)
const schedule = ref<ScheduleData | null>(null)
const isLoading = ref(true)
const selectedMatchupId = ref<number | null>(null)
const selectedPokemon = ref<Pokemon | null>(null)

const myMatchups = computed(() =>
  (schedule.value?.weeks ?? [])
    .flatMap((week) => week.matchups)
    .filter(
      (matchup) =>
        matchup.player1Id === authStore.playerId || matchup.player2Id === authStore.playerId,
    )
    .sort((a, b) => a.week - b.week),
)

const matchupOptions = computed(() =>
  myMatchups.value.map((matchup) => ({
    title: `Week ${matchup.week}: ${opponentLabel(matchup)}`,
    value: matchup.id,
  })),
)

const activeMatchup = computed(
  () =>
    myMatchups.value.find((matchup) => matchup.id === selectedMatchupId.value) ??
    myMatchups.value[0] ??
    null,
)

const opponentId = computed(() => {
  const matchup = activeMatchup.value
  if (!matchup) return ''
  return matchup.player1Id === authStore.playerId ? matchup.player2Id : matchup.player1Id
})

const opponent = computed(
  () => league.value?.players.find((player) => player.id === opponentId.value) ?? null,
)

const opponentStanding = computed<StandingRow | null>(
  () => schedule.value?.standings.find((row) => row.playerId === opponentId.value) ?? null,
)

const opponentRoster = computed<RosterEntry[]>(() => {
  if (!league.value || !opponentId.value) return []

  return league.value.draft.picks
    .filter((pick) => pick.playerId === opponentId.value)
    .sort((a, b) => a.pickNumber - b.pickNumber)
    .flatMap((pick) => {
      const pokemon = pokemonStore.getPokemonById(pick.pokemonId)
      return pokemon
        ? [{ pokemon, points: Number(league.value?.pointValues[pick.pokemonId] ?? 0) }]
        : []
    })
})

const rosterPoints = computed(() =>
  opponentRoster.value.reduce((total, entry) => total + entry.points, 0),
)

const averageBst = computed(() => {
  if (!opponentRoster.value.length) return 0
  const total = opponentRoster.value.reduce(
    (sum, entry) => sum + (entry.pokemon.bst ?? 0),
    0,
  )
  return Math.round(total / opponentRoster.value.length)
})

const availabilityDays = computed(() => parseAvailability(opponent.value?.availability))

const matchupStatus = computed(() => {
  const matchup = activeMatchup.value
  if (!matchup || matchup.player1Wins === null || matchup.player2Wins === null) {
    return { label: 'Upcoming', color: 'primary' }
  }

  const myWins =
    matchup.player1Id === authStore.playerId ? matchup.player1Wins : matchup.player2Wins
  const theirWins =
    matchup.player1Id === authStore.playerId ? matchup.player2Wins : matchup.player1Wins
  return myWins > theirWins
    ? { label: `Won ${myWins}-${theirWins}`, color: 'success' }
    : { label: `Lost ${myWins}-${theirWins}`, color: 'error' }
})

function applyState(state: ServerLeagueResponse) {
  league.value = state
  for (const [id, points] of Object.entries(state.pointValues ?? {})) {
    pokemonStore.setPointValue(Number(id), Number(points))
  }
}

async function fetchLeague() {
  const result = await apiGet<ServerLeagueResponse>(`/leagues/${authStore.leagueCode}`)
  if (result.error || !result.data) throw new Error(result.error ?? 'League data was empty.')
  applyState(result.data)
}

async function fetchSchedule() {
  const result = await apiGet<ScheduleData>(`/leagues/${authStore.leagueCode}/schedule`)
  if (result.error || !result.data) throw new Error(result.error ?? 'Schedule data was empty.')
  schedule.value = result.data
}

function selectDefaultMatchup() {
  if (myMatchups.value.some((matchup) => matchup.id === selectedMatchupId.value)) return
  const nextMatchup = myMatchups.value.find((matchup) => matchup.player1Wins === null)
  selectedMatchupId.value =
    nextMatchup?.id ?? myMatchups.value[myMatchups.value.length - 1]?.id ?? null
}

async function loadPage() {
  if (!authStore.leagueCode) return
  isLoading.value = true
  try {
    await Promise.all([pokemonStore.fetchAllPokemon(), fetchLeague(), fetchSchedule()])
    if (pokemonStore.error) throw new Error(pokemonStore.error)
    selectDefaultMatchup()
  } catch (error) {
    console.error(error)
    enqueueSnackbar('Unable to load matchup details right now.', 'error')
  } finally {
    isLoading.value = false
  }
}

function handleLeagueState(state: ServerLeagueResponse) {
  applyState(state)
}

onMounted(async () => {
  await loadPage()
  if (authStore.leagueCode) await subscribe(authStore.leagueCode, handleLeagueState)
})

onUnmounted(() => unsubscribe(handleLeagueState))

function opponentLabel(matchup: MatchupResponse) {
  const isPlayerOne = matchup.player1Id === authStore.playerId
  const teamName = isPlayerOne ? matchup.player2TeamName : matchup.player1TeamName
  const playerName = isPlayerOne ? matchup.player2Name : matchup.player1Name
  return teamName?.trim() || playerName
}

function playerLabel(matchup: MatchupResponse, side: 1 | 2) {
  const teamName = side === 1 ? matchup.player1TeamName : matchup.player2TeamName
  const playerName = side === 1 ? matchup.player1Name : matchup.player2Name
  return teamName?.trim() || playerName
}

function playerName(matchup: MatchupResponse, side: 1 | 2) {
  return side === 1 ? matchup.player1Name : matchup.player2Name
}

function playerImage(matchup: MatchupResponse, side: 1 | 2) {
  return side === 1 ? matchup.player1TeamImageUrl : matchup.player2TeamImageUrl
}

function initials(label: string) {
  return label
    .split(' ')
    .map((word) => word[0])
    .join('')
    .toUpperCase()
    .slice(0, 2)
}

function parseAvailability(value?: string): AvailabilityDay[] {
  if (!value?.trim()) return []
  try {
    const parsed = JSON.parse(value) as WeeklyAvailability
    if (!Array.isArray(parsed.days)) return []
    return parsed.days.filter((day) => day.enabled && day.start && day.end)
  } catch {
    return []
  }
}

function formatDay(day: string) {
  return day.charAt(0).toUpperCase() + day.slice(1)
}

function formatTime(value: string) {
  const [hourText, minute = '00'] = value.split(':')
  const hour = Number(hourText)
  if (Number.isNaN(hour)) return value
  const suffix = hour >= 12 ? 'PM' : 'AM'
  const displayHour = hour % 12 || 12
  return `${displayHour}:${minute} ${suffix}`
}

function localTime(timeZone?: string) {
  if (!timeZone) return 'Not set'
  try {
    return new Intl.DateTimeFormat(undefined, {
      timeZone,
      weekday: 'short',
      hour: 'numeric',
      minute: '2-digit',
      timeZoneName: 'short',
    }).format(new Date())
  } catch {
    return timeZone
  }
}
</script>

<template>
  <v-container fluid class="matchup-page">
    <v-card class="wrapper-card">
      <div class="matchup-header">
        <div>
          <div class="eyebrow">League Matchup</div>
          <h1>Your Battle Briefing</h1>
        </div>
        <v-select
          v-if="matchupOptions.length > 1"
          v-model="selectedMatchupId"
          :items="matchupOptions"
          label="Matchup"
          density="compact"
          variant="outlined"
          hide-details
          class="matchup-select"
        />
      </div>

      <div v-if="isLoading" class="loading-panel">
        <PokeballLoader variant="page" label="Loading matchup..." />
      </div>

      <v-alert
        v-else-if="!activeMatchup"
        type="info"
        variant="tonal"
        icon="mdi-calendar-blank"
      >
        You do not have a matchup scheduled yet.
      </v-alert>

      <div v-else class="matchup-content">
        <v-card class="battle-card" variant="tonal">
          <div class="week-label">Week {{ activeMatchup.week }}</div>
          <div class="battle-row">
            <div class="battle-team">
              <v-avatar size="72" color="primary">
                <v-img
                  v-if="playerImage(activeMatchup, 1)"
                  :src="playerImage(activeMatchup, 1)"
                  cover
                />
                <span v-else>{{ initials(playerLabel(activeMatchup, 1)) }}</span>
              </v-avatar>
              <div class="battle-team-label">
                <strong>{{ playerLabel(activeMatchup, 1) }}</strong>
                <span>{{ playerName(activeMatchup, 1) }}</span>
              </div>
            </div>

            <div class="battle-status">
              <v-chip :color="matchupStatus.color" variant="flat">{{ matchupStatus.label }}</v-chip>
              <span class="versus">VS</span>
            </div>

            <div class="battle-team">
              <v-avatar size="72" color="secondary">
                <v-img
                  v-if="playerImage(activeMatchup, 2)"
                  :src="playerImage(activeMatchup, 2)"
                  cover
                />
                <span v-else>{{ initials(playerLabel(activeMatchup, 2)) }}</span>
              </v-avatar>
              <div class="battle-team-label">
                <strong>{{ playerLabel(activeMatchup, 2) }}</strong>
                <span>{{ playerName(activeMatchup, 2) }}</span>
              </div>
            </div>
          </div>
        </v-card>

        <v-row dense>
          <v-col cols="12" md="6">
            <v-card class="info-card" variant="outlined">
              <v-card-title>
                <v-icon icon="mdi-chart-box-outline" start />
                Opponent Snapshot
              </v-card-title>
              <v-card-text class="snapshot-grid">
                <div class="metric">
                  <span>Record</span>
                  <strong>
                    {{ opponentStanding?.wins ?? 0 }}-{{ opponentStanding?.losses ?? 0 }}
                  </strong>
                </div>
                <div class="metric">
                  <span>Match Points</span>
                  <strong>{{ opponentStanding?.matchPoints ?? 0 }}</strong>
                </div>
                <div class="metric">
                  <span>Games</span>
                  <strong>
                    {{ opponentStanding?.gamesWon ?? 0 }}-{{ opponentStanding?.gamesLost ?? 0 }}
                  </strong>
                </div>
                <div class="metric">
                  <span>Roster Points</span>
                  <strong>{{ rosterPoints }} / {{ league?.pointLimit ?? 0 }}</strong>
                </div>
              </v-card-text>
            </v-card>
          </v-col>

          <v-col cols="12" md="6">
            <v-card class="info-card" variant="outlined">
              <v-card-title>
                <v-icon icon="mdi-clock-outline" start />
                Time &amp; Availability
              </v-card-title>
              <v-card-text>
                <div class="timezone">
                  <strong>{{ opponent?.timeZone || 'Time zone not set' }}</strong>
                  <span>{{ localTime(opponent?.timeZone) }}</span>
                </div>
                <div v-if="availabilityDays.length" class="availability-list">
                  <div v-for="day in availabilityDays" :key="day.key" class="availability-row">
                    <span>{{ formatDay(day.key) }}</span>
                    <strong>{{ formatTime(day.start) }} - {{ formatTime(day.end) }}</strong>
                  </div>
                </div>
                <div v-else class="empty-detail">Availability has not been added yet.</div>
              </v-card-text>
            </v-card>
          </v-col>
        </v-row>

        <section class="roster-section">
          <div class="section-header">
            <div>
              <div class="eyebrow">Scouting Report</div>
              <h2>{{ opponentLabel(activeMatchup) }}'s Pokémon</h2>
            </div>
            <div class="roster-summary">
              <v-chip prepend-icon="mdi-pokeball" variant="outlined">
                {{ opponentRoster.length }} Pokémon
              </v-chip>
              <v-chip prepend-icon="mdi-chart-line" variant="outlined">
                {{ averageBst }} Avg. BST
              </v-chip>
            </div>
          </div>

          <div v-if="opponentRoster.length" class="pokemon-grid">
            <v-card
              v-for="entry in opponentRoster"
              :key="entry.pokemon.id"
              class="pokemon-card"
              variant="outlined"
              hover
              @click="selectedPokemon = entry.pokemon"
            >
              <div class="point-badge">{{ entry.points }} pts</div>
              <v-img :src="entry.pokemon.spriteUrl" height="118" contain />
              <v-card-title>{{ formatPokemonName(entry.pokemon.name) }}</v-card-title>
              <v-card-text>
                <div class="type-row">
                  <v-chip
                    v-for="type in entry.pokemon.types"
                    :key="type"
                    size="x-small"
                    :color="TYPE_COLORS[type]"
                    variant="flat"
                  >
                    {{ type }}
                  </v-chip>
                </div>
                <span class="bst">BST {{ entry.pokemon.bst ?? '-' }}</span>
              </v-card-text>
            </v-card>
          </div>
          <v-alert v-else type="info" variant="tonal">
            This opponent does not have any Pokémon on their roster yet.
          </v-alert>
        </section>
      </div>
    </v-card>

    <PokemonDetailModal
      v-if="selectedPokemon"
      :pokemon="selectedPokemon"
      :point-value="Number(league?.pointValues[selectedPokemon.id] ?? 0)"
      :can-draft="false"
      :is-picked="true"
      :show-draft-action="false"
      @close="selectedPokemon = null"
    />
  </v-container>
</template>

<style scoped>
.matchup-page {
  padding-top: 0;
}

.wrapper-card {
  display: flex;
  flex-direction: column;
  gap: 16px;
  height: 85dvh;
  max-height: 85dvh;
  overflow: auto;
  padding: 8px;
  border-radius: 6px;
}

.matchup-header,
.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}

.matchup-header h1,
.section-header h2 {
  margin: 0;
  line-height: 1.15;
}

.matchup-header h1 {
  font-size: clamp(1.45rem, 3vw, 2rem);
}

.section-header h2 {
  font-size: clamp(1.15rem, 2vw, 1.5rem);
}

.eyebrow {
  color: rgb(var(--v-theme-primary));
  font-size: 0.72rem;
  font-weight: 800;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.matchup-select {
  flex: 0 1 340px;
}

.loading-panel {
  display: grid;
  min-height: 320px;
  place-items: center;
}

.matchup-content {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.battle-card {
  padding: 18px;
  text-align: center;
}

.week-label {
  color: rgb(var(--v-theme-primary));
  font-size: 0.78rem;
  font-weight: 800;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.battle-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  align-items: center;
  gap: 20px;
  margin-top: 10px;
}

.battle-team {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  min-width: 0;
}

.battle-team-label {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.battle-team-label strong {
  overflow: hidden;
  font-size: 1.05rem;
  text-overflow: ellipsis;
}

.battle-team-label span {
  color: var(--text-muted);
  font-size: 0.8rem;
}

.battle-status {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 5px;
}

.versus {
  color: var(--text-muted);
  font-size: 0.8rem;
  font-weight: 800;
}

.info-card {
  height: 100%;
}

.info-card .v-card-title {
  font-size: 1rem;
}

.snapshot-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.metric {
  display: flex;
  flex-direction: column;
  padding: 10px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
}

.metric span,
.timezone span,
.empty-detail,
.bst {
  color: var(--text-muted);
  font-size: 0.8rem;
}

.metric strong {
  font-size: 1.15rem;
}

.timezone {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}

.availability-list {
  display: grid;
  gap: 6px;
}

.availability-row {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding-top: 6px;
  border-top: 1px solid var(--border-color);
  font-size: 0.86rem;
}

.roster-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.roster-summary,
.type-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.pokemon-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(155px, 1fr));
  gap: 10px;
}

.pokemon-card {
  position: relative;
  cursor: pointer;
  overflow: hidden;
}

.pokemon-card .v-card-title {
  padding: 5px 10px 2px;
  overflow: hidden;
  font-size: 0.9rem;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.pokemon-card .v-card-text {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
  padding: 6px 10px 10px;
}

.point-badge {
  position: absolute;
  top: 7px;
  right: 7px;
  z-index: 1;
  padding: 3px 7px;
  border-radius: 999px;
  background: rgb(var(--v-theme-primary));
  color: rgb(var(--v-theme-on-primary));
  font-size: 0.7rem;
  font-weight: 800;
}

@media (max-width: 720px) {
  .matchup-page {
    padding-inline: 0;
  }

  .wrapper-card {
    height: auto;
    max-height: none;
    overflow: visible;
    padding: 6px;
    border-right: 0;
    border-left: 0;
    border-radius: 0;
  }

  .matchup-header,
  .section-header {
    align-items: stretch;
    flex-direction: column;
    gap: 10px;
  }

  .matchup-select {
    flex-basis: auto;
    max-width: none;
  }

  .battle-card {
    padding: 12px 8px;
  }

  .battle-row {
    grid-template-columns: minmax(0, 1fr) 50px minmax(0, 1fr);
    gap: 6px;
  }

  .battle-team {
    flex-direction: column;
    gap: 5px;
  }

  .battle-team-label {
    align-items: center;
    max-width: 100%;
  }

  .battle-team-label strong {
    max-width: 100%;
    font-size: 0.83rem;
  }

  .battle-team .v-avatar {
    width: 58px !important;
    height: 58px !important;
  }

  .battle-status .v-chip {
    max-width: 72px;
    padding-inline: 7px;
    font-size: 0.65rem;
  }

  .pokemon-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 7px;
  }

  .pokemon-card .v-card-text {
    align-items: flex-start;
    flex-direction: column;
  }

  .timezone,
  .availability-row {
    align-items: flex-start;
    flex-direction: column;
    gap: 2px;
  }
}
</style>
