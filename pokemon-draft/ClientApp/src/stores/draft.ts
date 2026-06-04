import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { useAuthStore } from './auth'
import { apiPost } from '@/services/api'
import type { ServerLeagueResponse, ServerPlayerResponse, ServerDraftPick } from '@/types'

export const useDraftStore = defineStore('draft', () => {
  // Source of truth: the full LeagueResponse broadcast from the server via SignalR.
  // Call applyServerState() whenever a LeagueState message is received.
  const serverState = ref<ServerLeagueResponse | null>(null)

  function applyServerState(state: ServerLeagueResponse) {
    serverState.value = state
  }

  const draftData = computed(() => serverState.value?.draft ?? null)

  const status = computed<'setup' | 'active' | 'complete'>(() => {
    const s = draftData.value?.status?.toLowerCase() ?? ''
    if (s === 'active') return 'active'
    if (s === 'complete') return 'complete'
    return 'setup'
  })

  const currentPickNumber = computed<number>(() => draftData.value?.currentPickNumber ?? 0)
  const totalPicks = computed<number>(() => draftData.value?.totalPicks ?? 0)

  const currentPickerId = computed<string | null>(() => draftData.value?.currentPickerId ?? null)
  const currentPickerName = computed<string | null>(
    () => draftData.value?.currentPickerName ?? null,
  )

  const isDraftComplete = computed(() => status.value === 'complete')

  const picks = computed<ServerDraftPick[]>(() => draftData.value?.picks ?? [])

  const pickedPokemonIds = computed<Set<number>>(() => new Set(picks.value.map((p) => p.pokemonId)))

  const players = computed<ServerPlayerResponse[]>(() => serverState.value?.players ?? [])
  const leagueName = computed<string>(() => serverState.value?.name ?? '')
  const pointLimit = computed<number>(() => serverState.value?.pointLimit ?? 0)
  const pointValues = computed<Record<number, number>>(() => serverState.value?.pointValues ?? {})
  const regulationSet = computed<string>(() => serverState.value?.regulationSet ?? 'national')
  const rounds = computed<number>(() => serverState.value?.rounds ?? 0)

  const currentPicker = computed<ServerPlayerResponse | null>(
    () => players.value.find((p) => p.id === currentPickerId.value) ?? null,
  )

  function playerCanDraft(playerId: string): boolean {
    return status.value === 'active' && !isDraftComplete.value && currentPickerId.value === playerId
  }

  function getPokemonPointValue(pokemonId: number): number {
    return pointValues.value[pokemonId] ?? 0
  }

  function getPlayerPointTotal(playerId: string): number {
    return getPlayerPicks(playerId).reduce(
      (sum, pick) => sum + getPokemonPointValue(pick.pokemonId),
      0,
    )
  }

  function getPlayerPointsRemaining(playerId: string): number {
    return pointLimit.value - getPlayerPointTotal(playerId)
  }

  function isPlayerAtPointLimit(playerId: string): boolean {
    return getPlayerPointTotal(playerId) >= pointLimit.value
  }

  function playerCanAffordPokemon(playerId: string, pokemonId: number): boolean {
    return getPokemonPointValue(pokemonId) <= getPlayerPointsRemaining(playerId)
  }

  /** Posts a draft pick to the API. Returns null on success or an error message. */
  async function makePick(pokemonId: number): Promise<string | null> {
    const authStore = useAuthStore()
    const draftee = currentPicker.value
    if (!authStore.leagueCode || !authStore.playerId || !authStore.pin) return 'Not authenticated.'
    let result = await apiPost(`/leagues/${authStore.leagueCode}/draft/pick`, {
      playerId: authStore.playerId,
      pin: authStore.pin,
      pokemonId,
    })

    if (result.error === 'Invalid player or PIN.' && authStore.authToken) {
      const refreshError = await authStore.enterLeague(authStore.leagueCode)
      if (!refreshError) {
        result = await apiPost(`/leagues/${authStore.leagueCode}/draft/pick`, {
          playerId: authStore.playerId,
          pin: authStore.pin,
          pokemonId,
        })
      }
    }

    if (result.error) return result.error

    return null
  }

  function getPlayerPicks(playerId: string): ServerDraftPick[] {
    return picks.value
      .filter((p) => p.playerId === playerId)
      .sort((a, b) => a.pickNumber - b.pickNumber)
  }

  return {
    serverState,
    applyServerState,
    status,
    currentPickNumber,
    totalPicks,
    currentPickerId,
    currentPickerName,
    isDraftComplete,
    picks,
    pickedPokemonIds,
    players,
    leagueName,
    pointLimit,
    pointValues,
    regulationSet,
    rounds,
    currentPicker,
    playerCanDraft,
    getPokemonPointValue,
    getPlayerPointTotal,
    getPlayerPointsRemaining,
    isPlayerAtPointLimit,
    playerCanAffordPokemon,
    makePick,
    getPlayerPicks,
  }
})
