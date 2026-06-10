<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import PokeballLoader from '@/components/PokeballLoader.vue'
import PageHeader from '@/components/PageHeader.vue'
import { API_BASE } from '@/services/signalr'

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

const statusLabel = (status: string) => {
  if (status === 'Clinched') return 'Clinched'
  if (status === 'Eliminated') return 'Eliminated'
  return 'In Contention'
}

const teamLabel = (entry: OutlookEntry) => entry.teamName || entry.playerName

const seasonOver = computed(() => outlook.value.every((e) => e.remainingMatchups === 0))
const clinchedCount = computed(() => outlook.value.filter((entry) => entry.status === 'Clinched').length)
const remainingGames = computed(() =>
  outlook.value.reduce((total, entry) => total + entry.remainingMatchups, 0) / 2,
)
const headers = [
  { title: 'Seed', key: 'rank', width: 72 },
  { title: 'Team', key: 'team' },
  { title: 'Record', key: 'record', align: 'center' as const },
  { title: 'Match pts', key: 'matchPoints', align: 'center' as const },
  { title: 'Remaining', key: 'remainingMatchups', align: 'center' as const },
  { title: 'Max wins', key: 'maxPossibleWins', align: 'center' as const },
  { title: 'Magic #', key: 'magicNumber', align: 'center' as const },
  { title: 'Status', key: 'status', align: 'end' as const },
]
</script>

<template>
  <v-container fluid class="playoffs-page">
    <PageHeader
      class="page-hero"
      eyebrow="Postseason race"
      title="Playoff Outlook"
      :subtitle="`Top ${playoffSpots} teams qualify for the bracket.`"
    >
      <template #actions>
        <v-avatar color="primary" variant="tonal" size="56">
          <v-icon icon="mdi-trophy-outline" size="30" />
        </v-avatar>
      </template>
    </PageHeader>

    <div v-if="isLoading" class="state-panel">
      <!-- <PokeballLoader variant="page" label="Loading outlook…" /> -->
    </div>
    <v-alert v-else-if="error" type="error" variant="tonal">{{ error }}</v-alert>
    <v-empty-state
      v-else-if="!draftComplete"
      icon="mdi-trophy-outline"
      title="The race has not started"
      text="Playoff projections unlock once the draft is complete."
    />
    <v-empty-state
      v-else-if="outlook.length === 0"
      icon="mdi-calendar-blank-outline"
      title="No schedule data yet"
    />

    <template v-else>
      <v-alert v-if="seasonOver" type="success" variant="tonal" class="mb-4">
        Regular season complete. Final standings are locked in.
      </v-alert>

      <v-row dense class="summary-grid">
        <v-col cols="4">
          <v-card variant="outlined" class="summary-card">
            <v-card-text><span>Playoff spots</span><strong>{{ playoffSpots }}</strong></v-card-text>
          </v-card>
        </v-col>
        <v-col cols="4">
          <v-card variant="outlined" class="summary-card">
            <v-card-text><span>Clinched</span><strong>{{ clinchedCount }}</strong></v-card-text>
          </v-card>
        </v-col>
        <v-col cols="4">
          <v-card variant="outlined" class="summary-card">
            <v-card-text><span>Games left</span><strong>{{ remainingGames }}</strong></v-card-text>
          </v-card>
        </v-col>
      </v-row>

      <v-card variant="outlined" class="outlook-card">
        <v-data-table :headers="headers" :items="outlook" item-value="playerId" hide-default-footer>
          <template #item.rank="{ index, item }">
            <v-chip :color="index < playoffSpots ? 'success' : undefined" size="small" variant="tonal">
              #{{ index + 1 }}
            </v-chip>
          </template>
          <template #item.team="{ item }">
            <div class="team-cell">
              <v-avatar size="36" color="surface">
                <v-img v-if="item.teamImageUrl" :src="item.teamImageUrl" :alt="teamLabel(item)" />
                <span v-else>{{ teamLabel(item).slice(0, 2).toUpperCase() }}</span>
              </v-avatar>
              <div>
                <strong>{{ teamLabel(item) }}</strong>
                <span>{{ item.teamName ? item.playerName : '' }}</span>
              </div>
              <v-chip v-if="item.playerId === authStore.playerId" size="x-small" color="primary">You</v-chip>
            </div>
          </template>
          <template #item.record="{ item }"><strong>{{ item.wins }}–{{ item.losses }}</strong></template>
          <template #item.magicNumber="{ item }">
            <v-chip v-if="item.magicNumber !== null" size="small" color="primary" variant="tonal">
              {{ item.magicNumber }}
            </v-chip>
            <span v-else>—</span>
          </template>
          <template #item.status="{ item }">
            <v-chip
              size="small"
              :color="item.status === 'Clinched' ? 'success' : item.status === 'Eliminated' ? 'error' : 'warning'"
              variant="tonal"
            >
              {{ statusLabel(item.status) }}
            </v-chip>
          </template>
        </v-data-table>
        <v-card-text class="explainer">
          <strong>Magic #</strong> is the number of wins needed to clinch. <strong>Max wins</strong>
          assumes a team wins every remaining matchup.
        </v-card-text>
      </v-card>
    </template>
  </v-container>
</template>

<style scoped>
.playoffs-page {
  padding: clamp(1rem, 2vw, 2rem);
}
.page-hero {
  margin-bottom: 16px;
}
.hero-content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}
.hero-content h1 {
  margin-top: 3px;
  font-size: clamp(1.5rem, 3vw, 2.1rem);
  font-weight: 800;
}
.hero-content p,
.summary-card span,
.team-cell span,
.explainer {
  color: var(--text-muted);
  font-size: 0.78rem;
}
.state-panel {
  display: flex;
  justify-content: center;
  padding: 48px;
}
.summary-grid {
  margin-bottom: 12px;
}
.summary-card .v-card-text {
  display: flex;
  flex-direction: column;
  gap: 4px;
  text-align: center;
}
.summary-card strong {
  font-size: 1.3rem;
}
.outlook-card {
  overflow: hidden;
}
.team-cell {
  display: flex;
  align-items: center;
  gap: 10px;
}
.explainer {
  border-top: 1px solid var(--border-color);
}
@media (max-width: 700px) {
  .playoffs-page {
    padding: 12px;
  }
  .outlook-card :deep(.v-data-table__wrapper) {
    overflow-x: auto;
  }
}
</style>
