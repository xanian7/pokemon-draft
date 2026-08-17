<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { REGULATIONS } from '@/data/regulations'
import { useSignalR, API_BASE } from '@/services/signalr'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import { useDraftStore } from '@/stores/draft'
import { enqueueSnackbar } from '@/services/snackbar'
import PageHeader from '@/components/PageHeader.vue'
import FormField from '@/components/FormField.vue'
import type { MatchupResponse, ScheduleData } from '@/types'

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
const players = ref<
  Array<{
    id: string
    name: string
    teamName: string
    teamImageUrl: string
    isCommissioner: boolean
    isCoCommissioner: boolean
  }>
>([])
const schedule = ref<ScheduleData | null>(null)
const leagueCode = computed(() => authStore.leagueCode ?? '')
const draftStatus = ref('setup')

const newPlayerName = ref('')
const newPlayerPin = ref('')
const addError = ref('')
const showResetWarning = ref(false)
const playerPendingDrop = ref<(typeof players.value)[number] | null>(null)
const isSaving = ref(false)
const isRandomizingOrder = ref(false)
const isDroppingPlayer = ref(false)
const selfPin = ref('')
const roleSavingPlayerId = ref<string | null>(null)
const scheduleError = ref('')
const scheduleSaving = ref(false)
const scheduleDialogOpen = ref(false)
const scheduleDeleteDialogOpen = ref(false)
const editingMatchup = ref<MatchupResponse | null>(null)
const deletingMatchup = ref<MatchupResponse | null>(null)
const scheduleForm = ref({
  week: 1,
  player1Id: '',
  player2Id: '',
  forceScoredChange: false,
})

