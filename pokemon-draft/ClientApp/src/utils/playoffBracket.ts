export interface BracketPlayer {
  playerId: string
  playerName: string
  teamName: string
}
export interface BracketSlot {
  source: string
  seed: number | null
}
export interface BracketMatch {
  id: string
  slots: [BracketSlot, BracketSlot]
  winner: number | null
}
export interface BracketRound {
  id: string
  name: string
  matches: BracketMatch[]
}
export interface BracketConfiguration {
  playIn: BracketRound[]
  playoffs: BracketRound[]
}
export interface BracketResponse {
  revision: string | null
  configuration: BracketConfiguration | null
}
export interface MatchResult {
  players: [string | null, string | null]
  winner: string | null
  loser: string | null
}

export const allRounds = (config: BracketConfiguration) => [...config.playIn, ...config.playoffs]
export const newMatch = (): BracketMatch => ({
  id: crypto.randomUUID(),
  slots: [
    { source: '', seed: null },
    { source: '', seed: null },
  ],
  winner: null,
})
export const newRound = (name: string): BracketRound => ({
  id: crypto.randomUUID(),
  name,
  matches: [newMatch()],
})

export function seedOrder(size: number): number[] {
  let seeds = [1, 2]
  while (seeds.length < size) {
    const total = seeds.length * 2 + 1
    seeds = seeds.flatMap((seed) => [seed, total - seed])
  }
  return seeds
}

export function createPlayoffs(size: number, entrants = size): BracketRound[] {
  if (![2, 4, 8, 16, 32, 64].includes(size)) throw new Error('Invalid bracket size')
  const seeds = seedOrder(size)
  const rounds: BracketRound[] = []
  for (let count = size / 2; count >= 1; count /= 2) {
    const previous = rounds.at(-1)
    const round = newRound(
      count === 1
        ? 'Final'
        : count === 2
          ? 'Semifinals'
          : count === 4
            ? 'Quarterfinals'
            : 'Round of ' + count * 2,
    )
    round.matches = Array.from({ length: count }, (_, index) => {
      const match = newMatch()
      match.slots.forEach((slot, side) => {
        if (previous) slot.source = 'winner:' + previous.matches[index * 2 + side]!.id
        else {
          const seed = seeds[index * 2 + side]!
          slot.seed = seed <= entrants ? seed : null
          slot.source = seed > entrants ? 'bye' : ''
        }
      })
      return match
    })
    rounds.push(round)
  }
  return rounds
}

export function resolveBracket(config: BracketConfiguration): Map<string, MatchResult> {
  const results = new Map<string, MatchResult>()
  for (const round of allRounds(config)) {
    const pending = new Map<string, MatchResult>()
    for (const match of round.matches) {
      const players = match.slots.map((slot) => {
        if (slot.source.startsWith('player:')) return slot.source.slice(7)
        const [kind, id] = slot.source.split(':')
        const result = id ? results.get(id) : undefined
        return kind === 'winner'
          ? (result?.winner ?? null)
          : kind === 'loser'
            ? (result?.loser ?? null)
            : null
      }) as [string | null, string | null]
      let side = match.winner
      if (side === null && players[0] && match.slots[1].source === 'bye') side = 0
      if (side === null && players[1] && match.slots[0].source === 'bye') side = 1
      const valid =
        side !== null &&
        players[side] &&
        (players[1 - side] || match.slots[1 - side]?.source === 'bye')
      pending.set(match.id, {
        players,
        winner: valid ? players[side!]! : null,
        loser: valid ? players[1 - side!]! : null,
      })
    }
    pending.forEach((result, id) => results.set(id, result))
  }
  return results
}

// A changed participant or outcome invalidates every dependent result.
export function clearDownstream(config: BracketConfiguration, changedId: string) {
  const changed = new Set([changedId])
  for (const round of allRounds(config))
    for (const match of round.matches) {
      if (match.slots.some((slot) => changed.has(slot.source.split(':')[1] ?? ''))) {
        match.winner = null
        changed.add(match.id)
      }
    }
}

export function removeDanglingSources(config: BracketConfiguration) {
  const earlier = new Set<string>()
  for (const round of allRounds(config)) {
    for (const match of round.matches)
      for (const slot of match.slots) {
        if (/^(winner|loser):/.test(slot.source) && !earlier.has(slot.source.split(':')[1]!)) {
          slot.source = ''
          match.winner = null
          clearDownstream(config, match.id)
        }
      }
    round.matches.forEach((match) => earlier.add(match.id))
  }
}

export function validateBracket(
  config: BracketConfiguration,
  players: BracketPlayer[],
): string | null {
  if (!config.playoffs.length) return 'Add at least one playoff round.'
  const results = resolveBracket(config)
  const playerIds = new Set(players.map((p) => p.playerId))
  for (const round of allRounds(config)) {
    if (!round.name.trim()) return 'Give each round a name.'
    const sources = new Set<string>(),
      participants = new Set<string>(),
      seeds = new Set<number>()
    for (const match of round.matches) {
      for (const slot of match.slots) {
        if (
          slot.seed !== null &&
          (!Number.isInteger(slot.seed) || slot.seed < 1 || slot.seed > 64 || seeds.has(slot.seed))
        )
          return round.name + ': use unique seed numbers from 1 to 64.'
        if (slot.seed !== null) seeds.add(slot.seed)
        if (slot.source === '' || slot.source === 'bye') continue
        if (sources.has(slot.source))
          return round.name + ': a participant or match outcome is used more than once.'
        sources.add(slot.source)
        if (slot.source.startsWith('player:') && !playerIds.has(slot.source.slice(7)))
          return 'Replace a player who is no longer in this league.'
      }
      for (const id of results.get(match.id)!.players) {
        if (id && participants.has(id))
          return round.name + ': a player appears in more than one slot.'
        if (id) participants.add(id)
      }
    }
  }
  return null
}
