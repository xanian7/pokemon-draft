<script setup lang="ts">
import { RouterLink, RouterView } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useDraftStore } from '@/stores/draft'

const authStore = useAuthStore()

// draft store only used for legacy live badge; real status comes from server now
</script>

<template>
  <header class="app-header">
    <RouterLink to="/" class="logo">🎴 PokéDraft</RouterLink>
    <nav>
      <RouterLink to="/join">Join / Login</RouterLink>
      <RouterLink v-if="authStore.isAuthenticated && authStore.isAdmin" to="/league/setup">
        League Setup
      </RouterLink>
      <RouterLink v-if="authStore.isAuthenticated && authStore.isAdmin" to="/pokemon">
        Point Values
      </RouterLink>
      <RouterLink v-if="authStore.isAuthenticated" to="/team">
        My Team
      </RouterLink>
      <RouterLink v-if="authStore.isAuthenticated" to="/draft" class="draft-link">
        Draft Board
      </RouterLink>
      <button
        v-if="authStore.isAuthenticated"
        class="btn-logout"
        @click="authStore.clearSession()"
      >
        Leave ({{ authStore.playerName }})
      </button>
    </nav>
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
  gap: 1rem;
  padding: 0 1.25rem;
  height: 56px;
  background: var(--card-bg);
  border-bottom: 1px solid var(--border-color);
  position: sticky;
  top: 0;
  z-index: 100;
  flex-wrap: wrap;
}

.logo {
  font-size: 1.1rem;
  font-weight: 800;
  color: var(--primary);
  white-space: nowrap;
}

nav {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  flex-wrap: wrap;
  margin-left: auto;
}

nav a {
  padding: 0.3rem 0.65rem;
  border-radius: 6px;
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--text-muted);
  transition: color 0.15s, background 0.15s;
}

nav a:hover { color: var(--text); background: var(--input-bg); }
nav a.router-link-active { color: var(--text); background: var(--input-bg); }
nav a.router-link-exact-active { color: var(--primary); }

.btn-logout {
  background: transparent;
  border: 1px solid var(--border-color);
  color: var(--text-muted);
  border-radius: 6px;
  padding: 0.3rem 0.65rem;
  font-size: 0.82rem;
  cursor: pointer;
  transition: color 0.15s, border-color 0.15s;
}

.btn-logout:hover { color: var(--text); border-color: var(--text-muted); }
</style>
