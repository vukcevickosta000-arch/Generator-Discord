#!/usr/bin/env python3
"""
Hero generator: turns the compact kits in roster_*.py into game data and Blender model specs.

    python3 Tools/heroes/generate_heroes.py            # every roster hero
    python3 Tools/heroes/generate_heroes.py sereth     # selected heroes (file stems)

Writes
  Shared/Runtime/Resources/GameData/heroes/<stem>.json   (hero, abilities, summon units)
  Blender/scripts/bf_roster_specs.py                     (character specs for build_models.py / render_portraits.py)
then refreshes GameDataIndex.txt. The eight concept heroes (vorak.json ...) are hand-written and never touched.

A kit is `H(...)` in a roster module: identity, lore, an optional stat tweak, a look, and five ability builders from
kitlib. Numbers in descriptions come from the same values the abilities use.
"""
import importlib
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kitlib as K  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
HEROES_DIR = os.path.join(ROOT, "Shared", "Runtime", "Resources", "GameData", "heroes")
SPECS_OUT = os.path.join(ROOT, "Blender", "scripts", "bf_roster_specs.py")
HAND_WRITTEN = {"vorak", "ilyra", "nyxara", "malgrave", "ardyn", "fenrax", "morwen", "thael"}
ROSTERS = ["roster_crimson", "roster_ashen", "roster_wild", "roster_dawn"]

from common import AGI, AL, CC, DG, INT, M, R, STR, WC, H  # noqa: E402,F401

# ------------------------------------------------------------------------------------------------ stat templates
# Derived from the eight hand-tuned heroes; a kit may override single fields.
STATS = {
    (STR, M): dict(baseHp=210, baseHpRegen=1.1, baseMana=75, baseManaRegen=0.3, baseArmor=1.5, damageMin=27, damageMax=33,
                   attackRange=1.6, attackPoint=0.42, attackBackswing=0.5, baseAttackTime=1.7, moveSpeed=3.75, turnRate=14,
                   collisionRadius=0.37, str=24, strGain=3.0, agi=15, agiGain=1.7, int=16, intGain=1.7),
    (STR, R): dict(baseHp=205, baseHpRegen=1.0, baseMana=75, baseManaRegen=0.3, baseArmor=1.0, damageMin=24, damageMax=30,
                   attackRange=5.5, attackPoint=0.45, attackBackswing=0.5, baseAttackTime=1.7, moveSpeed=3.7, turnRate=13,
                   collisionRadius=0.36, str=23, strGain=2.8, agi=16, agiGain=1.8, int=16, intGain=1.7, projectileSpeed=11),
    (AGI, M): dict(baseHp=200, baseHpRegen=0.8, baseMana=75, baseManaRegen=0.25, baseArmor=0.5, damageMin=25, damageMax=31,
                   attackRange=1.6, attackPoint=0.36, attackBackswing=0.48, baseAttackTime=1.65, moveSpeed=3.85, turnRate=15,
                   collisionRadius=0.34, str=19, strGain=2.2, agi=23, agiGain=2.9, int=15, intGain=1.5),
    (AGI, R): dict(baseHp=200, baseHpRegen=0.6, baseMana=75, baseManaRegen=0.25, baseArmor=0.0, damageMin=23, damageMax=29,
                   attackRange=6.3, attackPoint=0.38, attackBackswing=0.5, baseAttackTime=1.65, moveSpeed=3.8, turnRate=15,
                   collisionRadius=0.33, str=17, strGain=1.9, agi=23, agiGain=2.9, int=16, intGain=1.6, projectileSpeed=12.5),
    (INT, M): dict(baseHp=200, baseHpRegen=0.7, baseMana=75, baseManaRegen=0.35, baseArmor=0.5, damageMin=24, damageMax=30,
                   attackRange=1.6, attackPoint=0.42, attackBackswing=0.5, baseAttackTime=1.7, moveSpeed=3.7, turnRate=13,
                   collisionRadius=0.35, str=20, strGain=2.2, agi=14, agiGain=1.4, int=22, intGain=2.8),
    (INT, R): dict(baseHp=200, baseHpRegen=0.5, baseMana=75, baseManaRegen=0.35, baseArmor=0.3, damageMin=21, damageMax=27,
                   attackRange=6.7, attackPoint=0.45, attackBackswing=0.5, baseAttackTime=1.7, moveSpeed=3.6, turnRate=12,
                   collisionRadius=0.33, str=18, strGain=1.9, agi=15, agiGain=1.5, int=24, intGain=3.0, projectileSpeed=11),
}

