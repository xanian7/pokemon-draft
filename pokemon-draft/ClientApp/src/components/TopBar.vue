<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import MasterBall from '../../public/Master-Ball.png'
import ProfilePopup from '@/components/ProfilePopup.vue'
import SettingsPopup from '@/components/SettingsPopup.vue'
import { leagueTabs, leagueTabFromLocation, leagueWorkflowPaths } from '@/navigation/leagueTabs'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

const showLeagueTabs = computed(
  () => authStore.isAuthenticated && leagueWorkflowPaths.has(route.path),
)
const visibleTabs = computed(() => leagueTabs.filter((tab) => !tab.adminOnly || authStore.isAdmin))
const activeTab = computed({
  get: () => {
    const tab = leagueTabFromLocation(route.path, route.query.tab)
    return (tab === 'setup' || tab === 'pokemon') && !authStore.isAdmin ? 'home' : tab
  },
  set: (tab) => router.push({ path: '/league', query: { tab } }),
})
</script>

<template>
  <v-app-bar app :height="showLeagueTabs ? 94 : 64" class="top-bar" flat>
    <div class="top-bar__content">
      <div class="top-bar__inner">
        <div class="brand">
          <span class="brand__mark">
            <v-img :src="MasterBall" width="34" height="34" />
          </span>
          <span class="brand__copy">
            <strong>PokéDraft</strong>
            <small>League command center</small>
          </span>
        </div>
        <div class="account-actions">
          <SettingsPopup v-if="authStore.playerId" />
          <ProfilePopup />
        </div>
      </div>

      <v-tabs
        v-if="showLeagueTabs"
        v-model="activeTab"
        class="league-tabs"
        density="compact"
        show-arrows
      >
        <v-tab v-for="tab in visibleTabs" :key="tab.value" :value="tab.value">
          <v-icon :icon="tab.icon" start />
          {{ tab.label }}
        </v-tab>
      </v-tabs>
    </div>
  </v-app-bar>
</template>

<style scoped>
.top-bar {
  background: var(--top-bar-bg) !important;
  border-bottom: 1px solid var(--border-color) !important;
}

.top-bar__content {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
}

.top-bar__inner {
  width: 100%;
  height: 64px;
  flex: 0 0 64px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 18px;
}

.brand {
  display: flex;
  align-items: center;
  gap: 11px;
}

.brand__mark {
  width: 42px;
  height: 42px;
  display: grid;
  place-items: center;
  border-radius: 13px;
  background: linear-gradient(145deg, rgba(var(--primary-rgb), 0.24), rgba(42, 182, 255, 0.1));
  border: 1px solid rgba(var(--primary-rgb), 0.26);
  box-shadow: inset 0 1px rgba(27, 177, 114, 0.08);
}

.brand__copy {
  display: flex;
  flex-direction: column;
  line-height: 1.15;
}

.brand__copy strong {
  font-size: 1.02rem;
  font-weight: 800;
  letter-spacing: -0.025em;
}

.brand__copy small {
  margin-top: 3px;
  color: var(--text-muted);
  font-size: 0.68rem;
  letter-spacing: 0.04em;
}

.account-actions {
  display: flex;
  align-items: center;
  gap: 2px;
  margin-right: -12px;
}

.league-tabs {
  padding-left: 16px;
  padding-right: 16px;
  width: 100%;
  background: var(--top-bar-bg);
}

.league-tabs :deep(.v-slide-group__content) {
  gap: 2px;
}

.league-tabs :deep(.v-tab) {
  min-width: max-content;
  height: 30px;
  color: var(--text-muted);
  font-size: 0.8rem;
  font-weight: 700;
  text-transform: none;
  letter-spacing: 0;
}

.league-tabs :deep(.v-tab:hover) {
  color: var(--text);
  background: var(--top-bar-tab-hover);
}

.league-tabs :deep(.v-tab.v-tab--selected) {
  color: var(--primary-bright);
  background: rgba(var(--primary-rgb), 0.1);
}

.league-tabs :deep(.v-tab__slider) {
  height: 3px;
  color: var(--primary);
}

@media (max-width: 520px) {
  .top-bar__inner {
    padding-inline: 12px;
  }

  .brand__copy small {
    display: none;
  }

  .brand__mark {
    width: 38px;
    height: 38px;
    border-radius: 12px;
  }

  .league-tabs {
    padding-inline: 4px;
  }

  .league-tabs :deep(.v-tab) {
    min-width: 48px;
    padding-inline: 10px;
  }

  .league-tabs :deep(.v-tab__content) {
    gap: 4px;
    font-size: 0.7rem;
  }

  .league-tabs :deep(.v-icon) {
    margin-inline-end: 2px !important;
    font-size: 17px;
  }
}
</style>

