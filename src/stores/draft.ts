import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { DraftPick, DraftStatus } from '@/types'
import { useLeagueStore } from './league'
import { usePokemonStore } from './pokemon'

const STORAGE_KEY = 'pokemon-draft:draft'

export const useDraftStore = defineStore('draft', () => {
  const leagueStore = useLeagueStore()
  const pokemonStore = usePokemonStore()

  const picks = ref<DraftPick[]>([])
  const status = ref<DraftStatus>('setup')
  const currentPickNumber = ref(0)

  function load() {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored) {
      try {
        const data = JSON.parse(stored)
        picks.value = data.picks ?? []
        status.value = data.status ?? 'setup'
        currentPickNumber.value = data.currentPickNumber ?? 0
      } catch {
        // ignore
      }
    }
  }

  function save() {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({ picks: picks.value, status: status.value, currentPickNumber: currentPickNumber.value }),
    )
  }

  /** Returns the player ID whose turn it is at the given overall pick number (0-indexed). */
  function getPlayerIdAtPick(pickNumber: number): string {
    const players = leagueStore.config.players
    const n = players.length
    if (n === 0) return ''
    const round = Math.floor(pickNumber / n)
    const posInRound = pickNumber % n
    // Even rounds go 0→n-1, odd rounds reverse (snake)
    const idx = round % 2 === 0 ? posInRound : n - 1 - posInRound
    return players[idx]?.id ?? ''
  }

  const totalPicks = computed(
    () => leagueStore.config.players.length * leagueStore.config.rounds,
  )

  const isDraftComplete = computed(
    () => status.value !== 'setup' && currentPickNumber.value >= totalPicks.value,
  )

  const currentPickerId = computed(() =>
    status.value === 'active' && !isDraftComplete.value
      ? getPlayerIdAtPick(currentPickNumber.value)
      : null,
  )

  const currentPicker = computed(
    () => leagueStore.config.players.find((p) => p.id === currentPickerId.value) ?? null,
  )

  const pickedPokemonIds = computed(() => new Set(picks.value.map((p) => p.pokemonId)))

  /** Full ordered schedule of every pick, showing who picks and what they picked. */
  const pickSchedule = computed(() => {
    const n = leagueStore.config.players.length
    if (n === 0) return []
    return Array.from({ length: totalPicks.value }, (_, i) => ({
      pickNumber: i,
      round: Math.floor(i / n),
      playerId: getPlayerIdAtPick(i),
      pick: picks.value.find((p) => p.pickNumber === i) ?? null,
    }))
  })

  function getPlayerPicks(playerId: string) {
    return picks.value
      .filter((p) => p.playerId === playerId)
      .sort((a, b) => a.pickNumber - b.pickNumber)
      .map((p) => pokemonStore.getPokemonById(p.pokemonId))
      .filter(Boolean) as NonNullable<ReturnType<typeof pokemonStore.getPokemonById>>[]
  }

  function getPlayerPoints(playerId: string): number {
    return picks.value
      .filter((p) => p.playerId === playerId)
      .reduce((sum, p) => sum + pokemonStore.getPointValue(p.pokemonId), 0)
  }

  function startDraft() {
    picks.value = []
    currentPickNumber.value = 0
    status.value = 'active'
    save()
  }

  function resetDraft() {
    picks.value = []
    currentPickNumber.value = 0
    status.value = 'setup'
    localStorage.removeItem(STORAGE_KEY)
  }

  function makePick(pokemonId: number): boolean {
    if (status.value !== 'active' || isDraftComplete.value) return false
    if (pickedPokemonIds.value.has(pokemonId)) return false

    const playerId = currentPickerId.value!
    const n = leagueStore.config.players.length
    picks.value.push({
      pickNumber: currentPickNumber.value,
      round: Math.floor(currentPickNumber.value / n),
      playerId,
      pokemonId,
    })

    currentPickNumber.value++

    if (currentPickNumber.value >= totalPicks.value) {
      status.value = 'complete'
    }

    save()
    return true
  }

  load()

  return {
    picks,
    status,
    currentPickNumber,
    currentPickerId,
    currentPicker,
    totalPicks,
    isDraftComplete,
    pickedPokemonIds,
    pickSchedule,
    getPlayerPicks,
    getPlayerPoints,
    startDraft,
    resetDraft,
    makePick,
  }
})
