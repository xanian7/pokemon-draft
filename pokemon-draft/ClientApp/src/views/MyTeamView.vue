<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { API_BASE, useSignalR } from '@/services/signalr'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import PokemonCard from '@/components/PokemonCard.vue'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import type { DraftPick, LeaguePlayer, Pokemon, Trade } from '@/types'
import { formatPokemonName } from '@/utils/format'

interface LeagueState {
  code: string
  name: string
  pointLimit: number
  rounds: number
  regulationSet?: string
  players: LeaguePlayer[]
  pointValues: Record<number, number>
  draft: {
    status: string
    currentPickNumber: number
    totalPicks: number
    currentPickerId: string | null
    currentPickerName: string | null
    picks: DraftPick[]
  }
}

interface TeamEntry {
  pokemonId: number
  pickNumber: number
  pokemon?: Pokemon
  points: number
}

const router = useRouter()
const authStore = useAuthStore()
const pokemonStore = usePokemonStore()
const { subscribe, unsubscribe, isConnected } = useSignalR()

if (!authStore.isAuthenticated) router.replace('/join')

const leagueCode = computed(() => authStore.leagueCode ?? '')
const currentPlayerId = computed(() => authStore.playerId ?? '')

const league = ref<LeagueState | null>(null)
const trades = ref<Trade[]>([])
const isLoading = ref(true)
const loadError = ref('')
const tradeError = ref('')
const tradeActionId = ref<number | null>(null)
const teamAvatarError = ref(false)
const displayTeamName = computed(() =>
  authStore.teamName?.trim() ? authStore.teamName : `${authStore.playerName}'s Team`,
)
const heroInitials = computed(() => {
  const n = displayTeamName.value
  return n
    .split(' ')
    .map((w: string) => w[0])
    .join('')
    .toUpperCase()
    .slice(0, 2)
})

function normalizeStatus(status?: string | null) {
  return status?.toLowerCase() ?? ''
}

function getPointValue(pokemonId: number) {
  return Number(
    league.value?.pointValues?.[pokemonId] ?? pokemonStore.getPointValue(pokemonId) ?? 0,
  )
}

function applyState(state: LeagueState) {
  league.value = state
  for (const [id, points] of Object.entries(state.pointValues ?? {})) {
    pokemonStore.setPointValue(Number(id), Number(points))
  }
}

async function fetchLeagueState() {
  const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}`)
  if (!res.ok) throw new Error('Failed to load league state.')
  applyState((await res.json()) as LeagueState)
}

async function fetchTrades() {
  const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/trades`)
  if (!res.ok) throw new Error('Failed to load trades.')
  trades.value = (await res.json()) as Trade[]
}

async function loadPage() {
  if (!leagueCode.value) return

  isLoading.value = true
  loadError.value = ''

  try {
    await Promise.all([pokemonStore.fetchAllPokemon(), fetchLeagueState(), fetchTrades()])
  } catch (error) {
    console.error(error)
    loadError.value = 'Unable to load your team right now.'
  } finally {
    isLoading.value = false
  }
}

function getPlayerName(playerId: string) {
  const player = league.value?.players.find((player) => player.id === playerId)
  return player?.teamName || (player?.name ?? 'Unknown Player')
}

function getPokemonName(pokemonId: number) {
  const pokemon = pokemonStore.getPokemonById(pokemonId)
  return pokemon ? formatPokemonName(pokemon.name) : `#${pokemonId}`
}

function getPokemonSprite(pokemonId: number) {
  return pokemonStore.getPokemonById(pokemonId)?.spriteUrl ?? ''
}

function getTeamEntries(playerId: string) {
  if (!league.value || !playerId) return [] as TeamEntry[]

  return league.value.draft.picks
    .filter((pick) => pick.playerId === playerId)
    .sort((a, b) => a.pickNumber - b.pickNumber)
    .map((pick) => ({
      pokemonId: pick.pokemonId,
      pickNumber: pick.pickNumber,
      pokemon: pokemonStore.getPokemonById(pick.pokemonId),
      points: getPointValue(pick.pokemonId),
    }))
}

