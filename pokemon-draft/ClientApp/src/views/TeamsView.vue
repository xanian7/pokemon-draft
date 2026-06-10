<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import AppIcon from '@/components/AppIcon.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import { API_BASE } from '@/services/signalr'
import { mdiAccountGroup, mdiChevronDown, mdiChevronUp } from '@mdi/js'

const router = useRouter()
const authStore = useAuthStore()
const pokemonStore = usePokemonStore()

if (!authStore.isAuthenticated) router.replace('/join')

const league = ref<any>(null)
const standings = ref<any[]>([])
const isLoading = ref(true)
const error = ref('')
const expandedTeams = ref<Set<string>>(new Set())
const selectedPokemonId = ref<number | null>(null)
const selectedPokemon = computed(() =>
  selectedPokemonId.value !== null
    ? (pokemonStore.getPokemonById(selectedPokemonId.value) ?? null)
    : null,
)

onMounted(async () => {
  await pokemonStore.fetchAllPokemon()
  try {
    const [leagueRes, schedRes] = await Promise.all([
      fetch(`${API_BASE}/leagues/${authStore.leagueCode}`),
      fetch(`${API_BASE}/leagues/${authStore.leagueCode}/schedule`),
    ])
    if (!leagueRes.ok) {
      error.value = 'Could not load teams.'
      return
    }
    league.value = await leagueRes.json()
    if (schedRes.ok) {
      const sched = await schedRes.json()
      standings.value = sched.standings ?? []
    }
  } catch {
    error.value = 'Could not connect to server.'
  } finally {
    isLoading.value = false
  }
})

function toggleTeam(playerId: string) {
  const next = new Set(expandedTeams.value)
  if (next.has(playerId)) next.delete(playerId)
  else next.add(playerId)
  expandedTeams.value = next
}

const teams = computed(() => {
  if (!league.value) return []
  const picks: any[] = league.value.draft?.picks ?? []
  const pointValues: Record<number, number> = league.value.pointValues ?? {}

  return league.value.players
    .map((player: any) => {
      const myPicks = picks
        .filter((p: any) => p.playerId === player.id)
        .sort((a: any, b: any) => a.pickNumber - b.pickNumber)
        .map((p: any) => {
          const poke = pokemonStore.getPokemonById(p.pokemonId)
          const pts = pointValues[p.pokemonId] ?? 0
          return poke ? { ...poke, points: pts } : null
        })
        .filter(Boolean)

      const totalPoints = myPicks.reduce((sum: number, p: any) => sum + p.points, 0)
      const standing = standings.value.find((s: any) => s.playerId === player.id)

      return {
        ...player,
        picks: myPicks,
        totalPoints,
        wins: standing?.wins ?? 0,
        losses: standing?.losses ?? 0,
        matchPoints: standing?.matchPoints ?? 0,
        rank: standings.value.findIndex((s: any) => s.playerId === player.id) + 1,
      }
    })
    .sort((a: any, b: any) => {
      if (standings.value.length === 0) return 0
      return a.rank - b.rank
    })
})

function typeColor(type: string): string {
  const colors: Record<string, string> = {
    normal: '#a8a878',
    fire: '#f08030',
    water: '#6890f0',
    electric: '#f8d030',
    grass: '#78c850',
    ice: '#98d8d8',
    fighting: '#c03028',
    poison: '#a040a0',
    ground: '#e0c068',
    flying: '#a890f0',
    psychic: '#f85888',
    bug: '#a8b820',
    rock: '#b8a038',
    ghost: '#705898',
    dragon: '#7038f8',
    dark: '#705848',
    steel: '#b8b8d0',
    fairy: '#ee99ac',
  }
  return colors[type.toLowerCase()] ?? '#888'
}
</script>

<template>
  <main class="teams-page">
    <div class="page-header">
      <AppIcon :path="mdiAccountGroup" :size="26" class="header-icon" />
      <div>
        <h1>All Teams</h1>
        <p class="subtitle">{{ league?.name }}</p>
      </div>
    </div>

    <div v-if="isLoading" class="loading">
      <PokeballLoader variant="page" label="Loading teams…" />
    </div>
    <div v-else-if="error" class="error-msg">{{ error }}</div>
    <div v-else-if="!league?.draft?.picks?.length" class="empty-msg">
      Teams will appear here once the draft is complete.
    </div>

    <div v-else class="team-list">
      <div
        v-for="team in teams"
        :key="team.id"
        class="team-card"
        :class="{ 'my-team': team.id === authStore.playerId }"
      >
        <!-- Team header row -->
        <button class="team-header" @click="toggleTeam(team.id)">
          <div class="rank-badge">
            <span class="rank-num">#{{ team.rank || '—' }}</span>
          </div>
          <div class="team-avatar">
            <img
              v-if="team.teamImageUrl"
              :src="team.teamImageUrl"
              :alt="team.teamName"
              class="avatar-img"
            />
            <div v-else class="avatar-initials">
              {{ (team.teamName || team.name).slice(0, 2).toUpperCase() }}
            </div>
          </div>
          <div class="team-info">
            <span class="team-name">{{ team.teamName || team.name }}</span>
            <span class="player-name">{{ team.teamName ? team.name : '' }}</span>
          </div>
          <div class="team-record">
            <span class="record">{{ team.wins }}–{{ team.losses }}</span>
            <span class="mp">{{ team.matchPoints }} pts</span>
          </div>
          <div class="team-pts">
            <span class="pts-label">Draft pts</span>
            <span class="pts-val">{{ team.totalPoints }}</span>
          </div>
          <div class="you-badge" v-if="team.id === authStore.playerId">You</div>
          <AppIcon
            :path="expandedTeams.has(team.id) ? mdiChevronUp : mdiChevronDown"
            :size="20"
            class="chevron"
          />
        </button>

        <!-- Expanded roster -->
        <div v-if="expandedTeams.has(team.id)" class="team-roster">
          <div v-if="team.picks.length === 0" class="no-picks">No picks yet.</div>
          <div v-else class="picks-grid">
            <button
              v-for="poke in team.picks"
              :key="poke.id"
              class="pick-tile"
              @click="selectedPokemonId = poke.id"
            >
              <img :src="poke.spriteUrl" :alt="poke.name" class="pick-sprite" />
              <div class="pick-name">{{ poke.name }}</div>
              <div class="pick-types">
                <span
                  v-for="t in poke.types"
                  :key="t"
                  class="type-badge"
                  :style="{ background: typeColor(t) }"
                  >{{ t }}</span
                >
              </div>
              <div class="pick-pts">{{ poke.points }} pts</div>
            </button>
          </div>
        </div>
      </div>
    </div>

    <PokemonDetailModal
      v-if="selectedPokemon !== null"
      :key="selectedPokemon.id"
      :pokemon="selectedPokemon"
      :can-draft="false"
      :is-picked="true"
      :show-draft-action="false"
      :point-value="league?.pointValues?.[selectedPokemon.id] ?? 0"
      @close="selectedPokemonId = null"
    />
  </main>
