<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { RouterLink, RouterView, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import AppIcon from '@/components/AppIcon.vue'
import { mdiPokeball, mdiLogout, mdiCog } from '@mdi/js'

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
  authStore.clearSession()
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
  <header class="app-header">
    <RouterLink to="/" class="logo">
      <AppIcon :path="mdiPokeball" :size="22" class="logo-icon" />
      PokéDraft
    </RouterLink>

    <nav v-if="authStore.isAuthenticated">
      <RouterLink to="/team">My Team</RouterLink>
      <RouterLink to="/schedule">Schedule</RouterLink>
      <RouterLink to="/draft">Draft Board</RouterLink>
      <template v-if="authStore.isAdmin">
        <span class="nav-divider" />
        <RouterLink to="/league/setup">League Setup</RouterLink>
        <RouterLink to="/pokemon">Point Values</RouterLink>
      </template>
    </nav>

    <div class="header-right">
      <template v-if="authStore.isAuthenticated">
        <!-- Avatar button -->
        <div ref="menuRef" class="user-menu">
          <button class="avatar-btn" :title="authStore.playerName ?? ''" @click="toggleMenu">
            <img
              v-if="authStore.teamImageUrl"
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
              </div>
              <div class="dropdown-divider" />
              <button class="dropdown-item" @click="goToSettings">
                <AppIcon :path="mdiCog" :size="16" />
                Settings
              </button>
              <button class="dropdown-item dropdown-item--danger" @click="logout">
                <AppIcon :path="mdiLogout" :size="16" />
                Leave league
              </button>
              <div class="dropdown-divider" />
              <div class="dropdown-version">v{{ appVersion }}</div>
            </div>
          </Transition>
        </div>
      </template>
      <RouterLink v-else to="/join" class="btn-login">Log In</RouterLink>
    </div>
  </header>

  <RouterView />
</template>

<style>
:root {
  --primary: #cc0000;
  --secondary: #3b4cca;
  --text: #e8e8e8;
  --text-muted: #888;
  --bg: #111118;
  --card-bg: #1c1c28;
  --input-bg: #252535;
  --border-color: #2e2e42;
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
  align-items: center;
  gap: 0.75rem;
  padding: 0 1.25rem;
  height: 56px;
  background: var(--card-bg);
  border-bottom: 1px solid var(--border-color);
  position: sticky;
  top: 0;
  z-index: 100;
}

.logo {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 1.1rem;
  font-weight: 800;
  color: var(--primary);
  white-space: nowrap;
  flex-shrink: 0;
}

.logo-icon { flex-shrink: 0; }

nav {
  display: flex;
  align-items: center;
  gap: 0.15rem;
  margin-left: 1rem;
  flex: 1;
}

nav a {
  padding: 0.3rem 0.65rem;
  border-radius: 6px;
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
  background: rgba(204, 0, 0, 0.08);
  box-shadow: 0 2px 0 var(--primary);
  font-weight: 700;
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
  box-shadow: 0 0 0 3px rgba(204, 0, 0, 0.15);
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
  background: rgba(204, 0, 0, 0.08);
  color: var(--primary);
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
