<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import PokeballLoader from '@/components/PokeballLoader.vue'
import type { MatchupResponse, ScheduleData, StandingRow, WeekGroup } from '@/types'

const router = useRouter()
const authStore = useAuthStore()

if (!authStore.isAuthenticated) router.replace('/join')

const API_BASE = import.meta.env.VITE_API_BASE ?? ''

const schedule = ref<ScheduleData | null>(null)
const isLoading = ref(true)
const error = ref('')
const showMyMatchesOnly = ref(false)
const openWeeks = ref<number[]>([])

const activeMatchup = ref<MatchupResponse | null>(null)
const reportP1Wins = ref(2)
const reportP2Wins = ref(0)
const reportReplayUrl = ref('')
const reportError = ref('')
const reportLoading = ref(false)
const isEditing = ref(false)

const standingsHeaders = [
  { title: '#', key: 'rank', width: 56 },
  { title: 'Team', key: 'team' },
  { title: 'W', key: 'wins', align: 'end' as const, width: 64 },
  { title: 'L', key: 'losses', align: 'end' as const, width: 64 },
  { title: 'Pts', key: 'matchPoints', align: 'end' as const, width: 72 },
  { title: 'Games', key: 'games', align: 'end' as const, width: 96 },
]

const matchupHeaders = [
  { title: 'Matchup', key: 'matchup' },
  { title: 'Score', key: 'score', align: 'center' as const, width: 150 },
  { title: 'Pts', key: 'points', align: 'center' as const, width: 120 },
  { title: 'Replay', key: 'replay', align: 'center' as const, width: 110 },
  { title: '', key: 'actions', align: 'end' as const, sortable: false, width: 150 },
]

async function fetchSchedule() {
  if (!authStore.leagueCode) return

  isLoading.value = true
  try {
    error.value = ''
    const res = await fetch(`${API_BASE}/api/leagues/${authStore.leagueCode}/schedule`)
    if (!res.ok) {
      error.value = 'Could not load schedule.'
      return
    }

    schedule.value = (await res.json()) as ScheduleData
    const currentWeek = schedule.value.weeks.find((week) =>
      week.matchups.some((matchup) => matchup.player1Wins === null),
    )?.week
    openWeeks.value = currentWeek ? [currentWeek] : schedule.value.weeks.slice(0, 1).map((w) => w.week)
  } catch {
    error.value = 'Could not connect to server.'
  } finally {
    isLoading.value = false
  }
}

onMounted(fetchSchedule)

const filteredWeeks = computed<WeekGroup[]>(() => {
  if (!schedule.value) return []
  if (!showMyMatchesOnly.value) return schedule.value.weeks

  return schedule.value.weeks
    .map((week) => ({
      ...week,
      matchups: week.matchups.filter(
        (matchup) =>
          matchup.player1Id === authStore.playerId || matchup.player2Id === authStore.playerId,
      ),
    }))
    .filter((week) => week.matchups.length > 0)
})

const standingsRows = computed(() =>
  (schedule.value?.standings ?? []).map((row, index) => ({
    ...row,
    rank: index + 1,
    team: teamLabel(row.playerName, row.teamName),
    games: `${row.gamesWon}-${row.gamesLost}`,
  })),
)

function isMyMatchup(matchup: MatchupResponse) {
  return matchup.player1Id === authStore.playerId || matchup.player2Id === authStore.playerId
}

function canReport(matchup: MatchupResponse) {
  return isMyMatchup(matchup) && matchup.player1Wins === null
}

function canEdit(matchup: MatchupResponse) {
  return authStore.isAdmin && matchup.player1Wins !== null
}

function openReport(matchup: MatchupResponse) {
  activeMatchup.value = matchup
  reportP1Wins.value = 2
  reportP2Wins.value = 0
  reportReplayUrl.value = ''
  reportError.value = ''
  isEditing.value = false
}

function openEdit(matchup: MatchupResponse) {
  activeMatchup.value = matchup
  reportP1Wins.value = matchup.player1Wins ?? 2
  reportP2Wins.value = matchup.player2Wins ?? 0
  reportReplayUrl.value = matchup.replayUrl ?? ''
  reportError.value = ''
  isEditing.value = true
}

function closeReport() {
  activeMatchup.value = null
  reportError.value = ''
}

