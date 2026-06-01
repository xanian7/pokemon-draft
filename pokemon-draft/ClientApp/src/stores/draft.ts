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
  const regulationSet = computed<string>(() => serverState.value?.regulationSet ?? 'national')
  const rounds = computed<number>(() => serverState.value?.rounds ?? 0)

  const currentPicker = computed<ServerPlayerResponse | null>(
    () => players.value.find((p) => p.id === currentPickerId.value) ?? null,
  )

  function playerCanDraft(playerId: string): boolean {
    return status.value === 'active' && !isDraftComplete.value && currentPickerId.value === playerId
  }

  /** Posts a draft pick to the API. Returns null on success or an error message. */
  async function makePick(pokemonId: number): Promise<string | null> {
    const authStore = useAuthStore()
    if (!authStore.leagueCode || !authStore.playerId || !authStore.pin) return 'Not authenticated.'
    const result = await apiPost(`/leagues/${authStore.leagueCode}/draft/pick`, {
      playerId: authStore.playerId,
      pin: authStore.pin,
      pokemonId,
    })
    return result.error
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
    regulationSet,
    rounds,
    currentPicker,
    playerCanDraft,
    makePick,
    getPlayerPicks,
  }
})
