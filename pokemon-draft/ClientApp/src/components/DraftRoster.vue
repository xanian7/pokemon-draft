<script setup lang="ts">
import { ref, computed } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useDraftStore } from '@/stores/draft'
import { usePokemonStore } from '@/stores/pokemon'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import { formatPokemonName } from '@/utils/format'
import type { Pokemon } from '@/types'

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
  return draftStore.getPlayerPicks(playerId)
    .map((p: any) => pokemonStore.getPokemonById(p.pokemonId))
    .filter((p): p is Pokemon => Boolean(p))
}

function getPlayerPoints(playerId: string): number {
  return draftStore.getPlayerPicks(playerId)
    .reduce((sum: number, p: any) => sum + pokemonStore.getPointValue(p.pokemonId), 0)
}

function getInitials(name: string): string {
  return name.split(' ').map((w) => w[0]).join('').slice(0, 2).toUpperCase()
}

// ── My team ───────────────────────────────────────────────────────────────────
const myPlayer = computed(() =>
  draftStore.players.find((p: any) => p.id === authStore.playerId) ?? null,
)
const myPokemon = computed(() =>
  authStore.playerId ? getPlayerPokemon(authStore.playerId) : [],
)
const myPoints = computed(() =>
  authStore.playerId ? getPlayerPoints(authStore.playerId) : 0,
)

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
  <aside class="draft-roster">

    <!-- ── My Team ── -->
    <div class="roster-section roster-section--me">
      <div class="roster-header">
        <div class="roster-avatar">
          <img v-if="myPlayer?.teamImageUrl" :src="myPlayer.teamImageUrl" :alt="myPlayer.name" />
          <span v-else>{{ myPlayer ? getInitials(myPlayer.teamName || myPlayer.name) : '?' }}</span>
        </div>
        <div class="roster-header__info">
          <span class="roster-name">My Team ({{ myPlayer?.teamName || myPlayer?.name || '' }})</span>
          <span class="roster-points">{{ myPoints }} pts</span>
        </div>
      </div>

      <div class="roster-picks">
        <div
          v-if="myPokemon.length === 0"
          class="roster-empty"
        >No picks yet</div>
        <button
          v-for="pokemon in myPokemon"
          :key="pokemon.id"
          class="roster-row"
          @click="openDetail(pokemon)"
        >
          <img class="roster-sprite" :src="pokemon.spriteUrl" :alt="pokemon.name" />
          <span class="roster-poke-name">{{ formatPokemonName(pokemon.name) }}</span>
          <span class="roster-poke-pts">{{ pokemonStore.getPointValue(pokemon.id) }}pt</span>
        </button>
      </div>
    </div>

    <div class="roster-divider" />

    <!-- ── Other Teams ── -->
    <div class="roster-others">
      <div
        v-for="player in otherPlayers"
        :key="player.id"
        class="roster-team"
      >
        <!-- Dropdown toggle -->
        <button class="roster-team-toggle" @click="toggleTeam(player.id)">
          <div class="roster-avatar roster-avatar--sm">
            <img v-if="player.teamImageUrl" :src="player.teamImageUrl" :alt="player.name" />
            <span v-else>{{ getInitials(player.teamName || player.name) }}</span>
          </div>
          <div class="roster-header__info">
            <span class="roster-name">{{ player.teamName || player.name }}</span>
            <span class="roster-points">{{ getPlayerPoints(player.id) }} pts</span>
          </div>
          <span class="toggle-chevron" :class="{ 'toggle-chevron--open': openTeams.has(player.id) }">▾</span>
        </button>

        <!-- Picks list -->
        <div v-if="openTeams.has(player.id)" class="roster-picks">
          <div
            v-if="getPlayerPokemon(player.id).length === 0"
            class="roster-empty"
          >No picks yet</div>
          <button
            v-for="pokemon in getPlayerPokemon(player.id)"
            :key="pokemon.id"
            class="roster-row"
            @click="openDetail(pokemon)"
          >
            <img class="roster-sprite" :src="pokemon.spriteUrl" :alt="pokemon.name" />
            <span class="roster-poke-name">{{ formatPokemonName(pokemon.name) }}</span>
            <span class="roster-poke-pts">{{ pokemonStore.getPointValue(pokemon.id) }}pt</span>
          </button>
        </div>
      </div>
    </div>

  </aside>

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
.draft-roster {
  width: 220px;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  border-right: 1px solid var(--border-color);
  background: var(--card-bg);
  overflow-y: auto;
  overflow-x: hidden;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.draft-roster::-webkit-scrollbar { width: 4px; }
.draft-roster::-webkit-scrollbar-thumb { background: var(--border-color); border-radius: 2px; }

/* ── Section ──────────────────────────────────────────────────────────────── */
.roster-section--me {
  padding: 0.75rem 0.75rem 0.5rem;
}

.roster-divider {
  height: 1px;
  background: var(--border-color);
  margin: 0;
  flex-shrink: 0;
}

.roster-others {
  display: flex;
  flex-direction: column;
  flex: 1;
}

/* ── Header row ───────────────────────────────────────────────────────────── */
.roster-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}

.roster-avatar {
  width: 34px;
  height: 34px;
  border-radius: 50%;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.65rem;
  font-weight: 700;
  color: var(--text-muted);
  flex-shrink: 0;
  overflow: hidden;
}

.roster-avatar--sm {
  width: 26px;
  height: 26px;
  font-size: 0.55rem;
}

.roster-avatar img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.roster-header__info {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.roster-name {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.roster-points {
  font-size: 0.68rem;
  color: var(--secondary);
  font-weight: 600;
}

/* ── Team toggle (other teams) ────────────────────────────────────────────── */
.roster-team-toggle {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 0.55rem 0.75rem;
  background: transparent;
  border: none;
  border-bottom: 1px solid var(--border-color);
  cursor: pointer;
  text-align: left;
  transition: background 0.12s;
}

.roster-team-toggle:hover { background: var(--input-bg); }

.toggle-chevron {
  margin-left: auto;
  font-size: 0.75rem;
  color: var(--text-muted);
  transition: transform 0.2s;
  flex-shrink: 0;
}

.toggle-chevron--open { transform: rotate(180deg); }

/* ── Picks list ───────────────────────────────────────────────────────────── */
.roster-picks {
  display: flex;
  flex-direction: column;
}

.roster-row {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  padding: 3px 0.75rem;
  background: transparent;
  border: none;
  cursor: pointer;
  text-align: left;
  transition: background 0.1s;
  border-bottom: 1px solid rgba(255,255,255,0.03);
}

.roster-row:hover { background: var(--input-bg); }

.roster-sprite {
  width: 40px;
  height: 40px;
  flex-shrink: 0;
  image-rendering: pixelated;
}

.roster-poke-name {
  flex: 1;
  font-size: 0.75rem;
  color: var(--text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.roster-poke-pts {
  font-size: 0.68rem;
  color: var(--secondary);
  font-weight: 600;
  flex-shrink: 0;
}

.roster-empty {
  padding: 0.5rem 0.75rem;
  font-size: 0.75rem;
  color: var(--text-muted);
  font-style: italic;
}
</style>
