<script setup lang="ts">
import { useSignalR } from '@/services/signalr'
import { useAuthStore } from '@/stores/auth'

const { isConnected } = useSignalR()
const auth = useAuthStore()
</script>

<template>
  <Transition name="banner">
    <div v-if="auth.isAuthenticated && !isConnected" class="connection-banner" role="alert">
      <span class="banner-dot" aria-hidden="true" />
      Reconnecting to server&hellip;
    </div>
  </Transition>
</template>

<style scoped>
.connection-banner {
  position: fixed;
  bottom: 1.25rem;
  left: 50%;
  transform: translateX(-50%);
  z-index: 9999;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.6rem 1.25rem;
  background: #1a1a1a;
  border: 1px solid rgba(255, 200, 0, 0.45);
  color: #ffc800;
  border-radius: 8px;
  font-size: 0.85rem;
  font-weight: 600;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.55);
  white-space: nowrap;
  pointer-events: none;
}

.banner-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #ffc800;
  flex-shrink: 0;
  animation: pulse 1s ease-in-out infinite;
}

@keyframes pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.25;
  }
}

.banner-enter-active,
.banner-leave-active {
  transition:
    opacity 0.25s ease,
    transform 0.25s ease;
}

.banner-enter-from,
.banner-leave-to {
  opacity: 0;
  transform: translateX(-50%) translateY(8px);
}
</style>