ITEMS = {
    "carry": {"start": ["item_crimson_draught", "item_rusted_blade", "item_nightrunner_slippers"],
              "early": ["item_marchers_haste", "item_quickblade_gloves"],
              "core": ["item_vampiric_mask", "item_nightfang_greatsword", "item_headsman_cleaver"],
              "late": ["item_voidpiercer", "item_heart_colossus"]},
    "durable": {"start": ["item_crimson_draught", "item_gauntlets_might", "item_gauntlets_might", "item_iron_circlet"],
                "early": ["item_marchers_haste", "item_brutes_belt"],
                "core": ["item_bulwark_plate", "item_shadowstep_talisman", "item_blackened_chainmail"],
                "late": ["item_heart_colossus", "item_grave_ward_amulet"]},
    "support": {"start": ["item_crimson_draught", "item_mana_ember", "item_watcher_eye", "item_iron_circlet"],
                "early": ["item_pilgrim_greaves", "item_wellspring_ring"],
                "core": ["item_crown_of_mercy", "item_grave_ward_amulet", "item_shadowstep_talisman"],
                "late": ["item_stormcaller_rod"]},
    "caster": {"start": ["item_crimson_draught", "item_mana_ember", "item_mantle_lore", "item_mantle_lore"],
               "early": ["item_pilgrim_greaves", "item_sapphire_band"],
               "core": ["item_sanguine_scepter", "item_stormcaller_rod", "item_shadowstep_talisman"],
               "late": ["item_arcane_crystal", "item_heart_colossus"]},
}
ROLE_ITEMS = {"Carry": "carry", "Jungler": "carry", "Assassin": "carry", "Ganker": "carry", "Escape": "carry", "Anti-mage": "carry",
              "Durable": "durable", "Initiator": "durable", "Bruiser": "durable",
              "Support": "support", "Disabler": "support", "Summoner": "caster", "Pusher": "caster", "Nuker": "caster",
              "Carry (caster)": "caster"}
PROFILE = {"Carry": "carry", "Jungler": "carry", "Anti-mage": "carry", "Assassin": "assassin", "Ganker": "assassin",
           "Escape": "assassin", "Durable": "initiator", "Initiator": "initiator", "Support": "support", "Disabler": "support",
           "Nuker": "nuker", "Carry (caster)": "nuker", "Pusher": "pusher", "Summoner": "pusher"}

