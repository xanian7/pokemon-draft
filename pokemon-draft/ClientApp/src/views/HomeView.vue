<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import AppIcon from '@/components/AppIcon.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import { API_BASE } from '@/services/signalr'
import {
  mdiTrophy,
  mdiCog,
  mdiClipboardList,
  mdiCheckCircle,
  mdiCircleOutline,
  mdiCards,
  mdiAccountGroup,
  mdiStarCircle,
  mdiPlayCircle,
  mdiAccountPlus,
  mdiPlusCircle,
  mdiPokeball,
  mdiCalendarCheck,
  mdiAccountMultiple,
  mdiChartLine,
  mdiArrowRight,
} from '@mdi/js'

const router = useRouter()
const authStore = useAuthStore()
const pokemonStore = usePokemonStore()

// ── Live league data from API ────────────────────────────────────────────────
const league = ref<any>(null)

async function fetchLeague() {
  if (!authStore.leagueCode) return
  try {
    const res = await fetch(`${API_BASE}/leagues/${authStore.leagueCode}`)
    if (res.ok) league.value = await res.json()
  } catch {
    // silent — dashboard degrades gracefully
  }
}

onMounted(async () => {
  if (authStore.isAuthenticated) {
    await pokemonStore.fetchAllPokemon()
    await fetchLeague()
    if (draftStatus.value === 'complete') {
      fetchPostDraftData()
    }
  }
})

// ── Normalise API status (e.g. "Setup") to lowercase ────────────────────────
const draftStatus = computed<'setup' | 'active' | 'complete'>(() => {
  const s = (league.value?.draft?.status ?? 'Setup').toLowerCase()
  return s as 'setup' | 'active' | 'complete'
})

// ── Admin setup checklist ────────────────────────────────────────────────────
const hasPlayers = computed(() => (league.value?.players?.length ?? 0) >= 2)
const hasPointValues = computed(() => Object.keys(league.value?.pointValues ?? {}).length > 0)
const draftReady = computed(() => hasPlayers.value && hasPointValues.value)

const setupSteps = computed(() => [
  {
    label: 'Add players',
    done: hasPlayers.value,
    detail: hasPlayers.value ? `${league.value.players.length} players` : 'Minimum 2 required',
    action: () => router.push('/league?tab=setup'),
  },
  {
    label: 'Assign point values',
    done: hasPointValues.value,
    detail: hasPointValues.value
      ? `${Object.keys(league.value.pointValues).length} Pokémon valued`
      : 'None set yet',
    action: () => router.push('/league?tab=pokemon'),
  },
  {
    label: 'Start the draft',
    done: draftStatus.value !== 'setup',
    detail:
      draftStatus.value === 'active'
        ? 'In progress'
        : draftStatus.value === 'complete'
          ? 'Draft complete'
          : 'Ready when you are',
    action: () => router.push('/league?tab=draft'),
  },
])

// ── Player team snapshot ─────────────────────────────────────────────────────
const myPicks = computed(() => {
  if (!authStore.playerId || !league.value) return []
  return (league.value.draft.picks as any[])
    .filter((p: any) => p.playerId === authStore.playerId)
    .sort((a: any, b: any) => a.pickNumber - b.pickNumber)
    .map((p: any) => pokemonStore.getPokemonById(p.pokemonId))
    .filter(Boolean) as NonNullable<ReturnType<typeof pokemonStore.getPokemonById>>[]
})

const myPoints = computed(() => {
  if (!authStore.playerId || !league.value) return 0
  return (league.value.draft.picks as any[])
    .filter((p: any) => p.playerId === authStore.playerId)
    .reduce((sum: number, p: any) => sum + pokemonStore.getPointValue(p.pokemonId), 0)
})

const pointLimit = computed(() => league.value?.pointLimit ?? 0)
const pointsRemaining = computed(() => pointLimit.value - myPoints.value)

// ── Draft status labels ──────────────────────────────────────────────────────
const currentPickerId = computed(() => league.value?.draft?.currentPickerId ?? null)
const currentPickerName = computed(() => league.value?.draft?.currentPickerName ?? '—')
const isMyTurn = computed(() => currentPickerId.value === authStore.playerId)

