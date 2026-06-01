# Pokémon Draft — Refactoring Plan

## Goals
Improve security, maintainability, and user experience. Tracked and executed via GitHub Copilot CLI.

---

## Phase 1 — Security: Session Token Hardening ✅

**Problem:** `Player.SessionToken` was stored as plaintext in the database. If the database is ever compromised, any attacker could impersonate any Google-authenticated user.

**Fix:**
- Remove `Player.SessionToken` from the model and drop the DB column.
- `Player.Pin` (already BCrypt-hashed) becomes the sole credential for all users.
- `AuthController.EnterLeague` now generates a **fresh random token** on each call, BCrypt-hashes it, updates `player.Pin` (and `league.AdminPin` if the player is commissioner), and returns only the **plaintext token to the client**. It is never stored in plain form.
- `VerifyPin` and `VerifyAdminPin` remove the plaintext `==` comparison and rely only on `BCrypt.Verify`.
- EF Core migration drops the `SessionToken` column.

---

## Phase 2 — Remove Dead Code ✅

- Delete empty `IDataRepository.cs` and `DataRepository.cs` (the repository layer was defined but never implemented or registered).
- Delete `DraftView-OLD.vue` (an abandoned backup file left in the source tree).

---

## Phase 3 — Break Up the Monolith (Backend) ✅

**Problem:** `DataController.cs` was a 250-line god controller handling 20+ endpoints across 6 domains.

**Fix:** Split into focused controllers, all inheriting a thin `LeagueBaseController` that provides `BroadcastLeague()` and `GetRequestUserId()` helpers.

| Controller | Route prefix | Responsibility |
|---|---|---|
| `AuthController` | `api/auth` | Google auth, PIN join, my leagues, link/enter league |
| `LeagueController` | `api/leagues` | League CRUD, config, point values |
| `PlayerController` | `api/leagues/{code}/players` | Player management |
| `DraftController` | `api/leagues/{code}/draft` | Draft lifecycle + picks |
| `RosterController` | `api/leagues/{code}/roster` | Post-draft roster changes |
| `TradeController` | `api/leagues/{code}/trades` | Trade proposals + responses |
| `ScheduleController` | `api/leagues/{code}` | Schedule, standings, playoff outlook, matchup reporting |

---

## Phase 4 — Frontend Cleanup ✅

- **Centralize `API_BASE`:** Previously duplicated in `auth.ts` and `signalr.ts`. Now lives exclusively in `services/api.ts`.
- **Typed API client (`services/api.ts`):** Centralizes `fetch` boilerplate, auth headers, and error handling. All stores import from here instead of writing raw `fetch` calls.
- **Proper TypeScript types (`types/index.ts`):** Added `ServerLeagueResponse`, `ServerPlayerResponse`, `ServerDraftState`, `ServerDraftPick` interfaces matching backend DTOs exactly. Replace `any` in `draft.ts`.
- **Connection status banner (`ConnectionBanner.vue`):** A visible "Reconnecting to server…" banner appears when the SignalR connection is lost. Prevents users from silently losing state during network hiccups.
