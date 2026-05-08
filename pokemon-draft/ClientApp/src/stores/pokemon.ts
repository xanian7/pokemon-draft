import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { Pokemon } from '@/types'

const CACHE_KEY = 'pokemon-draft:pokemon-cache:v4'
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

// Forms that are cosmetic only (same types/stats as base, purely visual differences)
// These would appear as separate rows in the API but shouldn't be separate draftable Pokémon.
const COSMETIC_FORM_PREFIXES = [
  'pikachu-',      // costume/cap variants (pikachu-rock-star, pikachu-original-cap, etc.)
  'minior-',       // color variants of meteor and core forms (only default minior is needed)
  'squawkabilly-', // plumage color variants (same stats/types)
  'koraidon-',     // riding/mode builds (traversal forms)
  'miraidon-',     // riding/mode builds (traversal forms)
]

const COSMETIC_FORM_NAMES = new Set([
  'eevee-starter',      // special partner eevee
  'magearna-original',  // original color Magearna (cosmetic)
  'zarude-dada',        // dada Zarude (cosmetic)
  'gimmighoul-roaming', // same species, different overworld encounter form
  'keldeo-resolute',    // same stats/types as Keldeo-Ordinary
])

function isCosmeticForm(name: string, isDefault: boolean): boolean {
  if (isDefault) return false
  if (name.endsWith('-gmax')) return true
  if (name.endsWith('-totem') || name.includes('-totem-')) return true
  if (name.includes('-power-construct')) return true
  if (COSMETIC_FORM_NAMES.has(name)) return true
  return COSMETIC_FORM_PREFIXES.some((prefix) => name.startsWith(prefix))
}

interface PokeApiStatEntry {
  base_stat: number
}

interface PokeApiTypeEntry {
  pokemon_v2_type: { name: string }
}

interface PokeApiPokemon {
  id: number
  name: string
  is_default: boolean
  pokemon_v2_pokemontypes: PokeApiTypeEntry[]
  pokemon_v2_pokemonstats: PokeApiStatEntry[]
  pokemon_v2_pokemonspecy: { id: number } | null
}

interface PokeApiResponse {
  data: {
    pokemon_v2_pokemon: PokeApiPokemon[]
  }
}
// Alternate forms with distinct types/stats (regional variants, Rotom forms, etc.)
// are included. Cosmetic-only forms are filtered out client-side.
const GRAPHQL_QUERY = `
  query {
    pokemon_v2_pokemon(
      where: {
        pokemon_v2_pokemonforms: {
          is_battle_only: { _eq: false }
          is_mega: { _eq: false }
        }
      }
      order_by: { id: asc }
    ) {
      id
      name
      is_default
      pokemon_v2_pokemonspecy {
        id
      }
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
    if (allPokemon.value.length > 0 && allPokemon?.value[0]?.speciesId !== undefined) return

    const cached = localStorage.getItem(CACHE_KEY)
    if (cached) {
      try {
        const data: CacheData = JSON.parse(cached)
        const firstEntry = data.pokemon[0] as (Pokemon & { speciesId?: number }) | undefined
        const hasSpeciesId = firstEntry?.speciesId !== undefined
        if (hasSpeciesId && Date.now() - data.timestamp < CACHE_TTL) {
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

      const json: PokeApiResponse = await res.json()

      allPokemon.value = json.data.pokemon_v2_pokemon
        .filter((p) => !isCosmeticForm(p.name, p.is_default))
        .map((p) => ({
        id: p.id,
        speciesId: p.pokemon_v2_pokemonspecy?.id ?? p.id,
        name: p.name,
        spriteUrl: `https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/${p.id}.png`,
        types: p.pokemon_v2_pokemontypes.map((t) => t.pokemon_v2_type.name),
        bst: p.pokemon_v2_pokemonstats.reduce(
          (sum, stat) => sum + stat.base_stat,
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
