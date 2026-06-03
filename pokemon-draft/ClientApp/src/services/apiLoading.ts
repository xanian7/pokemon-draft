import { computed, ref } from 'vue'

const activeRequests = ref(0)
let installed = false

export const isApiLoading = computed(() => activeRequests.value > 0)

export function installApiLoadingTracker() {
  if (installed || typeof window === 'undefined') return

  installed = true
  const nativeFetch = window.fetch.bind(window)

  window.fetch = async (...args) => {
    activeRequests.value += 1
    try {
      return await nativeFetch(...args)
    } finally {
      activeRequests.value = Math.max(0, activeRequests.value - 1)
    }
  }
}
