<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { API_BASE, useSignalR } from '@/services/signalr'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
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
const { connect, disconnect, isConnected } = useSignalR()

if (!authStore.isAuthenticated) router.replace('/join')

const leagueCode = computed(() => authStore.leagueCode ?? '')
const currentPlayerId = computed(() => authStore.playerId ?? '')

const league = ref<LeagueState | null>(null)
const trades = ref<Trade[]>([])
const isLoading = ref(true)
const loadError = ref('')
const tradeError = ref('')
const tradeActionId = ref<number | null>(null)

function normalizeStatus(status?: string | null) {
  return status?.toLowerCase() ?? ''
}

function getPointValue(pokemonId: number) {
  return Number(league.value?.pointValues?.[pokemonId] ?? pokemonStore.getPointValue(pokemonId) ?? 0)
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
  return league.value?.players.find((player) => player.id === playerId)?.name ?? 'Unknown Player'
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
const myPointTotal = computed(() => myTeam.value.reduce((total, pokemon) => total + pokemon.points, 0))
const otherPlayers = computed(() =>
  (league.value?.players ?? []).filter((player) => player.id !== currentPlayerId.value),
)

const myTrades = computed(() =>
  trades.value
    .filter(
      (trade) =>
        trade.initiatorPlayerId === currentPlayerId.value || trade.targetPlayerId === currentPlayerId.value,
    )
    .sort((a, b) => new Date(b.proposedAt).getTime() - new Date(a.proposedAt).getTime()),
)

const pendingTrades = computed(() => myTrades.value.filter((trade) => trade.status === 'Pending'))
const tradeHistory = computed(() => myTrades.value.filter((trade) => trade.status !== 'Pending'))

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
    const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/trades/${trade.id}/${action}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        playerId: currentPlayerId.value,
        pin: authStore.pin,
      }),
    })

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

onMounted(async () => {
  await loadPage()

  if (!leagueCode.value) return

  await connect(leagueCode.value, (state: LeagueState) => {
    applyState(state)
    void fetchTrades().catch((error) => console.error(error))
  })
})

onUnmounted(disconnect)
</script>

