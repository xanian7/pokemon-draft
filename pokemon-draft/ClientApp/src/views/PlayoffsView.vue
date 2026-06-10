<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import PokeballLoader from '@/components/PokeballLoader.vue'
import AppIcon from '@/components/AppIcon.vue'
import { API_BASE } from '@/services/signalr'
import { mdiTrophy, mdiCheckCircle, mdiMinusCircle, mdiCircleOutline } from '@mdi/js'

const router = useRouter()
const authStore = useAuthStore()

if (!authStore.isAuthenticated) router.replace('/join')

interface OutlookEntry {
  playerId: string
  playerName: string
  teamName: string
  teamImageUrl: string
  wins: number
  losses: number
  matchPoints: number
  gamesWon: number
  gamesLost: number
  remainingMatchups: number
  maxPossibleWins: number
  magicNumber: number | null
  status: 'Clinched' | 'InContention' | 'Eliminated'
}

const outlook = ref<OutlookEntry[]>([])
const playoffSpots = ref(4)
const isLoading = ref(true)
const error = ref('')
const draftComplete = ref(false)

onMounted(async () => {
  try {
    const [leagueRes, outlookRes] = await Promise.all([
      fetch(`${API_BASE}/leagues/${authStore.leagueCode}`),
      fetch(`${API_BASE}/leagues/${authStore.leagueCode}/playoff-outlook`),
    ])

    if (leagueRes.ok) {
      const lg = await leagueRes.json()
      playoffSpots.value = lg.playoffSpots ?? 4
      draftComplete.value = lg.draft?.status?.toLowerCase() === 'complete'
    }

    if (!outlookRes.ok) {
      error.value = 'Could not load playoff outlook.'
      return
    }

    outlook.value = await outlookRes.json()
  } catch {
    error.value = 'Could not connect to server.'
  } finally {
    isLoading.value = false
  }
})

const statusIcon = (status: string) => {
  if (status === 'Clinched') return mdiCheckCircle
  if (status === 'Eliminated') return mdiMinusCircle
  return mdiCircleOutline
}

const statusColor = (status: string) => {
  if (status === 'Clinched') return '#10b981'
  if (status === 'Eliminated') return '#ef4444'
  return '#f59e0b'
}

const statusLabel = (status: string) => {
  if (status === 'Clinched') return 'Clinched'
  if (status === 'Eliminated') return 'Eliminated'
  return 'In Contention'
}

const teamLabel = (entry: OutlookEntry) => entry.teamName || entry.playerName

const rowClass = (entry: OutlookEntry) => ({
  'row-clinched': entry.status === 'Clinched',
  'row-eliminated': entry.status === 'Eliminated',
  'row-my-team': entry.playerId === authStore.playerId,
})

const seasonOver = computed(() => outlook.value.every((e) => e.remainingMatchups === 0))
</script>

<template>
  <main class="playoffs-page">
    <div class="page-header">
      <AppIcon :path="mdiTrophy" :size="26" class="header-icon" />
      <div>
        <h1>Playoff Outlook</h1>
        <p class="subtitle">Top {{ playoffSpots }} teams advance to playoffs</p>
      </div>
    </div>

    <div v-if="isLoading" class="loading">
      <PokeballLoader variant="page" label="Loading outlook…" />
    </div>
    <div v-else-if="error" class="error-msg">{{ error }}</div>
    <div v-else-if="!draftComplete" class="empty-msg">
      The playoff outlook will be available once the draft is complete.
    </div>
    <div v-else-if="outlook.length === 0" class="empty-msg">No schedule data yet.</div>

    <template v-else>
      <div v-if="seasonOver" class="season-over-banner">
        🏆 Regular season complete — final standings locked in.
      </div>

      <!-- Legend -->
      <div class="legend">
        <span class="legend-item clinched">
          <AppIcon :path="mdiCheckCircle" :size="14" />Clinched
        </span>
        <span class="legend-item contention">
          <AppIcon :path="mdiCircleOutline" :size="14" />In Contention
        </span>
        <span class="legend-item eliminated">
          <AppIcon :path="mdiMinusCircle" :size="14" />Eliminated
        </span>
      </div>

      <!-- Standings table -->
      <div class="table-wrap">
        <table class="outlook-table">
          <thead>
            <tr>
              <th class="col-rank">#</th>
              <th class="col-team">Team</th>
              <th class="col-num">W</th>
              <th class="col-num">L</th>
              <th class="col-num">MP</th>
              <th class="col-num">Rem</th>
              <th class="col-num">Max W</th>
              <th class="col-magic">Magic #</th>
              <th class="col-status">Status</th>
            </tr>
          </thead>
          <tbody>
            <template v-for="(entry, index) in outlook" :key="entry.playerId">
              <!-- Playoff cutline divider -->
              <tr
                v-if="index === playoffSpots && outlook.length > playoffSpots"
                class="cutline-row"
              >
                <td colspan="9">
                  <div class="cutline-label">— Playoff Cutline —</div>
                </td>
              </tr>
              <tr :class="['data-row', rowClass(entry)]">
                <td class="col-rank">{{ index + 1 }}</td>
                <td class="col-team">
                  <div class="team-cell">
                    <div class="team-avatar">
                      <img
                        v-if="entry.teamImageUrl"
                        :src="entry.teamImageUrl"
                        :alt="teamLabel(entry)"
                        class="avatar-img"
                      />
                      <div v-else class="avatar-initials">
                        {{ teamLabel(entry).slice(0, 2).toUpperCase() }}
                      </div>
                    </div>
                    <div class="team-names">
                      <span class="team-name">{{ teamLabel(entry) }}</span>
                      <span v-if="entry.teamName" class="player-name">{{ entry.playerName }}</span>
                      <span v-if="entry.playerId === authStore.playerId" class="you-tag">You</span>
                    </div>
                  </div>
                </td>
                <td class="col-num bold">{{ entry.wins }}</td>
                <td class="col-num">{{ entry.losses }}</td>
                <td class="col-num">{{ entry.matchPoints }}</td>
                <td class="col-num muted">{{ entry.remainingMatchups }}</td>
                <td class="col-num">{{ entry.maxPossibleWins }}</td>
                <td class="col-magic">
                  <span v-if="entry.magicNumber !== null" class="magic-pill">{{
                    entry.magicNumber
                  }}</span>
                  <span v-else class="muted">—</span>
                </td>
                <td class="col-status">
                  <div class="status-chip" :style="{ color: statusColor(entry.status) }">
                    <AppIcon :path="statusIcon(entry.status)" :size="14" />
                    {{ statusLabel(entry.status) }}
                  </div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>

      <div class="explainer">
        <strong>Magic #</strong> = wins needed to clinch a playoff spot. <strong>Max W</strong> =
        wins if they win every remaining game.
      </div>
    </template>
  </main>