const draftStatusLabel = computed(() => {
  if (draftStatus.value === 'setup') return 'Not started'
  if (draftStatus.value === 'complete') return 'Draft complete'
  if (isMyTurn.value) return "It's your turn!"
  return `${currentPickerName.value} is picking…`
})

const draftStatusColor = computed(() => {
  if (draftStatus.value === 'complete') return '#10b981'
  if (isMyTurn.value) return 'var(--primary)'
  if (draftStatus.value === 'active') return '#f59e0b'
  return 'var(--text-muted)'
})

// ── Draft progress ───────────────────────────────────────────────────────────
const totalPicks = computed(() => league.value?.draft?.totalPicks ?? 0)
const currentPickNumber = computed(() => league.value?.draft?.currentPickNumber ?? 0)

const draftProgress = computed(() => {
  if (totalPicks.value === 0) return 0
  return Math.round((currentPickNumber.value / totalPicks.value) * 100)
})

// ── Post-draft hub data ──────────────────────────────────────────────────────
const schedule = ref<any>(null)
const playoffOutlook = ref<any[]>([])

async function fetchPostDraftData() {
  if (!authStore.leagueCode) return
  try {
    const [schedRes, outlookRes] = await Promise.all([
      fetch(`${API_BASE}/leagues/${authStore.leagueCode}/schedule`),
      fetch(`${API_BASE}/leagues/${authStore.leagueCode}/playoff-outlook`),
    ])
    if (schedRes.ok) schedule.value = await schedRes.json()
    if (outlookRes.ok) playoffOutlook.value = await outlookRes.json()
  } catch {
    /* silent */
  }
}

const myStanding = computed(
  () => schedule.value?.standings?.find((s: any) => s.playerId === authStore.playerId) ?? null,
)

const myRank = computed(() => {
  if (!schedule.value?.standings) return 0
  return (
    (schedule.value.standings as any[]).findIndex((s: any) => s.playerId === authStore.playerId) + 1
  )
})

const myNextMatchup = computed(() => {
  if (!schedule.value?.weeks) return null
  for (const week of schedule.value.weeks as any[]) {
    for (const m of week.matchups as any[]) {
      if (
        (m.player1Id === authStore.playerId || m.player2Id === authStore.playerId) &&
        m.player1Wins === null
      ) {
        return {
          week: week.week,
          opponentName:
            m.player1Id === authStore.playerId
              ? m.player2TeamName || m.player2Name
              : m.player1TeamName || m.player1Name,
          opponentImg:
            m.player1Id === authStore.playerId ? m.player2TeamImageUrl : m.player1TeamImageUrl,
        }
      }
    }
  }
  return null
})

const topStandings = computed(() => {
  const all: any[] = schedule.value?.standings ?? []
  const top = all.slice(0, 5)
  const myIn = top.some((s: any) => s.playerId === authStore.playerId)
  if (myIn || all.length <= 5) return top
  const myRow = all.find((s: any) => s.playerId === authStore.playerId)
  return myRow ? [...top, myRow] : top
})

const playoffSpots = computed(() => league.value?.playoffSpots ?? 4)
const outlookPreview = computed(() =>
  playoffOutlook.value.slice(0, Math.min(6, playoffOutlook.value.length)),
)

const outlookStatusColor = (status: string) => {
  if (status === 'Clinched') return '#10b981'
  if (status === 'Eliminated') return '#ef4444'
  return '#f59e0b'
}

const outlookStatusLabel = (status: string) => {
  if (status === 'Clinched') return 'C'
  if (status === 'Eliminated') return 'E'
  return '—'
}
</script>

