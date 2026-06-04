<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { RouterLink, RouterView, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import AppIcon from '@/components/AppIcon.vue'
import ConnectionBanner from '@/components/ConnectionBanner.vue'
import { useSignalR } from '@/services/signalr'
import { isApiLoading } from '@/services/apiLoading'
import { mdiPokeball, mdiLogout, mdiCog, mdiTrophy } from '@mdi/js'
import LeftNav from '@/components/LeftNav.vue'
import TopBar from './components/TopBar.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'

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
  router.push('/join')
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
      <!-- <LeftNav /> -->
      <v-main scrollable>
        <RouterView />
      </v-main>
      <ConnectionBanner />
      <v-overlay
        :model-value="isApiLoading"
        class="api-loading-overlay"
        persistent
        scrim="rgba(0, 0, 0, 0.45)"
      >
        <PokeballLoader variant="page" label="Loading…" />
      </v-overlay>
    </v-layout>
  </v-app>
</template>

<style>
:root {
  --primary: #0facf5;
  --secondary: #f50f0f;
  --text: #e8e8e8;
  --text-muted: #888;
  --bg: #1a1a1a;
  --card-bg: #222222;
  --input-bg: #111111;
  --border-color: #363636;
  --primary-hover-bg: #282c30;
  --secondary-hover-bg: #331e1e;
  --draft-card-nonuser-bg: #35697e;
  color-scheme: light dark;
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
