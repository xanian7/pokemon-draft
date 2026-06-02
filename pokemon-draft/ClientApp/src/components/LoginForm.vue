<script lang="ts" setup>
import { ref, watch, computed } from 'vue'
import { API_BASE } from '@/services/signalr'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const authStore = useAuthStore()

const tab = ref('join')

// ── Discord login ────────────────────────────────────────────────────────────
function loginWithDiscord() {
  window.location.href = `${API_BASE}/auth/discord`
}

// ── Join League ──────────────────────────────────────────────────────────────
const joinCode = ref('')
const joinLeagueName = ref('')
const joinName = ref(authStore.authUser?.name ?? '')
const joinTeamName = ref('')
const joinTeamImageUrl = ref('')
const joinError = ref('')
const joinLoading = ref(false)

let lookupTimer: ReturnType<typeof setTimeout> | null = null
watch(joinCode, (val) => {
  joinLeagueName.value = ''
  if (lookupTimer) clearTimeout(lookupTimer)
  const code = val.trim().toUpperCase()
  if (code.length < 4) return
  lookupTimer = setTimeout(async () => {
    try {
      const res = await fetch(`${API_BASE}/leagues/${code}`)
      if (res.ok) {
        const data = await res.json()
        joinLeagueName.value = data.name ?? ''
      }
    } catch { /* silent */ }
  }, 400)
})

async function joinLeague() {
  joinError.value = ''
  const code = joinCode.value.trim().toUpperCase()
  if (!code || !joinName.value.trim()) {
    joinError.value = 'Please fill in all required fields.'
    return
  }
  joinLoading.value = true
  try {
    const res = await fetch(`${API_BASE}/leagues/${code}/players/register`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authStore.authHeaders() },
      body: JSON.stringify({
        name: joinName.value.trim(),
        teamName: joinTeamName.value.trim() || null,
        teamImageUrl: joinTeamImageUrl.value.trim() || null,
      }),
    })
    const text = await res.text()
    if (!res.ok) {
      joinError.value = text || 'Could not join league.'
      return
    }
    const err = await authStore.enterLeague(code)
    if (err) { joinError.value = err; return }
    router.push('/')
  } catch {
    joinError.value = 'Could not connect to server.'
  } finally {
    joinLoading.value = false
  }
}

// ── Create League ────────────────────────────────────────────────────────────
const createLeagueName = ref('My Draft League')
const createCommissionerName = ref(authStore.authUser?.name ?? '')
const createError = ref('')
const createLoading = ref(false)
const createdLeague = ref<{ code: string; name: string } | null>(null)

async function createLeague() {
  createError.value = ''
  if (!createLeagueName.value.trim() || !createCommissionerName.value.trim()) {
    createError.value = 'Please fill in all fields.'
    return
  }
  createLoading.value = true
  try {
    const res = await fetch(`${API_BASE}/leagues`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authStore.authHeaders() },
      body: JSON.stringify({
        name: createLeagueName.value.trim(),
        commissionerName: createCommissionerName.value.trim(),
      }),
    })
    if (!res.ok) {
      createError.value = 'Failed to create league.'
      return
    }
    createdLeague.value = await res.json()
  } catch {
    createError.value = 'Could not connect to server.'
  } finally {
    createLoading.value = false
  }
}

async function enterCreatedLeague() {
  if (!createdLeague.value) return
  const err = await authStore.enterLeague(createdLeague.value.code)
  if (!err) router.push('/league/setup')
}

const isSignedIn = computed(() => authStore.isSignedIn)
const isInLeague = computed(() => authStore.isAuthenticated)
</script>

