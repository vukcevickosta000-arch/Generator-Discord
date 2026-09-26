"""
Ability archetypes for the hero generator (Tools/heroes/generate_heroes.py).

Every function builds one ability in the engine's data format (Shared/Runtime/Data/Definitions.cs) and registers any
statuses or summon units it needs on the hero context. Descriptions are written from the same numbers the ability
uses, so the tooltip always matches the data. Basic abilities have 4 levels, ultimates 3.
"""
import re

SLOT_ANIM = {"Q": "Cast1", "W": "Cast2", "E": "Cast3", "R": "CastUlt"}


def snake(s):
    return re.sub(r"[^a-z0-9]+", "_", s.lower().replace("'", "")).strip("_")


def L4(a, step):
    return [round(a + step * i, 3) for i in range(4)]


def L3(a, step):
    return [round(a + step * i, 3) for i in range(3)]


def fmt(v, pct=False, suffix=""):
    """[80, 150, 220, 290] -> '80/150/220/290'; a scalar -> '80'."""
    def one(x):
        if pct:
            x = x * 100
        s = f"{x:.2f}".rstrip("0").rstrip(".")
        return s + ("%" if pct else "")
    if isinstance(v, (list, tuple)):
        if all(x == v[0] for x in v):
            return one(v[0]) + suffix
        return "/".join(one(x) for x in v) + suffix
    return one(v) + suffix


class Ctx:
    """Collects the abilities, statuses and units of one hero while its kit is built."""

    def __init__(self, hero_id):
        self.hero = hero_id              # e.g. "sereth"
        self.statuses = {}               # ability id -> [status dicts]
        self.units = []
        self.abilities = []

    def aid(self, name):
        return f"{self.hero}_{snake(name)}"

    def status(self, ability_id, st):
        self.statuses.setdefault(ability_id, []).append(st)
        return st["id"]


# ------------------------------------------------------------------------------------------------ plumbing

def _base(ctx, slot, name, desc, targeting, **kw):
    ult = slot == "R"
    a = {
        "id": ctx.aid(name),
        "name": name,
        "slot": slot,
        "targeting": targeting,
        "description": desc,
    }
    if ult:
        a["isUltimate"] = True
        a["maxLevel"] = 3
    if slot == "Innate":
        a["maxLevel"] = 1
    a.update(kw)
    a["icon"] = f"ability_{ctx.hero}_{snake(name).split('_')[-1]}"
    if slot in SLOT_ANIM and targeting != "Passive":
        a.setdefault("castAnimation", SLOT_ANIM[slot])
    return a


def _levels(slot):
    return 3 if slot == "R" else 4


def _cd(slot, basic, ult=None):
    """Cooldowns: basic tuple (start, step) for 4 levels or ult tuple for 3."""
    if slot == "R":
        a, s = ult or (100, -15)
        return L3(a, s)
    a, s = basic
    return L4(a, s)


def _mana(slot, basic, ult=None):
    if slot == "R":
        a, s = ult or (150, 50)
        return L3(a, s)
    a, s = basic
    return L4(a, s)


def _lv(slot, basic, ult):
    """Scales a (start, step) pair to the slot's level count."""
    return L3(*ult) if slot == "R" else L4(*basic)


def _finish(ctx, a):
    sts = ctx.statuses.pop(a["id"], None)
    if sts:
        a["statuses"] = sts
    ctx.abilities.append(a)
    return a


def _status_effect(status, duration, target=None):
    e = {"type": "ApplyStatus", "status": status, "duration": duration}
    if target:
        e["target"] = target
    return e


DISABLE_WORDS = {"stun": "stuns", "root": "roots", "silence": "silences", "hex": "hexes", "fear": "fears",
                 "slow_light": "slows by 20%", "slow_heavy": "slows by 40%", "disarm": "disarms", "ministun": "briefly stuns"}


PARTICIPLE = {"stun": "stunned", "root": "rooted", "silence": "silenced", "hex": "hexed", "fear": "feared",
              "slow_light": "slowed by 20%", "slow_heavy": "slowed by 40%", "disarm": "disarmed", "ministun": "briefly stunned"}


def _disable_text(status, dur):
    if not status:
        return ""
    return f" and {DISABLE_WORDS.get(status, 'afflicts')} them for {fmt(dur)} s"


# ================================================================================================ innates

def in_mods(ctx, name, desc, mods):
    """Plain passive stat bonuses. mods: [(stat, value)]."""
    return _finish(ctx, _base(ctx, "Innate", name, desc, "Passive",
                              passiveModifiers=[{"stat": s, "value": v} for s, v in mods]))


def in_crit(ctx, name, chance, mult, flavor=""):
    desc = f"{flavor}{int(chance * 100)}% chance for an attack to critically strike for {int(mult * 100)}% damage."
    return in_mods(ctx, name, desc, [("CritChance", chance), ("CritMultiplier", mult)])


def in_lifesteal(ctx, name, pct, flavor=""):
    return in_mods(ctx, name, f"{flavor}Attacks heal for {int(pct * 100)}% of the damage dealt.", [("Lifesteal", pct)])


def in_evasion(ctx, name, pct, flavor=""):
    return in_mods(ctx, name, f"{flavor}{int(pct * 100)}% chance to evade attacks.", [("Evasion", pct)])


def in_bash(ctx, name, every, dmg, stun=0.3, vfx="wrath_bash", flavor=""):
    desc = f"{flavor}Every {every}th attack on the same target deals {dmg} bonus physical damage and stuns for {stun} s."
    a = _base(ctx, "Innate", name, desc, "Passive", triggers=[{
        "on": "AttackLanded", "everyN": every, "sameTarget": True, "types": "Units", "team": "Enemy",
        "effects": [{"type": "Damage", "amount": dmg, "damageType": "Physical", "vfx": vfx},
                    _status_effect("ministun" if stun <= 0.3 else "stun", stun)]}])
    return _finish(ctx, a)


def in_feast(ctx, name, basic_pct, hero_pct, radius=7, vfx="feast_heal", flavor=""):
    desc = (f"{flavor}Restores {fmt(basic_pct, True)} of maximum health when a unit dies within {radius} m and "
            f"{fmt(hero_pct, True)} when a hero falls.")
    a = _base(ctx, "Innate", name, desc, "Passive", triggers=[
        {"on": "NearbyDeath", "types": "Basic", "team": "Any", "radius": radius,
         "effects": [{"type": "Heal", "target": "Caster", "pctMaxHp": basic_pct}]},
        {"on": "NearbyDeath", "types": "Hero", "team": "Any", "radius": radius,
         "effects": [{"type": "Heal", "target": "Caster", "pctMaxHp": hero_pct, "vfx": vfx}]}])
    return _finish(ctx, a)


def in_spell_stacks(ctx, name, amp, dur, stacks, vfx="blood_surge", flavor=""):
    aid = ctx.aid(name)
    sid = ctx.status(aid, {"id": aid + "_stack", "name": name, "isDebuff": False, "dispel": "Basic",
                           "stacking": "Intensity", "maxStacks": stacks,
                           "modifiers": [{"stat": "SpellAmp", "value": amp, "perStack": True}],
                           "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}", "vfx": vfx})
    desc = f"{flavor}Each spell cast grants +{fmt(amp, True)} spell amplification for {dur} s, stacking {stacks} times."
    a = _base(ctx, "Innate", name, desc, "Passive",
              triggers=[{"on": "AbilityCast", "team": "Any", "effects": [_status_effect(sid, dur, "Caster")]}])
    return _finish(ctx, a)


def in_kill_stacks(ctx, name, stat, per, stacks, dur=90, vfx="bloodlust", flavor="", pct=False, unit=""):
    aid = ctx.aid(name)
    sid = ctx.status(aid, {"id": aid + "_stack", "name": name, "isDebuff": False, "dispel": "None",
                           "stacking": "Intensity", "maxStacks": stacks, "removeOnDeath": False,
                           "modifiers": [{"stat": stat, "value": per, "perStack": True}],
                           "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}", "vfx": vfx})
    shown = fmt(per, pct) + unit
    desc = (f"{flavor}Each kill grants +{shown} {STAT_WORDS.get(stat, stat)} for {dur} s, stacking {stacks} times "
            f"(hero kills count as three).")
    a = _base(ctx, "Innate", name, desc, "Passive", triggers=[
        {"on": "Kill", "types": "Basic", "team": "Enemy", "effects": [_status_effect(sid, dur, "Caster")]},
        {"on": "Kill", "types": "Hero", "team": "Enemy",
         "effects": [dict(_status_effect(sid, dur, "Caster"), stacks=3)]}])
    return _finish(ctx, a)