<template>
  <!-- ── Not logged in: landing ──────────────────────────────────────────── -->
  <main v-if="!authStore.isAuthenticated" class="landing">
    <div class="landing-inner">
      <div class="landing-icon"><AppIcon :path="mdiPokeball" :size="64" /></div>
      <h1>PokéDraft</h1>
      <p class="landing-sub">
        Run a snake draft for your Pokémon league — assign point values, pick your team, and track
        everyone's roster in real time.
      </p>

      <div class="landing-actions">
        <button class="btn btn-primary btn-lg" @click="router.push('/join')">
          <AppIcon :path="mdiAccountPlus" :size="20" />
          Join a League
        </button>
        <button class="btn btn-ghost btn-lg" @click="router.push('/league/create')">
          <AppIcon :path="mdiPlusCircle" :size="20" />
          Create a League
        </button>
      </div>

      <div class="feature-grid">
        <div class="feature-card">
          <AppIcon :path="mdiAccountGroup" :size="28" class="feature-icon" />
          <strong>Multiplayer</strong>
          <span>Invite friends to your league and draft together in real time.</span>
        </div>
        <div class="feature-card">
          <AppIcon :path="mdiStarCircle" :size="28" class="feature-icon" />
          <strong>Point System</strong>
          <span>Assign custom point costs to Pokémon. Every team has a budget.</span>
        </div>
        <div class="feature-card">
          <AppIcon :path="mdiCards" :size="28" class="feature-icon" />
          <strong>Snake Draft</strong>
          <span>Pick order reverses every round so no one has an unfair advantage.</span>
        </div>
      </div>
    </div>
  </main>

  <!-- ── Admin dashboard ─────────────────────────────────────────────────── -->
  <main v-else-if="authStore.isAdmin" class="dashboard">
    <div class="dash-header">
      <div>
        <h1>{{ league?.name }}</h1>
        <p class="dash-sub">
          Commissioner dashboard · Welcome back, <strong>{{ authStore.playerName }}</strong>
        </p>
      </div>
      <button class="btn btn-primary" @click="router.push('/league?tab=draft')">
        <AppIcon :path="mdiTrophy" :size="18" />
        Draft Board
      </button>
    </div>

    <!-- Setup checklist -->
    <section class="section" v-if="draftStatus === 'setup'">
      <h2 class="section-heading">Setup Checklist</h2>
      <div class="checklist">
        <div
          v-for="step in setupSteps"
          :key="step.label"
          class="checklist-item"
          :class="{ done: step.done }"
          @click="step.action()"
        >
          <AppIcon
            :path="step.done ? mdiCheckCircle : mdiCircleOutline"
            :size="22"
            class="check-icon"
          />
          <div class="check-text">
            <span class="check-label">{{ step.label }}</span>
            <span class="check-detail">{{ step.detail }}</span>
          </div>
          <span v-if="!step.done" class="check-action">→</span>
        </div>
      </div>

      <div v-if="draftReady" class="ready-banner">
        <AppIcon :path="mdiPlayCircle" :size="20" />
        Everything is set — head to the Draft Board to start!
        <button class="btn btn-primary btn-sm" @click="router.push('/league?tab=draft')">Start Draft</button>
      </div>
    </section>

    <!-- Draft progress when active/complete -->
    <section class="section" v-else>
      <h2 class="section-heading">Draft Status</h2>
      <div class="draft-status-card">
        <div class="draft-status-row">
          <span class="status-dot" :style="{ background: draftStatusColor }" />
          <span class="status-label" :style="{ color: draftStatusColor }">{{
            draftStatusLabel
          }}</span>
        </div>
        <div class="progress-bar-wrap">
          <div class="progress-bar-fill" :style="{ width: draftProgress + '%' }" />
        </div>
        <span class="progress-label">Pick {{ currentPickNumber }} of {{ totalPicks }}</span>
      </div>
    </section>

    <!-- League at a glance -->
    <section class="section">
      <h2 class="section-heading">League Overview</h2>
      <div class="stat-row">
        <div class="stat-chip">
          <span class="stat-n">{{ league?.players?.length ?? 0 }}</span>
          <span class="stat-lbl">Players</span>
        </div>
        <div class="stat-chip">
          <span class="stat-n">{{ league?.rounds ?? 0 }}</span>
          <span class="stat-lbl">Rounds</span>
        </div>
        <div class="stat-chip">
          <span class="stat-n">{{ league?.pointLimit ?? 0 }}</span>
          <span class="stat-lbl">Point Limit</span>
        </div>
        <div class="stat-chip">
          <span class="stat-n">{{ Object.keys(league?.pointValues ?? {}).length }}</span>
          <span class="stat-lbl">Valued Pokémon</span>
        </div>
      </div>
    </section>

    <!-- Quick actions -->
    <section class="section">
      <h2 class="section-heading">Quick Actions</h2>
      <div class="action-row">
        <button class="btn btn-secondary" @click="router.push('/league?tab=setup')">
          <AppIcon :path="mdiCog" :size="16" /> Configure League
        </button>
        <button class="btn btn-secondary" @click="router.push('/league?tab=pokemon')">
          <AppIcon :path="mdiClipboardList" :size="16" /> Point Values
        </button>
        <button class="btn btn-secondary" @click="router.push('/roster')">
          <AppIcon :path="mdiAccountGroup" :size="16" /> View Rosters
        </button>
      </div>
    </section>
  </main>

  <!-- ── Player dashboard ────────────────────────────────────────────────── -->
  <main v-else class="dashboard">
    <div class="dash-header">
      <div>
        <h1>{{ league?.name }}</h1>
        <p class="dash-sub">
          Welcome back, <strong>{{ authStore.playerName }}</strong>
        </p>
      </div>
      <button
        v-if="draftStatus === 'active'"
        :class="['btn', isMyTurn ? 'btn-primary pulse' : 'btn-secondary']"
        @click="router.push('/league?tab=draft')"
      >
        <AppIcon :path="mdiTrophy" :size="18" />
        {{ isMyTurn ? 'Your Turn!' : 'Draft Board' }}
      </button>
    </div>

    <!-- ── Post-draft hub ──────────────────────────────────────────────── -->
    <template v-if="draftStatus === 'complete'">
      <!-- Row 1: My Record + Next Matchup -->
      <div class="hub-row">
        <!-- My Record -->
        <div class="hub-card hub-record">
          <div class="hub-card-label">My Record</div>
          <div v-if="myStanding" class="record-body">
            <span class="record-rank">#{{ myRank }}</span>
            <span class="record-wl">{{ myStanding.wins }}–{{ myStanding.losses }}</span>
            <span class="record-mp">{{ myStanding.matchPoints }} pts</span>
          </div>
          <div v-else class="hub-empty">
            <PokeballLoader variant="inline" label="" />
          </div>
        </div>

        <!-- Next Matchup -->
        <div class="hub-card hub-matchup">
          <div class="hub-card-label">
            {{ myNextMatchup ? `Week ${myNextMatchup.week} Matchup` : 'Season Complete' }}
          </div>
          <div v-if="myNextMatchup" class="matchup-body">
            <div class="matchup-vs">
              <img
                v-if="myNextMatchup.opponentImg"
                :src="myNextMatchup.opponentImg"
                class="opp-img"
                :alt="myNextMatchup.opponentName"
              />
              <div v-else class="opp-initials">
                {{ myNextMatchup.opponentName.slice(0, 2).toUpperCase() }}
              </div>
              <span class="matchup-opp">vs. {{ myNextMatchup.opponentName }}</span>
            </div>
            <button class="btn btn-ghost btn-sm" @click="router.push('/league?tab=schedule')">
              View Schedule <AppIcon :path="mdiArrowRight" :size="14" />
            </button>
          </div>
          <div v-else class="hub-empty muted">All games played.</div>
        </div>
      </div>

      <!-- Row 2: Standings -->
      <div class="hub-card hub-standings" v-if="topStandings.length">
        <div class="hub-card-header">
          <div class="hub-card-label">Standings</div>
          <button class="btn btn-ghost btn-xs" @click="router.push('/league?tab=schedule')">
            Full table →
          </button>
        </div>
        <table class="mini-table">
          <thead>
            <tr>
              <th>#</th>
              <th>Team</th>
              <th>W</th>
              <th>L</th>
              <th>MP</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="(s, i) in topStandings"
              :key="s.playerId"
              :class="{ 'my-row': s.playerId === authStore.playerId }"
            >
              <td>{{ i + 1 }}</td>
              <td>{{ s.teamName || s.playerName }}</td>
              <td class="num">{{ s.wins }}</td>
              <td class="num">{{ s.losses }}</td>
              <td class="num">{{ s.matchPoints }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else-if="!schedule" class="hub-card hub-standings hub-loading">
        <PokeballLoader variant="inline" label="Loading standings…" />
      </div>

      <!-- Row 3: Playoff Outlook -->
      <div class="hub-card hub-outlook" v-if="outlookPreview.length">
        <div class="hub-card-header">
          <div class="hub-card-label">Playoff Outlook</div>
          <button class="btn btn-ghost btn-xs" @click="router.push('/league?tab=playoffs')">
            Full view →
          </button>
        </div>
        <div class="outlook-strip">
          <template v-for="(e, i) in outlookPreview" :key="e.playerId">
            <div
              v-if="i === playoffSpots && outlookPreview.length > playoffSpots"
              class="cutline-dot"
              title="Playoff Cutline"
            />
            <div
              class="outlook-chip"
              :class="{ 'my-chip': e.playerId === authStore.playerId }"
              :title="`${e.teamName || e.playerName} · ${e.wins}W ${e.losses}L`"
            >
              <span class="chip-rank">{{ i + 1 }}</span>
              <span class="chip-name">{{ (e.teamName || e.playerName).slice(0, 12) }}</span>
              <span
                class="chip-status"
                :style="{ color: outlookStatusColor(e.status) }"
                :title="e.status"
                >{{ outlookStatusLabel(e.status) }}</span
              >
            </div>
          </template>
        </div>
      </div>

      <!-- Row 4: Quick Nav -->
      <div class="hub-card hub-quicknav">
        <div class="hub-card-label">Quick Nav</div>
        <div class="quicknav-grid">
          <button class="qnav-btn" @click="router.push('/league?tab=team')">
            <AppIcon :path="mdiTrophy" :size="22" />
            My Team
          </button>
          <button class="qnav-btn" @click="router.push('/league?tab=teams')">
            <AppIcon :path="mdiAccountMultiple" :size="22" />
            All Teams
          </button>
          <button class="qnav-btn" @click="router.push('/league?tab=schedule')">
            <AppIcon :path="mdiCalendarCheck" :size="22" />
            Schedule
          </button>
          <button class="qnav-btn" @click="router.push('/league?tab=playoffs')">
            <AppIcon :path="mdiChartLine" :size="22" />
            Playoffs
          </button>
          <button class="qnav-btn" @click="router.push('/league?tab=pokemon')">
            <AppIcon :path="mdiPokeball" :size="22" />
            Pokémon
          </button>
        </div>
      </div>

      <!-- My team snapshot (compact) -->
      <section class="section" v-if="myPicks.length">
        <div class="section-row">
          <h2 class="section-heading">My Team</h2>
          <div style="display: flex; align-items: center; gap: 0.5rem">
            <span class="points-badge" :class="{ over: myPoints > pointLimit }">
              {{ myPoints }} / {{ pointLimit }} pts
            </span>
            <button class="btn btn-ghost btn-xs" @click="router.push('/league?tab=team')">
              Full roster →
            </button>
          </div>
        </div>
        <div class="team-grid compact">
          <div
            v-for="pokemon in myPicks.slice(0, 12)"
            :key="pokemon.id"
            class="team-pokemon"
            :title="pokemon.name"
          >
            <img :src="pokemon.spriteUrl" :alt="pokemon.name" loading="lazy" />
            <span class="team-pokemon-name">{{ pokemon.name.replace(/-/g, ' ') }}</span>
          </div>
        </div>
      </section>
    </template>

    <!-- ── Setup / Active state ────────────────────────────────────────── -->
    <template v-else>
      <!-- Draft status banner -->
      <section class="section">
        <div class="draft-status-card" :class="{ 'my-turn': isMyTurn }">
          <div class="draft-status-row">
            <span class="status-dot" :style="{ background: draftStatusColor }" />
            <span class="status-label" :style="{ color: draftStatusColor }">{{
              draftStatusLabel
            }}</span>
          </div>
          <template v-if="draftStatus !== 'setup'">
            <div class="progress-bar-wrap">
              <div class="progress-bar-fill" :style="{ width: draftProgress + '%' }" />
            </div>
            <span class="progress-label">Pick {{ currentPickNumber }} of {{ totalPicks }}</span>
          </template>
          <p v-else class="draft-not-started">The draft hasn't started yet. Sit tight!</p>
        </div>
      </section>

      <!-- My team -->
      <section class="section">
        <div class="section-row">
          <h2 class="section-heading">My Team</h2>
          <span class="points-badge" :class="{ over: myPoints > pointLimit }">
            {{ myPoints }} / {{ pointLimit }} pts
          </span>
        </div>

        <div v-if="myPicks.length === 0" class="empty-team">
          <span>No Pokémon drafted yet.</span>
          <button
            v-if="draftStatus === 'active'"
            class="btn btn-primary btn-sm"
            @click="router.push('/league?tab=draft')"
          >
            Go to Draft
          </button>
        </div>

        <div v-else class="team-grid">
          <div
            v-for="pokemon in myPicks"
            :key="pokemon.id"
            class="team-pokemon"
            :title="pokemon.name"
          >
            <img :src="pokemon.spriteUrl" :alt="pokemon.name" loading="lazy" />
            <span class="team-pokemon-name">{{ pokemon.name.replace(/-/g, ' ') }}</span>
            <span class="team-pokemon-pts">{{ pokemonStore.getPointValue(pokemon.id) }}pt</span>
          </div>
        </div>

        <div v-if="myPicks.length > 0" class="team-summary">
          <span>{{ myPicks.length }} Pokémon</span>
          <span class="separator">·</span>
          <span :class="pointsRemaining < 0 ? 'over-limit' : ''">
            {{
              pointsRemaining >= 0
                ? pointsRemaining + ' pts remaining'
                : Math.abs(pointsRemaining) + ' pts over limit'
            }}
          </span>
        </div>
      </section>
    </template>
  </main>
</template>

<style scoped>
/* ── Shared layout ───────────────────────────────────────────────────────── */
.landing,
.dashboard {
  width: 100%;
  max-width: none;
  margin: 0;
  padding: 2rem clamp(1rem, 2vw, 2rem);
}

/* ── Landing ─────────────────────────────────────────────────────────────── */
.landing-inner {
  text-align: center;
  display: flex;
  flex-direction: column;
  align-items: center;
}

.landing-icon {
  font-size: 4rem;
  margin-bottom: 0.5rem;
  color: var(--primary);
}

h1 {
  font-size: 2.4rem;
  font-weight: 900;
  margin-bottom: 0.6rem;
}

.landing-sub {
  color: var(--text-muted);
  font-size: 1.05rem;
  max-width: 500px;
  line-height: 1.65;
  margin-bottom: 2rem;
}

.landing-actions {
  display: flex;
  gap: 1rem;
  flex-wrap: wrap;
  justify-content: center;
  margin-bottom: 3rem;
}

.feature-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 1rem;
  width: 100%;
  text-align: left;
}

