<script setup lang="ts">
import { computed } from 'vue'
import MasterBall from '../../public/Master-Ball.png'
import { useAuthStore } from '@/stores/auth'
import ProfilePopup from '@/components/ProfilePopup.vue'
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
  <v-app-bar app>
    <v-toolbar-title>
      <v-row class="align-center">
        <v-col cols="auto" class="logo d-flex align-center">
          <v-img :src="MasterBall" width="40" height="40" />
        </v-col>
        <v-col class="d-flex align-center">
          <span>Pokemon Draft</span>
        </v-col>
        <v-spacer />
        <v-col cols="auto" class="user d-flex align-center">
          <ProfilePopup />
        </v-col>
      </v-row>
    </v-toolbar-title>
  </v-app-bar>
</template>

<style scoped>
.logo {
  padding-left: 8px;
}

.user {
  padding-right: 8px;
}
</style>
