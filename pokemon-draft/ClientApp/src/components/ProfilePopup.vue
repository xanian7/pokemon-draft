<script setup lang="ts">
import { computed } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { RouterLink } from 'vue-router'
const authStore = useAuthStore()

const avatarInitials = computed(() => {
  const name = authStore.playerName
  if (!name) return '?'
  return name
    .split(' ')
    .map((w) => w[0])
    .join('')
    .slice(0, 2)
    .toUpperCase()
})
</script>

<template>
  <v-container fluid>
    <v-row class="justify-center" v-if="authStore.playerId">
      <v-menu min-width="200px">
        <template v-slot:activator="{ props }">
          <v-btn icon v-bind="props">
            <v-avatar size="large" border>
              <v-img v-if="authStore.teamImageUrl" :src="authStore.teamImageUrl" />
              <span v-else class="text-headline-small">{{ avatarInitials }}</span>
            </v-avatar>
          </v-btn>
        </template>
        <v-card>
          <v-card-text>
            <div class="mx-auto text-center">
              <v-avatar border>
                <v-img v-if="authStore.teamImageUrl" :src="authStore.teamImageUrl" />
                <span v-else class="text-headline-small">{{ avatarInitials }}</span>
              </v-avatar>
              <h3>{{ authStore.playerName }}</h3>
              <p class="team-name">
                {{ authStore.teamName }}
              </p>
              <v-divider></v-divider>
              <v-btn variant="text" width="100%">
                <router-link to="/settings">Settings</router-link>
              </v-btn>
              <v-divider></v-divider>
              <v-btn variant="text" width="100%">
                <router-link to="/my-leagues">Return to My Leagues</router-link>
              </v-btn>
              <v-divider></v-divider>
              <v-btn variant="text" width="100%" @click="authStore.signOut()">
                Logout
              </v-btn>
            </div>
          </v-card-text>
        </v-card>
      </v-menu>
    </v-row>
  </v-container>
</template>

<style scoped>
.team-name {
  margin-top: 4px;
}

.v-btn {
  padding-left: 8px;
  padding-right: 8px;
}
</style>
