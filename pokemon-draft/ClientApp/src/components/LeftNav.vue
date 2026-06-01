<script setup lang="ts">
import { useAuthStore } from '@/stores/auth'
import { NavigationButton } from '@/types/NavigationButton'
import { reactive, ref } from 'vue'

const authStore = useAuthStore()

const pageData = reactive({
  navigationButtons: [
    { label: 'Home', icon: 'mdi-home', route: '/', requiresAuth: true, adminOnly: false },
    { label: 'My Team', icon: 'mdi-account', route: '/team', requiresAuth: true, adminOnly: false },
    {
      label: 'All Teams',
      icon: 'mdi-account-group',
      route: '/teams',
      requiresAuth: true,
      adminOnly: false,
    },
    {
      label: 'Schedule',
      icon: 'mdi-calendar',
      route: '/schedule',
      requiresAuth: true,
      adminOnly: false,
    },
    {
      label: 'Playoffs',
      icon: 'mdi-trophy',
      route: '/playoffs',
      requiresAuth: true,
      adminOnly: false,
    },
    {
      label: 'Draft Board',
      icon: 'mdi-view-dashboard-variant',
      route: '/draft',
      requiresAuth: true,
      adminOnly: false,
    },
    // Admin-only buttons
    {
      label: 'League Setup',
      icon: 'mdi-cog',
      route: '/league/setup',
      requiresAuth: true,
      adminOnly: true,
    },
    {
      label: 'Point Values',
      icon: 'mdi-chart-bar',
      route: '/pokemon',
      requiresAuth: true,
      adminOnly: true,
    },
  ] as NavigationButton[],
})

const rail = ref(true)

function expandOrCollapse() {
  // on click, if the drawer is expanded, collapse it. If it's collapsed, expand it.
  rail.value = !rail.value
}
</script>

<template>
  <v-navigation-drawer app permanent :rail="rail" class="left-nav">
    <v-list v-if="authStore.isAuthenticated" nav>
      <v-list-item
        v-for="button in pageData.navigationButtons"
        :prepend-icon="button.icon"
        :title="button.label"
        :to="button.route"
      >
      </v-list-item>
    </v-list>
    <v-btn
      icon="mdi-chevron-left"
      @click="expandOrCollapse"
      variant="text"
      :class="'toggle-btn' + (rail == true ? ' rotate-180' : '')"
    >
    </v-btn>
  </v-navigation-drawer>
</template>

<style scoped>
.v-list {
  padding: 0;
}

.v-list-item {
  padding-left: 16px;
}

.toggle-btn {
  position: fixed;
  bottom: 16px;
  right: 2px;
  transition: transform 0.3s ease;
}

.toggle-btn.rotate-180 {
  transform: rotate(180deg);
}
</style>
