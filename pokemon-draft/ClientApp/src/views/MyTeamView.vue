<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { API_BASE, useSignalR } from '@/services/signalr'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import PokemonCard from '@/components/PokemonCard.vue'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import PageHeader from '@/components/PageHeader.vue'
import type {
  DraftPick,
  LeaguePlayer,
  Pokemon,
  RosterTransaction,
  Trade,
} from '@/types'
import { formatPokemonName } from '@/utils/format'
import { enqueueSnackbar } from '@/services/snackbar'

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

type ActivityTab = 'pending' | 'history' | 'roster'
type ActivityScope = 'mine' | 'all'

const router = useRouter()
const authStore = useAuthStore()
const pokemonStore = usePokemonStore()
const { subscribe, unsubscribe, isConnected } = useSignalR()

if (!authStore.isAuthenticated) router.replace('/join')

const leagueCode = computed(() => authStore.leagueCode ?? '')
const currentPlayerId = computed(() => authStore.playerId ?? '')
const league = ref<LeagueState | null>(null)
const trades = ref<Trade[]>([])
const rosterTransactions = ref<RosterTransaction[]>([])
const isLoading = ref(true)
const tradeActionId = ref<number | null>(null)
const teamAvatarError = ref(false)
const selectedPokemon = ref<Pokemon | null>(null)
const activityTab = ref<ActivityTab>('pending')
const activityScope = ref<ActivityScope>('mine')

const displayTeamName = computed(() =>
  authStore.teamName?.trim() ? authStore.teamName : `${authStore.playerName}'s Team`,
)
const heroInitials = computed(() => getInitials(displayTeamName.value))
const selectedPokemonPoints = computed(() =>
  selectedPokemon.value ? getPointValue(selectedPokemon.value.id) : 0,
)

function normalizeStatus(status?: string | null) {
  return status?.toLowerCase() ?? ''
}

function getInitials(name: string) {
  return name
    .split(' ')
    .map((word) => word[0])
    .join('')
    .toUpperCase()
    .slice(0, 2)
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

async function fetchRosterTransactions() {
  const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/roster/transactions`)
  if (!res.ok) throw new Error('Failed to load roster transactions.')
  rosterTransactions.value = (await res.json()) as RosterTransaction[]
}

async function loadPage() {
  if (!leagueCode.value) return
  isLoading.value = true

  try {
    await Promise.all([
      pokemonStore.fetchAllPokemon(),
      fetchLeagueState(),
      fetchTrades(),
      fetchRosterTransactions(),
    ])
  } catch (error) {
    console.error(error)
    enqueueSnackbar('Unable to load your team right now.', 'error')
  } finally {
    isLoading.value = false
  }
}

function getPlayerName(playerId: string) {
  const player = league.value?.players.find((item) => item.id === playerId)
  return player?.teamName || player?.name || 'Unknown Player'
}

function getPokemonName(pokemonId: number) {
  const pokemon = pokemonStore.getPokemonById(pokemonId)
  return pokemon ? formatPokemonName(pokemon.name) : `#${pokemonId}`
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
const sortedTrades = computed(() =>
  [...trades.value].sort(
    (a, b) => new Date(b.proposedAt).getTime() - new Date(a.proposedAt).getTime(),
  ),
)
const scopedTrades = computed(() =>
  activityScope.value === 'all'
    ? sortedTrades.value
    : sortedTrades.value.filter(isMyTrade),
)
const pendingTrades = computed(() =>
  scopedTrades.value.filter((trade) => trade.status === 'Pending'),
)
const tradeHistory = computed(() =>
  scopedTrades.value.filter((trade) => trade.status !== 'Pending'),
)
const scopedRosterTransactions = computed(() =>
  activityScope.value === 'all'
    ? rosterTransactions.value
    : rosterTransactions.value.filter(
        (transaction) => transaction.playerId === currentPlayerId.value,
      ),
)

function isMyTrade(trade: Trade) {
  return (
    trade.initiatorPlayerId === currentPlayerId.value ||
    trade.targetPlayerId === currentPlayerId.value
  )
}

function openDetail(pokemon: Pokemon | undefined) {
  if (pokemon) selectedPokemon.value = pokemon
}

function getTradeItems(trade: Trade, playerId: string) {
  return trade.items.filter((item) => item.fromPlayerId === playerId)
}

function canActOnTrade(trade: Trade) {
  return isMyTrade(trade) && trade.status === 'Pending'
}

async function refreshActivity() {
  await Promise.all([fetchLeagueState(), fetchTrades(), fetchRosterTransactions()])
}

async function actOnTrade(trade: Trade, action: 'accept' | 'reject' | 'cancel') {
  if (!leagueCode.value || !currentPlayerId.value) return

  tradeActionId.value = trade.id

  try {
    const res = await fetch(
      `${API_BASE}/leagues/${leagueCode.value}/trades/${trade.id}/${action}`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ playerId: currentPlayerId.value, pin: authStore.pin }),
      },
    )
    if (!res.ok) throw new Error((await res.text()) || `Failed to ${action} trade.`)
    await refreshActivity()
  } catch (error) {
    enqueueSnackbar(
      error instanceof Error ? error.message : 'Trade action failed.',
      'error',
    )
  } finally {
    tradeActionId.value = null
  }
}

