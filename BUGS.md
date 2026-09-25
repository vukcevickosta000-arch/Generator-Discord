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
| B-002 | S2 | Bots | Low last-hit counts: about 25 last hits per bot in 20 minutes, and the bot match did not end within 20 minutes (13–20 kills, 4 v 2 towers). | `SimRunner -- 20 11`. See T-016. |
| B-003 | S3 | Art | Unit and structure models are generated from primitives (rigged, animated, vertex-coloured, no textures). Props and trees are still procedural C# stand-ins. None of the FBX models has been imported into Unity yet: orientation, scale and clip import are unverified. | T-001 (props, trees, portraits), T-002 (Unity review) |
| B-004 | S2 | Backend | Password reset and email verification tokens are **not emailed**. In Development they appear only in the backend log. | T-017 |
| B-005 | S3 | Audio | All audio is procedural placeholder quality. The announcer is synthetic speech. There are no hero voice lines. | Replace with recorded/composed audio; keep the file keys. |
| B-006 | S2 | Networking | No client-side prediction. The local hero responds after one round trip plus interpolation delay (about 100 ms). | T-012 |
| B-007 | S3 | Unity client | Health bars and floating text are UI Toolkit elements repositioned each frame. Performance with 100+ units is unmeasured. | T-019 |
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
| F-019 | Ashfields was not fully symmetric: forests were sampled across the whole map, and neutral camps rolled their creeps at random. | Trees are sampled on one half and point-mirrored; mirrored camps get the same creeps. |
