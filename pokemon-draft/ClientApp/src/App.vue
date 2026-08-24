<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { RouterLink, RouterView, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import AppIcon from '@/components/AppIcon.vue'
import { useSignalR } from '@/services/signalr'
import { isApiLoading } from '@/services/apiLoading'
import { mdiPokeball, mdiLogout, mdiCog, mdiTrophy } from '@mdi/js'
import TopBar from './components/TopBar.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import SnackbarQueue from '@/components/SnackbarQueue.vue'

const authStore = useAuthStore()
const router = useRouter()
const { disconnect } = useSignalR()

const appVersion = (import.meta.env.VITE_GIT_SHA ?? 'dev').slice(0, 7)

const menuOpen = ref(false)
const menuRef = ref<HTMLElement | null>(null)

onMounted(async () => {
  await router.isReady()
  const publicPaths = ['/login', '/auth/discord/callback']
  const currentPath = router.currentRoute.value.path
  if (!authStore.isAuthenticated && !publicPaths.some(p => currentPath.startsWith(p))) {
    router.push('/login')
  }
})

const avatarInitials = computed(() => {
  const name = authStore.playerName
  if (!name) return '?'
  return name
    .split(' ')
    .map((w) => w[0])
    .join('')
    .slice(0, 2)
    .toUpperCase()
})

function toggleMenu() {
  menuOpen.value = !menuOpen.value
}

function goToSettings() {
  menuOpen.value = false
  router.push('/settings')
}

async function logout() {
  menuOpen.value = false
  await disconnect()
  authStore.signOut()
  router.push('/login')
}

function handleOutsideClick(e: MouseEvent) {
  if (menuRef.value && !menuRef.value.contains(e.target as Node)) {
    menuOpen.value = false
  }
}

onMounted(() => document.addEventListener('click', handleOutsideClick, true))
onUnmounted(() => document.removeEventListener('click', handleOutsideClick, true))
</script>

<template>
  <v-app>
    <v-layout>
      <TopBar />
      <v-main>
        <RouterView />
      </v-main>
      <SnackbarQueue />
      <v-overlay
        :model-value="isApiLoading"
        class="api-loading-overlay"
        persistent
        scrim="rgba(0, 0, 0, 0.1)"
      >
        <PokeballLoader variant="page" label="Loading…" />
      </v-overlay>
    </v-layout>
  </v-app>
</template>

<style>
:root {
  --primary: #7c6cff;
  --primary-rgb: 124, 108, 255;
  --primary-bright: #a99fff;
  --secondary: #ff5c7a;
  --secondary-rgb: 255, 92, 122;
  --success: #35d39a;
  --warning: #ffca62;
  --text: #f4f6ff;
  --text-muted: #9aa4bd;
  --text-subtle: #6f7890;
  --bg: #111111;
  --bg-elevated: #4e4e4e;
  --card-bg: rgba(0, 0, 0, 0.82);
  --card-bg-solid: #1b1a1f;
  --input-bg: rgb(0, 0, 0);
  --border-color: rgb(56, 56, 56);
  --border-strong: rgba(71, 71, 71, 0.24);
  --primary-hover-bg: rgba(var(--primary-rgb), 0.12);
  --secondary-hover-bg: rgba(var(--secondary-rgb), 0.12);
  --draft-card-nonuser-bg: #263d65;
  --top-bar-bg: #000000;
  --top-bar-tab-hover: rgba(255, 255, 255, 0.04);
  --surface-shadow: 0 0px 0px rgba(0, 0, 0, 0.24);
  --surface-shadow-hover: 0 24px 60px rgba(0, 0, 0, 0.34);
  --radius-sm: 10px;
  --radius-md: 16px;
  --radius-lg: 22px;
  color-scheme: dark;
}

:root[data-app-theme='light'] {
  --primary: #5a48e8;
  --primary-rgb: 90, 72, 232;
  --primary-bright: #4935d4;
  --secondary: #d9365b;
  --secondary-rgb: 217, 54, 91;
  --success: #16845d;
  --warning: #a56500;
  --text: #17191f;
  --text-muted: #626b7d;
  --text-subtle: #7b8495;
  --bg: #f5f7fb;
  --bg-elevated: #e9edf5;
  --card-bg: rgba(255, 255, 255, 0.94);
  --card-bg-solid: #ffffff;
  --input-bg: #ffffff;
  --border-color: #d9deea;
  --border-strong: rgba(33, 42, 64, 0.18);
  --primary-hover-bg: rgba(var(--primary-rgb), 0.09);
  --secondary-hover-bg: rgba(var(--secondary-rgb), 0.09);
  --draft-card-nonuser-bg: #dbe7f8;
  --top-bar-bg: rgba(255, 255, 255, 0.98);
  --top-bar-tab-hover: rgba(20, 25, 40, 0.05);
  --surface-shadow: 0 0px 0px rgba(33, 42, 64, 0.12);
  --surface-shadow-hover: 0 18px 44px rgba(33, 42, 64, 0.16);
  color-scheme: light;
}

html {
  background: var(--bg);
}

/* body {
  background:
    radial-gradient(circle at 8% -10%, rgba(var(--primary-rgb), 0.18), transparent 34rem),
    radial-gradient(circle at 94% 10%, rgba(42, 182, 255, 0.1), transparent 30rem),
    var(--bg);
} */

#app,
.v-application {
  background: transparent !important;
}
</style>

<style scoped>
.app-shell {
  height: 100vh;
  overflow: hidden;
}

.main-scroll {
  min-height: 0;
  overflow-y: auto;
}

.api-loading-overlay {
  display: flex;
  align-items: center;
  justify-content: center;
}
</style>
