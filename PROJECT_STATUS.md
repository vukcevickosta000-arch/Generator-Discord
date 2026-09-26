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
| Unit tests (`Server/tests/Bloodfall.Tests`) | ✅ 115 / 115 pass | `dotnet test Server/tests/Bloodfall.Tests` |
| End-to-end online test (real backend + game server over UDP): a MOBA match, then an RTS match | ✅ **E2E PASSED** | `Tools/dev/run-e2e.sh` |
| 20-minute 5v5 bot simulation (all eight heroes) | ✅ runs, 0.28 ms/tick | `dotnet run -c Release --project Server/tools/Bloodfall.SimRunner -- 20 11` |
| RTS bot games (Ashfields, 40-game series) | ✅ games decided in 12–14 min on average with heroes. Faction totals 45–54%. Start sides even: 112–121 over six mirror series. 0–3 draws per 40 games. About 0.05 ms/tick. | `... SimRunner -- 30 5000 --rts --games 40 --factions a,b` |
| Unity client scripts compile check (UnityEngine 2021.3 reference assemblies) | ✅ 0 errors | `dotnet build Tools/UnityCompileCheck` |
| Unity editor scripts compile check (Unity3D.SDK 2021.1) | ✅ 0 errors | `dotnet build Tools/UnityCompileCheck/Editor` |
| Unity 6 editor import / Play mode / Windows player build | ⛔ **not run** (no Unity editor available) | see BUILD_INSTRUCTIONS.md |
| URP shaders (`Client/Assets/Resources/Shaders`) | ⛔ never compiled by Unity | opens with the project |

## Current milestone

The plan's milestones run from 1 (move/attack/cast) to 10 (polish). Here is where each one stands.

