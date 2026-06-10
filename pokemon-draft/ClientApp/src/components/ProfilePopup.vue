<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const authStore = useAuthStore()
const router = useRouter()

const avatarInitials = computed(() => {
  const name = authStore.playerName
  if (!name) return '?'
  return name
    .split(' ')
    .map((word) => word[0])
    .join('')
    .slice(0, 2)
    .toUpperCase()
})
</script>

<template>
  <div v-if="authStore.playerId" class="profile-popup">
    <v-menu min-width="240px" location="bottom end" offset="8">
      <template #activator="{ props }">
        <v-btn
          v-bind="props"
          icon
          variant="text"
          class="profile-trigger"
          aria-label="Open profile menu"
        >
          <v-avatar size="38" class="profile-avatar">
            <v-img v-if="authStore.teamImageUrl" :src="authStore.teamImageUrl" />
            <span v-else>{{ avatarInitials }}</span>
          </v-avatar>
        </v-btn>
      </template>

      <v-card>
        <v-card-text>
          <div class="profile-menu">
            <v-avatar size="52" class="profile-avatar">
              <v-img v-if="authStore.teamImageUrl" :src="authStore.teamImageUrl" />
              <span v-else>{{ avatarInitials }}</span>
            </v-avatar>
            <h3>{{ authStore.playerName }}</h3>
            <p class="team-name">{{ authStore.teamName }}</p>
            <v-divider class="my-3" />
            <v-btn
              variant="text"
              width="100%"
              prepend-icon="mdi-cog-outline"
              @click="router.push('/settings')"
            >
              Settings
            </v-btn>
            <v-btn
              variant="text"
              width="100%"
              prepend-icon="mdi-view-grid-outline"
              @click="router.push('/my-leagues')"
            >
              My Leagues
            </v-btn>
            <v-btn
              variant="text"
              width="100%"
              prepend-icon="mdi-logout"
              color="error"
              @click="authStore.signOut()"
            >
              Logout
            </v-btn>
          </div>
        </v-card-text>
      </v-card>
    </v-menu>
  </div>
</template>

<style scoped>
.profile-popup {
  display: flex;
  align-items: center;
}

.profile-trigger {
  width: 46px;
  height: 46px;
}

.profile-avatar {
  color: #fff;
  font-size: 0.8rem;
  font-weight: 800;
  background: linear-gradient(135deg, var(--primary), #2ab6ff);
  border: 1px solid rgba(255, 255, 255, 0.2);
  box-shadow: 0 8px 20px rgba(var(--primary-rgb), 0.2);
}

.profile-menu {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
}

.profile-menu h3 {
  margin-top: 10px;
  font-size: 1rem;
  font-weight: 800;
}

.team-name {
  margin-top: 4px;
  color: var(--text-muted);
  font-size: 0.82rem;
}

.profile-menu :deep(.v-btn) {
  justify-content: flex-start;
  padding-inline: 12px;
}
</style>