.feature-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.feature-icon {
  color: var(--primary);
}

.feature-card strong {
  font-size: 0.95rem;
  font-weight: 700;
}

.feature-card span {
  font-size: 0.82rem;
  color: var(--text-muted);
  line-height: 1.5;
}

/* ── Dashboard shared ────────────────────────────────────────────────────── */
.dash-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1.75rem;
  flex-wrap: wrap;
}

.dash-header h1 {
  font-size: 1.8rem;
  font-weight: 800;
  margin-bottom: 0.2rem;
}

.dash-sub {
  color: var(--text-muted);
  font-size: 0.9rem;
}

.dash-sub strong {
  color: var(--text);
}

.section {
  margin-bottom: 1.75rem;
}

.section-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 0.75rem;
}

.section-heading {
  font-size: 0.78rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-muted);
  margin-bottom: 0.75rem;
}

.section-row .section-heading {
  margin-bottom: 0;
}

/* ── Checklist ───────────────────────────────────────────────────────────── */
.checklist {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.checklist-item {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  padding: 0.85rem 1rem;
  cursor: pointer;
  transition:
    border-color 0.15s,
    background 0.15s;
}

.checklist-item:hover {
  border-color: var(--primary);
  background: var(--input-bg);
}
.checklist-item.done {
  opacity: 0.6;
  cursor: default;
}
.checklist-item.done:hover {
  border-color: var(--border-color);
  background: var(--card-bg);
}

.check-icon {
  flex-shrink: 0;
}
.checklist-item.done .check-icon {
  color: #10b981;
}
.checklist-item:not(.done) .check-icon {
  color: var(--text-muted);
}

.check-text {
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
  flex: 1;
}

.check-label {
  font-weight: 600;
  font-size: 0.95rem;
}
.check-detail {
  font-size: 0.78rem;
  color: var(--text-muted);
}
.check-action {
  color: var(--text-muted);
  font-size: 1rem;
}

.ready-banner {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  flex-wrap: wrap;
  margin-top: 0.75rem;
  background: rgba(16, 185, 129, 0.1);
  border: 1px solid rgba(16, 185, 129, 0.3);
  color: #10b981;
  border-radius: 8px;
  padding: 0.75rem 1rem;
  font-size: 0.9rem;
  font-weight: 600;
}

/* ── Draft status card ───────────────────────────────────────────────────── */
.draft-status-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 1.1rem 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
  transition: border-color 0.2s;
}

