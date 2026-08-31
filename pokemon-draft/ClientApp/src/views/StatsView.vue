<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import PageHeader from '@/components/PageHeader.vue'
import SectionHeader from '@/components/SectionHeader.vue'
import { apiGet } from '@/services/api'
import { useAuthStore } from '@/stores/auth'
import { usePokemonStore } from '@/stores/pokemon'
import type { ReplayGameStat, ReplayPokemonStat, ReplayStatsData } from '@/types'

const authStore = useAuthStore()
const pokemonStore = usePokemonStore()
const stats = ref<ReplayStatsData | null>(null)
const loading = ref(true)
const error = ref('')
const activeWeek = ref<number | null>(null)
const selectedPlayerId = ref<string | null>(null)
const pokemonTotalsLimit = ref(10)

const completedGames = computed(() => stats.value?.games.filter((game) => game.status === 'Complete') ?? [])
const totalKos = computed(() => stats.value?.pokemon.reduce((sum, pokemon) => sum + pokemon.kos, 0) ?? 0)
const leader = computed(() => stats.value?.pokemon[0] ?? null)
const pokemonTotalsLimitOptions = [
  { title: 'Top 10', value: 10 },
  { title: 'Top 25', value: 25 },
  { title: 'Top 50', value: 50 },
  { title: 'All Pokémon', value: -1 },
]
const pokemonTotalsHeaders = [
  { title: '#', key: 'rank', width: 56 },
  { title: 'Pokémon', key: 'pokemon' },
  { title: 'Team', key: 'team' },
  { title: 'Games', key: 'games', align: 'end' as const, width: 80 },
  { title: 'KOs', key: 'kos', align: 'end' as const, width: 72 },
  { title: 'Deaths', key: 'deaths', align: 'end' as const, width: 80 },
  { title: 'K/D', key: 'kd', align: 'end' as const, width: 72 },
]
const pokemonTotalsRows = computed(() =>
  (stats.value?.pokemon ?? []).map((pokemon, index) => ({
    ...pokemon,
    rank: index + 1,
    pokemon: pokemon.pokemonName,
    team: pokemon.teamName || pokemon.playerName,
    kd: pokemon.deaths ? (pokemon.kos / pokemon.deaths).toFixed(2) : pokemon.kos.toFixed(2),
    rowId: `${pokemon.playerId}-${pokemon.pokemonId}-${pokemon.pokemonName}`,
  })),
)
const visiblePokemonTotals = computed(() => {
  const pokemon = pokemonTotalsRows.value
  return pokemonTotalsLimit.value < 0 ? pokemon : pokemon.slice(0, pokemonTotalsLimit.value)
})
const playerOptions = computed(() => {
  const players = new Map<string, string>()
  const details = new Map(
    (stats.value?.pokemon ?? [])
      .filter((pokemon) => pokemon.playerId)
      .map((pokemon) => [pokemon.playerId!, pokemon] as const),
  )
  const labelFor = (playerId: string, gameTeamName: string) => {
    const player = details.get(playerId)
    const teamName = player?.teamName || gameTeamName
    const playerName = player?.playerName
    if (teamName && playerName) return `${teamName} · ${playerName}`
    return teamName || playerName || playerId
  }
  for (const game of stats.value?.games ?? []) {
    players.set(game.player1Id, labelFor(game.player1Id, game.player1TeamName))
    players.set(game.player2Id, labelFor(game.player2Id, game.player2TeamName))
  }
  return [...players.entries()]
    .map(([value, title]) => ({ value, title }))
    .sort((left, right) => left.title.localeCompare(right.title))
})
const selectedGames = computed(() =>
  (stats.value?.games ?? []).filter(
    (game) =>
      !selectedPlayerId.value ||
      game.player1Id === selectedPlayerId.value ||
      game.player2Id === selectedPlayerId.value,
  ),
)
const weekStats = computed(() => {
  const grouped = new Map<number, ReplayGameStat[]>()
  for (const game of selectedGames.value) {
    const games = grouped.get(game.week) ?? []
    games.push(game)
    grouped.set(game.week, games)
  }

  return [...grouped.entries()]
    .sort(([left], [right]) => left - right)
    .map(([week, games]) => ({
      week,
      games: games.sort((left, right) =>
        left.matchupId - right.matchupId || left.gameNumber - right.gameNumber),
    }))
})

