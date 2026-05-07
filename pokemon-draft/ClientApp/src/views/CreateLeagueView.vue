<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import { API_BASE } from '@/services/signalr'
import { useAuthStore } from '@/stores/auth'
import { mdiTrophy, mdiPartyPopper } from '@mdi/js'

const router = useRouter()
const authStore = useAuthStore()

const leagueName = ref('My Draft League')
const commissionerName = ref('')
const adminPin = ref('')
const error = ref('')
const isLoading = ref(false)
const created = ref<{ code: string; name: string } | null>(null)

async function createLeague() {
  if (!leagueName.value.trim() || !adminPin.value.trim() || !commissionerName.value.trim()) {
    error.value = 'Please fill in all fields.'
    return
  }
  isLoading.value = true
  error.value = ''
  try {
    const res = await fetch(`${API_BASE}/leagues`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        name: leagueName.value.trim(),
        commissionerName: commissionerName.value.trim(),
        adminPin: adminPin.value.trim(),
      }),
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
  const err = await authStore.join(created.value.code, adminPin.value)
  if (!err) router.push('/league/setup')
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
        <form @submit.prevent="createLeague" class="create-form">
          <div class="field">
            <label for="commissionerName">Your Name</label>
            <input
              id="commissionerName"
              v-model="commissionerName"
              type="text"
              placeholder="e.g. Ash Ketchum"
            />
          </div>
          <div class="field">
            <label for="name">League Name</label>
            <input id="name" v-model="leagueName" type="text" placeholder="My Draft League" />
          </div>
          <div class="field">
            <label for="pin">Your Admin PIN</label>
            <input id="pin" v-model="adminPin" type="password" placeholder="Choose a PIN" />
            <span class="hint">Keep this private — it gives full control over the league.</span>
          </div>
          <div v-if="error" class="error-msg">{{ error }}</div>
          <button type="submit" class="btn-create" :disabled="isLoading">
            <span v-if="isLoading" class="spinner" />
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
          <button class="btn-enter" @click="enterAsAdmin">Enter League Setup →</button>
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

.btn-create {
  background: var(--secondary);
  color: white;
  border: none;
  border-radius: 8px;
  padding: 0.75rem;
  font-size: 1rem;
  font-weight: 700;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  width: 100%;
}

.btn-create:disabled {
  opacity: 0.6;
  cursor: not-allowed;
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

.btn-enter {
  background: var(--primary);
  color: white;
  border: none;
  border-radius: 8px;
  padding: 0.65rem 1.5rem;
  font-size: 0.95rem;
  font-weight: 700;
  cursor: pointer;
  margin-top: 0.5rem;
}

</style>
