<script lang="ts" setup>
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import DraftView from '@/views/DraftView.vue'
import HomeView from '@/views/HomeView.vue'
import LeagueSetupView from '@/views/LeagueSetupView.vue'
import MyTeamView from '@/views/MyTeamView.vue'
import PlayoffsView from '@/views/PlayoffsView.vue'
import PokemonView from '@/views/PokemonView.vue'
import ScheduleView from '@/views/ScheduleView.vue'
import TeamsView from '@/views/TeamsView.vue'

type LeagueTab = 'home' | 'team' | 'teams' | 'schedule' | 'playoffs' | 'draft' | 'setup' | 'pokemon'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

const tabs: Array<{
  label: string
  value: LeagueTab
  icon: string
  component: unknown
  adminOnly?: boolean
}> = [
  { label: 'Home', value: 'home', icon: 'mdi-home', component: HomeView },
  { label: 'My Team', value: 'team', icon: 'mdi-account', component: MyTeamView },
  { label: 'All Teams', value: 'teams', icon: 'mdi-account-group', component: TeamsView },
  { label: 'Schedule', value: 'schedule', icon: 'mdi-calendar', component: ScheduleView },
  { label: 'Playoffs', value: 'playoffs', icon: 'mdi-trophy', component: PlayoffsView },
  { label: 'Draft Board', value: 'draft', icon: 'mdi-view-dashboard-variant', component: DraftView },
  { label: 'League Setup', value: 'setup', icon: 'mdi-cog', component: LeagueSetupView, adminOnly: true },
  { label: 'Point Values', value: 'pokemon', icon: 'mdi-chart-bar', component: PokemonView, adminOnly: true },
]

if (!authStore.isAuthenticated) router.replace('/join')

const visibleTabs = computed(() => tabs.filter((tab) => !tab.adminOnly || authStore.isAdmin))

function isLeagueTab(value: unknown): value is LeagueTab {
  return tabs.some((tab) => tab.value === value)
}

function canAccessTab(tabValue: LeagueTab) {
  const tab = tabs.find((item) => item.value === tabValue)
  return Boolean(tab && (!tab.adminOnly || authStore.isAdmin))
}

function tabFromRoute(): LeagueTab {
  const tabQuery = Array.isArray(route.query.tab) ? route.query.tab[0] : route.query.tab
  let tab: LeagueTab = 'draft'
  if (isLeagueTab(tabQuery)) tab = tabQuery
  else if (route.path === '/') tab = 'home'
  else if (route.path === '/team') tab = 'team'
  else if (route.path === '/teams') tab = 'teams'
  else if (route.path === '/schedule') tab = 'schedule'
  else if (route.path === '/playoffs') tab = 'playoffs'
  else if (route.path === '/league/setup') tab = 'setup'
  else if (route.path === '/pokemon') tab = 'pokemon'

  return canAccessTab(tab) ? tab : 'draft'
}

const activeTab = ref<LeagueTab>(tabFromRoute())
let syncingFromRoute = false

function canonicalizeLeagueRoute(tab: LeagueTab) {
  const tabQuery = Array.isArray(route.query.tab) ? route.query.tab[0] : route.query.tab
  if (route.path === '/league' && tabQuery === tab) return
  router.replace({ path: '/league', query: { tab } })
}

watch(
  () => route.fullPath,
  () => {
    syncingFromRoute = true
    activeTab.value = tabFromRoute()
    syncingFromRoute = false
    canonicalizeLeagueRoute(activeTab.value)
  },
  { immediate: true },
)

watch(activeTab, (tab) => {
  if (syncingFromRoute) return
  canonicalizeLeagueRoute(tab)
})

const leagueTitle = computed(() => authStore.leagueName || 'League')
</script>

<template>
  <v-container fluid class="league-view">
    <v-tabs v-model="activeTab" class="league-tabs" density="comfortable" bg-color="var(--card-bg)">
      <v-tab v-for="tab in visibleTabs" :key="tab.value" :value="tab.value">
        <v-icon :icon="tab.icon" start />
        {{ tab.label }}
      </v-tab>
    </v-tabs>

    <section
      v-for="tab in visibleTabs"
      :key="tab.value"
      class="league-tab-panel"
      v-show="activeTab === tab.value"
    >
      <component :is="tab.component" />
    </section>
  </v-container>
</template>

<style scoped>
.league-view {
  display: flex;
  flex-direction: column;
  gap: 0;
  padding: 0;
}

.league-tabs {
  border-bottom: 1px solid var(--border-color);
  padding-bottom: 0;
}

.league-tab-panel {
  min-height: 0;
  max-height: calc(100vh - 108px);
  overflow: auto;
  padding-top: 16px;
}

.league-tab-panel :deep(> .v-container) {
  padding-top: 0;
}
</style>
