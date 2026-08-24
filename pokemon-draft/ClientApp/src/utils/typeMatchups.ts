import { ATTACK_TYPES, TYPE_CHART } from '@/data/typeChart'
import type { Pokemon } from '@/types'

export interface TypeExposure {
  type: string
  weakCount: number
  resistCount: number
  immuneCount: number
  fourTimesCount: number
  maxMultiplier: number
}

const attackTypes = [...ATTACK_TYPES]

export function typeMultiplier(attackType: string, defenderTypes: string[]) {
  return defenderTypes.reduce(
    (multiplier, defenderType) =>
      multiplier * (TYPE_CHART[attackType]?.[defenderType] ?? 1),
    1,
  )
}

export function pokemonWeaknesses(pokemon: Pokemon) {
  return attackTypes
    .map((type) => ({ type, multiplier: typeMultiplier(type, pokemon.types) }))
    .filter((entry) => entry.multiplier > 1)
    .sort((a, b) => b.multiplier - a.multiplier || a.type.localeCompare(b.type))
}

export function rosterWeaknesses(roster: Pokemon[]): TypeExposure[] {
  return attackTypes
    .map((type) => {
      const multipliers = roster.map((pokemon) => typeMultiplier(type, pokemon.types))
      return {
        type,
        weakCount: multipliers.filter((value) => value > 1).length,
        resistCount: multipliers.filter((value) => value > 0 && value < 1).length,
        immuneCount: multipliers.filter((value) => value === 0).length,
        fourTimesCount: multipliers.filter((value) => value >= 4).length,
        maxMultiplier: Math.max(1, ...multipliers),
      }
    })
    .filter((entry) => entry.weakCount > 0)
    .sort(
      (a, b) =>
        b.weakCount * 2 +
          b.fourTimesCount -
          b.resistCount * 0.5 -
          (a.weakCount * 2 + a.fourTimesCount - a.resistCount * 0.5) ||
        b.maxMultiplier - a.maxMultiplier ||
        a.type.localeCompare(b.type),
    )
}

export function pressureTypes(attackingRoster: Pokemon[], defendingRoster: Pokemon[]) {
  const availableTypes = new Set(attackingRoster.flatMap((pokemon) => pokemon.types))
  return rosterWeaknesses(defendingRoster).filter((entry) => availableTypes.has(entry.type))
}
