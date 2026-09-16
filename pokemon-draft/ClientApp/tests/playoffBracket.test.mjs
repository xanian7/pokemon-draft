import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  createPlayoffs,
  newRound,
  newMatch,
  resolveBracket,
  clearDownstream,
  removeDanglingSources,
  validateBracket,
} from '../src/utils/playoffBracket.ts'

const players = ['a', 'b', 'c', 'd'].map((playerId) => ({
  playerId,
  playerName: playerId,
  teamName: '',
}))
const setPlayer = (match, side, id) => {
  match.slots[side].source = 'player:' + id
}

test('standard seeding and explicit byes for a six-player bracket', () => {
  const rounds = createPlayoffs(8, 6)
  assert.deepEqual(
    rounds[0].matches.map((m) => m.slots.map((s) => s.seed)),
    [
      [1, null],
      [4, 5],
      [2, null],
      [3, 6],
    ],
  )
  setPlayer(rounds[0].matches[0], 0, 'a')
  const result = resolveBracket({ playIn: [], playoffs: rounds })
  assert.equal(result.get(rounds[0].matches[0].id).winner, 'a')
  assert.equal(result.get(rounds[1].matches[0].id).players[0], 'a')
  assert.equal(result.get(rounds[1].matches[0].id).winner, null)
})

test('winner and loser links support second-chance play-in games', () => {
  const first = newRound('First'),
    second = newRound('Second')
  setPlayer(first.matches[0], 0, 'a')
  setPlayer(first.matches[0], 1, 'b')
  first.matches[0].winner = 0
  second.matches[0].slots[0].source = 'loser:' + first.matches[0].id
  setPlayer(second.matches[0], 1, 'c')
  second.matches[0].winner = 0
  const playoffs = createPlayoffs(2)
  playoffs[0].matches[0].slots[0].source = 'winner:' + first.matches[0].id
  playoffs[0].matches[0].slots[1].source = 'winner:' + second.matches[0].id
  const config = { playIn: [first, second], playoffs }
  assert.deepEqual(resolveBracket(config).get(playoffs[0].matches[0].id).players, ['a', 'b'])
  assert.equal(validateBracket(config, players), null)
})

test('changing an upstream result clears all dependent results', () => {
  const config = { playIn: [], playoffs: createPlayoffs(8) }
  for (const round of config.playoffs) for (const match of round.matches) match.winner = 0
  clearDownstream(config, config.playoffs[0].matches[0].id)
  assert.equal(config.playoffs[1].matches[0].winner, null)
  assert.equal(config.playoffs[2].matches[0].winner, null)
  assert.equal(config.playoffs[1].matches[1].winner, 0)
})

test('removing a round clears orphaned slots and downstream results', () => {
  const config = { playIn: [], playoffs: createPlayoffs(8) }
  for (const round of config.playoffs) for (const match of round.matches) match.winner = 0
  config.playoffs.shift()
  removeDanglingSources(config)
  assert.equal(config.playoffs[0].matches[0].slots[0].source, '')
  assert.equal(config.playoffs[1].matches[0].winner, null)
})

test('duplicate participants and seeds are rejected but incomplete brackets can be saved', () => {
  const config = { playIn: [], playoffs: createPlayoffs(4) }
  assert.equal(validateBracket(config, players), null)
  setPlayer(config.playoffs[0].matches[0], 0, 'a')
  setPlayer(config.playoffs[0].matches[1], 0, 'a')
  assert.match(validateBracket(config, players), /more than once/)
  setPlayer(config.playoffs[0].matches[1], 0, 'b')
  config.playoffs[0].matches[1].slots[0].seed = 1
  assert.match(validateBracket(config, players), /unique seed/)
})

test('unresolved participants cannot advance even with a stale selected winner', () => {
  const match = newMatch()
  setPlayer(match, 0, 'a')
  match.winner = 0
  const config = { playIn: [], playoffs: [{ ...newRound('Final'), matches: [match] }] }
  assert.equal(resolveBracket(config).get(match.id).winner, null)
})
