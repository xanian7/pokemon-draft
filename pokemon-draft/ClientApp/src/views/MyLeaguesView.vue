<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore, type MyLeague } from '@/stores/auth'
import AppIcon from '@/components/AppIcon.vue'
import { mdiTrophy, mdiLogin } from '@mdi/js'
import LoginForm from '@/components/LoginForm.vue'
import { enqueueSnackbar } from '@/services/snackbar'

const router = useRouter()
const authStore = useAuthStore()

const leagues = ref<MyLeague[]>([])
const isLoading = ref(true)
const enteringCode = ref<string | null>(null)

const sortedLeagues = computed(() => {
  const recentOrder = new Map(authStore.recentLeagues.map((league, index) => [league.code, index]))
  // Keep the API order for leagues without a recorded visit.
  const unvisited = recentOrder.size
  return [...leagues.value].sort((a, b) =>
    (recentOrder.get(a.code) ?? unvisited) - (recentOrder.get(b.code) ?? unvisited),
  )
})

onMounted(async () => {
  if (!authStore.isSignedIn) {
    router.replace('/login')
    return
  }
  leagues.value = await authStore.fetchMyLeagues()
  isLoading.value = false
})

async function enterLeague(code: string) {
  if (enteringCode.value) return
  enteringCode.value = code
  try {
    const err = await authStore.enterLeague(code)
    if (err) {
      enqueueSnackbar(err, 'error')
    } else {
      await router.push('/league?tab=home')
    }
  } finally {
    enteringCode.value = null
  }
}
</script>

<template>
  <v-container fluid class="page">
    <div v-if="isLoading" class="loader-wrap" role="status" aria-label="Loading leagues">
      <v-progress-circular indeterminate color="primary" />
    </div>

    <v-row v-else>
      <v-col cols="12" md="4">
        <LoginForm />
      </v-col>
      <v-col cols="12" md="8">
        <header class="page-header">
          <div>
            <h1>My Leagues</h1>
            <p class="subtitle">Jump back into your leagues. Recently accessed leagues appear first.</p>
          </div>
        </header>

        <v-card v-if="!leagues.length" variant="outlined" rounded="lg" class="empty-state">
          <p>You haven't joined any leagues yet.</p>
          <p class="empty-sub">Join a league or create one to get started.</p>
        </v-card>

        <ul v-else class="league-list" aria-label="Your leagues">
          <li v-for="league in sortedLeagues" :key="league.code">
            <v-card variant="outlined" rounded="lg" class="league-card">
              <div class="league-info">
                <h2 class="league-name">{{ league.name }}</h2>
                <div class="league-meta">
                  <span class="code-badge">{{ league.code }}</span>
                  <v-chip v-if="league.isCommissioner" color="primary" size="small">
                    Commissioner
                  </v-chip>
                  <v-chip v-else-if="league.isCoCommissioner" color="secondary" size="small">
                    Co-Commissioner
                  </v-chip>
                  <v-chip v-else size="small">Player</v-chip>
                </div>
                <div class="team-info">
                  <v-avatar v-if="league.teamImageUrl" :image="league.teamImageUrl" size="40" />
                  <div class="team-details">
                    <div class="player-name">{{ league.playerName }}</div>
                    <div v-if="league.teamName" class="team-name">{{ league.teamName }}</div>
                  </div>
                </div>
              </div>
              <v-btn
                :color="league.isCommissioner || league.isCoCommissioner ? 'primary' : 'secondary'"
                :loading="enteringCode === league.code"
                :disabled="enteringCode !== null && enteringCode !== league.code"
                :aria-label="`Enter ${league.name}`"
                class="enter-btn"
                @click="enterLeague(league.code)"
              >
                <AppIcon :path="mdiLogin" :size="18" class="mr-2" />
                Enter League
              </v-btn>
            </v-card>
          </li>
        </ul>
      </v-col>
    </v-row>
  </v-container>
</template>

<style scoped>
.page {
  max-width: 1200px;
  margin: 0 auto;
  padding: 2rem 1.25rem;
}

.page-header {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.header-icon {
  color: rgb(var(--v-theme-primary));
  flex-shrink: 0;
}

h1 {
  font-size: 1.6rem;
  font-weight: 800;
  margin-bottom: 0.15rem;
}

.subtitle,
.team-name,
.empty-state {
  color: rgba(var(--v-theme-on-surface), var(--v-medium-emphasis-opacity));
}

.subtitle {
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
}

.empty-sub {
  font-size: 0.85rem;
  margin-top: 0.5rem;
}

.league-list {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  list-style: none;
  padding: 0;
  margin: 0;
}

.league-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1.25rem;
  border-color: rgba(var(--v-border-color), var(--v-border-opacity));
  transition: border-color 0.15s;
}

.league-card:hover,
.league-card:focus-within {
  border-color: rgb(var(--v-theme-primary));
}

.league-info,
.team-details {
  min-width: 0;
  overflow-wrap: anywhere;
}

.league-info {
  flex: 1;
}

.league-name {
  font-size: 1.1rem;
  font-weight: 700;
  margin-bottom: 0.5rem;
}

.league-meta,
.team-info {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.league-meta {
  flex-wrap: wrap;
  margin-bottom: 1rem;
}

.code-badge {
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.1em;
}

.player-name {
  font-size: 0.9rem;
}

.team-name {
  font-size: 0.85rem;
  margin-top: 0.15rem;
}

.enter-btn,
.team-info .v-avatar {
  flex-shrink: 0;
}

@media (max-width: 599px) {
  .league-card {
    flex-direction: column;
    align-items: stretch;
  }

  .enter-btn {
    width: 100%;
  }
}
</style>
