<script setup lang="ts">
import { ref, computed } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useDraftStore } from '@/stores/draft'
import { usePokemonStore } from '@/stores/pokemon'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import { formatPokemonName } from '@/utils/format'
import type { Pokemon } from '@/types'
import PokemonCard from '@/components/PokemonCard.vue'

const authStore = useAuthStore()
const draftStore = useDraftStore()
const pokemonStore = usePokemonStore()

// ── Detail modal ──────────────────────────────────────────────────────────────
const detailPokemon = ref<Pokemon | null>(null)

function openDetail(pokemon: Pokemon) {
  detailPokemon.value = pokemon
}

function closeDetail() {
  detailPokemon.value = null
}

// ── Helpers ───────────────────────────────────────────────────────────────────
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

// ── My team ───────────────────────────────────────────────────────────────────
const myPlayer = computed(
  () => draftStore.players.find((p: any) => p.id === authStore.playerId) ?? null,
)
const myPokemon = computed(() => (authStore.playerId ? getPlayerPokemon(authStore.playerId) : []))
const myPoints = computed(() => (authStore.playerId ? getPlayerPoints(authStore.playerId) : 0))

// ── Other teams ───────────────────────────────────────────────────────────────
const otherPlayers = computed(() =>
  draftStore.players.filter((p: any) => p.id !== authStore.playerId),
)

const openTeams = ref<Set<string>>(new Set())

function toggleTeam(playerId: string) {
  const s = new Set(openTeams.value)
  if (s.has(playerId)) s.delete(playerId)
  else s.add(playerId)
  openTeams.value = s
}
</script>

<template>
  <v-container fluid class="remove-left-right-padding">
    <v-card class="team-outline">
      <v-card color="var(--primary)">
        <v-card-title class="text-subtitle-1">
          <v-row>
            <v-col> My Team: {{ myPlayer?.teamName || myPlayer?.name || 'My Team' }} </v-col>
            <v-spacer />
            <v-col class="text-subtitle-1"> {{ myPoints }} pts </v-col>
          </v-row>
        </v-card-title>
        <v-card-text>
          <v-row class="pokemon-grid">
            <div v-for="p in myPokemon" :key="p.id" cols="6" md="4" lg="3" >
              <PokemonCard
                :pokemon="p"
                :pointValue="pokemonStore.getPointValue(p.id)"
                :canDraft="false"
                :isPicked="false"
                @click="openDetail(p)"
              />
            </div>
          </v-row>
          <div v-if="myPokemon.length === 0" class="text-center">No picks yet</div>
        </v-card-text>
      </v-card>
      <v-divider />
      <v-expansion-panels v-model="openTeams" multiple>
        <v-expansion-panel v-for="player in otherPlayers" :key="player.id">
          <v-expansion-panel-title class="text-subtitle-1">
            {{ player.teamName || player.name }}
            <span class="text-subtitle-1 points">{{ getPlayerPoints(player.id) }} pts</span>
          </v-expansion-panel-title>
          <v-expansion-panel-text>
            <v-row class="pokemon-grid">
              <div
                v-for="p in getPlayerPokemon(player.id)"
                :key="p.id"
                cols="6"
                md="4"
                lg="3"
              >
                <PokemonCard
                  :pokemon="p"
                  :pointValue="pokemonStore.getPointValue(p.id)"
                  :canDraft="false"
                  :isPicked="false"
                  @click="openDetail(p)"
                />
              </div>
            </v-row>
            <div v-if="getPlayerPokemon(player.id).length === 0" class="text-center">
              No picks yet
            </div>
          </v-expansion-panel-text>
        </v-expansion-panel>
      </v-expansion-panels>
    </v-card>
  </v-container>

  <!-- Detail modal (view-only, no draft action) -->
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
  overflow-y: auto;
  flex: 1;
}

.v-expansion-panels {
  padding-top: 12px;
}
.points {
  margin-left: auto;
  padding-right: 12px;
}
.v-divider {
  margin-top: 12px;
}
.team-outline {
  padding: 8px;
  border: 1px solid var(--border-color);
  max-height: 81vh;
  overflow-y: auto;
}
</style>
