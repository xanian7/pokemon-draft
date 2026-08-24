<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import PageHeader from '@/components/PageHeader.vue'
import ScoreReportDialog from '@/components/ScoreReportDialog.vue'
import SectionHeader from '@/components/SectionHeader.vue'

import DraftGateNotice from '@/components/DraftGateNotice.vue'
import { apiGet, apiPost } from '@/services/api'
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

const TeamScoutingReport = defineAsyncComponent(
  () => import('@/components/TeamScoutingReport.vue'),
)
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
const scoreDialogOpen = ref(false)
const reportMyWins = ref(2)
const reportOpponentWins = ref(0)
const reportReplayUrls = ref(['', '', ''])
const reportError = ref('')
const reportLoading = ref(false)

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

const draftComplete = computed(() => league.value?.draft.status.toLowerCase() === 'complete')

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

const myRoster = computed<RosterEntry[]>(() => {
  if (!league.value || !authStore.playerId) return []

  return league.value.draft.picks
    .filter((pick) => pick.playerId === authStore.playerId)
    .sort((a, b) => a.pickNumber - b.pickNumber)
    .flatMap((pick) => {
      const pokemon = pokemonStore.getPokemonById(pick.pokemonId)
      return pokemon
        ? [{ pokemon, points: Number(league.value?.pointValues[pick.pokemonId] ?? 0) }]
        : []
    })
})
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

const canReportScore = computed(
  () => activeMatchup.value?.player1Wins === null && activeMatchup.value?.player2Wins === null,
)

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

function selectMatchup(matchupId: number) {
  selectedMatchupId.value = matchupId
  scoreDialogOpen.value = false
  reportError.value = ''
}

function openScoreReport() {
  if (!activeMatchup.value || !canReportScore.value) return
  reportMyWins.value = 2
  reportOpponentWins.value = 0
  reportReplayUrls.value = ['', '', '']
  reportError.value = ''
  scoreDialogOpen.value = true
}

function closeScoreReport() {
  scoreDialogOpen.value = false
  reportError.value = ''
}

function normalizedReplayUrls() {
  return reportReplayUrls.value.map((url) => url.trim()).filter(Boolean).slice(0, 3)
}

function validateScoreReport() {
  if (
    reportMyWins.value < 0 ||
    reportOpponentWins.value < 0 ||
    reportMyWins.value > 2 ||
    reportOpponentWins.value > 2
  ) {
    return 'Wins must be between 0 and 2.'
  }
  if (reportMyWins.value + reportOpponentWins.value > 3) {
    return 'A best-of-3 cannot exceed 3 games.'
  }
  if (reportMyWins.value !== 2 && reportOpponentWins.value !== 2) {
    return 'One team must have 2 wins.'
  }
  if (reportMyWins.value === 2 && reportOpponentWins.value === 2) {
    return 'Both teams cannot have 2 wins.'
  }

  for (const replayUrl of normalizedReplayUrls()) {
    try {
      const url = new URL(replayUrl)
      if (url.protocol !== 'http:' && url.protocol !== 'https:') {
        return 'Replay links must be valid http or https URLs.'
      }
    } catch {
      return 'Replay links must be valid URLs.'
    }
  }

  return ''
}

