<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { apiGet, apiPut } from '@/services/api'
import PageHeader from '@/components/PageHeader.vue'
import PlayoffBracketStage from '@/components/PlayoffBracketStage.vue'
import {
  allRounds,
  clearDownstream,
  createPlayoffs,
  newMatch,
  newRound,
  removeDanglingSources,
  validateBracket,
  type BracketConfiguration,
  type BracketResponse,
  type BracketPlayer,
  type BracketMatch,
  type BracketRound,
} from '@/utils/playoffBracket'

const props = defineProps<{ players: BracketPlayer[]; playoffSpots: number; seasonOver: boolean }>()
const auth = useAuthStore()
const configuration = ref<BracketConfiguration>({ playIn: [], playoffs: [] })
const revision = ref<string | null>(null)
const saved = ref('')
const editing = ref(false)
const loading = ref(true)
const saving = ref(false)
const loadError = ref('')
const saveError = ref('')
const success = ref(false)
const bracketSize = ref(2 ** Math.ceil(Math.log2(Math.min(64, Math.max(2, props.playoffSpots)))))
const dirty = computed(() => editing.value && JSON.stringify(configuration.value) !== saved.value)
const validation = computed(() => validateBracket(configuration.value, props.players))
const canEdit = computed(() => auth.isAdmin && props.seasonOver)
const confirmMessage = ref('')
let confirmedAction: (() => void) | null = null
function confirmAction(message: string, action: () => void) {
  confirmMessage.value = message
  confirmedAction = action
}
function acceptAction() {
  confirmedAction?.()
  confirmedAction = null
  confirmMessage.value = ''
}
async function load() {
  loading.value = true
  loadError.value = ''
  const result = await apiGet<BracketResponse>('/leagues/' + auth.leagueCode + '/playoff-bracket')
  if (result.error !== null) loadError.value = result.error
  else {
    configuration.value = result.data.configuration ?? { playIn: [], playoffs: [] }
    revision.value = result.data.revision
    saved.value = JSON.stringify(configuration.value)
    editing.value = false
    saveError.value = ''
  }
  loading.value = false
}
function startEditing() {
  if (!configuration.value.playoffs.length)
    configuration.value.playoffs = createPlayoffs(
      bracketSize.value,
      Math.min(64, Math.max(2, props.playoffSpots)),
    )
  editing.value = true
  success.value = false
}
function cancel() {
  const discard = () => {
    configuration.value = JSON.parse(saved.value)
    editing.value = false
    saveError.value = ''
  }
  if (dirty.value) confirmAction('Discard your unsaved bracket changes?', discard)
  else discard()
}
function rebuild() {
  confirmAction(
    'Replace the playoff rounds and clear their results? Your play-in rounds will be kept.',
    () => {
      configuration.value.playoffs = createPlayoffs(bracketSize.value)
    },
  )
}
function changeMatch(id: string) {
  const match = allRounds(configuration.value)
    .flatMap((round) => round.matches)
    .find((match) => match.id === id)
  if (match) match.winner = null
  clearDownstream(configuration.value, id)
}
function changeResult(match: BracketMatch, winner: number | null) {
  match.winner = winner ?? null
  clearDownstream(configuration.value, match.id)
}
function addRound(rounds: BracketRound[], label: string) {
  if (rounds.length < 8) rounds.push(newRound(label + ' ' + (rounds.length + 1)))
}
function removeRound(rounds: BracketRound[], id: string) {
  confirmAction('Remove this round? Later slots that depend on it will be cleared.', () => {
    rounds.splice(
      rounds.findIndex((round) => round.id === id),
      1,
    )
    removeDanglingSources(configuration.value)
  })
}
function addMatch(id: string) {
  const round = allRounds(configuration.value).find((round) => round.id === id)
  if (round && round.matches.length < 32) round.matches.push(newMatch())
}
function removeMatch(roundId: string, matchId: string) {
  confirmAction('Remove this match? Later slots that depend on it will be cleared.', () => {
    const round = allRounds(configuration.value).find((round) => round.id === roundId)!
    round.matches = round.matches.filter((match) => match.id !== matchId)
    removeDanglingSources(configuration.value)
  })
}
async function save() {
  if (!canEdit.value || saving.value || validation.value) return
  saving.value = true
  saveError.value = ''
  const result = await apiPut<BracketResponse>('/leagues/' + auth.leagueCode + '/playoff-bracket', {
    adminPin: auth.pin,
    revision: revision.value,
    configuration: configuration.value,
  })
  if (result.error !== null) saveError.value = result.error
  else {
    revision.value = result.data.revision
    saved.value = JSON.stringify(configuration.value)
    editing.value = false
    success.value = true
  }
  saving.value = false
}
function reload() {
  if (dirty.value)
    confirmAction('Discard your changes and load the latest saved bracket?', () => {
      void load()
    })
  else void load()
}
function beforeUnload(event: BeforeUnloadEvent) {
  if (dirty.value) {
    event.preventDefault()
    event.returnValue = ''
  }
}
onBeforeRouteLeave(
  () => !dirty.value || window.confirm('Leave without saving your bracket changes?'),
)
onMounted(() => {
  void load()
  window.addEventListener('beforeunload', beforeUnload)
})
onBeforeUnmount(() => window.removeEventListener('beforeunload', beforeUnload))
</script>

