# Backend Architecture

ASP.NET Core 8 minimal APIs, organised as a **modular monolith** (`Server/src/Bloodfall.Backend/Modules/*`). Each
module owns its endpoints and its services. Every module could be split into its own service later: they talk
through interfaces and the realtime hub, not through each other's tables.

## Modules

| Module | Responsibility | Storage |
|---|---|---|
| **Auth** | Register, login, refresh, logout, email verification, password reset, sessions, username availability | `auth_*` tables |
| **Accounts** | Own profile, public profiles, match history, profile edits, search, achievements catalogue | `auth_accounts`, `stats_*` |
| **Social** | Friends and requests, blocks, presence, parties, chat channels and whispers, reports, commendations | `social_*`, in-memory presence/parties/chat |
| **Clans** | Create, invite, accept, ranks, kick, leave | `social_clan*` |
| **Directory** | Game server registration and heartbeats, regions (status, free servers, ping host), allocation, tickets | in-memory registry |
| **Lobbies** | Custom games: create/join/spectate, slots, bots, ready, kick, start, connection info. RTS lobbies add a faction per slot (players and bots), and team size is capped by the map's start locations. A lobby closes as soon as its match result is recorded. | in-memory |
| **Matchmaking** | Queues (quick with bot fill after 25 s, unranked, ranked, and strategy 1v1 with a faction preference and an RTS bot after 45 s), accept/decline, rating-based grouping, party size | in-memory, background loop |
| **Stats** | Match ingestion (game servers only), ratings (Elo), account/hero stats (hero stats only for matches played with a hero), RTS faction and totals, XP, achievements, leaderboards, match detail | `stats_*` |
| **Content** | News (dev sample news is flagged `DevSample`), client version and content hash, **service status built from real health checks** | `content_news` |

## Endpoints

| Group | Endpoints |
|---|---|
| `/healthz` | liveness |
| `/api/auth` | `POST register, login, refresh, logout, forgot-password, reset-password, verify-email` · `GET check-username` · `GET/DELETE sessions` (auth) |
| `/api/accounts` | `GET me`, `GET me/profile`, `PATCH me`, `GET {id}/profile`, `GET {id}/matches`, `GET search` |
| `/api/social` | `GET friends` · `POST friends/requests`, `…/{id}/accept`, `…/{id}/decline` · `DELETE friends/{id}` · `POST/DELETE blocks/{id}` · `GET party` · `POST party/invite, party/{id}/accept, party/{id}/decline, party/leave, party/kick, party/promote` · `GET chat/channels` · `POST reports, commend` |
| `/api/clans` | `GET mine`, `POST /` (create), `POST invite, invites/{id}/accept, leave, rank, kick`, `GET invites`, `GET {id}` |
| `/api/lobbies` | `GET /`, `GET mine`, `GET mine/connection`, `GET {id}`, `POST /` (create), `POST {id}/join`, `POST leave, slot, ready, faction (RTS), kick, bots, start` |
| `/api/matchmaking` | `GET queues`, `POST queue`, `DELETE queue`, `GET status`, `POST respond` |
| `/api/directory` | `GET regions` · `/api/directory/servers`: `POST register, heartbeat`, `GET /` (server key) |
| `/api/stats` | `POST matches` (**server key only**) · `GET leaderboards?category&region&friends` · `GET matches/{id}` |
| `/api/content` | `GET news`, `GET status`, `GET client-version` · `POST news` (admin) |

- Responses use camelCase JSON with enums as strings. That matches the client's `JsonMapper`, and the DTOs live in
  `Shared/Runtime/Contracts`.
- Errors are `{ code, message, fields? }` with sensible HTTP status codes. Messages are user-presentable.

## Realtime (`/ws?access_token=…`)

The WebSocket hub authenticates with the access token and routes envelopes `{ type, id, payload }`.

- **Client → server:** `chat.send`, `chat.join`, `chat.leave`, `presence.set`, `ping`.
- **Server → client:**
  - chat: `chat.message`, `chat.history`
  - presence and friends: `presence.update`, `friend.request`, `friend.update`
  - party: `party.invite`, `party.update`
  - lobby: `lobby.update`, `lobby.started`, `lobby.closed`
  - matchmaking: `mm.status`, `mm.found`, `mm.ready`, `mm.cancelled`
  - clan: `clan.update`, `clan.invite`
  - system: `notice`, `session.revoked`, `pong`, `error`

**Chat:**

- Channels: `global`, `region:<id>`, `party:<id>`, `lobby:<id>`, `clan:<id>`, `channel:<name>`, plus whispers.
- Slash commands: `/w`, `/join`, `/leave`, `/away`, `/back`.
- Flood control: 6 messages per 5 s. Text is cleaned and limited to 300 characters. Blocked users' whispers are
  dropped.

## Security

| Requirement | Implementation |
|---|---|
| No plaintext passwords | **Argon2id** (Konscious), per-password salt; parameters configurable (`Argon2MemoryKiB`, `Argon2Iterations`) |
| Sessions | Access JWT (HS256, 15 min) + opaque refresh token (only its SHA-256 is stored), rotated on every use. **Reuse of a rotated token revokes the whole family.** |
| Brute force | Lockout after 8 failed logins; ASP.NET rate limiter: `auth` policy on auth endpoints plus a global token bucket |
| Enumeration | Forgot-password always returns the same response; registration errors are field-specific but do not leak other accounts' data |
| Authorization | Every mutating endpoint derives the account from the token, never from the request body. Lobby host checks, party leader checks, clan rank checks are server-side. |
| Server-to-server | `X-Bloodfall-Server-Key` filter on directory register/heartbeat and **stats ingestion**. Clients cannot submit results. |
| Input validation | Username/email/display-name/clan rules in `Validation`; chat/text cleaning; enum and GUID parsing |
| SQL | EF Core LINQ only (parameterised); no string-built SQL |
| Secrets | `appsettings.json` ships **empty** keys and the app **fails fast** outside Development; dev keys only in `appsettings.Development.json`. Nothing secret is in the Unity client (it only knows the public backend URL). |
| Auditing | `audit_log` records register, login, failed login and refresh-token reuse (with the client IP) |

## Configuration

`appsettings.json` → section `Bloodfall`:

| Key | Purpose |
|---|---|
| `Environment` | Environment name |
| `JwtSigningKey` | JWT signing key |
| `GameServerKey` | Shared key for game-server calls |
| `TicketSigningKey` | Match ticket signing key |
| `SeedDevelopmentContent` | Seed the sample news in Development |
| `AllowCrossRegionFallback` | Allocate from the nearest region when none is free |
| `Argon2MemoryKiB`, `Argon2Iterations` | Password hashing cost |
| `LatestClientVersion`, `MinimumClientVersion` | Client version gate |
| `Maintenance`, `MaintenanceMessage` | Maintenance mode shown by the client |

Section `Database`: `Provider` (`Sqlite` | `Postgres`) and `ConnectionString`.

Environment variables use `__` as the separator, e.g. `Bloodfall__JwtSigningKey`, `Database__ConnectionString`.

## Background work

- `MatchmakingLoop`: forms matches, handles accept timeouts and bot fill.
- Directory liveness: servers missing heartbeats are dropped. Lobbies on a lost server get a `notice`.
- Presence cleanup on WebSocket disconnect.
