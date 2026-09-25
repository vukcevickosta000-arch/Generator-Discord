#!/usr/bin/env python3
"""Writes Shared/Runtime/Resources/GameDataIndex.txt: the relative path of every game data file.

Unity's Resources API drops folders and extensions from TextAsset names, but the content hash (which the game server
compares during the handshake) is computed over relative paths. The client uses this index to rebuild the exact
paths. Run after adding/renaming data files (the unit test GameDataIndexIsCurrent fails when it is stale)."""
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DATA = os.path.join(ROOT, "Shared", "Runtime", "Resources", "GameData")
OUT = os.path.join(ROOT, "Shared", "Runtime", "Resources", "GameDataIndex.txt")

paths = []
for d, _, files in os.walk(DATA):
    for f in files:
        if f.endswith(".json"):
            paths.append(os.path.relpath(os.path.join(d, f), DATA).replace("\\", "/"))
paths.sort()
names = [os.path.splitext(os.path.basename(p))[0] for p in paths]
dupes = sorted({n for n in names if names.count(n) > 1})
if dupes:
    raise SystemExit("Game data file names must be unique for Unity Resources: " + ", ".join(dupes))
with open(OUT, "w", newline="\n") as fh:
    fh.write("\n".join(paths) + "\n")
print(f"wrote {os.path.relpath(OUT, ROOT)} ({len(paths)} files)")
