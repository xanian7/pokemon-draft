<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import { REGULATIONS } from '@/data/regulations'
import { useSignalR, API_BASE } from '@/services/signalr'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import { useDraftStore } from '@/stores/draft'
import {
  mdiCog,
  mdiCheck,
  mdiTrophy,
  mdiClipboardList,
  mdiAlert,
  mdiArrowUp,
  mdiArrowDown,
} from '@mdi/js'

const router = useRouter()
const authStore = useAuthStore()
const pokemonStore = usePokemonStore()
const draftStore = useDraftStore()
const { subscribe, unsubscribe, isConnected } = useSignalR()

// Redirect non-admin/non-auth users
if (!authStore.isAuthenticated) router.replace('/join')
else if (!authStore.isAdmin) router.replace('/draft')

// ── Local state synced from server ───────────────────────────────────────────
const leagueName = ref('')
const pointLimit = ref(100)
const rounds = ref(6)
const playoffSpots = ref(4)
const regulationSet = ref('national')
const players = ref<{ id: string; name: string }[]>([])
const leagueCode = computed(() => authStore.leagueCode ?? '')

const newPlayerName = ref('')
const newPlayerPin = ref('')
const addError = ref('')
const showResetWarning = ref(false)
const isSaving = ref(false)
const selfPin = ref('')

function applyState(state: any) {
  leagueName.value = state.name
  pointLimit.value = state.pointLimit
  rounds.value = state.rounds
  playoffSpots.value = state.playoffSpots ?? 4
  regulationSet.value = state.regulationSet ?? 'national'
  players.value = state.players
}

onMounted(async () => {
  if (!authStore.leagueCode) return
  await subscribe(authStore.leagueCode, applyState)
  // Seed point values from server state to local pokemon store
  const res = await fetch(`${API_BASE}/leagues/${authStore.leagueCode}`)
  if (res.ok) {
    const state = await res.json()
    applyState(state)
    for (const [id, pts] of Object.entries(state.pointValues as Record<string, number>)) {
      pokemonStore.setPointValue(Number(id), pts)
    }
  }
})

onUnmounted(() => unsubscribe(applyState))

// ── API helpers ───────────────────────────────────────────────────────────────
async function patch(path: string, body: object) {
  isSaving.value = true
  await fetch(`${API_BASE}/leagues/${leagueCode.value}${path}`, {
    method: path === '/players/move' || (path.startsWith('/players/') && body) ? 'POST' : 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  isSaving.value = false
}

async function saveConfig() {
  await fetch(`${API_BASE}/leagues/${leagueCode.value}/config`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      name: leagueName.value,
      pointLimit: pointLimit.value,
      rounds: rounds.value,
      playoffSpots: playoffSpots.value,
      regulationSet: regulationSet.value,
    }),
  })
}

async function addPlayer() {
  addError.value = ''
  const name = newPlayerName.value.trim()
  const pin = newPlayerPin.value.trim()
  if (!name || !pin) {
    addError.value = 'Name and PIN required.'
    return
  }

  const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/players`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, pin }),
  })
  if (res.ok) {
    newPlayerName.value = ''
    newPlayerPin.value = ''
  } else {
    addError.value = 'Failed to add player.'
  }
}

async function removePlayer(id: string) {
  const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/players/${id}`, {
    method: 'DELETE',
  })
  if (res.ok) {
    // Optimistic update — SignalR will confirm
    players.value = players.value.filter((p) => p.id !== id)
  } else {
    addError.value = 'Failed to remove player.'
  }
}

async function addSelf() {
  const pin =
    selfPin.value.trim() ||
    prompt('Choose a player PIN for yourself (different from your admin PIN):')
  if (!pin) return
  addError.value = ''
  const name = authStore.playerName ?? 'Commissioner'
  const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/players`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, pin }),
  })
  if (res.ok) {
    selfPin.value = ''
  } else {
    addError.value = 'Failed to add yourself as player.'
  }
}

async function movePlayer(from: number, to: number) {
  await fetch(`${API_BASE}/leagues/${leagueCode.value}/players/move`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ fromIndex: from, toIndex: to }),
  })
}

async function startDraft() {
  const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/draft/start`, { method: 'POST' })
  if (res.ok) router.push('/league?tab=draft')
}

async function resetDraft() {
  await fetch(`${API_BASE}/leagues/${leagueCode.value}/draft/reset`, { method: 'POST' })
  showResetWarning.value = false
}

const inviteLink = computed(() => `${window.location.origin}/register?code=${leagueCode.value}`)
const linkCopied = ref(false)
async function copyInviteLink() {
  await navigator.clipboard.writeText(inviteLink.value)
  linkCopied.value = true
  setTimeout(() => (linkCopied.value = false), 2000)
}

const snakePreview = computed(() => {
  const n = players.value.length
  if (n < 2) return []
  const previewRounds = Math.min(rounds.value, 4)
  return Array.from({ length: previewRounds * n }, (_, i) => {
    const round = Math.floor(i / n)
    const pos = i % n
    const idx = round % 2 === 0 ? pos : n - 1 - pos
    return { pickNumber: i, round, player: players.value[idx] }
  })
})
</script>

