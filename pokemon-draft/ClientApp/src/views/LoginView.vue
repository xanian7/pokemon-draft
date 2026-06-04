<script lang="ts" setup>
import LoginForm from '@/components/LoginForm.vue'
import { useAuthStore } from '@/stores/auth'
import { useRouter } from 'vue-router'
import { onMounted } from 'vue'

const router = useRouter()
const authStore = useAuthStore()

onMounted(() => {
  if (authStore.isAuthenticated) {
    router.push('/league')
  } else if (authStore.isSignedIn) {
    // Discord-signed-in but no active league session → let them pick a league
    router.push('/my-leagues')
  }
})
</script>

<template>
    <v-container class="fill-height" fluid>
        <v-row align="center" justify="center">
            <v-col cols="12" sm="8" md="4">
                <LoginForm />
            </v-col>
        </v-row>
    </v-container>
</template>

<style scoped>  

</style>
