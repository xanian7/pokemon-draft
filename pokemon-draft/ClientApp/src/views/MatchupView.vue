<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import PokemonCard from '@/components/PokemonCard.vue'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import PageHeader from '@/components/PageHeader.vue'
import FormField from '@/components/FormField.vue'
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
      <PageHeader
        class="matchup-page-header"
        eyebrow="Competition"
        title="Matchup"
        :subtitle="activeMatchup ? `Week ${activeMatchup.week} · Review both rosters and prepare for battle.` : 'Your current scheduled opponent and roster comparison.'"
      >
        <template v-if="activeMatchup" #actions>
          <v-chip :color="matchupStatus.color" size="small" variant="tonal">
            {{ matchupStatus.label }}
          </v-chip>
          <FormField v-if="matchupOptions.length > 1" label="Matchup">
            <v-select
              v-model="selectedMatchupId"
              :items="matchupOptions"
              hide-details
              class="matchup-select"
            />
          </FormField>
        </template>
      </PageHeader>

      <div v-if="isLoading" class="loading-panel">
        <!-- <PokeballLoader variant="page" label="Loading matchup..." /> -->
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
        <v-card class="matchup-card" variant="outlined">
          <v-card-text class="battle-row">
            <div class="battle-team">
              <v-avatar size="64" color="primary" class="team-avatar">
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
            <v-chip class="versus" size="small" variant="outlined">VS</v-chip>
            <div class="battle-team battle-team-right">
              <div class="battle-team-label">
                <strong>{{ playerLabel(activeMatchup, 2) }}</strong>
                <span>{{ playerName(activeMatchup, 2) }}</span>
              </div>
              <v-avatar size="64" color="secondary" class="team-avatar">
                <v-img
                  v-if="playerImage(activeMatchup, 2)"
                  :src="playerImage(activeMatchup, 2)"
                  cover
                />
                <span v-else>{{ initials(playerLabel(activeMatchup, 2)) }}</span>
              </v-avatar>
            </div>
          </v-card-text>
        </v-card>

        <v-row class="details-row" dense>
          <v-col cols="12" md="6">
            <v-card class="info-card" variant="outlined">
              <v-card-title class="section-title">
                <div>
                  <div class="text-overline text-medium-emphasis">Season performance</div>
                  <span>Opponent Snapshot</span>
                </div>
                <v-icon icon="mdi-chart-box-outline" />
              </v-card-title>
              <v-divider />
              <v-card-text class="snapshot-grid">
                <v-card class="metric" variant="tonal">
                  <span>Record</span>
                  <strong>
                    {{ opponentStanding?.wins ?? 0 }}-{{ opponentStanding?.losses ?? 0 }}
                  </strong>
                </v-card>
                <v-card class="metric" variant="tonal">
                  <span>Match Points</span>
                  <strong>{{ opponentStanding?.matchPoints ?? 0 }}</strong>
                </v-card>
                <v-card class="metric" variant="tonal">
                  <span>Games</span>
                  <strong>
                    {{ opponentStanding?.gamesWon ?? 0 }}-{{ opponentStanding?.gamesLost ?? 0 }}
                  </strong>
                </v-card>
                <v-card class="metric" variant="tonal">
                  <span>Roster Points</span>
                  <strong>{{ rosterPoints }} / {{ league?.pointLimit ?? 0 }}</strong>
                </v-card>
              </v-card-text>
            </v-card>
          </v-col>

          <v-col cols="12" md="6">
            <v-card class="info-card" variant="outlined">
              <v-card-title class="section-title">
                <div>
                  <div class="text-overline text-medium-emphasis">Scheduling</div>
                  <span>Time &amp; Availability</span>
                </div>
                <v-icon icon="mdi-clock-outline" />
              </v-card-title>
              <v-divider />
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

        <v-card class="roster-section" variant="outlined">
          <v-card-title class="section-title">
            <div>
              <div class="text-overline text-medium-emphasis">Scouting report</div>
              <span>{{ opponentLabel(activeMatchup) }}'s Pokémon</span>
            </div>
            <div class="roster-summary">
              <v-chip prepend-icon="mdi-pokeball" size="small" variant="tonal">
                {{ opponentRoster.length }} Pokémon
              </v-chip>
              <v-chip prepend-icon="mdi-chart-line" size="small" variant="tonal">
                {{ averageBst }} Avg. BST
              </v-chip>
            </div>
          </v-card-title>
          <v-divider />
          <v-card-text>
            <div v-if="opponentRoster.length" class="roster-grid">
              <PokemonCard
                v-for="entry in opponentRoster"
                :key="entry.pokemon.id"
                :pokemon="entry.pokemon"
                :point-value="entry.points"
                mode="team"
                @click="selectedPokemon = entry.pokemon"
              />
            </div>
            <v-alert v-else type="info" variant="tonal">
              This opponent does not have any Pokémon on their roster yet.
            </v-alert>
          </v-card-text>
        </v-card>
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
  padding: clamp(1rem, 2vw, 2rem);
}