STAT_WORDS = {"BonusDamage": "attack damage", "AttackSpeed": "attack speed", "Armor": "armor", "MoveSpeedPct": "movement speed",
              "SpellAmp": "spell amplification", "HpRegen": "health regeneration", "MaxHp": "maximum health",
              "Lifesteal": "lifesteal", "MagicResist": "magic resistance", "BonusDamagePct": "attack damage",
              "Evasion": "evasion", "CritChance": "critical chance", "StatusResist": "status resistance",
              "ManaRegen": "mana regeneration", "DamageTakenPct": "damage taken", "OutgoingDamagePct": "damage dealt",
              "AttackRange": "attack range", "Str": "strength", "Agi": "agility", "Int": "intelligence",
              "CooldownReduction": "cooldown reduction", "HealAmp": "healing received", "SlowResist": "slow resistance"}

PCT_STATS = {"MoveSpeedPct", "SpellAmp", "Lifesteal", "MagicResist", "BonusDamagePct", "Evasion", "CritChance",
             "StatusResist", "DamageTakenPct", "OutgoingDamagePct", "CooldownReduction", "HealAmp", "SlowResist",
             "MaxHpPct", "HpRegenPct", "ManaRegenPct", "SpellVamp"}


def mods_text(mods):
    parts = []
    for s, v in mods:
        if s == "__blind":
            parts.append("half of their attacks miss")
            continue
        vv = v if not isinstance(v, (list, tuple)) else v
        neg = (vv[0] if isinstance(vv, list) else vv) < 0
        shown = fmt([abs(x) for x in vv] if isinstance(vv, list) else abs(vv), s in PCT_STATS)
        word = STAT_WORDS.get(s, s)
        parts.append(f"{'-' if neg else '+'}{shown} {word}")
    return ", ".join(parts)


def in_aura(ctx, name, mods, radius=9, team="AllyOrSelf", heroes_only=False, flavor="", vfx=None):
    aid = ctx.aid(name)
    st = {"id": aid + "_aura", "name": name, "isDebuff": team == "Enemy", "dispel": "None",
          "modifiers": [{"stat": s, "value": v} for s, v in mods],
          "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}"}
    if vfx:
        st["vfx"] = vfx
    sid = ctx.status(aid, st)
    who = "enemies" if team == "Enemy" else "allied heroes" if heroes_only else "allies"
    desc = f"{flavor}{who.capitalize()} within {radius} m have {mods_text(mods)}."
    a = _base(ctx, "Innate", name, desc, "Passive",
              aura={"radius": radius, "team": team, "types": "Hero" if heroes_only else "Units", "status": sid,
                    "heroesOnly": heroes_only})
    return _finish(ctx, a)


def in_on_hit(ctx, name, effects_desc, effects, chance=1.0, every=0, statuses=(), flavor=""):
    """Generic attack trigger: effects applied to the attacked unit."""
    aid = ctx.aid(name)
    for st in statuses:
        ctx.status(aid, st)
    trig = {"on": "AttackLanded", "types": "Units", "team": "Enemy", "effects": effects}
    if chance < 1:
        trig["chance"] = chance
    if every:
        trig["everyN"] = every
    return _finish(ctx, _base(ctx, "Innate", name, flavor + effects_desc, "Passive", triggers=[trig]))


def in_attack_slow(ctx, name, slow, dur, flavor=""):
    aid = ctx.aid(name)
    st = {"id": aid + "_slow", "name": name, "isDebuff": True, "dispel": "Basic",
          "modifiers": [{"stat": "MoveSpeedPct", "value": -slow}], "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}"}
    return in_on_hit(ctx, name, f"Attacks slow the target by {int(slow * 100)}% for {dur} s.",
                     [_status_effect(st["id"], dur)], statuses=[st], flavor=flavor)


def in_mana_burn(ctx, name, burn, flavor=""):
    return in_on_hit(ctx, name, f"Attacks burn {burn} mana and deal as much physical damage.",
                     [{"type": "ReduceMana", "amount": burn}, {"type": "Damage", "amount": burn, "damageType": "Physical", "vfx": "hit_magic"}],
                     flavor=flavor)


def in_thorns(ctx, name, dmg, flavor=""):
    desc = f"{flavor}Melee attackers take {dmg} magical damage each time they strike."
    a = _base(ctx, "Innate", name, desc, "Passive", triggers=[{
        "on": "Attacked", "meleeOnly": True, "types": "Units", "team": "Enemy",
        "effects": [{"type": "Damage", "target": "TriggerSource", "amount": dmg, "damageType": "Magical", "noReflect": True, "vfx": "thorn_strike"}]}])
    return _finish(ctx, a)


def in_cleave(ctx, name, dmg, radius=2.5, flavor=""):
    desc = f"{flavor}Attacks also deal {dmg} physical damage to other enemies within {radius} m of the target."
    return in_on_hit(ctx, name, desc, [{"type": "Area", "radius": radius, "center": "Target", "team": "Enemy", "types": "Units",
                                        "excludePrimary": True, "vfx": "cleave_arc",
                                        "effects": [{"type": "Damage", "amount": dmg, "damageType": "Physical"}]}])


def in_last_stand(ctx, name, threshold, mods, dur=5, cooldown=40, flavor="", vfx="bloodlust"):
    aid = ctx.aid(name)
    sid = ctx.status(aid, {"id": aid + "_buff", "name": name, "isDebuff": False, "dispel": "Basic",
                           "modifiers": [{"stat": s, "value": v} for s, v in mods],
                           "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}", "vfx": vfx})
    desc = (f"{flavor}Falling below {int(threshold * 100)}% health grants {mods_text(mods)} for {dur} s "
            f"(once every {cooldown} s).")
    a = _base(ctx, "Innate", name, desc, "Passive", triggers=[{
        "on": "LowHealth", "threshold": threshold, "internalCooldown": cooldown,
        "effects": [_status_effect(sid, dur, "Caster")]}])
    return _finish(ctx, a)


# ================================================================================================ basic abilities

def skillshot(ctx, slot, name, dmg, rng=11, width=1.0, speed=20, status=None, dur=1.5, pierce=True, vfx="blood_lance",
              hit_vfx="hit_magic", dtype="Magical", cd=(10, -1), mana=(90, 10), usage="nuke", flavor="", ult_dmg=None):
    amount = _lv(slot, dmg, ult_dmg or dmg)
    on_hit = [{"type": "Damage", "amount": amount, "damageType": dtype, "vfx": hit_vfx}]
    if status:
        on_hit.append(_status_effect(status, dur))
    what = "every enemy in its path" if pierce else "the first enemy hit"
    desc = f"{flavor}Launches a projectile in a line that hits {what} for {fmt(amount)} {dtype.lower()} damage{_disable_text(status, dur)}."
    a = _base(ctx, slot, name, desc, "Point", targetTeam="Enemy", castRange=[rng] * _levels(slot), castPoint=0.3, backswing=0.4,
              cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
              onCast=[{"type": "Projectile", "projectile": "Linear", "speed": speed, "range": rng, "width": width,
                       "stopOnFirstHit": not pierce, "team": "Enemy", "types": "Units", "vfx": vfx, "onHit": on_hit}],
              botUsage=usage)
    return _finish(ctx, a)


def bolt(ctx, slot, name, dmg, rng=8, speed=14, status=None, dur=1.2, vfx="blood_lance", hit_vfx="hit_magic", dtype="Magical",
         cd=(12, -1), mana=(100, 10), usage="nuke,stun", flavor="", ult_dmg=None, types="Hero, Creep, Neutral, Summon"):
    amount = _lv(slot, dmg, ult_dmg or dmg)
    on_hit = [{"type": "Damage", "amount": amount, "damageType": dtype, "vfx": hit_vfx}]
    if status:
        on_hit.append(_status_effect(status, dur))
    desc = f"{flavor}Hurls a bolt at an enemy that deals {fmt(amount)} {dtype.lower()} damage{_disable_text(status, dur)}."
    a = _base(ctx, slot, name, desc, "Unit", targetTeam="Enemy", targetTypes=types, castRange=[rng] * _levels(slot),
              castPoint=0.3, backswing=0.4, cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
              onCast=[{"type": "Projectile", "projectile": "Tracking", "speed": speed, "vfx": vfx, "onHit": on_hit}],
              botUsage=usage if status else usage.replace(",stun", ""))
    return _finish(ctx, a)


