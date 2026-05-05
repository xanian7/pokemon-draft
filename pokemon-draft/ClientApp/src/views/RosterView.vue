<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
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
const isLoading = ref(true)
const loadError = ref('')
const actionError = ref('')
const actionSuccess = ref('')
const activeTab = ref<'add-drop' | 'trade'>('add-drop')
const addSearch = ref('')
const isSubmitting = ref(false)

const targetPlayerId = ref('')
const offeringPokemonIds = ref<number[]>([])
const requestingPokemonIds = ref<number[]>([])
const submittedTrade = ref<Trade | null>(null)

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

async function loadPage() {
  if (!leagueCode.value) return

  isLoading.value = true
  loadError.value = ''

  try {
    await Promise.all([pokemonStore.fetchAllPokemon(), fetchLeagueState()])
  } catch (error) {
    console.error(error)
    loadError.value = 'Unable to load roster management.'
  } finally {
    isLoading.value = false
  }
}

function getPlayerName(playerId: string) {
  return league.value?.players.find((player) => player.id === playerId)?.name ?? 'Unknown Player'
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
const targetTeam = computed(() => getTeamEntries(targetPlayerId.value))
const rosteredPokemonIds = computed(() => new Set((league.value?.draft.picks ?? []).map((pick) => pick.pokemonId)))

const availablePokemon = computed(() => {
  const query = addSearch.value.trim().toLowerCase()

  return pokemonStore.allPokemon.filter((pokemon) => {
    if (rosteredPokemonIds.value.has(pokemon.id)) return false
    if (!query) return true

    const formatted = formatPokemonName(pokemon.name).toLowerCase()
    return pokemon.name.includes(query) || formatted.includes(query) || String(pokemon.id) === query
  })
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
  actionError.value = ''
  actionSuccess.value = ''
})

function resetMessages() {
  actionError.value = ''
  actionSuccess.value = ''
}

async function refreshRoster() {
  await fetchLeagueState()
}

async function dropPokemon(pokemonId: number) {
  if (!leagueCode.value || !currentPlayerId.value) return
  const pokemonName = formatPokemonName(pokemonStore.getPokemonById(pokemonId)?.name ?? `#${pokemonId}`)
  if (!window.confirm(`Drop ${pokemonName} from your roster?`)) return

  resetMessages()
  isSubmitting.value = true

  try {
    const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/roster/drop`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        playerId: currentPlayerId.value,
        pin: authStore.pin,
        pokemonId,
      }),
    })

    if (!res.ok) {
      const text = await res.text()
      throw new Error(text || 'Failed to drop Pokémon.')
    }

    actionSuccess.value = `${pokemonName} was dropped from your roster.`
    await refreshRoster()
  } catch (error) {
    actionError.value = error instanceof Error ? error.message : 'Failed to drop Pokémon.'
  } finally {
    isSubmitting.value = false
  }
}

async function addPokemon(pokemonId: number) {
  if (!league.value || !leagueCode.value || !currentPlayerId.value) return

  const nextTotal = myPointTotal.value + getPointValue(pokemonId)
  if (nextTotal > league.value.pointLimit) {
    actionError.value = 'Adding that Pokémon would put you over the point limit.'
    actionSuccess.value = ''
    return
  }

  resetMessages()
  isSubmitting.value = true

  try {
    const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/roster/add`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        playerId: currentPlayerId.value,
        pin: authStore.pin,
        pokemonId,
      }),
    })

    if (!res.ok) {
      const text = await res.text()
      throw new Error(text || 'Failed to add Pokémon.')
    }

    actionSuccess.value = `${formatPokemonName(pokemonStore.getPokemonById(pokemonId)?.name ?? `#${pokemonId}`)} was added to your roster.`
    await refreshRoster()
  } catch (error) {
    actionError.value = error instanceof Error ? error.message : 'Failed to add Pokémon.'
  } finally {
    isSubmitting.value = false
  }
}

async function submitTrade() {
  if (!leagueCode.value || !currentPlayerId.value || !targetPlayerId.value) return

  resetMessages()

  if (!offeringPokemonIds.value.length || !requestingPokemonIds.value.length) {
    actionError.value = 'Select at least one Pokémon on both sides before proposing a trade.'
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
    actionSuccess.value = 'Trade proposal sent successfully.'
    offeringPokemonIds.value = []
    requestingPokemonIds.value = []
    await refreshRoster()
  } catch (error) {
    actionError.value = error instanceof Error ? error.message : 'Failed to propose trade.'
  } finally {
    isSubmitting.value = false
  }
}

