<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import { useDraftStore } from '@/stores/draft'
import { useSignalR } from '@/services/signalr'
import { useRegulationFilter } from '@/composables/useRegulationFilter'
import PokemonGrid from '@/components/PokemonGrid.vue'
import DraftRoster from '@/components/DraftRoster.vue'

const router = useRouter()
const authStore = useAuthStore()
const pokemonStore = usePokemonStore()
const draftStore = useDraftStore()
const { connect, disconnect } = useSignalR()

if (!authStore.isAuthenticated) router.replace('/join')

function applyState(state: any) {
  draftStore.applyServerState(state)
  if (state.pointValues) {
    for (const [id, pts] of Object.entries(state.pointValues as Record<string, number>)) {
      pokemonStore.setPointValue(Number(id), pts as number)
    }
  }
}

onMounted(async () => {
  await pokemonStore.fetchAllPokemon()
  if (!authStore.leagueCode) return
  await connect(authStore.leagueCode, applyState)
})

onUnmounted(disconnect)

// ── Draft header ──────────────────────────────────────────────────────────────
const currentRound = computed(() => {
  const n = draftStore.players.length
  if (n === 0) return 0
  return Math.floor(draftStore.currentPickNumber / n) + 1
})

// ── Upcoming picks (snake order) ──────────────────────────────────────────────
const upcomingPicks = computed(() => {
  if (draftStore.status !== 'active') return []
  const players = draftStore.players
  const n = players.length
  if (n === 0) return []
  const remaining = draftStore.totalPicks - draftStore.currentPickNumber
  return Array.from({ length: remaining }, (_, i) => {
    const pickNumber = draftStore.currentPickNumber + i
    const round = Math.floor(pickNumber / n)
    const posInRound = pickNumber % n
    const idx = round % 2 === 0 ? posInRound : n - 1 - posInRound
    const player = players[idx]
    const displayName = player?.teamName || player?.name || '?'
    return {
      playerId: player?.id,
      playerName: displayName,
      teamImageUrl: player?.teamImageUrl || null,
      initials: displayName
        .split(' ')
        .map((w: string) => w[0])
        .join('')
        .slice(0, 2)
        .toUpperCase(),
      isMe: player?.id === authStore.playerId,
      isCurrent: i === 0,
      pickNumber: pickNumber + 1,
      round: round + 1,
    }
  })
})

const picksByRound = computed(() => {
  const groups: Array<{ round: number; picks: typeof upcomingPicks.value }> = []
  for (const pick of upcomingPicks.value) {
    const last = groups[groups.length - 1]
    if (!last || last.round !== pick.round) groups.push({ round: pick.round, picks: [pick] })
    else last.picks.push(pick)
  }
  return groups
})

// ── Player roster panels ──────────────────────────────────────────────────────
const expandedPlayers = ref<Set<string>>(new Set())

function toggleExpanded(playerId: string) {
  const s = new Set(expandedPlayers.value)
  if (s.has(playerId)) s.delete(playerId)
  else s.add(playerId)
  expandedPlayers.value = s
}

function isExpanded(playerId: string) {
  return playerId === authStore.playerId || expandedPlayers.value.has(playerId)
}

function getPlayerPicks(playerId: string) {
  return draftStore
    .getPlayerPicks(playerId)
    .map((p: any) => pokemonStore.getPokemonById(p.pokemonId))
    .filter(Boolean)
}

function getPlayerPoints(playerId: string): number {
  return draftStore
    .getPlayerPicks(playerId)
    .reduce((sum: number, p: any) => sum + pokemonStore.getPointValue(p.pokemonId), 0)
}

const myPicks = computed(() => (authStore.playerId ? getPlayerPicks(authStore.playerId) : []))

// ── Completion modal ──────────────────────────────────────────────────────────
const completionModalDismissed = ref(false)
const showCompletionModal = computed(
  () => draftStore.isDraftComplete && !completionModalDismissed.value,
)
watch(
  () => draftStore.isDraftComplete,
  (complete) => {
    if (!complete) completionModalDismissed.value = false
  },
)

const statusLabel = computed(() => {
  if (draftStore.status === 'active') return 'Live'
  if (draftStore.status === 'complete') return 'Complete'
  return 'Setup'
})

// ── Regulation filter (passed to PokemonGrid if needed) ───────────────────────
const { isLegalPokemon } = useRegulationFilter(computed(() => draftStore.regulationSet))
</script>

<template>
  <v-form class="draft-page">
    <!-- ── Draft bar ── -->
    <div class="draft-bar">
      <!-- Status -->
      <div class="draft-bar__status">
        <span class="status-dot" :class="`status-dot--${draftStore.status}`" />
        <span class="status-label">{{ statusLabel }}</span>
      </div>

      <div class="bar-divider" />

      <!-- Picker info when active -->
      <div v-if="draftStore.status === 'active'" class="draft-bar__current">
        <span class="current-label">Now picking</span>
        <span class="current-name">{{ draftStore.currentPickerName ?? '—' }}</span>
      </div>

      <div v-if="draftStore.status === 'active'" class="bar-divider" />

      <!-- Scrollable timeline -->
      <div v-if="draftStore.status === 'active'" class="draft-bar__timeline">
        <template v-for="group in picksByRound" :key="group.round">
          <div class="round-separator">
            <span class="round-label">R{{ group.round }}</span>
          </div>
          <div
            v-for="pick in group.picks"
            :key="pick.pickNumber"
            class="pick-chip"
            :class="{ 'pick-chip--current': pick.isCurrent, 'pick-chip--mine': pick.isMe }"
          >
            <div class="pick-chip__avatar">
              <img v-if="pick.teamImageUrl" :src="pick.teamImageUrl" :alt="pick.playerName" />
              <span v-else>{{ pick.initials }}</span>
            </div>
            <div class="pick-chip__info">
              <span class="pick-chip__name">{{ pick.playerName }}</span>
              <span class="pick-chip__num">#{{ pick.pickNumber }}</span>
            </div>
          </div>
        </template>
      </div>

      <!-- Draft complete / setup message -->
      <span v-else class="bar-message">
        {{ draftStore.status === 'complete' ? 'Draft is complete.' : 'Draft has not started yet.' }}
      </span>
    </div>

    <div class="draft-body">
      <DraftRoster />
      <PokemonGrid />
    </div>
  </v-form>
