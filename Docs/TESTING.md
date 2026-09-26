# Testing

## 1. Automated suites

| Suite | Command | What it covers |
|---|---|---|
| Unit and simulation tests (115) | `dotnet test Server/tests/Bloodfall.Tests` | See the breakdown below |
| End-to-end online | `Tools/dev/run-e2e.sh` | Real backend (fresh SQLite database) and a real game server over UDP; see §3 |
| Bot soak | `dotnet run -c Release --project Server/tools/Bloodfall.SimRunner -- <minutes> <seed> [--deaths] [--trace N] [--mirror \| --heroes id1,id2]` | Full 5v5 bot matches (every playable hero by default): stability, performance, balance numbers |
| RTS bot soak | `... SimRunner -- <minutes> <seed> --rts [--games N] [--factions a,b] [--difficulty X,Y] [--trace] [--log]` | 1v1 RTS bot games on Ashfields: wins per faction and per start side, economy and army statistics, AI errors |
| Unity client compile | `dotnet build Tools/UnityCompileCheck` | Every client script plus Shared, against UnityEngine 2021.3 reference assemblies |
| Unity editor compile | `dotnet build Tools/UnityCompileCheck/Editor` | Editor scripts against Unity3D.SDK 2021.1; APIs newer than 2021.2 are behind `UNITY_2021_2_OR_NEWER` |

What the unit and simulation tests cover:

- **Simulation:** armor math, movement and pathing, attacks and projectiles, denies, towers, creep waves, casting and
  statuses, items and recipes, XP and levels, respawn, fog of war, bots.
- **Protocol:** buffers, tickets, snapshot visibility, host join/reject/reconnect flows. RTS tests cover:
  - unit ids in Train/Build commands and the group cap;
  - a loopback RTS match with private economy, building state, training queue, rally point, group moves and fog;
  - research over the wire: an upgrade index in the command, units and research tagged in one queue, and completed
    research in the private block (v6);
  - a hero recruited over the wire: in the altar's queue, then in the private hero block with its ability points and
    learnable abilities, then an ability learned by a `LevelAbility` command (v7).
- **Data:** the index is current, a client-style load gives the same hash, model and icon keys are declared, and
  every model key has an FBX from the Blender pipeline.
- **Hero kits** (`HeroKitTests.cs`): every ability of Nyxara, Malgrave, Ardyn, Fenrax, Morwen and Thael, and the
  engine features they rely on. These include execute thresholds, summons scaling with level, ring walls, curse kill
  credit, echoed casts, forced night, tree counting and non-stacking auras. A 12-minute 10-bot match checks that every
  hero's bot casts at least two different abilities.
- **Vharoth** (`VharothTests.cs`):
  - seal timing and channel interrupts
  - awakening, leash and reset
  - health-gated boss phases
  - the Blood Moon
  - rewards, Heart revive and use
  - the snapshot header
  - bots breaking seals
- **RTS** (`RtsTests.cs`, 20 tests):
  - starting bases and map symmetry
  - mining rate and vein bookkeeping
  - lumber harvesting and falling trees
  - construction pausing without a builder, extra builders, self-building Legion structures
  - rejected placements and costs, re-validation on arrival
  - queue, supply, refunds and rally points
  - cancelling construction, watchtowers firing only when finished
  - elimination and victory, ruins releasing their ground
  - order ownership (no commanding or training with enemy units), veins cannot be attacked
  - one-time neutral camps and their bounty
  - determinism and data validity (every requirement buildable by the worker)
  - a complete Normal-vs-Beginner bot game (economy, production, razing; fewer than 60 rejected bot orders)
  - attack-move after a target dies mid-swing (regression)
- **Couriers** (`CourierTests.cs`, 5 tests):
  - one courier per player at the fountain;
  - bought stash items flown to the hero, then the courier flies home;
  - "nothing to deliver";
  - a killed courier keeps its cargo, pays its bounty, respawns and returns the cargo to the stash;
  - bots have their stash flown to them.
