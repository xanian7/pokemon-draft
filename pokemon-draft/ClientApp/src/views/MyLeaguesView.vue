<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import AppIcon from '@/components/AppIcon.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import { mdiPokeball, mdiTrophy, mdiPlusCircle, mdiLogin } from '@mdi/js'

interface MyLeague {
  code: string
  name: string
  playerId: string
  playerName: string
  teamName: string
  teamImageUrl: string
  isCommissioner: boolean
}

const router = useRouter()
const authStore = useAuthStore()

const leagues = ref<MyLeague[]>([])
const isLoading = ref(true)
const enteringCode = ref<string | null>(null)
const error = ref('')

onMounted(async () => {
  if (!authStore.isSignedInWithGoogle) {
    router.replace('/join')
    return
  }
  leagues.value = await authStore.fetchMyLeagues()
  isLoading.value = false
})

async function enterLeague(code: string) {
  enteringCode.value = code
  error.value = ''
  const err = await authStore.enterLeague(code)
  enteringCode.value = null
  if (err) {
    error.value = err
  } else {
    router.push('/')
  }
}
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div class="header-icon"><AppIcon :path="mdiTrophy" :size="28" /></div>
      <div>
        <h1>My Leagues</h1>
        <p class="subtitle" v-if="authStore.googleUser">
          Signed in as {{ authStore.googleUser.name }}
        </p>
      </div>
    </div>

    <div v-if="isLoading" class="loader-wrap">
      <PokeballLoader />
    </div>

    <template v-else>
      <div v-if="error" class="error-msg" style="margin-bottom:1rem">{{ error }}</div>

      <div v-if="leagues.length === 0" class="empty-state">
        <AppIcon :path="mdiPokeball" :size="48" class="empty-icon" />
        <p>You haven't joined any leagues yet.</p>
        <p class="empty-sub">Create a new league or register for one using a league code.</p>
      </div>

      <div v-else class="league-grid">
        <div v-for="league in leagues" :key="league.code" class="league-card">
          <div class="league-info">
            <div class="league-name">{{ league.name }}</div>
            <div class="league-meta">
              <span class="code-badge">{{ league.code }}</span>
              <span v-if="league.isCommissioner" class="commissioner-badge">Commissioner</span>
            </div>
            <div class="player-name">Playing as <strong>{{ league.playerName }}</strong></div>
            <div v-if="league.teamName" class="team-name">{{ league.teamName }}</div>
          </div>
          <button
            class="btn btn-primary enter-btn"
            :disabled="enteringCode === league.code"
            @click="enterLeague(league.code)"
          >
            <PokeballLoader v-if="enteringCode === league.code" variant="inline" :size="14" />
            <AppIcon v-else :path="mdiLogin" :size="16" />
            {{ enteringCode === league.code ? 'Entering…' : 'Enter League' }}
          </button>
        </div>
      </div>

      <div class="actions">
        <button class="btn btn-ghost" @click="router.push('/league/create')">
          <AppIcon :path="mdiPlusCircle" :size="18" />
          Create a League
        </button>
        <button class="btn btn-ghost" @click="router.push('/register')">
          <AppIcon :path="mdiPokeball" :size="18" />
          Join a New League
        </button>
      </div>
    </template>
  </div>
</template>

<style scoped>
.page {
  max-width: 700px;
  margin: 0 auto;
  padding: 2rem 1.25rem;
}

.page-header {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 2rem;
}

.header-icon {
  color: var(--primary);
  flex-shrink: 0;
}

h1 {
  font-size: 1.6rem;
  font-weight: 800;
  margin-bottom: 0.15rem;
}

.subtitle {
  color: var(--text-muted);
  font-size: 0.9rem;
}

.loader-wrap {
  display: flex;
  justify-content: center;
  padding: 3rem 0;
}

.empty-state {
  text-align: center;
  padding: 3rem 1rem;
  color: var(--text-muted);
}

.empty-icon {
  margin-bottom: 1rem;
  opacity: 0.4;
}

.empty-sub {
  font-size: 0.85rem;
  margin-top: 0.5rem;
}

.league-grid {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  margin-bottom: 2rem;
}

.league-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 1.1rem 1.25rem;
  transition: border-color 0.15s;
}

.league-card:hover {
  border-color: var(--primary);
}

.league-info {
  flex: 1;
  min-width: 0;
}

.league-name {
  font-size: 1.05rem;
  font-weight: 700;
  margin-bottom: 0.3rem;
}

.league-meta {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.4rem;
}

.code-badge {
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  padding: 0.1rem 0.4rem;
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.1em;
  color: var(--text-muted);
}

.commissioner-badge {
  background: var(--primary);
  color: #fff;
  border-radius: 4px;
  padding: 0.1rem 0.4rem;
  font-size: 0.72rem;
  font-weight: 700;
}

.player-name {
  font-size: 0.85rem;
  color: var(--text-muted);
}

.team-name {
  font-size: 0.8rem;
  color: var(--text-muted);
  margin-top: 0.15rem;
}

.enter-btn {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  white-space: nowrap;
  flex-shrink: 0;
}

.actions {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.actions .btn {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}
</style>
