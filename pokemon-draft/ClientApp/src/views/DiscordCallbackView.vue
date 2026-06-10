<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { apiGet } from '@/services/api'
import PokeballLoader from '@/components/PokeballLoader.vue'
import type { AuthUser } from '@/stores/auth'

const router = useRouter()
const authStore = useAuthStore()
const errorMsg = ref('')

onMounted(async () => {
  // Token is in the URL fragment (#token=...) so it never reaches the server
  const hash = window.location.hash.slice(1) // remove leading #
  const params = new URLSearchParams(hash)

  const error = params.get('error')
  if (error) {
    const messages: Record<string, string> = {
      access_denied: 'Discord login was cancelled.',
      state_mismatch: 'Login session expired. Please try again.',
      token_exchange_failed: 'Could not connect to Discord. Please try again.',
      no_access_token: 'Discord did not return a login token. Please try again.',
      user_fetch_failed: 'Could not load your Discord profile. Please try again.',
      invalid_user_data: 'Discord returned an incomplete profile. Please try again.',
      server_error: 'An unexpected error occurred. Please try again.',
    }
    errorMsg.value = messages[error] ?? 'Login failed. Please try again.'
    return
  }

  const token = params.get('token')
  if (!token) {
    errorMsg.value = 'No login token received. Please try again.'
    return
  }

  // Store the token first so the auth header is available for /auth/me
  authStore.saveAuthUser(token, { id: '', email: '', name: '', pictureUrl: '' })

  // Fetch real user data from the server (avoids client-side JWT decoding)
  const result = await apiGet<AuthUser>('/auth/me', authStore.authHeaders())
  if (result.error || !result.data) {
    errorMsg.value = 'Could not load your profile. Please try again.'
    authStore.signOut()
    return
  }

  authStore.saveAuthUser(token, result.data)

  // Clear the fragment from the browser URL before navigating away
  history.replaceState(null, '', window.location.pathname)
  router.replace('/my-leagues')
})
</script>

<template>
  <div class="callback-page">
    <div v-if="errorMsg" class="error-card">
      <p class="error-title">Login failed</p>
      <p class="error-msg">{{ errorMsg }}</p>
      <RouterLink to="/login" class="btn btn-primary">Back to Login</RouterLink>
    </div>
    <!-- <div v-else class="loading-wrap">
      <PokeballLoader />
      <p>Signing you in…</p>
    </div> -->
  </div>
</template>

<style scoped>
.callback-page {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 80vh;
}

.loading-wrap {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
  color: var(--text-muted);
}

.error-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 2rem;
  text-align: center;
  max-width: 380px;
}

.error-title {
  font-size: 1.1rem;
  font-weight: 700;
  margin-bottom: 0.5rem;
}

.error-msg {
  color: var(--text-muted);
  margin-bottom: 1.5rem;
}
</style>
