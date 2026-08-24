<script lang="ts" setup>
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { leagueTabFromLocation, type LeagueTab } from '@/navigation/leagueTabs'
import ActivityView from '@/views/ActivityView.vue'
import DraftView from '@/views/DraftView.vue'
import HomeView from '@/views/HomeView.vue'
import LeagueSetupView from '@/views/LeagueSetupView.vue'
import MatchupView from '@/views/MatchupView.vue'
import MyTeamView from '@/views/MyTeamView.vue'
import PlayoffsView from '@/views/PlayoffsView.vue'
import PokemonView from '@/views/PokemonView.vue'
import RosterView from '@/views/RosterView.vue'
import ScheduleView from '@/views/ScheduleView.vue'
import TeamsView from '@/views/TeamsView.vue'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

const tabComponents: Record<LeagueTab, unknown> = {
  home: HomeView,
  team: MyTeamView,
  manage: RosterView,
  activity: ActivityView,
  matchup: MatchupView,
  teams: TeamsView,
  schedule: ScheduleView,
  playoffs: PlayoffsView,
  draft: DraftView,
  setup: LeagueSetupView,
  pokemon: PokemonView,
}

if (!authStore.isAuthenticated) router.replace('/join')

const requestedTab = computed(() => leagueTabFromLocation(route.path, route.query.tab))
const activeTab = computed<LeagueTab>(() => {
  const tab = requestedTab.value
  if ((tab === 'setup' || tab === 'pokemon') && !authStore.isAdmin) return 'home'
  return tab
})
const activeTabComponent = computed(() => tabComponents[activeTab.value])
</script>

<template>
  <v-container fluid class="league-view">
    <section class="league-tab-panel">
      <component :is="activeTabComponent" :key="activeTab" />
    </section>
  </v-container>
</template>

<style scoped>
.league-view {
  display: flex;
  flex-direction: column;
  gap: 0;
  overflow: visible;
  padding: 0;
}
</style>
