<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import PokemonCard from '@/components/PokemonCard.vue'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import { useSignalR, API_BASE } from '@/services/signalr'
import { useRegulationFilter } from '@/composables/useRegulationFilter'
import type { Pokemon } from '@/types'
import { formatPokemonName, TYPE_COLORS } from '@/utils/format'
import { mdiTrophy, mdiCrosshairs, mdiAccountGroup } from '@mdi/js'

const router = useRouter()
const authStore = useAuthStore()
const pokemonStore = usePokemonStore()
const { connect, disconnect, isConnected } = useSignalR()

if (!authStore.isAuthenticated) router.replace('/join')

// ── Server state ──────────────────────────────────────────────────────────────
const league = ref<any>(null)

function applyState(state: any) {
  league.value = state
  // Keep local point values in sync with server
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

// ── Computed helpers ──────────────────────────────────────────────────────────
const isMyTurn = computed(() => league.value?.draft?.currentPickerId === authStore.playerId)

const currentPicker = computed(() => league.value?.draft?.currentPickerName ?? '—')
const draftStatus = computed(() => league.value?.draft?.status ?? 'Setup')
const pickedIds = computed<Set<number>>(
  () => new Set((league.value?.draft?.picks ?? []).map((p: any) => p.pokemonId)),
)

const currentRound = computed(() => {
  if (!league.value) return 0
  const n = league.value.players.length
  if (n === 0) return 0
  return Math.floor(league.value.draft.currentPickNumber / n) + 1
})

const completionModalDismissed = ref(false)

const showCompletionModal = computed(
  () => draftStatus.value === 'Complete' && !completionModalDismissed.value,
)

function getPlayerPicks(playerId: string): Pokemon[] {
  if (!league.value) return []
  return (league.value.draft.picks as any[])
    .filter((p: any) => p.playerId === playerId)
    .sort((a: any, b: any) => a.pickNumber - b.pickNumber)
    .map((p: any) => pokemonStore.getPokemonById(p.pokemonId))
    .filter((pokemon): pokemon is Pokemon => Boolean(pokemon))
}

function getPlayerPoints(playerId: string): number {
  if (!league.value) return 0
  return (league.value.draft.picks as any[])
    .filter((p: any) => p.playerId === playerId)
    .reduce((sum: number, p: any) => sum + pokemonStore.getPointValue(p.pokemonId), 0)
}

const myPicks = computed(() => (authStore.playerId ? getPlayerPicks(authStore.playerId) : []))

watch(draftStatus, (status) => {
  if (status !== 'Complete') completionModalDismissed.value = false
})

// ── Regulation filter ─────────────────────────────────────────────────────────
const leagueRegulationId = computed(() => league.value?.regulationSet ?? 'national')
const { isLegalPokemon } = useRegulationFilter(leagueRegulationId)

// ── Pokemon browser ───────────────────────────────────────────────────────────
const searchQuery = ref('')
const selectedType = ref('')
const showAvailableOnly = ref(true)
const pickError = ref('')

const filteredPokemon = computed(() => {
  const q = searchQuery.value.toLowerCase()
  return pokemonStore.pokemonWithPoints.filter((p) => {
    if (!isLegalPokemon(p)) return false
    if (showAvailableOnly.value && pickedIds.value.has(p.id)) return false
    if (q && !p.name.includes(q) && !formatPokemonName(p.name).toLowerCase().includes(q))
      return false
    if (selectedType.value && !p.types.includes(selectedType.value)) return false
    return true
  })
})

async function makePick(pokemonId: number) {
  if (!isMyTurn.value || draftStatus.value !== 'Active') return
  pickError.value = ''
  const res = await fetch(`${API_BASE}/leagues/${authStore.leagueCode}/draft/pick`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      playerId: authStore.playerId,
      pin: authStore.pin,
      pokemonId,
    }),
  })
  if (!res.ok) {
    const text = await res.text()
    pickError.value = text || 'Failed to make pick.'
  }
}

// ── Detail modal ──────────────────────────────────────────────────────────────
const detailPokemon = ref<(typeof filteredPokemon.value)[0] | null>(null)

function openDetail(p: (typeof filteredPokemon.value)[0]) {
  detailPokemon.value = p
}

