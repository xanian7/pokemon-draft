<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import DraftGateNotice from '@/components/DraftGateNotice.vue'
import PageHeader from '@/components/PageHeader.vue'
import SectionHeader from '@/components/SectionHeader.vue'
import { enqueueSnackbar } from '@/services/snackbar'
import { API_BASE, useSignalR } from '@/services/signalr'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import type { RosterTransaction, ServerLeagueResponse, Trade } from '@/types'
import { formatPokemonName } from '@/utils/format'

type ActivityTab = 'roster' | 'pending' | 'history'
type ActivityScope = 'mine' | 'all'

const router = useRouter()
const authStore = useAuthStore()
const pokemonStore = usePokemonStore()
const { subscribe, unsubscribe } = useSignalR()

if (!authStore.isAuthenticated) router.replace('/join')

const leagueCode = computed(() => authStore.leagueCode ?? '')
const currentPlayerId = computed(() => authStore.playerId ?? '')
const league = ref<ServerLeagueResponse | null>(null)
const trades = ref<Trade[]>([])
const rosterTransactions = ref<RosterTransaction[]>([])
const isLoading = ref(true)
const error = ref('')
const tradeActionId = ref<number | null>(null)
const activityTab = ref<ActivityTab>('roster')
const activityScope = ref<ActivityScope>('all')

