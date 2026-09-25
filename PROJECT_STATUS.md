# Bloodfall: War of the Ancients — Project Status

_Last updated: 2026-09-25 · branch `claude/friendly-pascal-1xhhif`_

This file is the honest source of truth for what works. Status labels:

- **WORKING**: implemented and verified by automated tests or the end-to-end run.
- **IMPLEMENTED (unverified)**: code complete and compiling, but not yet run in its real host. Mostly Unity code: this
  environment has no Unity editor.
- **PARTIAL**: works with known gaps.
- **PLANNED**: not started.
- **BLOCKED**: cannot progress without something external.

## Build status

| Check | Result | How to reproduce |
|---|---|---|
| Server solution build (`Server/Bloodfall.sln`) | ✅ builds, 0 warnings-as-errors | `dotnet build Server/Bloodfall.sln` |
| Unit tests (`Server/tests/Bloodfall.Tests`) | ✅ 28 / 28 pass | `dotnet test Server/tests/Bloodfall.Tests` |
| End-to-end online test (real backend + game server over UDP) | ✅ **E2E PASSED** | `Tools/dev/run-e2e.sh` |
| 20-minute 5v5 bot simulation | ✅ runs, 0.21 ms/tick | `dotnet run -c Release --project Server/tools/Bloodfall.SimRunner -- 20 11` |
| Unity client scripts compile check (UnityEngine 2021.3 reference assemblies) | ✅ 0 errors | `dotnet build Tools/UnityCompileCheck` |
| Unity editor scripts compile check (Unity3D.SDK 2021.1) | ✅ 0 errors | `dotnet build Tools/UnityCompileCheck/Editor` |
| Unity 6 editor import / Play mode / Windows player build | ⛔ **not run** (no Unity editor available) | see BUILD_INSTRUCTIONS.md |
| URP shaders (`Client/Assets/Resources/Shaders`) | ⛔ never compiled by Unity | opens with the project |

## Current milestone

The plan's milestones run from 1 (move/attack/cast) to 10 (polish). Here is where each one stands.

| # | Milestone | Status |
|---|---|---|
| 1 | Move / attack / cast | **WORKING** in simulation (tests). Unity presentation: IMPLEMENTED (unverified). |
| 2 | Lane, creeps, towers | **WORKING** in simulation (waves, towers, protection chains, barracks, super/mega creeps). |
| 3 | Offline match with bots | **WORKING** headless (10-bot matches). Unity offline practice: IMPLEMENTED (unverified). |
| 4 | Multiplayer match | **WORKING** headless. Dedicated server, UDP, signed tickets, fog-filtered snapshots, reconnect and concede are all covered by E2E. |
| 5 | Client / account / lobby / server flow | Backend **WORKING** (E2E). Unity screens IMPLEMENTED (unverified). |
| 6 | Multiple heroes and items | PARTIAL: 2 of 96 heroes (Vorak, Ilyra) and 38 items are playable. |
| 7 | Vharoth event | PLANNED. Only the simulation hook exists (`Match.Vharoth.cs` is an empty stub). |
| 8 | RTS match | PLANNED. Order types and unit kinds are reserved; the queue and lobbies refuse RTS with an explanation. |
| 9 | Content expansion | PLANNED |
| 10 | Polish | PLANNED |

## Systems

### Simulation (Shared/Runtime/Simulation): WORKING

- 30 Hz fixed tick with deterministic RNG, attack point/backswing, animation cancel, armor and magic resistance, evasion,
  crits (PRD) and lifesteal.
- Data-driven abilities: 30+ effect types, trigger passives, auras, statuses (4 stacking modes), dispels, shields,
  channels, charges, toggles.
- Creep waves, towers (targeting priority, backdoor protection, protection chains), barracks, super and mega creeps,
  neutral camps (spawn, stack, leash), denies.
- XP and gold economy: bounties, streaks, first blood, assists, passive gold, death gold loss, buyback, respawn timers.
- Items: shop range, stash and delivery, recipes, auto-combine, backpack, sell refund window.
- Fog of war: ray-cast line of sight, trees, high ground, true sight. The server filters snapshots, so there is no
  map hack possible.
- Bots: lane, retreat, fight, push and defend; ability usage by tags; item builds.
  - Known weakness: low last-hit counts (see BALANCE_NOTES.md).
- Day/night cycle and announcer events.
- Practice cheats (`-gold`, `-lvlup`, `-refresh`, `-respawn`, `-startgame`). Honoured only when the match config sets
  `AllowCheats` (offline practice). Online matches never set it.

### Networking (Shared/Runtime/Protocol + GameServer): WORKING