function applyState(state: any) {
  leagueName.value = state.name
  pointLimit.value = state.pointLimit
  rounds.value = state.rounds
  playoffSpots.value = state.playoffSpots ?? 4
  regulationSet.value = state.regulationSet ?? 'national'
  draftStatus.value = (state.draft?.status ?? 'Setup').toLowerCase()
  players.value = state.players.map((player: (typeof players.value)[number]) => ({
    ...player,
    teamName: player.teamName ?? '',
    teamImageUrl: player.teamImageUrl ?? '',
    isCommissioner:
      player.isCommissioner ??
      (authStore.isCommissioner && player.id === authStore.playerId),
    isCoCommissioner: player.isCoCommissioner ?? false,
  }))
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
  await fetchSchedule()
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

async function fetchSchedule() {
  if (!leagueCode.value) return

  try {
    scheduleError.value = ''
    const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/schedule`)
    if (!res.ok) {
      schedule.value = null
      return
    }

    schedule.value = (await res.json()) as ScheduleData
  } catch {
    scheduleError.value = 'Could not load the schedule editor.'
  }
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
    addError.value = 'You are not logged in.'
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

function queueDropPlayer(player: (typeof players.value)[number]) {
  if (!authStore.isCommissioner) {
    enqueueSnackbar('Only the league commissioner can drop a player.', 'error')
    return
  }
  playerPendingDrop.value = player
}

async function confirmDropPlayer() {
  const player = playerPendingDrop.value
  if (!player) return

  isDroppingPlayer.value = true
  try {
    const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/players/${player.id}`, {
      method: 'DELETE',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ commissionerPin: authStore.pin }),
    })

    if (!res.ok) {
      const message = await res.text()
      enqueueSnackbar(message || 'Unable to drop player.', 'error')
      return
    }

    players.value = players.value.filter((p) => p.id !== player.id)
    playerPendingDrop.value = null
    enqueueSnackbar(`${player.name} was dropped from the league.`, 'success')
  } catch {
    enqueueSnackbar('Could not connect to the server.', 'error')
  } finally {
    isDroppingPlayer.value = false
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

async function randomizeDraftOrder() {
  isRandomizingOrder.value = true
  try {
    const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/draft/randomize-order`, {
      method: 'POST',
    })

    if (!res.ok) {
      const message = await res.text()
      enqueueSnackbar(message || 'Unable to randomize draft order.', 'error')
      return
    }

    enqueueSnackbar('Draft order randomized.', 'success')
  } catch {
    enqueueSnackbar('Could not connect to the server.', 'error')
  } finally {
    isRandomizingOrder.value = false
  }
}

async function toggleCoCommissioner(player: (typeof players.value)[number]) {
  roleSavingPlayerId.value = player.id
  try {
    const res = await fetch(
      `${API_BASE}/leagues/${leagueCode.value}/players/${player.id}/co-commissioner`,
      {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          commissionerPin: authStore.pin,
          isCoCommissioner: !player.isCoCommissioner,
        }),
      },
    )

    if (!res.ok) {
      const message = await res.text()
      enqueueSnackbar(message || 'Unable to update commissioner access.', 'error')
      return
    }

    player.isCoCommissioner = !player.isCoCommissioner
    enqueueSnackbar(
      player.isCoCommissioner
        ? `${player.name} is now a co-commissioner.`
        : `${player.name} is no longer a co-commissioner.`,
      'success',
    )
  } catch {
    enqueueSnackbar('Could not connect to the server.', 'error')
  } finally {
    roleSavingPlayerId.value = null
  }
}

async function startDraft() {
  const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/draft/start`, { method: 'POST' })
  if (res.ok) router.push('/league?tab=draft')
}

async function resetDraft() {
  await fetch(`${API_BASE}/leagues/${leagueCode.value}/draft/reset`, { method: 'POST' })
  showResetWarning.value = false
  await fetchSchedule()
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

const playerOptions = computed(() =>
  players.value.map((player) => ({
    title: teamLabel(player),
    value: player.id,
  })),
)

const scheduleWeeks = computed(() => schedule.value?.weeks ?? [])
const nextScheduleWeek = computed(() =>
  Math.max(1, ...scheduleWeeks.value.map((week) => week.week), 0),
)
const scheduleMatchupCount = computed(() =>
  scheduleWeeks.value.reduce((total, week) => total + week.matchups.length, 0),
)
const scoredMatchupCount = computed(() =>
  scheduleWeeks.value.reduce(
    (total, week) => total + week.matchups.filter((matchup) => hasScore(matchup)).length,
    0,
  ),
)

function teamLabel(player: { name: string; teamName?: string }) {
  return player.teamName?.trim() || player.name
}

function matchupTeamLabel(matchup: MatchupResponse, side: 1 | 2) {
  return side === 1
    ? matchup.player1TeamName?.trim() || matchup.player1Name
    : matchup.player2TeamName?.trim() || matchup.player2Name
}

function hasScore(matchup: MatchupResponse) {
  return matchup.player1Wins !== null || matchup.player2Wins !== null
}

function matchupScoreLabel(matchup: MatchupResponse) {
  if (matchup.player1Wins === null || matchup.player2Wins === null) return 'Unplayed'
  return `${matchup.player1Wins}-${matchup.player2Wins}`
}

function openAddMatchup(week = nextScheduleWeek.value) {
  editingMatchup.value = null
  scheduleForm.value = {
    week,
    player1Id: '',
    player2Id: '',
    forceScoredChange: false,
  }
  scheduleError.value = ''
  scheduleDialogOpen.value = true
}

function openEditMatchup(matchup: MatchupResponse) {
  editingMatchup.value = matchup
  scheduleForm.value = {
    week: matchup.week,
    player1Id: matchup.player1Id,
    player2Id: matchup.player2Id,
    forceScoredChange: false,
  }
  scheduleError.value = ''
  scheduleDialogOpen.value = true
}

function queueDeleteMatchup(matchup: MatchupResponse) {
  deletingMatchup.value = matchup
  scheduleForm.value.forceScoredChange = false
  scheduleError.value = ''
  scheduleDeleteDialogOpen.value = true
}

async function submitScheduleMatchup() {
  if (!leagueCode.value) return

  const editing = editingMatchup.value
  scheduleSaving.value = true
  scheduleError.value = ''

  try {
    const body = {
      adminPin: authStore.pin,
      week: scheduleForm.value.week,
      player1Id: scheduleForm.value.player1Id,
      player2Id: scheduleForm.value.player2Id,
      forceScoredChange: scheduleForm.value.forceScoredChange,
    }
    const url = editing
      ? `${API_BASE}/leagues/${leagueCode.value}/schedule/${editing.id}/matchup`
      : `${API_BASE}/leagues/${leagueCode.value}/schedule`
    const res = await fetch(url, {
      method: editing ? 'PATCH' : 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })

    if (!res.ok) {
      scheduleError.value = (await res.text()) || 'Unable to save schedule matchup.'
      return
    }

    scheduleDialogOpen.value = false
    editingMatchup.value = null
    await fetchSchedule()
    enqueueSnackbar('Schedule updated.', 'success')
  } catch {
    scheduleError.value = 'Could not connect to the server.'
  } finally {
    scheduleSaving.value = false
  }
}

async function confirmDeleteMatchup() {
  if (!leagueCode.value || !deletingMatchup.value) return

  scheduleSaving.value = true
  scheduleError.value = ''

  try {
    const res = await fetch(
      `${API_BASE}/leagues/${leagueCode.value}/schedule/${deletingMatchup.value.id}`,
      {
        method: 'DELETE',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          adminPin: authStore.pin,
          forceScoredChange: scheduleForm.value.forceScoredChange,
        }),
      },
    )

    if (!res.ok) {
      scheduleError.value = (await res.text()) || 'Unable to delete schedule matchup.'
      return
    }

    scheduleDeleteDialogOpen.value = false
    deletingMatchup.value = null
    await fetchSchedule()
    enqueueSnackbar('Matchup removed from the schedule.', 'success')
  } catch {
    scheduleError.value = 'Could not connect to the server.'
  } finally {
    scheduleSaving.value = false
  }
}
</script>