def bounce(ctx, slot, name, dmg, bounces=3, rng=8, vfx="lightning_strike", dtype="Magical", cd=(11, -1), mana=(95, 10),
           status=None, dur=1.0, flavor="", usage="nuke,farm"):
    amount = _lv(slot, dmg, dmg)
    on_hit = [{"type": "Damage", "amount": amount, "damageType": dtype, "vfx": "hit_magic"}]
    if status:
        on_hit.append(_status_effect(status, dur))
    desc = (f"{flavor}A bolt strikes an enemy and leaps to {bounces} more nearby enemies, dealing {fmt(amount)} "
            f"{dtype.lower()} damage to each{_disable_text(status, dur)}.")
    a = _base(ctx, slot, name, desc, "Unit", targetTeam="Enemy", castRange=[rng] * _levels(slot), castPoint=0.3, backswing=0.4,
              cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
              onCast=[{"type": "Projectile", "projectile": "Tracking", "speed": 16, "bounces": bounces, "bounceRange": 6,
                       "vfx": vfx, "onHit": on_hit}], botUsage=usage)
    return _finish(ctx, a)


def point_aoe(ctx, slot, name, dmg, radius=3.0, rng=9, delay=0.0, status=None, dur=1.5, vfx="hemorrhage_eruption",
              telegraph="telegraph", dtype="Magical", cd=(12, -1), mana=(100, 10), usage="nuke,aoe,farm", flavor="",
              ult_dmg=None, shake=0.25, extra=None):
    amount = _lv(slot, dmg, ult_dmg or dmg)
    effects = [{"type": "Damage", "amount": amount, "damageType": dtype}]
    if status:
        effects.append(_status_effect(status, dur))
    if extra:
        effects += extra
    area = {"type": "Area", "radius": radius, "center": "Point", "team": "Enemy", "types": "Units", "vfx": vfx, "shake": shake,
            "effects": effects}
    on_cast = [area] if delay <= 0 else [{"type": "Delayed", "delay": delay, "radius": radius, "center": "Point",
                                          "vfx": telegraph, "effects": [area]}]
    when = f"After {delay} s, " if delay > 0 else ""
    desc = (f"{flavor}{when}{'e' if when else 'E'}nemies within {radius} m of the target point take {fmt(amount)} "
            f"{dtype.lower()} damage{_disable_text(status, dur)}.")
    a = _base(ctx, slot, name, desc, "Point", targetTeam="Enemy", castRange=[rng] * _levels(slot), castPoint=0.35, backswing=0.4,
              aoeRadius=radius, cooldown=_cd(slot, cd), manaCost=_mana(slot, mana), onCast=on_cast, botUsage=usage)
    return _finish(ctx, a)


def self_aoe(ctx, slot, name, dmg, radius=3.5, status=None, dur=1.5, vfx="ground_slam_blood", dtype="Magical", cd=(12, -1),
             mana=(90, 10), usage="aoe,nuke,farm", flavor="", ult_dmg=None, shake=0.3, heal_pct=None, extra=None):
    amount = _lv(slot, dmg, ult_dmg or dmg)
    effects = [{"type": "Damage", "amount": amount, "damageType": dtype}]
    if heal_pct:
        effects[0]["healCasterPct"] = heal_pct
    if status:
        effects.append(_status_effect(status, dur))
    if extra:
        effects += extra
    heal_txt = f", healing for {fmt(heal_pct, True)} of the damage" if heal_pct else ""
    desc = f"{flavor}Enemies within {radius} m take {fmt(amount)} {dtype.lower()} damage{_disable_text(status, dur)}{heal_txt}."
    a = _base(ctx, slot, name, desc, "NoTarget", castPoint=0.3, backswing=0.4, ignoreFacing=True, aoeRadius=radius,
              cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
              onCast=[{"type": "Area", "radius": radius, "center": "Caster", "team": "Enemy", "types": "Units", "vfx": vfx,
                       "shake": shake, "effects": effects}], botUsage=usage)
    return _finish(ctx, a)


def cone(ctx, slot, name, dmg, rng=7, angle=30, status=None, dur=2.0, vfx="grave_chill_cone", dtype="Magical", cd=(10, -1),
         mana=(90, 10), usage="nuke,aoe,farm", flavor="", slow=None):
    amount = _lv(slot, dmg, dmg)
    effects = [{"type": "Damage", "amount": amount, "damageType": dtype}]
    aid = ctx.aid(name)
    if slow:
        sid = ctx.status(aid, {"id": aid + "_slow", "name": name, "isDebuff": True, "dispel": "Basic",
                               "modifiers": [{"stat": "MoveSpeedPct", "value": [-x for x in L4(*slow)]}],
                               "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}"})
        effects.append(_status_effect(sid, dur))
        extra_txt = f" and slowed by {fmt(L4(*slow), True)} for {dur} s"
    elif status:
        effects.append(_status_effect(status, dur))
        extra_txt = _disable_text(status, dur)
    else:
        extra_txt = ""
    desc = f"{flavor}Enemies in a {angle * 2}° cone {rng} m long take {fmt(amount)} {dtype.lower()} damage{extra_txt}."
    a = _base(ctx, slot, name, desc, "Point", targetTeam="Enemy", castRange=[rng] * _levels(slot), castPoint=0.3, backswing=0.4,
              aoeRadius=rng, cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
              onCast=[{"type": "Area", "radius": rng, "center": "Caster", "team": "Enemy", "types": "Units", "coneAngle": angle,
                       "vfx": vfx, "effects": effects}], botUsage=usage)
    return _finish(ctx, a)


def dot(ctx, slot, name, dps, dur=5, rng=8, slow=0.0, vfx="curse_hit", tick_vfx=None, dtype="Magical", cd=(12, -1),
        mana=(90, 10), usage="nuke", flavor="", status_vfx="bleed_drip", spread=None):
    aid = ctx.aid(name)
    per = _lv(slot, dps, dps)
    st = {"id": aid + "_dot", "name": name, "isDebuff": True, "dispel": "Basic", "interval": 1.0,
          "onInterval": [{"type": "Damage", "target": "Target", "amount": per, "damageType": dtype}],
          "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}", "vfx": status_vfx}
    if slow:
        st["modifiers"] = [{"stat": "MoveSpeedPct", "value": -slow}]
    sid = ctx.status(aid, st)
    slow_txt = f" and slows by {int(slow * 100)}%" if slow else ""
    on_cast = [dict(_status_effect(sid, dur), vfx=vfx)]
    spread_txt = ""
    if spread:
        on_cast = [{"type": "Area", "radius": spread, "center": "Target", "team": "Enemy", "types": "Units", "vfx": vfx,
                    "effects": [_status_effect(sid, dur)]}]
        spread_txt = f" to the target and enemies within {spread} m of it"
    desc = (f"{flavor}Afflicts{spread_txt or ' an enemy'}: {fmt(per)} {dtype.lower()} damage per second for {dur} s{slow_txt}.")
    a = _base(ctx, slot, name, desc, "Unit", targetTeam="Enemy", castRange=[rng] * _levels(slot), castPoint=0.25, backswing=0.35,
              cooldown=_cd(slot, cd), manaCost=_mana(slot, mana), onCast=on_cast, botUsage=usage)
    return _finish(ctx, a)


