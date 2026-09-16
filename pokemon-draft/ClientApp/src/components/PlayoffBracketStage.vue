<script setup lang="ts">
import { computed } from 'vue'
import {
  allRounds,
  resolveBracket,
  type BracketConfiguration,
  type BracketPlayer,
  type BracketRound,
  type BracketMatch,
} from '@/utils/playoffBracket'

const props = defineProps<{
  title: string
  rounds: BracketRound[]
  configuration: BracketConfiguration
  players: BracketPlayer[]
  editing: boolean
}>()
const emit = defineEmits<{
  change: [matchId: string]
  result: [match: BracketMatch, winner: number | null]
  addRound: []
  removeRound: [roundId: string]
  addMatch: [roundId: string]
  removeMatch: [roundId: string, matchId: string]
}>()
const results = computed(() => resolveBracket(props.configuration))
const teamName = (id: string | null | undefined) => {
  const player = props.players.find((p) => p.playerId === id)
  return player ? player.teamName || player.playerName : id ? 'Unavailable player' : 'TBD'
}
function options(roundId: string) {
  const items = [
    { title: 'To be decided', value: '' },
    { title: 'Bye', value: 'bye' },
    ...props.players.map((p) => ({
      title: p.teamName ? p.teamName + ' · ' + p.playerName : p.playerName,
      value: 'player:' + p.playerId,
    })),
  ]
  for (const round of allRounds(props.configuration)) {
    if (round.id === roundId) break
    round.matches.forEach((match, index) => {
      items.push({
        title: round.name + ' · Match ' + (index + 1) + ' winner',
        value: 'winner:' + match.id,
      })
      items.push({
        title: round.name + ' · Match ' + (index + 1) + ' loser',
        value: 'loser:' + match.id,
      })
    })
  }
  return items
}
function sourceLabel(source: string, roundId: string) {
  return options(roundId).find((option) => option.value === source)?.title ?? 'TBD'
}
function winnerOptions(match: BracketMatch) {
  const result = results.value.get(match.id)!
  if (!result.players[0] || !result.players[1] || result.players[0] === result.players[1]) return []
  return result.players.map((id, index) => ({ title: teamName(id), value: index }))
}
</script>