const draftComplete = computed(() => league.value?.draft.status.toLowerCase() === 'complete')
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
  sortedTrades.value.filter((trade) => trade.status === 'Pending' && isMyTrade(trade)),
)
const tradeHistory = computed(() =>
  scopedTrades.value.filter((trade) => trade.status !== 'Pending'),
)
const scopedRosterTransactions = computed(() => {
  const sorted = [...rosterTransactions.value].sort(
    (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
  )
  return activityScope.value === 'all'
    ? sorted
    : sorted.filter((transaction) => transaction.playerId === currentPlayerId.value)
})

async function fetchLeagueState() {
  const res = await fetch(API_BASE + '/leagues/' + leagueCode.value)
  if (!res.ok) throw new Error('Failed to load league state.')
  league.value = (await res.json()) as ServerLeagueResponse
}

async function fetchTrades() {
  const res = await fetch(API_BASE + '/leagues/' + leagueCode.value + '/trades')
  if (!res.ok) throw new Error('Failed to load trades.')
  trades.value = (await res.json()) as Trade[]
}

async function fetchRosterTransactions() {
  const res = await fetch(
    API_BASE + '/leagues/' + leagueCode.value + '/roster/transactions',
  )
  if (!res.ok) throw new Error('Failed to load roster transactions.')
  rosterTransactions.value = (await res.json()) as RosterTransaction[]
}

async function refreshActivity() {
  await Promise.all([fetchLeagueState(), fetchTrades(), fetchRosterTransactions()])
}

async function loadPage() {
  if (!leagueCode.value) return
  isLoading.value = true
  error.value = ''

  try {
    await Promise.all([pokemonStore.fetchAllPokemon(), refreshActivity()])
  } catch (loadError) {
    console.error(loadError)
    error.value = 'Unable to load league activity right now.'
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
  return pokemon ? formatPokemonName(pokemon.name) : '#' + pokemonId
}

function getTradeItems(trade: Trade, playerId: string) {
  return trade.items.filter((item) => item.fromPlayerId === playerId)
}

function isMyTrade(trade: Trade) {
  return (
    trade.initiatorPlayerId === currentPlayerId.value ||
    trade.targetPlayerId === currentPlayerId.value
  )
}

function canActOnTrade(trade: Trade) {
  return isMyTrade(trade) && trade.status === 'Pending'
}

async function actOnTrade(trade: Trade, action: 'accept' | 'reject' | 'cancel') {
  if (!leagueCode.value || !currentPlayerId.value) return
  tradeActionId.value = trade.id

  try {
    const res = await fetch(
      API_BASE +
        '/leagues/' +
        leagueCode.value +
        '/trades/' +
        trade.id +
        '/' +
        action,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ playerId: currentPlayerId.value, pin: authStore.pin }),
      },
    )
    if (!res.ok) throw new Error((await res.text()) || 'Trade action failed.')
    await refreshActivity()
    enqueueSnackbar('Trade ' + action + 'ed.', 'success')
  } catch (actionError) {
    enqueueSnackbar(
      actionError instanceof Error ? actionError.message : 'Trade action failed.',
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

function handleLeagueState(state: ServerLeagueResponse) {
  league.value = state
  void Promise.all([fetchTrades(), fetchRosterTransactions()]).catch(console.error)
}

onMounted(async () => {
  await loadPage()
  if (leagueCode.value) await subscribe(leagueCode.value, handleLeagueState)
})

onUnmounted(() => unsubscribe(handleLeagueState))
</script>

<template>
  <v-container fluid class="page-card-small">

    <div v-if="isLoading" class="page-state">
      
    </div>

    <v-alert v-else-if="error" type="error" variant="tonal">
      {{ error }}
    </v-alert>

    <DraftGateNotice
      v-else-if="!draftComplete"
      title="Waiting for the draft"
      text="Roster transactions and trades unlock once the draft is complete."
    />

    <v-card v-else class="activity-card" variant="outlined">
      <SectionHeader
        eyebrow="League feed"
        title="Transactions & Trades"
        subtitle="Review your activity or switch to the full league feed."
      >
        <template #actions>
          <v-btn-toggle
            v-model="activityScope"
            mandatory
            density="compact"
            variant="outlined"
          >
            <v-btn value="mine">Yours</v-btn>
            <v-btn value="all">League</v-btn>
          </v-btn-toggle>
        </template>
      </SectionHeader>

      <v-tabs v-model="activityTab" class="activity-tabs" density="compact" show-arrows>
        <v-tab value="roster">Adds / Drops</v-tab>
        <v-tab value="pending">
          Pending Trades
          <v-badge :content="pendingTrades.length" inline color="warning" />
        </v-tab>
        <v-tab value="history">Trade History</v-tab>
      </v-tabs>

      <v-divider />

      <v-window v-model="activityTab">
        <v-window-item value="roster">
          <v-list v-if="scopedRosterTransactions.length" class="activity-list" lines="two">
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
                <strong>{{ getPlayerName(transaction.playerId) }}</strong>
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

          <div v-else class="activity-empty">
            <v-alert type="info" variant="tonal">
              No adds or drops for this view.
            </v-alert>
            <v-btn
              v-if="activityScope === 'mine'"
              prepend-icon="mdi-account-edit"
              variant="outlined"
              @click="router.push({ path: '/league', query: { tab: 'manage' } })"
            >
              Manage roster
            </v-btn>
          </div>
        </v-window-item>

        <v-window-item value="pending">
          <div v-if="pendingTrades.length" class="trade-grid">
            <v-card
              v-for="trade in pendingTrades"
              :key="trade.id"
              variant="outlined"
              class="trade-card"
            >
              <SectionHeader
                eyebrow="Pending trade"
                :title="
                  getPlayerName(trade.initiatorPlayerId) +
                  ' ↔ ' +
                  getPlayerName(trade.targetPlayerId)
                "
                :subtitle="formatDate(trade.proposedAt)"
              >
                <template #actions>
                  <v-chip size="x-small" color="warning" variant="tonal">
                    Pending
                  </v-chip>
                </template>
              </SectionHeader>

              <v-card-text class="trade-columns">
                <div>
                  <strong>{{ getPlayerName(trade.initiatorPlayerId) }} sends</strong>
                  <span
                    v-for="item in getTradeItems(trade, trade.initiatorPlayerId)"
                    :key="trade.id + '-offer-' + item.pokemonId"
                  >
                    {{ getPokemonName(item.pokemonId) }}
                  </span>
                </div>
                <div>
                  <strong>{{ getPlayerName(trade.targetPlayerId) }} sends</strong>
                  <span
                    v-for="item in getTradeItems(trade, trade.targetPlayerId)"
                    :key="trade.id + '-request-' + item.pokemonId"
                  >
                    {{ getPokemonName(item.pokemonId) }}
                  </span>
                </div>
              </v-card-text>

              <v-card-actions v-if="canActOnTrade(trade)">
                <template v-if="trade.targetPlayerId === currentPlayerId">
                  <v-btn
                    color="success"
                    :loading="tradeActionId === trade.id"
                    @click="actOnTrade(trade, 'accept')"
                  >
                    Accept
                  </v-btn>
                  <v-btn
                    variant="outlined"
                    :disabled="tradeActionId === trade.id"
                    @click="actOnTrade(trade, 'reject')"
                  >
                    Reject
                  </v-btn>
                </template>
                <v-btn
                  v-if="trade.initiatorPlayerId === currentPlayerId"
                  color="error"
                  variant="tonal"
                  prepend-icon="mdi-close-circle-outline"
                  :loading="tradeActionId === trade.id"
                  @click="actOnTrade(trade, 'cancel')"
                >
                  Cancel proposal
                </v-btn>
              </v-card-actions>
            </v-card>
          </div>

          <div v-else class="activity-empty">
            <v-alert type="info" variant="tonal">
              No pending trades for this view.
            </v-alert>
          </div>
        </v-window-item>

        <v-window-item value="history">
          <v-list v-if="tradeHistory.length" class="activity-list" lines="three">
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

          <div v-else class="activity-empty">
            <v-alert type="info" variant="tonal">
              No trade history for this view.
            </v-alert>
          </div>
        </v-window-item>
      </v-window>
    </v-card>
  </v-container>
</template>

<style scoped>
.activity-card {
  min-width: 0;
}

.activity-tabs {
  border-bottom: 1px solid var(--border-color);
}

.activity-tabs :deep(.v-tab) {
  min-width: max-content;
  font-weight: 700;
  letter-spacing: 0;
  text-transform: none;
}

.activity-list {
  padding: 6px;
}

.activity-list :deep(.v-list-item) {
  border-bottom: 1px solid var(--border-color);
}

.activity-list :deep(.v-list-item:last-child) {
  border-bottom: 0;
}

.activity-empty {
  display: grid;
  gap: 10px;
  justify-items: start;
  padding: 16px;
}

.trade-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 320px), 1fr));
  gap: 10px;
  padding: 12px;
}

.trade-card {
  border-radius: 4px !important;
}

.trade-columns {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.trade-columns strong,
.trade-columns span {
  display: block;
}

.trade-columns strong {
  margin-bottom: 5px;
  font-size: 0.78rem;
}

.trade-columns span {
  font-size: 0.85rem;
}

@media (max-width: 600px) {
  .trade-columns {
    grid-template-columns: 1fr;
  }

  .activity-list :deep(.v-list-item__append) {
    align-self: flex-start;
  }
}
</style>