- LiteNetLib UDP transport and binary protocol v3, with fragmentation for large snapshots.
- Content-hash and version handshake. Path separators are normalized, so Windows and Linux hash identically.
- HMAC-signed match tickets.
- Command rate limiting.
- Reconnect grace of 300 s, after which the player abandons and a bot takes over their hero.
- Concede vote.
- Result reporting server-to-server, authenticated by a game-server key. Clients cannot submit results (E2E checks this).

### Backend (Server/src/Bloodfall.Backend): WORKING, with PARTIAL items

Verified by E2E:

- register, login and refresh token rotation
- profile, match history and XP
- lobby create/join/start
- directory allocation of a game server, and tickets
- stats ingestion
- server browser listing
- server-side rejection of spoofed results

Security:

- passwords hashed with Argon2id
- short-lived JWT access tokens
- refresh tokens stored only as SHA-256 hashes, with reuse detection
- lockout after failed logins
- rate limiting
- parameterized EF Core queries throughout

Implemented but **not covered by E2E**:

- quick/unranked/ranked matchmaking with accept/decline
- friends, parties and blocks
- WebSocket chat and presence
- clans
- leaderboards
- reports and commendations
- news

These have unit-level or manual smoke coverage only.

Email delivery is **not implemented**. Verification and reset tokens are written to the backend log in Development
(see BUGS.md B-004).

### Unity client (Client/): IMPLEMENTED (unverified)

**Flow.** Splash → service checks (real HTTP calls) → login / register / password reset → main client → queue or
lobby → hero select → loading → match → post-game.

**Main client.**

- Pages: PLAY, HEROES, ITEMS, STRATEGY (an honest "in development" page), COMMUNITY, RANKINGS, PROFILE.
- Social sidebar, chat dock, server browser (sort, filter, join, spectate, create), lobby room, match-found dialog,
  and settings (graphics, audio, key rebinding, gameplay, interface).

**Match.**

- Terrain from the generated heightfield with an 8-layer splat shader.
- Water, instanced trees, props, flame lights and decals.
- Procedural stand-in models with jointed rigs and procedural animation. A Playables animator takes over when authored
  FBX models are added.
- Snapshot interpolation and client-side fog rendering.
- MOBA camera and input: smart right-click, attack-move, targeting indicators, quick-cast, pings.
- Pooled VFX: impacts, projectiles, zones, statuses, deaths.
- HUD: bars, abilities, items, stash, buffs, minimap with fog, kill feed, announcer text, chat, shop, scoreboard,
  death/buyback screen and game menu (pause in practice).

**Offline practice** hosts the same server code in-process.

### Art and audio: PARTIAL

| Asset | Status |
|---|---|
| UI kit (frames, buttons, bars, cursors, rank emblems, faction crests, logo) | Done (procedural, `Tools/art`) |
| Menu backdrop (sky, blood moon, castle, ruins, fog + live particles/lightning) | Done (procedural) |
| Terrain layer textures (8), VFX sprites (24), Velmoragh heightfield/splat/dressing | Done (procedural) |
| OFL fonts | Done |
| Hero/creep/structure/prop 3D models | **Procedural stand-ins only** (clearly stand-ins; Blender pipeline next) |
| Ability/item/status icons (73) | Done (procedural embossed emblems, `Tools/art/generate_icons.py`) |
| Hero portraits | Placeholder silhouettes; to be replaced by renders of the Blender models (T-001) |
| UI sounds, combat/spell/death SFX, ambience, music loops and stingers (61 clips) | Placeholder quality, procedurally synthesised (`Tools/audio/generate_audio.py`) |
| Announcer (35 lines) | Placeholder: espeak-ng speech processed into a deep, reverberant voice. Original streak names. |
| Hero voice lines | **Missing** (no content references any yet) |

## Next step (exact)

1. **Art pipeline (in progress next):**
   - Blender (bpy) generators for the hero and creep models with rigs and the standard clips (Idle, Run, Attack1/2,
     Cast1/2/3, CastUlt, Channel, Stun, Death).
   - Structures and props.
   - Exported as FBX to `Client/Assets/Resources/Models/<modelKey>.fbx`, where the model factory picks them up
     automatically.
2. New concept heroes in data (Nyxara, Malgrave, Ardyn, Fenrax, Morwen, Thael), with bots and tests.
3. Vharoth event (milestone 7) in `Match.Vharoth.cs`, with tests.
4. On a machine with Unity 6000.0.40f1:
   - Open `Client/`, run **Bloodfall ▸ Setup Project**, press Play.
   - Fix anything in the URP/Input System branches and shaders that the headless checks cannot see.
   - Record the results here.
