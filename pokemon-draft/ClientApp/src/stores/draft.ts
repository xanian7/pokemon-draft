import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { useAuthStore } from './auth'
import { API_BASE } from '@/services/signalr'

export const useDraftStore = defineStore('draft', () => {
  // Source of truth: the full LeagueResponse broadcast from the server via SignalR.
  // Call applyServerState() whenever a LeagueState message is received.
  const serverState = ref<any>(null)

  function applyServerState(state: any) {
    serverState.value = state
  }

  const draftData = computed(() => serverState.value?.draft ?? null)

  const status = computed<'setup' | 'active' | 'complete'>(() => {
    const s: string = draftData.value?.status?.toLowerCase() ?? ''
    if (s === 'active') return 'active'
    if (s === 'complete') return 'complete'
    return 'setup'
  })

  const currentPickNumber = computed<number>(() => draftData.value?.currentPickNumber ?? 0)
  const totalPicks = computed<number>(() => draftData.value?.totalPicks ?? 0)

  // The DB player ID of whoever's turn it is — comes directly from the server.
  const currentPickerId = computed<string | null>(() => draftData.value?.currentPickerId ?? null)
  const currentPickerName = computed<string | null>(
    () => draftData.value?.currentPickerName ?? null,
  )

  const isDraftComplete = computed(() => status.value === 'complete')

  const picks = computed<any[]>(() => draftData.value?.picks ?? [])

  const pickedPokemonIds = computed<Set<number>>(
    () => new Set(picks.value.map((p: any) => p.pokemonId as number)),
  )

  const players = computed<any[]>(() => serverState.value?.players ?? [])
  const leagueName = computed<string>(() => serverState.value?.name ?? '')
  const regulationSet = computed<string>(() => serverState.value?.regulationSet ?? 'national')
  const rounds = computed<number>(() => serverState.value?.rounds ?? 0)

  const currentPicker = computed(
    () => players.value.find((p: any) => p.id === currentPickerId.value) ?? null,
  )

  function playerCanDraft(playerId: string): boolean {
    return status.value === 'active' && !isDraftComplete.value && currentPickerId.value === playerId
  }

  /** Posts a draft pick to the API. Returns null on success or an error message. */
  async function makePick(pokemonId: number): Promise<string | null> {
    const authStore = useAuthStore()
    if (!authStore.leagueCode || !authStore.playerId || !authStore.pin) return 'Not authenticated.'
    const res = await fetch(`${API_BASE}/leagues/${authStore.leagueCode}/draft/pick`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ playerId: authStore.playerId, pin: authStore.pin, pokemonId }),
    })
    if (res.ok) return null
    const text = await res.text()
    return text || 'Failed to make pick.'
  }

  function getPlayerPicks(playerId: string): any[] {
    return picks.value
      .filter((p: any) => p.playerId === playerId)
      .sort((a: any, b: any) => a.pickNumber - b.pickNumber)
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
