# TODO

Items are listed in priority order. IDs are stable, so they can be referenced from code comments and commits.

## Now

- **T-001 Blender model pipeline.** Write `Blender/scripts/*.py` (run with the `bpy` module or `blender -b -P`) that
  builds the models for every `model` key in the game data:
  - Characters: all eight heroes (`hero_vorak`, `hero_ilyra`, `hero_nyxara`, `hero_malgrave`, `hero_ardyn`,
    `hero_fenrax` plus the `hero_fenrax_moonfang` transform, `hero_morwen`, `hero_thael`), creeps and neutrals.
  - Summons and forms: `summon_skeleton_legionnaire`, `summon_pale_revenant`, `summon_spirit_wolf`,
    `summon_treant`, `summon_cauldron`, `hex_bat`, `hex_toad`.
  - Structures: towers T1–T4 per team, barracks, cores, fountains, wards.
  - Props: every dressing prop type in `Maps/Velmoragh/dressing.json`, and trees.
  - Characters get an armature and the clips `Idle, Run, Attack1, Attack2, Cast1, Cast2, Cast3, CastUlt, Channel,
    Stun, Death`.
  - Put the impact frame at 40% of each attack and cast clip (`ClipAnimator.ImpactFraction`).
  - Add a `projectile_origin` empty and mark team-coloured materials with `team` in the name.
  - Export FBX to `Client/Assets/Resources/Models/`, with props under `Models/Props/<type>.fbx`.
- **T-002 Visual review of stand-ins in Unity**:
  - scale (1 unit = 1 m)
  - facing (+Z forward)
  - the procedural animation timing relative to server attack points
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
- **T-005 Vharoth event (milestone 7)** in `Shared/Runtime/Simulation/Match.Vharoth.cs`:
  - Phase 1 tremors after `vharothMinTime`.
  - 4 seals on `MapDef.VharothSeals`, broken by channeling.
  - Phase 2 awakening: the boss unit in `BossPit` with 3 combat phases.
  - Phase 3 Blood Moon.
  - Reward: the Heart of Vharoth item/buff for the killing team.
  - The corpse persists as a dressing prop.
  - Announcer keys already exist. Add tests.

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

- RTS mode (milestone 8): workers, harvesting, build trees per faction, production queues, research, an RTS map.
- Two-factor authentication (the `TwoFactorEnabled` column exists; there is no flow yet).
- Admin/moderation console for reports, bans and news publishing (the endpoints partially exist).
- Account deletion / data export endpoint (GDPR): delete the account row, anonymise match player rows.
- Localization.
- Cosmetics. The `accounts_cosmetics` table exists, but there is intentionally no store and no fake currency.
- Kubernetes / Agones deployment manifests (see SERVER_DEPLOYMENT.md).
