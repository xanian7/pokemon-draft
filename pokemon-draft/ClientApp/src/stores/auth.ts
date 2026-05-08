import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

const SESSION_KEY = 'pokemon-draft:session'
const RECENT_LEAGUES_KEY = 'pokemon-draft:recent-leagues'

interface Session {
  leagueCode: string
  leagueName: string
  playerId: string
  playerName: string
  isAdmin: boolean
  pin: string
  teamName: string
  teamImageUrl: string
}

interface RecentLeague {
  code: string
  name: string
}

const API_BASE = import.meta.env.DEV ? 'http://localhost:5050/api' : '/api'

function loadRecentLeagues(): RecentLeague[] {
  try {
    const stored = localStorage.getItem(RECENT_LEAGUES_KEY)
    return stored ? JSON.parse(stored) : []
  } catch {
    return []
  }
}

function persistRecentLeague(league: RecentLeague) {
  const list = loadRecentLeagues()
  const updated = [league, ...list.filter((l) => l.code !== league.code)].slice(0, 5)
  localStorage.setItem(RECENT_LEAGUES_KEY, JSON.stringify(updated))
}

export const useAuthStore = defineStore('auth', () => {
  const session = ref<Session | null>(null)
  const recentLeagues = ref<RecentLeague[]>(loadRecentLeagues())

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
      const code = leagueCode.trim().toUpperCase()
      const lName = data.leagueName ?? ''

      saveSession({
        leagueCode: code,
        leagueName: lName,
        playerId: data.playerId,
        playerName: data.playerName,
        isAdmin: data.isAdmin,
        pin: pin.trim(),
        teamName: data.teamName ?? '',
        teamImageUrl: data.teamImageUrl ?? '',
      })

      persistRecentLeague({ code, name: lName })
      recentLeagues.value = loadRecentLeagues()

      return null
    } catch {
      return 'Could not connect to server.'
    }
  }

  const isAuthenticated = computed(() => session.value !== null)
  const leagueCode = computed(() => session.value?.leagueCode ?? null)
  const leagueName = computed(() => session.value?.leagueName ?? '')
  const playerId = computed(() => session.value?.playerId ?? null)
  const playerName = computed(() => session.value?.playerName ?? null)
  const isAdmin = computed(() => session.value?.isAdmin ?? false)
  const pin = computed(() => session.value?.pin ?? '')
  const teamName = computed(() => session.value?.teamName ?? '')
  const teamImageUrl = computed(() => session.value?.teamImageUrl ?? '')

  async function updateProfile(teamName: string, teamImageUrl: string): Promise<string | null> {
    if (!session.value) return 'Not logged in.'
    try {
      const res = await fetch(
        `${API_BASE}/leagues/${session.value.leagueCode}/players/${session.value.playerId}/profile`,
        {
          method: 'PATCH',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            playerId: session.value.playerId,
            pin: session.value.pin,
            teamName: teamName || null,
            teamImageUrl: teamImageUrl || null,
          }),
        },
      )
      if (!res.ok) {
        const text = await res.text()
        return text || 'Failed to update profile.'
      }
      saveSession({ ...session.value, teamName, teamImageUrl })
      return null
    } catch {
      return 'Could not connect to server.'
    }
  }

  loadSession()

  return {
    session,
    recentLeagues,
    isAuthenticated,
    leagueCode,
    leagueName,
    playerId,
    playerName,
    isAdmin,
    pin,
    teamName,
    teamImageUrl,
    join,
    updateProfile,
    clearSession,
    saveSession,
  }
})
