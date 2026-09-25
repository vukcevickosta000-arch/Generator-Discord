# Testing

## 1. Automated suites

| Suite | Command | What it covers |
|---|---|---|
| Unit and simulation tests (72) | `dotnet test Server/tests/Bloodfall.Tests` | See the breakdown below |
| End-to-end online | `Tools/dev/run-e2e.sh` | Real backend (fresh SQLite database) and a real game server over UDP; see §3 |
| Bot soak | `dotnet run -c Release --project Server/tools/Bloodfall.SimRunner -- <minutes> <seed> [--deaths] [--trace N] [--mirror \| --heroes id1,id2]` | Full 5v5 bot matches (every playable hero by default): stability, performance, balance numbers |
| Unity client compile | `dotnet build Tools/UnityCompileCheck` | Every client script plus Shared, against UnityEngine 2021.3 reference assemblies |
| Unity editor compile | `dotnet build Tools/UnityCompileCheck/Editor` | Editor scripts against Unity3D.SDK 2021.1; APIs newer than 2021.2 are behind `UNITY_2021_2_OR_NEWER` |

What the unit and simulation tests cover:

- **Simulation:** armor math, movement and pathing, attacks and projectiles, denies, towers, creep waves, casting and
  statuses, items and recipes, XP and levels, respawn, fog of war, bots.
- **Protocol:** buffers, tickets, snapshot visibility, host join/reject/reconnect flows.
- **Data:** the index is current, a client-style load gives the same hash, and model and icon keys are declared.
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
- [ ] Settings persist (graphics, audio, key binds) across restarts.
- [ ] Performance: 1080p High ≥ 60 FPS in a 10-bot team fight on a mid-range GPU (record the GPU and FPS in
  PROJECT_STATUS).

## 5. Regressions and bugs

Every fixed bug gets a regression test when it is expressible headlessly. Record it in BUGS.md (Fixed table).
