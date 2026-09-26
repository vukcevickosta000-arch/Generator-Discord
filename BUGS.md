# Known Bugs and Limitations

Severity levels:

- **S1**: blocks play
- **S2**: wrong behaviour
- **S3**: cosmetic or minor

Everything here is real and reproducible (or a verified limitation). Fixed bugs are listed at the bottom with the fix.

## Open

| ID | Sev | Area | Description | Repro / notes |
|---|---|---|---|---|
| B-001 | S1* | Unity client | The client has **never been run inside Unity**, including the new RTS input and HUD. The C# compiles against Unity reference assemblies, but code behind `BLOODFALL_URP` and `BLOODFALL_INPUT_SYSTEM`, and all HLSL shaders, are unchecked. Expect first-open fixes. | Open `Client/` in Unity 6000.0.40f1. *S1 until verified. |
| B-002 | S2 | Bots | Low last-hit counts: about 25 last hits per bot in 20 minutes. Bot games now always end: 40 of 40 seeds, in 25–53 minutes (F-035). | `SimRunner -- 20 11`. See T-016. |
| B-003 | S3 | Art | Unit and structure models are generated from primitives (rigged, animated, vertex-coloured, no textures). Props and trees are still procedural C# stand-ins. None of the FBX models has been imported into Unity yet: orientation, scale and clip import are unverified. | T-001 (props, trees, portraits), T-002 (Unity review) |
| B-004 | S2 | Backend | Password reset and email verification tokens are **not emailed**. In Development they appear only in the backend log. | T-017 |
| B-005 | S3 | Audio | All audio is procedural placeholder quality. The announcer is synthetic speech. There are no hero voice lines. | Replace with recorded/composed audio; keep the file keys. |
| B-006 | S2 | Networking | No client-side prediction. The local hero responds after one round trip plus interpolation delay (about 100 ms). | T-012 |
| B-007 | S3 | Unity client | Health bars and floating text are UI Toolkit elements moved each frame with `translate` (no layout pass; styles are written only when a value changes). Frame cost with 100+ units is still unmeasured in Unity. | T-019 |
| B-008 | S2 | Simulation | Illusion and Resurrect effect types are parsed but do nothing (no current content uses them). | T-018 |
| B-009 | S3 | Client | `SecureStore` obfuscates the remembered refresh token with XOR. It is not OS-encrypted (DPAPI or Keychain is still to do). A stolen PlayerPrefs file could replay the session until the token expires or rotates. | Documented in `ApiClient.cs` |

## Fixed