# ------------------------------------------------------------------------------------------------ looks
PALETTES = {
    CC: [dict(armor=(0.2, 0.15, 0.17), cloth=(0.45, 0.04, 0.08), accent=(0.8, 0.62, 0.32), trim=(0.8, 0.62, 0.32), glow=(1.0, 0.14, 0.16), skin=(0.88, 0.8, 0.8)),
         dict(armor=(0.32, 0.07, 0.1), cloth=(0.18, 0.05, 0.08), accent=(0.88, 0.72, 0.74), trim=(0.88, 0.72, 0.74), glow=(1.0, 0.15, 0.38), skin=(0.93, 0.87, 0.88)),
         dict(armor=(0.12, 0.11, 0.13), cloth=(0.3, 0.02, 0.05), accent=(0.62, 0.1, 0.12), trim=(0.75, 0.55, 0.3), glow=(1.0, 0.2, 0.1), skin=(0.8, 0.72, 0.7)),
         dict(armor=(0.42, 0.4, 0.44), cloth=(0.35, 0.06, 0.12), accent=(0.55, 0.08, 0.14), trim=(0.7, 0.7, 0.76), glow=(0.95, 0.2, 0.3), skin=(0.6, 0.58, 0.6))],
    AL: [dict(armor=(0.3, 0.29, 0.26), cloth=(0.2, 0.22, 0.18), accent=(0.36, 0.55, 0.42), trim=(0.6, 0.58, 0.5), glow=(0.5, 1.0, 0.4), skin=(0.84, 0.8, 0.7)),
         dict(armor=(0.22, 0.2, 0.2), cloth=(0.28, 0.26, 0.2), accent=(0.55, 0.5, 0.25), trim=(0.6, 0.55, 0.3), glow=(0.75, 1.0, 0.25), skin=(0.62, 0.66, 0.52)),
         dict(armor=(0.35, 0.32, 0.3), cloth=(0.16, 0.15, 0.17), accent=(0.8, 0.4, 0.15), trim=(0.55, 0.5, 0.45), glow=(1.0, 0.5, 0.15), skin=(0.55, 0.52, 0.5)),
         dict(armor=(0.4, 0.42, 0.46), cloth=(0.12, 0.14, 0.2), accent=(0.45, 0.6, 0.8), trim=(0.6, 0.65, 0.72), glow=(0.5, 0.8, 1.0), skin=(0.78, 0.8, 0.84))],
    WC: [dict(armor=(0.32, 0.24, 0.17), cloth=(0.22, 0.32, 0.16), accent=(0.5, 0.38, 0.22), trim=(0.3, 0.45, 0.2), glow=(0.55, 1.0, 0.45), skin=(0.72, 0.58, 0.46)),
         dict(armor=(0.3, 0.3, 0.33), cloth=(0.26, 0.28, 0.34), accent=(0.76, 0.8, 0.86), trim=(0.76, 0.8, 0.86), glow=(0.6, 0.85, 1.0), skin=(0.78, 0.66, 0.56)),
         dict(armor=(0.4, 0.2, 0.1), cloth=(0.5, 0.25, 0.1), accent=(0.8, 0.55, 0.2), trim=(0.85, 0.6, 0.25), glow=(1.0, 0.7, 0.3), skin=(0.7, 0.55, 0.44)),
         dict(armor=(0.18, 0.2, 0.16), cloth=(0.12, 0.2, 0.18), accent=(0.45, 0.6, 0.5), trim=(0.4, 0.55, 0.45), glow=(0.4, 1.0, 0.8), skin=(0.66, 0.6, 0.5))],
    DG: [dict(armor=(0.86, 0.82, 0.74), cloth=(0.9, 0.86, 0.72), accent=(0.95, 0.75, 0.35), trim=(0.95, 0.75, 0.35), glow=(1.0, 0.86, 0.45), skin=(0.82, 0.68, 0.56)),
         dict(armor=(0.7, 0.72, 0.76), cloth=(0.2, 0.3, 0.55), accent=(0.9, 0.78, 0.4), trim=(0.9, 0.78, 0.4), glow=(0.7, 0.85, 1.0), skin=(0.78, 0.64, 0.54)),
         dict(armor=(0.62, 0.58, 0.5), cloth=(0.6, 0.12, 0.1), accent=(0.95, 0.8, 0.4), trim=(0.95, 0.8, 0.4), glow=(1.0, 0.75, 0.35), skin=(0.66, 0.5, 0.4)),
         dict(armor=(0.5, 0.46, 0.4), cloth=(0.32, 0.26, 0.2), accent=(0.85, 0.7, 0.4), trim=(0.75, 0.62, 0.36), glow=(1.0, 0.9, 0.6), skin=(0.74, 0.6, 0.5))],
}


def base_look(attr, atk, index):
    """Body archetype from attribute and attack type."""
    if attr == STR and atk == M:
        return dict(height=2.05, bulk=1.3, stance="heavy", helmet=True, cape=index % 2 == 0, weapon=["greatsword", "hammer", "mace", "sword", "club"][index % 5],
                    shield=index % 5 == 3, gauntlets=True)
    if atk == R and attr in (AGI, STR):
        return dict(height=1.9, bulk=1.0 if attr == AGI else 1.2, stance="agile" if attr == AGI else "heavy",
                    weapon="bow" if index % 2 == 0 else "crossbow", hood=index % 3 == 0, cape=True, pauldrons=attr == STR)
    if attr == AGI:
        return dict(height=1.88, bulk=0.95, stance="agile", weapon=["sword", "rapier"][index % 2], cape=index % 2 == 1,
                    hood=index % 3 == 0, pauldrons=index % 3 != 0, greaves=False)
    if atk == M:  # INT melee
        return dict(height=1.95, bulk=1.05, stance="caster", robe=True, collar=True, weapon="sword")
    return dict(height=1.85, bulk=0.9, stance="caster", robe=True, weapon=["staff", "staff", "scythe"][index % 3],
                hood=index % 4 == 0, collar=index % 4 == 1, crown=index % 4 == 2, hat=False)


