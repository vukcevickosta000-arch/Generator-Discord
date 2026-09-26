# Network Architecture

## 1. Topology

```
 Client ──HTTPS/JSON──▶ Backend (REST: auth, lobbies, matchmaking, stats…)
 Client ◀──WebSocket──▶ Backend (/ws realtime: chat, presence, party, lobby, queue, match-ready)
 Client ◀────UDP──────▶ Dedicated game server (LiteNetLib, binary protocol)
 Game server ─HTTPS─▶ Backend (directory register/heartbeat, match result) with X-Bloodfall-Server-Key
```

The backend never relays gameplay traffic. The game server never talks to the database; it reports results to the
backend.

## 2. Joining a match

1. **The backend decides a match.**
   - A lobby host presses Start, or matchmaking forms a match and all players accept.
   - The directory allocates an idle server in the chosen region. With the dev flag `AllowCrossRegionFallback`, the
     nearest region is used if needed.
   - It delivers a `ServerAssignment` on that server's next heartbeat.
2. **The backend issues one ticket per player** and sends `mm.ready` / `lobby.started` over the WebSocket with a
   `GameConnectionInfo` (address, port, ticket, match id).
   - Ticket format: `base64url(json payload) + "." + base64url(HMAC-SHA256(payload, ticketKey))`.
   - Payload: `matchId`, `accountId`, `displayName`, `team`, `slot`, `spectator`, `exp` (unix s), `nonce`.
3. **The client connects over UDP** (connection key `bloodfall`) and sends `Hello` with:
   - protocol version (currently **9**; v4 added the Vharoth phase and seal count to the snapshot header, v5 the
     RTS fields listed in §3, v6 research, v7 RTS heroes, v8 couriers, v9 shields)
   - game data content hash
   - client version
   - the ticket
4. **The server validates, in order:**
   1. protocol version
   2. content hash
   3. client version (if configured)
   4. ticket signature and expiry
   5. match id
   6. player membership
   7. not abandoned
5. **On success** the server sends `Welcome` (player id, team, spectator flag, map, mode, tick rate, server tick,
   reconnect flag) and `MatchState`, then snapshots.
   - A second connection for the same player kicks the old one ("connected from another location").
   - On failure it sends `Reject(reason)` and disconnects. Rejection reasons are human-readable and shown by the client.

**Offline practice** uses the same `MatchHost` in-process through `LoopbackConnection`. Its ticket validator accepts
only `offline:<name>` tickets, and it exists only inside the client.

## 3. Messages

Every message starts with one `MsgType` byte.

| Direction | Type | Content |
|---|---|---|
| C→S | `Hello` (1) | protocol, content hash, client version, ticket, spectator |
| C→S | `Command` (2) | `Order`: type, unit, target, point(s), slot(s), item id (content index; a unit index for Train/Build, an upgrade index for Research, v6), queue flag, group (up to 63 more unit ids, v5) |
| C→S | `Chat` (3) | team flag, text (≤ 200 chars; `-ff` votes concede; practice cheats when enabled) |
| C→S | `LoadProgress` (4) | 0–100 |
| C→S | `PickHero` (5) | hero id or `random` |
| C→S | `Ping` (6) / S→C `Pong` (70) | client clock ms / echo + server tick |
| C→S | `Leave` (7) | voluntary leave (abandons during a running match) |
| S→C | `Welcome` (64), `Reject` (65) | see above |
| S→C | `MatchState` (66) | phase, phase timer, player views (reliable, on change and every 0.5 s in pre-match phases) |
| S→C | `Snapshot` (67) | tick, time, phase, day/night, team kills, visible entities, player views, the private state of the receiving player (unreliable, sequenced) |
| S→C | `Events` (68) | batched `SimEvent`s visible to the receiving team/player (reliable) |
| S→C | `ChatBroadcast` (69) | player id, name, team flag, text |
| S→C | `MatchEnd` (71) | JSON `MatchResult` |

**RTS additions (v5).**

- **Group commands.** The server applies the order to every listed unit that the sender controls. Exceptions:
  - Train goes to the selected building with the shortest queue.
  - Build and casts use the first unit only.
- **Entities.** RTS kinds carry an extra block, keyed by the entity kind so MOBA snapshots are unchanged:
  - buildings: under-construction flag and progress;
  - the owner's team only: the training queue, its progress and the rally point;
  - workers: carried blood-iron and lumber;
  - veins: blood-iron left.
- **Player views** carry the RTS faction and whether the player is eliminated.
- **Private state.** The receiving player gets an RTS block with blood-iron, lumber, supply used and supply cap.

**Research (v6).**
- The content index gained an upgrade table (ids sorted ordinally, like units and items).
- A `Research` command names an upgrade index.
- Each training-queue entry is `index << 1`, with the low bit set when the entry is research, so units and
  research share one queue on the wire.
- The private RTS block ends with the completed research: a count byte (at most 64), then upgrade indices in
  ordinal order.
- `ResearchComplete` events go only to the researching player.

