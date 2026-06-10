<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import PokeballLoader from '@/components/PokeballLoader.vue'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import PageHeader from '@/components/PageHeader.vue'
import DraftGateNotice from '@/components/DraftGateNotice.vue'
import { API_BASE } from '@/services/signalr'

const router = useRouter()
const authStore = useAuthStore()
const pokemonStore = usePokemonStore()

if (!authStore.isAuthenticated) router.replace('/join')

const league = ref<any>(null)
const standings = ref<any[]>([])
const isLoading = ref(true)
const error = ref('')
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

const teams = computed<any[]>(() => {
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

const totalDrafted = computed(() => teams.value.reduce((total, team) => total + team.picks.length, 0))
const leader = computed(() => teams.value[0] ?? null)
</script>

<template>
  <v-container fluid class="teams-page">
    <PageHeader
      class="page-hero"
      eyebrow="League overview"
      title="All Teams"
      :subtitle="league?.name || 'Drafted rosters and standings'"
    >
      <template #actions>
        <v-chip color="primary" variant="tonal" prepend-icon="mdi-account-group">
          {{ teams.length }} teams
        </v-chip>
      </template>
    </PageHeader>

    <div v-if="isLoading" class="state-panel">
      <!-- <PokeballLoader variant="page" label="Loading teams…" /> -->
    </div>
    <v-alert v-else-if="error" type="error" variant="tonal">{{ error }}</v-alert>
    <DraftGateNotice
      v-else-if="!league?.draft?.picks?.length"
      title="Waiting for the draft"
      text="Team rosters will appear here once drafting begins."
    />

    <template v-else>
      <v-row class="summary-grid" dense>
        <v-col cols="12" sm="4">
          <v-card variant="outlined" class="summary-card">
            <v-card-text>
              <span>League leader</span>
              <strong>{{ leader?.teamName || leader?.name || '—' }}</strong>
            </v-card-text>
          </v-card>
        </v-col>
        <v-col cols="6" sm="4">
          <v-card variant="outlined" class="summary-card">
            <v-card-text><span>Pokémon drafted</span><strong>{{ totalDrafted }}</strong></v-card-text>
          </v-card>
        </v-col>
        <v-col cols="6" sm="4">
          <v-card variant="outlined" class="summary-card">
            <v-card-text><span>Teams competing</span><strong>{{ teams.length }}</strong></v-card-text>
          </v-card>
        </v-col>
      </v-row>

      <div class="team-grid">
        <v-card
          v-for="team in teams"
          :key="team.id"
          class="team-card"
          :class="{ 'my-team': team.id === authStore.playerId }"
          variant="outlined"
        >
          <v-card-title class="team-card-header">
            <div class="team-identity">
              <v-chip size="small" variant="tonal">#{{ team.rank || '—' }}</v-chip>
              <v-avatar size="40" color="surface">
                <v-img v-if="team.teamImageUrl" :src="team.teamImageUrl" :alt="team.teamName" />
                <span v-else>{{ (team.teamName || team.name).slice(0, 2).toUpperCase() }}</span>
              </v-avatar>
              <div class="team-info">
                <strong>{{ team.teamName || team.name }}</strong>
                <span>{{ team.teamName ? team.name : `${team.picks.length} Pokémon` }}</span>
              </div>
              <v-chip v-if="team.id === authStore.playerId" size="small" color="primary">You</v-chip>
            </div>
            <div class="team-metrics">
              <div><strong>{{ team.wins }}–{{ team.losses }}</strong><span>Record</span></div>
              <div><strong>{{ team.matchPoints }}</strong><span>Match pts</span></div>
              <div><strong>{{ team.totalPoints }}</strong><span>Draft pts</span></div>
            </div>
          </v-card-title>

          <v-divider />

          <v-card-text class="team-roster">
            <v-empty-state
              v-if="team.picks.length === 0"
              icon="mdi-pokeball-outline"
              title="No picks yet"
              size="compact"
            />
            <div v-else class="picks-grid">
              <v-card
                v-for="poke in team.picks"
                :key="poke.id"
                class="pick-tile"
                variant="tonal"
                hover
                @click="selectedPokemonId = poke.id"
              >
                <v-img :src="poke.spriteUrl" :alt="poke.name" height="74" contain />
                <v-card-text>
                  <strong>{{ poke.name }}</strong>
                  <span>{{ poke.points }} pts</span>
                </v-card-text>
              </v-card>
            </div>
          </v-card-text>
        </v-card>
      </div>
    </template>

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
  </v-container>
</template>

<style scoped>
.teams-page {
  padding: clamp(1rem, 2vw, 2rem);
}
.page-hero {
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
.summary-card span,
.team-info span,
.team-metrics span,
.pick-tile span {
  color: var(--text-muted);
  font-size: 0.78rem;
}
.state-panel {
  display: flex;
  justify-content: center;
  padding: 48px;
}
.summary-grid {
  margin-bottom: 12px;
}
.summary-card .v-card-text {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.summary-card strong {
  font-size: 1.15rem;
}
.team-grid {
  display: grid;
  gap: 14px;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 420px), 1fr));
}
.team-card {
  border: 1px solid var(--border-color);
  border-radius: 16px;
  overflow: hidden;
}
.team-card.my-team {
  border-color: rgba(var(--primary-rgb), 0.65);
  box-shadow: 0 0 0 1px rgba(var(--primary-rgb), 0.2);
}
.team-card-header {
  align-items: stretch;
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 14px 16px 12px;
  white-space: normal;
}
.team-identity {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
}
.team-info {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.picks-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(92px, 1fr));
  gap: 8px;
}
.team-roster {
  padding: 12px;
}
.pick-tile {
  cursor: pointer;
}
.pick-tile :deep(.v-img) {
  margin-top: 4px;
}
.pick-tile .v-card-text {
  display: flex;
  flex-direction: column;
  padding: 4px 6px 8px;
  text-align: center;
}
.pick-tile strong {
  text-transform: capitalize;
  font-size: 0.72rem;
}
.team-metrics {
  display: flex;
  justify-content: space-around;
  gap: 12px;
  padding: 0 4px;
}
.team-metrics div {
  display: flex;
  flex-direction: column;
  align-items: center;
}
@media (max-width: 700px) {
  .teams-page {
    padding: 12px;
  }
  .hero-content {
    align-items: flex-start;
  }
  .team-identity {
    gap: 8px;
  }
  .picks-grid {
    grid-template-columns: repeat(auto-fill, minmax(82px, 1fr));
  }
}
</style>