def build(h, index):
    ctx = K.Ctx(h.stem)
    h.kit(ctx)
    slots = [a["slot"] for a in ctx.abilities]
    assert slots == ["Innate", "Q", "W", "E", "R"], f"{h.stem}: kit slots {slots}"
    hid = f"hero_{h.stem}"
    stats = dict(STATS[(h.attr, h.atk)])
    stats.update(h.stats)
    kind = ROLE_ITEMS.get(h.roles[0], "carry")
    hero = {
        "id": hid, "name": h.name, "title": h.title[:1].upper() + h.title[1:], "faction": h.faction, "primaryAttribute": h.attr, "roles": h.roles,
        "difficulty": h.difficulty, "resource": h.resource, "attackType": h.atk, "lore": h.lore, "personality": h.personality,
        "strengths": h.strengths, "weaknesses": h.weaknesses, "counters": [], "synergies": [],
        "magicResist": 0.25, "visionDay": 22, "visionNight": 10,
    }
    hero.update(stats)
    hero.update({
        "abilities": [a["id"] for a in ctx.abilities],
        "recommendedItems": ITEMS[kind],
        "model": hid, "portrait": f"portrait_{h.stem}", "modelScale": 1.0,
        "botProfile": PROFILE.get(h.roles[0], "carry"),
    })
    out = {"$comment": "Generated by Tools/heroes/generate_heroes.py - edit the roster, not this file.",
           "hero": hero, "abilities": ctx.abilities}
    if ctx.units:
        out["units"] = ctx.units
    return out


# Per-hero variety inside a faction's range: robe/cloth colours, accents and wing membranes.
CLOTH = {
    CC: [(0.45, 0.04, 0.08), (0.3, 0.02, 0.12), (0.12, 0.08, 0.14), (0.55, 0.12, 0.1), (0.22, 0.04, 0.04), (0.4, 0.1, 0.25),
         (0.15, 0.15, 0.18), (0.6, 0.35, 0.3)],
    AL: [(0.2, 0.22, 0.18), (0.28, 0.26, 0.2), (0.16, 0.15, 0.17), (0.12, 0.14, 0.2), (0.3, 0.34, 0.24), (0.35, 0.3, 0.26),
         (0.2, 0.24, 0.28), (0.4, 0.38, 0.33)],
    WC: [(0.22, 0.32, 0.16), (0.26, 0.28, 0.34), (0.5, 0.25, 0.1), (0.12, 0.2, 0.18), (0.35, 0.28, 0.18), (0.18, 0.28, 0.3),
         (0.4, 0.42, 0.2), (0.3, 0.2, 0.28)],
    DG: [(0.9, 0.86, 0.72), (0.2, 0.3, 0.55), (0.6, 0.12, 0.1), (0.32, 0.26, 0.2), (0.85, 0.8, 0.55), (0.25, 0.4, 0.35),
         (0.5, 0.52, 0.6), (0.7, 0.5, 0.25)],
}
ACCENT = [(0.8, 0.62, 0.32), (0.75, 0.75, 0.8), (0.55, 0.4, 0.2), (0.9, 0.8, 0.5), (0.5, 0.2, 0.2), (0.35, 0.5, 0.6)]
WING = {CC: (0.28, 0.04, 0.07), AL: (0.55, 0.52, 0.46), WC: (0.35, 0.28, 0.2), DG: (0.92, 0.9, 0.82)}
CASTER_HEAD = ["hood", "crown", "hat", "collar", "horns", "tiara"]


def stable_hash(s):
    h = 2166136261
    for ch in s:
        h = ((h ^ ord(ch)) * 16777619) & 0xFFFFFFFF
    return h


