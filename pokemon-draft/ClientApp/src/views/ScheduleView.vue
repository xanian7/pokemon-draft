<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import PageHeader from '@/components/PageHeader.vue'
import FormField from '@/components/FormField.vue'
import DraftGateNotice from '@/components/DraftGateNotice.vue'
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
const reportReplayUrls = ref(['', '', ''])
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

const pointsProgression = computed(() => {
  if (!schedule.value) return []

  const latestReportedWeek = schedule.value.weeks.reduce(
    (latest, week) =>
      week.matchups.some((matchup) => matchup.player1MatchPoints !== null)
        ? Math.max(latest, week.week)
        : latest,
    0,
  )
  const reportedWeeks = schedule.value.weeks.filter((week) => week.week <= latestReportedWeek)

  return schedule.value.standings.map((standing) => {
    let total = 0
    const values: number[] = []

    for (const week of reportedWeeks) {
      for (const matchup of week.matchups) {
        if (matchup.player1Id === standing.playerId) {
          total += matchup.player1MatchPoints ?? 0
        } else if (matchup.player2Id === standing.playerId) {
          total += matchup.player2MatchPoints ?? 0
        }
      }
      values.push(total)
    }

    return {
      playerId: standing.playerId,
      label: teamLabel(standing.playerName, standing.teamName),
      values,
    }
  })
})

const chart = computed(() => {
  const width = 1000
  const height = 250
  const margin = { top: 14, right: 18, bottom: 40, left: 48 }
  const plotWidth = width - margin.left - margin.right
  const plotHeight = height - margin.top - margin.bottom
  const weekCount = pointsProgression.value[0]?.values.length ?? 0
  const maxPoints = Math.max(3, ...pointsProgression.value.flatMap((player) => player.values))
  const yMax = Math.ceil(maxPoints / 3) * 3
  const yTicks = Array.from({ length: yMax / 3 + 1 }, (_, index) => index * 3)
  const colors = ['#7c6cff', '#2ab6ff', '#35d39a', '#ffca62', '#ff5c7a', '#c084fc', '#fb923c', '#22d3ee']

  const x = (weekIndex: number) =>
    margin.left + (weekCount <= 1 ? plotWidth / 2 : (weekIndex / (weekCount - 1)) * plotWidth)
  const y = (points: number) => margin.top + plotHeight - (points / yMax) * plotHeight

  return {
    width,
    height,
    margin,
    plotWidth,
    plotHeight,
    weekCount,
    yMax,
    yTicks,
    x,
    y,
    series: pointsProgression.value.map((player, index) => ({
      ...player,
      color: colors[index % colors.length],
      path: player.values
        .map((points, weekIndex) => `${weekIndex === 0 ? 'M' : 'L'} ${x(weekIndex)} ${y(points)}`)
        .join(' '),
    })),
  }
})

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
  reportReplayUrls.value = ['', '', '']
  reportError.value = ''
  isEditing.value = false
}

