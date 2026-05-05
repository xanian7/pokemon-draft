import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { Pokemon } from '@/types'

const CACHE_KEY = 'pokemon-draft:pokemon-cache:v2'
const POINTS_KEY = 'pokemon-draft:point-values'
const CACHE_TTL = 24 * 60 * 60 * 1000 // 24 hours

interface CacheData {
  pokemon: Pokemon[]
  timestamp: number
}

function bstToPoints(bst: number): number {
  if (bst >= 720) return 20
  if (bst >= 680) return 18
  if (bst >= 640) return 16
  if (bst >= 600) return 14
  if (bst >= 570) return 12
  if (bst >= 540) return 10
  if (bst >= 510) return 9
  if (bst >= 480) return 8
  if (bst >= 460) return 7
  if (bst >= 440) return 6
  if (bst >= 420) return 5
  if (bst >= 400) return 4
  if (bst >= 380) return 3
  if (bst >= 360) return 2
  return 1
}

// Single GraphQL request returns all Pokémon with types and stats — much faster than 1000+ REST calls
const GRAPHQL_QUERY = `
  query {
    pokemon_v2_pokemon(
      where: { is_default: { _eq: true } }
      order_by: { id: asc }
    ) {
      id
      name
      pokemon_v2_pokemontypes {
        pokemon_v2_type {
          name
        }
      }
      pokemon_v2_pokemonstats {
        base_stat
      }
    }
  }
`

export const usePokemonStore = defineStore('pokemon', () => {
  const allPokemon = ref<Pokemon[]>([])
  const pointValues = ref<Record<number, number>>({})
  const isLoading = ref(false)
  const error = ref<string | null>(null)

  function loadPointValues() {
    const stored = localStorage.getItem(POINTS_KEY)
    if (stored) {
      try {
        pointValues.value = JSON.parse(stored)
      } catch {
        pointValues.value = {}
      }
    }
  }

  function savePointValues() {
    localStorage.setItem(POINTS_KEY, JSON.stringify(pointValues.value))
  }

  async function fetchAllPokemon() {
    const cached = localStorage.getItem(CACHE_KEY)
    if (cached) {
      try {
        const data: CacheData = JSON.parse(cached)
        if (Date.now() - data.timestamp < CACHE_TTL) {
          allPokemon.value = data.pokemon
          return
        }
      } catch {
        // stale or corrupt cache — re-fetch
      }
    }

    isLoading.value = true
    error.value = null

    try {
      const res = await fetch('https://beta.pokeapi.co/graphql/v1beta', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ query: GRAPHQL_QUERY }),
      })

      if (!res.ok) throw new Error(`HTTP ${res.status}`)

      const { data } = await res.json()

      allPokemon.value = data.pokemon_v2_pokemon.map((p: any) => ({
        id: p.id,
        name: p.name,
        spriteUrl: `https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/${p.id}.png`,
        types: p.pokemon_v2_pokemontypes.map((t: any) => t.pokemon_v2_type.name),
        bst: p.pokemon_v2_pokemonstats.reduce(
          (sum: number, stat: { base_stat: number }) => sum + stat.base_stat,
          0,
        ),
      }))

      localStorage.setItem(
        CACHE_KEY,
        JSON.stringify({ pokemon: allPokemon.value, timestamp: Date.now() }),
      )
    } catch {
      error.value = 'Failed to load Pokémon data. Please check your connection and try again.'
    } finally {
      isLoading.value = false
    }
  }

  function setPointValue(pokemonId: number, value: number) {
    if (value <= 0) {
      delete pointValues.value[pokemonId]
    } else {
      pointValues.value[pokemonId] = value
    }
    savePointValues()
  }

  function applyDefaultPoints(pokemonIds?: number[]) {
    const targetIds = pokemonIds ? new Set(pokemonIds) : null
    for (const pokemon of allPokemon.value) {
      if (!targetIds || targetIds.has(pokemon.id)) {
        setPointValue(pokemon.id, bstToPoints(pokemon.bst ?? 300))
      }
    }
  }

  function getPointValue(pokemonId: number): number {
    return pointValues.value[pokemonId] ?? 0
  }

  const pokemonWithPoints = computed(() =>
    allPokemon.value.map((p) => ({
      ...p,
      pointValue: pointValues.value[p.id] ?? 0,
    })),
  )

  const allTypes = computed(() => {
    const types = new Set<string>()
    allPokemon.value.forEach((p) => p.types.forEach((t) => types.add(t)))
    return [...types].sort()
  })

  function getPokemonById(id: number): Pokemon | undefined {
    return allPokemon.value.find((p) => p.id === id)
  }

  loadPointValues()

  return {
    allPokemon,
    pointValues,
    isLoading,
    error,
    pokemonWithPoints,
    allTypes,
    fetchAllPokemon,
    setPointValue,
    applyDefaultPoints,
    getPointValue,
    getPokemonById,
  }
})
