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
const { subscribe, unsubscribe } = useSignalR()

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
  await subscribe(authStore.leagueCode, applyState)
})

onUnmounted(() => unsubscribe(applyState))

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
  const picks = []

  for (let i = 0; i < remaining; i++) {
    const pickNumber = draftStore.currentPickNumber + i
    const round = Math.floor(pickNumber / n)
    const posInRound = pickNumber % n
    const idx = round % 2 === 0 ? posInRound : n - 1 - posInRound
    const player = players[idx]
    if (!player || draftStore.isPlayerAtPointLimit(player.id)) continue

    const displayName = player?.teamName || player?.name || '?'
    picks.push({
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
      isCurrent: picks.length === 0,
      pickNumber: pickNumber + 1,
      round: round + 1,
    })
  }

  return picks
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
  <v-container fluid>
    <v-card class="wrapper-card">
      <v-row class="upcoming-picks-row">
        <v-col cols="12" md="12">
          <div v-if="picksByRound.length > 0" class="upcoming-picks">
            <div class="upcoming-picks-track">
              <section v-for="group in picksByRound" :key="group.round" class="upcoming-round">
                <v-card class="round-separator">
                  <v-card-text>
                    <h3 class="text-subtitle-1">ROUND</h3>
                    <h3 class="text-subtitle-1">{{ group.round }}</h3>
                  </v-card-text>
                </v-card>
                <v-card
                  v-for="pick in group.picks"
                  :key="pick.pickNumber"
                  :class="['upcoming-pick-card', { 'current-upcoming-pick-card': pick.isCurrent }]"
                  :elevation="pick.isCurrent ? 8 : 2"
                  :color="pick.isMe ? 'primary' : 'var(--draft-card-nonuser-bg)'"
                >
                  <v-card-text class="d-flex align-center">
                    <v-avatar size="40" class="mr-3" v-if="pick.teamImageUrl">
                      <v-img :src="pick.teamImageUrl" />
                    </v-avatar>
                    <v-avatar size="40" class="mr-3" v-else>
                      <span class="text-subtitle-1">{{ pick.initials }}</span>
                    </v-avatar>
                    <div>
                      <div class="text-subtitle-2">
                        {{
                          pick.isCurrent
                            ? `ON THE CLOCK: PICK ${pick.pickNumber}`
                            : `PICK ${pick.pickNumber}`
                        }}
                      </div>
                      <div class="text-body-2">{{ pick.playerName }}</div>
                    </div>
                  </v-card-text>
                </v-card>
              </section>
            </div>
          </div>
        </v-col>
      </v-row>
      <v-row class="draft-main-row">
        <v-col cols="12" md="2" class="draft-grid-col">
          <DraftRoster />
        </v-col>
        <v-col cols="12" md="10" class="draft-grid-col">
          <PokemonGrid />
        </v-col>
      </v-row>
    </v-card>
  </v-container>
</template>

<style scoped>
.upcoming-picks {
  border: 1px solid var(--border-color);
  border-radius: 6px;
  padding: 4px 8px 4px 8px;
  overflow-x: auto;
  background: var(--bg);
}

.upcoming-round {
  display: flex;
  flex: 0 0 auto;
  align-items: stretch;
  gap: 4px;
}

.round-separator {
  display: flex;
  align-items: center;
  white-space: nowrap;
  font-weight: 600;
  text-align: center;
  padding: 0;
}

.upcoming-picks-track {
  display: flex;
  flex-wrap: nowrap;
  gap: 4px;
  align-items: stretch;
  min-width: max-content;
}

.upcoming-pick-card {
  flex: 0 0 200px;
}

.current-upcoming-pick-card {
  flex-basis: 300px;
}

.v-avatar {
  margin-right: 12px;
}

.draft-status {
  margin-bottom: 16px;
  min-width: 280px;
  text-align: center;
  font-weight: 600;
  height: 100%;
}

.wrapper-card {
  max-height: 89dvh;
  height: 89dvh;
  display: flex;
  flex-direction: column;
  padding: 8px;
}

.upcoming-picks-row {
  flex: 0 0 auto;
  margin-bottom: 4px;
}

.upcoming-picks-row > .v-col {
  padding-bottom: 4px;
}

.draft-main-row {
  flex: 1 1 auto;
  min-height: 0;
  margin-top: 0;
}

.draft-grid-col {
  min-height: 0;
  display: flex;
}
</style>