def dash(ctx, slot, name, dist, pass_dmg, hit_dmg, stun, vfx="crimson_charge_trail", cd=(15, -1.5), mana=(90, 10),
         usage="engage,stun", flavor=""):
    d = _lv(slot, dist, dist)
    pd, hd, st = _lv(slot, pass_dmg, pass_dmg), _lv(slot, hit_dmg, hit_dmg), _lv(slot, stun, stun)
    desc = (f"{flavor}Charges up to {fmt(d)} m, dealing {fmt(pd)} physical damage to enemies passed. The first enemy hero struck "
            f"stops the charge, takes {fmt(hd)} damage and is stunned for {fmt(st)} s.")
    a = _base(ctx, slot, name, desc, "Point", targetTeam="Enemy", castRange=d, castPoint=0.2, backswing=0.3,
              cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
              onCast=[{"type": "Dash", "speed": 22, "maxDistance": d, "stopOnUnitCollision": True, "collisionRadius": 1.1,
                       "collisionTypes": "Hero", "vfx": vfx,
                       "onCollide": [{"type": "Damage", "amount": hd, "damageType": "Physical", "vfx": "blood_impact_heavy"},
                                     _status_effect("stun", st)],
                       "onPass": [{"type": "Damage", "amount": pd, "damageType": "Physical", "vfx": "hit_physical"}]}],
              botUsage=usage)
    return _finish(ctx, a)


def leap(ctx, slot, name, dist, dmg, radius=3.0, status="stun", dur=(1.0, 0.1), vfx="bloodfall_leap", impact="bloodfall_impact",
         dtype="Physical", cd=(16, -1.5), mana=(100, 10), usage="engage,stun", flavor="", ult_dmg=None, ult_dur=None):
    d = _lv(slot, dist, dist)
    amount = _lv(slot, dmg, ult_dmg or dmg)
    du = _lv(slot, dur, ult_dur or dur)
    effects = [{"type": "Damage", "amount": amount, "damageType": dtype}]
    if status:
        effects.append(_status_effect(status, du))
    desc = (f"{flavor}Leaps up to {fmt(d)} m; landing deals {fmt(amount)} {dtype.lower()} damage to enemies within {radius} m"
            f"{_disable_text(status, du)}.")
    a = _base(ctx, slot, name, desc, "Point", targetTeam="Enemy", castRange=d, castPoint=0.25, backswing=0.35, aoeRadius=radius,
              cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
              onCast=[{"type": "Leap", "maxDistance": d, "moveDuration": 0.6, "height": 4, "invulnerable": False, "vfx": vfx,
                       "onArrive": [{"type": "Area", "radius": radius, "center": "Caster", "team": "Enemy", "types": "Units",
                                     "vfx": impact, "shake": 0.5, "effects": effects}]}],
              botUsage=usage)
    return _finish(ctx, a)


def blink(ctx, slot, name, rng, arrive_dmg=None, radius=2.5, vfx="shadowstep", cd=(14, -2), mana=(60, 0), usage="escape,engage",
          flavor=""):
    r = _lv(slot, rng, rng)
    on_cast = [{"type": "Blink", "target": "Caster", "maxDistance": r, "vfx": vfx}]
    txt = ""
    if arrive_dmg:
        amount = _lv(slot, arrive_dmg, arrive_dmg)
        on_cast.append({"type": "Area", "radius": radius, "center": "Caster", "team": "Enemy", "types": "Units", "vfx": "hit_magic",
                        "effects": [{"type": "Damage", "amount": amount, "damageType": "Magical"}]})
        txt = f" Enemies within {radius} m of the arrival take {fmt(amount)} magical damage."
    desc = f"{flavor}Teleports up to {fmt(r)} m.{txt}"
    a = _base(ctx, slot, name, desc, "Point", targetTeam="Enemy", castRange=r, castPoint=0.1, backswing=0.2,
              cooldown=_cd(slot, cd), manaCost=_mana(slot, mana), onCast=on_cast, botUsage=usage)
    return _finish(ctx, a)


def invis(ctx, slot, name, dur=(2.5, 0.5), ms=(0.15, 0.05), vfx="veil_shadow_burst", status_vfx="veil_shadow", cd=(20, -2),
          mana=(60, 5), flavor="", bonus_dmg=None):
    aid = ctx.aid(name)
    du, m = _lv(slot, dur, dur), _lv(slot, ms, ms)
    st = {"id": aid + "_veil", "name": name, "isDebuff": False, "dispel": "Basic", "flags": "Invisible", "breakOnAction": True,
          "modifiers": [{"stat": "MoveSpeedPct", "value": m}], "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}",
          "vfx": status_vfx}
    txt = ""
    if bonus_dmg:
        bd = _lv(slot, bonus_dmg, bonus_dmg)
        st["triggers"] = [{"on": "AttackLanded", "types": "Units", "team": "Enemy",
                           "effects": [{"type": "Damage", "amount": bd, "damageType": "Physical", "vfx": "midnight_slash"}]}]
        txt = f" The attack that breaks it deals {fmt(bd)} bonus physical damage."
    sid = ctx.status(aid, st)
    desc = f"{flavor}Turns invisible for {fmt(du)} s with {fmt(m, True)} bonus movement speed. Attacking or casting reveals.{txt}"
    a = _base(ctx, slot, name, desc, "NoTarget", castPoint=0, backswing=0, ignoreFacing=True, keepsStealth=True,
              cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
              onCast=[dict(_status_effect(sid, du, "Caster"), vfx=vfx)], botUsage="escape")
    return _finish(ctx, a)


def self_buff(ctx, slot, name, mods, dur=6, vfx="bloodlust", cast_vfx=None, cd=(18, -1.5), mana=(70, 10), usage="buff",
              flavor="", flags=None, dispel_self=False, ult_mods=None):
    aid = ctx.aid(name)
    ms = []
    for i, (s, v) in enumerate(mods):
        vv = _lv(slot, v, (ult_mods or mods)[i][1]) if isinstance(v, tuple) else v
        ms.append((s, vv))
    st = {"id": aid + "_buff", "name": name, "isDebuff": False, "dispel": "Basic",
          "modifiers": [{"stat": s, "value": v} for s, v in ms], "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}", "vfx": vfx}
    if flags:
        st["flags"] = flags
    sid = ctx.status(aid, st)
    du = _lv(slot, dur, dur) if isinstance(dur, tuple) else dur
    on_cast = []
    if dispel_self:
        on_cast.append({"type": "Dispel", "target": "Caster", "dispelStrength": "Basic", "dispelDebuffs": True})
    on_cast.append(dict(_status_effect(sid, du, "Caster"), vfx=cast_vfx or vfx))
    flag_txt = {"MagicImmune": " and magic immunity", "Invulnerable": " and invulnerability", "CannotDie": ". It cannot die meanwhile"}.get(flags or "", "")
    desc = f"{flavor}{'Removes debuffs, then gains' if dispel_self else 'Gains'} {mods_text(ms)}{flag_txt} for {fmt(du)} s."
    a = _base(ctx, slot, name, desc, "NoTarget", castPoint=0.1, backswing=0.2, ignoreFacing=True,
              cooldown=_cd(slot, cd), manaCost=_mana(slot, mana), onCast=on_cast, botUsage=usage)
    return _finish(ctx, a)


def ally_buff(ctx, slot, name, mods, dur=6, rng=8, vfx="dawn_fervor", cd=(16, -1.5), mana=(80, 10), flavor="", heal=None, dispel=False):
    aid = ctx.aid(name)
    ms = [(s, _lv(slot, v, v) if isinstance(v, tuple) else v) for s, v in mods]
    sid = ctx.status(aid, {"id": aid + "_buff", "name": name, "isDebuff": False, "dispel": "Basic",
                           "modifiers": [{"stat": s, "value": v} for s, v in ms],
                           "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}", "vfx": vfx})
    on_cast = []
    txt = ""
    if dispel:
        on_cast.append({"type": "Dispel", "dispelStrength": "Basic", "dispelDebuffs": True})
        txt += " Removes its debuffs."
    if heal:
        h = _lv(slot, heal, heal)
        on_cast.append({"type": "Heal", "amount": h, "vfx": "heal_blood"})
        txt += f" Heals it for {fmt(h)}."
    on_cast.append(_status_effect(sid, dur))
    desc = f"{flavor}An ally (or self) gains {mods_text(ms)} for {dur} s.{txt}"
    a = _base(ctx, slot, name, desc, "Unit", targetTeam="AllyOrSelf", targetTypes="Hero, Creep, Summon", castRange=[rng] * _levels(slot),
              castPoint=0.2, backswing=0.3, cooldown=_cd(slot, cd), manaCost=_mana(slot, mana), onCast=on_cast,
              botUsage="buff,ally")
    return _finish(ctx, a)