</template>

<style scoped>
.draft-page {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

/* ── Body (roster + grid) ───────────────────────────────────────────────── */
.draft-body {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

/* ── Draft bar ──────────────────────────────────────────────────────────── */
.draft-bar {
  display: flex;
  align-items: center;
  gap: 0;
  padding: 0 1rem;
  height: 64px;
  flex-shrink: 0;
  background: var(--card-bg);
  border-bottom: 1px solid var(--border-color);
  overflow-x: auto;
  overflow-y: hidden;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.draft-bar::-webkit-scrollbar {
  height: 3px;
}
.draft-bar::-webkit-scrollbar-thumb {
  background: var(--border-color);
  border-radius: 2px;
}

/* ── Status ─────────────────────────────────────────────────────────────── */
.draft-bar__status {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 3px;
  flex-shrink: 0;
  padding-right: 1rem;
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}

.status-dot--active {
  background: #22c55e;
  box-shadow: 0 0 6px #22c55e88;
  animation: pulse-dot 2s ease-in-out infinite;
}
.status-dot--complete {
  background: var(--primary);
}
.status-dot--setup {
  background: var(--text-muted);
}

@keyframes pulse-dot {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.4;
  }
}

.status-label {
  font-size: 0.6rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-muted);
  white-space: nowrap;
}

/* ── Divider ─────────────────────────────────────────────────────────────── */
.bar-divider {
  width: 1px;
  height: 36px;
  background: var(--border-color);
  flex-shrink: 0;
  margin: 0 0.75rem;
}

/* ── Current picker summary ──────────────────────────────────────────────── */
.draft-bar__current {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex-shrink: 0;
  padding-right: 0.75rem;
}

.current-label {
  font-size: 0.6rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-muted);
}

.current-name {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--primary);
  white-space: nowrap;
}

/* ── Scrollable timeline ─────────────────────────────────────────────────── */
.draft-bar__timeline {
  display: flex;
  align-items: center;
  gap: 5px;
  flex: 1;
  overflow-x: auto;
  overflow-y: hidden;
  scrollbar-width: none;
  padding: 4px 0;
}

.draft-bar__timeline::-webkit-scrollbar {
  display: none;
}

/* ── Round separator ─────────────────────────────────────────────────────── */
.round-separator {
  display: flex;
  flex-direction: column;
  align-items: center;
  flex-shrink: 0;
  padding: 0 8px;
  border-left: 1px solid var(--border-color);
  margin-left: 4px;
  align-self: stretch;
  justify-content: center;
}

.round-separator:first-child {
  border-left: none;
  margin-left: 0;
  padding-left: 0;
}

.round-label {
  font-size: 0.6rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--text-muted);
  white-space: nowrap;
}

/* ── Pick chip ───────────────────────────────────────────────────────────── */
.pick-chip {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px 4px 5px;
  border-radius: 8px;
  border: 1px solid var(--border-color);
  background: var(--input-bg);
  flex-shrink: 0;
  transition:
    border-color 0.15s,
    background 0.15s,
    box-shadow 0.15s;
  cursor: default;
}

.pick-chip--current {
  border-color: var(--primary);
  background: var(--primary-hover-bg);
  box-shadow: 0 0 0 2px rgba(15, 172, 245, 0.15);
}

.pick-chip--mine:not(.pick-chip--current) {
  border-color: rgba(34, 197, 94, 0.4);
  background: rgba(34, 197, 94, 0.07);
}

.pick-chip__avatar {
  width: 26px;
  height: 26px;
  border-radius: 50%;
  background: var(--border-color);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.58rem;
  font-weight: 700;
  color: var(--text-muted);
  flex-shrink: 0;
  overflow: hidden;
}

.pick-chip__avatar img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.pick-chip__info {
  display: flex;
  flex-direction: column;
  gap: 1px;
}

.pick-chip__name {
  font-size: 0.72rem;
  font-weight: 600;
  color: var(--text);
  white-space: nowrap;
  max-width: 80px;
  overflow: hidden;
  text-overflow: ellipsis;
}

.pick-chip--current .pick-chip__name {
  color: var(--primary);
}

.pick-chip__num {
  font-size: 0.62rem;
  color: var(--text-muted);
  white-space: nowrap;
}

/* ── Bar message (setup / complete) ─────────────────────────────────────── */
.bar-message {
  font-size: 0.85rem;
  color: var(--text-muted);
  white-space: nowrap;
}
</style>
