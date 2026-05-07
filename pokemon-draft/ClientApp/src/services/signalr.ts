import * as signalR from '@microsoft/signalr'
import { ref, shallowRef } from 'vue'

export const API_BASE = import.meta.env.DEV ? 'http://localhost:5050/api' : '/api'
export const HUB_URL = import.meta.env.DEV ? 'http://localhost:5050/hubs/draft' : '/hubs/draft'

const connection = shallowRef<signalR.HubConnection | null>(null)
const isConnected = ref(false)
const currentLeagueCode = ref<string | null>(null)

export function useSignalR() {
  async function connect(leagueCode: string, onLeagueState: (state: any) => void) {
    // If already connected to this league, just resubscribe the handler
    if (connection.value && currentLeagueCode.value === leagueCode && isConnected.value) {
      connection.value.off('LeagueState')
      connection.value.on('LeagueState', onLeagueState)
      return
    }

    await disconnect()

    const hub = new signalR.HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) => {
          // Keep retrying indefinitely: escalating backoff, capped at 30s
          if (ctx.elapsedMilliseconds < 10_000) return 2_000
          if (ctx.elapsedMilliseconds < 30_000) return 5_000
          if (ctx.elapsedMilliseconds < 60_000) return 10_000
          return 30_000
        }
      })
      .withKeepAliveInterval(15_000)
      .withServerTimeout(60_000)
      .build()

    hub.on('LeagueState', onLeagueState)

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

  async function disconnect() {
    if (connection.value) {
      try {
        await connection.value.stop()
      } catch {
        // ignore
      }
      connection.value = null
      isConnected.value = false
      currentLeagueCode.value = null
    }
  }

  return { connect, disconnect, isConnected }
}
