import vgcData from './vgc-regulations.json'

const POKEDEX_CACHE_PREFIX = 'pokedex-cache-'

export interface RegulationDef {
  id: string
  label: string
  description: string
  isLegal: ((pokemonId: number) => boolean) | null
  fetchLegalIds?: () => Promise<Set<number>>
}

function createRangeRegulation(
  id: string,
  label: string,
  description: string,
  minId: number,
  maxId: number,
): RegulationDef {
  return {
    id,
    label,
    description,
    isLegal: (pokemonId: number) => pokemonId >= minId && pokemonId <= maxId,
  }
}

async function fetchPokedexIds(names: string[]): Promise<Set<number>> {
  const cacheKey = `${POKEDEX_CACHE_PREFIX}${names.join('-')}`
  const cached = localStorage.getItem(cacheKey)

  if (cached) {
    try {
      const parsed = JSON.parse(cached)
      if (Array.isArray(parsed)) {
        return new Set(parsed.filter((id): id is number => typeof id === 'number'))
      }
    } catch {
      localStorage.removeItem(cacheKey)
    }
  }

  const responses = await Promise.all(
    names.map((name) => fetch(`https://pokeapi.co/api/v2/pokedex/${name}`)),
  )

  for (const response of responses) {
    if (!response.ok) throw new Error('Failed to load regulation data from PokeAPI.')
  }

  const payloads = await Promise.all(responses.map((response) => response.json()))
  const ids = new Set<number>()

  for (const payload of payloads) {
    for (const entry of payload.pokemon_entries ?? []) {
      const url = entry.pokemon_species?.url as string | undefined
      const match = url?.match(/\/pokemon-species\/(\d+)\/?$/)
      if (match) ids.add(Number(match[1]))
    }
  }

  localStorage.setItem(cacheKey, JSON.stringify([...ids].sort((a, b) => a - b)))
  return ids
}

export function clearRegulationCache() {
  for (let index = localStorage.length - 1; index >= 0; index -= 1) {
    const key = localStorage.key(index)
    if (key?.startsWith(POKEDEX_CACHE_PREFIX)) localStorage.removeItem(key)
  }
}

function createVgcRegulation(
  id: string,
  label: string,
  description: string,
  pokedexNames: string[],
): RegulationDef {
  const regulation: RegulationDef = {
    id,
    label,
    description,
    isLegal: null,
    async fetchLegalIds() {
      const ids = await fetchPokedexIds(pokedexNames)
      regulation.isLegal = (pokemonId: number) => ids.has(pokemonId)
      return ids
    },
  }

  return regulation
}

function createStaticRegulation(entry: (typeof vgcData.sets)[number]): RegulationDef {
  const ids = new Set(entry.pokemonIds)
  return {
    id: entry.id,
    label: entry.label,
    description: entry.description,
    isLegal: (pokemonId: number) => ids.has(pokemonId),
  }
}

export const REGULATIONS: RegulationDef[] = [
  {
    id: 'national',
    label: 'National Dex (All)',
    description: 'Show every Pokémon in the National Dex.',
    isLegal: null,
  },
  createRangeRegulation('gen1', 'Gen 1 – Kanto', 'Show Kanto Pokémon only.', 1, 151),
  createRangeRegulation('gen2', 'Gen 2 – Johto', 'Show Johto Pokémon only.', 152, 251),
  createRangeRegulation('gen3', 'Gen 3 – Hoenn', 'Show Hoenn Pokémon only.', 252, 386),
  createRangeRegulation('gen4', 'Gen 4 – Sinnoh', 'Show Sinnoh Pokémon only.', 387, 493),
  createRangeRegulation('gen5', 'Gen 5 – Unova', 'Show Unova Pokémon only.', 494, 649),
  createRangeRegulation('gen6', 'Gen 6 – Kalos', 'Show Kalos Pokémon only.', 650, 721),
  createRangeRegulation('gen7', 'Gen 7 – Alola', 'Show Alola Pokémon only.', 722, 809),
  createRangeRegulation('gen8', 'Gen 8 – Galar', 'Show Galar Pokémon only.', 810, 905),
  createRangeRegulation(
    'gen9',
    'Gen 9 – Paldea only',
    'Show Paldea Pokédex Pokémon only.',
    906,
    1025,
  ),
  createVgcRegulation('vgc-regd', 'VGC Reg D', 'Paldea Pokédex legal Pokémon.', ['paldea']),
  createVgcRegulation(
    'vgc-regg',
    'VGC Reg G (2025)',
    'Paldea, Kitakami, and Blueberry Pokédex legal Pokémon.',
    ['paldea', 'kitakami', 'blueberry'],
  ),
  ...vgcData.sets.map(createStaticRegulation),
]

export function getRegulation(id: string): RegulationDef {
  const regulation = REGULATIONS.find((entry) => entry.id === id)
  return regulation ?? REGULATIONS[0]!
}
