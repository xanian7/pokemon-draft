<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { API_BASE, useSignalR } from '@/services/signalr'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import type { DraftPick, LeaguePlayer, Pokemon, Trade } from '@/types'
import { formatPokemonName } from '@/utils/format'
import AppIcon from '@/components/AppIcon.vue'
import PokemonGrid from '@/components/PokemonGrid.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import { enqueueSnackbar } from '@/services/snackbar'
import { mdiAccountGroup } from '@mdi/js'

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
const isLoading = ref(true)
const activeTab = ref<'add-drop' | 'trade'>('add-drop')
const isSubmitting = ref(false)
const addPokemonId = ref<number | null>(null)
const dropPokemonId = ref<number | null>(null)

const targetPlayerId = ref('')
const offeringPokemonIds = ref<number[]>([])
const requestingPokemonIds = ref<number[]>([])
const submittedTrade = ref<Trade | null>(null)

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

async function loadPage() {
  if (!leagueCode.value) return

  isLoading.value = true

  try {
    await Promise.all([pokemonStore.fetchAllPokemon(), fetchLeagueState()])
  } catch (error) {
    console.error(error)
    enqueueSnackbar('Unable to load roster management.', 'error')
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
const targetTeam = computed(() => getTeamEntries(targetPlayerId.value))
const rosteredPokemonIds = computed(
  () => new Set((league.value?.draft.picks ?? []).map((pick) => pick.pokemonId)),
)
const selectedAddPokemon = computed(() =>
  addPokemonId.value === null ? null : pokemonStore.getPokemonById(addPokemonId.value),
)
const selectedDrop = computed(
  () => myTeam.value.find((entry) => entry.pokemonId === dropPokemonId.value) ?? null,
)
const resultingPointTotal = computed(
  () =>
    myPointTotal.value -
    (selectedDrop.value?.points ?? 0) +
    (addPokemonId.value === null ? 0 : getPointValue(addPokemonId.value)),
)
const resultingRosterCount = computed(
  () => myTeam.value.length - (dropPokemonId.value === null ? 0 : 1) + (addPokemonId.value === null ? 0 : 1),
)
const transactionNeedsDrop = computed(() => {
  if (!league.value || addPokemonId.value === null) return false
  return (
    myTeam.value.length >= league.value.rounds ||
    myPointTotal.value + getPointValue(addPokemonId.value) > league.value.pointLimit
  )
})
const transactionIsValid = computed(() => {
  if (!league.value || (addPokemonId.value === null && dropPokemonId.value === null)) return false
  return (
    resultingPointTotal.value <= league.value.pointLimit &&
    resultingRosterCount.value <= league.value.rounds
  )
})
const transactionSummary = computed(() => {
  if (!league.value) return ''
  if (addPokemonId.value === null && dropPokemonId.value === null) {
    return 'Select a free agent to add, a roster Pokémon to drop, or both.'
  }
  if (resultingRosterCount.value > league.value.rounds) {
    return 'Your roster would be over its slot limit. Select a Pokémon to drop.'
  }
  if (resultingPointTotal.value > league.value.pointLimit) {
    return `Free ${resultingPointTotal.value - league.value.pointLimit} more points to complete this transaction.`
  }
  return 'This roster transaction is ready to submit.'
})

const selectedOffer = computed(() =>
  myTeam.value.filter((entry) => offeringPokemonIds.value.includes(entry.pokemonId)),
)
const selectedRequest = computed(() =>
  targetTeam.value.filter((entry) => requestingPokemonIds.value.includes(entry.pokemonId)),
)

watch(targetPlayerId, () => {
  offeringPokemonIds.value = []
  requestingPokemonIds.value = []
  submittedTrade.value = null
})

async function refreshRoster() {
  await fetchLeagueState()
}

function canSelectFreeAgent(pokemon: Pokemon) {
  if (!league.value || pokemonStore.getPointValue(pokemon.id) > league.value.pointLimit) return false
  const canAddDirectly =
    myTeam.value.length < league.value.rounds &&
    myPointTotal.value + getPointValue(pokemon.id) <= league.value.pointLimit
  if (canAddDirectly) return true

  return myTeam.value.some(
    (entry) =>
      myPointTotal.value - entry.points + getPointValue(pokemon.id) <= league.value!.pointLimit,
  )
}

function freeAgentDisabledReason(pokemon: Pokemon) {
  if (getPointValue(pokemon.id) > (league.value?.pointLimit ?? 0)) {
    return 'Costs more than the roster limit'
  }
  return 'No valid one-for-one roster transaction'
}

function stageAdd(pokemon: Pokemon) {
  addPokemonId.value = pokemon.id
  if (
    dropPokemonId.value !== null &&
    resultingPointTotal.value <= (league.value?.pointLimit ?? 0) &&
    resultingRosterCount.value <= (league.value?.rounds ?? 0)
  ) {
    return
  }
  if (!transactionNeedsDrop.value) dropPokemonId.value = null
}

function toggleDrop(pokemonId: number) {
  dropPokemonId.value = dropPokemonId.value === pokemonId ? null : pokemonId
}

function clearRosterTransaction() {
  addPokemonId.value = null
  dropPokemonId.value = null
}

async function submitRosterTransaction() {
  if (!leagueCode.value || !currentPlayerId.value || !transactionIsValid.value) return
  isSubmitting.value = true

  try {
    const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/roster/transaction`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        playerId: currentPlayerId.value,
        pin: authStore.pin,
        addPokemonId: addPokemonId.value,
        dropPokemonId: dropPokemonId.value,
      }),
    })
    if (!res.ok) throw new Error((await res.text()) || 'Roster transaction failed.')

    enqueueSnackbar('Roster transaction completed.', 'success')
    clearRosterTransaction()
    await refreshRoster()
  } catch (error) {
    enqueueSnackbar(
      error instanceof Error ? error.message : 'Roster transaction failed.',
      'error',
    )
  } finally {
    isSubmitting.value = false
  }
}

async function submitTrade() {
  if (!leagueCode.value || !currentPlayerId.value || !targetPlayerId.value) return

  if (!offeringPokemonIds.value.length || !requestingPokemonIds.value.length) {
    enqueueSnackbar('Select at least one Pokémon on both sides before proposing a trade.', 'error')
    return
  }

  isSubmitting.value = true

  try {
    const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/trades`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        initiatorPlayerId: currentPlayerId.value,
        initiatorPin: authStore.pin,
        targetPlayerId: targetPlayerId.value,
        offeringPokemonIds: offeringPokemonIds.value,
        requestingPokemonIds: requestingPokemonIds.value,
      }),
    })

    if (!res.ok) {
      const text = await res.text()
      throw new Error(text || 'Failed to propose trade.')
    }

    submittedTrade.value = (await res.json()) as Trade
    enqueueSnackbar('Trade proposal sent successfully.', 'success')
    offeringPokemonIds.value = []
    requestingPokemonIds.value = []
    await refreshRoster()
  } catch (error) {
    enqueueSnackbar(
      error instanceof Error ? error.message : 'Failed to propose trade.',
      'error',
    )
  } finally {
    isSubmitting.value = false
  }
}