<template>
  <main class="team-view">
    <section v-if="isLoading" class="state-card">
      <h1>Loading your team…</h1>
      <p>Syncing league state and trades.</p>
    </section>

    <section v-else-if="loadError" class="state-card error-card">
      <h1>Couldn’t load My Team</h1>
      <p>{{ loadError }}</p>
      <button class="primary-btn" @click="loadPage">Try Again</button>
    </section>

    <template v-else-if="league">
      <section class="hero-card">
        <div>
          <p class="eyebrow">{{ league.name }}</p>
          <h1>My Team</h1>
          <p class="subtitle">Keep tabs on your roster and pending trades.</p>
        </div>
        <div class="hero-actions">
          <div class="connection-badge" :class="isConnected ? 'live' : 'offline'">
            {{ isConnected ? '● Live Updates' : '○ Offline' }}
          </div>
          <button class="primary-btn" @click="router.push('/team/manage')">Manage Roster</button>
        </div>
      </section>

      <section v-if="!draftComplete" class="state-card warning-card">
        <h2>Draft is still in progress</h2>
        <p>Roster moves unlock once the draft is complete.</p>
        <RouterLink class="inline-link" to="/draft">Go to Draft Board →</RouterLink>
      </section>

      <template v-else>
        <section class="summary-grid">
          <article class="summary-card highlight-card">
            <div class="summary-header">
              <div>
                <p class="eyebrow">{{ authStore.playerName }}</p>
                <h2>Your roster</h2>
              </div>
              <span class="summary-points">{{ myPointTotal }} / {{ league.pointLimit }} pts</span>
            </div>

            <div v-if="myTeam.length" class="pokemon-grid">
              <article v-for="entry in myTeam" :key="entry.pokemonId" class="pokemon-tile">
                <img
                  v-if="entry.pokemon?.spriteUrl"
                  :src="entry.pokemon.spriteUrl"
                  :alt="getPokemonName(entry.pokemonId)"
                  class="pokemon-sprite"
                />
                <div v-else class="sprite-fallback">#{{ entry.pokemonId }}</div>
                <span class="pokemon-name">{{ getPokemonName(entry.pokemonId) }}</span>
                <span class="pokemon-meta">{{ entry.points }} pts</span>
              </article>
            </div>
            <p v-else class="empty-text">No Pokémon on your roster yet.</p>
          </article>

          <article class="summary-card trades-card">
            <div class="summary-header">
              <div>
                <p class="eyebrow">Trading desk</p>
                <h2>Your trades</h2>
              </div>
              <span class="trade-count">{{ pendingTrades.length }} pending</span>
            </div>

            <p v-if="tradeError" class="error-message">{{ tradeError }}</p>

            <div v-if="pendingTrades.length" class="trade-list">
              <article v-for="trade in pendingTrades" :key="trade.id" class="trade-card pending-card">
                <div class="trade-topline">
                  <div>
                    <span class="trade-title">
                      {{ isIncomingTrade(trade) ? `Incoming from ${getPlayerName(trade.initiatorPlayerId)}` : `Sent to ${getPlayerName(trade.targetPlayerId)}` }}
                    </span>
                    <span class="trade-date">{{ formatDate(trade.proposedAt) }}</span>
                  </div>
                  <span class="status-pill pending">Pending</span>
                </div>

                <div class="trade-columns">
                  <div>
                    <p class="trade-label">
                      {{ isIncomingTrade(trade) ? `${getPlayerName(trade.initiatorPlayerId)} sends` : 'You send' }}
                    </p>
                    <ul>
                      <li v-for="item in getTradeItems(trade, trade.initiatorPlayerId)" :key="`${trade.id}-offer-${item.pokemonId}`">
                        {{ getPokemonName(item.pokemonId) }}
                      </li>
                    </ul>
                  </div>
                  <div>
                    <p class="trade-label">
                      {{ isIncomingTrade(trade) ? 'You send back' : `${getPlayerName(trade.targetPlayerId)} sends` }}
                    </p>
                    <ul>
                      <li v-for="item in getTradeItems(trade, trade.targetPlayerId)" :key="`${trade.id}-request-${item.pokemonId}`">
                        {{ getPokemonName(item.pokemonId) }}
                      </li>
                    </ul>
                  </div>
                </div>

                <div class="trade-actions">
                  <template v-if="trade.targetPlayerId === currentPlayerId">
                    <button
                      class="success-btn"
                      :disabled="tradeActionId === trade.id"
                      @click="actOnTrade(trade, 'accept')"
                    >
                      Accept
                    </button>
                    <button
                      class="secondary-btn"
                      :disabled="tradeActionId === trade.id"
                      @click="actOnTrade(trade, 'reject')"
                    >
                      Reject
                    </button>
                  </template>
                  <button
                    v-else
                    class="secondary-btn"
                    :disabled="tradeActionId === trade.id"
                    @click="actOnTrade(trade, 'cancel')"
                  >
                    Cancel
                  </button>
                </div>
              </article>
            </div>
            <p v-else class="empty-text">No pending trades right now.</p>
          </article>
        </section>

        <section class="panel-card">
          <div class="section-header">
            <div>
              <p class="eyebrow">League overview</p>
              <h2>Other teams</h2>
            </div>
            <span class="section-meta">{{ otherPlayers.length }} opponents</span>
          </div>

          <div class="team-list">
            <details v-for="player in otherPlayers" :key="player.id" class="team-details">
              <summary>
                <span>{{ player.name }}</span>
                <span>{{ getTeamEntries(player.id).length }} Pokémon • {{ getTeamEntries(player.id).reduce((sum, entry) => sum + entry.points, 0) }} pts</span>
              </summary>
              <div class="team-pokemon-grid">
                <article v-for="entry in getTeamEntries(player.id)" :key="`${player.id}-${entry.pokemonId}`" class="mini-pokemon-card">
                  <img
                    v-if="entry.pokemon?.spriteUrl"
                    :src="entry.pokemon.spriteUrl"
                    :alt="getPokemonName(entry.pokemonId)"
                    class="mini-sprite"
                  />
                  <div v-else class="mini-sprite fallback">#{{ entry.pokemonId }}</div>
                  <div>
                    <p>{{ getPokemonName(entry.pokemonId) }}</p>
                    <span>{{ entry.points }} pts</span>
                  </div>
                </article>
              </div>
            </details>
          </div>
        </section>

        <section class="panel-card">
          <div class="section-header">
            <div>
              <p class="eyebrow">Archive</p>
              <h2>Trade history</h2>
            </div>
          </div>

          <div v-if="tradeHistory.length" class="trade-list history-list">
            <article v-for="trade in tradeHistory" :key="trade.id" class="trade-card history-card">
              <div class="trade-topline">
                <div>
                  <span class="trade-title">
                    {{ getPlayerName(trade.initiatorPlayerId) }} ↔ {{ getPlayerName(trade.targetPlayerId) }}
                  </span>
                  <span class="trade-date">{{ formatDate(trade.proposedAt) }}</span>
                </div>
                <span class="status-pill" :class="trade.status.toLowerCase()">{{ trade.status }}</span>
              </div>

              <p class="history-summary">
                {{ getTradeItems(trade, trade.initiatorPlayerId).map((item) => getPokemonName(item.pokemonId)).join(', ') || 'Nothing' }}
                for
                {{ getTradeItems(trade, trade.targetPlayerId).map((item) => getPokemonName(item.pokemonId)).join(', ') || 'Nothing' }}
              </p>
            </article>
          </div>
          <p v-else class="empty-text">No completed trade history yet.</p>
        </section>
      </template>
    </template>
  </main>