function closeDetail() {
  detailPokemon.value = null
}
</script>

<template>
  <main class="draft-view">
    <!-- Header -->
    <div class="draft-header">
      <div v-if="!league || draftStatus === 'Setup'" class="status-banner setup">
        Waiting for commissioner to start the draft…
        <button v-if="authStore.isAdmin" @click="router.push('/league/setup')">
          Go to Setup →
        </button>
      </div>
      <div v-else-if="draftStatus === 'Complete'" class="status-banner complete">
        <AppIcon :path="mdiTrophy" :size="20" />
        Draft Complete!
      </div>
      <template v-else>
        <div class="round-info">
          <span class="label">Round</span>
          <span class="value">{{ currentRound }} / {{ league?.rounds }}</span>
        </div>
        <div class="round-info">
          <span class="label">Pick</span>
          <span class="value"
            >{{ (league?.draft?.currentPickNumber ?? 0) + 1 }} /
            {{ league?.draft?.totalPicks }}</span
          >
        </div>
        <div class="current-picker">
          <span class="label">Now Picking</span>
          <span class="picker-name" :class="{ 'is-me': isMyTurn }">
            <template v-if="isMyTurn">
              <AppIcon :path="mdiCrosshairs" :size="16" />
              YOU
            </template>
            <template v-else>{{ currentPicker }}</template>
          </span>
        </div>
        <div v-if="pickError" class="pick-error">{{ pickError }}</div>
        <div class="connection-badge" :class="isConnected ? 'live' : 'offline'">
          {{ isConnected ? '● Live' : '○ Disconnected' }}
        </div>
      </template>
    </div>

    <div v-if="league && draftStatus !== 'Setup'" class="draft-layout">
      <!-- Pokemon browser -->
      <section class="pokemon-panel">
        <div class="panel-header">
          <h2>Available Pokémon</h2>
          <span v-if="!isMyTurn && draftStatus === 'Active'" class="not-your-turn">
            Waiting for {{ currentPicker }}…
          </span>
        </div>

        <div v-if="pokemonStore.isLoading" class="loading"><span class="spinner" /> Loading…</div>

        <template v-else>
          <div class="filters">
            <input v-model="searchQuery" type="text" placeholder="Search…" class="search-input" />
            <select v-model="selectedType">
              <option value="">All Types</option>
              <option v-for="type in pokemonStore.allTypes" :key="type" :value="type">
                {{ type.charAt(0).toUpperCase() + type.slice(1) }}
              </option>
            </select>
            <label class="toggle">
              <input v-model="showAvailableOnly" type="checkbox" />
              Available only
            </label>
          </div>

          <div class="pokemon-grid">
            <div
              v-for="p in filteredPokemon"
              :key="p.id"
              class="pick-wrapper"
              :class="{ 'can-pick': isMyTurn && draftStatus === 'Active' && !pickedIds.has(p.id) }"
            >
              <PokemonCard
                :pokemon="p"
                :point-value="p.pointValue"
                :is-picked="pickedIds.has(p.id)"
                mode="draft"
                @click="openDetail(p)"
              />
            </div>
          </div>
        </template>
      </section>

      <!-- Teams sidebar -->
      <section class="teams-panel">
        <h2>Teams</h2>
        <div
          v-for="player in league.players"
          :key="player.id"
          class="team-card"
          :class="{
            'active-picker': player.id === league.draft.currentPickerId,
            'is-me': player.id === authStore.playerId,
          }"
        >
          <div class="team-header">
            <span class="team-name">
              {{ player.name }}
              <span v-if="player.id === authStore.playerId" class="you-tag">You</span>
            </span>
            <span class="team-points">
              {{ getPlayerPoints(player.id) }}
              <span v-if="league.pointLimit > 0"> / {{ league.pointLimit }}</span>
              pts
            </span>
          </div>
          <div class="team-picks">
            <div v-for="round in league.rounds" :key="round" class="pick-slot">
              <PokemonCard
                v-if="getPlayerPicks(player.id)[round - 1]"
                :pokemon="getPlayerPicks(player.id)[round - 1]!"
                :point-value="pokemonStore.getPointValue(getPlayerPicks(player.id)[round - 1]!.id)"
                mode="team"
              />
              <div v-else class="empty-pick">
                <span>R{{ round }}</span>
              </div>
            </div>
          </div>
        </div>
      </section>
    </div>

    <Teleport to="body">
      <div
        v-if="showCompletionModal"
        class="completion-backdrop"
        @click.self="completionModalDismissed = true"
      >
        <div class="completion-modal" role="dialog" aria-modal="true">
          <div class="completion-header">
            <AppIcon :path="mdiTrophy" :size="40" class="trophy-icon" />
            <h2>Draft Complete!</h2>
            <p class="completion-sub">Here's the team you drafted:</p>
          </div>

          <div v-if="myPicks.length > 0" class="my-picks-grid">
            <PokemonCard
              v-for="pick in myPicks"
              :key="pick.id"
              :pokemon="pick"
              :point-value="pokemonStore.getPointValue(pick.id)"
              mode="team"
            />
          </div>
          <p v-else class="no-picks">No picks recorded for your account.</p>

          <div class="completion-actions">
            <button class="btn-primary completion-btn" @click="router.push('/team')">
              <AppIcon :path="mdiAccountGroup" :size="18" />
              View My Team
            </button>
            <button class="btn-ghost completion-btn" @click="completionModalDismissed = true">
              Close
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </main>

  <PokemonDetailModal
    v-if="detailPokemon"
    :pokemon="detailPokemon"
    :point-value="detailPokemon.pointValue"
    :can-draft="isMyTurn && draftStatus === 'Active'"
    :is-picked="pickedIds.has(detailPokemon.id)"
    @close="closeDetail"
    @draft="(id) => { makePick(id); closeDetail() }"
  />
