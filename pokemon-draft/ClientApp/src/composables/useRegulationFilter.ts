import { ref, watch, type Ref } from 'vue'
import { getRegulation } from '@/data/regulations'
import type { Pokemon } from '@/types'

/**
 * Provides regulation-based Pokémon filtering from a reactive regulation ID.
 * Automatically fetches dynamic legal ID lists (e.g. VGC Reg D/G) and
 * exposes `isLegalPokemon` for use in `filter()` calls.
 */
export function useRegulationFilter(regulationId: Ref<string>) {
  const legalIds = ref<Set<number> | null>(null)
  const regulationLoading = ref(false)

  async function loadRegulation() {
    const reg = getRegulation(regulationId.value)
    legalIds.value = null
    if (!reg.fetchLegalIds) return

    regulationLoading.value = true
    try {
      legalIds.value = await reg.fetchLegalIds()
    } finally {
      regulationLoading.value = false
    }
  }

  function isLegalPokemon(p: Pick<Pokemon, 'speciesId'>): boolean {
    if (regulationId.value === 'national') return true
    const reg = getRegulation(regulationId.value)
    if (reg.isLegal) return reg.isLegal(p.speciesId)
    if (legalIds.value) return legalIds.value.has(p.speciesId)
    return true // still loading — show all until ready
  }

  watch(regulationId, loadRegulation, { immediate: true })

  return { legalIds, regulationLoading, isLegalPokemon }
}
