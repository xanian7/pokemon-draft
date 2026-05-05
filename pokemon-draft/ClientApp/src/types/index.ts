export interface Pokemon {
  id: number
  name: string
  spriteUrl: string
  types: string[]
  bst?: number
}

export interface LeaguePlayer {
  id: string
  name: string
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