onMounted(async () => {
  try {
    await pokemonStore.fetchAllPokemon()
    const result = await apiGet<ReplayStatsData>(`/leagues/${authStore.leagueCode}/replay-stats`)
    if (result.error || !result.data) throw new Error(result.error ?? 'Stats were empty.')
    stats.value = result.data
    const availablePlayerIds = new Set(
      result.data.games.flatMap((game) => [game.player1Id, game.player2Id]),
    )
    selectedPlayerId.value =
      authStore.playerId && availablePlayerIds.has(authStore.playerId)
        ? authStore.playerId
        : playerOptions.value[0]?.value ?? null
    const weeks = [...new Set(selectedGames.value.map((game) => game.week))].sort((a, b) => a - b)
    activeWeek.value = weeks[weeks.length - 1] ?? null
  } catch (caught) {
    console.error(caught)
    error.value = 'Unable to load replay stats right now.'
  } finally {
    loading.value = false
  }
})

watch(selectedPlayerId, () => {
  activeWeek.value = weekStats.value[weekStats.value.length - 1]?.week ?? null
})

function spriteFor(pokemonId: number | null) {
  return pokemonId ? pokemonStore.getPokemonById(pokemonId)?.spriteUrl : undefined
}

function teamFor(game: ReplayGameStat, stat: ReplayPokemonStat) {
  if (stat.playerId === game.player1Id) return game.player1TeamName
  if (stat.playerId === game.player2Id) return game.player2TeamName
  return stat.side === 'p1' ? game.showdownPlayer1 : game.showdownPlayer2
}

function gameTitle(game: ReplayGameStat) {
  return `Game ${game.gameNumber}`
}
</script>

