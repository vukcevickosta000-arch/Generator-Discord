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
| Unit tests (`Server/tests/Bloodfall.Tests`) | ✅ 73 / 73 pass | `dotnet test Server/tests/Bloodfall.Tests` |
| End-to-end online test (real backend + game server over UDP) | ✅ **E2E PASSED** | `Tools/dev/run-e2e.sh` |
| 20-minute 5v5 bot simulation (all eight heroes) | ✅ runs, 0.28 ms/tick | `dotnet run -c Release --project Server/tools/Bloodfall.SimRunner -- 20 11` |
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
| 6 | Multiple heroes and items | PARTIAL: **8 of 96 heroes** playable (all eight concept heroes: Vorak, Ilyra, Nyxara, Malgrave, Ardyn, Fenrax, Morwen, Thael), each with kit tests and bot usage. 38 items. |
| 7 | Vharoth event | **WORKING** in simulation: seals, awakening, three boss phases, Blood Moon, Heart of Vharoth with revive, bots that break seals and kill him. 13 tests. Unity presentation (models, effects, HUD boss bar, Blood Moon lighting, corpse): IMPLEMENTED (unverified). |
| 8 | RTS match | PLANNED. Order types and unit kinds are reserved; the queue and lobbies refuse RTS with an explanation. |
| 9 | Content expansion | PLANNED |
| 10 | Polish | PLANNED |

## Systems

### Simulation (Shared/Runtime/Simulation): WORKING

- 30 Hz fixed tick with deterministic RNG, attack point/backswing, animation cancel, armor and magic resistance, evasion,
  crits (PRD) and lifesteal.
- Data-driven abilities: 35+ effect types, trigger passives (including periodic ones), auras, statuses (4 stacking
  modes), dispels, shields, channels, charges, toggles, transforms, walls and summons that scale with level.
  - Effects can be gated by conditions: statuses, day/night, HP thresholds, unseen, stacks, nearby trees.
  - Curse damage credits its caster, veils break on action, and casts can be echoed.
  - The full list is in TECHNICAL_ARCHITECTURE.md §2.
- Creep waves, towers (targeting priority, backdoor protection, protection chains), barracks, super and mega creeps,
  neutral camps (spawn, stack, leash), denies.
- XP and gold economy: bounties, streaks, first blood, assists, passive gold, death gold loss, buyback, respawn timers.
- Items: shop range, stash and delivery, recipes, auto-combine, backpack, sell refund window.
- Fog of war: ray-cast line of sight, trees, high ground, true sight. The server filters snapshots, so there is no
  map hack possible.
- Bots: lane, retreat, fight, push and defend; ability usage by tags; item builds.
  - Known weakness: low last-hit counts (see BALANCE_NOTES.md).
- Day/night cycle and announcer events.
- **Vharoth event** (GAME_DESIGN.md §8):
  - Blood Seals broken by a contested channel, then the Blood Titan with leash and reset.
  - Crush, Blood Rain and Titan's Wrath phases.
  - A Blood Moon that forces night and shrinks vision.
  - The Heart of Vharoth relic (revive on the spot, Bloodthirst).
  - The event state is in every snapshot header (protocol v4).
- Practice cheats (`-gold`, `-lvlup`, `-refresh`, `-respawn`, `-startgame`). Honoured only when the match config sets
  `AllowCheats` (offline practice). Online matches never set it.

### Networking (Shared/Runtime/Protocol + GameServer): WORKING

- LiteNetLib UDP transport and binary protocol v4, with fragmentation for large snapshots.
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
| Hero/creep/summon/neutral/boss/structure 3D models | **Done (generated)**: 56 FBX models from the Blender pipeline (`Blender/scripts`), with rigs, 12–13 animation clips and baked ambient occlusion (`Docs/Images/models_all.png`). Stylised primitive-based modelling, not sculpted or textured. Not yet imported in Unity. |
| Map props and trees | Procedural stand-ins (C#); Blender versions are the next art task |
| Ability/item/status icons (123) | Done (procedural embossed emblems, `Tools/art/generate_icons.py`) |
| Hero portraits (8) | Placeholder silhouettes with per-hero headgear; to be replaced by renders of the Blender models (T-001) |
| UI sounds, combat/spell/death SFX, ambience, music loops and stingers (97 clips) | Placeholder quality, procedurally synthesised (`Tools/audio/generate_audio.py`) |
| Announcer (35 lines) | Placeholder: espeak-ng speech processed into a deep, reverberant voice. Original streak names. |
| Hero voice lines | **Missing** (no content references any yet) |

## Next step (exact)

1. **Art (T-001 remainder):** Blender props and trees, then portraits rendered from the models.
2. **RTS mode (milestone 8).** The order types and unit kinds are reserved; see GAME_DESIGN.md §9.
3. **Hero-specific bot behaviour** (T-022) and last-hitting (T-016). The Nyxara bot is the weakest in bot matches.
4. On a machine with Unity 6000.0.40f1:
   - Open `Client/`, run **Bloodfall ▸ Setup Project**, press Play.
   - Fix anything in the URP/Input System branches and shaders that the headless checks cannot see.
   - Review the new stand-in models and effects (T-002).
   - Record the results here.