async function submitScoreReport() {
  const matchup = activeMatchup.value
  if (!matchup) return

  const validationError = validateScoreReport()
  if (validationError) {
    reportError.value = validationError
    return
  }

  reportLoading.value = true
  reportError.value = ''

  const isPlayerOne = matchup.player1Id === authStore.playerId
  const replayUrls = normalizedReplayUrls()
  const result = await apiPost(
    '/leagues/' + authStore.leagueCode + '/schedule/' + matchup.id + '/report',
    {
      playerId: authStore.playerId,
      pin: authStore.pin,
      player1Wins: isPlayerOne ? reportMyWins.value : reportOpponentWins.value,
      player2Wins: isPlayerOne ? reportOpponentWins.value : reportMyWins.value,
      replayUrl: replayUrls[0] ?? null,
      replayUrls,
    },
  )

  if (result.error) {
    reportError.value = result.error
    reportLoading.value = false
    return
  }

  try {
    await fetchSchedule()
    closeScoreReport()
    enqueueSnackbar('Week ' + matchup.week + ' score reported.', 'success')
  } catch {
    closeScoreReport()
    enqueueSnackbar('Score saved, but the schedule could not be refreshed.', 'warning')
  } finally {
    reportLoading.value = false
  }
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
  <v-container fluid class="page-card-small">

      <div v-if="isLoading" class="page-state">
        
      </div>

      <DraftGateNotice
        v-else-if="!draftComplete"
        text="Matchups and opponent scouting unlock once the draft is complete."
      />

      <v-alert
        v-else-if="!activeMatchup"
        type="info"
        variant="tonal"
        icon="mdi-calendar-blank"
      >
        You do not have a matchup scheduled yet.
      </v-alert>

      <div v-else class="matchup-content">
        <div class="matchup-toolbar">
          <div class="matchup-toolbar__identity">
            <span>Selected matchup</span>
            <strong>Week {{ activeMatchup.week }} · {{ opponentLabel(activeMatchup) }}</strong>
          </div>

          <div class="matchup-toolbar__actions">
            <v-chip :color="matchupStatus.color" size="small" variant="tonal">
              {{ matchupStatus.label }}
            </v-chip>

            <v-menu v-if="matchupOptions.length > 1" location="bottom end">
              <template #activator="{ props: menuProps }">
                <v-btn
                  v-bind="menuProps"
                  prepend-icon="mdi-calendar-sync"
                  size="small"
                  variant="outlined"
                >
                  Switch week
                </v-btn>
              </template>
              <v-list density="compact" min-width="260">
                <v-list-item
                  v-for="option in matchupOptions"
                  :key="option.value"
                  :title="option.title"
                  :active="option.value === selectedMatchupId"
                  @click="selectMatchup(option.value)"
                />
              </v-list>
            </v-menu>

            <v-btn
              v-if="canReportScore"
              prepend-icon="mdi-trophy-outline"
              size="small"
              color="primary"
              @click="openScoreReport"
            >
              Report score
            </v-btn>
          </div>
        </div>

        <v-card class="matchup-card section-card" variant="outlined">
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
            <v-card class="info-card section-card" variant="outlined">
              <SectionHeader
                eyebrow="Season performance"
                title="Opponent Snapshot"
                icon="mdi-chart-box-outline"
              />
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
            <v-card class="info-card section-card" variant="outlined">
              <SectionHeader
                eyebrow="Scheduling"
                title="Time &amp; Availability"
                icon="mdi-clock-outline"
              />
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

        <v-row class="content-divider">
            <v-col cols="12">
              <v-divider class="border-opacity-25"></v-divider>
            </v-col>
          </v-row>

        <TeamScoutingReport
          :my-team-name="authStore.teamName || authStore.playerName || 'Your Team'"
          :opponent-team-name="opponentLabel(activeMatchup)"
          :my-roster="myRoster"
          :opponent-roster="opponentRoster"
          @select-pokemon="selectedPokemon = $event"
        />
      </div>

    <ScoreReportDialog
      v-if="activeMatchup"
      :model-value="scoreDialogOpen"
      :title="'Report Week ' + activeMatchup.week + ' Score'"
      :subtitle="
        (authStore.teamName || authStore.playerName || 'Your Team') +
        ' vs ' +
        opponentLabel(activeMatchup)
      "
      left-label="Your wins"
      right-label="Opponent wins"
      :left-wins="reportMyWins"
      :right-wins="reportOpponentWins"
      :replay-urls="reportReplayUrls"
      :error="reportError"
      :loading="reportLoading"
      @update:model-value="(value) => !value && closeScoreReport()"
      @update:left-wins="reportMyWins = $event"
      @update:right-wins="reportOpponentWins = $event"
      @update:replay-urls="reportReplayUrls = $event"
      @submit="submitScoreReport"
    />

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


.matchup-content {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.matchup-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  min-height: 58px;
  padding: 8px 10px;
  border-bottom: 1px solid var(--border-color);
}

.matchup-toolbar__identity {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.matchup-toolbar__identity span {
  color: var(--text-muted);
  font-size: 0.68rem;
  font-weight: 700;
  text-transform: uppercase;
}

.matchup-toolbar__identity strong {
  overflow: hidden;
  font-size: 0.95rem;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.matchup-toolbar__actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 6px;
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



@media (max-width: 720px) {
  .matchup-toolbar {
    align-items: stretch;
    flex-direction: column;
  }

  .matchup-toolbar__actions {
    justify-content: flex-start;
  }

  .matchup-toolbar__actions :deep(.v-btn) {
    flex: 1 1 auto;
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


  .timezone,
  .availability-row {
    align-items: flex-start;
    flex-direction: column;
    gap: 2px;
  }
}
</style>
