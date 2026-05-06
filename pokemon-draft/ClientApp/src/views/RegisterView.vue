<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { API_BASE } from '@/services/signalr'
import AppIcon from '@/components/AppIcon.vue'
import { mdiPokeball } from '@mdi/js'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()

const leagueCode = ref((route.query.code as string ?? '').toUpperCase())
const leagueName = ref('')
const name = ref('')
const pin = ref('')
const confirmPin = ref('')
const error = ref('')
const isLoading = ref(false)
const step = ref<'form' | 'done'>('form')

onMounted(async () => {
  if (!leagueCode.value) return
  const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}`)
  if (res.ok) {
    const data = await res.json()
    leagueName.value = data.name
  } else {
    error.value = 'Invalid or unknown league code.'
  }
})

async function register() {
  error.value = ''
  if (!name.value.trim()) { error.value = 'Please enter your name.'; return }
  if (pin.value.length < 3) { error.value = 'PIN must be at least 3 characters.'; return }
  if (pin.value !== confirmPin.value) { error.value = 'PINs do not match.'; return }

  isLoading.value = true
  try {
    const res = await fetch(`${API_BASE}/leagues/${leagueCode.value}/players/register`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: name.value.trim(), pin: pin.value }),
    })

    const text = await res.text()
    if (!res.ok) { error.value = text || 'Could not register.'; return }

    // Auto-login after registration
    const joinErr = await authStore.join(leagueCode.value, pin.value)
    if (joinErr) { error.value = joinErr; return }

    router.push('/draft')
  } catch {
    error.value = 'Could not connect to server.'
  } finally {
    isLoading.value = false
  }
}
</script>

<template>
  <div class="page-center">
    <div class="auth-card">
      <div class="logo"><AppIcon :path="mdiPokeball" :size="52" /></div>
      <h1>Join the Draft</h1>

      <div v-if="leagueName" class="league-badge">
        {{ leagueName }}
      </div>

      <template v-if="step === 'form'">
        <form @submit.prevent="register" class="register-form">
          <div class="field">
            <label for="code">League Code</label>
            <input
              id="code"
              v-model="leagueCode"
              type="text"
              placeholder="ABC123"
              maxlength="6"
              style="text-transform:uppercase"
            />
          </div>
          <div class="field">
            <label for="name">Your Name</label>
            <input id="name" v-model="name" type="text" placeholder="e.g. Misty" autofocus />
          </div>
          <div class="field">
            <label for="pin">Choose a PIN</label>
            <input id="pin" v-model="pin" type="password" placeholder="Min. 3 characters" />
            <span class="hint">You'll use this PIN to log back in. Keep it private!</span>
          </div>
          <div class="field">
            <label for="confirm">Confirm PIN</label>
            <input id="confirm" v-model="confirmPin" type="password" placeholder="Repeat PIN" />
          </div>

          <div v-if="error" class="error-msg">{{ error }}</div>

          <button type="submit" class="btn-join" :disabled="isLoading">
            <span v-if="isLoading" class="spinner" />
            {{ isLoading ? 'Joining…' : 'Join League →' }}
          </button>
        </form>

        <p class="login-link">
          Already joined?
          <RouterLink to="/join">Log in here</RouterLink>
        </p>
      </template>
    </div>
  </div>
</template>

<style scoped>
.logo { display: flex; justify-content: center; margin-bottom: 0.4rem; color: var(--primary); }

h1 { font-size: 1.5rem; font-weight: 800; margin-bottom: 0.75rem; }

.league-badge {
  display: inline-block;
  background: var(--primary);
  color: white;
  border-radius: 20px;
  padding: 0.25rem 1rem;
  font-size: 0.85rem;
  font-weight: 700;
  margin-bottom: 1.5rem;
}

.register-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  text-align: left;
}

.hint { font-size: 0.74rem; color: var(--text-muted); }

.btn-join {
  background: var(--primary);
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
  margin-top: 0.25rem;
  width: 100%;
}

.btn-join:disabled { opacity: 0.6; cursor: not-allowed; }

.login-link {
  margin-top: 1.25rem;
  font-size: 0.85rem;
  color: var(--text-muted);
}

.login-link a { color: var(--primary); text-decoration: none; }
</style>