.draft-status-card.my-turn {
  border-color: var(--primary);
  background: rgba(204, 0, 0, 0.06);
}

.draft-status-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.status-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  flex-shrink: 0;
}

.status-label {
  font-size: 1rem;
  font-weight: 700;
}

.progress-bar-wrap {
  background: var(--input-bg);
  border-radius: 4px;
  height: 6px;
  overflow: hidden;
}

.progress-bar-fill {
  height: 100%;
  background: var(--primary);
  border-radius: 4px;
  transition: width 0.4s ease;
}

.progress-label {
  font-size: 0.78rem;
  color: var(--text-muted);
}

.draft-not-started {
  font-size: 0.88rem;
  color: var(--text-muted);
  margin-top: 0.1rem;
}

/* ── Stats row ───────────────────────────────────────────────────────────── */
.stat-row {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.stat-chip {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  padding: 0.75rem 1.1rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.15rem;
  min-width: 80px;
}

.stat-n {
  font-size: 1.4rem;
  font-weight: 800;
  color: var(--text);
}

.stat-lbl {
  font-size: 0.72rem;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  white-space: nowrap;
}

/* ── Quick actions ───────────────────────────────────────────────────────── */
.action-row {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}

/* ── Team grid ───────────────────────────────────────────────────────────── */
.points-badge {
  font-size: 0.82rem;
  font-weight: 700;
  padding: 0.2rem 0.65rem;
  border-radius: 20px;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  color: var(--text-muted);
}

.points-badge.over {
  background: rgba(220, 38, 38, 0.12);
  border-color: rgba(220, 38, 38, 0.4);
  color: #f87171;
}

.empty-team {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  padding: 1.5rem;
  text-align: center;
  color: var(--text-muted);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
  font-size: 0.9rem;
}

.team-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(90px, 1fr));
  gap: 0.6rem;
}

