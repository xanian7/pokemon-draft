<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const authStore = useAuthStore()

if (!authStore.isAuthenticated) router.replace('/join')

const teamName = ref(authStore.teamName)
const teamImageUrl = ref(authStore.teamImageUrl)
const isSaving = ref(false)
const saveError = ref('')
const saveSuccess = ref(false)
const imgError = ref(false)

const initials = computed(() => {
  const n = teamName.value.trim() || authStore.playerName || '?'
  return n.split(' ').map((w: string) => w[0]).join('').toUpperCase().slice(0, 2)
})

function onImgError() { imgError.value = true }
function onImgLoad() { imgError.value = false }

async function save() {
  isSaving.value = true
  saveError.value = ''
  saveSuccess.value = false
  const err = await authStore.updateProfile(teamName.value.trim(), teamImageUrl.value.trim())
  if (err) {
    saveError.value = err
  } else {
    saveSuccess.value = true
    setTimeout(() => { saveSuccess.value = false }, 3000)
  }
  isSaving.value = false
}
</script>

<template>
  <main class="settings-wrap">
    <div class="settings-card">
      <p class="eyebrow">Account</p>
      <h1>Team Settings</h1>
      <p class="subtitle">Customise how your team appears to other players in the league.</p>

      <div class="avatar-section">
        <div class="avatar-preview">
          <img
            v-if="teamImageUrl && !imgError"
            :src="teamImageUrl"
            alt="Team avatar"
            class="avatar-img"
            @error="onImgError"
            @load="onImgLoad"
          />
          <div v-else class="avatar-initials">{{ initials }}</div>
        </div>
        <div class="avatar-info">
          <p class="avatar-label">Team Avatar</p>
          <p class="avatar-hint">Your avatar is shown to other players. You can change this later.</p>
        </div>
      </div>

      <form class="settings-form" @submit.prevent="save">
        <div class="field">
          <label for="team-name">Team Name</label>
          <input
            id="team-name"
            v-model="teamName"
            type="text"
            placeholder=""
            maxlength="40"
          />
          <span class="hint">{{ 40 - teamName.length }} characters remaining</span>
        </div>

        <div class="field">
          <label for="team-image">Team Avatar URL</label>
          <input
            id="team-image"
            v-model="teamImageUrl"
            type="url"
            placeholder="https://example.com/avatar.png"
          />
          <span class="hint">Paste any direct image URL.</span>
        </div>

        <div v-if="saveError" class="feedback error">{{ saveError }}</div>
        <div v-if="saveSuccess" class="feedback success">✓ Profile saved!</div>

        <div class="form-actions">
          <button type="button" class="btn btn-secondary" @click="router.back()">Cancel</button>
          <button type="submit" class="btn btn-primary" :disabled="isSaving">
            {{ isSaving ? 'Saving…' : 'Save Changes' }}
          </button>
        </div>
      </form>
    </div>
  </main>
</template>

<style scoped>
.settings-wrap {
  display: flex;
  justify-content: center;
  align-items: flex-start;
  padding: 2.5rem 1rem 4rem;
}

.settings-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 20px;
  padding: 2rem 2.5rem;
  width: 100%;
  max-width: 520px;
}

.eyebrow {
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  font-size: 0.72rem;
  margin-bottom: 0.3rem;
}

h1 { font-size: 1.75rem; font-weight: 800; color: var(--text); margin-bottom: 0.4rem; }
.subtitle { color: var(--text-muted); font-size: 0.9rem; margin-bottom: 1.75rem; }

.avatar-section {
  display: flex;
  align-items: center;
  gap: 1.25rem;
  padding: 1.25rem;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 14px;
  margin-bottom: 1.75rem;
}

.avatar-preview {
  width: 72px;
  height: 72px;
  border-radius: 50%;
  overflow: hidden;
  flex-shrink: 0;
  background: var(--border-color);
}

.avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.avatar-initials {
  width: 100%;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(59, 76, 202, 0.25);
  color: #a5b4fc;
  font-size: 1.4rem;
  font-weight: 800;
}

.avatar-label {
  font-weight: 700;
  color: var(--text);
  margin-bottom: 0.3rem;
}

.avatar-hint { color: var(--text-muted); font-size: 0.82rem; }

.settings-form {
  display: flex;
  flex-direction: column;
  gap: 1.1rem;
}

label {
  font-size: 0.88rem;
  font-weight: 600;
  color: var(--text);
}

input {
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 10px;
  padding: 0.65rem 0.9rem;
  color: var(--text);
  font-size: 0.95rem;
  outline: none;
  transition: border-color 0.15s;
}

input:focus { border-color: var(--secondary); }
.hint { font-size: 0.76rem; color: var(--text-muted); }

.feedback {
  padding: 0.65rem 1rem;
  border-radius: 10px;
  font-size: 0.88rem;
  font-weight: 600;
}
.feedback.error   { background: rgba(248, 113, 113, 0.12); color: #f87171; border: 1px solid rgba(248,113,113,0.3); }
.feedback.success { background: rgba(52, 211, 153, 0.12); color: #34d399; border: 1px solid rgba(52,211,153,0.3); }

.form-actions {
  display: flex;
  gap: 0.75rem;
  justify-content: flex-end;
  margin-top: 0.5rem;
}
</style>
