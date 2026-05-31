<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { RouterLink, RouterView, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import AppIcon from '@/components/AppIcon.vue'
import { mdiPokeball, mdiLogout, mdiCog, mdiTrophy } from '@mdi/js'

const authStore = useAuthStore()
const router = useRouter()

const appVersion = (import.meta.env.VITE_GIT_SHA ?? 'dev').slice(0, 7)

const menuOpen = ref(false)
const menuRef = ref<HTMLElement | null>(null)

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

function logout() {
  menuOpen.value = false
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
  <v-app-bar>
      <v-app-bar-title>
      <RouterLink to="/" class="logo">
        <AppIcon :path="mdiPokeball" :size="22" class="logo-icon" />
        PokéDraft
      </RouterLink>
      </v-app-bar-title>
      <v-divider vertical />
      <span class="league-name">{{ authStore.leagueName }}</span>

      <template v-slot:append>
        <!-- Google identity (always visible when signed in with Google) -->
        <RouterLink
          v-if="authStore.isSignedInWithGoogle && !authStore.isAuthenticated"
          to="/my-leagues"
          class="my-leagues-btn"
        >
          <img
            v-if="authStore.googleUser?.picture"
            :src="authStore.googleUser.picture"
            class="google-avatar"
            alt=""
          />
          <span>My Leagues</span>
        </RouterLink>

        <template v-if="authStore.isAuthenticated">
          <!-- Avatar button -->
          <div ref="menuRef" class="user-menu">
            <button class="avatar-btn" :title="authStore.playerName ?? ''" @click="toggleMenu">
              <!-- Prefer Google avatar when available -->
              <img
                v-if="authStore.googleUser?.picture"
                :src="authStore.googleUser.picture"
                :alt="authStore.playerName ?? ''"
                class="avatar-img"
              />
              <img
                v-else-if="authStore.teamImageUrl"
                :src="authStore.teamImageUrl"
                :alt="authStore.playerName ?? ''"
                class="avatar-img"
              />
              <span v-else class="avatar-initials">{{ avatarInitials }}</span>
            </button>

            <!-- Dropdown -->
            <Transition name="menu">
              <div v-if="menuOpen" class="user-dropdown">
                <div class="dropdown-header">
                  <span class="dropdown-name">{{ authStore.playerName }}</span>
                  <span v-if="authStore.teamName" class="dropdown-team">{{ authStore.teamName }}</span>
                  <span v-if="authStore.isSignedInWithGoogle" class="dropdown-google">
                    {{ authStore.googleUser?.email }}
                  </span>
                </div>
                <div class="dropdown-divider" />
                <RouterLink
                  v-if="authStore.isSignedInWithGoogle"
                  to="/my-leagues"
                  class="dropdown-item"
                  @click="menuOpen = false"
                >
                  <AppIcon :path="mdiTrophy" :size="16" />
                  My Leagues
                </RouterLink>
                <button class="dropdown-item" @click="goToSettings">
                  <AppIcon :path="mdiCog" :size="16" />
                  Settings
                </button>
                <button class="dropdown-item dropdown-item--danger" @click="logout">
                  <AppIcon :path="mdiLogout" :size="16" />
                  {{ authStore.isSignedInWithGoogle ? 'Sign Out' : 'Log Out' }}
                </button>
                <div class="dropdown-divider" />
                <div class="dropdown-version">v{{ appVersion }}</div>
              </div>
            </Transition>
          </div>
        </template>
        <RouterLink v-else-if="!authStore.isSignedInWithGoogle" to="/join" class="btn-login">Log In</RouterLink>
      </template>

    <nav v-if="authStore.isAuthenticated">
      <RouterLink to="/">Home</RouterLink>
      <RouterLink to="/team">My Team</RouterLink>
      <RouterLink to="/teams">All Teams</RouterLink>
      <RouterLink to="/schedule">Schedule</RouterLink>
      <RouterLink to="/playoffs">Playoffs</RouterLink>
      <RouterLink to="/draft">Draft Board</RouterLink>
      <template v-if="authStore.isAdmin">
        <span class="nav-divider" />
        <RouterLink to="/league/setup">League Setup</RouterLink>
        <RouterLink to="/pokemon">Point Values</RouterLink>
      </template>
    </nav>
  </v-app-bar>
  <v-main class="d-flex align-center justify-center">
    <RouterView />
  </v-main>
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
  color-scheme: light dark;
}

* {
  box-sizing: border-box;
  margin: 0;
  padding: 0;
}

body {
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
  background: var(--bg);
  color: var(--text);
  min-height: 100vh;
}

a { color: inherit; text-decoration: none; }
</style>

<style scoped>
.app-header {
  display: flex;
  flex-direction: column;
  background: var(--card-bg);
  border-bottom: 1px solid var(--border-color);
  position: sticky;
  top: 0;
  z-index: 100;
}

.header-top {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0 1.25rem;
  height: 56px;
}

.league-name {
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.logo {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 1.1rem;
  font-weight: 800;
  color: var(--secondary);
  white-space: nowrap;
  flex-shrink: 0;
}

.logo-icon { flex-shrink: 0; }

nav {
  display: flex;
  align-items: center;
  gap: 0.15rem;
  padding: 0 1.25rem;
  border-top: 1px solid var(--border-color);
}

nav a {
  padding: 0.4rem 0.65rem;
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--text-muted);
  white-space: nowrap;
  transition: color 0.15s, background 0.15s;
}

nav a:hover { color: var(--text); background: var(--input-bg); }
nav a.router-link-active { color: var(--text); background: var(--input-bg); }
nav a.router-link-exact-active {
  color: var(--primary);
  background: var(--primary-hover-bg);
  font-weight: 700;
  border-bottom: 2px solid var(--primary);
}

.nav-divider {
  display: block;
  width: 1px;
  height: 18px;
  background: var(--border-color);
  margin: 0 0.35rem;
  flex-shrink: 0;
}

/* Right-side user area */
.header-right {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-left: auto;
  flex-shrink: 0;
}

/* Avatar button */
.user-menu {
  position: relative;
}

.avatar-btn {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  border: 2px solid var(--border-color);
  background: var(--input-bg);
  cursor: pointer;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  transition: border-color 0.15s, box-shadow 0.15s;
}

.avatar-btn:hover {
  border-color: var(--primary);
  box-shadow: 0 0 0 3px var(--primary-hover-bg);
}

.avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.avatar-initials {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--text-muted);
  user-select: none;
}