def shield(ctx, slot, name, amount, dur=6, rng=8, vfx="aegis_shell", cast_vfx="aegis_cast", cd=(16, -1.5), mana=(90, 10),
           flavor="", self_only=False, mods=None):
    aid = ctx.aid(name)
    am = _lv(slot, amount, amount)
    st = {"id": aid + "_shield", "name": name, "isDebuff": False, "dispel": "Basic", "shield": am,
          "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}", "vfx": vfx}
    txt = ""
    if mods:
        ms = [(s, _lv(slot, v, v) if isinstance(v, tuple) else v) for s, v in mods]
        st["modifiers"] = [{"stat": s, "value": v} for s, v in ms]
        txt = f" While it holds: {mods_text(ms)}."
    sid = ctx.status(aid, st)
    who = "Shields itself" if self_only else "Shields an ally (or self)"
    desc = f"{flavor}{who}, absorbing {fmt(am)} damage for {dur} s.{txt}"
    if self_only:
        a = _base(ctx, slot, name, desc, "NoTarget", castPoint=0.1, backswing=0.2, ignoreFacing=True, cooldown=_cd(slot, cd),
                  manaCost=_mana(slot, mana), onCast=[dict(_status_effect(sid, dur, "Caster"), vfx=cast_vfx)], botUsage="buff")
    else:
        a = _base(ctx, slot, name, desc, "Unit", targetTeam="AllyOrSelf", targetTypes="Hero, Creep, Summon",
                  castRange=[rng] * _levels(slot), castPoint=0.2, backswing=0.3, cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
                  onCast=[dict(_status_effect(sid, dur), vfx=cast_vfx)], botUsage="buff,ally")
    return _finish(ctx, a)


def heal(ctx, slot, name, amount, rng=8, radius=0, vfx="heal_blood", cd=(14, -1), mana=(90, 15), flavor="", hot=None, hot_dur=4):
    am = _lv(slot, amount, amount)
    aid = ctx.aid(name)
    effects = [{"type": "Heal", "amount": am, "vfx": vfx}]
    txt = ""
    if hot:
        h = _lv(slot, hot, hot)
        sid = ctx.status(aid, {"id": aid + "_regen", "name": name, "isDebuff": False, "dispel": "Basic",
                               "modifiers": [{"stat": "HpRegen", "value": h}], "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}",
                               "vfx": "mend_pulse"})
        effects.append(_status_effect(sid, hot_dur))
        txt = f", then {fmt(h)} health per second for {hot_dur} s"
    if radius:
        desc = f"{flavor}Heals allies within {radius} m of the target point for {fmt(am)}{txt}."
        a = _base(ctx, slot, name, desc, "Point", targetTeam="AllyOrSelf", castRange=[rng] * _levels(slot), aoeRadius=radius,
                  castPoint=0.3, backswing=0.3, cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
                  onCast=[{"type": "Area", "radius": radius, "center": "Point", "team": "AllyOrSelf", "types": "Units",
                           "vfx": "mend_pulse", "effects": effects}], botUsage="buff,self")
    else:
        desc = f"{flavor}Heals an ally (or self) for {fmt(am)}{txt}."
        a = _base(ctx, slot, name, desc, "Unit", targetTeam="AllyOrSelf", targetTypes="Hero, Creep, Summon",
                  castRange=[rng] * _levels(slot), castPoint=0.25, backswing=0.3, cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
                  onCast=effects, botUsage="buff,ally")
    return _finish(ctx, a)


def disable(ctx, slot, name, status, dur, dmg=None, rng=8, vfx="curse_hit", dtype="Magical", cd=(14, -1), mana=(100, 10),
            flavor="", usage="stun", ult_dur=None, types="Hero, Creep, Neutral, Summon"):
    du = _lv(slot, dur, ult_dur or dur)
    on_cast = []
    txt = ""
    if dmg:
        am = _lv(slot, dmg, dmg)
        on_cast.append({"type": "Damage", "amount": am, "damageType": dtype, "vfx": vfx})
        txt = f"Deals {fmt(am)} {dtype.lower()} damage to an enemy and {DISABLE_WORDS.get(status, status)} it for {fmt(du)} s."
    else:
        txt = f"{DISABLE_WORDS.get(status, status).capitalize()} an enemy for {fmt(du)} s."
    on_cast.append(dict(_status_effect(status, du), vfx=vfx) if not dmg else _status_effect(status, du))
    a = _base(ctx, slot, name, flavor + txt, "Unit", targetTeam="Enemy", targetTypes=types, castRange=[rng] * _levels(slot),
              castPoint=0.3, backswing=0.35, cooldown=_cd(slot, cd), manaCost=_mana(slot, mana), onCast=on_cast,
              botUsage=usage + (",nuke" if dmg else ""))
    return _finish(ctx, a)


def area_disable(ctx, slot, name, status, dur, radius=3.5, dmg=None, rng=0, vfx="howl_wave", cd=(16, -1), mana=(100, 10),
                 flavor="", ult_dur=None, dtype="Magical"):
    """Self-centred (rng=0) or point-targeted area disable."""
    du = _lv(slot, dur, ult_dur or dur)
    effects = []
    dtxt = ""
    if dmg:
        am = _lv(slot, dmg, dmg)
        effects.append({"type": "Damage", "amount": am, "damageType": dtype})
        dtxt = f" take {fmt(am)} {dtype.lower()} damage and"
    effects.append(_status_effect(status, du))
    center = "Point" if rng else "Caster"
    where = " of the target point" if rng else ""
    desc = f"{flavor}Enemies within {radius} m{where}{dtxt} are {PARTICIPLE.get(status, status)} for {fmt(du)} s."
    kw = dict(castPoint=0.3, backswing=0.4, aoeRadius=radius, cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
              onCast=[{"type": "Area", "radius": radius, "center": center, "team": "Enemy", "types": "Units", "vfx": vfx, "shake": 0.3,
                       "effects": effects}], botUsage="stun,aoe" + (",ultimate" if slot == "R" else ""))
    if rng:
        a = _base(ctx, slot, name, desc, "Point", targetTeam="Enemy", castRange=[rng] * _levels(slot), **kw)
    else:
        a = _base(ctx, slot, name, desc, "NoTarget", ignoreFacing=True, **kw)
    return _finish(ctx, a)


def summon(ctx, slot, name, unit, count=(1, 0), dur=30, vfx="summon", cd=(30, -2), mana=(100, 15), flavor="", usage="farm,buff"):
    """unit: dict with id suffix, name, model and combat numbers (see unit_def)."""
    u = unit_def(ctx, **unit)
    c = _lv(slot, count, count)
    desc = (f"{flavor}Summons {fmt(c)} {u['name']}{'s' if max(c) > 1 else ''} for {dur} s "
            f"({u['maxHp']} health, {u['damageMin']}-{u['damageMax']} damage).")
    a = _base(ctx, slot, name, desc, "NoTarget", castPoint=0.4, backswing=0.4, ignoreFacing=True, cooldown=_cd(slot, cd),
              manaCost=_mana(slot, mana), onCast=[{"type": "SpawnUnit", "unitId": u["id"], "count": c, "summonDuration": dur, "vfx": vfx}],
              botUsage=usage)
    return _finish(ctx, a)


def unit_def(ctx, key, name, model, hp, dmg, armor=1, attack="Melee", rng=1.3, ms=3.5, bat=1.3, scale=1.0, tags=None,
             projectile=None):
    u = {"id": f"{ctx.hero}_{key}", "name": name, "kind": "Summon", "attackType": attack, "maxHp": hp, "armor": armor,
         "magicResist": 0.1, "damageMin": dmg[0], "damageMax": dmg[1], "attackRange": rng, "attackPoint": 0.4,
         "attackBackswing": 0.5, "baseAttackTime": bat, "moveSpeed": ms, "turnRate": 10, "collisionRadius": 0.32,
         "visionDay": 9, "visionNight": 7, "acquisitionRange": 7, "bountyGoldMin": 14, "bountyGoldMax": 20,
         "bountyXp": 20, "model": model, "modelScale": scale}
    if attack == "Ranged":
        u["projectileSpeed"] = projectile or 11
    if tags:
        u["tags"] = tags
    if not any(x["id"] == u["id"] for x in ctx.units):
        ctx.units.append(u)
    return u


