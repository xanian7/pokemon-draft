<script setup lang="ts">
import { computed, ref } from 'vue'
import PokemonCard from '@/components/PokemonCard.vue'
import type { Pokemon } from '@/types'
import { ATTACK_TYPES } from '@/data/typeChart'
import { TYPE_COLORS } from '@/utils/format'
import {
  pokemonWeaknesses,
  pressureTypes,
  rosterWeaknesses,
  typeMultiplier,
} from '@/utils/typeMatchups'

interface RosterEntry {
  pokemon: Pokemon
  points: number
}

type ReportTab = 'overview' | 'type-chart'

const props = defineProps<{
  myTeamName: string
  opponentTeamName: string
  myRoster: RosterEntry[]
  opponentRoster: RosterEntry[]
}>()

const emit = defineEmits<{
  selectPokemon: [pokemon: Pokemon]
}>()

const activeTab = ref<ReportTab>('overview')

const myPokemon = computed(() => props.myRoster.map((entry) => entry.pokemon))
const opponentPokemon = computed(() => props.opponentRoster.map((entry) => entry.pokemon))

const reports = computed(() => [
  {
    key: 'mine',
    eyebrow: 'Your roster',
    name: props.myTeamName,
    roster: props.myRoster,
    weaknesses: rosterWeaknesses(myPokemon.value),
  },
  {
    key: 'opponent',
    eyebrow: 'Opponent roster',
    name: props.opponentTeamName,
    roster: props.opponentRoster,
    weaknesses: rosterWeaknesses(opponentPokemon.value),
  },
])

const myPressure = computed(() => pressureTypes(myPokemon.value, opponentPokemon.value))
const opponentPressure = computed(() => pressureTypes(opponentPokemon.value, myPokemon.value))

const myTypeChart = computed(() =>
  props.myRoster.map((entry) => ({
    ...entry,
    superEffectiveTypes: ATTACK_TYPES.map((type) => ({
      type,
      multiplier: Math.max(
        ...entry.pokemon.types.map((attackType) => typeMultiplier(attackType, [type])),
      ),
    })).filter((type) => type.multiplier > 1),
    weaknesses: pokemonWeaknesses(entry.pokemon),
  })),
)
function typeColor(type: string) {
  return TYPE_COLORS[type] ?? '#6f7890'
}

</script>