function formatDate(value: string) {
  return new Date(value).toLocaleString()
}

function tradeStatusColor(status: Trade['status']) {
  if (status === 'Accepted') return 'success'
  if (status === 'Pending') return 'warning'
  return 'error'
}

function handleLeagueState(state: LeagueState) {
  applyState(state)
  void Promise.all([fetchTrades(), fetchRosterTransactions()]).catch(console.error)
}

onMounted(async () => {
  await loadPage()
  if (leagueCode.value) await subscribe(leagueCode.value, handleLeagueState)
})

onUnmounted(() => unsubscribe(handleLeagueState))
</script>

<template>
  <v-container fluid class="team-page">
    <v-card class="team-wrapper">
    <v-card v-if="isLoading" class="state-card" variant="outlined">
      <v-card-text class="d-flex justify-center pa-10">
        <!-- <PokeballLoader variant="page" label="Loading your team…" /> -->
      </v-card-text>
    </v-card>

    <template v-else-if="league">
      <PageHeader
        class="hero-card mb-2"
        :eyebrow="league.name"
        :title="displayTeamName"
        subtitle="Your roster, budget, and league activity."
      >
        <template #leading>
          <v-avatar size="72" color="primary" class="hero-avatar">
            <v-img
              v-if="authStore.teamImageUrl && !teamAvatarError"
              :src="authStore.teamImageUrl"
              alt="Team avatar"
              cover
              @error="teamAvatarError = true"
            />
            <span v-else class="text-h6 font-weight-bold">{{ heroInitials }}</span>
          </v-avatar>
        </template>
        <template #meta>
            <v-btn
              variant="text"
              size="small"
              prepend-icon="mdi-pencil"
              class="px-0"
              @click="router.push('/settings')"
            >
              Edit team profile
            </v-btn>
            <div v-if="myTeam.length" class="hero-sprites">
              <v-img
                v-for="entry in myTeam.slice(0, 10)"
                :key="entry.pokemonId"
                :src="entry.pokemon?.spriteUrl"
                :alt="getPokemonName(entry.pokemonId)"
                width="56"
                height="56"
                class="hero-sprite"
              />
            </div>
        </template>
        <template #actions>
          <div class="hero-side">
          <div class="hero-stats">
            <div class="stat">
              <strong>{{ myTeam.length }}</strong>
              <span>Pokémon</span>
            </div>
            <div class="stat">
              <strong>{{ myPointTotal }}</strong>
              <span>of {{ league.pointLimit }} pts</span>
            </div>
            <div class="stat">
              <strong>{{ league.pointLimit - myPointTotal }}</strong>
              <span>pts remaining</span>
            </div>
          </div>

          <div class="hero-actions">
            <v-chip
              :color="isConnected ? 'success' : undefined"
              :prepend-icon="isConnected ? 'mdi-wifi' : 'mdi-wifi-off'"
              size="small"
              variant="tonal"
            >
              {{ isConnected ? 'Live' : 'Offline' }}
            </v-chip>
            <v-btn color="primary" prepend-icon="mdi-account-edit" @click="router.push('/team/manage')">
              Manage Roster
            </v-btn>
          </div>
          </div>
        </template>
      </PageHeader>

      <v-alert
        v-if="!draftComplete"
        type="warning"
        variant="tonal"
        title="Draft is still in progress"
        text="Roster moves unlock once the draft is complete."
      >
        <template #append>
          <v-btn variant="tonal" @click="router.push('/draft')">Go to Draft Board</v-btn>
        </template>
      </v-alert>

      <v-row v-else align="start" class="team-content-row">
        <v-col cols="12" lg="8" class="main-team-column">
          <v-card variant="outlined" class="mb-2">
            <v-card-title class="section-title">
              <div>
                <div class="text-overline text-medium-emphasis">{{ authStore.playerName }}</div>
                <span>My Roster</span>
              </div>
              <v-chip color="primary" variant="tonal">
                {{ myPointTotal }} / {{ league.pointLimit }} pts
              </v-chip>
            </v-card-title>
            <v-card-text>
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
              <v-alert v-else type="info" variant="tonal">No Pokémon on your roster yet.</v-alert>
            </v-card-text>
          </v-card>

          <v-card variant="outlined">
            <v-card-title class="section-title">
              <div>
                <div class="text-overline text-medium-emphasis">League overview</div>
                <span>Other Teams</span>
              </div>
              <v-chip size="small" variant="tonal">{{ otherPlayers.length }} opponents</v-chip>
            </v-card-title>
            <v-card-text>
              <v-expansion-panels variant="accordion">
                <v-expansion-panel v-for="player in otherPlayers" :key="player.id">
                  <v-expansion-panel-title>
                    <div class="team-summary">
                      <v-avatar size="38" color="primary">
                        <v-img
                          v-if="player.teamImageUrl"
                          :src="player.teamImageUrl"
                          :alt="player.teamName || player.name"
                          cover
                        />
                        <span v-else class="text-caption font-weight-bold">
                          {{ getInitials(player.teamName || player.name) }}
                        </span>
                      </v-avatar>
                      <div class="team-name">
                        <strong>{{ player.teamName || player.name }}</strong>
                        <span>{{ player.teamName ? player.name : '' }}</span>
                      </div>
                      <v-chip size="small" variant="tonal">
                        {{ getTeamEntries(player.id).length }} Pokémon ·
                        {{ getTeamEntries(player.id).reduce((sum, entry) => sum + entry.points, 0) }}
                        pts
                      </v-chip>
                    </div>
                  </v-expansion-panel-title>
                  <v-expansion-panel-text>
                    <div v-if="getTeamEntries(player.id).length" class="roster-grid">
                      <PokemonCard
                        v-for="entry in getTeamEntries(player.id)"
                        :key="entry.pokemonId"
                        :pokemon="entry.pokemon!"
                        :point-value="entry.points"
                        mode="team"
                        @click="openDetail(entry.pokemon)"
                      />
                    </div>
                    <v-alert v-else type="info" variant="tonal">No picks yet.</v-alert>
                  </v-expansion-panel-text>
                </v-expansion-panel>
              </v-expansion-panels>
            </v-card-text>
          </v-card>
        </v-col>

        <v-col cols="12" lg="4" class="activity-column">
          <v-card variant="outlined">
            <v-card-title class="activity-header">
              <div>
                <div class="text-overline text-medium-emphasis">League activity</div>
                <span>Trading & Archive</span>
              </div>
              <v-tabs v-model="activityScope" density="compact" color="primary">
                <v-tab value="mine">Yours</v-tab>
                <v-tab value="all">League</v-tab>
              </v-tabs>
            </v-card-title>

            <v-tabs v-model="activityTab" grow density="compact" color="primary">
              <v-tab value="pending">
                Pending
                <v-badge :content="pendingTrades.length" inline color="warning" />
              </v-tab>
              <v-tab value="history">Trade History</v-tab>
              <v-tab value="roster">Adds / Drops</v-tab>
            </v-tabs>

            <v-divider />
            <v-window v-model="activityTab">
              <v-window-item value="pending">
                <v-card-text class="activity-list">
                  <v-card
                    v-for="trade in pendingTrades"
                    :key="trade.id"
                    variant="tonal"
                    color="warning"
                    class="trade-card"
                  >
                    <v-card-title class="trade-title">
                      <span>
                        {{ getPlayerName(trade.initiatorPlayerId) }} ↔
                        {{ getPlayerName(trade.targetPlayerId) }}
                      </span>
                      <v-chip size="x-small" color="warning">Pending</v-chip>
                    </v-card-title>
                    <v-card-subtitle>{{ formatDate(trade.proposedAt) }}</v-card-subtitle>
                    <v-card-text class="trade-columns">
                      <div>
                        <strong>{{ getPlayerName(trade.initiatorPlayerId) }} sends</strong>
                        <span
                          v-for="item in getTradeItems(trade, trade.initiatorPlayerId)"
                          :key="`${trade.id}-offer-${item.pokemonId}`"
                        >
                          {{ getPokemonName(item.pokemonId) }}
                        </span>
                      </div>
                      <div>
                        <strong>{{ getPlayerName(trade.targetPlayerId) }} sends</strong>
                        <span
                          v-for="item in getTradeItems(trade, trade.targetPlayerId)"
                          :key="`${trade.id}-request-${item.pokemonId}`"
                        >
                          {{ getPokemonName(item.pokemonId) }}
                        </span>
                      </div>
                    </v-card-text>
                    <v-card-actions v-if="canActOnTrade(trade)">
                      <template v-if="trade.targetPlayerId === currentPlayerId">
                        <v-btn
                          color="success"
                          variant="flat"
                          :loading="tradeActionId === trade.id"
                          @click="actOnTrade(trade, 'accept')"
                        >
                          Accept
                        </v-btn>
                        <v-btn
                          variant="tonal"
                          :disabled="tradeActionId === trade.id"
                          @click="actOnTrade(trade, 'reject')"
                        >
                          Reject
                        </v-btn>
                      </template>
                      <v-btn
                        v-else
                        variant="tonal"
                        :disabled="tradeActionId === trade.id"
                        @click="actOnTrade(trade, 'cancel')"
                      >
                        Cancel
                      </v-btn>
                    </v-card-actions>
                  </v-card>
                  <v-alert v-if="!pendingTrades.length" type="info" variant="tonal">
                    No pending trades for this view.
                  </v-alert>
                </v-card-text>
              </v-window-item>

              <v-window-item value="history">
                <v-list v-if="tradeHistory.length" lines="three">
                  <v-list-item v-for="trade in tradeHistory" :key="trade.id">
                    <template #prepend>
                      <v-avatar :color="tradeStatusColor(trade.status)" variant="tonal">
                        <v-icon icon="mdi-swap-horizontal" />
                      </v-avatar>
                    </template>
                    <v-list-item-title>
                      {{ getPlayerName(trade.initiatorPlayerId) }} ↔
                      {{ getPlayerName(trade.targetPlayerId) }}
                    </v-list-item-title>
                    <v-list-item-subtitle>
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
                      · {{ formatDate(trade.proposedAt) }}
                    </v-list-item-subtitle>
                    <template #append>
                      <v-chip
                        :color="tradeStatusColor(trade.status)"
                        size="small"
                        variant="tonal"
                      >
                        {{ trade.status }}
                      </v-chip>
                    </template>
                  </v-list-item>
                </v-list>
                <v-card-text v-else>
                  <v-alert type="info" variant="tonal">No trade history for this view.</v-alert>
                </v-card-text>
              </v-window-item>

              <v-window-item value="roster">
                <v-list v-if="scopedRosterTransactions.length" lines="two">
                  <v-list-item
                    v-for="transaction in scopedRosterTransactions"
                    :key="transaction.id"
                  >
                    <template #prepend>
                      <v-avatar
                        :color="transaction.type === 'Add' ? 'success' : 'error'"
                        variant="tonal"
                      >
                        <v-icon
                          :icon="
                            transaction.type === 'Add'
                              ? 'mdi-plus-circle-outline'
                              : 'mdi-minus-circle-outline'
                          "
                        />
                      </v-avatar>
                    </template>
                    <v-list-item-title>
                      {{ getPlayerName(transaction.playerId) }}
                      {{ transaction.type === 'Add' ? 'added' : 'dropped' }}
                      {{ getPokemonName(transaction.pokemonId) }}
                    </v-list-item-title>
                    <v-list-item-subtitle>
                      {{ formatDate(transaction.createdAt) }}
                    </v-list-item-subtitle>
                    <template #append>
                      <v-chip
                        :color="transaction.type === 'Add' ? 'success' : 'error'"
                        size="small"
                        variant="tonal"
                      >
                        {{ transaction.type }}
                      </v-chip>
                    </template>
                  </v-list-item>
                </v-list>
                <v-card-text v-else>
                  <v-alert type="info" variant="tonal">No adds or drops for this view.</v-alert>
                </v-card-text>
              </v-window-item>
            </v-window>
          </v-card>
        </v-col>
      </v-row>
    </template>
    </v-card>
  </v-container>

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
.team-page {
  padding: clamp(1rem, 2vw, 2rem);
}