- **Economy:** a last hit pays exactly 32 gold, seen only by its player; passive income is 0.5 gold/s.
- **RTS factions and heroes** (`RtsFactionTests.cs`, 12 tests):
  - research: cost, queue, no double research, effect on existing and new units, melee-only selector
  - the right building, prerequisites (research and buildings), cancel refunds, "not enough" errors
  - Legion raising: supply-free 30 s skeletons, the cooldown, no undead or siege, and the killer's side first in a
    mirror
  - Sun Shrine healing near the shrine only
  - expansion camps attacking intruders, while the centre camp is left alone by passers-by and attack-move but
    fights back when hit
  - Crimson Court Blood Price (paid to the killer's owner, not to other factions)
  - Wild Covenant night: the Moonlit bonus on soldiers, wolf form on shifters (including units trained at night),
    and both ending at dawn
  - a 10-minute Crimson Court vs Wild Covenant bot game: economy, tech, armies, heroes that level, a fight and fewer
    than 40 rejected orders
  - altars:
    - three heroes at rising prices, no duplicates, the cap;
    - an exact refund on cancel, unknown heroes and non-altars rejected;
    - heroes arrive near the altar and `Player.Hero` stays empty.
  - fallen heroes: no gold changes, no respawn on their own, revived at level for 100 + 30 per level, and a revival
    is not a new hero
  - experience from kills near a hero (per supply, buildings), none for a distant hero

## 2. What the compile checks do *not* cover

- Code behind `BLOODFALL_URP` and `BLOODFALL_INPUT_SYSTEM`. Those packages are absent from the reference set.
- HLSL shaders in `Client/Assets/Resources/Shaders`.
- Runtime behaviour in Unity: layout, rendering, input, audio.

These need a Unity 6000.0.40f1 editor. See the manual checklist below and BUGS.md B-001.

## 3. E2E scenario (`Server/tools/Bloodfall.E2E`)

1. **Accounts:**
   - Register two accounts.
   - A duplicate username is rejected (409) and a weak password is rejected (400).
   - A wrong password gets 401; the correct one logs in.
   - Refresh tokens rotate; reusing an old refresh token is rejected (401) and revokes the whole token family.
2. **Custom lobby:**
   - Create; the lobby is visible in the server browser.
   - The second player joins; a non-host cannot start; the host starts.
   - The directory allocates a server and issues tickets.
   - An unconnected UDP ping to the server succeeds.
3. **Game server:**
   - Both clients connect with valid tickets; a forged ticket is rejected.
   - Players land on opposite teams and pick heroes.
   - Snapshots stream; fog hides the enemy hero.
   - The server executes movement and validates purchases.
4. **Results:**
   - A concede ends the match; the right team wins.
   - Profiles, history, match detail and account XP update.
   - A client attempt to submit a result gets 401.
5. **Strategy (RTS) match** on the same game server, once it frees up:
   - An RTS lobby opens on Ashfields; an unknown faction is rejected; both factions show in the lobby.
   - Both clients connect. The private economy streams (500 / 150, supply 5/10), the lobby's factions reach the
     game server, and the player sees their own hall, workers and a full vein.
   - Fog hides the enemy base.
   - A single group order sends all five workers to mine; training is queued and paid.
   - The opponent's Train and Cancel orders on this player's hall are ignored.
   - Mined blood-iron and the vein's decline show in snapshots.
   - Masonry is researched at the hall over the wire: queued, and paid with all the starting lumber.
   - A concede ends the match. The result carries the faction and economy; match history shows the RTS win; no
     empty hero statistics are created.

## 4. Manual checklist (Unity, per release)

- [ ] Fresh clone opens; **Bloodfall ▸ Setup Project** completes; no console errors on Play.
- [ ] Splash → service checks show real results. Kill the backend: the checks fail and offer Retry and Play Offline.
- [ ] Register and log in; restart the client: the session resumes; Log out works.
- [ ] Practice vs bots:
  - hero select → loading → match
  - camera: edge, drag, zoom, Space, Y
  - right-click move/attack, A-click, QWER, Ctrl+Q level-up, items
  - shop (P), scoreboard (Tab), chat (Enter), pause (Esc menu)
  - cheats `-gold 5000`, `-lvlup 5`
- [ ] Online:
  - Custom game create/join in the browser; lobby slots, bots, ready and start; both clients reach the match.
  - Disconnect a client (kill it) and relaunch: the **Reconnect** prompt appears and rejoins the running match.
  - Concede (`-ff` after 15:00, or `--dev-concede-anytime`) → post-game shows rating/XP.
- [ ] War of the Ancients (Strategy page ▸ Practice vs AI, and a 1v1 through the Strategy queue):
  - The camera starts over the hall, which is selected; the resource bar shows 500 / 150 and supply 5/10.
  - Selection: click, drag box, double-click, Shift add/remove, Ctrl+1–9 then 1–9 (double tap centres the camera).
  - Right-click a vein or trees with workers: they harvest, carry cargo and deliver (floating +10).
  - Build menu (B): the ghost turns red on trees, buildings and near veins; placing pays on arrival; Dawnguard
    buildings rise with a builder and Legion buildings on their own; the construction sink animates.
  - Train from the hall and barracks (hotkeys on the card); click a queue slot to cancel; set a rally (Y or
    right-click).
  - A-click and Stop/Hold; enemy buildings stay as faded ghosts once out of sight; "Base under attack" alerts;
    Victory/Defeat banner and post-game list the factions.
- [ ] Settings persist (graphics, audio, key binds) across restarts.
- [ ] Performance: 1080p High ≥ 60 FPS in a 10-bot team fight on a mid-range GPU (record the GPU and FPS in
  PROJECT_STATUS).

## 5. Regressions and bugs

Every fixed bug gets a regression test when it is expressible headlessly. Record it in BUGS.md (Fixed table).
