<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import type { MatchupResponse, ScheduleData } from '@/types'

const router = useRouter()
const authStore = useAuthStore()

if (!authStore.isAuthenticated) router.replace('/join')

const API_BASE = import.meta.env.VITE_API_BASE ?? ''

const schedule = ref<ScheduleData | null>(null)
const isLoading = ref(true)
const error = ref('')
const showMyMatchesOnly = ref(false)
const collapsedWeeks = ref<Set<number>>(new Set())

const reportingMatchupId = ref<number | null>(null)
const reportP1Wins = ref(2)
const reportP2Wins = ref(0)
const reportError = ref('')
const reportLoading = ref(false)
const isEditing = ref(false)

async function fetchSchedule() {
  if (!authStore.leagueCode) return

  try {
    error.value = ''
    const res = await fetch(`${API_BASE}/api/leagues/${authStore.leagueCode}/schedule`)
    if (!res.ok) {
      error.value = 'Could not load schedule.'
      return
    }

    schedule.value = (await res.json()) as ScheduleData

    const nextCollapsed = new Set<number>()
    const currentWeek = schedule.value.weeks.find((week) =>
      week.matchups.some((matchup) => matchup.player1Wins === null),
    )?.week

    if (currentWeek) {
      schedule.value.weeks.forEach((week) => {
        if (week.week !== currentWeek) nextCollapsed.add(week.week)
      })
    }

    collapsedWeeks.value = nextCollapsed
  } catch {
    error.value = 'Could not connect to server.'
  } finally {
    isLoading.value = false
  }
}

onMounted(fetchSchedule)

function toggleWeek(week: number) {
  const next = new Set(collapsedWeeks.value)
  if (next.has(week)) next.delete(week)
  else next.add(week)
  collapsedWeeks.value = next
}

