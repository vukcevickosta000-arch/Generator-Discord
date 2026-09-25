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

## 5. Process

1. Change data.
2. Run `dotnet test`: data validation and simulation tests.
3. Run `SimRunner -- 20 <seed>` with at least 3 seeds and compare against the tables above.
4. Record results here with the date and commit.