.team-pokemon {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  padding: 0.5rem 0.4rem 0.4rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.2rem;
  transition: border-color 0.15s;
}

.team-pokemon:hover {
  border-color: var(--primary);
}

.team-pokemon img {
  width: 64px;
  height: 64px;
}

.team-pokemon-name {
  font-size: 0.65rem;
  color: var(--text-muted);
  text-align: center;
  text-transform: capitalize;
  line-height: 1.2;
  max-width: 80px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.team-pokemon-pts {
  font-size: 0.68rem;
  font-weight: 700;
  color: var(--primary);
}

.team-summary {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-top: 0.6rem;
  font-size: 0.82rem;
  color: var(--text-muted);
}

.separator {
  color: var(--border-color);
}
.over-limit {
  color: #f87171;
  font-weight: 700;
}

/* ── Post-draft hub ───────────────────────────────────────────────────────── */
.hub-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.85rem;
  margin-bottom: 0.85rem;
}

@media (max-width: 540px) {
  .hub-row {
    grid-template-columns: 1fr;
  }
}

.hub-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 1rem 1.15rem;
  margin-bottom: 0.85rem;
}

.hub-card-label {
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-muted);
  margin-bottom: 0.6rem;
}

.hub-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 0.6rem;
}

.hub-card-header .hub-card-label {
  margin-bottom: 0;
}