</template>

<style scoped>
.team-view {
  max-width: 1180px;
  margin: 0 auto;
  padding: 1.5rem 1rem 2rem;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.hero-card,
.summary-card,
.panel-card,
.state-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 16px;
}

.hero-card {
  padding: 1.5rem;
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  align-items: center;
  flex-wrap: wrap;
}

.hero-actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

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
  margin-bottom: 0.35rem;
}

h2 {
  font-size: 1.2rem;
}

.subtitle,
.state-card p,
.empty-text,
.history-summary,
.trade-date,
.trade-label,
.section-meta {
  color: var(--text-muted);
}

.connection-badge,
.status-pill,
.summary-points,
.trade-count {
  font-size: 0.8rem;
  font-weight: 700;
  border-radius: 999px;
  padding: 0.35rem 0.7rem;
}

.connection-badge.live {
  color: #34d399;
  background: rgba(52, 211, 153, 0.12);
}

.connection-badge.offline {
  color: var(--text-muted);
  background: var(--input-bg);
}

.summary-grid {
  display: grid;
  grid-template-columns: 1.4fr 1fr;
  gap: 1.25rem;
}

.summary-card,
.panel-card,
.state-card {
  padding: 1.25rem;
}

.highlight-card {
  border-color: rgba(59, 76, 202, 0.5);
}

.warning-card {
  border-color: rgba(245, 158, 11, 0.45);
}

.error-card {
  border-color: rgba(248, 113, 113, 0.5);
}

.summary-header,
.section-header,
.trade-topline {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  align-items: flex-start;
  flex-wrap: wrap;
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

.pokemon-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(110px, 1fr));
  gap: 0.9rem;
  margin-top: 1rem;
}

.pokemon-tile,
.mini-pokemon-card {
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 0.85rem;
}

.pokemon-tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: 0.35rem;
}

.pokemon-sprite,
.mini-sprite {
  image-rendering: pixelated;
}

.pokemon-sprite {
  width: 72px;
  height: 72px;
}

.sprite-fallback,
.fallback {
  display: grid;
  place-items: center;
  color: var(--text-muted);
  background: rgba(255, 255, 255, 0.04);
  border-radius: 10px;
}

.sprite-fallback {
  width: 72px;
  height: 72px;
}

.pokemon-name {
  color: var(--text);
  font-weight: 700;
}

.pokemon-meta {
  color: var(--primary);
  font-size: 0.82rem;
  font-weight: 700;
}

.trade-list {
  display: flex;
  flex-direction: column;
  gap: 0.85rem;
  margin-top: 1rem;
}

.trade-card {
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 1rem;
}

.pending-card {
  border-color: rgba(245, 158, 11, 0.3);
}

.trade-title {
  display: block;
  color: var(--text);
  font-weight: 700;
  margin-bottom: 0.25rem;
}

.trade-columns {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1rem;
  margin-top: 0.85rem;
}

.trade-columns ul {
  margin: 0;
  padding-left: 1rem;
  color: var(--text);
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.trade-actions {
  display: flex;
  gap: 0.65rem;
  flex-wrap: wrap;
  margin-top: 1rem;
}

.team-list {
  display: flex;
  flex-direction: column;
  gap: 0.85rem;
  margin-top: 1rem;
}

.team-details {
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  overflow: hidden;
}

.team-details summary {
  list-style: none;
  cursor: pointer;
  padding: 1rem;
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  color: var(--text);
  font-weight: 700;
}

.team-details summary::-webkit-details-marker {
  display: none;
}

.team-pokemon-grid {
  padding: 0 1rem 1rem;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 0.75rem;
}

.mini-pokemon-card {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.mini-sprite {
  width: 48px;
  height: 48px;
  flex-shrink: 0;
}

.mini-pokemon-card p {
  color: var(--text);
  font-weight: 600;
  margin-bottom: 0.2rem;
}

.mini-pokemon-card span {
  color: var(--text-muted);
  font-size: 0.82rem;
}

.error-message {
  color: #f87171;
  margin-top: 0.75rem;
}

.primary-btn,
.secondary-btn,
.success-btn {
  border: none;
  border-radius: 10px;
  padding: 0.72rem 1rem;
  font-size: 0.92rem;
  font-weight: 700;
  cursor: pointer;
}

.primary-btn {
  background: var(--primary);
  color: white;
}

.secondary-btn {
  background: var(--input-bg);
  color: var(--text);
  border: 1px solid var(--border-color);
}

.success-btn {
  background: #059669;
  color: white;
}

.primary-btn:disabled,
.secondary-btn:disabled,
.success-btn:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

.inline-link {
  color: var(--secondary);
  font-weight: 700;
}

@media (max-width: 900px) {
  .summary-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 640px) {
  .trade-columns {
    grid-template-columns: 1fr;
  }

  .team-view {
    padding-inline: 0.75rem;
  }
}
</style>