const draftComplete = computed(() => normalizeStatus(league.value?.draft?.status) === 'complete')
const myTeam = computed(() => getTeamEntries(currentPlayerId.value))
const myPointTotal = computed(() =>
  myTeam.value.reduce((total, pokemon) => total + pokemon.points, 0),
)
const otherPlayers = computed(() =>
  (league.value?.players ?? []).filter((player) => player.id !== currentPlayerId.value),
)

const myTrades = computed(() =>
  trades.value
    .filter(
      (trade) =>
        trade.initiatorPlayerId === currentPlayerId.value ||
        trade.targetPlayerId === currentPlayerId.value,
    )
    .sort((a, b) => new Date(b.proposedAt).getTime() - new Date(a.proposedAt).getTime()),
)

const pendingTrades = computed(() => myTrades.value.filter((trade) => trade.status === 'Pending'))
const tradeHistory = computed(() => myTrades.value.filter((trade) => trade.status !== 'Pending'))

// ── Pokemon detail modal ───────────────────────────────────────────────────────
const selectedPokemon = ref<Pokemon | null>(null)
const selectedPokemonPoints = computed(() =>
  selectedPokemon.value ? getPointValue(selectedPokemon.value.id) : 0,
)

function openDetail(pokemon: Pokemon | undefined) {
  if (pokemon) selectedPokemon.value = pokemon
}

// ── Other teams expand/collapse ───────────────────────────────────────────────
const expandedOtherTeams = ref<Set<string>>(new Set())

function toggleOtherTeam(playerId: string) {
  const s = new Set(expandedOtherTeams.value)
  if (s.has(playerId)) s.delete(playerId)
  else s.add(playerId)
  expandedOtherTeams.value = s
}

function getTradeItems(trade: Trade, playerId: string) {
  return trade.items.filter((item) => item.fromPlayerId === playerId)
}

function isIncomingTrade(trade: Trade) {
  return trade.targetPlayerId === currentPlayerId.value
}

async function refreshAfterAction() {
  await Promise.all([fetchLeagueState(), fetchTrades()])
}

async function actOnTrade(trade: Trade, action: 'accept' | 'reject' | 'cancel') {
  if (!leagueCode.value || !currentPlayerId.value) return

  tradeError.value = ''
  tradeActionId.value = trade.id

  try {
    const res = await fetch(
      `${API_BASE}/leagues/${leagueCode.value}/trades/${trade.id}/${action}`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          playerId: currentPlayerId.value,
          pin: authStore.pin,
        }),
      },
    )

    if (!res.ok) {
      const text = await res.text()
      throw new Error(text || `Failed to ${action} trade.`)
    }

    await refreshAfterAction()
  } catch (error) {
    tradeError.value = error instanceof Error ? error.message : 'Trade action failed.'
  } finally {
    tradeActionId.value = null
  }
}

function formatDate(value: string) {
  return new Date(value).toLocaleString()
}

function handleLeagueState(state: LeagueState) {
  applyState(state)
  void fetchTrades().catch((error) => console.error(error))
}

onMounted(async () => {
  await loadPage()
  if (!leagueCode.value) return
  await subscribe(leagueCode.value, handleLeagueState)
})

onUnmounted(() => unsubscribe(handleLeagueState))
</script>

