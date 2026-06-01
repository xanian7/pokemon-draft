# SignalR Connection Drop Fix

## Root Cause

The SignalR connection is a **module-level singleton** in `signalr.ts`, but every view that
uses real-time data calls `disconnect()` inside `onUnmounted`. This means:

1. User navigates to **Draft** → `connect()` opens the WebSocket
2. User clicks to **My Team** → `DraftView` unmounts → `disconnect()` **kills the connection**
3. `MyTeamView` mounts → `connect()` restarts from scratch
4. Repeat on every navigation

Every page transition through a SignalR-aware view destroys and recreates the WebSocket.
The kill happens instantly on unmount — the automatic reconnect logic never even fires.

## Affected Files

| File | Problem |
|---|---|
| `ClientApp/src/services/signalr.ts` | Exposes `disconnect` — called per-view |
| `ClientApp/src/views/DraftView.vue` | `onUnmounted(disconnect)` |
| `ClientApp/src/views/LeagueSetupView.vue` | `onUnmounted(disconnect)` |
| `ClientApp/src/views/MyTeamView.vue` | `onUnmounted(disconnect)` |
| `ClientApp/src/views/RosterView.vue` | `onUnmounted(disconnect)` |
| `ClientApp/src/App.vue` | Logout does not clean up SignalR |

## Fix Plan

### 1. Redesign `signalr.ts` API

Replace the single `onLeagueState` callback pattern with a **handler registry**:

- **`subscribe(leagueCode, handler)`** — adds handler to a `Set`; starts the connection only if
  not already running for that league. Re-invokes `JoinLeagueGroup` so the new subscriber gets
  an immediate state snapshot.
- **`unsubscribe(handler)`** — removes the handler from the `Set`. Does **not** stop the connection.
- **`disconnect()`** — clears all handlers and stops the connection (called on logout only).

A single `LeagueState` hub listener dispatches to all registered handlers. This means the
WebSocket stays open across navigations.

### 2. Update All Views

For each of the four views, change:

```diff
- const { connect, disconnect } = useSignalR()
+ const { subscribe, unsubscribe } = useSignalR()

- onMounted(() => connect(leagueCode, handler))
- onUnmounted(disconnect)
+ onMounted(() => subscribe(leagueCode, handler))
+ onUnmounted(() => unsubscribe(handler))
```

### 3. Disconnect on Logout

In `App.vue`, call `disconnect()` inside the `logout()` function so the WebSocket is torn
down cleanly when the user signs out.

## Expected Result

- WebSocket opens once when the user first lands on a real-time page
- Navigating between pages only swaps the active handler — no reconnect
- `ConnectionBanner` only appears if the server actually drops (network issue, server restart)
- Logout cleanly closes the connection
