import * as signalR from '@microsoft/signalr'
import { ref, shallowRef } from 'vue'
import { API_BASE } from '@/services/api'

// Re-export so existing consumers don't need to change their import path.
export { API_BASE }

export const HUB_URL = import.meta.env.DEV ? 'http://localhost:5050/hubs/draft' : '/hubs/draft'

// ── Singleton state ───────────────────────────────────────────────────────────
const connection = shallowRef<signalR.HubConnection | null>(null)
const isConnected = ref(false)
const currentLeagueCode = ref<string | null>(null)

// All active per-view handlers. The hub listener dispatches to each one so
// navigating between pages only swaps handlers — the socket stays open.
type StateHandler = (state: any) => void
const handlers = new Set<StateHandler>()

function dispatchState(state: any) {
  for (const h of handlers) h(state)
}

async function startConnection(leagueCode: string) {
  const hub = new signalR.HubConnectionBuilder()
    .withUrl(HUB_URL)
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (ctx) => {
        if (ctx.elapsedMilliseconds < 10_000) return 2_000
        if (ctx.elapsedMilliseconds < 30_000) return 5_000
        if (ctx.elapsedMilliseconds < 60_000) return 10_000
        return 30_000
      },
    })
    .withKeepAliveInterval(15_000)
    .withServerTimeout(60_000)
    .build()

  hub.on('LeagueState', dispatchState)

  hub.onreconnected(async () => {
    isConnected.value = true
    await hub.invoke('JoinLeagueGroup', leagueCode)
  })

  hub.onclose(() => {
    isConnected.value = false
  })

  try {
    await hub.start()
    isConnected.value = true
    currentLeagueCode.value = leagueCode
    connection.value = hub
    await hub.invoke('JoinLeagueGroup', leagueCode)
  } catch (e) {
    console.error('SignalR connection failed:', e)
    isConnected.value = false
  }
}

export function useSignalR() {
  /**
   * Register a per-view handler and ensure the connection is open.
   * Safe to call on every onMounted — only starts a new connection when needed.
   */
  async function subscribe(leagueCode: string, handler: StateHandler) {
    handlers.add(handler)

    if (connection.value && currentLeagueCode.value === leagueCode) {
      // Already connected to the right league. Re-join to get an immediate state snapshot.
      if (isConnected.value) {
        await connection.value.invoke('JoinLeagueGroup', leagueCode)
      }
      return
    }

    // Different league or no connection yet — stop any existing connection first.
    if (connection.value) {
      try {
        await connection.value.stop()
      } catch {
        /* ignore */
      }
      connection.value = null
      isConnected.value = false
      currentLeagueCode.value = null
    }

    await startConnection(leagueCode)
  }

  /**
   * Deregister a per-view handler. Does NOT close the connection — the socket
   * stays alive so the next page doesn't have to reconnect.
   */
  function unsubscribe(handler: StateHandler) {
    handlers.delete(handler)
  }

  /**
   * Fully stop the connection and clear all handlers. Call this on logout only.
   */
  async function disconnect() {
    handlers.clear()
    if (connection.value) {
      try {
        await connection.value.stop()
      } catch {
        /* ignore */
      }
      connection.value = null
      isConnected.value = false
      currentLeagueCode.value = null
    }
  }

  return { subscribe, unsubscribe, disconnect, isConnected }
}
