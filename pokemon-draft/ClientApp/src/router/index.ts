import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: () => import('../views/LeagueView.vue'),
    },
    {
      path: '/register',
      name: 'register',
      component: () => import('../views/RegisterView.vue'),
    },
    {
      path: '/league/create',
      name: 'league-create',
      component: () => import('../views/CreateLeagueView.vue'),
    },
    {
      path: '/league/setup',
      name: 'league-setup',
      component: () => import('../views/LeagueView.vue'),
    },
    {
      path: '/league',
      name: 'league',
      component: () => import('../views/LeagueView.vue'),
    },
    {
      path: '/pokemon',
      name: 'pokemon',
      component: () => import('../views/LeagueView.vue'),
    },
    {
      path: '/draft',
      name: 'draft',
      component: () => import('../views/LeagueView.vue'),
    },
    {
      path: '/team',
      name: 'team',
      component: () => import('../views/LeagueView.vue'),
    },
    {
      path: '/schedule',
      name: 'schedule',
      component: () => import('../views/LeagueView.vue'),
    },
    {
      path: '/settings',
      name: 'settings',
      component: () => import('../views/SettingsView.vue'),
    },
    {
      path: '/teams',
      name: 'teams',
      component: () => import('../views/LeagueView.vue'),
    },
    {
      path: '/playoffs',
      name: 'playoffs',
      component: () => import('../views/LeagueView.vue'),
    },
    {
      path: '/auth/discord/callback',
      name: 'discord-callback',
      component: () => import('../views/DiscordCallbackView.vue'),
    },
    {
      path: '/my-leagues',
      name: 'my-leagues',
      component: () => import('../views/MyLeaguesView.vue'),
    },
    {
      path: '/team/manage',
      name: 'team-manage',
      component: () => import('../views/RosterView.vue'),
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('../views/LoginView.vue'),
    }
  ],
})

export default router
