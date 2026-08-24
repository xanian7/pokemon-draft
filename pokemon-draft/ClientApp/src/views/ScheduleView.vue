<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import PageHeader from '@/components/PageHeader.vue'
import DraftGateNotice from '@/components/DraftGateNotice.vue'
import PointsProgressionChart from '@/components/PointsProgressionChart.vue'
import ScoreReportDialog from '@/components/ScoreReportDialog.vue'
import SectionHeader from '@/components/SectionHeader.vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import type { MatchupResponse, ScheduleData, StandingRow, WeekGroup } from '@/types'

const router = useRouter()
const authStore = useAuthStore()

if (!authStore.isAuthenticated) router.replace('/join')

const API_BASE = import.meta.env.VITE_API_BASE ?? ''

const schedule = ref<ScheduleData | null>(null)
const isLoading = ref(true)
const error = ref('')
const showMyMatchesOnly = ref(false)
const activeWeek = ref<number | null>(null)

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
    activeWeek.value = currentWeek ?? schedule.value.weeks[0]?.week ?? null
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

watch(filteredWeeks, (weeks) => {
  if (!weeks.some((week) => week.week === activeWeek.value)) {
    activeWeek.value = weeks[0]?.week ?? null
  }
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

function isMyMatchup(matchup: MatchupResponse) {
  return matchup.player1Id === authStore.playerId || matchup.player2Id === authStore.playerId
}

function canReport(matchup: MatchupResponse) {
  return (isMyMatchup(matchup) || authStore.isAdmin) && matchup.player1Wins === null
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
    <div class="page-card-small">
      <div class="page-content">
        <div v-if="isLoading" class="page-state">
        </div>
        <v-alert v-else-if="error" type="error" variant="tonal">{{ error }}</v-alert>
        <DraftGateNotice
          v-else-if="!schedule || !schedule.weeks.length"
          text="The schedule and standings will appear once the draft is complete."
        />

        <div v-else>
          <v-row>
            <v-col cols="12" lg="6" xl="7">
              <v-card class="progression-card section-card">
                <SectionHeader
                  eyebrow="Season performance"
                  title="Points Progression"
                  subtitle="Cumulative match points by week"
                  icon="mdi-chart-line"
                />
                <v-card-text class="progression-content">
                  <PointsProgressionChart
                    v-if="pointsProgression.length && pointsProgression[0]?.values.length"
                    :series="pointsProgression"
                    :current-player-id="authStore.playerId"
                  />
                  <div v-else class="chart-empty">
                    The graph will appear after the first score is reported.
                  </div>
                </v-card-text>
              </v-card>
            </v-col>
            <v-col cols="12" lg="6" xl="5">
              <v-card class="standings-card section-card">
                <SectionHeader
                  eyebrow="League table"
                  title="Standings"
                  subtitle="Current record and match points"
                  icon="mdi-podium"
                />
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

          <v-row class="content-divider">
            <v-col cols="12">
              <v-divider class="border-opacity-25"></v-divider>
            </v-col>
          </v-row>

          <v-row class="schedule-layout">
            <v-col cols="12">
            <div class="week-schedule">
              <v-tabs v-model="activeWeek" class="week-tabs" density="comfortable" show-arrows>
                <v-tab v-for="week in filteredWeeks" :key="week.week" :value="week.week">
                  Week {{ week.week }}
                </v-tab>
              </v-tabs>

              <v-tabs-window v-model="activeWeek" class="week-window">
                <v-tabs-window-item
                  v-for="week in filteredWeeks"
                  :key="week.week"
                  :value="week.week"
                >
                  <section class="week-panel">
                    <SectionHeader
                      class="week-section-header"
                      eyebrow="Schedule"
                      :title="'Week ' + week.week"
                      subtitle="League matchups and reported results"
                    >
                      <template #actions>
                        <v-chip size="small" variant="tonal">
                          {{ completedCount(week) }}/{{ week.matchups.length }} played
                        </v-chip>
                      </template>
                    </SectionHeader>

                    <div class="matchup-grid">
                      <v-card
                        v-for="matchup in week.matchups"
                        :key="matchup.id"
                        class="matchup-card section-card"
                        :class="{ 'matchup-card--mine': isMyMatchup(matchup) }"
                      >
                        <v-card-text class="matchup-card__content">
                          <div class="matchup-card__status">
                            <v-chip
                              :color="matchup.player1Wins === null ? undefined : 'success'"
                              size="x-small"
                              variant="tonal"
                            >
                              {{ matchup.player1Wins === null ? 'Upcoming' : 'Final' }}
                            </v-chip>
                          </div>

                          <div class="matchup-card__teams">
                            <div
                              class="matchup-team"
                              :class="{ 'matchup-team--winner': isWinner(matchup, 1) }"
                            >
                              <v-avatar size="48" class="matchup-team__avatar">
                                <v-img
                                  v-if="matchup.player1TeamImageUrl"
                                  :src="matchup.player1TeamImageUrl"
                                  :alt="matchup.player1TeamName"
                                />
                                <span v-else>
                                  {{ avatarInitials(matchup.player1Name, matchup.player1TeamName) }}
                                </span>
                              </v-avatar>
                              <strong>{{ teamLabel(matchup.player1Name, matchup.player1TeamName) }}</strong>
                              <small v-if="matchup.player1TeamName">{{ matchup.player1Name }}</small>
                            </div>

                            <div class="matchup-score">
                              <strong>{{ scoreLabel(matchup) }}</strong>
                              <span v-if="matchup.player1MatchPoints !== null">
                                {{ pointsLabel(matchup) }} pts
                              </span>
                            </div>

                            <div
                              class="matchup-team matchup-team--right"
                              :class="{ 'matchup-team--winner': isWinner(matchup, 2) }"
                            >
                              <v-avatar size="48" class="matchup-team__avatar">
                                <v-img
                                  v-if="matchup.player2TeamImageUrl"
                                  :src="matchup.player2TeamImageUrl"
                                  :alt="matchup.player2TeamName"
                                />
                                <span v-else>
                                  {{ avatarInitials(matchup.player2Name, matchup.player2TeamName) }}
                                </span>
                              </v-avatar>
                              <strong>{{ teamLabel(matchup.player2Name, matchup.player2TeamName) }}</strong>
                              <small v-if="matchup.player2TeamName">{{ matchup.player2Name }}</small>
                            </div>
                          </div>

                          <v-divider />

                          <div class="matchup-card__footer">
                            <div class="matchup-replays">
                              <v-btn
                                v-for="(replayUrl, index) in getMatchupReplayUrls(matchup)"
                                :key="replayUrl"
                                :href="replayUrl"
                                target="_blank"
                                rel="noopener noreferrer"
                                size="small"
                                variant="text"
                                append-icon="mdi-open-in-new"
                              >
                                {{ getMatchupReplayUrls(matchup).length > 1 ? `Game ${index + 1}` : replayHost(replayUrl) }}
                              </v-btn>
                              <span v-if="!getMatchupReplayUrls(matchup).length" class="muted">
                                No replay submitted
                              </span>
                            </div>

                            <div class="matchup-actions">
                              <v-btn
                                v-if="canReport(matchup)"
                                size="small"
                                color="primary"
                                variant="tonal"
                                @click="openReport(matchup)"
                              >
                                Report score
                              </v-btn>
                              <v-btn
                                v-if="canEdit(matchup)"
                                size="small"
                                variant="text"
                                @click="openEdit(matchup)"
                              >
                                Edit
                              </v-btn>
                            </div>
                          </div>
                        </v-card-text>
                      </v-card>
                    </div>
                  </section>
                </v-tabs-window-item>
              </v-tabs-window>
            </div>
          </v-col>

          </v-row>
        </div>
      </div>
    </div>

    <ScoreReportDialog
      v-if="activeMatchup"
      :model-value="activeMatchup !== null"
      :title="isEditing ? 'Edit Score' : 'Report Score'"
      :subtitle="'Week ' + activeMatchup.week"
      :left-label="teamLabel(activeMatchup.player1Name, activeMatchup.player1TeamName) + ' wins'"
      :right-label="teamLabel(activeMatchup.player2Name, activeMatchup.player2TeamName) + ' wins'"
      :left-wins="reportP1Wins"
      :right-wins="reportP2Wins"
      :replay-urls="reportReplayUrls"
      :error="reportError"
      :loading="reportLoading"
      :submit-label="isEditing ? 'Save changes' : 'Submit score'"
      @update:model-value="(value) => !value && closeReport()"
      @update:left-wins="reportP1Wins = $event"
      @update:right-wins="reportP2Wins = $event"
      @update:replay-urls="reportReplayUrls = $event"
      @submit="submitReport"
    />
  </v-container>
</template>

<style scoped>

.v-container {
  padding: 0;
}

.page-content {
  padding: 0;
}



.schedule-layout {
  align-items: start;
  margin-top: 10px;
}

.week-schedule {
  min-width: 0;
}

.week-tabs {
  border-bottom: 1px solid var(--border-color);
}

.week-tabs :deep(.v-tab) {
  min-width: max-content;
  color: var(--text-muted);
  font-weight: 700;
  letter-spacing: 0;
  text-transform: none;
}

.week-tabs :deep(.v-tab.v-tab--selected) {
  color: var(--primary-bright);
  background: rgba(var(--primary-rgb), 0.1);
}

.week-window {
  margin-top: 10px;
}

.week-panel {
  min-width: 0;
}

.week-section-header {
  margin-bottom: 12px;
}


.matchup-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 360px), 1fr));
  gap: 12px;
}