</template>

<style scoped>
.draft-view {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 56px);
  overflow: hidden;
}

.draft-header {
  display: flex;
  align-items: center;
  gap: 1.5rem;
  padding: 0.75rem 1.25rem;
  background: var(--card-bg);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
  flex-wrap: wrap;
}

.status-banner {
  display: flex;
  align-items: center;
  gap: 1rem;
  font-weight: 600;
  font-size: 0.95rem;
  flex: 1;
}
.status-banner.setup {
  color: var(--text-muted);
}
.status-banner.complete {
  color: #10b981;
  font-size: 1.1rem;
}
.status-banner button {
  background: var(--primary);
  color: white;
  border: none;
  border-radius: 6px;
  padding: 0.3rem 0.75rem;
  font-size: 0.85rem;
  cursor: pointer;
}

.round-info {
  display: flex;
  flex-direction: column;
}
.round-info .label,
.current-picker .label {
  font-size: 0.62rem;
  text-transform: uppercase;
  color: var(--text-muted);
  letter-spacing: 0.05em;
}
.round-info .value {
  font-weight: 700;
  font-size: 1rem;
}

.current-picker {
  display: flex;
  flex-direction: column;
}
.picker-name {
  font-size: 1.1rem;
  font-weight: 800;
  color: var(--text);
  display: flex;
  align-items: center;
  gap: 0.35rem;
}
.picker-name.is-me {
  color: var(--primary);
  animation: pulse-text 1.5s ease-in-out infinite;
}

@keyframes pulse-text {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.7;
  }
}

.pick-error {
  background: rgba(220, 38, 38, 0.12);
  border: 1px solid rgba(220, 38, 38, 0.35);
  color: #f87171;
  border-radius: 6px;
  padding: 0.3rem 0.6rem;
  font-size: 0.82rem;
  max-width: 240px;
}

.connection-badge {
  font-size: 0.72rem;
  font-weight: 700;
  padding: 0.15rem 0.5rem;
  border-radius: 20px;
  margin-left: auto;
}
.connection-badge.live {
  color: #10b981;
  background: rgba(16, 185, 129, 0.12);
}
.connection-badge.offline {
  color: var(--text-muted);
  background: var(--input-bg);
}

.draft-layout {
  display: flex;
  flex: 1;
  overflow: hidden;
}

/* Pokemon panel */
.pokemon-panel {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border-right: 1px solid var(--border-color);
  padding: 0.85rem;
}

.panel-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.6rem;
}

.panel-header h2 {
  font-size: 0.82rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-muted);
  margin: 0;
}