<template>
  <main class="page-wrap">
    <section v-if="isLoading" class="state-card loading-card">
      <PokeballLoader variant="page" label="Loading your team…" />
    </section>

    <section v-else-if="loadError" class="state-card error-card">
      <h1>Couldn't load My Team</h1>
      <p>{{ loadError }}</p>
      <button class="btn btn-primary" @click="loadPage">Try Again</button>
    </section>

    <template v-else-if="league">
      <!-- ── Hero banner ── -->
      <section class="hero">
        <div class="hero-avatar">
          <img
            v-if="authStore.teamImageUrl && !teamAvatarError"
            :src="authStore.teamImageUrl"
            alt="Team avatar"
            class="hero-avatar-img"
            @error="teamAvatarError = true"
          />
          <div v-else class="hero-avatar-initials">{{ heroInitials }}</div>
        </div>
        <div class="hero-left">
          <p class="eyebrow">{{ league.name }}</p>
          <h1>{{ displayTeamName }}</h1>
          <RouterLink to="/settings" class="edit-team-link">Edit team profile</RouterLink>
          <div v-if="myTeam.length" class="hero-sprites">
            <img
              v-for="entry in myTeam.slice(0, 10)"
              :key="entry.pokemonId"
              :src="entry.pokemon?.spriteUrl"
              :alt="getPokemonName(entry.pokemonId)"
              class="hero-sprite"
              :title="getPokemonName(entry.pokemonId)"
            />
          </div>
        </div>
        <div class="hero-right">
          <div class="stat-block">
            <span class="stat-value">{{ myTeam.length }}</span>
            <span class="stat-label">Pokémon</span>
          </div>
          <div class="stat-block">
            <span class="stat-value">{{ myPointTotal }}</span>
            <span class="stat-label">of {{ league.pointLimit }} pts</span>
          </div>
          <div class="stat-block">
            <span class="stat-value">{{ league.pointLimit - myPointTotal }}</span>
            <span class="stat-label">pts remaining</span>
          </div>
        </div>
        <div class="hero-actions">
          <div class="connection-badge" :class="isConnected ? 'live' : 'offline'">
            {{ isConnected ? '● Live' : '○ Offline' }}
          </div>
          <button class="btn btn-primary" @click="router.push('/team/manage')">
            Manage Roster
          </button>
        </div>
      </section>

      <section v-if="!draftComplete" class="state-card warning-card">
        <h2>Draft is still in progress</h2>
        <p>Roster moves unlock once the draft is complete.</p>
        <RouterLink class="inline-link" to="/draft">Go to Draft Board &rarr;</RouterLink>
      </section>

      <template v-else>
        <div class="content-grid">
          <!-- ── Left: My Roster + Other Teams ── -->
          <div class="main-col">
            <section class="panel-card">
              <div class="section-header">
                <div>
                  <p class="eyebrow">{{ authStore.playerName }}</p>
                  <h2>My Roster</h2>
                </div>
                <span class="summary-points">{{ myPointTotal }} / {{ league.pointLimit }} pts</span>
              </div>
              <div v-if="myTeam.length" class="roster-grid">
                <PokemonCard
                  v-for="entry in myTeam"
                  :key="entry.pokemonId"
                  :pokemon="entry.pokemon!"
                  :point-value="entry.points"
                  mode="team"
                  @click="openDetail(entry.pokemon)"
                />
              </div>
              <p v-else class="empty-text">No Pokémon on your roster yet.</p>
            </section>

            <section class="panel-card">
              <div class="section-header">
                <div>
                  <p class="eyebrow">League overview</p>
                  <h2>Other teams</h2>
                </div>
                <span class="section-meta">{{ otherPlayers.length }} opponents</span>
              </div>
              <div class="other-teams-list">
                <div v-for="player in otherPlayers" :key="player.id" class="other-team">
                  <button class="other-team-toggle" @click="toggleOtherTeam(player.id)">
                    <div class="other-team-avatar">
                      <img
                        v-if="player.teamImageUrl"
                        :src="player.teamImageUrl"
                        :alt="player.teamName || player.name"
                        class="other-team-avatar-img"
                      />
                      <div v-else class="other-team-avatar-initials">
                        {{
                          (player.teamName || player.name)
                            .split(' ')
                            .map((w: string) => w[0])
                            .join('')
                            .toUpperCase()
                            .slice(0, 2)
                        }}
                      </div>
                    </div>
                    <span class="other-team-name">{{ player.teamName || player.name }}</span>
                    <span class="other-team-meta">
                      {{ getTeamEntries(player.id).length }} Pokémon &middot;
                      {{ getTeamEntries(player.id).reduce((s, e) => s + e.points, 0) }} pts
                    </span>
                    <span
                      class="other-team-chevron"
                      :class="{ open: expandedOtherTeams.has(player.id) }"
                      >&#9660;</span
                    >
                  </button>
                  <div v-if="expandedOtherTeams.has(player.id)" class="other-team-roster">
                    <p v-if="!getTeamEntries(player.id).length" class="empty-text">No picks yet.</p>
                    <div v-else class="roster-grid">
                      <PokemonCard
                        v-for="entry in getTeamEntries(player.id)"
                        :key="entry.pokemonId"
                        :pokemon="entry.pokemon!"
                        :point-value="entry.points"
                        mode="team"
                        @click="openDetail(entry.pokemon)"
                      />
                    </div>
                  </div>
                </div>
              </div>
            </section>
          </div>

          <!-- ── Right: Sticky sidebar ── -->
          <aside class="side-col">
            <!-- Pending Trades -->
            <section class="panel-card">
              <div class="section-header">
                <div>
                  <p class="eyebrow">Trading desk</p>
                  <h2>Trades</h2>
                </div>
                <span class="trade-count">{{ pendingTrades.length }} pending</span>
              </div>
              <p v-if="tradeError" class="error-message">{{ tradeError }}</p>
              <div v-if="pendingTrades.length" class="trade-list">
                <article
                  v-for="trade in pendingTrades"
                  :key="trade.id"
                  class="trade-card pending-card"
                >
                  <div class="trade-topline">
                    <div>
                      <span class="trade-title">
                        {{
                          isIncomingTrade(trade)
                            ? `From ${getPlayerName(trade.initiatorPlayerId)}`
                            : `To ${getPlayerName(trade.targetPlayerId)}`
                        }}
                      </span>
                      <span class="trade-date">{{ formatDate(trade.proposedAt) }}</span>
                    </div>
                    <span class="status-pill pending">Pending</span>
                  </div>
                  <div class="trade-columns">
                    <div>
                      <p class="trade-label">
                        {{ isIncomingTrade(trade) ? 'They send' : 'You send' }}
                      </p>
                      <ul>
                        <li
                          v-for="item in getTradeItems(trade, trade.initiatorPlayerId)"
                          :key="`${trade.id}-offer-${item.pokemonId}`"
                        >
                          {{ getPokemonName(item.pokemonId) }}
                        </li>
                      </ul>
                    </div>
                    <div>
                      <p class="trade-label">
                        {{ isIncomingTrade(trade) ? 'You send' : 'They send' }}
                      </p>
                      <ul>
                        <li
                          v-for="item in getTradeItems(trade, trade.targetPlayerId)"
                          :key="`${trade.id}-request-${item.pokemonId}`"
                        >
                          {{ getPokemonName(item.pokemonId) }}
                        </li>
                      </ul>
                    </div>
                  </div>
                  <div class="trade-actions">
                    <template v-if="trade.targetPlayerId === currentPlayerId">
                      <button
                        class="btn btn-success"
                        :disabled="tradeActionId === trade.id"
                        @click="actOnTrade(trade, 'accept')"
                      >
                        Accept
                      </button>
                      <button
                        class="btn btn-secondary"
                        :disabled="tradeActionId === trade.id"
                        @click="actOnTrade(trade, 'reject')"
                      >
                        Reject
                      </button>
                    </template>
                    <button
                      v-else
                      class="btn btn-secondary"
                      :disabled="tradeActionId === trade.id"
                      @click="actOnTrade(trade, 'cancel')"
                    >
                      Cancel
                    </button>
                  </div>
                </article>
              </div>
              <p v-else class="empty-text">No pending trades.</p>
            </section>

            <!-- Trade History -->
            <section class="panel-card">
              <div class="section-header">
                <div>
                  <p class="eyebrow">Archive</p>
                  <h2>Trade history</h2>
                </div>
              </div>
              <div v-if="tradeHistory.length" class="trade-list history-list">
                <article
                  v-for="trade in tradeHistory"
                  :key="trade.id"
                  class="trade-card history-card"
                >
                  <div class="trade-topline">
                    <div>
                      <span class="trade-title">
                        {{ getPlayerName(trade.initiatorPlayerId) }} &harr;
                        {{ getPlayerName(trade.targetPlayerId) }}
                      </span>
                      <span class="trade-date">{{ formatDate(trade.proposedAt) }}</span>
                    </div>
                    <span class="status-pill" :class="trade.status.toLowerCase()">{{
                      trade.status
                    }}</span>
                  </div>
                  <p class="history-summary">
                    {{
                      getTradeItems(trade, trade.initiatorPlayerId)
                        .map((item) => getPokemonName(item.pokemonId))
                        .join(', ') || 'Nothing'
                    }}
                    for
                    {{
                      getTradeItems(trade, trade.targetPlayerId)
                        .map((item) => getPokemonName(item.pokemonId))
                        .join(', ') || 'Nothing'
                    }}
                  </p>
                </article>
              </div>
              <p v-else class="empty-text">No trade history yet.</p>
            </section>
          </aside>
        </div>
      </template>
    </template>
  </main>

  <PokemonDetailModal
    v-if="selectedPokemon"
    :pokemon="selectedPokemon"
    :point-value="selectedPokemonPoints"
    :can-draft="false"
    :is-picked="true"
    @close="selectedPokemon = null"
  />