def zone(ctx, slot, name, dps, radius=3.0, dur=4, rng=9, slow=0.2, vfx="hemorrhage_pool", impact=None, dtype="Magical",
         cd=(14, -1), mana=(100, 10), flavor="", follow=False, ult_dps=None, usage="aoe,farm,nuke", extra=None):
    per = _lv(slot, dps, ult_dps or dps)
    tick = [round(x / 2, 2) for x in per]
    effects = [{"type": "Damage", "amount": tick, "damageType": dtype}]
    if slow:
        effects.append(_status_effect("slow_light" if slow <= 0.2 else "slow_heavy", 0.6))
    if extra:
        effects += extra
    z = {"type": "Zone", "radius": radius, "center": "Caster" if follow else "Point", "zoneDuration": dur, "interval": 0.5,
         "team": "Enemy", "types": "Units", "vfx": vfx, "effects": effects}
    if follow:
        z["followCaster"] = True
    on_cast = [z]
    if impact:
        on_cast.insert(0, {"type": "Area", "radius": radius, "center": "Caster" if follow else "Point", "team": "Enemy", "types": "Units",
                           "vfx": impact, "effects": []})
    slow_txt = f" and slows them by {int(round((0.2 if slow <= 0.2 else 0.4) * 100))}%" if slow else ""
    where = "around the caster (it follows)" if follow else "at the target point"
    desc = f"{flavor}Creates a {radius} m field {where} for {dur} s that deals {fmt(per)} {dtype.lower()} damage per second{slow_txt}."
    kw = dict(castPoint=0.3, backswing=0.4, aoeRadius=radius, cooldown=_cd(slot, cd), manaCost=_mana(slot, mana), onCast=on_cast,
              botUsage=usage + (",ultimate" if slot == "R" else ""))
    if follow:
        a = _base(ctx, slot, name, desc, "NoTarget", ignoreFacing=True, **kw)
    else:
        a = _base(ctx, slot, name, desc, "Point", targetTeam="Enemy", castRange=[rng] * _levels(slot), **kw)
    return _finish(ctx, a)


def hook(ctx, slot, name, dmg, rng=(10, 1), vfx="root_chains", cd=(18, -2), mana=(110, 10), flavor="", stun=None):
    r = _lv(slot, rng, rng)
    am = _lv(slot, dmg, dmg)
    on_hit = [{"type": "Damage", "amount": am, "damageType": "Pure", "vfx": "hit_physical"},
              {"type": "Pull", "toCaster": True, "moveDuration": 0.5}]
    txt = ""
    if stun:
        on_hit.append(_status_effect("stun", stun))
        txt = f" and stuns for {stun} s"
    desc = f"{flavor}Throws a chain up to {fmt(r)} m. The first unit hit takes {fmt(am)} pure damage, is dragged back{txt}."
    a = _base(ctx, slot, name, desc, "Point", targetTeam="Enemy", castRange=r, castPoint=0.3, backswing=0.4,
              cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
              onCast=[{"type": "Projectile", "projectile": "Linear", "speed": 18, "range": max(r), "width": 0.9, "stopOnFirstHit": True,
                       "team": "Enemy", "types": "Units", "vfx": vfx, "onHit": on_hit}], botUsage="engage,nuke")
    return _finish(ctx, a)


def knockback(ctx, slot, name, dmg, radius=3.0, dist=3.0, vfx="howl_wave", cd=(14, -1), mana=(90, 10), flavor="", status=None, dur=1.0):
    am = _lv(slot, dmg, dmg)
    effects = [{"type": "Damage", "amount": am, "damageType": "Magical"}, {"type": "Knockback", "distance": dist, "moveDuration": 0.3}]
    if status:
        effects.append(_status_effect(status, dur))
    desc = f"{flavor}Enemies within {radius} m take {fmt(am)} magical damage and are knocked back {dist} m{_disable_text(status, dur)}."
    a = _base(ctx, slot, name, desc, "NoTarget", castPoint=0.25, backswing=0.35, ignoreFacing=True, aoeRadius=radius,
              cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
              onCast=[{"type": "Area", "radius": radius, "center": "Caster", "team": "Enemy", "types": "Units", "vfx": vfx, "shake": 0.3,
                       "effects": effects}], botUsage="escape,aoe")
    return _finish(ctx, a)


def mana_burn(ctx, slot, name, burn, dmg_mult=1.0, rng=8, vfx="hit_magic", cd=(14, -1), mana=(80, 10), flavor=""):
    b = _lv(slot, burn, burn)
    dm = [round(x * dmg_mult) for x in b]
    desc = f"{flavor}Burns {fmt(b)} of an enemy's mana and deals {fmt(dm)} magical damage."
    a = _base(ctx, slot, name, desc, "Unit", targetTeam="Enemy", targetTypes="Hero", castRange=[rng] * _levels(slot), castPoint=0.3,
              backswing=0.35, cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
              onCast=[{"type": "ReduceMana", "amount": b, "vfx": vfx}, {"type": "Damage", "amount": dm, "damageType": "Magical"}],
              botUsage="nuke")
    return _finish(ctx, a)


def purge(ctx, slot, name, slow_dur=3, rng=8, dmg=None, cd=(12, -1), mana=(70, 5), flavor="", vfx="dawn_burst_small"):
    on_cast = [{"type": "Dispel", "dispelStrength": "Basic", "dispelDebuffs": False, "dispelBuffs": True, "vfx": vfx},
               _status_effect("slow_heavy", slow_dur)]
    txt = ""
    if dmg:
        am = _lv(slot, dmg, dmg)
        on_cast.insert(1, {"type": "Damage", "amount": am, "damageType": "Magical"})
        txt = f", deals {fmt(am)} magical damage"
    desc = f"{flavor}Strips an enemy's buffs{txt} and slows it by 40% for {slow_dur} s."
    a = _base(ctx, slot, name, desc, "Unit", targetTeam="Enemy", castRange=[rng] * _levels(slot), castPoint=0.25, backswing=0.3,
              cooldown=_cd(slot, cd), manaCost=_mana(slot, mana), onCast=on_cast, botUsage="nuke,stun")
    return _finish(ctx, a)


def debuff(ctx, slot, name, mods, dur=5, rng=8, radius=0, vfx="curse_mark", dmg=None, cd=(14, -1), mana=(80, 10), flavor="",
           usage="nuke", flags=None):
    aid = ctx.aid(name)
    ms = [(s, _lv(slot, v, v) if isinstance(v, tuple) else v) for s, v in mods]
    st = {"id": aid + "_debuff", "name": name, "isDebuff": True, "dispel": "Basic",
          "modifiers": [{"stat": s, "value": v} for s, v in ms],
          "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}", "vfx": vfx}
    if flags:
        st["flags"] = flags
    sid = ctx.status(aid, st)
    if flags == "Blinded":
        ms = ms + [("__blind", 0)]
    effects = []
    txt = ""
    if dmg:
        am = _lv(slot, dmg, dmg)
        effects.append({"type": "Damage", "amount": am, "damageType": "Magical"})
        txt = f"deals {fmt(am)} magical damage and "
    effects.append(_status_effect(sid, dur))
    if radius:
        desc = f"{flavor}Enemies within {radius} m of the target point: {txt}{mods_text(ms)} for {dur} s."
        a = _base(ctx, slot, name, desc, "Point", targetTeam="Enemy", castRange=[rng] * _levels(slot), aoeRadius=radius, castPoint=0.3,
                  backswing=0.35, cooldown=_cd(slot, cd), manaCost=_mana(slot, mana),
                  onCast=[{"type": "Area", "radius": radius, "center": "Point", "team": "Enemy", "types": "Units", "vfx": "curse_hit",
                           "effects": effects}], botUsage=usage + ",aoe")
    else:
        desc = f"{flavor}Curses an enemy: {txt}{mods_text(ms)} for {dur} s."
        a = _base(ctx, slot, name, desc, "Unit", targetTeam="Enemy", castRange=[rng] * _levels(slot), castPoint=0.3, backswing=0.35,
                  cooldown=_cd(slot, cd), manaCost=_mana(slot, mana), onCast=[dict(e) for e in effects], botUsage=usage)
    return _finish(ctx, a)