<template>
  <v-container fluid class="page-card-small stats-page">
    <PageHeader
      eyebrow="Replay analysis"
      title="Battle Stats"
      subtitle="KO and death totals parsed from reported Pokémon Showdown replays."
    />

    <div v-if="loading" class="page-state">
      <v-progress-circular indeterminate color="primary" />
    </div>
    <v-alert v-else-if="error" type="error" variant="tonal">{{ error }}</v-alert>
    <v-alert
      v-else-if="!stats?.games.length"
      type="info"
      variant="tonal"
      icon="mdi-chart-box-outline"
    >
      Stats will appear here after a matchup is reported with a Showdown replay link.
    </v-alert>

    <template v-else>
      <v-row class="summary-grid" dense>
        <v-col cols="12" sm="4">
          <v-card variant="outlined" class="summary-card section-card">
            <v-card-text>
              <span>Analyzed games</span><strong>{{ completedGames.length }}</strong>
            </v-card-text>
          </v-card>
        </v-col>
        <v-col cols="6" sm="4">
          <v-card variant="outlined" class="summary-card section-card">
            <v-card-text>
              <span>Attributed KOs</span><strong>{{ totalKos }}</strong>
            </v-card-text>
          </v-card>
        </v-col>
        <v-col cols="6" sm="4">
          <v-card variant="outlined" class="summary-card section-card">
            <v-card-text>
              <span>KO leader</span>
              <strong>{{ leader ? `${leader.pokemonName} (${leader.kos})` : '—' }}</strong>
            </v-card-text>
          </v-card>
        </v-col>
      </v-row>

      <v-card class="section-card totals-card">
        <SectionHeader
          class="totals-header"
          eyebrow="Season leaderboard"
          title="Pokémon totals"
          subtitle="KO and death totals across all analyzed games."
        >
          <template #actions>
            <v-select
              v-model="pokemonTotalsLimit"
              class="totals-limit-select"
              :items="pokemonTotalsLimitOptions"
              item-title="title"
              item-value="value"
              label="Show"
              density="compact"
              variant="outlined"
              hide-details
            />
          </template>
        </SectionHeader>
        <v-data-table
          :headers="pokemonTotalsHeaders"
          :items="visiblePokemonTotals"
          :items-per-page="-1"
          class="standings-table pokemon-totals-table"
          density="compact"
          hide-default-footer
          item-value="rowId"
        >
          <template #item.pokemon="{ item }">
            <div class="pokemon-cell">
              <v-avatar size="38">
                <v-img
                  v-if="spriteFor(item.pokemonId)"
                  :src="spriteFor(item.pokemonId)"
                  :alt="item.pokemonName"
                />
              </v-avatar>
              <strong>{{ item.pokemonName }}</strong>
            </div>
          </template>
          <template #item.kos="{ item }">
            <span class="ko-value">{{ item.kos }}</span>
          </template>
        </v-data-table>
      </v-card>

      <section class="games-section">
        <div class="games-toolbar">
          <div>
            <span>Weekly results</span>
            <h2>Match breakdown</h2>
          </div>
          <v-select
            v-model="selectedPlayerId"
            class="games-player-select"
            :items="playerOptions"
            item-title="title"
            item-value="value"
            label="Team"
            prepend-inner-icon="mdi-account-search-outline"
            density="compact"
            variant="outlined"
            hide-details
          />
        </div>
        <v-tabs v-model="activeWeek" class="week-tabs" density="comfortable" show-arrows>
          <v-tab v-for="week in weekStats" :key="week.week" :value="week.week">
            Week {{ week.week }}
          </v-tab>
        </v-tabs>

        <v-tabs-window v-model="activeWeek" class="week-window">
          <v-tabs-window-item v-for="week in weekStats" :key="week.week" :value="week.week">
            <section class="week-panel">
              <SectionHeader
                class="week-section-header"
                eyebrow="Replay analysis"
                :title="`Week ${week.week}`"
                subtitle="Game-by-game performance and revealed battle details"
              >
                <template #actions>
                  <v-chip size="small" variant="tonal">
                    {{ week.games.length }} {{ week.games.length === 1 ? 'game' : 'games' }} analyzed
                  </v-chip>
                </template>
              </SectionHeader>

              <div class="game-list">
                <v-card v-for="game in week.games" :key="game.id" class="game-card section-card">
                <v-card-title class="game-card__title">
                  <div class="game-heading">
                    <strong>{{ gameTitle(game) }}</strong>
                    <span v-if="game.status === 'Complete'">
                      {{ game.showdownPlayer1 }} vs {{ game.showdownPlayer2 }} · {{ game.winnerName || 'Tie' }} won
                    </span>
                    <span v-else class="game-error">Analysis failed · {{ game.error }}</span>
                  </div>
                </v-card-title>
                <v-card-text>
                  <div v-if="game.status === 'Complete'" class="game-teams">
                    <div v-for="side in ['p1', 'p2']" :key="side" class="game-team">
                      <h3>{{ side === 'p1' ? game.showdownPlayer1 : game.showdownPlayer2 }}</h3>
                      <div
                        v-for="pokemon in game.pokemon.filter((entry) => entry.side === side)"
                        :key="`${side}-${pokemon.pokemonName}`"
                        class="game-pokemon"
                      >
                        <div class="pokemon-cell">
                          <img v-if="spriteFor(pokemon.pokemonId)" :src="spriteFor(pokemon.pokemonId)" alt="" />
                          <span>{{ pokemon.pokemonName }}</span>
                        </div>
                        <small>{{ teamFor(game, pokemon) }}</small>
                        <strong>{{ pokemon.kos }} KO · {{ pokemon.deaths }} death</strong>
                        <div class="scouting-data">
                          <div class="scouting-field scouting-field--moves">
                            <span>{{ pokemon.movesAreComplete ? 'Revealed moveset' : 'Observed moves' }}</span>
                            <div v-if="pokemon.moves.length" class="move-list">
                              <v-chip
                              v-for="move in pokemon.moves"
                              :key="move"
                              size="small"
                                variant="tonal"
                              >
                                {{ move }}
                              </v-chip>
                            </div>
                            <small v-else>None revealed in this replay</small>
                          </div>
                          <div class="scouting-field">
                            <span>Revealed item</span>
                            <strong>{{ pokemon.heldItem || 'Not revealed' }}</strong>
                          </div>
                          <div class="scouting-field">
                            <span>Revealed ability</span>
                            <strong>{{ pokemon.ability || 'Not revealed' }}</strong>
                          </div>
                          <div class="scouting-field">
                            <span>Nature</span>
                            <strong>{{ pokemon.nature || 'Not exposed by replay' }}</strong>
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                  <v-alert v-else type="warning" variant="tonal" density="compact">{{ game.error }}</v-alert>
                  <v-btn
                    :href="game.replayUrl"
                    target="_blank"
                    rel="noopener noreferrer"
                    prepend-icon="mdi-open-in-new"
                    size="small"
                    variant="text"
                    class="replay-link"
                  >
                    Open replay
                  </v-btn>
                </v-card-text>
                </v-card>
              </div>
            </section>
          </v-tabs-window-item>
        </v-tabs-window>
      </section>

    </template>
  </v-container>