.hub-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 60px;
}
.hub-empty {
  color: var(--text-muted);
  font-size: 0.85rem;
}
.hub-empty.muted {
  opacity: 0.6;
}

/* Record card */
.record-body {
  display: flex;
  align-items: baseline;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.record-rank {
  font-size: 2rem;
  font-weight: 900;
  color: var(--primary);
  line-height: 1;
}

.record-wl {
  font-size: 1.3rem;
  font-weight: 800;
}

.record-mp {
  font-size: 0.85rem;
  color: var(--text-muted);
}

/* Matchup card */
.matchup-body {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.matchup-vs {
  display: flex;
  align-items: center;
  gap: 0.6rem;
}

.opp-img {
  width: 34px;
  height: 34px;
  border-radius: 50%;
  object-fit: cover;
  border: 1px solid var(--border-color);
}

.opp-initials {
  width: 34px;
  height: 34px;
  border-radius: 50%;
  background: var(--input-bg);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.72rem;
  font-weight: 700;
  color: var(--text-muted);
  flex-shrink: 0;
}

.matchup-opp {
  font-weight: 600;
  font-size: 0.95rem;
}

/* Mini standings table */
.mini-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.82rem;
}

.mini-table th {
  padding: 0.3rem 0.4rem;
  text-align: left;
  font-size: 0.68rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-muted);
  border-bottom: 1px solid var(--border-color);
}

