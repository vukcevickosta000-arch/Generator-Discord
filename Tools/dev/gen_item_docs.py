#!/usr/bin/env python3
"""Regenerates the 'Implemented items' section of Docs/ITEM_DATABASE.md from the game data, so the document can
never drift from what the game actually contains. Run after editing items/*.json."""
import json
import os
import re

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DATA = os.path.join(ROOT, "Shared", "Runtime", "Resources", "GameData", "items")
DOC = os.path.join(ROOT, "Docs", "ITEM_DATABASE.md")
PCT = {"Lifesteal", "SpellVamp", "CritChance", "MagicResist", "SpellAmp", "HpRegenPct", "Evasion", "MoveSpeedPct", "StatusResist",
       "CooldownReduction", "BonusDamagePct", "ManaRegenPct", "HealAmp"}


def load():
    items = []
    for f in sorted(os.listdir(DATA)):
        if f.endswith(".json"):
            t = re.sub(r"(?m)^\s*//.*$", "", open(os.path.join(DATA, f), encoding="utf-8").read())
            items += json.loads(t).get("items", [])
    return {i["id"]: i for i in items}


def total(items, i, seen=()):
    return i.get("cost", 0) + sum(total(items, items[c]) for c in i.get("components") or [] if c in items)


def mods(i):
    out = []
    for m in i.get("modifiers") or []:
        v = m["value"]
        if isinstance(v, list):
            v = "/".join(str(x) for x in v)
            out.append(f"+{v} {m['stat']}")
        elif m["stat"] in PCT:
            out.append(f"+{v * 100:g}% {m['stat']}")
        else:
            out.append(f"+{v:g} {m['stat']}")
    return ", ".join(out)


def main():
    items = load()
    rows = []
    for i in sorted(items.values(), key=lambda x: (x.get("category", ""), total(items, x))):
        comps = " + ".join(items[c]["name"] for c in i.get("components") or [] if c in items)
        recipe = f"{comps} + {i.get('cost', 0)}" if comps else ""
        extra = []
        if i.get("active"):
            extra.append("Active: " + (i["active"].get("name") or "yes"))
        if i.get("triggers") or i.get("aura"):
            extra.append("Passive")
        if i.get("shop") == "Secret":
            extra.append("Secret shop")
        rows.append(f"| {i['name']} | {i.get('category', '')} | {total(items, i)} | {recipe} | {mods(i)} | {'; '.join(extra)} |")
    section = ("<!-- GENERATED:BEGIN (Tools/dev/gen_item_docs.py) -->\n"
               f"**{len(items)} items implemented.**\n\n"
               "| Item | Category | Total cost | Recipe | Stats | Notes |\n|---|---|---|---|---|---|\n" + "\n".join(rows) +
               "\n<!-- GENERATED:END -->")
    doc = open(DOC, encoding="utf-8").read()
    doc = re.sub(r"<!-- GENERATED:BEGIN.*?<!-- GENERATED:END -->", lambda _: section, doc, flags=re.S)
    open(DOC, "w", encoding="utf-8", newline="\n").write(doc)
    print(f"updated {os.path.relpath(DOC, ROOT)} ({len(items)} items)")


if __name__ == "__main__":
    main()