.matchup-card {
  min-width: 0;
  height: 100%;
  border: 1px solid var(--border-color);
  border-radius: 8px !important;
}

.matchup-card--mine {
  border-color: rgba(var(--primary-rgb), 0.62);
  box-shadow: inset 3px 0 0 var(--primary);
}

.matchup-card__content {
  display: flex;
  flex-direction: column;
  gap: 14px;
  height: 100%;
  padding: 14px;
}

.matchup-card__status {
  display: flex;
  justify-content: flex-end;
  min-height: 24px;
}

.matchup-card__teams {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  align-items: center;
  gap: 12px;
}

.matchup-team {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  min-width: 0;
  text-align: center;
}

.matchup-team strong,
.matchup-team small {
  width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.matchup-team strong {
  color: var(--text);
  font-size: 0.9rem;
  font-weight: 750;
}

.matchup-team small {
  color: var(--text-muted);
  font-size: 0.72rem;
}

.matchup-team--winner strong {
  color: var(--success);
}

.matchup-team__avatar {
  flex: 0 0 auto;
  border: 1px solid var(--border-color);
}

.matchup-score {
  display: flex;
  flex-direction: column;
  align-items: center;
  min-width: 52px;
  color: var(--text-muted);
}

.matchup-score strong {
  color: var(--text);
  font-size: 1.35rem;
  font-weight: 800;
  line-height: 1.1;
}

.matchup-score span {
  margin-top: 4px;
  font-size: 0.7rem;
  white-space: nowrap;
}

.matchup-card__footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  min-height: 32px;
  margin-top: auto;
}

.matchup-replays,
.matchup-actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 4px;
}

.muted {
  color: var(--text-muted);
  font-size: 0.76rem;
}

.standings-card {
  border: 1px solid var(--border-color);
}

.progression-card {
  border: 1px solid var(--border-color);
}


.progression-content {
  padding-bottom: 10px;
  padding-top: 4px;
}

.chart-empty {
  color: var(--text-muted);
  padding: 48px 0;
  text-align: center;
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


@media (max-width: 720px) {

  .matchup-grid {
    grid-template-columns: 1fr;
  }

  .matchup-card__content {
    padding: 12px;
  }

  .matchup-card__teams {
    gap: 8px;
  }

  .matchup-card__footer {
    align-items: flex-start;
    flex-direction: column;
  }

  .matchup-actions {
    width: 100%;
    justify-content: flex-end;
  }
}
</style>
