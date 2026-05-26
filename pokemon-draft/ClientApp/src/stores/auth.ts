import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

const SESSION_KEY = 'pokemon-draft:session'
const RECENT_LEAGUES_KEY = 'pokemon-draft:recent-leagues'
const AUTH_TOKEN_KEY = 'pokemon-draft:auth-token'
const AUTH_USER_KEY = 'pokemon-draft:auth-user'

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

export interface GoogleUser {
  id: string
  email: string
  name: string
  picture: string
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
  // ── League session (PIN-based, per tab) ─────────────────────────────────────
  const session = ref<Session | null>(null)
  const recentLeagues = ref<RecentLeague[]>(loadRecentLeagues())

  // ── Global Google identity (persisted in localStorage) ───────────────────────
  const authToken = ref<string | null>(localStorage.getItem(AUTH_TOKEN_KEY))

  function loadGoogleUser(): GoogleUser | null {
    try {
      const stored = localStorage.getItem(AUTH_USER_KEY)
      return stored ? JSON.parse(stored) : null
    } catch {
      return null
    }
  }
  const googleUser = ref<GoogleUser | null>(loadGoogleUser())

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

  function saveGoogleUser(token: string, user: GoogleUser) {
    authToken.value = token
    googleUser.value = user
    localStorage.setItem(AUTH_TOKEN_KEY, token)
    localStorage.setItem(AUTH_USER_KEY, JSON.stringify(user))
  }

  function clearGoogleUser() {
    authToken.value = null
    googleUser.value = null
    localStorage.removeItem(AUTH_TOKEN_KEY)
    localStorage.removeItem(AUTH_USER_KEY)
  }

  /** Returns authorization headers for API calls when signed in with Google. */
  function authHeaders(): Record<string, string> {
    return authToken.value ? { Authorization: `Bearer ${authToken.value}` } : {}
  }

  // ── Google Sign-In ───────────────────────────────────────────────────────────
  async function signInWithGoogle(idToken: string): Promise<string | null> {
    try {
      const res = await fetch(`${API_BASE}/auth/google`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ idToken }),
      })
      if (!res.ok) return 'Google sign-in failed. Please try again.'
      const data = await res.json()
      saveGoogleUser(data.token, data.user)
      return null
    } catch {
      return 'Could not connect to server.'
    }
  }

  function signOut() {
    clearGoogleUser()
    clearSession()
  }

  // ── My Leagues ───────────────────────────────────────────────────────────────
  async function fetchMyLeagues() {
    if (!authToken.value) return []
    try {
      const res = await fetch(`${API_BASE}/auth/my-leagues`, {
        headers: authHeaders(),
      })
      if (!res.ok) return []
      return await res.json()
    } catch {
      return []
    }
  }

  /** Enter a league the Google user is already a member of (no PIN required). */
  async function enterLeague(leagueCode: string): Promise<string | null> {
    if (!authToken.value) return 'Not signed in.'
    try {
      const res = await fetch(`${API_BASE}/auth/enter-league`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...authHeaders() },
        body: JSON.stringify({ leagueCode: leagueCode.trim().toUpperCase() }),
      })
      if (res.status === 404) return 'You are not a member of this league.'
      if (!res.ok) return 'Could not enter league.'
      const data = await res.json()
      const code = leagueCode.trim().toUpperCase()
      const lName = data.leagueName ?? ''
      saveSession({
        leagueCode: code,
        leagueName: lName,
        playerId: data.playerId,
        playerName: data.playerName,
        isAdmin: data.isAdmin,
        pin: data.sessionToken ?? data.pin ?? '',
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

  // ── PIN-based join (existing) ────────────────────────────────────────────────
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

  // ── Computed ─────────────────────────────────────────────────────────────────
  const isAuthenticated = computed(() => session.value !== null)
  const isSignedInWithGoogle = computed(() => googleUser.value !== null)
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
    googleUser,
    authToken,
    isAuthenticated,
    isSignedInWithGoogle,
    leagueCode,
    leagueName,
    playerId,
    playerName,
    isAdmin,
    pin,
    teamName,
    teamImageUrl,
    authHeaders,
    signInWithGoogle,
    signOut,
    fetchMyLeagues,
    enterLeague,
    join,
    updateProfile,
    clearSession,
    saveSession,
  }
})