<template>
  <v-card class="mx-auto" max-width="440" rounded="lg">
    <v-card-title class="text-h6 pt-4 pb-2 px-5">Pokémon Draft</v-card-title>

    <v-card-text class="px-5 pb-5">

      <!-- ── Not signed in: Discord login ────────────────────────────────── -->
      <template v-if="!isSignedIn">
        <p class="text-body-2 text-medium-emphasis mb-4">
          Sign in with Discord to create or join a league.
        </p>
        <v-btn block class="login-discord" size="large" @click="loginWithDiscord">
          <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" fill="currentColor" viewBox="0 0 16 16" class="mr-2">
            <path d="M13.545 2.907a13.2 13.2 0 0 0-3.257-1.011.05.05 0 0 0-.052.025c-.141.25-.297.577-.406.833a12.2 12.2 0 0 0-3.658 0 8 8 0 0 0-.412-.833.05.05 0 0 0-.052-.025c-1.125.194-2.22.534-3.257 1.011a.04.04 0 0 0-.021.018C.356 6.024-.213 9.047.066 12.032q.003.022.021.037a13.3 13.3 0 0 0 3.995 2.02.05.05 0 0 0 .056-.019q.463-.63.818-1.329a.05.05 0 0 0-.01-.059l-.018-.011a9 9 0 0 1-1.248-.595.05.05 0 0 1-.02-.066l.015-.019q.127-.095.248-.195a.05.05 0 0 1 .051-.007c2.619 1.196 5.454 1.196 8.041 0a.05.05 0 0 1 .053.007q.121.1.248.195a.05.05 0 0 1-.004.085 8 8 0 0 1-1.249.594.05.05 0 0 0-.03.03.05.05 0 0 0 .003.041c.24.465.515.909.817 1.329a.05.05 0 0 0 .056.019 13.2 13.2 0 0 0 4.001-2.02.05.05 0 0 0 .021-.037c.334-3.451-.559-6.449-2.366-9.106a.03.03 0 0 0-.02-.019m-8.198 7.307c-.789 0-1.438-.724-1.438-1.612s.637-1.613 1.438-1.613c.807 0 1.45.73 1.438 1.613 0 .888-.637 1.612-1.438 1.612m5.316 0c-.788 0-1.438-.724-1.438-1.612s.637-1.613 1.438-1.613c.807 0 1.451.73 1.438 1.613 0 .888-.631 1.612-1.438 1.612"/>
          </svg>
          <span class="discord-btn-text">Continue with Discord</span>
        </v-btn>
      </template>

      <!-- ── In a league: welcome ─────────────────────────────────────────── -->
      

      <!-- ── Signed in, not in a league: join / create tabs ──────────────── -->

        <!-- Discord user banner -->
        <div class="discord-banner mb-4">
          <v-avatar v-if="authStore.authUser?.pictureUrl" :image="authStore.authUser.pictureUrl" size="24" />
          <span class="text-body-2 text-medium-emphasis">
            Signed in as <strong>{{ authStore.authUser?.name }}</strong>
          </span>
        </div>

        <v-tabs v-model="tab" grow density="compact" class="tab-bar">
          <v-tab value="join">Join League</v-tab>
          <v-tab value="create">Create League</v-tab>
        </v-tabs>
        <v-divider/>

        <v-tabs-window v-model="tab">

          <!-- Join League -->
          <v-tabs-window-item value="join">
            <v-text-field
              v-model="joinCode"
              label="League Code"
              variant="outlined"
              density="compact"
              class="top-btn poke-input"
              :hint="joinLeagueName || ' '"
              persistent-hint
              maxlength="6"
              @input="joinCode = joinCode.toUpperCase()"
            />
            <v-text-field
              v-model="joinName"
              label="Your Name"
              variant="outlined"
              density="compact"
              class="poke-input"
            />
            <v-text-field
              v-model="joinTeamName"
              label="Team Name (optional)"
              variant="outlined"
              density="compact"
              class="poke-input"
            />
            <v-text-field
              v-model="joinTeamImageUrl"
              label="Team Avatar URL (optional)"
              variant="outlined"
              density="compact"
              class="poke-input"
              placeholder="https://..."
            />
            <v-alert v-if="joinError" type="error" density="compact" class="mb-3" :text="joinError" />
            <v-btn block color="primary" :loading="joinLoading" @click="joinLeague">
              Join League
            </v-btn>
          </v-tabs-window-item>

          <!-- Create League -->
          <v-tabs-window-item value="create">
            <template v-if="!createdLeague">
              <v-text-field
                v-model="createCommissionerName"
                label="Your Name"
                variant="outlined"
                density="compact"
                class="top-btn poke-input"
              />
              <v-text-field
                v-model="createLeagueName"
                label="League Name"
                variant="outlined"
                density="compact"
                class="poke-input"
                placeholder="My Draft League"
              />
              <v-alert v-if="createError" type="error" density="compact" class="mb-3" :text="createError" />
              <v-btn block color="primary" :loading="createLoading" @click="createLeague">
                Create League
              </v-btn>
            </template>

            <!-- Success state -->
            <template v-else>
              <div class="text-center py-2">
                <div class="text-body-2 text-medium-emphasis mb-2">League created!</div>
                <div class="league-code mb-2">{{ createdLeague.code }}</div>
                <div class="text-caption text-medium-emphasis">
                  Share this code with your players.
                </div>
                <v-btn block color="primary" @click="enterCreatedLeague" class="top-btn">
                  Enter League Setup →
                </v-btn>
              </div>
            </template>
          </v-tabs-window-item>
        </v-tabs-window>


    </v-card-text>
  </v-card>
</template>

<style scoped>
.login-discord {
  background-color: #5865f2 !important;
  color: white !important;
}

.discord-btn-text {
  font-weight: 600;
}

.discord-banner {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  background: rgba(88, 101, 242, 0.08);
  border: 1px solid rgba(88, 101, 242, 0.25);
  border-radius: 8px;
  padding: 0.45rem 0.75rem;
}

.league-code {
  font-size: 2rem;
  font-weight: 900;
  letter-spacing: 0.2em;
  color: rgb(var(--v-theme-primary));
  background: rgba(var(--v-theme-primary), 0.08);
  border: 2px dashed rgba(var(--v-theme-primary), 0.3);
  border-radius: 10px;
  padding: 0.6rem 1.25rem;
  display: inline-block;
  margin-bottom: 8px;
  margin-top: 8px;
}

.tab-bar {
  margin-top: 16px;
}

.top-btn {
  margin-top: 16px;
}

.poke-input :deep(.v-field__input) {
  padding-inline-start: 12px !important;
}

.poke-input :deep(.v-field-label) {
  left: 12px !important;
}
</style>