function validateReport() {
  if (
    reportP1Wins.value < 0 ||
    reportP2Wins.value < 0 ||
    reportP1Wins.value > 2 ||
    reportP2Wins.value > 2
  ) {
    return 'Wins must be between 0 and 2.'
  }
  if (reportP1Wins.value + reportP2Wins.value > 3) return 'A best-of-3 cannot exceed 3 games.'
  if (reportP1Wins.value !== 2 && reportP2Wins.value !== 2) return 'One player must have 2 wins.'
  if (reportP1Wins.value === 2 && reportP2Wins.value === 2) {
    return 'Both players cannot have 2 wins.'
  }

  const trimmedReplay = reportReplayUrl.value.trim()
  if (trimmedReplay) {
    try {
      const url = new URL(trimmedReplay)
      if (url.protocol !== 'http:' && url.protocol !== 'https:') {
        return 'Replay link must be a valid http or https URL.'
      }
    } catch {
      return 'Replay link must be a valid URL.'
    }
  }

  return ''
}

async function submitReport() {
  const matchup = activeMatchup.value
  if (!matchup) return

  const validationError = validateReport()
  if (validationError) {
    reportError.value = validationError
    return
  }

  reportLoading.value = true
  reportError.value = ''

  try {
    const url = isEditing.value
      ? `${API_BASE}/api/leagues/${authStore.leagueCode}/schedule/${matchup.id}/edit`
      : `${API_BASE}/api/leagues/${authStore.leagueCode}/schedule/${matchup.id}/report`

    const replayUrl = reportReplayUrl.value.trim() || null
    const body = isEditing.value
      ? {
          adminPin: authStore.pin,
          player1Wins: reportP1Wins.value,
          player2Wins: reportP2Wins.value,
          replayUrl,
        }
      : {
          playerId: authStore.playerId,
          pin: authStore.pin,
          player1Wins: reportP1Wins.value,
          player2Wins: reportP2Wins.value,
          replayUrl,
        }

    const res = await fetch(url, {
      method: isEditing.value ? 'PATCH' : 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })

    if (!res.ok) {
      const text = await res.text()
      reportError.value = text || 'Failed to report score.'
      return
    }

    closeReport()
    await fetchSchedule()
  } catch {
    reportError.value = 'Could not connect to server.'
  } finally {
    reportLoading.value = false
  }
}

function teamLabel(name: string, teamName: string) {
  return teamName?.trim() ? teamName : name
}

function avatarInitials(name: string, teamName: string) {
  const label = teamName?.trim() || name
  return label
    .split(' ')
    .map((word) => word[0])
    .join('')
    .toUpperCase()
    .slice(0, 2)
}

function isWinner(matchup: MatchupResponse, side: 1 | 2) {
  if (matchup.player1Wins === null || matchup.player2Wins === null) return false
  return side === 1
    ? matchup.player1Wins > matchup.player2Wins
    : matchup.player2Wins > matchup.player1Wins
}

function completedCount(week: WeekGroup) {
  return week.matchups.filter((matchup) => matchup.player1Wins !== null).length
}

function scoreLabel(matchup: MatchupResponse) {
  if (matchup.player1Wins === null || matchup.player2Wins === null) return 'vs'
  return `${matchup.player1Wins}-${matchup.player2Wins}`
}

function pointsLabel(matchup: MatchupResponse) {
  if (matchup.player1MatchPoints === null || matchup.player2MatchPoints === null) return '-'
  return `${matchup.player1MatchPoints}-${matchup.player2MatchPoints}`
}

function replayHost(replayUrl: string) {
  try {
    return new URL(replayUrl).hostname.replace(/^www\./, '')
  } catch {
    return 'Replay'
  }
}
</script>