</template>

<style scoped>
/* ── Page shell ──────────────────────────────────────── */
.page-wrap {
  padding: 1.5rem 2.5rem 3rem;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

/* ── Shared card base ────────────────────────────────── */
.hero,
.panel-card,
.state-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 16px;
}

.panel-card,
.state-card {
  padding: 1.25rem;
}

.warning-card {
  border-color: rgba(245, 158, 11, 0.45);
}
.error-card {
  border-color: rgba(248, 113, 113, 0.5);
}
.loading-card {
  display: flex;
  justify-content: center;
  padding: 3rem 1.25rem;
}

/* ── Hero ────────────────────────────────────────────── */
.hero {
  padding: 2rem 2.5rem;
  display: flex;
  align-items: center;
  gap: 2.5rem;
  flex-wrap: wrap;
  background: linear-gradient(135deg, rgba(59, 76, 202, 0.1) 0%, var(--card-bg) 55%);
}

.hero-avatar {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  overflow: hidden;
  border: 2px solid var(--border-color);
  flex-shrink: 0;
}

.hero-avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.hero-avatar-initials {
  width: 100%;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(59, 76, 202, 0.25);
  color: #a5b4fc;
  font-size: 0.85rem;
  font-weight: 800;
}

.hero-left {
  flex: 1;
  min-width: 220px;
}

