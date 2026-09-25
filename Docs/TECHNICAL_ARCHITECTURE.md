# Technical Architecture

## 1. Overview

```
            ┌────────────────────────────── Shared (netstandard2.1, no engine) ──────────────────────────────┐
            │ Core: JSON DOM + mapper, deterministic RNG/PRD, math    Data: definitions + GameData loader/hash  │
            │ Simulation: Match (30 Hz), units, abilities/effects, AI, vision, nav grid                          │
            │ Protocol: binary codec, MatchHost (server session), GameClient, tickets    Contracts: REST/WS DTOs │
            └───────────────┬───────────────────────────────┬───────────────────────────────┬─────────────────┘
                            │                               │                               │
            Unity client (com.bloodfall.shared)    Dedicated game server             Backend (ASP.NET Core)
            presentation + offline host             MatchHost over LiteNetLib         accounts, social, lobbies,
                                                                                      matchmaking, directory, stats
```

- One simulation codebase runs on the dedicated server, inside the client for offline practice, and in the tests and
  the SimRunner.
- The client never simulates online; it renders authoritative snapshots.

## 2. Shared/Runtime

| Folder | Responsibility |
|---|---|
| `Core/` | `Json` (tolerant parser/writer), `JsonMapper` (reflection mapping, camelCase), `MathUtil` (armor formula, deterministic xorshift RNG, pseudo-random distribution with a bisection-solved C), `LeveledValue` |
| `Data/` | Definitions (heroes, abilities, effects, statuses, items, units, maps, rules, modes, factions), `GameData.Load` (sorted by path, SHA-256 content hash with normalised line endings and separators, link + validate), loaders |
| `Simulation/` | `Match` partial classes: Statuses, Effects, Combat, Casting, Movement, Projectiles, Items, Spawning, Trees, Vharoth, Rts. Plus `NavGrid` (A* + LOS smoothing), `SpatialHash`, `VisionSystem`, AI brains |
| `Protocol/` | `NetWriter/NetReader`, `Codec`, `Fragments`, `MatchHost`, `GameClient` + loopback, `Tickets`, `MatchResult` |
| `Contracts/` | Every REST and WebSocket DTO shared by backend and client |
| `Resources/GameData` | JSON content (hash-checked on connect). `Resources/GameDataIndex.txt` maps names back to paths for Unity. |

### Simulation details

- **Tick:** 30 Hz fixed (`Match.Step`). Time is negative during pre-game.
- **Order of work per tick:** orders → statuses/auras → unit AI → movement and forced motion → attacks and casts →
  projectiles/zones → deaths and respawns → spawning → vision → economy.
- **Units:** meters. 1 m ≈ 80 classic MOBA units.
  - Nav grid: 0.5 m cells. Cell bits: walkable, 2-bit height level, tree, structure, vision blocker, water, special.
  - Vision grid: 1 m cells.
- **Abilities** are data: `AbilityDef` → `EffectDef` graph (Damage, Heal, ApplyStatus, Area, Projectile,
  Dash/Leap/Blink/Teleport, Knockback/Pull, Delayed, Zone, CreateWall, SpawnUnit, ConsumeCorpse, Chance, Sequence,
  Execute, Swap, Transform, ModifyCooldowns, EchoCast, ForceNight, …).
  - **Passives** are `TriggerDef`s: AttackLanded, Attacked, DamageTaken, DamageDealt, Kill, Death, NearbyDeath,
    AbilityCast, SpellHit, LowHealth and Interval (every `internalCooldown` seconds).
  - **Auras** apply statuses in a radius. They are keyed by ability definition, so the same aura never stacks with
    itself.
  - **Conditions.** Any effect can carry a `condition` gate: `targetHasStatus:`, `targetLacksStatus:`,
    `casterHasStatus:`, `casterLacksStatus:`, `night`, `day`, `targetIsHero`, `targetNotHero`, `targetHpBelow:`,
    `casterHpBelow:`, `casterUnseen`, `casterStacksAtLeast:<status>:<n>`, `casterNearTrees:<count>:<radius>`.
  - **Effect targets:** Target, Caster, Point, TriggerSource and StatusSource (whoever applied the running status).
  - **Kill credit.** `creditStatusSource` credits curse and DoT damage to the status' source.
  - **Veils.** Statuses with `breakOnAction` end when the bearer attacks or casts; abilities marked `keepsStealth`
    are the exception.
  - **Summons** run their abilities at the level of the ability that raised them. Their `tags` choose the AI:
    `stationary` or `guard`.
  - **Trees.** `Match.Trees.cs` indexes the map's tree records (`CountTreesNear`, `ForEachTreeNear`) and tracks
    lumber per tree. `DestroyTree` clears the trunk's cells with `NavGrid.ClearTreeAt` (0.75 m, the same rule the
    client applies on `TreeDestroyed`) and rebuilds vision locally.