function handleLeagueState(state: LeagueState) {
  applyState(state)
}

onMounted(async () => {
  await loadPage()
  if (!leagueCode.value) return
  await subscribe(leagueCode.value, handleLeagueState)
})

onUnmounted(() => unsubscribe(handleLeagueState))
</script>

<template>
  <main class="roster-view page">
    <section v-if="isLoading" class="state-card loading-card">
      <PokeballLoader variant="page" label="Loading roster tools…" />
    </section>

    <template v-else-if="league">
      <section class="hero-card">
        <div class="hero-left">
          <p class="eyebrow">{{ league.name }}</p>
          <h1>Team Management</h1>
        </div>
        <div class="hero-actions">
          <div class="connection-badge" :class="isConnected ? 'live' : 'offline'">
            {{ isConnected ? '● Live' : '○ Offline' }}
          </div>
          <RouterLink class="secondary-link" to="/team">← My Team</RouterLink>
        </div>
      </section>

      <section v-if="!draftComplete" class="state-card warning-card">
        <h2>Draft is still in progress</h2>
        <p>Come back here after the draft is complete to manage your roster.</p>
        <RouterLink class="secondary-link" to="/draft">Go to Draft Board</RouterLink>
      </section>

      <template v-else>
        <div class="page-body">
          <v-card class="toolbar-card" variant="outlined">
            <v-btn-toggle
              v-model="activeTab"
              color="primary"
              density="comfortable"
              mandatory
              divided
            >
              <v-btn value="add-drop" prepend-icon="mdi-swap-horizontal">
                Add / Drop
              </v-btn>
              <v-btn value="trade" prepend-icon="mdi-account-switch">
                Propose Trade
              </v-btn>
            </v-btn-toggle>
            <v-chip color="primary" variant="tonal">
              {{ myPointTotal }} / {{ league.pointLimit }} pts
            </v-chip>
          </v-card>

          <section v-if="activeTab === 'add-drop'" class="add-drop-layout">
            <aside class="team-sidebar panel-card">
              <div class="section-header">
                <div class="sidebar-heading">
                  <AppIcon :path="mdiAccountGroup" :size="16" />
                  <h2>Build Transaction</h2>
                </div>
                <span class="section-meta">{{ myTeam.length }} Pokémon</span>
              </div>
              <div class="points-bar">
                <span class="points-used">{{ myPointTotal }}</span>
                <span class="points-sep">/</span>
                <span class="points-limit">{{ league.pointLimit }} pts</span>
                <div class="points-track">
                  <div
                    class="points-fill"
                    :style="{
                      width: `${Math.min(100, (myPointTotal / league.pointLimit) * 100)}%`,
                      background:
                        myPointTotal > league.pointLimit
                          ? '#f87171'
                          : myPointTotal > league.pointLimit * 0.85
                            ? '#f97316'
                            : 'var(--primary)',
                    }"
                  />
                </div>
              </div>

              <div class="transaction-preview">
                <div class="transaction-slot">
                  <span class="transaction-label">Add</span>
                  <div v-if="selectedAddPokemon" class="transaction-pokemon">
                    <img :src="selectedAddPokemon.spriteUrl" :alt="selectedAddPokemon.name" />
                    <div>
                      <strong>{{ formatPokemonName(selectedAddPokemon.name) }}</strong>
                      <span>{{ getPointValue(selectedAddPokemon.id) }} pts</span>
                    </div>
                    <v-btn icon="mdi-close" size="x-small" variant="text" @click="addPokemonId = null" />
                  </div>
                  <span v-else class="empty-text">Choose a free agent from the grid.</span>
                </div>

                <div class="transaction-slot" :class="{ required: transactionNeedsDrop }">
                  <span class="transaction-label">
                    Drop
                    <small v-if="transactionNeedsDrop">Required</small>
                  </span>
                  <div v-if="selectedDrop" class="transaction-pokemon">
                    <img
                      v-if="selectedDrop.pokemon"
                      :src="selectedDrop.pokemon.spriteUrl"
                      :alt="selectedDrop.pokemon.name"
                    />
                    <div>
                      <strong>{{ getPokemonName(selectedDrop.pokemonId) }}</strong>
                      <span>{{ selectedDrop.points }} pts</span>
                    </div>
                    <v-btn icon="mdi-close" size="x-small" variant="text" @click="dropPokemonId = null" />
                  </div>
                  <span v-else class="empty-text">Optional unless points or roster space require it.</span>
                </div>
              </div>

              <div v-if="myTeam.length" class="team-list">
                <button
                  v-for="entry in myTeam"
                  :key="entry.pokemonId"
                  class="team-row"
                  :class="{ selected: dropPokemonId === entry.pokemonId }"
                  @click="toggleDrop(entry.pokemonId)"
                >
                  <div class="pokemon-info">
                    <img
                      v-if="entry.pokemon?.spriteUrl"
                      :src="entry.pokemon.spriteUrl"
                      :alt="
                        entry.pokemon
                          ? formatPokemonName(entry.pokemon.name)
                          : `#${entry.pokemonId}`
                      "
                      class="pokemon-sprite"
                    />
                    <div v-else class="sprite-fallback">#{{ entry.pokemonId }}</div>
                    <div>
                      <h3>
                        {{
                          entry.pokemon
                            ? formatPokemonName(entry.pokemon.name)
                            : `#${entry.pokemonId}`
                        }}
                      </h3>
                      <p>{{ entry.points }} pts</p>
                    </div>
                  </div>
                  <v-chip
                    :color="dropPokemonId === entry.pokemonId ? 'error' : undefined"
                    size="small"
                    variant="tonal"
                  >
                    {{ dropPokemonId === entry.pokemonId ? 'Dropping' : 'Select drop' }}
                  </v-chip>
                </button>
              </div>
              <p v-else class="empty-text">Your roster is currently empty.</p>

              <div class="transaction-footer">
                <div class="result-row">
                  <span>Result</span>
                  <strong
                    :class="{
                      invalid:
                        resultingPointTotal > league.pointLimit ||
                        resultingRosterCount > league.rounds,
                    }"
                  >
                    {{ resultingPointTotal }} / {{ league.pointLimit }} pts ·
                    {{ resultingRosterCount }} / {{ league.rounds }} Pokémon
                  </strong>
                </div>
                <p class="transaction-guidance">{{ transactionSummary }}</p>
                <div class="transaction-actions">
                  <v-btn variant="text" :disabled="isSubmitting" @click="clearRosterTransaction">
                    Clear
                  </v-btn>
                  <v-btn
                    color="primary"
                    :disabled="!transactionIsValid"
                    :loading="isSubmitting"
                    @click="submitRosterTransaction"
                  >
                    Submit Transaction
                  </v-btn>
                </div>
              </div>
            </aside>

            <PokemonGrid
              mode="select"
              :regulation-set="league.regulationSet"
              :picked-pokemon-ids="rosteredPokemonIds"
              :hide-picked-default="true"
              action-label="Stage Add"
              :can-select="canSelectFreeAgent"
              :disabled-reason="freeAgentDisabledReason"
              @select="stageAdd"
            />
          </section>

          <section v-else class="trade-layout">
            <v-card v-if="submittedTrade" variant="outlined" color="success">
              <v-card-title class="section-header">
                <div>
                  <div class="text-overline text-medium-emphasis">Trade sent</div>
                  <span>Proposal delivered</span>
                </div>
                <v-chip color="warning" size="small" variant="tonal">
                  Trade #{{ submittedTrade.id }}
                </v-chip>
              </v-card-title>
              <v-card-text>
                Your proposal was sent to {{ getPlayerName(submittedTrade.targetPlayerId) }}.
              </v-card-text>
              <v-card-actions>
                <v-btn
                  color="primary"
                  variant="flat"
                  prepend-icon="mdi-arrow-left"
                  @click="router.push('/league?tab=team')"
                >
                  Back to My Team
                </v-btn>
              </v-card-actions>
            </v-card>

            <template v-else>
              <v-card variant="outlined">
                <v-card-title class="section-header">
                  <div>
                    <div class="text-overline text-medium-emphasis">Step 1</div>
                    <span>Select target player</span>
                  </div>
                </v-card-title>
                <v-card-text>
                  <v-select
                    v-model="targetPlayerId"
                    :items="otherPlayers"
                    item-title="name"
                    item-value="id"
                    label="Choose a manager"
                    prepend-inner-icon="mdi-account-search"
                    variant="outlined"
                    hide-details
                  />
                </v-card-text>
              </v-card>

              <v-card v-if="targetPlayerId" variant="outlined">
                <v-card-title class="section-header">
                  <div>
                    <div class="text-overline text-medium-emphasis">Steps 2 & 3</div>
                    <span>Build the trade</span>
                  </div>
                </v-card-title>
                <v-card-text>
                  <v-row>
                    <v-col cols="12" md="6">
                      <v-card variant="tonal">
                        <v-card-title class="trade-roster-title">
                          <span>Your team</span>
                          <v-chip size="small" variant="tonal">
                            {{ offeringPokemonIds.length }} selected
                          </v-chip>
                        </v-card-title>
                        <v-card-subtitle>Select what you’re offering</v-card-subtitle>
                        <v-list bg-color="transparent">
                          <v-list-item
                            v-for="entry in myTeam"
                            :key="`offer-${entry.pokemonId}`"
                            @click="
                              offeringPokemonIds.includes(entry.pokemonId)
                                ? (offeringPokemonIds = offeringPokemonIds.filter(
                                    (id) => id !== entry.pokemonId,
                                  ))
                                : offeringPokemonIds.push(entry.pokemonId)
                            "
                          >
                            <template #prepend>
                              <v-checkbox-btn
                                v-model="offeringPokemonIds"
                                :value="entry.pokemonId"
                                @click.stop
                              />
                              <v-avatar size="44">
                                <v-img
                                  v-if="entry.pokemon?.spriteUrl"
                                  :src="entry.pokemon.spriteUrl"
                                  :alt="getPokemonName(entry.pokemonId)"
                                />
                              </v-avatar>
                            </template>
                            <v-list-item-title>{{ getPokemonName(entry.pokemonId) }}</v-list-item-title>
                            <template #append>
                              <v-chip size="small" color="primary" variant="tonal">
                                {{ entry.points }} pts
                              </v-chip>
                            </template>
                          </v-list-item>
                        </v-list>
                      </v-card>
                    </v-col>

                    <v-col cols="12" md="6">
                      <v-card variant="tonal">
                        <v-card-title class="trade-roster-title">
                          <span>{{ getPlayerName(targetPlayerId) }}’s team</span>
                          <v-chip size="small" variant="tonal">
                            {{ requestingPokemonIds.length }} selected
                          </v-chip>
                        </v-card-title>
                        <v-card-subtitle>Select what you’re requesting</v-card-subtitle>
                        <v-list bg-color="transparent">
                          <v-list-item
                            v-for="entry in targetTeam"
                            :key="`request-${entry.pokemonId}`"
                            @click="
                              requestingPokemonIds.includes(entry.pokemonId)
                                ? (requestingPokemonIds = requestingPokemonIds.filter(
                                    (id) => id !== entry.pokemonId,
                                  ))
                                : requestingPokemonIds.push(entry.pokemonId)
                            "
                          >
                            <template #prepend>
                              <v-checkbox-btn
                                v-model="requestingPokemonIds"
                                :value="entry.pokemonId"
                                @click.stop
                              />
                              <v-avatar size="44">
                                <v-img
                                  v-if="entry.pokemon?.spriteUrl"
                                  :src="entry.pokemon.spriteUrl"
                                  :alt="getPokemonName(entry.pokemonId)"
                                />
                              </v-avatar>
                            </template>
                            <v-list-item-title>{{ getPokemonName(entry.pokemonId) }}</v-list-item-title>
                            <template #append>
                              <v-chip size="small" color="primary" variant="tonal">
                                {{ entry.points }} pts
                              </v-chip>
                            </template>
                          </v-list-item>
                        </v-list>
                      </v-card>
                    </v-col>
                  </v-row>
                </v-card-text>
              </v-card>

              <v-card v-if="targetPlayerId" variant="outlined">
                <v-card-title class="section-header">
                  <div>
                    <div class="text-overline text-medium-emphasis">Step 4</div>
                    <span>Preview and submit</span>
                  </div>
                </v-card-title>
                <v-card-text>
                  <v-row>
                    <v-col cols="12" md="6">
                      <v-card variant="tonal">
                        <v-card-title class="text-subtitle-1">You offer</v-card-title>
                        <v-list v-if="selectedOffer.length" bg-color="transparent" density="compact">
                          <v-list-item
                            v-for="entry in selectedOffer"
                            :key="`preview-offer-${entry.pokemonId}`"
                            :title="getPokemonName(entry.pokemonId)"
                          >
                            <template #append>
                              <v-chip size="x-small" variant="tonal">{{ entry.points }} pts</v-chip>
                            </template>
                          </v-list-item>
                        </v-list>
                        <v-card-text v-else class="text-medium-emphasis">
                          Nothing selected yet.
                        </v-card-text>
                      </v-card>
                    </v-col>
                    <v-col cols="12" md="6">
                      <v-card variant="tonal">
                        <v-card-title class="text-subtitle-1">You request</v-card-title>
                        <v-list v-if="selectedRequest.length" bg-color="transparent" density="compact">
                          <v-list-item
                            v-for="entry in selectedRequest"
                            :key="`preview-request-${entry.pokemonId}`"
                            :title="getPokemonName(entry.pokemonId)"
                          >
                            <template #append>
                              <v-chip size="x-small" variant="tonal">{{ entry.points }} pts</v-chip>
                            </template>
                          </v-list-item>
                        </v-list>
                        <v-card-text v-else class="text-medium-emphasis">
                          Nothing selected yet.
                        </v-card-text>
                      </v-card>
                    </v-col>
                  </v-row>
                </v-card-text>
                <v-card-actions class="justify-end">
                  <v-btn
                    color="primary"
                    variant="flat"
                    prepend-icon="mdi-send"
                    :disabled="!selectedOffer.length || !selectedRequest.length"
                    :loading="isSubmitting"
                    @click="submitTrade"
                  >
                    Submit Trade
                  </v-btn>
                </v-card-actions>
              </v-card>
            </template>
          </section>
        </div>
      </template>
    </template>
  </main>