def strike(ctx, slot, name, dmg, mult_text="", status=None, dur=1.0, rng=2.0, vfx="midnight_slash", dtype="Physical", cd=(10, -1),
           mana=(60, 10), flavor="", usage="nuke", lifesteal=None, ult_dmg=None):
    """A melee-range strike on a unit (cleaving blow, pommel bash)."""
    am = _lv(slot, dmg, ult_dmg or dmg)
    d = {"type": "Damage", "amount": am, "damageType": dtype, "vfx": vfx, "shake": 0.2}
    if lifesteal:
        d["healCasterPct"] = lifesteal
    on_cast = [d]
    if status:
        on_cast.append(_status_effect(status, dur))
    ls = f", healing for {fmt(lifesteal, True)} of it" if lifesteal else ""
    desc = f"{flavor}A heavy blow deals {fmt(am)} {dtype.lower()} damage{ls}{_disable_text(status, dur)}."
    a = _base(ctx, slot, name, desc, "Unit", targetTeam="Enemy", castRange=[rng] * _levels(slot), castPoint=0.3, backswing=0.35,
              cooldown=_cd(slot, cd), manaCost=_mana(slot, mana), onCast=on_cast,
              botUsage=usage + (",stun" if status in ("stun", "hex", "root") else ""))
    return _finish(ctx, a)


# ================================================================================================ ultimates

def ult_execute(ctx, name, dmg, threshold, rng=(6, 1), vfx="midnight_slash", flavor="", blink_behind=True):
    r, am, th = L3(*rng), L3(*dmg), L3(*threshold)
    on_cast = []
    if blink_behind:
        on_cast.append({"type": "Blink", "target": "Caster", "behindTarget": True, "vfx": "midnight_step"})
    on_cast += [{"type": "Damage", "amount": am, "damageType": "Pure", "vfx": vfx, "shake": 0.3},
                {"type": "Execute", "threshold": th, "vfx": "midnight_execute"}]
    step = "Steps behind an enemy hero and strikes" if blink_behind else "Strikes an enemy hero"
    desc = f"{flavor}{step} for {fmt(am)} pure damage. A hero left below {fmt(th, True)} health is executed."
    a = _base(ctx, "R", name, desc, "Unit", targetTeam="Enemy", targetTypes="Hero", castRange=r, castPoint=0.3, backswing=0.3,
              cooldown=L3(80, -15), manaCost=L3(120, 50), onCast=on_cast, botUsage="ultimate,nuke")
    return _finish(ctx, a)


def ult_drain(ctx, name, dps, dur=3.0, end_dmg=(150, 100), end_stun=1.5, rng=(7, 0.5), flavor=""):
    per = L3(*dps)
    tick = [round(x / 4, 2) for x in per]
    ed = L3(*end_dmg)
    desc = (f"{flavor}Channels for up to {dur} s, draining {fmt(per)} magical damage per second from an enemy and healing for it. "
            f"Completing the channel deals {fmt(ed)} magical damage and stuns for {end_stun} s.")
    a = _base(ctx, "R", name, desc, "Unit", targetTeam="Enemy", targetTypes="Hero, Creep, Neutral, Summon", castRange=L3(*rng),
              castPoint=0.2, backswing=0.3, channelTime=dur, channelInterval=0.25, cooldown=L3(110, -20), manaCost=L3(150, 75),
              onCast=[], onChannelTick=[{"type": "Damage", "amount": tick, "damageType": "Magical", "healCasterPct": 1.0,
                                         "vfx": "exsanguinate_beam"}, _status_effect("slow_light", 0.3)],
              onChannelEnd=[{"type": "Damage", "amount": ed, "damageType": "Magical", "vfx": "blood_boil", "shake": 0.4},
                            _status_effect("stun", end_stun)],
              castAnimation="Channel", botUsage="ultimate,nuke")
    return _finish(ctx, a)


def ult_cannot_die(ctx, name, dur=(3, 0.5), radius=9, vfx="aegis_shell", flavor=""):
    aid = ctx.aid(name)
    du = L3(*dur)
    sid = ctx.status(aid, {"id": aid + "_ward", "name": name, "isDebuff": False, "dispel": "None", "flags": "CannotDie",
                           "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}", "vfx": vfx})
    desc = f"{flavor}Allied heroes within {radius} m cannot die for {fmt(du)} s (their health stops at 1)."
    a = _base(ctx, "R", name, desc, "NoTarget", castPoint=0.2, backswing=0.3, ignoreFacing=True, aoeRadius=radius,
              cooldown=L3(120, -20), manaCost=L3(150, 50),
              onCast=[{"type": "Area", "radius": radius, "center": "Caster", "team": "AllyOrSelf", "types": "Hero", "vfx": "dawn_burst",
                       "effects": [_status_effect(sid, du)]}], botUsage="ultimate,buff")
    return _finish(ctx, a)


def ult_transform(ctx, name, mods, dur=(12, 2), vfx="bloodlust", cast_vfx="werewolf_transform", flavor="", flags=None):
    return self_buff(ctx, "R", name, mods, dur=dur, vfx=vfx, cast_vfx=cast_vfx, cd=(0, 0), mana=(0, 0), flavor=flavor,
                     flags=flags, usage="ultimate,buff")


def _fix_ult(a, cd=(100, -15), mana=(125, 50), usage=None):
    a["cooldown"] = L3(*cd)
    a["manaCost"] = L3(*mana)
    if usage:
        a["botUsage"] = usage
    return a


def ult_storm(ctx, name, dps, radius=5.0, dur=6, rng=10, follow=False, vfx="blood_rain_pool", impact="blood_rain_impact",
              slow=0.2, flavor="", dtype="Magical"):
    a = zone(ctx, "R", name, dps, radius=radius, dur=dur, rng=rng, slow=slow, vfx=vfx, impact=impact, flavor=flavor,
             follow=follow, ult_dps=dps, dtype=dtype)
    return _fix_ult(a, (110, -15), (150, 50), "ultimate,aoe,nuke")


def ult_nuke(ctx, name, dmg, radius=4.5, rng=11, delay=0.6, status="stun", dur=(1.5, 0.25), vfx="bloodfall_impact", flavor="",
             dtype="Magical"):
    du = L3(*dur)
    effects = [{"type": "Damage", "amount": L3(*dmg), "damageType": dtype}]
    if status:
        effects.append(_status_effect(status, du))
    area = {"type": "Area", "radius": radius, "center": "Point", "team": "Enemy", "types": "Units", "vfx": vfx, "shake": 0.8,
            "effects": effects}
    on_cast = [area] if delay <= 0 else [{"type": "Delayed", "delay": delay, "radius": radius, "center": "Point", "vfx": "telegraph",
                                          "effects": [area]}]
    when = f"After {delay} s, e" if delay > 0 else "E"
    desc = (f"{flavor}{when}nemies within {radius} m of the target point take {fmt(L3(*dmg))} {dtype.lower()} damage"
            f"{_disable_text(status, du)}.")
    a = _base(ctx, "R", name, desc, "Point", targetTeam="Enemy", castRange=[rng] * 3, castPoint=0.4, backswing=0.4, aoeRadius=radius,
              cooldown=L3(110, -15), manaCost=L3(150, 75), onCast=on_cast, botUsage="ultimate,aoe,stun")
    return _finish(ctx, a)


def ult_leap(ctx, name, dist, dmg, radius=4.0, stun=(1.2, 0.3), flavor="", vfx="bloodfall_leap", impact="bloodfall_impact"):
    a = leap(ctx, "R", name, dist, dmg, radius=radius, dur=stun, vfx=vfx, impact=impact, flavor=flavor, ult_dmg=dmg, ult_dur=stun)
    return _fix_ult(a, (100, -15), (150, 50), "ultimate,engage")


def ult_army(ctx, name, unit, count=(3, 1), dur=25, flavor="", vfx="crypt_open"):
    a = summon(ctx, "R", name, unit, count=count, dur=dur, vfx=vfx, flavor=flavor, usage="ultimate,farm")
    return _fix_ult(a, (110, -15), (150, 50))


def ult_mass(ctx, name, status, dur=(2.0, 0.5), radius=6.0, dmg=(150, 75), flavor="", vfx="howl_fury"):
    a = area_disable(ctx, "R", name, status, dur, radius=radius, dmg=dmg, vfx=vfx, flavor=flavor, ult_dur=dur)
    return _fix_ult(a, (120, -20), (150, 50), "ultimate,aoe,stun")


