# TODO

Items are listed in priority order. IDs are stable, so they can be referenced from code comments and commits.

## Now

- **RTS mode (milestone 8).** Phases R1 and R3 are done:
  - R1: the simulation, the Ashfields map, Dawnguard and Ashen Legion.
  - R3: the RTS AI and SimRunner `--rts`.
  - 20 tests (GAME_DESIGN.md §11).

  The remaining phases, in order:
  - **T-030 (R2) Factions and heroes.**
    - Crimson Court: blood economy (buildings or units convert HP into blood-iron).
    - Wild Covenant: shapeshifters stronger at night.
    - Altars that recruit up to three MOBA heroes per player; the heroes level from kills via ShareXp.
    - Research (`OrderType.Research`, which currently answers "Research is not available yet."): weapon and armour
      upgrades per faction.
    - Ashen Legion corpse raising and a Dawnguard healing aura, as the faction mechanics.
    - Neutral camps should attack units that come near them (they currently only retaliate).
  - **T-031 (R3) RTS AI.** *Done (`Shared/Runtime/Simulation/AI/RtsAi.cs`, BALANCE_NOTES.md §5).* Follow-ups:
    - Nightmare needs a real edge over Veteran. Thinking every 0.5 s measured no better than every 1 s, so it
      currently plays like Veteran.
    - Hero use once altars exist (R2).
    - Towers at expansions.
    - Harass and counter-attacks while the enemy army is away.
  - **T-032 (R4) Networking.** *Done:*
    - protocol v5: group orders, RTS entity and private fields, RTS fog;
    - lobby faction pick, the strategy queue with an AI opponent after 45 s, RTS results and history;
    - E2E section 5 plays an RTS match over UDP.

    The Unity client still keeps the strategy queue disabled until T-033.
  - **T-033 (R5) Unity RTS interface.** *Implemented, not yet run in Unity* (`Match/RtsInput.cs`,
    `UI/Hud/RtsHudScreen.cs`):
    - selection, control groups, smart right-click, placement ghost, command card, resource and supply bar;
    - construction sink, work swing, cargo marker and last-seen ghosts;
    - Strategy queue with faction pick, lobby factions, practice vs the RTS AI.

    Verify with the TESTING.md §4 RTS checklist (part of T-002). Still missing:
    - unit icons (the card shows text);
    - a proper construction scaffold;
    - ghosts on the minimap.
  - **T-034 RTS art.** Dedicated worker, building and siege models per faction; the RTS units currently borrow the
    MOBA creep and structure models.