.hero-sprites {
  display: flex;
  flex-wrap: wrap;
  gap: 0;
  margin-top: 0.75rem;
}

.hero-sprite {
  width: 64px;
  height: 64px;
  image-rendering: pixelated;
  filter: drop-shadow(0 2px 4px rgba(0, 0, 0, 0.4));
  transition: transform 0.15s;
}

.hero-sprite:hover {
  transform: translateY(-4px) scale(1.15);
  z-index: 1;
}

.hero-right {
  display: flex;
  gap: 2rem;
}

.stat-block {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.2rem;
}

.stat-value {
  font-size: 2.2rem;
  font-weight: 800;
  color: var(--text);
  line-height: 1;
}

.stat-label {
  font-size: 0.72rem;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.07em;
}

.hero-actions {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.65rem;
}

/* ── Two-column layout ───────────────────────────────── */
.content-grid {
  display: grid;
  grid-template-columns: 1fr 360px;
  gap: 1.25rem;
  align-items: start;
}

.main-col {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.side-col {
  position: sticky;
  top: 1rem;
  max-height: calc(100vh - 3rem);
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

/* ── Typography helpers ──────────────────────────────── */
.eyebrow {
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  font-size: 0.72rem;
  margin-bottom: 0.35rem;
}

h1,
h2 {
  color: var(--text);
}
h1 {
  font-size: 2rem;
  margin-bottom: 0.25rem;
}
h2 {
  font-size: 1.15rem;
}

.edit-team-link {
  font-size: 0.8rem;
  color: var(--text-muted);
  text-decoration: underline;
  margin-top: 0.25rem;
  display: inline-block;
}

.edit-team-link:hover {
  color: var(--text);
}

.subtitle,
.state-card p,
.empty-text,
.history-summary,
.trade-date,
.trade-label,
.section-meta {
  color: var(--text-muted);
  font-size: 0.88rem;
}

.empty-text {
  margin-top: 0.75rem;
}

/* ── Section header ──────────────────────────────────── */
.section-header,
.trade-topline {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  align-items: flex-start;
  flex-wrap: wrap;
  margin-bottom: 0.25rem;
}

/* ── Pills & badges ──────────────────────────────────── */
.connection-badge,
.status-pill,
.summary-points,
.trade-count {
  font-size: 0.78rem;
  font-weight: 700;
  border-radius: 999px;
  padding: 0.3rem 0.65rem;
  white-space: nowrap;
}

.connection-badge.live {
  color: #34d399;
  background: rgba(52, 211, 153, 0.12);
}
.connection-badge.offline {
  color: var(--text-muted);
  background: var(--input-bg);
}
.summary-points {
  background: rgba(59, 76, 202, 0.14);
  color: #a5b4fc;
}
.trade-count,
.status-pill.pending {
  background: rgba(245, 158, 11, 0.14);
  color: #fbbf24;
}
.status-pill.accepted {
  background: rgba(52, 211, 153, 0.14);
  color: #34d399;
}
.status-pill.rejected,
.status-pill.cancelled {
  background: rgba(248, 113, 113, 0.14);
  color: #f87171;
}

/* ── Roster grid ─────────────────────────────────────── */
.roster-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(155px, 1fr));
  gap: 0.9rem;
  margin-top: 1rem;
}