<template>
  <v-container fluid class="league-setup">
    <PageHeader
      class="setup-hero"
      eyebrow="Commissioner workspace"
      title="League Setup"
      subtitle="Configure the season, organize players, and launch the draft."
    >
      <template #actions>
        <v-chip :color="isConnected ? 'success' : undefined" variant="tonal">
          <v-icon start :icon="isConnected ? 'mdi-access-point' : 'mdi-access-point-off'" />
          {{ isConnected ? 'Live' : 'Disconnected' }}
        </v-chip>
      </template>
    </PageHeader>

    <v-row align="start">
      <v-col cols="12" lg="5">
        <v-card variant="outlined" class="workspace-card">
          <v-card-title>
            <v-icon icon="mdi-link-variant" start /> Invite players
            <v-chip class="ml-auto" color="primary" variant="tonal">{{ leagueCode }}</v-chip>
          </v-card-title>
          <v-card-text>
            <FormField label="Registration Link">
              <v-text-field
                :model-value="inviteLink"
                readonly
                hide-details
                append-inner-icon="mdi-content-copy"
                @click:append-inner="copyInviteLink"
              />
            </FormField>
            <div class="field-help">
              {{ linkCopied ? 'Copied to clipboard.' : 'Anyone with this link can register for the league.' }}
            </div>
          </v-card-text>
        </v-card>

        <v-card variant="outlined" class="workspace-card">
          <v-card-title><v-icon icon="mdi-tune-variant" start /> League rules</v-card-title>
          <v-card-text class="config-grid">
            <FormField label="League Name">
              <v-text-field v-model="leagueName" hide-details @change="saveConfig" />
            </FormField>
            <FormField label="Regulation Set">
              <v-select
                v-model="regulationSet"
                :items="REGULATIONS"
                item-title="label"
                item-value="id"
                hide-details
                @update:model-value="saveConfig"
              />
            </FormField>
            <FormField label="Team Point Limit">
              <v-number-input v-model="pointLimit" :min="1" control-variant="stacked" hide-details @change="saveConfig" />
            </FormField>
            <FormField label="Draft Rounds">
              <v-number-input v-model="rounds" :min="1" :max="20" control-variant="stacked" hide-details @change="saveConfig" />
            </FormField>
            <FormField label="Playoff Spots">
              <v-number-input v-model="playoffSpots" :min="2" :max="16" control-variant="stacked" hide-details @change="saveConfig" />
            </FormField>
          </v-card-text>
        </v-card>

        <v-card v-if="players.length >= 2" variant="outlined" class="workspace-card">
          <v-card-title><v-icon icon="mdi-swap-vertical-bold" start /> Snake preview</v-card-title>
          <v-card-text class="pick-schedule">
            <v-chip
              v-for="entry in snakePreview"
              :key="entry.pickNumber"
              size="small"
              variant="tonal"
              :color="entry.round % 2 === 0 ? 'primary' : undefined"
            >
              {{ entry.pickNumber + 1 }} · {{ entry.player?.name }}
            </v-chip>
          </v-card-text>
        </v-card>
      </v-col>

      <v-col cols="12" lg="7">
        <v-card variant="outlined" class="workspace-card">
          <v-card-title class="players-title">
            <span><v-icon icon="mdi-account-group-outline" start /> Draft order</span>
            <div class="draft-order-actions">
              <v-chip size="small" variant="tonal">{{ players.length }} players</v-chip>
              <v-btn
                size="small"
                variant="tonal"
                prepend-icon="mdi-shuffle-variant"
                :loading="isRandomizingOrder"
                :disabled="players.length < 2 || draftStatus !== 'setup'"
                @click="randomizeDraftOrder"
              >
                Randomize order
              </v-btn>
            </div>
          </v-card-title>
          <v-card-text>
            <div class="add-player">
              <FormField label="Player Name">
                <v-text-field v-model="newPlayerName" placeholder="Enter a name" hide-details @keydown.enter="addPlayer" />
              </FormField>
              <FormField label="PIN">
                <v-text-field v-model="newPlayerPin" placeholder="Choose a PIN" hide-details @keydown.enter="addPlayer" />
              </FormField>
              <v-btn color="primary" prepend-icon="mdi-account-plus" @click="addPlayer">Add</v-btn>
            </div>
            <v-alert v-if="addError" type="error" variant="tonal" density="compact" class="mt-3">
              {{ addError }}
            </v-alert>
          </v-card-text>

          <v-list lines="two" class="player-list">
            <v-list-item v-if="players.length === 0" title="No players yet" subtitle="Add at least two players to begin." />
            <v-list-item v-for="(player, index) in players" :key="player.id">
              <template #prepend>
                <v-avatar color="primary" variant="tonal" size="36">{{ index + 1 }}</v-avatar>
              </template>
              <v-list-item-title>{{ player.name }}</v-list-item-title>
              <v-list-item-subtitle>
                {{ player.isCommissioner ? 'Commissioner' : player.isCoCommissioner ? 'Co-commissioner' : 'Player' }}
              </v-list-item-subtitle>
              <template #append>
                <div class="player-actions">
                  <v-btn
                    icon="mdi-arrow-up"
                    size="small"
                    variant="text"
                    :disabled="index === 0"
                    @click="movePlayer(index, index - 1)"
                  />
                  <v-btn
                    icon="mdi-arrow-down"
                    size="small"
                    variant="text"
                    :disabled="index === players.length - 1"
                    @click="movePlayer(index, index + 1)"
                  />
                  <v-menu v-if="authStore.isCommissioner && !player.isCommissioner">
                    <template #activator="{ props }">
                      <v-btn v-bind="props" icon="mdi-dots-vertical" size="small" variant="text" />
                    </template>
                    <v-list>
                      <v-list-item
                        v-if="authStore.isCommissioner"
                        prepend-icon="mdi-shield-account-outline"
                        :title="player.isCoCommissioner ? 'Remove co-commissioner' : 'Make co-commissioner'"
                        :disabled="roleSavingPlayerId === player.id"
                        @click="toggleCoCommissioner(player)"
                      />
                      <v-list-item
                        prepend-icon="mdi-account-cancel-outline"
                        title="Drop player from league"
                        base-color="error"
                        @click="queueDropPlayer(player)"
                      />
                    </v-list>
                  </v-menu>
                </div>
              </template>
            </v-list-item>
          </v-list>
        </v-card>

        <v-card variant="outlined" class="workspace-card control-card">
          <v-card-title><v-icon icon="mdi-pokeball" start /> Draft controls</v-card-title>
          <v-card-text>
            <p>Point values and player order should be finalized before starting the draft.</p>
          </v-card-text>
          <v-card-actions>
            <v-btn
              color="primary"
              variant="flat"
              prepend-icon="mdi-play"
              :disabled="players.length < 2"
              @click="startDraft"
            >
              Start draft
            </v-btn>
            <v-btn
              variant="tonal"
              prepend-icon="mdi-format-list-numbered"
              @click="router.push('/league?tab=pokemon')"
            >
              Point values
            </v-btn>
            <v-spacer />
            <v-btn color="error" variant="text" prepend-icon="mdi-delete-alert-outline" @click="showResetWarning = true">
              Reset draft
            </v-btn>
          </v-card-actions>
        </v-card>

        <v-card variant="outlined" class="workspace-card schedule-editor-card">
          <v-card-title class="schedule-editor-title">
            <span><v-icon icon="mdi-calendar-edit" start /> Schedule editor</span>
            <div class="schedule-editor-actions">
              <v-chip size="small" variant="tonal">{{ scheduleMatchupCount }} matchups</v-chip>
              <v-chip v-if="scoredMatchupCount" size="small" color="warning" variant="tonal">
                {{ scoredMatchupCount }} scored
              </v-chip>
              <v-btn
                size="small"
                color="primary"
                variant="flat"
                prepend-icon="mdi-plus"
                :disabled="players.length < 2"
                @click="openAddMatchup()"
              >
                Add matchup
              </v-btn>
            </div>
          </v-card-title>
          <v-card-text>
            <v-alert v-if="scheduleError && !scheduleDialogOpen && !scheduleDeleteDialogOpen" type="error" variant="tonal" density="compact" class="mb-3">
              {{ scheduleError }}
            </v-alert>

            <DraftGateNotice
              v-if="!schedule || !scheduleWeeks.length"
              text="The schedule editor will appear once the schedule exists. You can also add matchups manually."
            />

            <v-expansion-panels v-else multiple class="schedule-editor-weeks">
              <v-expansion-panel v-for="week in scheduleWeeks" :key="week.week" :value="week.week">
                <v-expansion-panel-title>
                  <div class="week-editor-title">
                    <span>Week {{ week.week }}</span>
                    <div>
                      <v-chip size="x-small" variant="tonal">{{ week.matchups.length }} matchups</v-chip>
                      <v-btn
                        icon="mdi-plus"
                        size="x-small"
                        variant="text"
                        class="ml-1"
                        @click.stop="openAddMatchup(week.week)"
                      />
                    </div>
                  </div>
                </v-expansion-panel-title>
                <v-expansion-panel-text>
                  <v-list density="compact" class="schedule-editor-list">
                    <v-list-item v-for="matchup in week.matchups" :key="matchup.id">
                      <v-list-item-title>
                        {{ matchupTeamLabel(matchup, 1) }} vs {{ matchupTeamLabel(matchup, 2) }}
                      </v-list-item-title>
                      <v-list-item-subtitle>
                        <v-chip
                          size="x-small"
                          :color="hasScore(matchup) ? 'primary' : undefined"
                          variant="tonal"
                        >
                          {{ matchupScoreLabel(matchup) }}
                        </v-chip>
                      </v-list-item-subtitle>
                      <template #append>
                        <div class="schedule-row-actions">
                          <v-btn
                            icon="mdi-pencil"
                            size="small"
                            variant="text"
                            @click="openEditMatchup(matchup)"
                          />
                          <v-btn
                            icon="mdi-delete-outline"
                            size="small"
                            variant="text"
                            color="error"
                            @click="queueDeleteMatchup(matchup)"
                          />
                        </div>
                      </template>
                    </v-list-item>
                  </v-list>
                </v-expansion-panel-text>
              </v-expansion-panel>
            </v-expansion-panels>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <v-dialog v-model="showResetWarning" max-width="460">
      <v-card>
        <v-card-title>Reset the draft?</v-card-title>
        <v-card-text>This permanently removes every draft pick. This action cannot be undone.</v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="showResetWarning = false">Cancel</v-btn>
          <v-btn color="error" variant="flat" @click="resetDraft">Reset draft</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-dialog
      :model-value="playerPendingDrop !== null"
      max-width="520"
      @update:model-value="!$event && (playerPendingDrop = null)"
    >
      <v-card>
        <v-card-title>Drop {{ playerPendingDrop?.name }}?</v-card-title>
        <v-card-text>
          This removes the player from the league, deletes their schedule matchups, and releases every Pokemon on their roster.
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" :disabled="isDroppingPlayer" @click="playerPendingDrop = null">Cancel</v-btn>
          <v-btn color="error" variant="flat" :loading="isDroppingPlayer" @click="confirmDropPlayer">
            Drop player
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-dialog v-model="scheduleDialogOpen" max-width="560">
      <v-card>
        <v-card-title>{{ editingMatchup ? 'Edit matchup' : 'Add matchup' }}</v-card-title>
        <v-card-text class="schedule-dialog-body">
          <FormField label="Week">
            <v-number-input v-model="scheduleForm.week" :min="1" control-variant="stacked" hide-details />
          </FormField>
          <FormField label="Player 1">
            <v-select
              v-model="scheduleForm.player1Id"
              :items="playerOptions"
              hide-details
            />
          </FormField>
          <FormField label="Player 2">
            <v-select
              v-model="scheduleForm.player2Id"
              :items="playerOptions"
              hide-details
            />
          </FormField>

          <v-alert v-if="editingMatchup && hasScore(editingMatchup)" type="warning" variant="tonal" density="compact">
            This matchup already has a score. Changing the teams or week can alter standings and playoff results.
          </v-alert>
          <v-checkbox
            v-if="editingMatchup && hasScore(editingMatchup)"
            v-model="scheduleForm.forceScoredChange"
            density="compact"
            hide-details
            label="I understand this changes a scored matchup"
          />
          <v-alert v-if="scheduleError && scheduleDialogOpen" type="error" variant="tonal" density="compact">
            {{ scheduleError }}
          </v-alert>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" :disabled="scheduleSaving" @click="scheduleDialogOpen = false">Cancel</v-btn>
          <v-btn color="primary" variant="flat" :loading="scheduleSaving" @click="submitScheduleMatchup">
            Save matchup
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-dialog v-model="scheduleDeleteDialogOpen" max-width="520">
      <v-card>
        <v-card-title>Remove matchup?</v-card-title>
        <v-card-text>
          <p v-if="deletingMatchup">
            Week {{ deletingMatchup.week }}: {{ matchupTeamLabel(deletingMatchup, 1) }} vs {{ matchupTeamLabel(deletingMatchup, 2) }}
          </p>
          <v-alert v-if="deletingMatchup && hasScore(deletingMatchup)" type="warning" variant="tonal" density="compact" class="mt-3">
            This matchup already has a score. Removing it will remove that result from standings.
          </v-alert>
          <v-checkbox
            v-if="deletingMatchup && hasScore(deletingMatchup)"
            v-model="scheduleForm.forceScoredChange"
            density="compact"
            hide-details
            label="I understand this removes a scored matchup"
          />
          <v-alert v-if="scheduleError && scheduleDeleteDialogOpen" type="error" variant="tonal" density="compact" class="mt-3">
            {{ scheduleError }}
          </v-alert>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" :disabled="scheduleSaving" @click="scheduleDeleteDialogOpen = false">Cancel</v-btn>
          <v-btn color="error" variant="flat" :loading="scheduleSaving" @click="confirmDeleteMatchup">
            Remove matchup
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-container>
</template>