</template>

<style scoped>
.roster-view {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  height: 85dvh;
  max-height: 85dvh;
  overflow: hidden;
  padding: 8px;
}

.hero-card,
.toolbar-card,
.panel-card,
.state-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 6px;
}

.hero-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 8px;
  flex-shrink: 0;
}

.hero-left {
  display: flex;
  align-items: baseline;
  gap: 0.85rem;
}

.hero-left h1 {
  font-size: 1.1rem;
  font-weight: 800;
  margin: 0;
}

.toolbar-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  flex-wrap: wrap;
  padding: 6px 8px;
  flex-shrink: 0;
}

.page-body {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
  padding: 0;
  gap: 8px;
}

.page-body :deep(.v-card) {
  border-color: var(--border-color);
  border-radius: 6px;
}

.page-body :deep(.v-card-text) {
  padding: 8px;
}

.page-body :deep(.v-card-actions) {
  padding: 6px 8px 8px;
}

.trade-layout :deep(.v-row) {
  margin: -4px;
}

.trade-layout :deep(.v-col) {
  padding: 4px;
}

.panel-card {
  border-radius: 6px;
  padding: 8px;
}

/* State cards (loading / error / warning) */
.state-card {
  border-radius: 6px;
  padding: 12px;
  margin: 0;
}

