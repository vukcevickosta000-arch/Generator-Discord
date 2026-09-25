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

## 3. Hero notes

| Hero | Intent | Watch |
|---|---|---|
| Vorak | Frontline initiator that snowballs through fights (feast heal) | Crimson Charge + Bloodfall chain stun length (1.6 s + 1.8 s = 3.4 s combined at max levels); Sovereign's Wrath bash frequency with attack speed items |
| Ilyra | High-risk nuker paying health for damage | Health-cost spells + lifesteal items can loop; Exsanguinate channel break range = 1.5 × cast range + 2 m |

## 4. Items

- Component pricing follows attribute value, roughly 1 attribute point ≈ 75 gold.
- Recipes cost 10–25% of the total.
- Build-ups should read clearly: 3 tiers at most.

## 5. Process

1. Change data.
2. Run `dotnet test`: data validation and simulation tests.
3. Run `SimRunner -- 20 <seed>` with at least 3 seeds and compare against the tables above.
4. Record results here with the date and commit.