.roster-grid-sm {
  grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
}

.roster-card-item {
  cursor: pointer;
}

/* ── Other teams ─────────────────────────────────────── */
.other-teams-list {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
  margin-top: 1rem;
}

.other-team {
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  overflow: hidden;
}

.other-team-toggle {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.85rem 1rem;
  background: none;
  border: none;
  cursor: pointer;
  text-align: left;
}

.other-team-avatar {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  overflow: hidden;
  flex-shrink: 0;
}

.other-team-avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.other-team-avatar-initials {
  width: 100%;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(59, 76, 202, 0.25);
  color: #a5b4fc;
  font-size: 0.72rem;
  font-weight: 800;
}

.other-team-name {
  flex: 1;
  color: var(--text);
  font-weight: 700;
  font-size: 0.92rem;
}

.other-team-meta {
  color: var(--text-muted);
  font-size: 0.8rem;
}

.other-team-chevron {
  color: var(--text-muted);
  font-size: 0.7rem;
  transition: transform 0.2s ease;
}

.other-team-chevron.open {
  transform: rotate(180deg);
}

.other-team-roster {
  padding: 0 1rem 1rem;
}

/* ── Trades ──────────────────────────────────────────── */
.trade-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  margin-top: 0.75rem;
}

.trade-card {
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 0.9rem;
}

.pending-card {
  border-color: rgba(245, 158, 11, 0.3);
}

.trade-title {
  display: block;
  color: var(--text);
  font-weight: 700;
  font-size: 0.88rem;
  margin-bottom: 0.2rem;
}

.trade-columns {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.75rem;
  margin-top: 0.75rem;
}

.trade-columns ul {
  margin: 0;
  padding-left: 1rem;
  color: var(--text);
  font-size: 0.85rem;
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.trade-actions {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin-top: 0.85rem;
}

.error-message {
  color: #f87171;
  margin-top: 0.6rem;
  font-size: 0.88rem;
}

/* btn-success is not in global design system (green action) */
.btn-success {
  background: #059669;
  color: white;
}
.btn-success:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

.inline-link {
  color: var(--secondary);
  font-weight: 700;
}

/* ── Responsive ──────────────────────────────────────── */
@media (max-width: 1100px) {
  .content-grid {
    grid-template-columns: 1fr 320px;
  }
}

@media (max-width: 860px) {
  .content-grid {
    grid-template-columns: 1fr;
  }

  .side-col {
    position: static;
    max-height: none;
    overflow-y: visible;
  }

  .hero-right {
    gap: 1.25rem;
  }
}

@media (max-width: 600px) {
  .page-wrap {
    padding: 1rem 0.9rem 2rem;
  }

  .hero {
    padding: 1.25rem;
    gap: 1.25rem;
  }

  .trade-columns {
    grid-template-columns: 1fr;
  }

  .stat-value {
    font-size: 1.6rem;
  }
}
</style>