onMounted(async () => {
  await loadPage()

  if (!leagueCode.value) return

  await connect(leagueCode.value, (state: LeagueState) => {
    applyState(state)
  })
})

onUnmounted(disconnect)
</script>

<template>
  <main class="roster-view">
    <section v-if="isLoading" class="state-card">
      <h1>Loading roster tools…</h1>
      <p>Syncing your league state.</p>
    </section>

    <section v-else-if="loadError" class="state-card error-card">
      <h1>Couldn’t load Team Management</h1>
      <p>{{ loadError }}</p>
      <button class="primary-btn" @click="loadPage">Try Again</button>
    </section>

    <template v-else-if="league">
      <section class="hero-card">
        <div>
          <p class="eyebrow">{{ league.name }}</p>
          <h1>Team Management</h1>
          <p class="subtitle">Handle add/drop moves or propose trades after the draft.</p>
        </div>
        <div class="hero-actions">
          <div class="connection-badge" :class="isConnected ? 'live' : 'offline'">
            {{ isConnected ? '● Live Updates' : '○ Offline' }}
          </div>
          <RouterLink class="secondary-link" to="/team">← Back to My Team</RouterLink>
        </div>
      </section>

      <section v-if="!draftComplete" class="state-card warning-card">
        <h2>Draft is still in progress</h2>
        <p>Come back here after the draft is complete to manage your roster.</p>
        <RouterLink class="secondary-link" to="/draft">Go to Draft Board</RouterLink>
      </section>

      <template v-else>
        <section class="toolbar-card">
          <div class="tab-list">
            <button
              class="tab-btn"
              :class="{ active: activeTab === 'add-drop' }"
              @click="activeTab = 'add-drop'"
            >
              Add / Drop
            </button>
            <button
              class="tab-btn"
              :class="{ active: activeTab === 'trade' }"
              @click="activeTab = 'trade'"
            >
              Propose Trade
            </button>
          </div>
          <div class="points-pill">{{ myPointTotal }} / {{ league.pointLimit }} pts</div>
        </section>

        <p v-if="actionError" class="message error-message">{{ actionError }}</p>
        <p v-if="actionSuccess" class="message success-message">{{ actionSuccess }}</p>

        <section v-if="activeTab === 'add-drop'" class="content-grid">
          <article class="panel-card">
            <div class="section-header">
              <div>
                <p class="eyebrow">Current roster</p>
                <h2>Your team</h2>
              </div>
              <span class="section-meta">{{ myTeam.length }} Pokémon</span>
            </div>

            <div v-if="myTeam.length" class="team-list">
              <article v-for="entry in myTeam" :key="entry.pokemonId" class="team-row">
                <div class="pokemon-info">
                  <img
                    v-if="entry.pokemon?.spriteUrl"
                    :src="entry.pokemon.spriteUrl"
                    :alt="entry.pokemon ? formatPokemonName(entry.pokemon.name) : `#${entry.pokemonId}`"
                    class="pokemon-sprite"
                  />
                  <div v-else class="sprite-fallback">#{{ entry.pokemonId }}</div>
                  <div>
                    <h3>{{ entry.pokemon ? formatPokemonName(entry.pokemon.name) : `#${entry.pokemonId}` }}</h3>
                    <p>{{ entry.points }} pts</p>
                  </div>
                </div>
                <button class="danger-btn" :disabled="isSubmitting" @click="dropPokemon(entry.pokemonId)">
                  Drop
                </button>
              </article>
            </div>
            <p v-else class="empty-text">Your roster is currently empty.</p>
          </article>

          <article class="panel-card">
            <div class="section-header">
              <div>
                <p class="eyebrow">Free agents</p>
                <h2>Add Pokémon</h2>
              </div>
              <span class="section-meta">{{ availablePokemon.length }} available</span>
            </div>

            <input
              v-model="addSearch"
              type="text"
              class="search-input"
              placeholder="Search available Pokémon…"
            />

            <div class="free-agent-list">
              <article v-for="pokemon in availablePokemon" :key="pokemon.id" class="free-agent-row">
                <div class="pokemon-info">
                  <img :src="pokemon.spriteUrl" :alt="formatPokemonName(pokemon.name)" class="pokemon-sprite" />
                  <div>
                    <h3>{{ formatPokemonName(pokemon.name) }}</h3>
                    <p>{{ getPointValue(pokemon.id) }} pts</p>
                  </div>
                </div>
                <button
                  class="primary-btn"
                  :disabled="isSubmitting || myPointTotal + getPointValue(pokemon.id) > league.pointLimit"
                  @click="addPokemon(pokemon.id)"
                >
                  Add
                </button>
              </article>
              <p v-if="!availablePokemon.length" class="empty-text">No available Pokémon match your search.</p>
            </div>
          </article>
        </section>

        <section v-else class="trade-layout">
          <article v-if="submittedTrade" class="panel-card success-card">
            <div class="section-header">
              <div>
                <p class="eyebrow">Trade sent</p>
                <h2>Proposal delivered</h2>
              </div>
              <span class="status-pill pending">Trade #{{ submittedTrade.id }}</span>
            </div>
            <p class="subtitle">Your proposal was sent to {{ getPlayerName(submittedTrade.targetPlayerId) }}.</p>
            <button class="primary-btn" @click="router.push('/team')">Back to My Team</button>
          </article>

          <template v-else>
            <article class="panel-card">
              <div class="section-header">
                <div>
                  <p class="eyebrow">Step 1</p>
                  <h2>Select target player</h2>
                </div>
              </div>

              <select v-model="targetPlayerId" class="select-input">
                <option value="">Choose a manager…</option>
                <option v-for="player in otherPlayers" :key="player.id" :value="player.id">
                  {{ player.name }}
                </option>
              </select>
            </article>

            <article v-if="targetPlayerId" class="panel-card">
              <div class="section-header">
                <div>
                  <p class="eyebrow">Steps 2 & 3</p>
                  <h2>Build the trade</h2>
                </div>
              </div>

              <div class="trade-team-grid">
                <section>
                  <div class="trade-team-header">
                    <h3>Your team</h3>
                    <span>Select what you’re offering</span>
                  </div>
                  <label v-for="entry in myTeam" :key="`offer-${entry.pokemonId}`" class="checkbox-row">
                    <input v-model="offeringPokemonIds" type="checkbox" :value="entry.pokemonId" />
                    <img
                      v-if="entry.pokemon?.spriteUrl"
                      :src="entry.pokemon.spriteUrl"
                      :alt="entry.pokemon ? formatPokemonName(entry.pokemon.name) : `#${entry.pokemonId}`"
                      class="mini-sprite"
                    />
                    <div class="checkbox-copy">
                      <strong>{{ entry.pokemon ? formatPokemonName(entry.pokemon.name) : `#${entry.pokemonId}` }}</strong>
                      <span>{{ entry.points }} pts</span>
                    </div>
                  </label>
                </section>

                <section>
                  <div class="trade-team-header">
                    <h3>{{ getPlayerName(targetPlayerId) }}’s team</h3>
                    <span>Select what you’re requesting</span>
                  </div>
                  <label v-for="entry in targetTeam" :key="`request-${entry.pokemonId}`" class="checkbox-row">
                    <input v-model="requestingPokemonIds" type="checkbox" :value="entry.pokemonId" />
                    <img
                      v-if="entry.pokemon?.spriteUrl"
                      :src="entry.pokemon.spriteUrl"
                      :alt="entry.pokemon ? formatPokemonName(entry.pokemon.name) : `#${entry.pokemonId}`"
                      class="mini-sprite"
                    />
                    <div class="checkbox-copy">
                      <strong>{{ entry.pokemon ? formatPokemonName(entry.pokemon.name) : `#${entry.pokemonId}` }}</strong>
                      <span>{{ entry.points }} pts</span>
                    </div>
                  </label>
                </section>
              </div>
            </article>

            <article v-if="targetPlayerId" class="panel-card">
              <div class="section-header">
                <div>
                  <p class="eyebrow">Step 4</p>
                  <h2>Preview and submit</h2>
                </div>
              </div>

              <div class="preview-grid">
                <div class="preview-card">
                  <h3>You offer</h3>
                  <ul>
                    <li v-for="entry in selectedOffer" :key="`preview-offer-${entry.pokemonId}`">
                      {{ entry.pokemon ? formatPokemonName(entry.pokemon.name) : `#${entry.pokemonId}` }}
                      <span>{{ entry.points }} pts</span>
                    </li>
                  </ul>
                  <p v-if="!selectedOffer.length" class="empty-text">Nothing selected yet.</p>
                </div>
                <div class="preview-card">
                  <h3>You request</h3>
                  <ul>
                    <li v-for="entry in selectedRequest" :key="`preview-request-${entry.pokemonId}`">
                      {{ entry.pokemon ? formatPokemonName(entry.pokemon.name) : `#${entry.pokemonId}` }}
                      <span>{{ entry.points }} pts</span>
                    </li>
                  </ul>
                  <p v-if="!selectedRequest.length" class="empty-text">Nothing selected yet.</p>
                </div>
              </div>

              <button class="primary-btn" :disabled="isSubmitting" @click="submitTrade">Submit Trade</button>
            </article>
          </template>
        </section>
      </template>
    </template>
  </main>