<style scoped>
.league-setup {
  padding: clamp(1rem, 2vw, 2rem);
}
.setup-hero {
  margin-bottom: 16px;
}
.hero-content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}
.hero-content h1 {
  margin-top: 3px;
  font-size: clamp(1.5rem, 3vw, 2.1rem);
  font-weight: 800;
}
.hero-content p,
.field-help,
.control-card p {
  color: var(--text-muted);
  font-size: 0.82rem;
}
.workspace-card {
  margin-bottom: 16px;
  overflow: hidden;
}
.workspace-card .v-card-title,
.players-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 1rem;
  font-weight: 800;
}
.players-title {
  justify-content: space-between;
}
.draft-order-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  justify-content: flex-end;
}
.config-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 4px 12px;
}
.config-grid > :first-child,
.config-grid > :nth-child(2) {
  grid-column: span 2;
}
.field-help {
  margin-top: 8px;
}
.add-player {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 130px auto;
  gap: 10px;
  align-items: center;
}
.player-list {
  background: transparent;
  border-top: 1px solid var(--border-color);
}
.player-list :deep(.v-list-item) {
  border-bottom: 1px solid var(--border-color);
}
.player-actions {
  display: flex;
  align-items: center;
}
.pick-schedule {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.control-card .v-card-actions {
  display: flex;
  flex-wrap: wrap;
}
.schedule-editor-title {
  justify-content: space-between;
}
.schedule-editor-actions,
.week-editor-title,
.schedule-row-actions {
  align-items: center;
  display: flex;
  gap: 8px;
}
.schedule-editor-actions {
  flex-wrap: wrap;
  justify-content: flex-end;
}
.week-editor-title {
  justify-content: space-between;
  width: 100%;
}
.schedule-editor-weeks {
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  overflow: hidden;
}
.schedule-editor-list {
  background: transparent;
}
.schedule-editor-list :deep(.v-list-item) {
  border-bottom: 1px solid var(--border-color);
}
.schedule-dialog-body {
  display: grid;
  gap: 12px;
}
@media (max-width: 720px) {
  .league-setup {
    padding: 12px;
  }
  .config-grid {
    grid-template-columns: 1fr;
  }
  .config-grid > :first-child,
  .config-grid > :nth-child(2) {
    grid-column: auto;
  }
  .add-player {
    grid-template-columns: 1fr;
  }
  .players-title {
    align-items: flex-start;
    flex-direction: column;
  }
  .draft-order-actions {
    justify-content: flex-start;
    width: 100%;
  }
  .schedule-editor-title {
    align-items: flex-start;
    flex-direction: column;
  }
  .schedule-editor-actions {
    justify-content: flex-start;
    width: 100%;
  }
  .player-actions > .v-btn:not(:last-child) {
    display: none;
  }
  .control-card .v-card-actions {
    align-items: stretch;
    flex-direction: column;
  }
  .control-card .v-spacer {
    display: none;
  }
}
</style>
