# Balance Notes

All numbers live in game data. Economy numbers are in `rules.json`; heroes, items and units in their own files.
These notes record intent and measured results.

## 1. Economy targets (classic MOBA pacing)

| Metric (20 min, competent players) | Target | Notes |
|---|---|---|
| Carry GPM | 450–600 | Last hits are the main source |
| Support GPM | 250–350 | Passive income 1.5/s = 90 GPM baseline |
| Carry level at 20:00 | 16–18 | |
| Kills per team at 20:00 | 15–30 | |
| Match length | 30–45 min | Vharoth becomes available at 25:00 |

**Creep bounties:**

| Creep | Gold | XP |
|---|---|---|
| Melee | 36–46 | 57 |
| Ranged | 41–49 | 69 |
| Siege | 66–80 | 88 |

Creeps upgrade every 7.5 min (+HP, +damage, +gold).

## 2. Measured: bot simulation (SimRunner, 20 min, seed 11, 5 Vorak/Ilyra bots per side, 2026-09-25)

| Metric | Value |
|---|---|
| Kills | Dawn 13 – Dusk 20 (33 total) |
| Towers destroyed | Dawn lost 4, Dusk lost 2 |
| Winner at 20:00 | none (match still running) |
| Hero levels | 6–11 |
| GPM | 150–259 |
| Last hits per hero | 8–45 (avg ≈ 25) |
| Denies per hero | 6–36 |
| Hero deaths by source | heroes 28, creeps 5, towers 0 |
| Performance | 0.21 ms per simulation tick (Release) |

**Reading:**

- **Bots under-farm:** about 25 last hits against a target of 80+. Their GPM is support-level, and levels trail
  targets by about 6. Fix the bot last-hit timing (T-016) before tuning hero numbers against bot data.
- **Denies are high relative to last hits.** Bots deny eagerly because deny checks are cheaper than last-hit
  prediction. Not a rules problem.
- **Towers no longer kill bots.** The tower-threat avoidance works; before that fix, bots dove towers.
- **Kill pace is in range** (33 at 20:00).

### Measured: all eight heroes (SimRunner, 20 min, seed 11, default roster rotation, 2026-09-25)

| Metric | Value |
|---|---|
| Kills | Dawn 15 – Dusk 17 (32 total) |
| Towers destroyed | Dawn 5, Dusk 4 |
| Winner at 20:00 | none |
| Hero levels | 8–10 |
| GPM | 156–247 |
| Last hits per hero | 7–41 |
| Hero deaths by source | heroes 22, creeps 8, towers 2 |
| Performance | 0.28 ms per simulation tick (Release) |

**Reading:**

- **Summoners farm best:** Malgrave had 41 last hits and the highest GPM. His legionnaires and Grave Chill clear waves,
  so the bot's weak last-hit timing matters less for him.
- **The Nyxara bot is the weakest** (7 last hits, 6 deaths). Bots use her Kiss and her blink as an engage, but they do
  not stalk from the veil to set up Velvet Dark and Midnight Sentence. This is bot behaviour (T-022), not her numbers.
- **Kill pace is unchanged** (32 vs 33). The new crowd control did not produce stun-lock deaths in bot play.

### Measured: Vharoth (SimRunner, 45 min, seeds 11/12/13, all heroes, 2026-09-25)

| Seed | Seals broken by | Awakened | Blood Moon | Slain | By |
|---|---|---|---|---|---|
| 11 | Dusk (4/4, 25:18–25:39) | 25.6 min | 28.7 min | 33.3 min | Dawn |
| 12 | — | 25.9 min | 29.0 min | 32.3 min | Dawn |
| 13 | — | 26.0 min | 29.1 min | 33.3 min | Dusk |

**Tuning history:**

- The first version (16 000 HP, 12 armor, Wrath pulse every 3 s for 160, reset heal 8%/s) was never killed by bots.
  They reached 25–30% and were then driven off by Titan's Wrath while he healed back.
