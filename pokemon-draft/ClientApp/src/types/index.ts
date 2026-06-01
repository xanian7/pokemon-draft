export interface Pokemon {
  id: number
  /** National Pokédex species number — same for all forms of a species (e.g. 479 for all Rotom forms). */
  speciesId: number
  name: string
  spriteUrl: string
  types: string[]
  bst?: number
}

export interface LeaguePlayer {
  id: string
  name: string
  teamName: string
  teamImageUrl: string
}

export interface DraftPick {
  pickNumber: number
  round: number
  playerId: string
  pokemonId: number
}

export interface LeagueConfig {
  name: string
  players: LeaguePlayer[]
  pointLimit: number
  rounds: number
}

export interface Trade {
  id: number
  initiatorPlayerId: string
  targetPlayerId: string
  status: 'Pending' | 'Accepted' | 'Rejected' | 'Cancelled'
  proposedAt: string
  items: Array<{ fromPlayerId: string; pokemonId: number }>
}

export type DraftStatus = 'setup' | 'active' | 'complete'

export interface MatchupResponse {
  id: number
  week: number
  player1Id: string
  player1Name: string
  player1TeamName: string
  player1TeamImageUrl: string
  player2Id: string
  player2Name: string
  player2TeamName: string
  player2TeamImageUrl: string
  player1Wins: number | null
  player2Wins: number | null
  player1MatchPoints: number | null
  player2MatchPoints: number | null
}

export interface StandingRow {
  playerId: string
  playerName: string
  teamName: string
  teamImageUrl: string
  wins: number
  losses: number
  matchPoints: number
  gamesWon: number
  gamesLost: number
}

export interface WeekGroup {
  week: number
  matchups: MatchupResponse[]
}

export interface ScheduleData {
  weeks: WeekGroup[]
  standings: StandingRow[]
}

export interface NavigationButton {
  label: string
  icon: string
  route: string
  requiresAuth: boolean
  adminOnly?: boolean
}

// ── Server broadcast types (must match backend DTOs exactly) ─────────────────

export interface ServerPlayerResponse {
  id: string
  name: string
  teamName: string
  teamImageUrl: string
}

export interface ServerDraftPick {
  pickNumber: number
  round: number
  playerId: string
  pokemonId: number
}

export interface ServerDraftState {
  status: string
  currentPickNumber: number
  totalPicks: number
  currentPickerId: string | null
  currentPickerName: string | null
  picks: ServerDraftPick[]
}

export interface ServerLeagueResponse {
  code: string
  name: string
  pointLimit: number
  rounds: number
  playoffSpots: number
  regulationSet: string
  players: ServerPlayerResponse[]
  pointValues: Record<number, number>
  draft: ServerDraftState
}