.not-your-turn {
  font-size: 0.78rem;
  color: var(--text-muted);
  font-style: italic;
}

.filters {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 0.65rem;
  flex-wrap: wrap;
  align-items: center;
}

.search-input {
  flex: 1;
  min-width: 130px;
}

input[type='text'],
select {
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  color: var(--text);
  border-radius: 6px;
  padding: 0.3rem 0.55rem;
  font-size: 0.82rem;
}

.toggle {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  font-size: 0.8rem;
  color: var(--text-muted);
  cursor: pointer;
  white-space: nowrap;
}

.pokemon-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(95px, 1fr));
  gap: 0.45rem;
  overflow-y: auto;
  flex: 1;
  padding-top: 4px;
}

.pick-wrapper {
  position: relative;
}

.pick-wrapper.can-pick :deep(.pokemon-card) {
  border-color: var(--primary);
  box-shadow: 0 0 0 1px rgba(204, 0, 0, 0.3);
}

.overflow-hint,
.empty {
  font-size: 0.78rem;
  color: var(--text-muted);
  text-align: center;
  padding: 0.75rem 0;
  flex-shrink: 0;
}

/* Teams panel */
.teams-panel {
  width: 300px;
  flex-shrink: 0;
  overflow-y: auto;
  padding: 0.85rem;
  display: flex;
  flex-direction: column;
  gap: 0.65rem;
}

.teams-panel h2 {
  font-size: 0.82rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-muted);
  margin: 0;
}

.team-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  overflow: hidden;
  flex-shrink: 0;
  transition:
    border-color 0.15s,
    box-shadow 0.15s;
}

.team-card.active-picker {
  border-color: var(--primary);
  box-shadow: 0 0 0 2px rgba(204, 0, 0, 0.2);
}

.team-card.is-me {
  border-color: var(--secondary);
}

.team-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.45rem 0.65rem;
  background: var(--input-bg);
}

.team-name {
  font-weight: 700;
  font-size: 0.85rem;
  display: flex;
  align-items: center;
  gap: 0.35rem;
}
.you-tag {
  font-size: 0.6rem;
  background: var(--secondary);
  color: white;
  border-radius: 4px;
  padding: 1px 4px;
  font-weight: 800;
}
.team-points {
  font-size: 0.75rem;
  color: var(--primary);
  font-weight: 700;
}

.team-picks {
  padding: 0.35rem;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}
.pick-slot {
  min-height: 52px;
}
.empty-pick {
  border: 1px dashed var(--border-color);
  border-radius: 6px;
  height: 50px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-muted);
  font-size: 0.68rem;
}

.loading {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  color: var(--text-muted);
  padding: 2rem;
}

.spinner {
  width: 20px;
  height: 20px;
  border: 3px solid var(--border-color);
  border-top-color: var(--primary);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

/* Completion modal */
.completion-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 200;
  padding: 1rem;
}

.completion-modal {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 16px;
  padding: 2rem;
  max-width: 640px;
  width: 100%;
  max-height: 80vh;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.completion-header {
  text-align: center;
}

.trophy-icon {
  color: #f59e0b;
}

.completion-header h2 {
  font-size: 1.5rem;
  font-weight: 800;
  margin: 0.5rem 0 0.25rem;
  color: var(--text);
}

.completion-sub {
  color: var(--text-muted);
  margin: 0;
  font-size: 0.9rem;
}

.my-picks-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  gap: 0.75rem;
}

.no-picks {
  text-align: center;
  color: var(--text-muted);
  font-style: italic;
}

.completion-actions {
  display: flex;
  gap: 0.75rem;
  justify-content: center;
}

.completion-btn {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.65rem 1.5rem;
  border-radius: 8px;
  font-size: 0.95rem;
  font-weight: 600;
  cursor: pointer;
  border: none;
}

.completion-actions .btn-primary {
  background: var(--primary);
  color: white;
}

.btn-ghost {
  background: transparent;
  border: 1px solid var(--border-color) !important;
  color: var(--text-muted);
}

.btn-ghost:hover {
  color: var(--text);
  border-color: var(--text-muted) !important;
}

@media (max-width: 640px) {
  .teams-panel {
    display: none;
  }
}
</style>