</template>

<style scoped>
.teams-page {
  width: 100%;
  max-width: none;
  margin: 0;
  padding: 2rem clamp(1rem, 2vw, 2rem);
}

.page-header {
  display: flex;
  align-items: center;
  gap: 0.85rem;
  margin-bottom: 1.75rem;
}

.header-icon {
  color: var(--primary);
  flex-shrink: 0;
}

h1 {
  font-size: 1.5rem;
  font-weight: 800;
}
.subtitle {
  font-size: 0.875rem;
  color: var(--text-muted);
}

.loading {
  display: flex;
  justify-content: center;
  padding: 3rem 0;
}
.error-msg {
  color: var(--secondary);
  padding: 1rem 0;
}
.empty-msg {
  text-align: center;
  color: var(--text-muted);
  padding: 3rem 0;
}

.team-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.team-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  overflow: hidden;
  transition: border-color 0.15s;
}

.team-card.my-team {
  border-color: var(--primary);
}
.team-card:hover {
  border-color: color-mix(in srgb, var(--primary) 60%, transparent);
}

.team-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  width: 100%;
  padding: 0.85rem 1rem;
  background: transparent;
  border: none;
  color: var(--text);
  cursor: pointer;
  text-align: left;
  transition: background 0.12s;
}

.team-header:hover {
  background: var(--input-bg);
}

.rank-badge {
  width: 36px;
  flex-shrink: 0;
  text-align: center;
}

.rank-num {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--text-muted);
}

.team-avatar {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  overflow: hidden;
  background: var(--input-bg);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.avatar-initials {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--text-muted);
}

.team-info {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
}

.team-name {
  font-weight: 700;
  font-size: 0.95rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.player-name {
  font-size: 0.78rem;
  color: var(--text-muted);
}

.team-record {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.1rem;
  flex-shrink: 0;
  min-width: 54px;
}

.record {
  font-weight: 700;
  font-size: 0.9rem;
}
.mp {
  font-size: 0.72rem;
  color: var(--text-muted);
}

.team-pts {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.1rem;
  flex-shrink: 0;
  min-width: 54px;
}

.pts-label {
  font-size: 0.68rem;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}
.pts-val {
  font-weight: 700;
  font-size: 0.9rem;
}

.you-badge {
  background: var(--primary);
  color: #fff;
  border-radius: 4px;
  padding: 0.15rem 0.4rem;
  font-size: 0.7rem;
  font-weight: 700;
  flex-shrink: 0;
}

.chevron {
  color: var(--text-muted);
  flex-shrink: 0;
}

/* Expanded roster */
.team-roster {
  padding: 0.75rem 1rem 1rem;
  border-top: 1px solid var(--border-color);
}

.no-picks {
  color: var(--text-muted);
  font-size: 0.875rem;
}

.picks-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(90px, 1fr));
  gap: 0.6rem;
}

.pick-tile {
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 0.5rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.25rem;
  cursor: pointer;
  transition:
    border-color 0.12s,
    background 0.12s;
}

.pick-tile:hover {
  border-color: var(--primary);
  background: var(--primary-hover-bg);
}

.pick-sprite {
  width: 60px;
  height: 60px;
  object-fit: contain;
}
.pick-name {
  font-size: 0.7rem;
  font-weight: 600;
  text-align: center;
  text-transform: capitalize;
}
.pick-types {
  display: flex;
  gap: 0.2rem;
  flex-wrap: wrap;
  justify-content: center;
}
.type-badge {
  font-size: 0.6rem;
  font-weight: 700;
  color: #fff;
  padding: 0.1rem 0.3rem;
  border-radius: 3px;
  text-transform: capitalize;
}
.pick-pts {
  font-size: 0.68rem;
  color: var(--text-muted);
}
</style>
