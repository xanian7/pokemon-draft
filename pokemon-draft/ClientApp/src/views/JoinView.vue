<script setup lang="ts">
import { ref, watch } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import AppIcon from '@/components/AppIcon.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import { mdiPokeball, mdiClose } from '@mdi/js'
import { API_BASE } from '@/services/signalr'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()

// Pre-fill from URL /join/ABC123 or ?code=ABC123, then fall back to the most recently used league
const initialCode =
  (route.params.code as string | undefined)?.toUpperCase() ||
  (route.query.code as string | undefined)?.toUpperCase() ||
  authStore.recentLeagues[0]?.code ||
  ''

const leagueCode = ref(initialCode)
const pin = ref('')
const error = ref('')
const isLoading = ref(false)
const leagueName = ref('')
const lookupPending = ref(false)

// If already authenticated, go straight to draft
if (authStore.isAuthenticated) {
  router.replace('/draft')
}

// Live league name lookup – debounced
let lookupTimer: ReturnType<typeof setTimeout> | null = null
watch(leagueCode, (val) => {
  leagueName.value = ''
  if (lookupTimer) clearTimeout(lookupTimer)
  const code = val.trim().toUpperCase()
  if (code.length < 4) return
  lookupPending.value = true
  lookupTimer = setTimeout(async () => {
    try {
      const res = await fetch(`${API_BASE}/leagues/${code}`)
      if (res.ok) {
        const data = await res.json()
        leagueName.value = data.name ?? ''
      }
    } finally {
      lookupPending.value = false
    }
  }, 400)
})

// Trigger lookup for pre-filled code
if (initialCode.length >= 4) {
  lookupPending.value = true
  fetch(`${API_BASE}/leagues/${initialCode}`)
    .then((r) => (r.ok ? r.json() : null))
    .then((d) => { if (d) leagueName.value = d.name ?? '' })
    .finally(() => { lookupPending.value = false })
}

function selectRecent(code: string) {
  leagueCode.value = code
}

async function join() {
  if (!leagueCode.value.trim() || !pin.value.trim()) {
    error.value = 'Please enter both a league code and your PIN.'
    return
  }
  isLoading.value = true
  error.value = ''
  const err = await authStore.join(leagueCode.value, pin.value)
  isLoading.value = false
  if (err) {
    error.value = err
  } else {
    router.push('/draft')
  }
}
</script>

<template>
  <div class="page-center">
    <div class="auth-card">
      <div class="join-logo"><AppIcon :path="mdiPokeball" :size="52" /></div>
      <h1>Log In</h1>
      <p class="subtitle">Enter your league code and PIN to continue.</p>

      <!-- Recent leagues quick-pick -->
      <div v-if="authStore.recentLeagues.length > 0" class="recent-section">
        <p class="recent-label">Recent leagues</p>
        <div class="recent-chips">
          <button
            v-for="league in authStore.recentLeagues"
            :key="league.code"
            type="button"
            class="chip"
            :class="{ 'chip-active': leagueCode === league.code }"
            @click="selectRecent(league.code)"
          >
            <span class="chip-name">{{ league.name || league.code }}</span>
            <span class="chip-code">{{ league.code }}</span>
          </button>
        </div>
      </div>

      <form @submit.prevent="join" class="join-form">
        <div class="field">
          <label for="code">League Code</label>
          <div class="code-wrap">
            <input
              id="code"
              v-model="leagueCode"
              type="text"
              placeholder="e.g. ABC123"
              maxlength="8"
              autocomplete="off"
              spellcheck="false"
              class="code-input"
            />
            <button
              v-if="leagueCode"
              type="button"
              class="clear-btn"
              @click="leagueCode = ''"
              aria-label="Clear code"
            >
              <AppIcon :path="mdiClose" :size="16" />
            </button>
          </div>
          <!-- Live league name badge -->
          <div v-if="leagueName" class="league-badge">{{ leagueName }}</div>
          <div v-else-if="lookupPending" class="league-badge league-badge--loading">Looking up…</div>
        </div>

        <div class="field">
          <label for="pin">Your PIN</label>
          <input
            id="pin"
            v-model="pin"
            type="password"
            placeholder="Enter PIN"
            autocomplete="current-password"
          />
        </div>

        <div v-if="error" class="error-msg">{{ error }}</div>

        <button type="submit" class="btn btn-primary btn-full btn-lg" :disabled="isLoading">
          <PokeballLoader v-if="isLoading" variant="inline" :size="16" />
          {{ isLoading ? 'Logging in…' : 'Log In' }}
        </button>
      </form>

      <p class="signup-link">
        New to a league?
        <RouterLink :to="leagueCode ? `/register?code=${leagueCode}` : '/register'">
          Sign up here
        </RouterLink>
      </p>

      <div class="divider">Commissioner?</div>
      <button class="btn btn-ghost btn-full" @click="router.push('/league/create')">
        Create a New League
      </button>
    </div>
  </div>
</template>

<style scoped>
.join-logo {
  display: flex;
  justify-content: center;
  margin-bottom: 0.5rem;
  color: var(--primary);
}

h1 {
  font-size: 1.5rem;
  font-weight: 800;
  margin-bottom: 0.4rem;
}

.subtitle {
  color: var(--text-muted);
  font-size: 0.9rem;
  margin-bottom: 1.25rem;
}

/* Recent leagues */
.recent-section {
  margin-bottom: 1.25rem;
}

.recent-label {
  font-size: 0.75rem;
  color: var(--text-muted);
  margin-bottom: 0.4rem;
  text-align: left;
}

.recent-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.chip {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 0.35rem 0.65rem;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
  min-width: 80px;
}

.chip:hover,
.chip-active {
  border-color: var(--primary);
  background: color-mix(in srgb, var(--primary) 10%, transparent);
}

.chip-name {
  font-size: 0.82rem;
  font-weight: 700;
  color: var(--text);
}

.chip-code {
  font-size: 0.7rem;
  color: var(--text-muted);
  letter-spacing: 0.08em;
}

/* Code field with clear button */
.code-wrap {
  position: relative;
  display: flex;
  align-items: center;
}

.code-wrap input {
  width: 100%;
  padding-right: 2rem;
}

.clear-btn {
  position: absolute;
  right: 0.5rem;
  background: none;
  border: none;
  padding: 0;
  cursor: pointer;
  color: var(--text-muted);
  display: flex;
  align-items: center;
}

.clear-btn:hover {
  color: var(--text);
}

.code-input {
  text-transform: uppercase;
  letter-spacing: 0.15em;
  font-weight: 700;
}

/* League name badge */
.league-badge {
  margin-top: 0.4rem;
  display: inline-block;
  background: var(--primary);
  color: white;
  border-radius: 20px;
  padding: 0.2rem 0.75rem;
  font-size: 0.8rem;
  font-weight: 700;
}

.league-badge--loading {
  background: var(--border-color);
  color: var(--text-muted);
}

.join-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  text-align: left;
}

.signup-link {
  margin-top: 1rem;
  font-size: 0.85rem;
  color: var(--text-muted);
}

.signup-link a {
  color: var(--primary);
  text-decoration: none;
}

.divider {
  margin: 1.5rem 0 1rem;
  font-size: 0.78rem;
  color: var(--text-muted);
  position: relative;
}

.divider::before,
.divider::after {
  content: '';
  position: absolute;
  top: 50%;
  width: 35%;
  height: 1px;
  background: var(--border-color);
}

.divider::before { left: 0; }
.divider::after { right: 0; }

</style>

