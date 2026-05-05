<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const authStore = useAuthStore()

const leagueCode = ref('')
const pin = ref('')
const error = ref('')
const isLoading = ref(false)

// If already authenticated, go straight to draft
if (authStore.isAuthenticated) {
  router.replace('/draft')
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
  <div class="join-page">
    <div class="join-card">
      <div class="join-logo">🎴</div>
      <h1>Join Draft League</h1>
      <p class="subtitle">Enter the league code and your PIN to join.</p>

      <form @submit.prevent="join" class="join-form">
        <div class="field">
          <label for="code">League Code</label>
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

        <button type="submit" class="btn-join" :disabled="isLoading">
          <span v-if="isLoading" class="spinner" />
          {{ isLoading ? 'Joining…' : 'Join Draft' }}
        </button>
      </form>

      <div class="divider">Commissioner?</div>
      <button class="btn-create" @click="router.push('/league/create')">
        Create a New League
      </button>
    </div>
  </div>
</template>

<style scoped>
.join-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1rem;
  background: var(--bg);
}

.join-card {
  width: 100%;
  max-width: 380px;
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 16px;
  padding: 2.5rem 2rem;
  text-align: center;
}

.join-logo {
  font-size: 3rem;
  margin-bottom: 0.5rem;
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

.join-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  text-align: left;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.field label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

input {
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  color: var(--text);
  border-radius: 8px;
  padding: 0.6rem 0.8rem;
  font-size: 0.95rem;
  transition: border-color 0.15s;
}

input:focus {
  outline: none;
  border-color: var(--primary);
}

.code-input {
  text-transform: uppercase;
  letter-spacing: 0.15em;
  font-weight: 700;
}

.error-msg {
  background: rgba(220, 38, 38, 0.12);
  border: 1px solid rgba(220, 38, 38, 0.35);
  color: #f87171;
  border-radius: 6px;
  padding: 0.6rem 0.75rem;
  font-size: 0.85rem;
}

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
  transition: opacity 0.15s;
}

.btn-join:disabled {
  opacity: 0.6;
  cursor: not-allowed;
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

.btn-create {
  width: 100%;
  background: transparent;
  border: 1px solid var(--border-color);
  color: var(--text);
  border-radius: 8px;
  padding: 0.6rem;
  font-size: 0.9rem;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
}

.btn-create:hover {
  border-color: var(--primary);
  background: var(--input-bg);
}

.spinner {
  width: 16px;
  height: 16px;
  border: 2px solid rgba(255,255,255,0.4);
  border-top-color: white;
  border-radius: 50%;
  animation: spin 0.7s linear infinite;
}

@keyframes spin { to { transform: rotate(360deg); } }
</style>