</template>

<style scoped>
.stats-page { padding: 16px; }
.page-state { display: grid; min-height: 240px; place-items: center; }
.summary-grid { margin-bottom: 12px; }
.summary-card .v-card-text { display: flex; flex-direction: column; gap: 4px; }
.summary-card span { color: var(--text-muted); font-size: 0.78rem; }
.summary-card strong { font-size: 1.15rem; }
.totals-card { margin-top: 22px; overflow: hidden; border: 1px solid var(--border-color); }
.totals-limit-select { width: 150px; }
.ko-value { color: var(--primary-bright); font-weight: 800; }
.pokemon-cell { display: flex; align-items: center; gap: 8px; }
.pokemon-cell :deep(.v-img__img) { object-fit: contain; image-rendering: pixelated; }
.games-section { min-width: 0; margin-top: 18px; }
.games-toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 10px; }
.games-toolbar span { display: block; color: var(--text-muted); font-size: 0.66rem; font-weight: 700; text-transform: uppercase; }
.games-toolbar h2 { font-size: 1rem; font-weight: 800; }
.games-player-select { width: 100%; max-width: 260px; }
.week-tabs { border-bottom: 1px solid var(--border-color); }
.week-tabs :deep(.v-tab) { min-width: max-content; color: var(--text-muted); font-weight: 700; letter-spacing: 0; text-transform: none; }
.week-tabs :deep(.v-tab.v-tab--selected) { color: var(--primary-bright); background: rgba(var(--primary-rgb), 0.1); }
.week-window { margin-top: 10px; }
.week-panel { min-width: 0; }
.week-section-header { margin-bottom: 12px; }
.game-list { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 700px), 1fr)); gap: 12px; }
.game-card { min-width: 0; height: 100%; border: 1px solid var(--border-color); border-radius: 8px !important; }
.game-card__title { border-bottom: 1px solid var(--border-color); font-size: inherit; }
.game-heading { display: flex; flex-direction: column; gap: 2px; }
.game-heading span { color: var(--text-muted); font-size: 0.75rem; }
.game-heading .game-error { color: rgb(var(--v-theme-warning)); }
.game-teams { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 24px; }
.game-team h3 { margin-bottom: 8px; font-size: 1.05rem; }
.game-pokemon { display: grid; grid-template-columns: minmax(170px, 1fr) minmax(110px, 1fr) auto; align-items: center; gap: 12px; min-height: 72px; padding-block: 12px; border-top: 1px solid var(--border-color); font-size: 0.9rem; }
.game-pokemon small { color: var(--text-muted); }
.game-pokemon > .pokemon-cell { gap: 12px; font-size: 1rem; font-weight: 750; }
.game-pokemon > .pokemon-cell img { width: 62px; height: 62px; }
.game-pokemon > strong { font-size: 0.92rem; white-space: nowrap; }
.scouting-data { grid-column: 1 / -1; display: grid; grid-template-columns: minmax(220px, 2fr) repeat(3, minmax(120px, 1fr)); gap: 12px; padding: 12px; background: var(--card-bg); border: 1px solid var(--border-color); border-radius: 4px; }
.scouting-field { display: flex; flex-direction: column; gap: 5px; min-width: 0; }
.scouting-field > span { color: var(--text-muted); font-size: 0.72rem; font-weight: 700; text-transform: uppercase; }
.scouting-field > strong { overflow: hidden; font-size: 0.86rem; text-overflow: ellipsis; white-space: nowrap; }
.move-list { display: flex; flex-wrap: wrap; gap: 4px; }
.replay-link { margin-top: 10px; }
@media (max-width: 700px) {
  .totals-header { align-items: stretch; flex-direction: column; }
  .totals-header :deep(.section-header__aside) { width: 100%; }
  .totals-limit-select { width: 100%; }
  .games-toolbar { align-items: stretch; flex-direction: column; }
  .games-player-select { width: 100%; max-width: none; }
  .game-teams { grid-template-columns: 1fr; }
  .game-pokemon { grid-template-columns: minmax(120px, 1fr) auto; }
  .game-pokemon > .pokemon-cell img { width: 54px; height: 54px; }
  .game-pokemon small { display: none; }
  .scouting-data { grid-template-columns: 1fr; }
  .scouting-field--moves { grid-column: auto; }
}
</style>