<template>
  <v-container fluid>
    <v-card class="page-card">
      <v-card-title class="page-header">
        <div>
          <div class="eyebrow">League</div>
          <h1>Schedule &amp; Standings</h1>
        </div>
        <v-btn-toggle
          v-model="showMyMatchesOnly"
          class="match-filter"
          mandatory
          density="compact"
          variant="outlined"
        >
          <v-btn :value="false">All Matches</v-btn>
          <v-btn :value="true">My Matches</v-btn>
        </v-btn-toggle>
      </v-card-title>

      <v-card-text>
        <div v-if="isLoading" class="state-panel">
          <PokeballLoader variant="page" label="Loading schedule..." />
        </div>
        <v-alert v-else-if="error" type="error" variant="tonal">{{ error }}</v-alert>
        <v-alert v-else-if="!schedule || !schedule.weeks.length" type="info" variant="tonal">
          The schedule will appear here once the draft is complete.
        </v-alert>

        <v-row v-else class="schedule-layout">
          <v-col cols="12" lg="6" xl="7">
            <v-expansion-panels v-model="openWeeks" multiple>
              <v-expansion-panel v-for="week in filteredWeeks" :key="week.week" :value="week.week">
                <v-expansion-panel-title>
                  <div class="week-title">
                    <span>Week {{ week.week }}</span>
                    <v-chip size="small" variant="tonal">
                      {{ completedCount(week) }}/{{ week.matchups.length }} played
                    </v-chip>
                  </div>
                </v-expansion-panel-title>
                <v-expansion-panel-text>
                  <v-data-table
                    :headers="matchupHeaders"
                    :items="week.matchups"
                    :items-per-page="-1"
                    class="matchup-table"
                    density="comfortable"
                    hide-default-footer
                    item-value="id"
                  >
                    <template #item.matchup="{ item }">
                      <div class="matchup-cell" :class="{ 'my-matchup': isMyMatchup(item) }">
                        <div class="team-pill" :class="{ winner: isWinner(item, 1) }">
                          <v-avatar size="34">
                            <v-img
                              v-if="item.player1TeamImageUrl"
                              :src="item.player1TeamImageUrl"
                              :alt="item.player1TeamName"
                            />
                            <span v-else>{{ avatarInitials(item.player1Name, item.player1TeamName) }}</span>
                          </v-avatar>
                          <span>{{ teamLabel(item.player1Name, item.player1TeamName) }}</span>
                        </div>
                        <span class="versus">vs</span>
                        <div class="team-pill right" :class="{ winner: isWinner(item, 2) }">
                          <span>{{ teamLabel(item.player2Name, item.player2TeamName) }}</span>
                          <v-avatar size="34">
                            <v-img
                              v-if="item.player2TeamImageUrl"
                              :src="item.player2TeamImageUrl"
                              :alt="item.player2TeamName"
                            />
                            <span v-else>{{ avatarInitials(item.player2Name, item.player2TeamName) }}</span>
                          </v-avatar>
                        </div>
                      </div>
                    </template>

                    <template #item.score="{ item }">
                      <v-chip :color="item.player1Wins === null ? undefined : 'primary'" size="small" variant="tonal">
                        {{ scoreLabel(item) }}
                      </v-chip>
                    </template>

                    <template #item.points="{ item }">
                      <span class="points-label">{{ pointsLabel(item) }}</span>
                    </template>

                    <template #item.replay="{ item }">
                      <v-btn
                        v-if="item.replayUrl"
                        :href="item.replayUrl"
                        target="_blank"
                        rel="noopener noreferrer"
                        size="small"
                        variant="tonal"
                      >
                        {{ replayHost(item.replayUrl) }}
                      </v-btn>
                      <span v-else class="muted">-</span>
                    </template>

                    <template #item.actions="{ item }">
                      <div class="table-actions">
                        <v-btn v-if="canReport(item)" size="small" variant="tonal" @click="openReport(item)">
                          Report
                        </v-btn>
                        <v-btn v-if="canEdit(item)" size="small" variant="text" @click="openEdit(item)">
                          Edit
                        </v-btn>
                      </div>
                    </template>
                  </v-data-table>
                </v-expansion-panel-text>
              </v-expansion-panel>
            </v-expansion-panels>
          </v-col>

          <v-col cols="12" lg="6" xl="5">
            <v-card class="standings-card">
              <v-card-title class="text-h6">Standings</v-card-title>
              <v-data-table
                :headers="standingsHeaders"
                :items="standingsRows"
                :items-per-page="-1"
                class="standings-table"
                density="compact"
                hide-default-footer
                item-value="playerId"
              >
                <template #item.team="{ item }">
                  <div class="standing-team" :class="{ mine: item.playerId === authStore.playerId }">
                    <v-avatar size="28">
                      <v-img v-if="item.teamImageUrl" :src="item.teamImageUrl" :alt="item.team" />
                      <span v-else>{{ avatarInitials(item.playerName, item.teamName) }}</span>
                    </v-avatar>
                    <span>{{ item.team }}</span>
                  </div>
                </template>
              </v-data-table>
            </v-card>
          </v-col>
        </v-row>
      </v-card-text>
    </v-card>

    <v-dialog :model-value="activeMatchup !== null" max-width="560" @update:model-value="(value) => !value && closeReport()">
      <v-card v-if="activeMatchup" class="report-card">
        <v-card-title>{{ isEditing ? 'Edit Score' : 'Report Score' }}</v-card-title>
        <v-card-text>
          <div class="report-grid">
            <div class="report-team">{{ teamLabel(activeMatchup.player1Name, activeMatchup.player1TeamName) }}</div>
            <v-number-input v-model="reportP1Wins" :min="0" :max="2" density="compact" variant="outlined" hide-details />
            <div class="report-team right">{{ teamLabel(activeMatchup.player2Name, activeMatchup.player2TeamName) }}</div>
            <v-number-input v-model="reportP2Wins" :min="0" :max="2" density="compact" variant="outlined" hide-details />
          </div>

          <v-text-field
            v-model="reportReplayUrl"
            class="replay-input"
            label="Replay link"
            placeholder="https://replay.pokemonshowdown.com/..."
            variant="outlined"
            density="compact"
            clearable
            hide-details
          />

          <v-alert v-if="reportError" type="error" variant="tonal" density="compact">
            {{ reportError }}
          </v-alert>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="tonal" @click="closeReport">Cancel</v-btn>
          <v-btn color="primary" :loading="reportLoading" @click="submitReport">Save</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-container>