function openEdit(matchup: MatchupResponse) {
  activeMatchup.value = matchup
  reportP1Wins.value = matchup.player1Wins ?? 2
  reportP2Wins.value = matchup.player2Wins ?? 0
  reportReplayUrls.value = paddedReplayUrls(getMatchupReplayUrls(matchup))
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

  const replayUrls = normalizedReportReplayUrls()
  if (replayUrls.length > 3) return 'A match report can include at most 3 replay links.'

  for (const replayUrl of replayUrls) {
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

    const replayUrls = normalizedReportReplayUrls()
    const replayUrl = replayUrls[0] ?? null
    const body = isEditing.value
      ? {
          adminPin: authStore.pin,
          player1Wins: reportP1Wins.value,
          player2Wins: reportP2Wins.value,
          replayUrl,
          replayUrls,
        }
      : {
          playerId: authStore.playerId,
          pin: authStore.pin,
          player1Wins: reportP1Wins.value,
          player2Wins: reportP2Wins.value,
          replayUrl,
          replayUrls,
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

function normalizedReportReplayUrls() {
  return reportReplayUrls.value.map((url) => url.trim()).filter(Boolean).slice(0, 3)
}

function paddedReplayUrls(replayUrls: string[]) {
  return [...replayUrls, '', '', ''].slice(0, 3)
}

function getMatchupReplayUrls(matchup: MatchupResponse) {
  if (matchup.replayUrls?.length) return matchup.replayUrls.slice(0, 3)
  return matchup.replayUrl ? [matchup.replayUrl] : []
}
</script>

<template>
  <v-container fluid>
    <div class="page-card">
      <PageHeader
        class="page-header"
        eyebrow="League"
        title="Schedule & Standings"
        subtitle="Weekly matchups, reported scores, and the current table."
      >
        <template #actions>
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
        </template>
      </PageHeader>

      <div class="page-content">
        <div v-if="isLoading" class="state-panel">
          <!-- <PokeballLoader variant="page" label="Loading schedule..." /> -->
        </div>
        <v-alert v-else-if="error" type="error" variant="tonal">{{ error }}</v-alert>
        <DraftGateNotice
          v-else-if="!schedule || !schedule.weeks.length"
          text="The schedule and standings will appear once the draft is complete."
        />

        <div v-else>
          <v-card class="progression-card">
            <v-card-title class="text-h6">Points Progression</v-card-title>
            <v-card-subtitle>Cumulative match points by week</v-card-subtitle>
            <v-card-text class="progression-content">
              <div v-if="chart.weekCount" class="points-chart">
                <svg
                  :viewBox="`0 0 ${chart.width} ${chart.height}`"
                  role="img"
                  aria-label="Player points by week"
                >
                  <g class="chart-grid">
                    <line
                      v-for="tick in chart.yTicks"
                      :key="`grid-${tick}`"
                      :x1="chart.margin.left"
                      :x2="chart.margin.left + chart.plotWidth"
                      :y1="chart.y(tick)"
                      :y2="chart.y(tick)"
                    />
                  </g>

                  <g class="chart-axis">
                    <line
                      :x1="chart.margin.left"
                      :x2="chart.margin.left"
                      :y1="chart.margin.top"
                      :y2="chart.margin.top + chart.plotHeight"
                    />
                    <line
                      :x1="chart.margin.left"
                      :x2="chart.margin.left + chart.plotWidth"
                      :y1="chart.margin.top + chart.plotHeight"
                      :y2="chart.margin.top + chart.plotHeight"
                    />
                  </g>

                  <g class="chart-labels">
                    <text
                      v-for="tick in chart.yTicks"
                      :key="`y-${tick}`"
                      :x="chart.margin.left - 12"
                      :y="chart.y(tick) + 4"
                      text-anchor="end"
                    >
                      {{ tick }}
                    </text>
                    <text
                      v-for="weekIndex in chart.weekCount"
                      :key="`x-${weekIndex}`"
                      :x="chart.x(weekIndex - 1)"
                      :y="chart.margin.top + chart.plotHeight + 24"
                      text-anchor="middle"
                    >
                      {{ weekIndex }}
                    </text>
                    <text
                      :x="chart.margin.left + chart.plotWidth / 2"
                      :y="chart.height - 8"
                      text-anchor="middle"
                      class="axis-title"
                    >
                      Week
                    </text>
                    <text
                      :x="16"
                      :y="chart.margin.top + chart.plotHeight / 2"
                      text-anchor="middle"
                      class="axis-title"
                      :transform="`rotate(-90 16 ${chart.margin.top + chart.plotHeight / 2})`"
                    >
                      Points
                    </text>
                  </g>

                  <g v-for="player in chart.series" :key="player.playerId">
                    <path
                      :d="player.path"
                      :stroke="player.color"
                      :class="{ 'my-chart-line': player.playerId === authStore.playerId }"
                      class="chart-line"
                    />
                    <circle
                      v-for="(points, weekIndex) in player.values"
                      :key="`${player.playerId}-${weekIndex}`"
                      :cx="chart.x(weekIndex)"
                      :cy="chart.y(points)"
                      :fill="player.color"
                      r="3.5"
                    >
                      <title>{{ player.label }} - Week {{ weekIndex + 1 }}: {{ points }} points</title>
                    </circle>
                  </g>
                </svg>
              </div>
              <div v-else class="chart-empty">The graph will appear after the first score is reported.</div>

              <div class="chart-legend">
                <div
                  v-for="player in chart.series"
                  :key="`legend-${player.playerId}`"
                  class="legend-item"
                  :class="{ mine: player.playerId === authStore.playerId }"
                >
                  <span class="legend-swatch" :style="{ backgroundColor: player.color }" />
                  <span>{{ player.label }}</span>
                </div>
              </div>
            </v-card-text>
          </v-card>

          <v-row class="schedule-layout">
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
                      <div v-if="getMatchupReplayUrls(item).length === 1" class="replay-links">
                        <v-btn
                          v-for="replayUrl in getMatchupReplayUrls(item)"
                          :key="replayUrl"
                          :href="replayUrl"
                          target="_blank"
                          rel="noopener noreferrer"
                          size="small"
                          variant="tonal"
                        >
                          {{ replayHost(replayUrl) }}
                        </v-btn>
                      </div>
                      <v-menu v-else-if="getMatchupReplayUrls(item).length > 1">
                        <template #activator="{ props }">
                          <v-btn v-bind="props" size="small" variant="tonal">
                            {{ getMatchupReplayUrls(item).length }} replays
                          </v-btn>
                        </template>
                        <v-list density="compact">
                          <v-list-item
                            v-for="(replayUrl, index) in getMatchupReplayUrls(item)"
                            :key="replayUrl"
                            :href="replayUrl"
                            target="_blank"
                            rel="noopener noreferrer"
                          >
                            <v-list-item-title>
                              Game {{ index + 1 }} - {{ replayHost(replayUrl) }}
                            </v-list-item-title>
                          </v-list-item>
                        </v-list>
                      </v-menu>
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
        </div>
      </div>
    </div>

    <v-dialog :model-value="activeMatchup !== null" max-width="560" @update:model-value="(value) => !value && closeReport()">
      <v-card v-if="activeMatchup" class="report-card">
        <v-card-title>{{ isEditing ? 'Edit Score' : 'Report Score' }}</v-card-title>
        <v-card-text>
          <div class="report-grid">
            <div class="report-team">{{ teamLabel(activeMatchup.player1Name, activeMatchup.player1TeamName) }}</div>
            <FormField label="Wins">
              <v-number-input v-model="reportP1Wins" :min="0" :max="2" class="score-input" hide-details />
            </FormField>
            <div class="report-team right">{{ teamLabel(activeMatchup.player2Name, activeMatchup.player2TeamName) }}</div>
            <FormField label="Wins">
              <v-number-input v-model="reportP2Wins" :min="0" :max="2" class="score-input" hide-details />
            </FormField>
          </div>

          <div class="replay-inputs">
            <FormField
              v-for="(_, index) in reportReplayUrls"
              :key="index"
              :label="`Replay Link ${index + 1}`"
            >
              <v-text-field
                v-model="reportReplayUrls[index]"
                class="replay-input"
                placeholder="https://replay.pokemonshowdown.com/..."
                clearable
                hide-details
              />
            </FormField>
          </div>

          <v-alert v-if="reportError" type="error" variant="tonal" density="compact">
            {{ reportError }}
          </v-alert>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn class="btn-secondary" @click="closeReport">Cancel</v-btn>
          <v-btn class="btn-primary" :loading="reportLoading" @click="submitReport">Save</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-container>
</template>

<style scoped>

.v-container {
  padding: 0;
}

.page-card {
  padding: 0 clamp(1rem, 2vw, 2rem);
}

.page-content {
  padding: 0;
}

.page-header {
  margin-bottom: 10px;
}

.state-panel {
  display: flex;
  justify-content: center;
  padding: 48px 0;
}

.schedule-layout {
  align-items: start;
  margin-top: 10px;
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
  background: transparent;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
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

.replay-links {
  display: flex;
  justify-content: center;
}

.standings-card {
  border: 1px solid var(--border-color);
}

.progression-card {
  border: 1px solid var(--border-color);
  box-shadow: 0 10px 30px rgb(0 0 0 / 18%);
}

.progression-card :deep(.v-card-title) {
  padding-bottom: 0;
  padding-top: 12px;
}

.progression-card :deep(.v-card-subtitle) {
  padding-bottom: 4px;
}

.progression-content {
  padding-bottom: 10px;
  padding-top: 4px;
}

.points-chart {
  width: 100%;
}

.points-chart svg {
  display: block;
  height: auto;
  margin: 0 auto;
  max-width: 1100px;
  width: 100%;
}

.chart-grid line {
  stroke: var(--border-color);
  stroke-width: 1;
}

.chart-axis line {
  stroke: var(--text-muted);
  stroke-width: 1.5;
}

.chart-labels text {
  fill: var(--text-muted);
  font-size: 11px;
}

.chart-labels .axis-title {
  fill: var(--text);
  font-size: 12px;
  font-weight: 700;
}

.chart-line {
  fill: none;
  opacity: 0.8;
  stroke-linecap: round;
  stroke-linejoin: round;
  stroke-width: 3;
}

.chart-line.my-chart-line {
  opacity: 1;
  stroke-width: 5;
}

.chart-empty {
  color: var(--text-muted);
  padding: 24px 0;
  text-align: center;
}

.chart-legend {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 14px;
  justify-content: center;
  margin-top: 4px;
}

.legend-item {
  align-items: center;
  color: var(--text-muted);
  display: flex;
  font-size: 0.76rem;
  gap: 6px;
}

.legend-item.mine {
  color: var(--primary);
  font-weight: 800;
}

.legend-swatch {
  border-radius: 999px;
  height: 4px;
  width: 20px;
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
  grid-template-columns: 1fr 140px;
  margin-bottom: 16px;
}

.score-input {
  width: 140px;
}

.report-team {
  color: var(--text);
  font-weight: 700;
}

.report-team.right {
  grid-column: 1;
}

.replay-inputs {
  display: grid;
  gap: 10px;
  margin-bottom: 12px;
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
