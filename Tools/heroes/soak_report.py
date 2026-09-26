#!/usr/bin/env python3
"""
Per-hero statistics from SimRunner --random game logs.

    for s in $(seq 1001 1100); do SimRunner 60 $s --random > logs/g_$s.txt; done
    python3 Tools/heroes/soak_report.py logs/

Prints games, win rate, kills/deaths/assists per game and last hits per hero, sorted by win rate, plus faction
totals. Win rates from a few games per hero are noisy: read them as outlier detection, not as balance truth.
"""
import collections
import glob
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
HEROES = os.path.join(ROOT, "Shared", "Runtime", "Resources", "GameData", "heroes")
LINE = re.compile(r"^(Dawn|Dusk)\d\s+(hero_\w+)\s+L\s*(\d+)\s+K/D/A\s+(\d+)/\s*(\d+)/\s*(\d+)\s+LH\s+(\d+)")


def main(folder):
    faction = {}
    for f in glob.glob(os.path.join(HEROES, "*.json")):
        h = json.load(open(f))["hero"]
        faction[h["id"]] = h["faction"]
    st = collections.defaultdict(lambda: dict(g=0, w=0, k=0, d=0, a=0, lh=0))
    games = decided = 0
    for f in sorted(glob.glob(os.path.join(folder, "*.txt"))):
        text = open(f).read()
        m = re.search(r"winner (\w+)", text)
        if not m:
            continue
        games += 1
        winner = m.group(1)
        if winner == "None":
            continue
        decided += 1
        for line in text.splitlines():
            p = LINE.match(line)
            if not p:
                continue
            team, hero = p.group(1), p.group(2)
            s = st[hero]
            s["g"] += 1
            s["w"] += team == winner
            s["k"] += int(p.group(4)); s["d"] += int(p.group(5)); s["a"] += int(p.group(6)); s["lh"] += int(p.group(7))
    print(f"{games} games, {decided} decided, {len(st)} heroes seen")
    rows = sorted(st.items(), key=lambda kv: (-kv[1]["w"] / kv[1]["g"], -kv[1]["g"]))
    print(f"{'hero':<22}{'games':>6}{'win%':>7}{'K':>6}{'D':>6}{'A':>6}{'LH':>6}")
    for h, s in rows:
        g = s["g"]
        print(f"{h:<22}{g:>6}{100 * s['w'] / g:>6.0f}%{s['k'] / g:>6.1f}{s['d'] / g:>6.1f}{s['a'] / g:>6.1f}{s['lh'] / g:>6.0f}")
    fac = collections.defaultdict(lambda: [0, 0])
    for h, s in st.items():
        fac[faction.get(h, "?")][0] += s["w"]
        fac[faction.get(h, "?")][1] += s["g"]
    print()
    for f, (w, g) in sorted(fac.items()):
        print(f"{f:<14} {w}/{g} = {100 * w / g:.0f}%")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