<template>
  <section class="scouting-report">
    <header class="scouting-report__header">
      <div>
        <span>Scouting report</span>
        <h2>Roster matchup</h2>
      </div>
      <v-chip prepend-icon="mdi-radar" size="small" variant="outlined">Type-based</v-chip>
    </header>

    <v-tabs v-model="activeTab" class="report-tabs" density="compact">
      <v-tab value="overview">Overview</v-tab>
      <v-tab value="type-chart">Type chart</v-tab>
    </v-tabs>

    <v-tabs-window v-model="activeTab">
      <v-tabs-window-item value="overview">
        <div class="report-view">
          <div class="pressure-grid">
            <div class="pressure-panel pressure-panel--mine">
              <div class="pressure-panel__heading">
                <v-icon icon="mdi-target" size="18" />
                <strong>Your strengths</strong>
              </div>
              <p>Types on your roster that hit their weaknesses.</p>
              <div v-if="myPressure.length" class="type-badge-list">
                <span
                  v-for="entry in myPressure.slice(0, 6)"
                  :key="entry.type"
                  class="type-badge"
                  :style="{ backgroundColor: typeColor(entry.type) }"
                >
                  {{ entry.type }} · {{ entry.weakCount }} target{{ entry.weakCount === 1 ? '' : 's' }}
                </span>
              </div>
              <span v-else class="empty-analysis">No direct STAB pressure found.</span>
            </div>

            <div class="pressure-panel pressure-panel--opponent">
              <div class="pressure-panel__heading">
                <v-icon icon="mdi-alert-outline" size="18" />
                <strong>Your weaknesses</strong>
              </div>
              <p>Types on their roster that hit your weaknesses.</p>
              <div v-if="opponentPressure.length" class="type-badge-list">
                <span
                  v-for="entry in opponentPressure.slice(0, 6)"
                  :key="entry.type"
                  class="type-badge"
                  :style="{ backgroundColor: typeColor(entry.type) }"
                >
                  {{ entry.type }} · {{ entry.weakCount }} target{{ entry.weakCount === 1 ? '' : 's' }}
                </span>
              </div>
              <span v-else class="empty-analysis">No direct STAB pressure found.</span>
            </div>
          </div>

          <div class="team-report-grid">
            <section v-for="report in reports" :key="report.key" class="team-report">
              <header class="team-report__header">
                <div>
                  <span>{{ report.eyebrow }}</span>
                  <strong>{{ report.name }}</strong>
                </div>
                <v-chip size="small" variant="tonal">
                  {{ report.roster.length }} Pokémon
                </v-chip>
              </header>

              <div class="shared-weaknesses">
                <div class="analysis-label">
                  <strong>Shared weaknesses</strong>
                  <span>Most common defensive openings</span>
                </div>
                <div v-if="report.weaknesses.length" class="type-badge-list">
                  <span
                    v-for="entry in report.weaknesses.slice(0, 6)"
                    :key="entry.type"
                    class="type-badge"
                    :style="{ backgroundColor: typeColor(entry.type) }"
                  >
                    {{ entry.type }} · {{ entry.weakCount }}
                    <b v-if="entry.fourTimesCount">· {{ entry.fourTimesCount }}×4</b>
                  </span>
                </div>
                <span v-else class="empty-analysis">No shared weaknesses to display.</span>
              </div>

              <div v-if="report.roster.length" class="pokemon-analysis-list">
                <div
                  v-for="entry in report.roster"
                  :key="entry.pokemon.id"
                  class="pokemon-analysis"
                >
                  <PokemonCard
                    :pokemon="entry.pokemon"
                    :point-value="entry.points"
                    mode="team"
                    :show-sprite="true"
                    @click="emit('selectPokemon', entry.pokemon)"
                  />
                  <div class="pokemon-weaknesses">
                    <span
                      v-for="weakness in pokemonWeaknesses(entry.pokemon)"
                      :key="weakness.type"
                      class="type-badge"
                      :style="{ backgroundColor: typeColor(weakness.type) }"
                    >
                      {{ weakness.type }}
                      <b v-if="weakness.multiplier >= 4">4×</b>
                    </span>
                  </div>
                </div>
              </div>

              <v-alert v-else type="info" variant="tonal">
                This team does not have any Pokémon on its roster yet.
              </v-alert>
            </section>
          </div>
        </div>
      </v-tabs-window-item>

      <v-tabs-window-item value="type-chart">
        <div class="report-view">
          <p class="type-chart-note">
            Natural STAB coverage and defensive weaknesses for your roster. Movesets and abilities are not included.
          </p>

          <div v-if="myTypeChart.length" class="type-chart" role="table" aria-label="Your roster type chart">
            <div class="type-chart__header" role="row">
              <span role="columnheader">Pokémon</span>
              <span role="columnheader">Hits super effectively</span>
              <span role="columnheader">Weak to</span>
            </div>

            <div
              v-for="entry in myTypeChart"
              :key="entry.pokemon.id"
              class="type-chart__row"
              role="row"
            >
              <div class="type-chart__pokemon" role="cell">
                <PokemonCard
                  :pokemon="entry.pokemon"
                  :point-value="entry.points"
                  mode="team"
                  :show-sprite="true"
                  @click="emit('selectPokemon', entry.pokemon)"
                />
              </div>

              <div class="type-chart__results">
                <section class="type-chart__cell type-chart__cell--offense" role="cell">
                  <span class="type-chart__label">Hits super effectively</span>
                  <div v-if="entry.superEffectiveTypes.length" class="type-chart__types">
                    <span
                      v-for="type in entry.superEffectiveTypes"
                      :key="type.type"
                      class="type-badge"
                      :style="{ backgroundColor: typeColor(type.type) }"
                    >
                      {{ type.type }}
                    </span>
                  </div>
                  <span v-else class="empty-analysis">No natural STAB advantages.</span>
                </section>

                <section class="type-chart__cell type-chart__cell--weakness" role="cell">
                  <span class="type-chart__label">Weak to</span>
                  <div v-if="entry.weaknesses.length" class="type-chart__types">
                    <span
                      v-for="weakness in entry.weaknesses"
                      :key="weakness.type"
                      class="type-badge"
                      :style="{ backgroundColor: typeColor(weakness.type) }"
                    >
                      {{ weakness.type }}
                      <b v-if="weakness.multiplier >= 4">{{ weakness.multiplier }}×</b>
                    </span>
                  </div>
                  <span v-else class="empty-analysis">No type weaknesses.</span>
                </section>
              </div>
            </div>
          </div>

          <v-alert v-else type="info" variant="tonal">
            Your roster does not have any Pokémon to chart yet.
          </v-alert>
        </div>
      </v-tabs-window-item>
    </v-tabs-window>
  </section>
</template>

<style scoped>
.scouting-report {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.scouting-report__header,
.pressure-panel__heading,
.team-report__header,
.analysis-label {
  display: flex;
  align-items: center;
}

.scouting-report__header {
  justify-content: space-between;
}

.scouting-report__header span,
.team-report__header span {
  color: var(--text-muted);
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
}

.scouting-report__header h2 {
  margin-top: 2px;
  font-size: 1.1rem;
  font-weight: 800;
}

.report-tabs {
  border-bottom: 1px solid var(--border-color);
}

.report-tabs :deep(.v-tab) {
  font-weight: 700;
  letter-spacing: 0;
  text-transform: none;
}

.report-view {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding-top: 12px;
}

.pressure-grid,
.team-report-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.pressure-panel {
  min-width: 0;
  padding: 12px;
  border: 1px solid var(--border-color);
  border-left: 3px solid var(--primary);
  border-radius: 4px;
  background: rgba(var(--primary-rgb), 0.06);
}

.pressure-panel--opponent {
  border-left-color: var(--secondary);
  background: rgba(var(--secondary-rgb), 0.06);
}

.pressure-panel__heading {
  gap: 6px;
}

.pressure-panel p {
  margin: 3px 0 10px;
  color: var(--text-muted);
  font-size: 0.76rem;
}

.type-badge-list,
.pokemon-weaknesses {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.type-badge {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  padding: 2px 7px;
  color: #fff;
  font-size: 0.6rem;
  font-weight: 600;
  line-height: 1.35;
  text-transform: capitalize;
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.4);
  border-radius: 999px;
}

.type-badge b {
  color: #fff;
  font-size: inherit;
  font-weight: 800;
}

.team-report {
  min-width: 0;
}

.team-report__header {
  justify-content: space-between;
  gap: 12px;
  min-height: 48px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--border-color);
}