.team-wrapper {
  display: flex;
  flex-direction: column;
  gap: 14px;
  height: auto;
  max-height: none;
  overflow: visible;
  padding: 0;
}

.team-wrapper :deep(.v-card),
.team-wrapper :deep(.v-expansion-panel) {
  border-color: var(--border-color);
  border-radius: var(--radius-md);
}

.team-wrapper > :deep(.v-row) {
  margin: -4px;
}

.team-wrapper > :deep(.v-row) > .v-col {
  padding: 4px;
}

.team-wrapper :deep(.v-card-text) {
  padding: 16px;
}

.team-wrapper :deep(.v-expansion-panel-text__wrapper) {
  padding: 16px;
}

.hero-avatar {
  border: 2px solid color-mix(in srgb, var(--primary) 45%, transparent);
}

.hero-sprites {
  display: flex;
  flex-wrap: wrap;
  margin-top: 0.25rem;
}

.hero-sprite {
  transition: transform 0.15s ease;
}

.hero-sprite:hover {
  transform: translateY(-3px) scale(1.1);
}

.hero-stats {
  display: flex;
  gap: 12px;
}

.stat {
  min-width: 74px;
  text-align: center;
}

.stat strong,
.stat span {
  display: block;
}

.stat strong {
  font-size: 1.8rem;
  line-height: 1.1;
}