**Shields (v9).**
- Every entity carries the damage its shield statuses can still absorb (a varint, rounded up, after its damage), so
  health bars can draw the shield.

**Couriers (v8).**
- The Blood War private block ends with the player's courier: its unit id (0 = none), then its state (idle, fetching,
  delivering, returning), respawn countdown and number of items aboard.
- `CourierDeliver` (order 30) always acts on the sender's own courier, whatever unit id it carries.
- `CourierDelivered` events go only to the owner.

**RTS heroes (v7).**
- Altars take ordinary `Train` commands whose unit index names a hero, and queue entries use the unit index as well.
  (Hero ids are in the content index's unit table.)
- The private RTS block ends with the player's heroes, at most 8. Each has:
  - unit id, hero index, level, dead flag;
  - XP, XP at the level's start, XP for the next level;
  - ability points;
  - a varuint mask of the abilities that can be learned now.
- Ability ids, levels and cooldowns travel in each hero's entity, as in Blood War. `Player.Hero` (the Blood War private
  block) stays empty in the strategy mode.
- **Fog.**
  - Enemy RTS buildings are sent only while visible. The client is expected to remember last-seen buildings.
  - MOBA structures stay always known.
  - Veins are always sent, since they are part of the map.

### Encoding

- **Varints** for ids and counts.
- **Positions** quantized to 1/256 m in 16.8 fixed point.
- **Angles** as one byte.
- **HP, mana and timers** as tenths.
- **Content ids** (units, abilities, statuses, items) are small integers from `ContentIndex`. Both sides build it
  identically from the hash-verified data.
- **Action start ticks** are sent as negative ages, so the client can align animations to the server's wind-up
  without clock sync.

### Fragmentation

- Messages larger than 900 bytes are split into parts: `[0xF0][u16 message id][u8 index][u8 count][payload]` and
  reassembled by `FragmentAssembler`.
- If a send still throws `TooBigPacketException`, the server falls back to the reliable channel. A single bad peer
  never crashes the server loop.

## 4. Snapshots and interpolation

- **Rates.** The simulation runs at 30 Hz. Snapshots are sent every `TickRate / SnapshotRate` ticks, which is 1 with
  the current rules (30 Hz).
- **Size.** Full state, no delta compression yet (TODO T-011). A 5v5 mid-game snapshot is roughly 3–6 KB before
  fragmentation.
- **Client timing:**
  - The client keeps the last 6 frames and renders about 3 ticks (~100 ms) behind the newest.
  - It lerps positions, heights and facing between the two frames that bracket the render tick.
  - Teleports (>8 m) snap instead of sliding.
- **No client-side prediction yet** (TODO T-012). Local orders show feedback instantly (move markers, cursor,
  indicators), but the hero moves when the server says so.

## 5. Visibility (anti-maphack)

- `Codec.IsVisibleForViewer` sends an entity to a team only if:
  - it is on that team, or
  - it is a structure (known, as last-seen), or
  - it is visible to that team this tick (the vision grid, invisibility and true sight).
- Dead non-hero units are sent for 0.2 s after death if they were visible, so death animations play.
- Events carry the same filtering: `EventVisible` checks the event's unit position against the viewer's vision.
  Private events (errors, gold) go only to their player.
- Enemy net worth is hidden (`-1`) in player views. Enemy hero picks stay hidden until locked.
- Spectators receive everything (`Team.None`).

## 6. Reliability, rate limits and robustness

- **Commands** are rate-limited to 40 per second per peer. The excess is dropped.
- **Malformed packets** are caught and the peer is kicked with "Protocol error".
- **Handshake timeout** is 15 s.
- **Pings** run once per second. RTT is shown in the scoreboard.

**Disconnects:**

1. The player becomes `Disconnected`, and the hero keeps its last orders.
2. After `reconnectGraceSeconds` (300 s) the player is marked `Abandoned` and a bot takes over.
3. The client retries up to 5 times. Each retry fetches a fresh ticket from `GET /api/lobbies/mine/connection`.
4. After a client restart, the main client offers **Reconnect** if the backend still lists a running match.

## 7. Results

- When the core falls (or a concede vote passes), the server builds a `MatchResult` from simulation state and sends
  `MatchEnd` to clients. It also POSTs the result to `/api/stats/matches` with `X-Bloodfall-Server-Key`.
- The stats service is the only writer of ratings, XP and history.
- Clients cannot submit results: the endpoint requires the server key, and E2E verifies a client attempt gets 401.

## 8. Security notes and upgrade path

- **Shared ticket key.** Tickets use an HMAC key shared by the backend and game servers. The upgrade path is ECDSA:
  the backend signs with a private key and servers hold only the public key, so a compromised game server cannot mint
  tickets.
- **Server authentication.** Game servers authenticate to the backend with a shared server key over TLS in
  production. Per-server credentials or mTLS come with orchestration (see SERVER_DEPLOYMENT.md).
- **UDP traffic is not encrypted** (LiteNetLib). It carries no secrets beyond the per-match ticket, which is
  short-lived and bound to a match id and account.