<template>
  <div variant="outlined" class="postseason-card">

      <PageHeader eyebrow="Postseason" title="Playoff bracket">
        <template #actions>
          <v-chip v-if="dirty" color="warning" size="small">Unsaved changes</v-chip>
          <v-btn v-if="!loading" variant="text" :disabled="saving" @click="reload">Reload</v-btn>
          <template v-if="editing">
            <v-btn variant="text" :disabled="saving" @click="cancel">Cancel</v-btn>
            <v-btn
              color="primary"
              :loading="saving"
              :disabled="!!validation || !canEdit || saving"
              prepend-icon="mdi-content-save-outline"
              @click="save"
              >Save bracket</v-btn
            >
          </template>
          <v-btn
            v-else-if="canEdit && !loading && !loadError"
            color="primary"
            prepend-icon="mdi-pencil-outline"
            @click="startEditing"
            >{{ revision ? 'Edit bracket' : 'Create bracket' }}</v-btn
          >
        </template>
      </PageHeader>
      <v-progress-linear v-if="loading" indeterminate class="mt-4" />
      <v-alert v-else-if="loadError" type="error" variant="tonal" class="mt-4">{{
        loadError
      }}</v-alert>
      <template v-else>
        <v-alert v-if="saveError" type="error" variant="tonal" class="mt-4">{{
          saveError
        }}</v-alert>
        <v-alert v-if="!seasonOver" type="info" variant="tonal" class="mt-4"
          >Bracket editing opens once the regular season is complete.</v-alert
        >
        <div v-if="!configuration.playoffs.length && !editing" class="empty-bracket">
          <v-icon icon="mdi-tournament" size="44" color="primary" />
          <h3>The postseason bracket has not been set</h3>
          <p>
            A commissioner can place the seeds and configure the play-in tournament after the
            regular season.
          </p>
        </div>
        <fieldset v-else :disabled="saving" class="bracket-editor" :class="{ saving }">
          <div v-if="editing" class="bracket-settings">
            <p>
              Assign players and seeds manually. Choose an earlier match’s winner or loser to
              connect rounds. Empty slots stay TBD; explicit byes advance automatically. Changing a
              matchup clears dependent results.
            </p>
            <div class="rebuild-controls">
              <v-select
                v-model="bracketSize"
                :items="[2, 4, 8, 16, 32, 64]"
                label="Playoff bracket size"
                density="compact"
                hide-details
              />
              <v-btn variant="tonal" @click="rebuild">Rebuild playoffs</v-btn>
            </div>
            <v-alert v-if="validation" type="warning" variant="tonal">{{ validation }}</v-alert>
          </div>
          <PlayoffBracketStage
            v-if="editing || configuration.playIn.length"
            title="Play-in tournament"
            :rounds="configuration.playIn"
            :configuration="configuration"
            :players="players"
            :editing="editing"
            @change="changeMatch"
            @result="changeResult"
            @add-round="addRound(configuration.playIn, 'Play-in round')"
            @remove-round="removeRound(configuration.playIn, $event)"
            @add-match="addMatch"
            @remove-match="removeMatch"
          />
          <PlayoffBracketStage
            title="Championship playoffs"
            :rounds="configuration.playoffs"
            :configuration="configuration"
            :players="players"
            :editing="editing"
            @change="changeMatch"
            @result="changeResult"
            @add-round="addRound(configuration.playoffs, 'Playoff round')"
            @remove-round="removeRound(configuration.playoffs, $event)"
            @add-match="addMatch"
            @remove-match="removeMatch"
          />
        </fieldset>
      </template>
  </div>
  <v-snackbar v-model="success" color="success" :timeout="3000">
    Bracket saved for the league.
  </v-snackbar>
  <v-dialog
    :model-value="!!confirmMessage"
    max-width="460"
    @update:model-value="confirmMessage = ''"
  >
    <v-card title="Confirm bracket change">
      <v-card-text>{{ confirmMessage }}</v-card-text>
      <v-card-actions
        ><v-spacer /><v-btn @click="confirmMessage = ''">Keep editing</v-btn
        ><v-btn color="primary" @click="acceptAction">Continue</v-btn></v-card-actions
      >
    </v-card>
  </v-dialog>
</template>

<style scoped>
.rebuild-controls {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}
.bracket-settings {
  margin-top: 20px;
  display: grid;
  gap: 16px;
}
.bracket-settings p,
.empty-bracket p {
  color: rgb(var(--v-theme-on-surface), 0.65);
}
.rebuild-controls {
  max-width: 460px;
}
.rebuild-controls :deep(.v-select) {
  min-width: 180px;
}
.bracket-editor {
  border: 0;
  min-width: 0;
}
.saving {
  pointer-events: none;
  opacity: 0.7;
}
.empty-bracket {
  text-align: center;
  padding: 40px 16px;
  display: grid;
  justify-items: center;
  gap: 12px;
}
.postseason-card {
  margin-bottom: 24px;
  margin-top: 20px;
}
</style>