<template>
  <section class="bracket-stage">
    <div class="stage-heading">
      <div>
        <h2>{{ title }}</h2>
        <p>{{ rounds.length }} {{ rounds.length === 1 ? 'round' : 'rounds' }}</p>
      </div>
      <v-btn
        v-if="editing"
        variant="tonal"
        prepend-icon="mdi-plus"
        :disabled="rounds.length >= 8"
        @click="emit('addRound')"
        >Add round</v-btn
      >
    </div>
    <p v-if="!rounds.length" class="text-medium-emphasis">
      No play-in tournament. Add a round to include one.
    </p>
    <div class="bracket-scroll" :aria-label="title + ' bracket'">
      <div v-for="(round, roundIndex) in rounds" :key="round.id" class="bracket-round">
        <div class="round-heading">
          <v-text-field
            v-if="editing"
            v-model="round.name"
            :label="'Round ' + (roundIndex + 1) + ' name'"
            maxlength="80"
            density="compact"
            hide-details
          />
          <h3 v-else>{{ round.name }}</h3>
          <v-btn
            v-if="editing"
            icon="mdi-delete-outline"
            variant="text"
            size="small"
            :aria-label="'Remove ' + round.name"
            @click="emit('removeRound', round.id)"
          />
        </div>
        <div class="round-matches">
          <v-card
            v-for="(match, index) in round.matches"
            :key="match.id"
            variant="outlined"
            class="bracket-match"
          >
            <div class="match-heading">
              <span>Match {{ index + 1 }}</span>
              <v-btn
                v-if="editing && round.matches.length > 1"
                icon="mdi-close"
                size="x-small"
                variant="text"
                :aria-label="'Remove match ' + (index + 1)"
                @click="emit('removeMatch', round.id, match.id)"
              />
            </div>
            <div
              v-for="(slot, side) in match.slots"
              :key="side"
              class="bracket-slot"
              :class="{
                winner:
                  results.get(match.id)?.winner &&
                  results.get(match.id)?.winner === results.get(match.id)?.players[side],
              }"
            >
              <template v-if="editing">
                <div class="slot-controls">
                  <v-text-field
                    :model-value="slot.seed"
                    label="Seed"
                    type="number"
                    min="1"
                    max="64"
                    density="compact"
                    hide-details
                    class="seed-input"
                    @update:model-value="
                      slot.seed = $event === '' || $event === null ? null : Number($event)
                    "
                  />
                  <v-select
                    v-model="slot.source"
                    :items="options(round.id)"
                    :label="'Participant ' + (side + 1)"
                    density="compact"
                    hide-details
                    @update:model-value="emit('change', match.id)"
                  />
                </div>
                <small v-if="/^(winner|loser):/.test(slot.source)">{{
                  teamName(results.get(match.id)?.players[side])
                }}</small>
              </template>
              <template v-else>
                <span class="seed-badge">{{ slot.seed ? '#' + slot.seed : '—' }}</span>
                <div class="participant-name">
                  <strong>{{
                    slot.source === 'bye' ? 'Bye' : teamName(results.get(match.id)?.players[side])
                  }}</strong>
                  <small v-if="/^(winner|loser):/.test(slot.source)">{{
                    sourceLabel(slot.source, round.id)
                  }}</small>
                </div>
                <v-icon
                  v-if="
                    results.get(match.id)?.winner &&
                    results.get(match.id)?.winner === results.get(match.id)?.players[side]
                  "
                  icon="mdi-check"
                  size="18"
                />
              </template>
            </div>
            <v-select
              v-if="editing && winnerOptions(match).length"
              :model-value="match.winner"
              :items="winnerOptions(match)"
              label="Winner"
              clearable
              density="compact"
              hide-details
              class="ma-3"
              @update:model-value="emit('result', match, $event)"
            />
            <div v-if="results.get(match.id)?.winner" class="match-result">
              Advances: {{ teamName(results.get(match.id)?.winner) }}
            </div>
          </v-card>
        </div>
        <v-btn
          v-if="editing"
          class="mt-3"
          variant="text"
          prepend-icon="mdi-plus"
          :disabled="round.matches.length >= 32"
          @click="emit('addMatch', round.id)"
          >Add match</v-btn
        >
      </div>
    </div>
  </section>
</template>

<style scoped>
.bracket-stage {
  margin-bottom: 24px;
}
.stage-heading,
.round-heading,
.match-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.stage-heading {
  margin-bottom: 16px;
}
.stage-heading h2 {
  font-size: 1.2rem;
}
.stage-heading p,
small,
.match-heading,
.match-result {
  color: rgb(var(--v-theme-on-surface), 0.65);
  font-size: 0.75rem;
}
.bracket-scroll {
  display: flex;
  gap: 28px;
  overflow-x: auto;
  padding: 4px 4px 16px;
}
.bracket-round {
  flex: 0 0 330px;
  display: flex;
  flex-direction: column;
}
.round-heading {
  min-height: 48px;
  margin-bottom: 12px;
}
.round-heading h3 {
  font-size: 0.95rem;
}
.round-matches {
  display: flex;
  flex-direction: column;
  justify-content: space-around;
  gap: 20px;
  flex: 1;
}
.bracket-match {
  overflow: visible;
  position: relative;
}
.bracket-round:not(:last-child) .bracket-match::after {
  content: '';
  position: absolute;
  width: 28px;
  right: -29px;
  top: 50%;
  border-top: 2px solid rgba(var(--v-theme-primary), 0.3);
}
.match-heading {
  padding: 8px 12px;
  min-height: 34px;
}
.bracket-slot {
  padding: 10px 12px;
  border-top: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}
.slot-controls {
  display: flex;
  gap: 8px;
  width: 100%;
}
.seed-input {
  flex: 0 0 72px;
}
.slot-controls :deep(.v-select) {
  min-width: 0;
}
.winner {
  background: rgba(var(--v-theme-success), 0.09);
}
.participant-name {
  flex: 1;
  min-width: 0;
}
.participant-name strong,
.participant-name small {
  display: block;
  overflow-wrap: anywhere;
}
.seed-badge {
  color: rgb(var(--v-theme-primary));
  font-size: 0.8rem;
  min-width: 25px;
}
.match-result {
  padding: 8px 12px;
  border-top: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
}
</style>
