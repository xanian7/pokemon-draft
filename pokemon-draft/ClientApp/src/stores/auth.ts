import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

const SESSION_KEY = 'pokemon-draft:session'

interface Session {
  leagueCode: string
  playerId: string
  playerName: string
  isAdmin: boolean
  pin: string
}

const API_BASE = import.meta.env.DEV ? 'http://localhost:5050/api' : '/api'

export const useAuthStore = defineStore('auth', () => {
  const session = ref<Session | null>(null)

  function loadSession() {
    const stored = sessionStorage.getItem(SESSION_KEY)
    if (stored) {
      try {
        session.value = JSON.parse(stored)
      } catch {
        session.value = null
      }
    }
  }

  function saveSession(s: Session) {
    session.value = s
    sessionStorage.setItem(SESSION_KEY, JSON.stringify(s))
  }

  function clearSession() {
    session.value = null
    sessionStorage.removeItem(SESSION_KEY)
  }

  async function join(leagueCode: string, pin: string): Promise<string | null> {
    try {
      const res = await fetch(`${API_BASE}/auth/join`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ leagueCode: leagueCode.trim().toUpperCase(), pin: pin.trim() }),
      })

      if (res.status === 401) return 'Invalid league code or PIN.'
      if (!res.ok) return 'Server error. Please try again.'

      const data = await res.json()
      saveSession({
        leagueCode: leagueCode.trim().toUpperCase(),
        playerId: data.playerId,
        playerName: data.playerName,
        isAdmin: data.isAdmin,
        pin: pin.trim(),
      })
      return null
    } catch {
      return 'Could not connect to server.'
    }
  }

  const isAuthenticated = computed(() => session.value !== null)
  const leagueCode = computed(() => session.value?.leagueCode ?? null)
  const playerId = computed(() => session.value?.playerId ?? null)
  const playerName = computed(() => session.value?.playerName ?? null)
  const isAdmin = computed(() => session.value?.isAdmin ?? false)
  const pin = computed(() => session.value?.pin ?? '')

  loadSession()

  return {
    session,
    isAuthenticated,
    leagueCode,
    playerId,
    playerName,
    isAdmin,
    pin,
    join,
    clearSession,
    saveSession,
  }
})