| ID | Description | Fix |
|---|---|---|
| F-001 | Ranged attacks never hit: the projectile's last known target position defaulted to (0,0) and triggered dodge logic. | Set `LastKnownTargetPos` at launch. |
| F-002 | Bots dove towers and died to creeps. | Tower-threat avoidance, local lane direction, consumable use, retreat exit. |
| F-003 | LiteNetLib `TooBigPacketException` on large snapshots. | 900-byte fragments, a reliable fallback, and a try/catch in the server loop. |
| F-004 | Concede did nothing during pre-game in dev mode (negative match time). | `ConcedeMinTime > 0f` guard plus the dev flag. |
| F-005 | Blank player names in lobbies and tickets when presence was empty. | The database display name is used. |
| F-006 | Bots could pick the hero a human wanted, and with a two-hero roster humans could be locked out. | Bots pick after humans, and unique picks relax when the roster is smaller than the player count. |
| F-007 | The content hash would differ between Windows and Linux (path separators). | Separators are normalized before hashing, with a test. |
| F-008 | Hexed units could not move at all: the Hexed flag counted as a hard disable, making hex a stun with a model swap. | Hex is no longer a hard disable; hex statuses carry Silenced/Disarmed/Muted. It still interrupts casts and channels. Test: `Morwen_ToadHex_SilencesButLetsTheToadHop`. |
| F-009 | Unit-targeted leaps ran their landing effects on the caster (the arrival context replaced the target with the lander). | Only point-targeted leaps use the lander as target. Test: `Fenrax_Pounce_LeapsAndRoots`. |
| F-010 | Crit multipliers from several sources added up (1.8 + 2.0 = 3.8×). | The strongest multiplier wins. Test: `CritMultiplier_TakesTheStrongestSource`. |
| F-011 | Snapshots fell back to the unit **id** instead of the definition's model key when no model override was active, so a unit whose model key differs from its id would render the wrong model. | The override is null when absent; the client resolves the definition's model. |
| F-012 | Bots with a blink tagged `escape` always fled, even when engaging. | Abilities tagged `escape,engage` jump onto the target when engaging. |
| F-013 | The same aura from two copies of a hero (possible when the roster is smaller than the lobby) stacked. | Auras are keyed by ability definition. Test: `Ardyn_SameAuraFromTwoArdyns_DoesNotStack`. |
| F-014 | Summons ignored the level of the ability that raised them: their abilities always ran at level 1. | Summon abilities take the caster's ability level; stats recompute on spawn. |
| F-015 | Every channel except the waystone showed Ilyra's blood beam. | Channel effects are chosen per ability (seal, grove, generic). |
| F-016 | A shop tab would appear for a category with nothing for sale (Relics). | Tabs are only created for categories with purchasable items. |
| F-017 | A unit on attack-move whose target died during its wind-up stayed frozen in the wind-up forever, if nothing else was in reach (any mode; found through the RTS AI). | The swing is dropped and the unit keeps marching. Regression test `AttackMoveResumesWhenTheTargetDiesMidSwing`. |
| F-018 | RTS: mirrored bases mined at different rates (the Dusk start won 22 of 24 Dawnguard mirrors). Walking to a vein or hall aimed at its blocked centre, and the path search then chose a free cell by scan order, on the far side for one start and the near side for the other. | Workers walk to the side of veins, halls, sites and trees that faces them (`ApproachObject`). 80 mirror games now split 36–40. |
| F-020 | A finished match's lobby stayed open until the game server's next heartbeat, so starting another game at once failed with "Leave your current lobby first". | The lobby closes as soon as the server reports the result. |
| F-021 | `CreateLobbyRequest.MapId` defaulted to Velmoragh whatever the mode. | It defaults to the mode's map. The backend rejects a MOBA mode on a map without lanes, and an RTS mode on a map without start locations. |
| F-022 | The Ashen Legion's raise went to the first player in the list whenever two Legion players qualified. That settled mirror games for the Dawn start (32–6). | The killer's side raises first, otherwise the player with the closest soldier (`TheKillersSideRaisesFirstInALegionMirror`). |
| F-023 | About one RTS bot game in five was drawn at the 30-minute cap. An army stepped along straight lines into cliffs, or waited forever on a scout spot it could not reach. | Staging follows the nav path; scouting moves on after 30 s without progress (fighting excluded). Draws are now 0–1 per 40 games. |
| F-024 | Once camps guarded their ground, the army that reached the Ashfields centre first fought the Ancient camp and then lost to the other army. | The centre camp is marked as not guarding (`guards: false`). Attack-move passes camps that are not fighting, and the bot walks round guarding camps. |
| F-025 | In RTS matches, units always updated in creation order, and so did the bots (the Dawn bot first). The Dawn bot also thought 0.25 s earlier each second (phase = player id × 0.25 s). The first player tended to win simultaneous exchanges and decisions. | Unit and bot updates alternate direction every tick in RTS matches, and each bot's think phase is random (seeded). |
| F-026 | RTS bots played nearly the same game for every seed, so a 40-game series was a handful of games repeated, and its side splits (13–26, 28–11) reflected tie-breaks, not the map. | Each bot has seeded variety (wave size, hold time, expansion time) on its own RNG. Mirror splits are now 19–21 to 23–16. |
| F-027 | The Veteran RTS bot lost to Normal (11–18, 10–20): it pulled hurt units home in the decisive fight. | The pull-back was removed. Veteran and Nightmare wait for a 34-supply first wave and counter-attack; Veteran now beats Normal 22–6 and 30–0. |
| F-028 | On Ashfields, which has no bases, the MOBA bot's retreat point, "the fountain", fell back to (0,0), the Dawn corner. Dusk hero bots would have escaped into the enemy. | Maps without bases use the team's start location. |
| F-029 | A* expanded neighbours in one fixed order, so on a point-symmetric map a path and its mirror image broke ties differently. RTS mirrors with altars leaned Dusk 64–86. | Starts on the far half expand in the point-reflected order; mirrors are now 112–121. |
| F-030 | `Match.Emit` treats `PlayerId` 0 as "not set" and broadcasts it, so player 0's private last-hit gold popups were sent to every player. | Last-hit gold goes through `EmitPrivate`; `Emit` documents the pitfall; a test checks the event reaches only its player. |
| F-031 | Health bars and combat text trailed their units by a frame and jittered whenever the camera moved: they were projected in `Update`, but the camera moves in `LateUpdate`. With the camera following the hero it happened constantly. | The overlay now draws from `MatchWorld.CameraMoved`, raised after the camera update. |
| F-032 | The health bar's shield segment was never shown: shield amounts were not in the snapshot. | Protocol v9 streams each unit's remaining shield; the bar draws it after current health (test in `Ardyn_AegisOfDawn_AbsorbsDamage_ThenBursts`). |
| F-033 | Pooled health bars were reused for a different unit kind by destroying and recreating them, and `Clear` left pooled bars attached to the UI between matches. Hero bars could draw underneath creep bars. | One pool per bar kind, separate layers (structures, units, heroes, then text), and `Clear` empties everything. |
| F-034 | One exception in the match tick skipped the HUD and every other system for that frame, and a fault repeating every frame wrote a stack trace every frame (itself a stall). | `Faults` contains each stage (match, late tick, events, each entity view, each HUD) and logs a repeating fault at most every 10 s with a repeat count. |
| F-035 | Bot games never ended: 17 of 22 hour-long 5v5 games had no winner, even at 188–31 kills with every enemy tower and barracks down. Every lane ends about 18 m short of the core, and a wave that reached the last waypoint stood there idle, out of the core's reach; bots waited behind their creeps. | Past the lane end, creeps attack-move to the nearest enemy tower, barracks or core (preferring ones that can be damaged). All 40 seeds now end, in 25–53 minutes. Test: `CreepsPastTheLaneEnd_MarchOnTheCore` (fails without the fix). |
| F-036 | Blood War units updated in creation order every tick, and each wave spawns Dawn's creeps first, so Dawn's side struck first in every simultaneous exchange. | The update direction alternates every tick in both modes (it already did in RTS). There was no real side bias to begin with (F-038). |
| F-037 | Blender previews and hero portraits showed every colour darker and greyer than authored (vertex colours decoded twice, then the AgX transform). | Materials re-encode the attribute, and renders use the Standard transform; portraits re-rendered. |
| F-038 | (Not a bug.) The Dawn lean in 5v5 bot games (33 of 40) came from `SimRunner` giving the two teams different default lineups (Dawn heroes 1–5, Dusk 5–8 then 1). With the same hero on all ten bots, Dusk won 16 and Dawn 13 of 30. | Documented in TESTING.md. `--random` now draws ten distinct heroes per seed for balance runs. |
| F-019 | Ashfields was not fully symmetric: forests were sampled across the whole map, and neutral camps rolled their creeps at random. | Trees are sampled on one half and point-mirrored; mirrored camps get the same creeps. |