- The current version (14 000 / 10 armor / 4 s / 140 / 4%/s) dies to a coordinated bot team 7–8 minutes after
  awakening, always after the Blood Moon rises.
- Human teams farm about twice the bot GPM, so expect faster kills. Revisit once there is player data.

## 3. Hero notes

| Hero | Intent | Watch |
|---|---|---|
| Vorak | Frontline initiator that snowballs through fights (feast heal) | Crimson Charge + Bloodfall chain stun length (1.6 s + 1.8 s = 3.4 s combined at max levels); Sovereign's Wrath bash frequency with attack speed items |
| Ilyra | High-risk nuker paying health for damage | Health-cost spells + lifesteal items can loop; Exsanguinate channel break range = 1.5 × cast range + 2 m |
| Nyxara | Assassin: marks a target, waits unseen, then executes | Execute thresholds 22/28/34% after 150–350 pure damage; the Crimson Mark heal (12–30 per enemy-hero attack) scales with how many allies focus the target |
| Malgrave | Summoner and pusher; wins through board presence | Summons are worth 14–32 gold each, so trading them feeds the enemy. The Bone Cage can trap allies too. The shard bonus only helps instant spells (it is consumed as the cast resolves) |
| Ardyn | Bodyguard with light initiation | Two Ardyns do not stack Oathkeeper. The Crusade's 60% status resistance roughly halves the enemy's counter-stuns |
| Fenrax | Night carry | The forced night (8 s) also helps enemy night heroes and cuts everyone's vision. Moonfang +50% HP at level 3 with Heart of the Colossus is the tankiest carry state in the game |
| Morwen | Disabler and support | Echoes double her burst during the ultimate: Crooked Curse deals about 1.6× (the echo lands after the curse's -15% magic resistance). A second cauldron does not stack its auras (same source definition) |
| Thael | Forest initiator | Barkskin is binary (3+ trees within 5 m). Grove Call is a 60 m teleport on a 25 s cooldown at level 4: watch map pressure |

## 4. Items

- Component pricing follows attribute value, roughly 1 attribute point ≈ 75 gold.
- Recipes cost 10–25% of the total.
- Build-ups should read clearly: 3 tiers at most.

## 5. RTS economy (phase R1 numbers, 2026-09-25; R2 changes at the end of this section)

These are design targets checked by `RtsTests`.

- **Start.** 500 blood-iron and 150 lumber, a hall (10 supply) and 5 workers (75 blood-iron, 1 supply, 14 s each).
- **Mining.**
  - A trip carries 10 blood-iron, with 1 s inside the vein. The hall is 11.5 m from its main vein (about 7 m edge to
    edge, roughly 4 s of walking), so one worker earns about 1.9/s.
  - One worker at a time per vein caps a vein at 10/s. The test measures 250–600 in 60 s with five workers.
  - A main vein lasts about 21 minutes at saturation.
- **Lumber.** A trip carries 10 lumber after 6 s of chopping, so one worker earns about 0.8/s at the base forest.
  A tree holds 50 lumber (5 trips).
- **Supply.** Supply structures cost 80/20, take 30 s and give +8. The cap is 100. Soldiers cost 2 supply, siege 3,
  elites 5.
- **First army.**
  - A barracks costs 160/60 and takes 55 s.
  - Footman 135/0, 20 s; Arbalist 135/20, 22 s.
  - Legion equivalents cost 5–10 less and train 1–2 s faster, with about 10% less HP.
- **Towers.** 110/80, 45 s, 22–26 damage per second at 7 m. Enough to stop a lone worker harass, not an army.

### Measured: RTS bots on Ashfields (SimRunner `--rts`, 30-minute cap, 2026-09-25)

| Series | Result |
|---|---|
| Dawnguard vs Ashen Legion, Normal, 24 games (seeds 500–523), sides alternating | 12–12. Start sides also 12–12. Decided games last 23.7 min on average. |
| Same, seeds 900–923 | Dawnguard 15–9. Start sides Dawn 17 – Dusk 7, but see the next row. |
| Mirror games, 80 in total (Dawnguard seeds 1000+, Legion 2000+) | Start sides Dawn 36 – Dusk 40 (4 draws): no side advantage. |
| Normal vs Beginner, 10 games per faction | Normal 10–0 with both factions |
| Veteran vs Normal, 10 games per faction | Veteran 7–2 (Dawnguard), 6–3 (Legion) |
| Nightmare vs Veteran | 2–7 and 5–4: no better (TODO T-031) |
| Performance | 0.06–0.08 ms per tick with about 150 units |

History of the fixes behind these numbers:

- Legion line units were buffed after the first series (Dawnguard 12–3). Thrall 400 HP for 120, Bone Archer 290 HP
  for 125, Catapult 580 HP for 170, Crypt Horror 1000 HP for 260.
- Map symmetry fixes (BUGS.md F-018, F-019) removed a start-side advantage of up to 22–2 in mirrors.

### R2 part 1 (research, faction mechanics, guarding camps), 2026-09-25

Data changes:
- **Line units.** Both factions pay the same: 135 for the melee and ranged soldiers, 180/60 for siege, 280/80 for
  elites. Speeds are equal by role: soldiers 3.6, elites and workers 3.4, siege 2.6.
  - In mirror-AI games, fights react steeply to small edges. Arrival order decided the first clash, and the first
    clash decided the game.
- **Dawnguard build times.** Shortened to offset the squires who stay to build: Citadel 85 s, Sun Shrine 22,
  Barracks 42, Watchtower 35, Workshop 45, Sanctum 60.
- **Research.** See GAME_DESIGN §11.1. The bot researches from minute 9 once it has over 450 blood-iron, army
  upgrades first.
- **Legion raising.** One skeleton per 25 s, lasting 30 s. With a 30 s cooldown the Legion won about 40% of games.

Bot and simulation fixes found by measuring:

| Symptom | Cause | Fix |
|---|---|---|
| Legion mirror won by the Dawn start 32–6 | The raise went to the first player in the list | Killer's side first, then the closest soldier |
| After camps started guarding, the first army to the centre lost | It fought the centre camp, then the enemy | Centre camp does not guard; attack-move ignores passive camps; the bot walks round guarding camps |
| About 20% of games drawn at 30 min | Armies stuck on unreachable scout spots or straight-line staging against cliffs | Staging along the nav path; scouting moves on after 30 s without progress (fighting excluded) |
| Big armies crawled 4 m every 3 s | "Strung out" measured by the farthest unit | 80th percentile against a threshold that grows with army size |
| The faster bot's first wave died under the enemy's tower | It walked straight into the base | Waves gather and hold at the midpoint for 20 s first |

Measured after these changes (SimRunner `--rts`, 40 games per row, 30-minute cap):

| Series | Result |
|---|---|
| Dawnguard vs Ashen Legion, seeds 5000+ and 900+ | Dawnguard 23–16 and 21–18: 44–34 in total (56%) |
| Dawnguard mirror, seeds 3000+ | Start sides 22–18, no draws |
| Legion mirror, seeds 2000+ | Start sides 18–22, no draws |
| Normal vs Beginner, 20 games | 20–0 |
| Veteran vs Normal, 20 games per faction pairing | 12–7 and 11–8 (the edge is smaller than R3's 7–2 and 6–3) |

## 6. Process

1. Change data.
2. Run `dotnet test`: data validation and simulation tests.
3. Run `SimRunner -- 20 <seed>` with at least 3 seeds and compare against the tables above. For the RTS, run
   `SimRunner -- 30 <seed> --rts --games 24` (plus `--factions a,b` and `--difficulty X,Y`).
4. Record results here with the date and commit.