const filteredWeeks = computed(() => {
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

function isMyMatchup(matchup: MatchupResponse) {
  return matchup.player1Id === authStore.playerId || matchup.player2Id === authStore.playerId
}

function startReport(matchup: MatchupResponse) {
  reportingMatchupId.value = matchup.id
  reportP1Wins.value = 2
  reportP2Wins.value = 0
  reportError.value = ''
  isEditing.value = false
}

function startEdit(matchup: MatchupResponse) {
  reportingMatchupId.value = matchup.id
  reportP1Wins.value = matchup.player1Wins ?? 2
  reportP2Wins.value = matchup.player2Wins ?? 0
  reportError.value = ''
  isEditing.value = true
}

function cancelReport() {
  reportingMatchupId.value = null
  reportError.value = ''
}

async function submitReport(matchup: MatchupResponse) {
  if (reportP1Wins.value < 0 || reportP2Wins.value < 0 || reportP1Wins.value > 2 || reportP2Wins.value > 2) {
    reportError.value = 'Wins must be between 0 and 2.'
    return
  }
  if (reportP1Wins.value + reportP2Wins.value > 3) {
    reportError.value = 'A best-of-3 cannot exceed 3 games.'
    return
  }
  if (reportP1Wins.value !== 2 && reportP2Wins.value !== 2) {
    reportError.value = 'One player must have 2 wins.'
    return
  }
  if (reportP1Wins.value === 2 && reportP2Wins.value === 2) {
    reportError.value = 'Both players cannot have 2 wins.'
    return
  }

  reportLoading.value = true
  reportError.value = ''

  try {
    const url = isEditing.value
      ? `${API_BASE}/api/leagues/${authStore.leagueCode}/schedule/${matchup.id}/edit`
      : `${API_BASE}/api/leagues/${authStore.leagueCode}/schedule/${matchup.id}/report`

    const body = isEditing.value
      ? { adminPin: authStore.pin, player1Wins: reportP1Wins.value, player2Wins: reportP2Wins.value }
      : { playerId: authStore.playerId, pin: authStore.pin, player1Wins: reportP1Wins.value, player2Wins: reportP2Wins.value }

    const res = await fetch(url, {
      method: isEditing.value ? 'PATCH' : 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })

    if (!res.ok) {
      const txt = await res.text()
      reportError.value = txt || 'Failed to report score.'
      return
    }

    reportingMatchupId.value = null
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
    .map((word: string) => word[0])
    .join('')
    .toUpperCase()
    .slice(0, 2)
}
</script>

<template>
  <main class="schedule-wrap">
    <div class="schedule-header">
      <div>
        <p class="eyebrow">League</p>
        <h1>Schedule &amp; Standings</h1>
      </div>
      <div class="filter-toggle">
        <button :class="['filter-btn', { active: !showMyMatchesOnly }]" @click="showMyMatchesOnly = false">
          All Matches
        </button>
        <button :class="['filter-btn', { active: showMyMatchesOnly }]" @click="showMyMatchesOnly = true">
          My Matches
        </button>
      </div>
    </div>

    <div v-if="isLoading" class="loading">Loading schedule…</div>
    <div v-else-if="error" class="error-msg">{{ error }}</div>
    <div v-else-if="!schedule || !schedule.weeks.length" class="empty-msg">
      The schedule will appear here once the draft is complete.
    </div>

    <div v-else class="schedule-layout">
      <div class="weeks-col">
        <div v-for="week in filteredWeeks" :key="week.week" class="week-section">
          <button class="week-header" @click="toggleWeek(week.week)">
            <span class="week-label">Week {{ week.week }}</span>
            <span class="week-meta">
              {{ week.matchups.filter((matchup) => matchup.player1Wins !== null).length }}/{{ week.matchups.length }} played
            </span>
            <span class="week-chevron" :class="{ open: !collapsedWeeks.has(week.week) }">&#9660;</span>
          </button>

          <div v-if="!collapsedWeeks.has(week.week)" class="week-matchups">
            <div
              v-for="matchup in week.matchups"
              :key="matchup.id"
              class="matchup-card"
              :class="{ 'my-matchup': isMyMatchup(matchup) }"
            >
              <div
                class="team-side"
                :class="{ winner: matchup.player1Wins !== null && matchup.player1Wins > matchup.player2Wins! }"
              >
                <div class="team-avatar">
                  <img
                    v-if="matchup.player1TeamImageUrl"
                    :src="matchup.player1TeamImageUrl"
                    :alt="matchup.player1TeamName"
                    class="avatar-img"
                  />
                  <div v-else class="avatar-initials">
                    {{ avatarInitials(matchup.player1Name, matchup.player1TeamName) }}
                  </div>
                </div>
                <span class="team-label-text">{{ teamLabel(matchup.player1Name, matchup.player1TeamName) }}</span>
                <span v-if="matchup.player1Wins !== null" class="score">{{ matchup.player1Wins }}</span>
              </div>

              <div class="vs-col">
                <span v-if="matchup.player1Wins === null" class="vs-text">vs</span>
                <template v-else>
                  <span class="match-pts-label">Match Pts</span>
                  <span class="match-pts">{{ matchup.player1MatchPoints }} – {{ matchup.player2MatchPoints }}</span>
                </template>
              </div>

              <div
                class="team-side right"
                :class="{ winner: matchup.player2Wins !== null && matchup.player2Wins > matchup.player1Wins! }"
              >
                <span v-if="matchup.player2Wins !== null" class="score">{{ matchup.player2Wins }}</span>
                <span class="team-label-text">{{ teamLabel(matchup.player2Name, matchup.player2TeamName) }}</span>
                <div class="team-avatar">
                  <img
                    v-if="matchup.player2TeamImageUrl"
                    :src="matchup.player2TeamImageUrl"
                    :alt="matchup.player2TeamName"
                    class="avatar-img"
                  />
                  <div v-else class="avatar-initials">
                    {{ avatarInitials(matchup.player2Name, matchup.player2TeamName) }}
                  </div>
                </div>
              </div>

              <div v-if="(isMyMatchup(matchup) && matchup.player1Wins === null) || authStore.isAdmin" class="report-area">
                <template v-if="reportingMatchupId === matchup.id">
                  <div class="report-form">
                    <p class="report-form-title">{{ isEditing ? '✏️ Edit Score' : 'Report Score' }}</p>
                    <div class="report-inputs">
                      <div class="report-player">
                        <span class="report-player-name">{{ teamLabel(matchup.player1Name, matchup.player1TeamName) }}</span>
                        <input v-model.number="reportP1Wins" type="number" min="0" max="2" class="wins-input" />
                      </div>
                      <span class="report-dash">–</span>
                      <div class="report-player">
                        <input v-model.number="reportP2Wins" type="number" min="0" max="2" class="wins-input" />
                        <span class="report-player-name">{{ teamLabel(matchup.player2Name, matchup.player2TeamName) }}</span>
                      </div>
                    </div>
                    <p v-if="reportError" class="report-error">{{ reportError }}</p>
                    <div class="report-actions">
                      <button class="cancel-btn" @click="cancelReport">Cancel</button>
                      <button class="submit-btn" :disabled="reportLoading" @click="submitReport(matchup)">
                        {{ reportLoading ? 'Saving…' : 'Submit' }}
                      </button>
                    </div>
                  </div>
                </template>
                <button v-else-if="matchup.player1Wins === null" class="report-btn" @click="startReport(matchup)">
                    Report Score
                  </button>
                  <button v-if="authStore.isAdmin && matchup.player1Wins !== null && reportingMatchupId !== matchup.id" class="edit-score-btn" @click="startEdit(matchup)">
                    ✏️ Edit Score
                  </button>
              </div>
            </div>
          </div>
        </div>
      </div>

      <aside class="standings-col">
        <div class="standings-card">
          <h2>Standings</h2>
          <table class="standings-table">
            <thead>
              <tr>
                <th>#</th>
                <th>Team</th>
                <th>W</th>
                <th>L</th>
                <th>Pts</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(row, index) in schedule.standings"
                :key="row.playerId"
                :class="{ 'my-row': row.playerId === authStore.playerId }"
              >
                <td class="rank">{{ index + 1 }}</td>
                <td class="team-cell">
                  <div class="standing-avatar">
                    <img v-if="row.teamImageUrl" :src="row.teamImageUrl" :alt="row.teamName" class="avatar-img" />
                    <div v-else class="avatar-initials sm">{{ avatarInitials(row.playerName, row.teamName) }}</div>
                  </div>
                  <span class="standing-name">{{ row.teamName || row.playerName }}</span>
                </td>
                <td class="stat">{{ row.wins }}</td>
                <td class="stat">{{ row.losses }}</td>
                <td class="stat pts">{{ row.matchPoints }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </aside>
    </div>
  </main>
</template>

<style scoped>
.schedule-wrap {
  max-width: 1200px;
  margin: 0 auto;
  padding: 2rem 1.25rem 4rem;
}

.schedule-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  margin-bottom: 1.75rem;
  gap: 1rem;
  flex-wrap: wrap;
}

.eyebrow {
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  font-size: 0.72rem;
  margin-bottom: 0.25rem;
}

h1 {
  font-size: 2rem;
  font-weight: 800;
  color: var(--text);
  margin: 0;
}

.filter-toggle {
  display: flex;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  overflow: hidden;
}

.filter-btn {
  border: none;
  background: none;
  padding: 0.5rem 1rem;
  color: var(--text-muted);
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  transition: background 0.15s, color 0.15s;
}

.filter-btn.active {
  background: var(--primary);
  color: white;
}

.loading,
.empty-msg {
  color: var(--text-muted);
  padding: 3rem 0;
  text-align: center;
}

.error-msg {
  color: #f87171;
  padding: 1rem;
}

.schedule-layout {
  display: grid;
  grid-template-columns: 1fr 280px;
  gap: 1.5rem;
  align-items: start;
}

@media (max-width: 860px) {
  .schedule-layout {
    grid-template-columns: 1fr;
  }

  .standings-col {
    order: -1;
  }
}

.week-section {
  border: 1px solid var(--border-color);
  border-radius: 16px;
  overflow: hidden;
  margin-bottom: 1rem;
  background: var(--card-bg);
}

.week-header {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.85rem 1.1rem;
  background: none;
  border: none;
  cursor: pointer;
  color: var(--text);
}

.week-header:hover {
  background: var(--input-bg);
}

.week-label {
  font-weight: 800;
  font-size: 1rem;
}

.week-meta {
  color: var(--text-muted);
  font-size: 0.8rem;
  flex: 1;
  text-align: right;
}

.week-chevron {
  color: var(--text-muted);
  font-size: 0.7rem;
  transition: transform 0.2s;
}

.week-chevron.open {
  transform: rotate(180deg);
}

.week-matchups {
  padding: 0.5rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.matchup-card {
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 0.75rem 1rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.matchup-card.my-matchup {
  border-color: rgba(204, 0, 0, 0.4);
  box-shadow: 0 0 0 1px rgba(204, 0, 0, 0.15);
}

.team-side {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  flex: 1;
  min-width: 100px;
}

.team-side.right {
  flex-direction: row-reverse;
  text-align: right;
}

.team-side.winner .team-label-text {
  font-weight: 800;
  color: var(--text);
}

.team-avatar {
  width: 34px;
  height: 34px;
  border-radius: 50%;
  overflow: hidden;
  flex-shrink: 0;
  border: 1px solid var(--border-color);
}

.avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.avatar-initials {
  width: 100%;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(59, 76, 202, 0.25);
  color: #a5b4fc;
  font-size: 0.65rem;
  font-weight: 800;
}

.team-label-text {
  font-size: 0.88rem;
  color: var(--text);
  font-weight: 600;
  line-height: 1.2;
}

.score {
  font-size: 1.4rem;
  font-weight: 800;
  color: var(--text);
  min-width: 1.5ch;
  text-align: center;
}

.vs-col {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.1rem;
  min-width: 56px;
}

.vs-text {
  color: var(--text-muted);
  font-size: 0.85rem;
  font-weight: 600;
}

.match-pts-label {
  font-size: 0.62rem;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.match-pts {
  font-size: 0.9rem;
  font-weight: 700;
  color: var(--text);
}

.report-area {
  width: 100%;
  margin-top: 0.5rem;
}

.report-btn {
  border: 1px solid var(--border-color);
  background: var(--card-bg);
  color: var(--text);
  border-radius: 8px;
  padding: 0.4rem 0.9rem;
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
}

.report-btn:hover {
  background: var(--input-bg);
}

.edit-score-btn {
  border: 1px solid var(--border-color);
  background: var(--card-bg);
  color: var(--text-muted);
  border-radius: 8px;
  padding: 0.35rem 0.75rem;
  font-size: 0.75rem;
  font-weight: 600;
  cursor: pointer;
  margin-left: 0.5rem;
}

.edit-score-btn:hover { background: var(--input-bg); color: var(--text); }

.report-form-title {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--text-muted);
  margin-bottom: 0.5rem;
}

.report-form {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  padding: 0.75rem;
}

.report-inputs {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}

.report-player {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex: 1;
}

.report-player-name {
  font-size: 0.82rem;
  color: var(--text);
  font-weight: 600;
}

.wins-input {
  width: 52px;
  text-align: center;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 0.35rem;
  color: var(--text);
  font-size: 1rem;
  font-weight: 700;
}

.report-dash {
  font-size: 1.2rem;
  color: var(--text-muted);
}

.report-error {
  color: #f87171;
  font-size: 0.8rem;
  margin-bottom: 0.4rem;
}

.report-actions {
  display: flex;
  gap: 0.5rem;
  justify-content: flex-end;
}

.cancel-btn,
.submit-btn {
  border: none;
  border-radius: 8px;
  padding: 0.4rem 0.85rem;
  font-size: 0.82rem;
  font-weight: 700;
  cursor: pointer;
}

.cancel-btn {
  background: var(--input-bg);
  color: var(--text);
  border: 1px solid var(--border-color);
}

.submit-btn {
  background: var(--primary);
  color: white;
}

.submit-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.standings-col {
  position: sticky;
  top: 1rem;
}

.standings-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 16px;
  padding: 1.25rem;
}

h2 {
  font-size: 1.1rem;
  font-weight: 800;
  color: var(--text);
  margin: 0 0 1rem;
}

.standings-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
}

.standings-table th {
  color: var(--text-muted);
  font-size: 0.72rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 0.3rem 0.5rem;
  text-align: left;
}

.standings-table td {
  padding: 0.5rem;
  border-top: 1px solid var(--border-color);
  vertical-align: middle;
}

.standings-table tr.my-row td {
  background: rgba(204, 0, 0, 0.06);
}

.rank {
  color: var(--text-muted);
  font-weight: 700;
  width: 1.5rem;
}

.team-cell {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  max-width: 160px;
}

.standing-avatar {
  width: 26px;
  height: 26px;
  border-radius: 50%;
  overflow: hidden;
  flex-shrink: 0;
}

.avatar-initials.sm {
  font-size: 0.55rem;
}

.standing-name {
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.stat {
  text-align: center;
  color: var(--text-muted);
  font-weight: 600;
}

.stat.pts {
  color: var(--primary);
  font-weight: 800;
}
</style>
