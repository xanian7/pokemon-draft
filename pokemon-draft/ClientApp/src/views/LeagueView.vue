<script lang="ts" setup>
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
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

type LeagueTab =
  | 'home'
  | 'team'
  | 'manage'
  | 'matchup'
  | 'teams'
  | 'schedule'
  | 'playoffs'
  | 'draft'
  | 'setup'
  | 'pokemon'

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
  { label: 'Manage Team', value: 'manage', icon: 'mdi-account-edit', component: RosterView },
  { label: 'Matchup', value: 'matchup', icon: 'mdi-sword-cross', component: MatchupView },
  { label: 'All Teams', value: 'teams', icon: 'mdi-account-group', component: TeamsView },
  { label: 'Schedule', value: 'schedule', icon: 'mdi-calendar', component: ScheduleView },
  { label: 'Playoffs', value: 'playoffs', icon: 'mdi-trophy', component: PlayoffsView },
  { label: 'Draft Board', value: 'draft', icon: 'mdi-view-dashboard-variant', component: DraftView },
  { label: 'League Setup', value: 'setup', icon: 'mdi-cog', component: LeagueSetupView, adminOnly: true },
  { label: 'Point Values', value: 'pokemon', icon: 'mdi-chart-bar', component: PokemonView, adminOnly: true },
]

if (!authStore.isAuthenticated) router.replace('/join')

const visibleTabs = computed(() => tabs.filter((tab) => !tab.adminOnly || authStore.isAdmin))
const activeTabComponent = computed(
  () => visibleTabs.value.find((tab) => tab.value === activeTab.value)?.component,
)

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
  else if (route.path === '/team/manage') tab = 'manage'
  else if (route.path === '/matchup') tab = 'matchup'
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

</script>

<template>
  <v-container fluid class="league-view">
    <v-tabs v-model="activeTab" class="league-tabs" density="comfortable" show-arrows>
      <v-tab v-for="tab in visibleTabs" :key="tab.value" :value="tab.value">
        <v-icon :icon="tab.icon" start />
        {{ tab.label }}
      </v-tab>
    </v-tabs>

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

.league-tabs {
  flex: 0 0 auto;
  margin: 10px 14px 8px;
  padding: 5px;
  width: calc(100% - 28px);
  min-height: 54px;
  border: 1px solid var(--border-color);
  border-radius: 16px;
  background: rgba(15, 20, 35, 0.78);
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.2);
  z-index: 20;
}

.league-tabs :deep(.v-tab) {
  min-width: max-content;
  min-height: 42px;
  padding-inline: 15px;
  border-radius: 11px;
  color: var(--text-muted);
  font-size: 0.82rem;
  font-weight: 700;
  text-transform: none;
  letter-spacing: 0;
}

.league-tabs :deep(.v-tab:hover) {
  color: var(--text);
  background: rgba(255, 255, 255, 0.04);
}

.league-tabs :deep(.v-tab.v-tab--selected) {
  color: #fff;
  background: linear-gradient(135deg, rgba(var(--primary-rgb), 0.92), rgba(91, 75, 220, 0.92));
  box-shadow: 0 7px 18px rgba(var(--primary-rgb), 0.24);
}

.league-tabs :deep(.v-tab__slider) {
  display: none;
}

.league-tab-panel {
  flex: 1 1 auto;
  overflow: visible;
  padding-top: 0;
}

.league-tab-panel :deep(> .v-container) {
  padding-top: 0;
}

@media (max-width: 720px) {
  .league-tabs {
    margin: 8px 8px 6px;
    width: calc(100% - 16px);
    min-height: 50px;
    padding: 4px;
    border-radius: 14px;
  }

  .league-tabs :deep(.v-tab) {
    min-width: 48px;
    min-height: 40px;
    padding-inline: 11px;
  }

  .league-tabs :deep(.v-tab__content) {
    font-size: 0.7rem;
    gap: 4px;
  }

  .league-tabs :deep(.v-icon) {
    font-size: 17px;
    margin-inline-end: 2px !important;
  }

}
</style>