.mini-table td {
  padding: 0.45rem 0.4rem;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
}

.mini-table td.num {
  text-align: center;
}
.mini-table tr.my-row td {
  background: color-mix(in srgb, var(--primary) 8%, transparent);
  font-weight: 700;
}
.mini-table tr:last-child td {
  border-bottom: none;
}

/* Playoff outlook strip */
.outlook-strip {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: center;
}

.outlook-chip {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  padding: 0.25rem 0.55rem;
  font-size: 0.78rem;
  cursor: default;
}

.outlook-chip.my-chip {
  border-color: var(--primary);
  background: color-mix(in srgb, var(--primary) 10%, transparent);
}

.chip-rank {
  font-weight: 700;
  color: var(--text-muted);
  font-size: 0.68rem;
}
.chip-name {
  font-weight: 600;
}
.chip-status {
  font-size: 0.72rem;
  font-weight: 700;
}

.cutline-dot {
  width: 6px;
  height: 24px;
  border-left: 2px dashed var(--border-color);
  margin: 0 0.15rem;
}

/* Quick nav */
.quicknav-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
}

.qnav-btn {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.35rem;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  padding: 0.85rem 1.1rem;
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--text);
  cursor: pointer;
  transition:
    border-color 0.15s,
    background 0.15s;
  min-width: 78px;
}

.qnav-btn:hover {
  border-color: var(--primary);
  background: var(--card-bg);
}
.qnav-btn :deep(svg) {
  color: var(--primary);
}

.team-grid.compact .team-pokemon img {
  width: 52px;
  height: 52px;
}

.btn-xs {
  font-size: 0.72rem;
  padding: 0.2rem 0.5rem;
  border-radius: 4px;
}
</style>