.state-card h1 {
  font-size: 1.4rem;
  margin-bottom: 0.4rem;
}
.state-card h2 {
  font-size: 1.1rem;
  margin-bottom: 0.4rem;
}
.loading-card {
  display: flex;
  justify-content: center;
  padding: 3rem 1.5rem;
}

.section-header,
.hero-actions,
.team-row,
.pokemon-info {
  display: flex;
  align-items: center;
}

.section-header,
.hero-actions,
.team-row {
  justify-content: space-between;
}

.section-header {
  gap: 1rem;
  flex-wrap: wrap;
}

.hero-actions,
.team-row,
.pokemon-info {
  gap: 0.85rem;
}

.eyebrow {
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  font-size: 0.72rem;
  margin-bottom: 0.35rem;
}

h1,
h2,
h3 {
  color: var(--text);
}

.subtitle,
.section-meta,
.message,
.pokemon-info p,
.empty-text {
  color: var(--text-muted);
}

.connection-badge,
.points-pill {
  border-radius: 999px;
  font-size: 0.85rem;
  font-weight: 700;
}

.connection-badge {
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

.secondary-link {
  color: var(--secondary);
  font-weight: 700;
}

.warning-card {
  border-color: rgba(245, 158, 11, 0.4);
}

.error-card {
  border-color: rgba(248, 113, 113, 0.45);
}

.points-pill {
  background: rgba(59, 76, 202, 0.14);
  color: #a5b4fc;
  padding: 0.45rem 0.8rem;
}

.message {
  font-size: 0.95rem;
}

.error-message {
  color: #f87171;
}

.success-message {
  color: #34d399;
}

/* ── Add/Drop split layout ───────────────────────────────────────────────── */
.add-drop-layout {
  display: grid;
  grid-template-columns: minmax(340px, 390px) 1fr;
  gap: 8px;
  align-items: stretch;
  flex: 1;
  min-height: 0;
}

.team-sidebar {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  min-height: 0;
}

.sidebar-heading {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.points-bar {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  margin: 8px 0 4px;
  font-size: 0.82rem;
}

.points-used {
  font-weight: 700;
  color: var(--text);
}
.points-sep,
.points-limit {
  color: var(--text-muted);
}

.points-track {
  flex: 1;
  height: 6px;
  background: var(--border-color);
  border-radius: 999px;
  overflow: hidden;
}

.points-fill {
  height: 100%;
  border-radius: 999px;
  transition: width 0.3s ease;
}

.team-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-top: 8px;
  overflow-y: auto;
  flex: 1;
  padding-right: 0.15rem;
}

.transaction-preview {
  display: grid;
  gap: 6px;
  margin-top: 8px;
}

.transaction-slot {
  border: 1px solid var(--border-color);
  border-radius: 6px;
  padding: 8px;
  background: var(--bg);
}

.transaction-slot.required {
  border-color: rgba(245, 158, 11, 0.7);
}

.transaction-label {
  display: flex;
  align-items: center;
  justify-content: space-between;
  color: var(--text-muted);
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  margin-bottom: 0.4rem;
}

.transaction-label small {
  color: #fbbf24;
}

.transaction-pokemon {
  display: flex;
  align-items: center;
  gap: 0.55rem;
}

.transaction-pokemon img {
  width: 42px;
  height: 42px;
  image-rendering: pixelated;
}

.transaction-pokemon div {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
}

.transaction-pokemon strong,
.transaction-pokemon span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.transaction-pokemon span {
  color: var(--text-muted);
  font-size: 0.75rem;
}

.team-row,
.free-agent-row {
  background: var(--bg);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  padding: 8px;
}

.team-row {
  width: 100%;
  color: inherit;
  cursor: pointer;
  text-align: left;
}

.team-row.selected {
  border-color: #f87171;
  background: color-mix(in srgb, #f87171 10%, var(--input-bg));
}

.transaction-footer {
  border-top: 1px solid var(--border-color);
  margin-top: 8px;
  padding-top: 8px;
}

.result-row,
.transaction-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
}

.result-row {
  font-size: 0.82rem;
}

.result-row strong.invalid {
  color: #f87171;
}

.transaction-guidance {
  color: var(--text-muted);
  font-size: 0.78rem;
  margin: 0.45rem 0 0.65rem;
}

.transaction-actions {
  justify-content: flex-end;
}

.pokemon-info {
  min-width: 0;
}

.pokemon-info h3,
.trade-roster-title {
  margin-bottom: 0.25rem;
}

.pokemon-sprite,
.mini-sprite {
  image-rendering: pixelated;
  flex-shrink: 0;
}

.pokemon-sprite {
  width: 56px;
  height: 56px;
}

.mini-sprite,
.sprite-fallback {
  width: 44px;
  height: 44px;
}

.sprite-fallback {
  display: grid;
  place-items: center;
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.04);
  color: var(--text-muted);
}

.trade-layout {
  display: flex;
  flex-direction: column;
  gap: 8px;
  flex: 1;
  overflow-y: auto;
  padding-right: 0.15rem;
}

.trade-roster-title {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  flex-wrap: wrap;
}

@media (max-width: 900px) {
  .add-drop-layout {
    grid-template-columns: 1fr;
  }

  .team-sidebar {
    max-height: 280px;
  }
}

@media (max-width: 720px) {
  .roster-view {
    border-left: 0;
    border-right: 0;
    border-radius: 0;
    height: auto;
    max-height: none;
    overflow: visible;
    padding: 6px;
  }

  .hero-card,
  .toolbar-card,
  .panel-card,
  .state-card,
  .trade-layout :deep(.v-card) {
    border-left: 0;
    border-right: 0;
    border-radius: 0;
  }

  .page-body {
    flex: none;
    min-height: auto;
    overflow: visible;
  }

  .add-drop-layout {
    flex: none;
    min-height: auto;
    overflow: visible;
  }

  .team-sidebar {
    max-height: none;
    min-height: auto;
    overflow: visible;
  }

  .team-list {
    flex: none;
    overflow: visible;
  }
}

@media (max-width: 640px) {
  .team-row {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
