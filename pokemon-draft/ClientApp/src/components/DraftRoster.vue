<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useDraftStore } from '@/stores/draft'
import { usePokemonStore } from '@/stores/pokemon'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import type { Pokemon, ServerPlayerResponse } from '@/types'
import PokemonCard from '@/components/PokemonCard.vue'

const authStore = useAuthStore()
const draftStore = useDraftStore()
const pokemonStore = usePokemonStore()

const detailPokemon = ref<Pokemon | null>(null)

function openDetail(pokemon: Pokemon) {
  detailPokemon.value = pokemon
}

function closeDetail() {
  detailPokemon.value = null
}

function getPlayerPokemon(playerId: string): Pokemon[] {
  return draftStore
    .getPlayerPicks(playerId)
    .map((p: any) => pokemonStore.getPokemonById(p.pokemonId))
    .filter((p): p is Pokemon => Boolean(p))
}

function getPlayerPoints(playerId: string): number {
  return draftStore
    .getPlayerPicks(playerId)
    .reduce((sum: number, p: any) => sum + pokemonStore.getPointValue(p.pokemonId), 0)
}

function getInitials(name: string): string {
  return name
    .split(' ')
    .map((w) => w[0])
    .join('')
    .slice(0, 2)
    .toUpperCase()
}

function getDisplayName(player: ServerPlayerResponse): string {
  return player.teamName || player.name
}

const activePlayerId = ref<string | null>(authStore.playerId ?? null)

const playerRosters = computed(() => {
  const myId = authStore.playerId
  const players = [...draftStore.players].sort((a, b) => {
    if (a.id === myId) return -1
    if (b.id === myId) return 1
    return 0
  })

  return players.map((player) => {
    const pokemon = getPlayerPokemon(player.id)
    const displayName = getDisplayName(player)

    return {
      player,
      pokemon,
      displayName,
      initials: getInitials(displayName),
      isMe: player.id === myId,
      points: getPlayerPoints(player.id),
    }
  })
})

watch(
  playerRosters,
  (rosters) => {
    const activeStillExists = rosters.some((roster) => roster.player.id === activePlayerId.value)
    if (activeStillExists) return
    activePlayerId.value =
      rosters.find((roster) => roster.player.id === authStore.playerId)?.player.id ??
      rosters[0]?.player.id ??
      null
  },
  { immediate: true },
)
</script>

<template>
  <v-container fluid class="pa-0 pb-3 roster-shell">
    <v-card class="team-outline">
      <template v-if="playerRosters.length > 0">
        <v-tabs
          v-model="activePlayerId"
          class="roster-tabs"
          density="compact"
          show-arrows
          grow
        >
          <v-tab
            v-for="roster in playerRosters"
            :key="roster.player.id"
            :value="roster.player.id"
            class="roster-tab"
          >
            <v-avatar size="24" class="roster-tab-avatar" v-if="roster.player.teamImageUrl">
              <v-img :src="roster.player.teamImageUrl" />
            </v-avatar>
            <v-avatar size="24" class="roster-tab-avatar" v-else>
              <span>{{ roster.initials }}</span>
            </v-avatar>
            <span class="roster-tab-label">{{ roster.isMe ? 'My Team' : roster.displayName }}</span>
          </v-tab>
        </v-tabs>

        <v-divider />

        <v-window v-model="activePlayerId" class="roster-window">
          <v-window-item
            v-for="roster in playerRosters"
            :key="roster.player.id"
            :value="roster.player.id"
          >
            <div class="roster-header">
              <div class="text-subtitle-1 points">{{ roster.points }} pts</div>
            </div>

            <v-card-text class="roster-content">
              <v-row class="pokemon-grid">
                <div v-for="p in roster.pokemon" :key="p.id">
                  <PokemonCard
                    :pokemon="p"
                    :pointValue="pokemonStore.getPointValue(p.id)"
                    :canDraft="false"
                    :isPicked="false"
                    :show-sprite="true"
                    @click="openDetail(p)"
                  />
                </div>
              </v-row>
              <div v-if="roster.pokemon.length === 0" class="text-center empty-roster">
                No picks yet
              </div>
            </v-card-text>
          </v-window-item>
        </v-window>
      </template>

      <div v-else class="text-center empty-roster">No players yet</div>
    </v-card>
  </v-container>

  <PokemonDetailModal
    v-if="detailPokemon"
    :pokemon="detailPokemon"
    :pointValue="pokemonStore.getPointValue(detailPokemon.id)"
    :canDraft="false"
    :isPicked="true"
    :showDraftAction="false"
    @close="closeDetail"
    @draft="() => {}"
  />
</template>

<style scoped>
.pokemon-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  gap: 0.4rem;
  align-content: start;
}


.v-divider {
  flex: 0 0 auto;
}

.team-outline {
  border: 1px solid var(--border-color);
  display: flex;
  flex: 1 1 0;
  flex-direction: column;
  width: 100%;
  max-height: 100%;
  padding: 8px;
}

.roster-tabs {
  flex: 0 0 auto;
}

.roster-tab {
  min-width: 92px;
  max-width: 150px;
}

.roster-tab-avatar {
  flex: 0 0 auto;
  margin-right: 6px;
}

.roster-tab-avatar span {
  font-size: 0.68rem;
  font-weight: 700;
}

.roster-tab-label {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.roster-window {
  flex: 1 1 auto;
  min-height: 0;
}

.roster-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 4px 6px;
}

.roster-subtitle {
  color: var(--text-muted);
}

.roster-content {
  padding: 6px 0 0;
}

.empty-roster {
  padding: 20px 8px;
}

@media (max-width: 720px) {
  .roster-shell {
    display: block;
    height: auto;
    max-height: none;
    min-height: 0;
    overflow: visible;
  }

  .team-outline {
    display: block;
    height: auto;
    max-height: none;
    min-height: 220px;
    overflow: visible;
    padding: 6px;
  }

  .roster-window {
    min-height: 160px;
  }

  .pokemon-grid {
    grid-template-columns: repeat(auto-fill, minmax(96px, 1fr));
  }
}
</style>