.stat span {
  color: var(--text-muted);
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.hero-actions {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 8px;
}

.hero-side {
  display: flex;
  align-items: center;
  gap: 18px;
}

.section-title,
.activity-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  flex-wrap: wrap;
  padding: 8px 12px;
}

.roster-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
  gap: 6px;
}

.team-summary {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 8px;
  padding-right: 8px;
}

.team-name {
  flex: 1;
  min-width: 0;
}

.team-name strong,
.team-name span {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.team-name span {
  color: var(--text-muted);
  font-size: 0.75rem;
}

.activity-column {
  position: static;
}

.activity-header :deep(.v-tabs) {
  flex: 0 0 auto;
}

.activity-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.trade-title {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  font-size: 0.95rem;
}

.trade-columns {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}

.trade-columns strong,
.trade-columns span {
  display: block;
}

.trade-columns strong {
  margin-bottom: 0.35rem;
  font-size: 0.78rem;
}

.trade-columns span {
  font-size: 0.85rem;
}

@media (max-width: 1264px) {
  .activity-column {
    position: static;
  }
}

@media (min-width: 1280px) {
  .team-content-row {
    display: grid;
    grid-template-columns: minmax(0, 1.35fr) minmax(340px, 0.65fr);
    gap: 14px;
    margin: 0 !important;
  }

  .main-team-column {
    display: contents;
  }

  .main-team-column > :first-child {
    grid-column: 1 / -1;
  }

  .main-team-column > :last-child {
    grid-column: 1;
  }

  .activity-column {
    grid-column: 2;
    padding: 0 !important;
  }
}

@media (max-width: 720px) {
  .team-wrapper {
    border-left: 0;
    border-right: 0;
    border-radius: 0;
    height: auto;
    max-height: none;
    overflow: visible;
    padding: 0;
  }

  .team-wrapper :deep(.v-card) {
    border-left: 0;
    border-right: 0;
    border-radius: 0;
  }

  .hero-stats {
    width: 100%;
    justify-content: space-between;
  }

  .hero-actions {
    width: 100%;
    flex-direction: row;
    justify-content: space-between;
    align-items: center;
  }

  .hero-side {
    width: 100%;
    align-items: stretch;
    flex-direction: column;
  }

  .activity-header :deep(.v-tabs) {
    width: 100%;
  }

  .trade-columns {
    grid-template-columns: 1fr;
  }

  .team-summary .v-chip {
    display: none;
  }
}
</style>