/* Dropdown */
.user-dropdown {
  position: absolute;
  top: calc(100% + 10px);
  right: 0;
  min-width: 200px;
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.4);
  overflow: hidden;
  z-index: 200;
}

.dropdown-header {
  padding: 0.75rem 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.dropdown-name {
  font-size: 0.9rem;
  font-weight: 700;
  color: var(--text);
}

.dropdown-team {
  font-size: 0.78rem;
  color: var(--text-muted);
}

.dropdown-divider {
  height: 1px;
  background: var(--border-color);
}

.dropdown-item {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  width: 100%;
  padding: 0.65rem 1rem;
  background: transparent;
  border: none;
  color: var(--text-muted);
  font-size: 0.875rem;
  font-weight: 500;
  cursor: pointer;
  text-align: left;
  transition: background 0.12s, color 0.12s;
}

.dropdown-item:hover {
  background: var(--input-bg);
  color: var(--text);
}

.dropdown-item--danger:hover {
  background: var(--secondary-hover-bg);
  color: var(--secondary);
}

.dropdown-version {
  padding: 0.4rem 1rem;
  font-size: 0.7rem;
  color: var(--text-muted);
  opacity: 0.6;
}

/* Dropdown transition */
.menu-enter-active,
.menu-leave-active {
  transition: opacity 0.15s ease, transform 0.15s ease;
}

.menu-enter-from,
.menu-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}

/* Google identity in header */
.my-leagues-btn {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.3rem 0.75rem;
  border-radius: 6px;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-muted);
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  transition: color 0.15s, border-color 0.15s;
}

.my-leagues-btn:hover {
  color: var(--text);
  border-color: var(--primary);
}

.google-avatar {
  width: 22px;
  height: 22px;
  border-radius: 50%;
}

.dropdown-google {
  font-size: 0.72rem;
  color: var(--text-muted);
  opacity: 0.7;
}

/* Login button */
.btn-login {
  padding: 0.3rem 0.85rem;
  border-radius: 6px;
  background: var(--primary);
  color: #fff;
  font-size: 0.85rem;
  font-weight: 600;
  white-space: nowrap;
  transition: opacity 0.15s;
}

.btn-login:hover { opacity: 0.85; }
</style>
