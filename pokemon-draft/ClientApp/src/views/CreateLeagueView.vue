<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import PokeballLoader from '@/components/PokeballLoader.vue'
import { API_BASE } from '@/services/signalr'
import { useAuthStore } from '@/stores/auth'
import { mdiTrophy, mdiPartyPopper, mdiGoogle } from '@mdi/js'

const router = useRouter()
const authStore = useAuthStore()

const isGoogleUser = computed(() => authStore.isSignedInWithGoogle)

const leagueName = ref('My Draft League')
const commissionerName = ref(authStore.googleUser?.name ?? '')
const adminPin = ref('')
const error = ref('')
const isLoading = ref(false)
const created = ref<{ code: string; name: string } | null>(null)

async function createLeague() {
  if (!leagueName.value.trim() || !commissionerName.value.trim()) {
    error.value = 'Please fill in all fields.'
    return
  }
  if (!isGoogleUser.value && !adminPin.value.trim()) {
    error.value = 'Please fill in all fields.'
    return
  }
  isLoading.value = true
  error.value = ''
  try {
    const body: Record<string, string> = {
      name: leagueName.value.trim(),
      commissionerName: commissionerName.value.trim(),
    }
    if (!isGoogleUser.value) {
      body.adminPin = adminPin.value.trim()
    }

    const res = await fetch(`${API_BASE}/leagues`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...authStore.authHeaders(),
      },
      body: JSON.stringify(body),
    })
    if (!res.ok) {
      error.value = 'Failed to create league.'
      return
    }
    created.value = await res.json()
  } catch {
    error.value = 'Could not connect to server.'
  } finally {
    isLoading.value = false
  }
}

async function enterAsAdmin() {
  if (!created.value) return
  if (isGoogleUser.value) {
    const err = await authStore.enterLeague(created.value.code)
    if (!err) router.push('/league/setup')
  } else {
    const err = await authStore.join(created.value.code, adminPin.value)
    if (!err) router.push('/league/setup')
  }
}
</script>

<template>
  <div class="page-center">
    <div class="auth-card">
      <button class="back-btn" @click="router.push('/join')">← Back</button>
      <div class="create-logo"><AppIcon :path="mdiTrophy" :size="48" /></div>
      <h1>Create a League</h1>
      <p class="subtitle">Set up your league as commissioner. Share the code with your players.</p>

      <template v-if="!created">
        <!-- Google user indicator -->
        <div v-if="isGoogleUser" class="google-banner">
          <img
            v-if="authStore.googleUser?.picture"
            :src="authStore.googleUser.picture"
            class="google-avatar"
            alt=""
          />
          <span
            >Creating as <strong>{{ authStore.googleUser?.name }}</strong> via Google</span
          >
        </div>

        <form @submit.prevent="createLeague" class="create-form">
          <div class="field">
            <label for="commissionerName">Your Name</label>
            <input id="commissionerName" v-model="commissionerName" type="text" />
          </div>
          <div class="field">
            <label for="name">League Name</label>
            <input id="name" v-model="leagueName" type="text" placeholder="My Draft League" />
          </div>

          <!-- Admin PIN – hidden for Google users -->
          <template v-if="!isGoogleUser">
            <div class="field">
              <label for="pin">Your Admin PIN</label>
              <input id="pin" v-model="adminPin" type="password" placeholder="Choose a PIN" />
              <span class="hint">Keep this private — it gives full control over the league.</span>
            </div>
          </template>
          <div v-else class="info-note">
            <AppIcon :path="mdiGoogle" :size="14" />
            No Admin PIN needed — your Google account secures this league.
          </div>

          <div v-if="error" class="error-msg">{{ error }}</div>
          <button type="submit" class="btn btn-primary btn-full btn-lg" :disabled="isLoading">
            <PokeballLoader v-if="isLoading" variant="inline" :size="16" />
            {{ isLoading ? 'Creating…' : 'Create League' }}
          </button>
        </form>
      </template>

      <template v-else>
        <div class="success-block">
          <p class="success-label"><AppIcon :path="mdiPartyPopper" :size="20" /> League Created!</p>
          <div class="league-code">{{ created.code }}</div>
          <p class="code-hint">
            Share this code with your players. They'll use it along with their individual PINs to
            join.
          </p>
          <button class="btn btn-primary" @click="enterAsAdmin">Enter League Setup →</button>
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.back-btn {
  position: absolute;
  top: 1rem;
  left: 1rem;
  background: none;
  border: none;
  color: var(--text-muted);
  font-size: 0.85rem;
  cursor: pointer;
  padding: 0.25rem 0.5rem;
  border-radius: 4px;
}

.back-btn:hover {
  color: var(--text);
}

.create-logo {
  margin-bottom: 0.5rem;
  display: flex;
  align-items: center;
  justify-content: center;
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
  margin-bottom: 1.75rem;
}

.create-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  text-align: left;
}

.hint {
  font-size: 0.75rem;
  color: var(--text-muted);
}

.google-banner {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 0.5rem 0.75rem;
  font-size: 0.85rem;
  color: var(--text-muted);
  margin-bottom: 1rem;
}

.google-avatar {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  flex-shrink: 0;
}

.info-note {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.82rem;
  color: var(--text-muted);
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  padding: 0.5rem 0.75rem;
}

.success-block {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
}

.success-label {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-weight: 700;
  font-size: 1.1rem;
  color: #10b981;
}

.league-code {
  font-size: 2.5rem;
  font-weight: 900;
  letter-spacing: 0.2em;
  color: var(--primary);
  background: var(--input-bg);
  border: 2px dashed var(--border-color);
  border-radius: 10px;
  padding: 0.75rem 1.5rem;
}

.code-hint {
  font-size: 0.82rem;
  color: var(--text-muted);
  line-height: 1.5;
}
</style>