.wrapper-card {
  display: flex;
  flex-direction: column;
  gap: 14px;
  height: auto;
  max-height: none;
  overflow: auto;
  padding: 0;
}

.wrapper-card :deep(.v-card) {
  border-color: var(--border-color);
  border-radius: var(--radius-md);
}

.wrapper-card :deep(.v-card-text) {
  padding: 16px;
}

.matchup-content {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.section-title {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  flex-wrap: wrap;
  padding: 8px 12px;
}

.section-title > div:first-child {
  min-width: 0;
}

.section-title span {
  display: block;
  overflow: hidden;
  font-size: 1rem;
  font-weight: 600;
  text-overflow: ellipsis;
}

.matchup-title {
  min-height: 58px;
}

.matchup-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
}

.matchup-select {
  width: min(320px, 42vw);
}

.loading-panel {
  display: grid;
  min-height: 320px;
  place-items: center;
}

.battle-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  align-items: center;
  gap: 12px;
  min-height: 88px;
}

.battle-team {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}

.battle-team-right {
  justify-content: flex-end;
  text-align: right;
}

.team-avatar {
  flex: 0 0 auto;
  border: 2px solid color-mix(in srgb, var(--primary) 45%, transparent);
}

.battle-team-label {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.battle-team-label strong {
  overflow: hidden;
  font-size: 1rem;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.battle-team-label span {
  color: var(--text-muted);
  font-size: 0.75rem;
}

.versus {
  min-width: 42px;
  justify-content: center;
}

.details-row {
  margin: -4px;
}

.details-row > .v-col {
  padding: 4px;
}

.info-card {
  height: 100%;
}

.snapshot-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 6px;
}

.metric {
  display: flex;
  flex-direction: column;
  padding: 8px;
}

.metric span,
.timezone span,
.empty-detail {
  color: var(--text-muted);
  font-size: 0.75rem;
}

.metric strong {
  font-size: 1.1rem;
}

.timezone {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 8px;
}

.availability-list {
  display: grid;
  gap: 4px;
}

.availability-row {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  padding-top: 4px;
  border-top: 1px solid var(--border-color);
  font-size: 0.82rem;
}

.roster-summary {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.roster-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
  gap: 6px;
}

@media (max-width: 720px) {
  .wrapper-card {
    height: auto;
    max-height: none;
    overflow: visible;
    padding: 6px;
    border-right: 0;
    border-left: 0;
    border-radius: 0;
  }

  .wrapper-card :deep(.v-card) {
    border-right: 0;
    border-left: 0;
    border-radius: 0;
  }

  .matchup-title {
    align-items: stretch;
    flex-direction: column;
  }

  .matchup-actions {
    justify-content: space-between;
  }

  .matchup-select {
    flex: 1 1 auto;
    width: auto;
    min-width: 0;
  }

  .battle-row {
    grid-template-columns: minmax(0, 1fr) 42px minmax(0, 1fr);
    gap: 4px;
  }

  .battle-team,
  .battle-team-right {
    align-items: center;
    flex-direction: column;
    justify-content: center;
    gap: 4px;
    max-width: 100%;
    text-align: center;
  }

  .battle-team-label strong {
    max-width: 100%;
    font-size: 0.8rem;
  }

  .team-avatar {
    width: 54px !important;
    height: 54px !important;
  }

  .roster-grid {
    grid-template-columns: 1fr;
  }

  .timezone,
  .availability-row {
    align-items: flex-start;
    flex-direction: column;
    gap: 2px;
  }
}
</style>