.team-report__header > div {
  min-width: 0;
}

.team-report__header > div {
  display: flex;
  flex-direction: column;
}

.team-report__header strong {
  overflow: hidden;
  font-size: 0.95rem;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.shared-weaknesses {
  min-height: 86px;
  padding: 10px 0;
}

.analysis-label {
  align-items: flex-start;
  flex-direction: column;
  margin-bottom: 7px;
}

.analysis-label strong {
  font-size: 0.82rem;
}

.analysis-label span,
.empty-analysis {
  color: var(--text-muted);
  font-size: 0.72rem;
}

.pokemon-analysis-list {
  display: grid;
  gap: 7px;
}

.pokemon-analysis {
  display: grid;
  gap: 5px;
  min-width: 0;
}

.pokemon-analysis :deep(.pokemon-card) {
  width: 100%;
  border-radius: 4px;
  cursor: pointer;
}

.pokemon-analysis :deep(.mode-team .sprite) {
  display: block;
  flex: 0 0 58px;
  width: 58px;
  height: 58px;
}

.pokemon-weaknesses {
  padding-inline: 4px;
}

.type-chart-note {
  margin: 0;
  color: var(--text-muted);
  font-size: 0.72rem;
}

.type-chart {
  overflow: hidden;
  border: 1px solid var(--border-color);
  border-radius: 4px;
}

.type-chart__header,
.type-chart__row {
  display: grid;
  grid-template-columns: minmax(180px, 0.8fr) repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.type-chart__header {
  padding: 8px 10px;
  color: var(--text-muted);
  background: var(--card-bg-solid);
  font-size: 0.68rem;
  font-weight: 800;
  text-transform: uppercase;
}

.type-chart__row {
  align-items: start;
  padding: 10px;
  border-top: 1px solid var(--border-color);
}

.type-chart__pokemon,
.type-chart__cell {
  min-width: 0;
}

.type-chart__pokemon :deep(.pokemon-card) {
  width: 100%;
  cursor: pointer;
}

.type-chart__pokemon :deep(.mode-team .sprite) {
  display: block;
  flex: 0 0 58px;
  width: 58px;
  height: 58px;
}

.type-chart__results {
  display: grid;
  grid-column: 2 / 4;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.type-chart__cell {
  min-height: 58px;
  padding-top: 5px;
}

.type-chart__cell--offense {
  padding-left: 10px;
  border-left: 3px solid rgb(var(--primary-rgb));
}

.type-chart__cell--weakness {
  padding-left: 10px;
  border-left: 3px solid rgb(var(--secondary-rgb));
}

.type-chart__label {
  display: none;
  margin-bottom: 6px;
  color: var(--text-muted);
  font-size: 0.68rem;
  font-weight: 800;
  text-transform: uppercase;
}

.type-chart__types {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

@media (max-width: 1000px) {
  .pressure-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 600px) {
  .team-report-grid {
    gap: 6px;
  }

  .team-report__header {
    align-items: flex-start;
    min-height: 56px;
  }

  .team-report__header :deep(.v-chip) {
    display: none;
  }

  .team-report__header strong {
    max-width: 100%;
    font-size: 0.82rem;
  }

  .shared-weaknesses {
    min-height: 0;
  }

  .pokemon-analysis :deep(.mode-team) {
    flex-direction: column;
    gap: 3px;
    align-items: center;
    padding: 0.45rem 0.35rem;
    text-align: center;
  }

  .pokemon-analysis :deep(.mode-team .sprite) {
    flex-basis: 50px;
    width: 50px;
    height: 50px;
  }

  .pokemon-analysis :deep(.mode-team .types) {
    justify-content: center;
  }

  .pokemon-analysis :deep(.pokemon-name) {
    font-size: 0.72rem;
  }

  .type-chart__header {
    display: none;
  }

  .type-chart__row {
    grid-template-columns: 1fr;
    gap: 8px;
    padding: 8px;
  }

  .type-chart__results {
    grid-column: auto;
    gap: 6px;
  }

  .type-chart__label {
    display: block;
  }

  .type-chart__cell {
    min-height: 0;
    padding: 7px;
    background: rgba(var(--primary-rgb), 0.04);
  }
  .type-badge {
    padding-inline: 5px;
    font-size: 0.56rem;
  }
}
</style>