</template>

<style scoped>
.playoffs-page {
  width: 100%;
  max-width: none;
  margin: 0;
  padding: 2rem clamp(1rem, 2vw, 2rem);
}

.page-header {
  display: flex;
  align-items: center;
  gap: 0.85rem;
  margin-bottom: 1.5rem;
}

.header-icon {
  color: var(--primary);
  flex-shrink: 0;
}
h1 {
  font-size: 1.5rem;
  font-weight: 800;
}
.subtitle {
  font-size: 0.875rem;
  color: var(--text-muted);
}

.loading {
  display: flex;
  justify-content: center;
  padding: 3rem 0;
}
.error-msg {
  color: var(--secondary);
  padding: 1rem 0;
}
.empty-msg {
  text-align: center;
  color: var(--text-muted);
  padding: 3rem 0;
}

.season-over-banner {
  background: color-mix(in srgb, #10b981 12%, transparent);
  border: 1px solid #10b981;
  border-radius: 8px;
  padding: 0.65rem 1rem;
  font-weight: 600;
  margin-bottom: 1.25rem;
  font-size: 0.9rem;
}

.legend {
  display: flex;
  gap: 1.25rem;
  margin-bottom: 1rem;
  flex-wrap: wrap;
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  font-size: 0.8rem;
  font-weight: 600;
}

.legend-item.clinched {
  color: #10b981;
}
.legend-item.contention {
  color: #f59e0b;
}
.legend-item.eliminated {
  color: #ef4444;
}

.table-wrap {
  overflow-x: auto;
  margin-bottom: 1rem;
}

.outlook-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
}

.outlook-table th {
  padding: 0.6rem 0.75rem;
  text-align: left;
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-muted);
  border-bottom: 1px solid var(--border-color);
  white-space: nowrap;
}

.outlook-table th.col-num,
.outlook-table td.col-num {
  text-align: center;
}

.data-row td {
  padding: 0.65rem 0.75rem;
  border-bottom: 1px solid var(--border-color);
  vertical-align: middle;
}

.data-row.row-clinched {
  background: color-mix(in srgb, #10b981 6%, transparent);
}
.data-row.row-eliminated {
  background: color-mix(in srgb, #ef4444 5%, transparent);
  opacity: 0.75;
}
.data-row.row-my-team {
  outline: 2px solid var(--primary);
  outline-offset: -2px;
}

.data-row:hover {
  background: var(--input-bg);
}

.cutline-row td {
  padding: 0;
  border: none;
}
.cutline-label {
  text-align: center;
  font-size: 0.72rem;
  font-weight: 700;
  color: var(--text-muted);
  padding: 0.4rem 0;
  border-top: 2px dashed var(--border-color);
  border-bottom: 2px dashed var(--border-color);
  letter-spacing: 0.08em;
}

.col-rank {
  width: 36px;
  font-weight: 700;
  color: var(--text-muted);
}

.team-cell {
  display: flex;
  align-items: center;
  gap: 0.6rem;
}

.team-avatar {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  overflow: hidden;
  background: var(--input-bg);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.avatar-initials {
  font-size: 0.72rem;
  font-weight: 700;
  color: var(--text-muted);
}

.team-names {
  display: flex;
  flex-direction: column;
  gap: 0.05rem;
}

.team-name {
  font-weight: 600;
  white-space: nowrap;
}
.player-name {
  font-size: 0.72rem;
  color: var(--text-muted);
}
.you-tag {
  display: inline-block;
  background: var(--primary);
  color: #fff;
  border-radius: 3px;
  padding: 0.05rem 0.3rem;
  font-size: 0.65rem;
  font-weight: 700;
}

.bold {
  font-weight: 700;
}
.muted {
  color: var(--text-muted);
}

.magic-pill {
  display: inline-block;
  background: var(--primary-hover-bg);
  border: 1px solid var(--primary);
  color: var(--primary);
  border-radius: 4px;
  padding: 0.1rem 0.45rem;
  font-size: 0.78rem;
  font-weight: 700;
}

.status-chip {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  font-size: 0.78rem;
  font-weight: 600;
  white-space: nowrap;
}

.explainer {
  font-size: 0.8rem;
  color: var(--text-muted);
  line-height: 1.5;
}
</style>