<template>
  <main class="league-setup">
    <div class="page-header">
      <h1><AppIcon :path="mdiCog" :size="24" /> League Setup</h1>
      <div class="connection-badge" :class="isConnected ? 'live' : 'offline'">
        {{ isConnected ? '● Live' : '○ Disconnected' }}
      </div>
    </div>

    <div class="invite-banner">
      <div class="invite-top">
        <span class="label">Invite Players</span>
        <span class="code-pill">{{ leagueCode }}</span>
      </div>
      <div class="invite-link-row">
        <input
          class="invite-url"
          :value="inviteLink"
          readonly
          @focus="($event.target as HTMLInputElement).select()"
        />
        <button class="btn-copy" @click="copyInviteLink">
          <template v-if="linkCopied"><AppIcon :path="mdiCheck" :size="16" /> Copied!</template>
          <template v-else>Copy Link</template>
        </button>
      </div>
      <p class="hint">Share this link — players click it to choose their name and PIN.</p>
    </div>

    <section class="card">
      <h2>League Info</h2>
      <div class="form-row">
        <label>League Name</label>
        <input v-model="leagueName" type="text" @change="saveConfig" />
      </div>
      <div class="form-row">
        <label>Team Point Limit</label>
        <input v-model.number="pointLimit" type="number" min="1" @change="saveConfig" />
        <span class="hint">Max total points a player can spend.</span>
      </div>
      <div class="form-row">
        <label>Draft Rounds</label>
        <input v-model.number="rounds" type="number" min="1" max="20" @change="saveConfig" />
        <span class="hint">Pokémon drafted per player.</span>
      </div>
      <div class="form-row">
        <label>Playoff Spots</label>
        <input v-model.number="playoffSpots" type="number" min="2" max="16" @change="saveConfig" />
        <span class="hint">Teams that advance to playoffs.</span>
      </div>
      <div class="form-row">
        <label>Regulation Set</label>
        <select v-model="regulationSet" @change="saveConfig">
          <option v-for="reg in REGULATIONS" :key="reg.id" :value="reg.id">{{ reg.label }}</option>
        </select>
        <span class="hint">Filters which Pokémon are available to draft.</span>
      </div>
    </section>

    <section class="card">
      <h2>Players</h2>
      <p class="hint">
        Add players and assign each a unique PIN. They'll use it to log in at
        <strong>/join</strong>.
      </p>

      <div class="add-player">
        <input
          v-model="newPlayerName"
          type="text"
          placeholder="Player name"
          @keydown.enter="addPlayer"
        />
        <input
          v-model="newPlayerPin"
          type="text"
          placeholder="PIN"
          style="width: 90px"
          @keydown.enter="addPlayer"
        />
        <button class="btn btn-primary" @click="addPlayer">Add</button>
      </div>

      <div v-if="addError" class="error-msg">{{ addError }}</div>
      <ul class="player-list">
        <li v-if="players.length === 0" class="empty">No players yet.</li>
        <li v-for="(player, index) in players" :key="player.id" class="player-item">
          <span class="player-order">{{ index + 1 }}</span>
          <span class="player-name">{{ player.name }}</span>
          <button class="btn-icon" @click="movePlayer(index, index - 1)" :disabled="index === 0">
            <AppIcon :path="mdiArrowUp" :size="18" />
          </button>
          <button
            class="btn-icon"
            @click="movePlayer(index, index + 1)"
            :disabled="index === players.length - 1"
          >
            <AppIcon :path="mdiArrowDown" :size="18" />
          </button>
          <button class="btn btn-danger" @click="removePlayer(player.id)">Remove</button>
        </li>
      </ul>
    </section>

    <section class="card" v-if="players.length >= 2">
      <h2>Snake Draft Preview</h2>
      <div class="pick-schedule">
        <div v-for="entry in snakePreview" :key="entry.pickNumber" class="pick-slot">
          <span class="pick-number">Pick {{ entry.pickNumber + 1 }}</span>
          <span class="pick-player">{{ entry.player?.name }}</span>
          <span class="pick-round">R{{ entry.round + 1 }}</span>
        </div>
      </div>
    </section>

    <section class="card">
      <h2>Draft Controls</h2>
      <div class="btn-row">
        <button class="btn btn-primary" :disabled="players.length < 2" @click="startDraft">
          <AppIcon :path="mdiTrophy" :size="18" />
          Start Draft
        </button>
        <button class="btn-secondary" @click="router.push('/league?tab=pokemon')">
          <AppIcon :path="mdiClipboardList" :size="18" />
          Manage Point Values
        </button>
        <button class="btn btn-danger" @click="showResetWarning = true">Reset Draft</button>
      </div>
      <p v-if="players.length < 2" class="hint" style="margin-top: 0.5rem">
        Need at least 2 players to start.
      </p>
      <div v-if="showResetWarning" class="confirm-reset">
        <p>
          <AppIcon :path="mdiAlert" :size="18" /> This will erase all draft picks. Are you sure?
        </p>
        <button class="btn btn-danger" @click="resetDraft">Yes, Reset</button>
        <button @click="showResetWarning = false">Cancel</button>
      </div>
    </section>
  </main>