- **T-001 Blender model pipeline.** *Done for every unit and structure key (57 models,
  `Blender/scripts/build_models.py`).* Remaining:
  - Props (`Models/Props/<type>.fbx`, 28 dressing types) and tree variants (MapRenderer still draws C# meshes).
  - Texture maps (normal and mask) once hand-authored art replaces the generated shapes; the rigs and clip names
    are the contract.
- **T-002 Visual review in Unity** of the generated FBX models and the procedural stand-ins:
  - scale (1 unit = 1 m)
  - facing (+Z forward after `bakeAxisConversion`)
  - clip import and looping flags
  - attack and cast impact timing (40% of each clip) against the server's attack points
  - the bf_* material mapping
- **T-003 Icons and portraits.** *Done at placeholder level. Icons are procedural; portraits should become renders of
  the T-001 models.*
  - Generate with `Tools/art/generate_icons.py` into:
    - `Textures/Icons/Abilities/<icon>`
    - `Textures/Icons/Items/<icon>`
    - `Textures/Icons/Statuses/<icon>`
    - `Textures/Icons/Portraits/portrait_<hero>`
  - Filenames must match the `icon`/`portrait` keys in the data.
  - `Bloodfall ▸ Validate Game Data` reports coverage.
- **T-004 Audio.** *Done at placeholder level (`Tools/audio/generate_audio.py`, 132 clips).* Replace with final audio
  under the same keys: SFX, music and announcer clips under `Resources/Audio/{UI,Sfx,Music,Ambience,Announcer,Voice}`.
  The keys used by the code are listed in the tables below; missing clips are logged once.

  | Folder | Keys |
  |---|---|
  | UI | click, hover, tab, error, notify, panel_open, panel_close, match_found, lock_in, buy, shop_open, ping |
  | Sfx | gold, horn, level_up, structure_collapse, core_destroyed |
  | Music | menu_theme, client_theme, hero_select, loading, victory, defeat |
  | Ambience | menu_wind, bats, thunder, velmoragh_night |
  | Announcer | every key in `AnnouncerKeys` |

  In addition, Sfx needs the attack, death and cast clips named in the data.
- **T-005 Vharoth event (milestone 7).** *Done in simulation, tests and bots (GAME_DESIGN.md §8). The Unity
  presentation is implemented but unverified (B-001).* Remaining polish:
  - a Blender model and animations for `boss_vharoth` (currently a scaled stand-in biped)
  - a boss-fight music layer
  - Titan's Tooth and Moon-Blood Vial relics (ITEM_DATABASE.md)

## Soon

- **T-010** Matchmaking E2E: extend `Server/tools/Bloodfall.E2E` with a quick-queue test covering the accept flow and
  bot fill, plus a WebSocket chat round-trip.
- **T-011** Delta-compressed snapshots, sending only changed entity fields against an acked baseline. Full snapshots
  are about 3–6 KB at 30 Hz in a 5v5.
- **T-012** Client-side prediction for the local hero's movement (server reconciliation). Online play is currently
  purely interpolated, so there is about 100 ms of added latency on your own hero.
- **T-013** Replays: record the order stream plus seed from `MatchHost` and play it back through the same simulation.
- **T-014** Hero roster expansion toward 96 (8 playable). All eight concept heroes are done. Next, per faction, are the
  ⚪ planned heroes in Docs/HERO_ROSTER.md. The data format and the kit tests in `HeroKitTests.cs` are the template.
  Engine gaps that would unlock planned kits:
  - arc/line walls (the engine only builds ring walls)
  - corpses from heroes
  - "on shield break" triggers
  - targeting trees (treants from trees, teleport to trees)
- **T-015** Item catalogue expansion toward 150–200 (currently 38). The ITEM_DATABASE.md plan lists planned items by
  category.
- **T-016** Bot last-hitting. Bots currently average about 25 last hits per 20 minutes. Add creep-HP prediction using
  the attack point and projectile travel time.
- **T-022** Hero-specific bot behaviour. Bot ability use is tag-driven (`botUsage`), which is enough for nukes,
  disables and buffs but not for setup plays:
  - Nyxara: stalk from the veil, then Velvet Dark and Midnight Sentence on a marked target
  - Thael: Grove Call to reach fights
  - Ardyn: Aegis on the ally being focused
  - Malgrave: cage a fleeing target
- **T-017** Email delivery for verification and reset: an SMTP or provider adapter behind an `IEmailSender`
  interface. Development keeps logging.
- **T-018** Illusions and resurrection effect types, which are currently no-ops with a comment.
- **T-019** Unity: pooled health-bar meshes if UI Toolkit bar count becomes a bottleneck (profile first).
- **T-021** Side shops: purchases within side-shop range go to the inventory. Flag side-shop and secret-shop stock
  in the item data.
- **T-020** SSAO renderer feature via ProjectSetup once verified against URP 17 APIs (the setting exists but is not
  wired).

## Later

- Two-factor authentication (the `TwoFactorEnabled` column exists; there is no flow yet).
- Admin/moderation console for reports, bans and news publishing (the endpoints partially exist).
- Account deletion / data export endpoint (GDPR): delete the account row, anonymise match player rows.
- Localization.
- Cosmetics. The `accounts_cosmetics` table exists, but there is intentionally no store and no fake currency.
- Kubernetes / Agones deployment manifests (see SERVER_DEPLOYMENT.md).
