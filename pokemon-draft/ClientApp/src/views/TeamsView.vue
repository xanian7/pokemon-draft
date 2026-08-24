<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import PokemonDetailModal from '@/components/PokemonDetailModal.vue'
import PokemonCard from '@/components/PokemonCard.vue'
import PageHeader from '@/components/PageHeader.vue'
import SectionHeader from '@/components/SectionHeader.vue'
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
  <v-container fluid class="page-card-large">
    <div v-if="isLoading" class="page-state">
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
          <v-card variant="outlined" class="summary-card section-card">
            <v-card-text>
              <span>League leader</span>
              <strong>{{ leader?.teamName || leader?.name || '—' }}</strong>
            </v-card-text>
          </v-card>
        </v-col>
        <v-col cols="6" sm="4">
          <v-card variant="outlined" class="summary-card section-card">
            <v-card-text><span>Pokémon drafted</span><strong>{{ totalDrafted }}</strong></v-card-text>
          </v-card>
        </v-col>
        <v-col cols="6" sm="4">
          <v-card variant="outlined" class="summary-card section-card">
            <v-card-text><span>Teams competing</span><strong>{{ teams.length }}</strong></v-card-text>
          </v-card>
        </v-col>
      </v-row>

      <SectionHeader
        class="roster-section-header"
        eyebrow="Competition"
        title="League Rosters"
        subtitle="Drafted Pokémon, records, and point totals"
      >
        <template #actions>
          <v-chip size="small" variant="tonal">
            {{ totalDrafted }} drafted
          </v-chip>
        </template>
      </SectionHeader>

      <div class="team-grid">
        <v-card
          v-for="team in teams"
          :key="team.id"
          class="team-card section-card"
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
              <PokemonCard
                v-for="poke in team.picks"
                :key="poke.id"
                :pokemon="poke"
                :point-value="poke.points"
                mode="team"
                stacked
                :show-sprite="true"
                @click="selectedPokemonId = poke.id"
              />
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



.summary-card span,
.team-info span,
.team-metrics span {
  color: var(--text-muted);
  font-size: 0.78rem;
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
.roster-section-header {
  margin: 4px 0 12px;
  padding-inline: 0;
}

.team-grid {
  display: grid;
  gap: 14px;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 420px), 1fr));
}
.team-card {
  border: 1px solid var(--border-color);
  overflow: hidden;
}
.team-card.my-team {
  border-color: rgba(var(--primary-rgb), 0.65);
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
  grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  gap: 8px;
}

.picks-grid :deep(.pokemon-card) {
  width: 100%;
  border-radius: 4px;
}
.team-roster {
  padding: 12px;
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


  .team-identity {
    gap: 8px;
  }
  .picks-grid {
    grid-template-columns: repeat(auto-fill, minmax(125px, 1fr));
  }
}
</style>