- **RTS mode** (`Match.Rts.cs`, active when the mode's `kind` is `Rts`):
  - The same tick loop runs, without waves, passive income or Vharoth; `UpdateRts` runs after unit movement.
  - Worker orders (Harvest, Build, ReturnResources) are validated in `IssueOrder` before they are queued, and
    re-checked when they execute.
  - Building orders (Train, SetRally, CancelQueue) are immediate.
  - Buildings and veins block their footprint (0.85 × collision radius) as dynamic nav-grid blocks, released on
    death or cancel.
  - Supply is recomputed every tick from living units, queues and finished buildings.
  - New unit kinds: `Soldier` (targets as Creep) and `Resource` (veins: never attacked, not in `TargetType.All`).
  - New action state: `Working`. New events: ConstructionComplete, UnitTrained, ResourcesDelivered, MineDepleted and
    PlayerEliminated (UnitTrained and ResourcesDelivered are private to the owner).
  - Data:
    - `UnitDef`: costs, `buildTime`, `supplyProvided`, `trains`, `builds`, `requires`, drop-off flags, gather
      amounts and times, `selfBuilds`, `resourceAmount`.
    - `rtsFactions` (hall, worker, starting units).
    - `MapDef.startLocations` and `resourceNodes`.
    - `PlayerSetup.RtsFaction`.
- **Randomness:** only through the match RNG (seeded). PRD for procs and crits.
- **Events:** `SimEvent` is the only output channel besides state. Each has a `PlayerId` (−1 = broadcast), and the
  server filters them per team.

## 3. Game server (Server/src/Bloodfall.GameServer)

- A single-match process:
  1. Registers with the directory (region, address, port, content hash).
  2. Heartbeats every few seconds.
  3. Receives a `ServerAssignment` with the players, mode and seed.
  4. Creates a `Match` + `MatchHost` and accepts UDP peers.
  5. Reports the `MatchResult` to `/api/stats/matches` using the game-server key.
  6. Returns to idle.
- It answers unconnected pings (`bf-pong:region:state`) so clients can measure region latency.

Flags:

| Flag | Meaning |
|---|---|
| `--port` | UDP port |
| `--region` | Region id |
| `--public-address` | Address advertised to clients |
| `--backend` | Backend URL |
| `--server-key` | Game-server key for authenticated calls |
| `--ticket-key` | Ticket signing key |
| `--id` | Server id |
| `--gamedata` | Game data path |
| `--client-version` | Required client version |
| `--dev-concede-anytime` | Development only |

## 4. Backend (Server/src/Bloodfall.Backend)

This is a modular monolith. See [BACKEND_ARCHITECTURE.md](BACKEND_ARCHITECTURE.md).

## 5. Unity client (Client/)

**No scene wiring.** `Bootstrap` (`RuntimeInitializeOnLoadMethod`) creates a single `GameApp` MonoBehaviour. Everything
else is plain C# ticked in a fixed order. The UI is UI Toolkit, built in C# and styled by
`Resources/UI/Styles/bloodfall.uss`.

| Namespace / folder | Contents |
|---|---|
| `Core/` | `GameApp` (service host, main-thread queue), `FlowController` (state machine + real service checks), `ClientConfig`, `ClientSettings` (+ key binds), `MenuBackdrop` (2.5D parallax scene), `RenderPipelineBridge` (URP isolation), `CursorManager` |
| `Backend/` | `ApiClient` (UnityWebRequest, token refresh on 401), `RealtimeClient` (WebSocket), `BackendClient` (facade + live social state), `SessionStore` |
| `Networking/` | `LiteNetClientTransport` (UDP, fragment reassembly, unconnected ping) |
| `Match/` | `MatchController` (connection, phases, loading, reconnect), `MatchWorld` (interpolation, events, lighting), `MapRenderer` (terrain chunks, water, instanced trees, props, decals, minimap bake), `EntityView`, `ModelFactory` + `ProceduralModels` + `MeshBuilder`, `UnitAnimator` (Playables clips or procedural rig), `VfxSystem`, `FogOfWar`, `MobaCamera`, `MatchInput` |
| `UI/` | `UIManager` (layers, dialogs, toasts, tooltips), `El` (element builders), `GameText`, screens (`Screens/*`), HUD (`Hud/*`) |
| `Audio/` | `AudioManager` (buses, pooled 3D sources, announcer queue, music crossfade) |
| `Combat/` | `ParticleFactory` (code-defined particle systems) |
| `Editor/` | `ProjectSetup` (URP asset, colour space, Boot scene), `AssetImportRules`, `BloodfallTools` (validation, builds) |

### Rendering

- **URP:** forward path, HDR, depth texture for water, soft shadows.
- **Custom HLSL shaders** in `Resources/Shaders`:
  - `Terrain`: 8-layer height-blended splat, slope-based rock, surface-gradient bump, fog of war.
  - `Lit`: units, structures and props. Rim, hit flash, dissolve, fog of war.
  - `Foliage`: wind and translucency, instanced.
  - `Water`: procedural ripples, depth fade, foam.
  - `GroundDecal`, `ParticleAdd/Alpha`, `BackdropLayer`.
- **Fog of war:** the shared `VisionSystem` runs on the client from allied units. The smoothed texture is set as
  `_BF_FogTex`. Hidden enemies are never sent by the server, so the fog is purely visual.
- **Post-processing:** ACES, bloom, vignette, grading, film grain. Separate looks for the menu and the match.

### Assets and fallbacks

- **Models** are loaded from `Resources/Models/<modelKey>` (Blender FBX). Missing models get procedural stand-ins,
  logged once.
- **Icons** are loaded from `Resources/Textures/Icons/...`. Missing icons leave empty frames.
- **Audio** clips are loaded from `Resources/Audio/<Category>/<name>`. Missing clips are skipped with a log line.

## 6. Determinism and authority

| Concern | Authority | Client role |
|---|---|---|
| Movement, pathing, collisions | Server | Interpolates snapshots |
| Damage, statuses, cooldowns, mana | Server | Local pre-checks only for instant error feedback |
| Gold, XP, items, purchases | Server | Sends buy/sell/swap orders |
| Vision | Server filters snapshots and events | Renders fog from allied units |
| Match result, stats, rating, XP | Game server → backend (server key) | Displays results fetched from the backend |
