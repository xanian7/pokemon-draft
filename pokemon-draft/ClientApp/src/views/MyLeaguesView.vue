<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import AppIcon from '@/components/AppIcon.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import { mdiPokeball, mdiTrophy, mdiPlusCircle, mdiLogin } from '@mdi/js'
import LoginForm from '@/components/LoginForm.vue'

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
  if (!authStore.isSignedIn) {
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
  <v-container fluid class="page">
    <div v-if="isLoading" class="loader-wrap">
      <PokeballLoader />
    </div>

    <template v-else>
      <div v-if="error" class="error-msg" style="margin-bottom: 1rem">{{ error }}</div>
      <v-row>
        <v-col cols="2"></v-col>
        <v-col cols="12" md="2" class="d-flex justify-end">
          <LoginForm />
        </v-col>
        <v-col cols="12" md="6" class="d-flex justify-start">
          <v-data-table
            :items="leagues"
            :headers="[
              { title: 'League Name', value: 'name' },
              { title: 'Your Team', value: 'team' },
              { title: 'Actions', value: 'actions', sortable: false },
            ]"
            class="rounded-lg"
          >
            <template #item.team="{ item }">
              <div class="d-flex align-center">
                <v-avatar v-if="item.teamImageUrl" :image="item.teamImageUrl" size="36" />
                <div>
                  <div class="player-name">{{ item.playerName }}</div>
                  <div class="team-name">{{ item.teamName }}</div>
                </div>
              </div>
            </template>
            <template #item.actions="{ item }">
              <v-btn
                :color="item.isCommissioner ? 'primary' : 'secondary'"
                @click="enterLeague(item.code)"
                :loading="enteringCode === item.code"
                class="enter-btn"
              >
                <AppIcon :path="mdiLogin" :size="18" class="icon" />
                Enter League
              </v-btn>
            </template>
          </v-data-table>
        </v-col> 
        <v-col cols="2"></v-col> 
      </v-row>
    </template>
  </v-container>
</template>

<style scoped>
.page {
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

.v-avatar {
  margin-right: 12px;
}

.icon {
  margin-top: 2px;
  margin-right: 4px;
}
</style>
