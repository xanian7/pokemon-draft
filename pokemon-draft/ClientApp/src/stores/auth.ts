import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { apiPost, apiGet, apiPatch } from '@/services/api'

interface Session {
  leagueCode: string
  leagueName: string
  playerId: string
  playerName: string
  isAdmin: boolean
  pin: string
  teamName: string
  teamImageUrl: string
  timeZone: string
  availability: string
}

interface RecentLeague {
  code: string
  name: string
}

export interface MyLeague {
  code: string
  name: string
  playerId: string
  playerName: string
  teamName: string
  teamImageUrl: string
  isCommissioner: boolean
}

export interface AuthUser {
  id: string
  email: string
  name: string
  pictureUrl: string
}

const SESSION_KEY = 'pokemon-draft:session'
const RECENT_LEAGUES_KEY = 'pokemon-draft:recent-leagues'
const AUTH_TOKEN_KEY = 'pokemon-draft:auth-token'
const AUTH_USER_KEY = 'pokemon-draft:auth-user'

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

  // ── Global auth identity (persisted in localStorage, works for Google + Discord) ─
  const authToken = ref<string | null>(localStorage.getItem(AUTH_TOKEN_KEY))

  function loadAuthUser(): AuthUser | null {
    try {
      const stored = localStorage.getItem(AUTH_USER_KEY)
      if (!stored) return null
      const user = JSON.parse(stored)
      // Migrate old 'picture' field name to 'pictureUrl'
      if (user.picture !== undefined && user.pictureUrl === undefined) {
        user.pictureUrl = user.picture
        delete user.picture
      }
      return user
    } catch {
      return null
    }
  }
  const authUser = ref<AuthUser | null>(loadAuthUser())

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

  function saveAuthUser(token: string, user: AuthUser) {
    authToken.value = token
    authUser.value = user
    localStorage.setItem(AUTH_TOKEN_KEY, token)
    localStorage.setItem(AUTH_USER_KEY, JSON.stringify(user))
  }

  function clearAuthUser() {
    authToken.value = null
    authUser.value = null
    localStorage.removeItem(AUTH_TOKEN_KEY)
    localStorage.removeItem(AUTH_USER_KEY)
  }

  /** Returns authorization headers for API calls when signed in with Google. */
  function authHeaders(): Record<string, string> {
    return authToken.value ? { Authorization: `Bearer ${authToken.value}` } : {}
  }

  // ── Sign-In ──────────────────────────────────────────────────────────────────
  async function signInWithGoogle(idToken: string): Promise<string | null> {
    const result = await apiPost<{ token: string; user: AuthUser }>('/auth/google', { idToken })
    if (result.error) return result.error
    saveAuthUser(result.data!.token, result.data!.user)
    return null
  }

  function signOut() {
    clearAuthUser()
    clearSession()
  }

  // ── My Leagues ───────────────────────────────────────────────────────────────
  async function fetchMyLeagues() {
    if (!authToken.value) return []
    const result = await apiGet<MyLeague[]>('/auth/my-leagues', authHeaders())
    return result.data ?? []
  }

  /** Enter a league the Google user is already a member of (no PIN required). */
  async function enterLeague(leagueCode: string): Promise<string | null> {
    if (!authToken.value) return 'Not signed in.'
    const code = leagueCode.trim().toUpperCase()
    const result = await apiPost<{
      playerId: string
      playerName: string
      isAdmin: boolean
      leagueName: string
      teamName: string
      teamImageUrl: string
      timeZone: string
      availability: string
      sessionToken: string
    }>('/auth/enter-league', { leagueCode: code }, authHeaders())
    if (result.error) return result.error
    const data = result.data!
    const lName = data.leagueName ?? ''
    saveSession({
      leagueCode: code,
      leagueName: lName,
      playerId: data.playerId,
      playerName: data.playerName,
      isAdmin: data.isAdmin,
      pin: data.sessionToken,
      teamName: data.teamName ?? '',
      teamImageUrl: data.teamImageUrl ?? '',
      timeZone: data.timeZone ?? '',
      availability: data.availability ?? '',
    })
    persistRecentLeague({ code, name: lName })
    recentLeagues.value = loadRecentLeagues()
    return null
  }

  async function linkPlayer(leagueCode: string, pin: string): Promise<string | null> {
    if (!authToken.value) return 'Not signed in.'
    const code = leagueCode.trim().toUpperCase()
    const result = await apiPost('/auth/link-player', { leagueCode: code, pin: pin.trim() }, authHeaders())
    return result.error
  }

  // ── PIN-based join (existing) ────────────────────────────────────────────────
  async function join(leagueCode: string, pin: string): Promise<string | null> {
    const code = leagueCode.trim().toUpperCase()
    const trimmedPin = pin.trim()
    const result = await apiPost<{
      playerId: string
      playerName: string
      isAdmin: boolean
      leagueName: string
      teamName: string
      teamImageUrl: string
      timeZone: string
      availability: string
    }>('/auth/join', { leagueCode: code, pin: trimmedPin })
    if (result.error) return result.error
    const data = result.data!
    const lName = data.leagueName ?? ''
    saveSession({
      leagueCode: code,
      leagueName: lName,
      playerId: data.playerId,
      playerName: data.playerName,
      isAdmin: data.isAdmin,
      pin: trimmedPin,
      teamName: data.teamName ?? '',
      teamImageUrl: data.teamImageUrl ?? '',
      timeZone: data.timeZone ?? '',
      availability: data.availability ?? '',
    })
    persistRecentLeague({ code, name: lName })
    recentLeagues.value = loadRecentLeagues()
    return null
  }

  // ── Computed ─────────────────────────────────────────────────────────────────
  const isAuthenticated = computed(() => session.value !== null)
  const isSignedIn = computed(() => authUser.value !== null)
  const leagueCode = computed(() => session.value?.leagueCode ?? null)
  const leagueName = computed(() => session.value?.leagueName ?? '')
  const playerId = computed(() => session.value?.playerId ?? null)
  const playerName = computed(() => session.value?.playerName ?? null)
  const isAdmin = computed(() => session.value?.isAdmin ?? false)
  const pin = computed(() => session.value?.pin ?? '')
  const teamName = computed(() => session.value?.teamName ?? '')
  const teamImageUrl = computed(() => session.value?.teamImageUrl ?? '')
  const timeZone = computed(() => session.value?.timeZone ?? '')
  const availability = computed(() => session.value?.availability ?? '')

  async function updateProfile(
    teamName: string,
    teamImageUrl: string,
    timeZone: string,
    availability: string,
  ): Promise<string | null> {
    if (!session.value) return 'Not logged in.'
    const result = await apiPatch(
      `/leagues/${session.value.leagueCode}/players/${session.value.playerId}/profile`,
      {
        playerId: session.value.playerId,
        pin: session.value.pin,
        teamName: teamName || null,
        teamImageUrl: teamImageUrl || null,
        timeZone: timeZone || null,
        availability: availability || null,
      },
    )
    if (result.error) return result.error
    saveSession({ ...session.value, teamName, teamImageUrl, timeZone, availability })
    return null
  }

  loadSession()

  return {
    session,
    recentLeagues,
    authUser,
    authToken,
    isAuthenticated,
    isSignedIn,
    leagueCode,
    leagueName,
    playerId,
    playerName,
    isAdmin,
    pin,
    teamName,
    teamImageUrl,
    timeZone,
    availability,
    authHeaders,
    saveAuthUser,
    signInWithGoogle,
    signOut,
    fetchMyLeagues,
    enterLeague,
    linkPlayer,
    join,
    updateProfile,
    clearSession,
    saveSession,
  }
})