</template>

<style scoped>
.league-setup {
  max-width: 700px;
  margin: 0 auto;
  padding: 2rem 1rem;
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.page-header {
  display: flex;
  align-items: center;
  gap: 1rem;
}

h1 {
  font-size: 1.6rem;
  font-weight: 800;
  margin: 0;
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.connection-badge {
  font-size: 0.75rem;
  font-weight: 700;
  padding: 0.2rem 0.6rem;
  border-radius: 20px;
}

.connection-badge.live {
  color: #10b981;
  background: rgba(16, 185, 129, 0.12);
}
.connection-badge.offline {
  color: var(--text-muted);
  background: var(--input-bg);
}

.invite-banner {
  background: var(--card-bg);
  border: 2px dashed var(--border-color);
  border-radius: 10px;
  padding: 1rem 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}

.invite-top {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.invite-top .label {
  font-size: 0.75rem;
  text-transform: uppercase;
  color: var(--text-muted);
  letter-spacing: 0.05em;
  font-weight: 700;
}

.code-pill {
  font-size: 1rem;
  font-weight: 900;
  letter-spacing: 0.15em;
  color: var(--primary);
  background: rgba(204, 0, 0, 0.1);
  border-radius: 6px;
  padding: 0.15rem 0.6rem;
}

.invite-link-row {
  display: flex;
  gap: 0.5rem;
}

.invite-url {
  flex: 1;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  color: var(--text-muted);
  border-radius: 6px;
  padding: 0.4rem 0.6rem;
  font-size: 0.82rem;
  cursor: text;
}

.btn-copy {
  background: var(--secondary);
  color: white;
  border: none;
  border-radius: 6px;
  padding: 0.4rem 0.9rem;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  white-space: nowrap;
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  padding: 1.5rem;
}

.card h2 {
  margin: 0 0 1rem;
  font-size: 0.85rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-muted);
}

.form-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
  flex-wrap: wrap;
}

.form-row label {
  width: 130px;
  font-weight: 600;
  font-size: 0.9rem;
  flex-shrink: 0;
}

.hint {
  font-size: 0.78rem;
  color: var(--text-muted);
  margin: 0;
}

input[type='text'],
input[type='number'],
input[type='password'] {
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  color: var(--text);
  border-radius: 6px;
  padding: 0.4rem 0.6rem;
  font-size: 0.9rem;
  flex: 1;
}

input[type='number'] {
  max-width: 100px;
}

.add-self-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
}

.btn-sm {
  padding: 0.3rem 0.75rem;
  font-size: 0.82rem;
}

.add-player input {
  flex: 1;
}

.player-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}
.empty {
  color: var(--text-muted);
  font-size: 0.9rem;
}

.player-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  background: var(--input-bg);
  border-radius: 6px;
  padding: 0.4rem 0.6rem;
}

.player-order {
  width: 1.4rem;
  font-weight: 700;
  color: var(--text-muted);
  font-size: 0.85rem;
}
.player-name {
  flex: 1;
  font-weight: 600;
}

.pick-schedule {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  gap: 0.4rem;
}

.pick-slot {
  background: var(--input-bg);
  border-radius: 6px;
  padding: 0.4rem 0.6rem;
  display: flex;
  flex-direction: column;
  font-size: 0.78rem;
}
.pick-number {
  color: var(--text-muted);
}
.pick-player {
  font-weight: 700;
}
.pick-round {
  color: var(--primary);
  font-size: 0.7rem;
}

.btn-row {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}

/* btn-secondary here is red (--secondary brand color), not the gray global .btn-secondary */
.btn-secondary {
  background: var(--secondary);
  color: white;
  border: none;
  border-radius: 6px;
  padding: 0.5rem 1.2rem;
  font-size: 0.95rem;
  font-weight: 600;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.btn-icon {
  background: transparent;
  border: 1px solid var(--border-color);
  color: var(--text);
  border-radius: 4px;
  padding: 0.2rem 0.4rem;
  cursor: pointer;
  font-size: 0.8rem;
  display: flex;
  align-items: center;
  justify-content: center;
}

.btn-icon:disabled {
  opacity: 0.3;
  cursor: not-allowed;
}

.confirm-reset {
  margin-top: 1rem;
  background: rgba(204, 0, 0, 0.1);
  border: 1px solid var(--primary);
  border-radius: 8px;
  padding: 1rem;
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.confirm-reset p {
  flex: 1;
  margin: 0;
  font-size: 0.9rem;
  display: flex;
  align-items: center;
  gap: 0.4rem;
}
</style>