</template>

<style scoped>
.roster-view {
  max-width: 1180px;
  margin: 0 auto;
  padding: 1.5rem 1rem 2rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.hero-card,
.toolbar-card,
.panel-card,
.state-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 16px;
}

.hero-card,
.toolbar-card,
.panel-card,
.state-card {
  padding: 1.25rem;
}

.hero-card,
.toolbar-card,
.section-header,
.hero-actions,
.trade-team-header,
.team-row,
.free-agent-row,
.pokemon-info,
.checkbox-row,
.preview-card li {
  display: flex;
  align-items: center;
}

.hero-card,
.toolbar-card,
.section-header,
.hero-actions,
.trade-team-header,
.team-row,
.free-agent-row,
.preview-card li {
  justify-content: space-between;
}

.hero-card,
.toolbar-card,
.section-header {
  gap: 1rem;
  flex-wrap: wrap;
}

.hero-actions,
.trade-team-header,
.team-row,
.free-agent-row,
.pokemon-info,
.checkbox-row {
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
h3,
.checkbox-copy strong {
  color: var(--text);
}

h1 {
  font-size: 2rem;
}

.subtitle,
.section-meta,
.message,
.trade-team-header span,
.checkbox-copy span,
.preview-card li span,
.pokemon-info p,
.empty-text {
  color: var(--text-muted);
}

.connection-badge,
.points-pill,
.status-pill,
.tab-btn {
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

.tab-list {
  display: flex;
  gap: 0.6rem;
  flex-wrap: wrap;
}

.tab-btn {
  border: 1px solid var(--border-color);
  background: var(--input-bg);
  color: var(--text-muted);
  padding: 0.7rem 1rem;
  cursor: pointer;
}

.tab-btn.active {
  background: var(--secondary);
  border-color: var(--secondary);
  color: white;
}

.points-pill,
.status-pill.pending {
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

.content-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1rem;
}

.team-list,
.free-agent-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  margin-top: 1rem;
}

.free-agent-list {
  max-height: 520px;
  overflow-y: auto;
  padding-right: 0.25rem;
}

.free-agent-list::-webkit-scrollbar {
  width: 6px;
}

.free-agent-list::-webkit-scrollbar-track {
  background: var(--bg);
  border-radius: 3px;
}

.free-agent-list::-webkit-scrollbar-thumb {
  background: var(--border-color);
  border-radius: 3px;
}

.free-agent-list::-webkit-scrollbar-thumb:hover {
  background: var(--text-muted);
}

.team-row,
.free-agent-row,
.checkbox-row,
.preview-card,
.success-card {
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 0.9rem;
}

.pokemon-info {
  min-width: 0;
}

.pokemon-info h3,
.preview-card h3 {
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

.search-input,
.select-input {
  width: 100%;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  color: var(--text);
  border-radius: 10px;
  padding: 0.75rem 0.9rem;
  margin-top: 1rem;
}

.trade-layout {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.trade-team-grid,
.preview-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1rem;
  margin-top: 1rem;
}

.trade-team-header {
  margin-bottom: 0.85rem;
}

.checkbox-row {
  cursor: pointer;
  margin-bottom: 0.65rem;
}

.checkbox-row input {
  accent-color: var(--primary);
}

.checkbox-copy {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.preview-card {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}

.preview-card ul {
  margin: 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.primary-btn,
.danger-btn {
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

.danger-btn {
  background: rgba(248, 113, 113, 0.15);
  color: #f87171;
  border: 1px solid rgba(248, 113, 113, 0.4);
}

.primary-btn:disabled,
.danger-btn:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

@media (max-width: 900px) {
  .content-grid,
  .trade-team-grid,
  .preview-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 640px) {
  .roster-view {
    padding-inline: 0.75rem;
  }

  .team-row,
  .free-agent-row {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