</template>

<style scoped>

.page-card {
  border: 1px solid var(--border-color);
}

.page-header {
  align-items: center;
  display: flex;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}

.eyebrow {
  color: var(--text-muted);
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

h1 {
  color: var(--text);
  font-size: 1.8rem;
  font-weight: 800;
  line-height: 1.2;
  margin: 0;
}

.state-panel {
  display: flex;
  justify-content: center;
  padding: 48px 0;
}

.schedule-layout {
  align-items: start;
}

.week-title {
  align-items: center;
  display: flex;
  justify-content: space-between;
  width: 100%;
  gap: 12px;
  font-weight: 800;
}

.matchup-table,
.standings-table {
  background: var(--bg);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  overflow: hidden;
}

.matchup-table :deep(th),
.standings-table :deep(th) {
  background: var(--input-bg) !important;
  color: var(--text-muted) !important;
  font-size: 0.7rem;
  font-weight: 800 !important;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}

.matchup-cell {
  align-items: center;
  display: grid;
  gap: 10px;
  grid-template-columns: minmax(120px, 1fr) auto minmax(120px, 1fr);
  padding: 4px 0;
}

.matchup-cell.my-matchup {
  color: var(--primary);
}

.team-pill {
  align-items: center;
  display: flex;
  gap: 8px;
  min-width: 0;
}

.team-pill.right {
  justify-content: flex-end;
  text-align: right;
}

.team-pill span:last-child,
.team-pill span:first-child {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.team-pill.winner {
  color: var(--text);
  font-weight: 800;
}

.versus,
.muted {
  color: var(--text-muted);
}

.points-label {
  color: var(--text);
  font-weight: 700;
}

.table-actions {
  display: flex;
  justify-content: flex-end;
  gap: 6px;
}

.standings-card {
  border: 1px solid var(--border-color);
  position: sticky;
  top: 12px;
}

.standing-team {
  align-items: center;
  display: flex;
  gap: 8px;
  min-width: 0;
}

.standing-team span:last-child {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.standing-team.mine {
  color: var(--primary);
  font-weight: 800;
}

.report-grid {
  align-items: center;
  display: grid;
  gap: 10px;
  grid-template-columns: 1fr 96px;
  margin-bottom: 16px;
}

.report-team {
  color: var(--text);
  font-weight: 700;
}

.report-team.right {
  grid-column: 1;
}

.replay-input {
  margin-bottom: 12px;
}

@media (max-width: 1279px) {
  .standings-card {
    position: static;
  }
}

@media (max-width: 720px) {
  .matchup-cell {
    align-items: stretch;
    grid-template-columns: 1fr;
  }

  .team-pill.right {
    justify-content: flex-start;
    text-align: left;
  }

  .versus {
    display: none;
  }
}
</style>