| # | Milestone | Status |
|---|---|---|
| 1 | Move / attack / cast | **WORKING** in simulation (tests). Unity presentation: IMPLEMENTED (unverified). |
| 2 | Lane, creeps, towers | **WORKING** in simulation (waves, towers, protection chains, barracks, super/mega creeps). Economy: 32 gold per lane creep, 0.5 gold/s passive. Couriers: the ` key flies bought items from the stash to the hero (5 tests; HUD button compile-checked only). |
| 3 | Offline match with bots | **WORKING** headless (10-bot matches). Unity offline practice: IMPLEMENTED (unverified). |
| 4 | Multiplayer match | **WORKING** headless. Dedicated server, UDP, signed tickets, fog-filtered snapshots, reconnect and concede are all covered by E2E. |
| 5 | Client / account / lobby / server flow | Backend **WORKING** (E2E). Unity screens IMPLEMENTED (unverified). |
| 6 | Multiple heroes and items | PARTIAL: **8 of 96 heroes** playable (all eight concept heroes: Vorak, Ilyra, Nyxara, Malgrave, Ardyn, Fenrax, Morwen, Thael), each with kit tests and bot usage. 38 items. |
| 7 | Vharoth event | **WORKING** in simulation: seals, awakening, three boss phases, Blood Moon, Heart of Vharoth with revive, bots that break seals and kill him. 13 tests. Unity presentation (models, effects, HUD boss bar, Blood Moon lighting, corpse): IMPLEMENTED (unverified). |
| 8 | RTS match | PARTIAL.<br>**Server side WORKING** (R1–R4 and R2: 32 simulation tests, 4 protocol tests, E2E section 5): harvesting, construction, training, supply, rally points, victory by razing, the Ashfields map, an RTS AI at four difficulties, protocol v7, lobby factions and the strategy queue, all played over UDP.<br>**Unity interface IMPLEMENTED (unverified)** (R5): selection, control groups, command card, placement ghost, resource HUD, practice vs AI, Strategy queue and lobby factions. It compiles against the reference assemblies but has never been run in Unity.<br>**All four factions** (Dawnguard, Ashen Legion, Crimson Court, Wild Covenant). Each has its own mechanic (healing shrines, raising the dead, Blood Price, Moonlit night forms) and five or six research upgrades, and each wins 45–58% of 40-game bot series against every other faction. Also:
- research over protocol v6 with command-card buttons, and camps that guard the expansions;
- **hero altars:** up to three Blood War heroes per player, levelling from kills and revived at altars, with bots that recruit and cast (protocol v7; Unity buttons, targeting and hero bar compile-checked only).

12 faction and hero tests, 4 protocol tests and the E2E cover this. Open: hero balance for the strategy mode (T-035). |
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
- **RTS rules** (`Match.Rts.cs`, GAME_DESIGN.md §11):
  - Each player starts with a hall and five workers next to a blood-iron vein.
  - Workers mine veins (one worker at a time per vein) and cut trees. Trees fall when their lumber runs out, which
    opens paths and vision.
  - Construction: placement is checked twice (when ordered and on arrival). Dawnguard buildings need a builder, and
    extra builders speed them up; Ashen Legion buildings rise on their own. Cancelling refunds 75%.
  - Training queues (5 slots), supply cap from buildings, rally points (a vein rally puts new workers to work),
    watchtowers that shoot once finished, and one-time neutral camps whose bounty goes to the owner.
  - A player with no buildings left is eliminated.
  - All costs, requirements, placement and supply are validated on the server.
  - **RTS AI** (`AI/RtsAi.cs`):
    - Plays through the same validated orders as a human and only knows what a player can see.
    - Four difficulties: Beginner < Normal < Veteran, measured. Nightmare currently plays like Veteran.
    - Takes over abandoned players.
  - Fixed along the way: an attack-move wind-up freeze in every mode, and start-side unfairness on Ashfields
    (BUGS.md F-017 to F-019).
- Practice cheats (`-gold`, `-lvlup`, `-refresh`, `-respawn`, `-startgame`). Honoured only when the match config sets
  `AllowCheats` (offline practice). Online matches never set it.

### Networking (Shared/Runtime/Protocol + GameServer): WORKING

- LiteNetLib UDP transport and binary protocol v5, with fragmentation for large snapshots.
- RTS support: group orders, per-entity construction/training/cargo/vein state, private economy, fog for enemy
  buildings.
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

**War of the Ancients (RTS).** Its own input and HUD are chosen automatically for RTS matches.
- `RtsInput`: selection, control groups, the smart right-click, attack-move, rally, and a placement ghost that
  mirrors the server's placement rules.
- `RtsHudScreen`: resources and supply, the selection panel (construction, queue with cancel, cargo, vein), the
  command card with hotkeys and costs, alerts, chat and menu.
- Buildings under construction sink into the ground; workers swing while working and carry visible cargo; enemy
  buildings out of sight stay as last-seen ghosts.
- Entry points: Strategy page (Practice vs AI), the Practice dialog, the Strategy queue with a faction pick, and
  custom lobbies with per-slot factions.

### Art and audio: PARTIAL

| Asset | Status |
|---|---|
| UI kit (frames, buttons, bars, cursors, rank emblems, faction crests, logo) | Done (procedural, `Tools/art`) |
| Menu backdrop (sky, blood moon, castle, ruins, fog + live particles/lightning) | Done (procedural) |
| Terrain layer textures (8), VFX sprites (24), Velmoragh heightfield/splat/dressing | Done (procedural) |
| Ashfields (RTS map) heightfield/splat/dressing | Done (procedural, `Tools/mapgen/generate_ashfields.py`, preview `Docs/Images/ashfields_layout.png`) |
| OFL fonts | Done |
| Hero/creep/summon/neutral/boss/structure 3D models | **Done (generated)**: 83 FBX models (including the RTS blood-iron vein, the Crimson Court and Wild Covenant kits and four hero altars) from the Blender pipeline (`Blender/scripts`), with rigs, 12–13 animation clips and baked ambient occlusion (`Docs/Images/models_all.png`). Stylised primitive-based modelling, not sculpted or textured. Not yet imported in Unity. |
| Map props and trees | Procedural stand-ins (C#); Blender versions are still to do |
| RTS unit and building models | **Crimson Court and Wild Covenant: generated.** 22 models: blood-marble noble buildings, organic Covenant groves, a lodge, a stone circle and a den, and every unit including the shifter's wolf form. **Dawnguard and Ashen Legion: borrowed** Dawn/Dusk creep and structure models, scaled (TODO T-034). |
| Ability/item/status icons (123) | Done (procedural embossed emblems, `Tools/art/generate_icons.py`) |
| Hero portraits (8) | Rendered from the hero models (`Blender/scripts/render_portraits.py`), faction-coloured |
| UI sounds, combat/spell/death SFX, ambience, music loops and stingers (97 clips) | Placeholder quality, procedurally synthesised (`Tools/audio/generate_audio.py`) |
| Announcer (35 lines) | Placeholder: espeak-ng speech processed into a deep, reverberant voice. Original streak names. |
| Hero voice lines | **Missing** (no content references any yet) |

## Next step (exact)

1. **RTS mode (milestone 8), in order** (details in TODO.md T-030 to T-034):
   - T-035: tune the Blood War heroes for the strategy mode (R2 is otherwise done: factions, research, altars).
   - R5 follow-ups: dedicated models and unit icons (T-033, T-034).
2. **Art (T-001 remainder):** Blender props and trees.
3. **Hero-specific bot behaviour** (T-022) and last-hitting (T-016). The Nyxara bot is the weakest in bot matches.
4. On a machine with Unity 6000.0.40f1:
   - Open `Client/`, run **Bloodfall ▸ Setup Project**, press Play.
   - Fix anything in the URP/Input System branches and shaders that the headless checks cannot see.
   - Review the new stand-in models and effects (T-002).
   - Record the results here.