def look_spec(h, index):
    spec = base_look(h.attr, h.atk, index)
    pal = PALETTES[h.faction][index % len(PALETTES[h.faction])]
    spec.update(pal)
    hv = stable_hash(h.stem)
    spec["cloth"] = CLOTH[h.faction][hv % len(CLOTH[h.faction])]
    spec["cape_color"] = spec["cloth"]
    spec["accent"] = ACCENT[(hv >> 4) % len(ACCENT)]
    spec["trim"] = spec["accent"]
    if spec.get("robe") and not any(k in h.look for k in CASTER_HEAD):
        for k in CASTER_HEAD:
            spec.pop(k, None)
        spec[CASTER_HEAD[(hv >> 8) % len(CASTER_HEAD)]] = True
        if not spec.get("hood"):
            spec.setdefault("hair", [(0.1, 0.08, 0.08), (0.55, 0.5, 0.45), (0.85, 0.82, 0.78), (0.35, 0.2, 0.1)][(hv >> 12) % 4])
            spec.setdefault("long_hair", (hv >> 14) % 2 == 0)
    spec["wing_color"] = WING[h.faction]
    spec["team_band"] = True
    spec.update(h.look)
    return spec


FACTION_NAMES = {CC: "Crimson Court", AL: "Ashen Legion", WC: "Wild Covenant", DG: "Dawnguard"}
SLOT_NAMES = {"Innate": "Innate", "Q": "Q", "W": "W", "E": "E", "R": "R (ultimate)"}


def write_kit_doc(heroes):
    """Docs/HERO_KITS.md: every generated hero's kit, straight from the data (never edit by hand)."""
    out = ["# Generated Hero Kits", "",
           "Generated by `Tools/heroes/generate_heroes.py` from the rosters in `Tools/heroes/roster_*.py`. Do not edit by hand.",
           "The eight concept heroes (Vorak, Ilyra, Nyxara, Malgrave, Ardyn, Fenrax, Morwen, Thael) are hand-written and",
           "described in HERO_ROSTER.md.", "",
           "Every ability below is cast in a real match by `RosterTests.EveryHeroCastsEveryAbility`. Numbers are per level",
           "(4 levels, 3 for ultimates). Balance is first-pass: see BALANCE_NOTES.md.", ""]
    for fac in (CC, AL, WC, DG):
        out += [f"## {FACTION_NAMES[fac]}", ""]
        for h, i in heroes:
            if h.faction != fac:
                continue
            data = build(h, i)
            hero = data["hero"]
            title = f", {h.title}" if h.title else ""
            atk = "melee" if h.atk == M else "ranged"
            out.append(f"### {h.name}{title}")
            out.append(f"{h.attr} · {atk} · {', '.join(h.roles)}")
            out.append("")
            for a in data["abilities"]:
                out.append(f"- **{a['name']}** ({SLOT_NAMES[a['slot']]}): {a['description']}")
            if data.get("units"):
                for u in data["units"]:
                    out.append(f"- *Summon* {u['name']}: {u['maxHp']} health, {u['damageMin']}-{u['damageMax']} damage, {u['attackType'].lower()}.")
            out.append("")
    with open(os.path.join(ROOT, "Docs", "HERO_KITS.md"), "w") as f:
        f.write("\n".join(out))


def main(argv):
    wanted = set(a for a in argv if not a.startswith("-"))
    heroes = []
    for mod in ROSTERS:
        try:
            m = importlib.import_module(mod)
        except ModuleNotFoundError:
            continue
        for i, h in enumerate(m.HEROES):
            heroes.append((h, i))
    specs = {}
    written = 0
    seen = set()
    for h, i in heroes:
        assert h.stem not in HAND_WRITTEN and h.stem not in seen, f"duplicate or hand-written stem {h.stem}"
        seen.add(h.stem)
        specs[f"hero_{h.stem}"] = look_spec(h, i)
        if wanted and h.stem not in wanted:
            continue
        data = build(h, i)
        with open(os.path.join(HEROES_DIR, h.stem + ".json"), "w") as f:
            json.dump(data, f, indent=2)
            f.write("\n")
        written += 1
    with open(SPECS_OUT, "w") as f:
        f.write('"""Generated by Tools/heroes/generate_heroes.py: character specs for the roster heroes (merged into SPECS)."""\n\n')
        f.write("ROSTER_SPECS = {\n")
        for k, v in specs.items():
            f.write(f"    {k!r}: {v!r},\n")
        f.write("}\n")
    write_kit_doc(heroes)
    subprocess.run([sys.executable, os.path.join(ROOT, "Tools", "dev", "gen_gamedata_index.py")], check=True)
    print(f"wrote {written} hero files, {len(specs)} model specs")


if __name__ == "__main__":
    main(sys.argv[1:])
