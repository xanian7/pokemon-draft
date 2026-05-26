<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import AppIcon from '@/components/AppIcon.vue'
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
    detail: hasPlayers.value
      ? `${league.value.players.length} players`
      : 'Minimum 2 required',
    action: () => router.push('/league/setup'),
  },
  {
    label: 'Assign point values',
    done: hasPointValues.value,
    detail: hasPointValues.value
      ? `${Object.keys(league.value.pointValues).length} Pokémon valued`
      : 'None set yet',
    action: () => router.push('/pokemon'),
  },
  {
    label: 'Start the draft',
    done: draftStatus.value !== 'setup',
    detail: draftStatus.value === 'active'
      ? 'In progress'
      : draftStatus.value === 'complete'
        ? 'Draft complete'
        : 'Ready when you are',
    action: () => router.push('/draft'),
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
</script>

<template>
  <!-- ── Not logged in: landing ──────────────────────────────────────────── -->
  <main v-if="!authStore.isAuthenticated" class="landing">
    <div class="landing-inner">
      <div class="landing-icon"><AppIcon :path="mdiPokeball" :size="64" /></div>
      <h1>PokéDraft</h1>
      <p class="landing-sub">Run a snake draft for your Pokémon league — assign point values, pick your team, and track everyone's roster in real time.</p>

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
        <p class="dash-sub">Commissioner dashboard · Welcome back, <strong>{{ authStore.playerName }}</strong></p>
      </div>
      <button class="btn btn-primary" @click="router.push('/draft')">
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
        <button class="btn btn-primary btn-sm" @click="router.push('/draft')">Start Draft</button>
      </div>
    </section>

    <!-- Draft progress when active/complete -->
    <section class="section" v-else>
      <h2 class="section-heading">Draft Status</h2>
      <div class="draft-status-card">
        <div class="draft-status-row">
          <span class="status-dot" :style="{ background: draftStatusColor }" />
          <span class="status-label" :style="{ color: draftStatusColor }">{{ draftStatusLabel }}</span>
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
        <button class="btn btn-secondary" @click="router.push('/league/setup')">
          <AppIcon :path="mdiCog" :size="16" /> Configure League
        </button>
        <button class="btn btn-secondary" @click="router.push('/pokemon')">
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
        <p class="dash-sub">Welcome back, <strong>{{ authStore.playerName }}</strong></p>
      </div>
      <button
        v-if="draftStatus === 'active'"
        :class="isMyTurn ? 'primary pulse' : 'secondary'"
        @click="router.push('/draft')"
      >
        <AppIcon :path="mdiTrophy" :size="18" />
        {{ isMyTurn ? "Your Turn!" : "Draft Board" }}
      </button>
    </div>

    <!-- Draft status banner -->
    <section class="section">
      <div class="draft-status-card" :class="{ 'my-turn': isMyTurn }">
        <div class="draft-status-row">
          <span class="status-dot" :style="{ background: draftStatusColor }" />
          <span class="status-label" :style="{ color: draftStatusColor }">{{ draftStatusLabel }}</span>
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
        <button v-if="draftStatus === 'active'" class="btn btn-primary btn-sm" @click="router.push('/draft')">
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
          {{ pointsRemaining >= 0 ? pointsRemaining + ' pts remaining' : Math.abs(pointsRemaining) + ' pts over limit' }}
        </span>
      </div>
    </section>
  </main>
</template>

<style scoped>
/* ── Shared layout ───────────────────────────────────────────────────────── */
.landing,
.dashboard {
  max-width: 860px;
  margin: 0 auto;
  padding: 2rem 1rem;
}

/* ── Landing ─────────────────────────────────────────────────────────────── */
.landing-inner {
  text-align: center;
  display: flex;
  flex-direction: column;
  align-items: center;
}

.landing-icon { font-size: 4rem; margin-bottom: 0.5rem; color: var(--primary); }

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

.feature-icon { color: var(--primary); }

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

.dash-sub strong { color: var(--text); }

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

.section-row .section-heading { margin-bottom: 0; }

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
  transition: border-color 0.15s, background 0.15s;
}

.checklist-item:hover { border-color: var(--primary); background: var(--input-bg); }
.checklist-item.done { opacity: 0.6; cursor: default; }
.checklist-item.done:hover { border-color: var(--border-color); background: var(--card-bg); }

.check-icon { flex-shrink: 0; }
.checklist-item.done .check-icon { color: #10b981; }
.checklist-item:not(.done) .check-icon { color: var(--text-muted); }

.check-text {
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
  flex: 1;
}

.check-label { font-weight: 600; font-size: 0.95rem; }
.check-detail { font-size: 0.78rem; color: var(--text-muted); }
.check-action { color: var(--text-muted); font-size: 1rem; }

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

.team-pokemon:hover { border-color: var(--primary); }

.team-pokemon img {
  width: 64px;
  height: 64px;
  image-rendering: pixelated;
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

.separator { color: var(--border-color); }
.over-limit { color: #f87171; font-weight: 700; }
</style>