def ult_bolt(ctx, name, dmg, status="stun", dur=(1.5, 0.5), rng=(9, 1), flavor="", vfx="lightning_strike"):
    am, du, r = L3(*dmg), L3(*dur), L3(*rng)
    effects = [{"type": "Damage", "amount": am, "damageType": "Magical", "vfx": vfx, "shake": 0.4}]
    if status:
        effects.append(_status_effect(status, du))
    desc = f"{flavor}Strikes an enemy for {fmt(am)} magical damage{_disable_text(status, du)}."
    a = _base(ctx, "R", name, desc, "Unit", targetTeam="Enemy", targetTypes="Hero, Creep, Neutral, Summon", castRange=r, castPoint=0.35,
              backswing=0.35, cooldown=L3(90, -15), manaCost=L3(150, 75), onCast=effects, botUsage="ultimate,nuke,stun")
    return _finish(ctx, a)


# ================================================================================================ leveled passives (Q/W/E)

def p_mods(ctx, slot, name, mods, flavor=""):
    ms = [(s, _lv(slot, v, v) if isinstance(v, tuple) else v) for s, v in mods]
    a = _base(ctx, slot, name, f"{flavor}Passive: {mods_text(ms)}.", "Passive",
              passiveModifiers=[{"stat": s, "value": v} for s, v in ms])
    return _finish(ctx, a)


def p_crit(ctx, slot, name, chance=(0.1, 0.03), mult=1.7, flavor=""):
    ch = _lv(slot, chance, chance)
    a = _base(ctx, slot, name, f"{flavor}Passive: {fmt(ch, True)} chance to critically strike for {int(mult * 100)}% damage.",
              "Passive", passiveModifiers=[{"stat": "CritChance", "value": ch}, {"stat": "CritMultiplier", "value": mult}])
    return _finish(ctx, a)


def p_bash(ctx, slot, name, every, dmg, stun=0.3, vfx="wrath_bash", flavor=""):
    am = _lv(slot, dmg, dmg)
    desc = f"{flavor}Passive: every {every}th attack on the same target deals {fmt(am)} bonus physical damage and stuns for {stun} s."
    a = _base(ctx, slot, name, desc, "Passive", triggers=[{
        "on": "AttackLanded", "everyN": every, "sameTarget": True, "types": "Units", "team": "Enemy",
        "effects": [{"type": "Damage", "amount": am, "damageType": "Physical", "vfx": vfx},
                    _status_effect("ministun" if stun <= 0.3 else "stun", stun)]}])
    return _finish(ctx, a)


def p_cleave(ctx, slot, name, dmg, radius=2.5, flavor=""):
    am = _lv(slot, dmg, dmg)
    desc = f"{flavor}Passive: attacks deal {fmt(am)} physical damage to other enemies within {radius} m of the target."
    a = _base(ctx, slot, name, desc, "Passive", triggers=[{
        "on": "AttackLanded", "types": "Units", "team": "Enemy",
        "effects": [{"type": "Area", "radius": radius, "center": "Target", "team": "Enemy", "types": "Units", "excludePrimary": True,
                     "vfx": "cleave_arc", "effects": [{"type": "Damage", "amount": am, "damageType": "Physical"}]}]}])
    return _finish(ctx, a)


def p_thorns(ctx, slot, name, dmg, flavor=""):
    am = _lv(slot, dmg, dmg)
    desc = f"{flavor}Passive: melee attackers take {fmt(am)} magical damage each time they strike."
    a = _base(ctx, slot, name, desc, "Passive", triggers=[{
        "on": "Attacked", "meleeOnly": True, "types": "Units", "team": "Enemy",
        "effects": [{"type": "Damage", "target": "TriggerSource", "amount": am, "damageType": "Magical", "noReflect": True,
                     "vfx": "thorn_strike"}]}])
    return _finish(ctx, a)


def p_proc(ctx, slot, name, chance, dmg, status=None, dur=1.0, dtype="Magical", vfx="lightning_strike", flavor=""):
    am = _lv(slot, dmg, dmg)
    effects = [{"type": "Damage", "amount": am, "damageType": dtype, "vfx": vfx}]
    if status:
        effects.append(_status_effect(status, dur))
    desc = f"{flavor}Passive: attacks have a {int(chance * 100)}% chance to deal {fmt(am)} bonus {dtype.lower()} damage{_disable_text(status, dur)}."
    a = _base(ctx, slot, name, desc, "Passive", triggers=[{"on": "AttackLanded", "chance": chance, "types": "Units", "team": "Enemy",
                                                          "effects": effects}])
    return _finish(ctx, a)


def swap(ctx, slot, name, rng=(8, 1), cd=(18, -2), mana=(80, 10), flavor="", vfx="shadowstep"):
    r = _lv(slot, rng, rng)
    desc = f"{flavor}Swaps places with a unit up to {fmt(r)} m away (ally or enemy)."
    a = _base(ctx, slot, name, desc, "Unit", targetTeam="Any", targetTypes="Hero, Creep, Neutral, Summon", castRange=r, castPoint=0.2,
              backswing=0.3, cooldown=_cd(slot, cd), manaCost=_mana(slot, mana), onCast=[{"type": "Swap", "vfx": vfx}], botUsage="escape")
    return _finish(ctx, a)


def taunt(ctx, slot, name, radius=3.5, dur=(1.5, 0.25), armor=None, cd=(16, -1), mana=(90, 10), flavor="", vfx="judgment_glow"):
    """Enemies around the caster are forced to attack it; optionally the caster gains armor meanwhile."""
    aid = ctx.aid(name)
    du = _lv(slot, dur, dur)
    sid = ctx.status(aid, {"id": aid + "_taunt", "name": name, "isDebuff": True, "dispel": "Strong", "flags": "Taunted",
                           "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}", "vfx": vfx})
    on_cast = [{"type": "Area", "radius": radius, "center": "Caster", "team": "Enemy", "types": "Units", "vfx": "dawn_burst",
                "effects": [_status_effect(sid, du)]}]
    txt = ""
    if armor:
        ar = _lv(slot, armor, armor)
        bid = ctx.status(aid, {"id": aid + "_guard", "name": name, "isDebuff": False, "dispel": "Basic",
                               "modifiers": [{"stat": "Armor", "value": ar}], "icon": f"status_{ctx.hero}_guard", "vfx": "barrier"})
        on_cast.append(_status_effect(bid, du, "Caster"))
        txt = f" The caster gains {fmt(ar)} armor meanwhile."
    desc = f"{flavor}Enemies within {radius} m are forced to attack the caster for {fmt(du)} s.{txt}"
    a = _base(ctx, slot, name, desc, "NoTarget", castPoint=0.2, backswing=0.3, ignoreFacing=True, aoeRadius=radius,
              cooldown=_cd(slot, cd), manaCost=_mana(slot, mana), onCast=on_cast, botUsage="engage,aoe,stun")
    return _finish(ctx, a)


def ult_rally(ctx, name, mods, radius=8, dur=(6, 1), vfx="crusade_aura", cast_vfx="crusade_banner", flavor="", heal=None):
    """Allied heroes around the caster gain leveled modifiers (and an optional heal)."""
    aid = ctx.aid(name)
    ms = [(s, L3(*v) if isinstance(v, tuple) else v) for s, v in mods]
    du = L3(*dur)
    sid = ctx.status(aid, {"id": aid + "_rally", "name": name, "isDebuff": False, "dispel": "Basic",
                           "modifiers": [{"stat": s, "value": v} for s, v in ms],
                           "icon": f"status_{ctx.hero}_{snake(name).split('_')[-1]}", "vfx": vfx})
    effects = [_status_effect(sid, du)]
    txt = ""
    if heal:
        h = L3(*heal)
        effects.insert(0, {"type": "Heal", "amount": h, "vfx": "heal_blood"})
        txt = f" They are healed for {fmt(h)}."
    desc = f"{flavor}Allied heroes within {radius} m gain {mods_text(ms)} for {fmt(du)} s.{txt}"
    a = _base(ctx, "R", name, desc, "NoTarget", castPoint=0.2, backswing=0.3, ignoreFacing=True, aoeRadius=radius,
              cooldown=L3(100, -15), manaCost=L3(150, 50),
              onCast=[{"type": "Area", "radius": radius, "center": "Caster", "team": "AllyOrSelf", "types": "Hero", "vfx": cast_vfx,
                       "effects": effects}], botUsage="ultimate,buff")
    return _finish(ctx, a)
