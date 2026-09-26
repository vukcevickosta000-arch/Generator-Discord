"""Wild Covenant roster (#4-#24 of HERO_ROSTER.md): werewolves, witches, druids, forest spirits under lunar pacts."""
import kitlib as K
from common import AGI, INT, M, R, STR, WC, H

BARK = (0.36, 0.27, 0.18)


def beast(fur, height=2.3, bulk=1.5, head=1.3, **kw):
    d = dict(stance="beast", hunch=18, muzzle=True, ears=True, mane=True, claws_hands=True, weapon=None, helmet=False, cape=False,
             skin=fur, torso=fur, fur=fur, legs=fur, sleeve=fur, belly=tuple(min(1, x * 1.5) for x in fur), cuirass=False,
             pauldrons=False, greaves=False, skirt=False, height=height, bulk=bulk, head_scale=head, boots=tuple(x * 0.8 for x in fur))
    d.update(kw)
    return d


def ursolmar(c):
    K.in_mods(c, "Thick Hide", "A bear's hide and a bear's heart: +150 health and +2 health regeneration.", [("MaxHp", 150), ("HpRegen", 2)])
    K.strike(c, "Q", "Maul", (80, 40), status="slow_heavy", dur=1.5, vfx="claw_impact")
    K.self_buff(c, "W", "Hibernate", [("DamageTakenPct", (-0.4, -0.1)), ("HpRegen", (15, 10)), ("MoveSpeedPct", -0.4)], dur=3,
                vfx="barkskin_glow", flavor="Curls up and heals like a bear in its den. ")
    K.self_aoe(c, "E", "Ground Slam", (60, 35), radius=3.5, status="slow_heavy", dur=1.5, dtype="Physical", vfx="ground_slam_blood")
    K.ult_transform(c, "Wrath of the Barrow", [("MaxHp", (400, 250)), ("BonusDamage", (30, 20)), ("Armor", (5, 3))], dur=(12, 1),
                    cast_vfx="werewolf_transform", vfx="moonfang_aura")


def kithra(c):
    K.in_mods(c, "Owl Eyes", "Sees in the dark: +6 night vision and +4 day vision.", [("VisionNight", 6), ("VisionDay", 4)])
    K.bolt(c, "Q", "Talon Strike", (70, 40), status="slow_heavy", dur=1.0, vfx="bat_swarm_blink", hit_vfx="claw_impact", usage="nuke")
    K.ally_buff(c, "W", "Seer's Insight", [("Evasion", (0.15, 0.05)), ("MoveSpeedPct", (0.1, 0.03))], dur=5, vfx="moon_glow",
                flavor="Whispers where the next blow will fall. ")
    K.debuff(c, "E", "Revealing Gaze", [("Armor", (-2, -1))], dur=6, radius=3, rng=10, vfx="judgment_glow", flags="Revealed",
             flavor="Nothing hides from the owl; invisible enemies are revealed. ")
    K.ult_storm(c, "Parliament of Owls", (50, 25), radius=5.5, dur=6, vfx="dazzle_sparks", impact="bat_swarm_blink")


def briarheart(c):
    K.in_thorns(c, "Bramble Hide", 25, "A body of living thorns: ")
    K.cone(c, "Q", "Thorn Lash", (70, 40), rng=5, angle=35, slow=(0.2, 0.05), dur=2, vfx="root_lash")
    K.disable(c, "W", "Entangle", "root", (1.5, 0.3), dmg=(50, 30), vfx="root_eruption")
    K.p_mods(c, "E", "Overgrowth", [("HpRegen", (2, 2)), ("Armor", (1, 1))])
    K.ult_mass(c, "Heart of Briars", "root", dur=(2, 0.5), radius=6, dmg=(140, 70), vfx="root_eruption")


def luneth(c):
    K.in_mods(c, "Silver Hooves", "Swift as moonlight: +10% movement speed.", [("MoveSpeedPct", 0.1)])
    K.dash(c, "Q", "Antler Charge", (9, 1), (40, 20), (70, 35), (1.0, 0.2), vfx="moon_glow")
    K.leap(c, "W", "Moonlit Leap", (7, 1), (60, 30), radius=2.5, status="slow_heavy", dur=(1.5, 0), vfx="pounce_leap", impact="howl_wave")
    K.self_buff(c, "E", "Stag's Grace", [("Evasion", (0.2, 0.05)), ("AttackSpeed", (20, 10))], dur=5, vfx="moon_glow")
    K.ult_mass(c, "Silver Stampede", "stun", dur=(1.5, 0.25), radius=5, dmg=(160, 80), vfx="howl_fury")


def grimtooth(c):
    K.in_aura(c, "Pack Leader", [("AttackSpeed", 15)], radius=9, flavor="The pack runs faster with the Alpha: ")
    K.summon(c, "Q", "Call the Pack", dict(key="wolf", name="Pack Wolf", model="summon_spirit_wolf", hp=300, dmg=(18, 22), ms=4.2, bat=1.1,
                                           tags=["beast"]), count=(2, 0), dur=30, vfx="spirit_wolf_summon", cd=(30, -2))
    K.strike(c, "W", "Savage Bite", (70, 35), lifesteal=0.5, vfx="claw_impact")
    K.p_crit(c, "E", "Go for the Throat", (0.1, 0.03), 1.8)
    K.ult_transform(c, "Lord of the Pack", [("BonusDamage", (40, 30)), ("AttackSpeed", (40, 20)), ("MoveSpeedPct", (0.15, 0.05))],
                    dur=(10, 1), cast_vfx="howl_fury", vfx="moonfang_aura")


def yew(c):
    K.in_aura(c, "Ancient Sap", [("HealAmp", 0.15)], radius=9, flavor="Healing flows stronger in her shade: ")
    K.heal(c, "Q", "Sap Mend", (90, 45), hot=(4, 3), vfx="leaf_burst")
    K.area_disable(c, "W", "Grasping Roots", "root", (1.25, 0.25), radius=3, rng=9, dmg=(40, 25), vfx="root_eruption")
    K.ally_buff(c, "E", "Bark Skin", [("Armor", (4, 2)), ("HpRegen", (3, 2))], dur=8, vfx="barkskin_glow")
    K.heal(c, "R", "Grove's Embrace", (220, 110), radius=7, rng=10, hot=(8, 4), hot_dur=5, vfx="leaf_burst")


def vix(c):
    K.in_evasion(c, "Foxfire", 0.18, "Never where you swung: ")
    K.blink(c, "Q", "Feint", (6, 1), arrive_dmg=(40, 30), vfx="veil_shadow_burst")
    K.debuff(c, "W", "Trickster's Mark", [("MoveSpeedPct", (-0.2, -0.05))], dur=3, dmg=(50, 30), vfx="crimson_mark_rose")
    K.invis(c, "E", "Vanish", dur=(2, 0.5), bonus_dmg=(60, 30))
    K.ult_transform(c, "Nine Tails", [("Evasion", (0.3, 0.1)), ("MoveSpeedPct", (0.2, 0.05)), ("CritChance", (0.2, 0.1))], dur=(8, 1),
                    cast_vfx="veil_shadow_burst", vfx="velvet_dark_glow")


def mossbeard(c):
    K.in_mods(c, "Mossy Hide", "Green and growing: +4 health regeneration.", [("HpRegen", 4)])
    K.heal(c, "Q", "Regrowth", (80, 40), rng=7, hot=(4, 3), vfx="leaf_burst")
    K.shield(c, "W", "Mossy Mantle", (90, 40), vfx="barkskin_glow", cast_vfx="leaf_burst", mods=[("HpRegen", (3, 2))])
    K.self_aoe(c, "E", "Rooted Stomp", (60, 30), radius=3, status="root", dur=1.0, vfx="root_eruption")
    K.ult_rally(c, "Verdant Resurgence", [("HpRegen", (20, 10)), ("Armor", (5, 3))], radius=9, dur=(6, 1), vfx="barkskin_glow",
                cast_vfx="leaf_burst", heal=(150, 75))


def ravenna(c):
    K.in_spell_stacks(c, "Carrion Sight", 0.03, 8, 5, vfx="veil_shadow", flavor="The crows see for her: ")
    K.skillshot(c, "Q", "Crow Barrage", (80, 45), width=1.2, vfx="bat_swarm_blink", hit_vfx="bat_swarm_blink")
    K.debuff(c, "W", "Peck Out Eyes", [("MoveSpeedPct", -0.1)], dur=3, dmg=(60, 30), vfx="claw_marks", flags="Blinded")
    K.blink(c, "E", "Crow Form", (8, 1), vfx="bat_swarm_blink", usage="escape")
    K.ult_storm(c, "Storm of Crows", (65, 30), radius=5, dur=6, vfx="veil_shadow", impact="bat_swarm_blink")


def nerissa(c):
    K.in_evasion(c, "Marsh Mist", 0.1, "Mist clings to her: ")
    K.disable(c, "Q", "Drowning Grip", "stun", (1.2, 0.3), dmg=(60, 35), vfx="brew_steam")
    K.zone(c, "W", "Bog", (20, 10), radius=3.5, dur=5, slow=0.4, vfx="fumes_green")
    K.hook(c, "E", "Undertow", (50, 30), rng=(8, 1), vfx="brew_steam", flavor="Drags a victim into the marsh. ")
    K.ult_nuke(c, "Sunken Tide", (180, 90), radius=5, status="root", dur=(2.5, 0.5), vfx="root_eruption")


def hollowstag(c):
    K.in_bash(c, "Spirit Antlers", 4, 40, 0.3, vfx="howl_wave")
    K.dash(c, "Q", "Spirit Charge", (10, 1), (40, 20), (80, 40), (1.2, 0.2), vfx="moon_glow")
    K.area_disable(c, "W", "Ghostly Bellow", "fear", (1, 0.25), radius=3.5, dmg=(50, 30), vfx="howl_wave")
    K.self_buff(c, "E", "Ethereal", [("MoveSpeedPct", (0.15, 0.05))], dur=3, flags="Phased", vfx="moon_glow", usage="escape,buff")
    K.ult_leap(c, "Wild Hunt", (12, 1), (180, 90), radius=4.5, vfx="pounce_leap", impact="howl_fury")


def agathe(c):
    K.in_attack_slow(c, "Thorny Darts", 0.12, 1.5, "Her darts catch like briars. ")
    K.bolt(c, "Q", "Thornbolt", (80, 40), vfx="thorn_strike", hit_vfx="thorn_strike", usage="nuke")
    K.zone(c, "W", "Bramble Patch", (30, 15), radius=3, dur=5, vfx="root_lash")
    K.dot(c, "E", "Witch's Thorns", (20, 12), dur=6, spread=2.5, vfx="thorn_strike", status_vfx="bleed_drip")
    K.ult_bolt(c, "Thorned Crown", (250, 125), status="root", dur=(2, 0.5), vfx="root_eruption")


def varkul(c):
    K.in_last_stand(c, "Blood Rage", 0.35, [("AttackSpeed", 60), ("Lifesteal", 0.2)], dur=6, cooldown=30)
    K.leap(c, "Q", "Frenzied Leap", (7, 1), (60, 35), radius=2.5, status="slow_heavy", dur=(1.5, 0), vfx="pounce_leap", impact="claw_impact")
    K.self_aoe(c, "W", "Rending Howl", (50, 30), radius=4, status="slow_light", dur=2, vfx="howl_fury")
    K.p_mods(c, "E", "Berserk", [("AttackSpeed", (10, 10)), ("BonusDamage", (5, 5))])
    K.ult_transform(c, "Blood Moon Frenzy", [("AttackSpeed", (80, 40)), ("Lifesteal", (0.25, 0.1)), ("MoveSpeedPct", 0.15)], dur=(10, 1),
                    cast_vfx="werewolf_transform", vfx="bloodlust")


def isra(c):
    wound = {"id": "isra_silver_claws_wound", "name": "Silver Wound", "isDebuff": True, "dispel": "Basic",
             "modifiers": [{"stat": "HealAmp", "value": -0.3}, {"stat": "HpRegen", "value": -4}], "icon": "status_isra_wound",
             "vfx": "bleed_drip"}
    K.in_on_hit(c, "Silver Claws", "Attacks leave a silver wound for 3 s: -30% healing received and -4 health regeneration.",
                [{"type": "ApplyStatus", "status": wound["id"], "duration": 3}], statuses=[wound],
                flavor="Silver keeps wounds from closing. ")
    K.leap(c, "Q", "Silver Pounce", (6, 1), (60, 30), radius=1.5, status="slow_heavy", dur=(1.5, 0), vfx="pounce_leap", impact="claw_impact")
    K.strike(c, "W", "Rake", (70, 40), status="slow_heavy", dur=1.5, vfx="claw_marks")
    K.invis(c, "E", "Moon Shadow", dur=(2, 0.5), bonus_dmg=(50, 30), vfx="moon_glow")
    K.ult_execute(c, "Silver Execution", (180, 90), (0.16, 0.05))


def greenknight(c):
    K.in_mods(c, "Evergreen", "His wounds grow back like spring: +4 health regeneration.", [("HpRegen", 4)])
    K.strike(c, "Q", "Beheading Stroke", (90, 45), status="stun", dur=0.8, vfx="midnight_slash")
    K.taunt(c, "W", "Challenge of the Green", radius=3.5, dur=(1.5, 0.25), armor=(3, 2))
    K.p_mods(c, "E", "Pact of Harrowmere", [("StatusResist", (0.08, 0.04)), ("HpRegen", (2, 2))])
    K.ult_execute(c, "The Returned Blow", (200, 100), (0.2, 0.05), rng=(3, 0), blink_behind=False,
                  flavor="A year and a day later, the blow is returned. ")


def sprigg(c):
    K.in_aura(c, "Spore Cloud", [("AttackSpeed", -10)], radius=6, team="Enemy", flavor="Coughing spores slow every arm: ", vfx="fumes_green")
    K.bounce(c, "Q", "Spore Bolt", (60, 40), bounces=3, vfx="fumes_green")
    K.point_aoe(c, "W", "Fungus Trap", (80, 40), radius=2.5, delay=1.0, status="root", dur=1.5, vfx="fumes_green")
    K.summon(c, "E", "Mycelium", dict(key="mushroom", name="Mushroom Man", model="summon_treant", hp=260, dmg=(14, 20), ms=3.2, scale=0.6,
                                      tags=["plant"]), count=(1, 1), dur=25, vfx="treant_rise", cd=(24, -2))
    K.ult_storm(c, "Great Sporebloom", (50, 25), radius=6, dur=7, vfx="fumes_green", impact="leaf_burst")


def ansa(c):
    K.in_aura(c, "Den Mother", [("BonusDamage", 8)], radius=9, flavor="Her pack fights harder at her side: ")
    K.summon(c, "Q", "Call the Pups", dict(key="wolf", name="Young Wolf", model="summon_spirit_wolf", hp=220, dmg=(14, 18), ms=4.2, bat=1.1,
                                           scale=0.85, tags=["beast"]), count=(1, 1), dur=30, vfx="spirit_wolf_summon", cd=(28, -2))
    K.area_disable(c, "W", "Protective Snarl", "fear", (1, 0.2), radius=3, vfx="howl_wave")
    K.ally_buff(c, "E", "Mother's Fury", [("AttackSpeed", (20, 10)), ("BonusDamage", (10, 10))], dur=6, vfx="howl_fury")
    K.ult_army(c, "Great Wolf", dict(key="great_wolf", name="Great Wolf", model="rts_wc_shifter_wolf", hp=1100, dmg=(45, 55), armor=4,
                                     ms=4.0, bat=1.2, scale=1.4, tags=["beast"]), count=(1, 1), dur=30, vfx="spirit_wolf_summon")


def stormhorn(c):
    K.in_thorns(c, "Static Hide", 20, "Lightning crawls across its hide: ")
    K.self_aoe(c, "Q", "Thunderclap", (80, 40), radius=3.5, status="slow_heavy", dur=2, vfx="lightning_strike")
    K.dash(c, "W", "Lightning Charge", (9, 1), (40, 20), (70, 35), (0.8, 0.2), vfx="moon_glow")
    K.p_proc(c, "E", "Stormcaller", 0.2, (40, 20), vfx="lightning_strike")
    K.ult_storm(c, "Heart of the Storm", (70, 35), radius=4.5, dur=6, follow=True, vfx="lightning_strike", impact="lightning_strike")


def mira(c):
    K.in_attack_slow(c, "Nightshade", 0.12, 2, "Every dart carries a little poison. ")
    K.dot(c, "Q", "Poison Dart", (25, 12), dur=5, slow=0.2, vfx="fumes_green", status_vfx="fumes_green")
    K.invis(c, "W", "Thornveil", dur=(2.5, 0.5), vfx="leaf_burst", status_vfx="veil_shadow")
    K.bolt(c, "E", "Paralytic", (40, 30), status="stun", dur=1.0, vfx="thorn_strike", hit_vfx="fumes_green")
    K.ult_bolt(c, "Deathbloom", (280, 140), status="silence", dur=(2, 0.5), vfx="fumes_green")


def aldric(c):
    K.in_aura(c, "Grove Warden", [("Armor", 1.5), ("HpRegen", 1.5)], radius=9, flavor="Allies take heart in his grove: ")
    K.heal(c, "Q", "Sanctuary Seed", (60, 30), radius=4, rng=9, hot=(4, 2), vfx="leaf_burst")
    K.shield(c, "W", "Thorn Barrier", (80, 40), vfx="barkskin_glow", cast_vfx="leaf_burst")
    K.disable(c, "E", "Root Snare", "root", (1.5, 0.25), dmg=(40, 25), vfx="root_eruption")
    K.ult_rally(c, "Grove Sanctuary", [("DamageTakenPct", (-0.3, -0.1)), ("HpRegen", (15, 10))], radius=8, dur=(6, 1),
                vfx="barkskin_glow", cast_vfx="treant_rise")


def lunara(c):
    K.in_spell_stacks(c, "Moonblessed", 0.04, 8, 4, vfx="moon_glow", flavor="The moon answers every prayer: ")
    K.point_aoe(c, "Q", "Lunar Beam", (80, 45), radius=2, status="ministun", dur=0.5, vfx="moon_glow")
    K.bounce(c, "W", "Crescent Glaive", (70, 40), bounces=3, vfx="moon_glow")
    K.shield(c, "E", "Moon Ward", (80, 40), self_only=True, vfx="barrier", cast_vfx="moon_glow")
    a = K.ult_storm(c, "Eclipse", (80, 40), radius=5.5, dur=5, vfx="moon_glow", impact="witching_hour_burst")
    a["onCast"].insert(0, {"type": "ForceNight", "duration": [8, 10, 12]})
    a["description"] += " The sky goes dark: it is night for 8/10/12 s."


HEROES = [
    H("ursolmar", "Ursolmar", "the Barrow Bear", WC, STR, M, ["Durable"],
      "Ursolmar sleeps in the barrow mounds of the old kings and wakes only when the Covenant calls. He is older than the "
      "mounds, and he remembers when the kings were cubs.",
      "Slow, grumbling, fiercely protective.", ursolmar, difficulty=1, look=beast((0.35, 0.24, 0.16), height=2.45, bulk=1.75, head=1.35)),
    H("kithra", "Kithra", "the Owl Seer", WC, INT, R, ["Support"],
      "Kithra keeps the Covenant's watch from the tallest trees, and her owls see everything that moves beneath the canopy. "
      "She speaks rarely and is never surprised.",
      "Watchful, calm, cryptic.", kithra, difficulty=1, look=dict(hood=True, wings=True, weapon="staff", cloth=(0.45, 0.38, 0.3))),
    H("briarheart", "Briarheart", "", WC, STR, M, ["Durable"],
      "Briarheart grew from a thicket planted over a fallen knight. The thorns kept the knight's courage and none of his "
      "mercy.",
      "Silent, stubborn, rustling.", briarheart, difficulty=1,
      look=dict(skin=BARK, armor=BARK, armor_mat="bf_matte", bark_ridges=True, leaves=(0.3, 0.42, 0.18), weapon="club", helmet=False,
                wood=(0.3, 0.22, 0.15), antlers=True, hunch=8)),
    H("luneth", "Luneth", "the Silver Stag", WC, AGI, M, ["Initiator"],
      "Luneth is the stag that leads the Covenant's hunts under the full moon. Hunters who have seen her charge say "
      "the moonlight goes with her.",
      "Proud, swift, wild.", luneth, look=dict(antlers=True, skin=(0.82, 0.82, 0.86), weapon="rapier", helmet=False, cape=True,
                                               glow=(0.7, 0.9, 1.0))),
    H("grimtooth", "Grimtooth", "the Alpha", WC, AGI, M, ["Carry"],
      "Grimtooth has led the Covenant's largest pack for forty winters. Every wolf in the forest knows his howl, and "
      "every wolf answers.",
      "Commanding, scarred, loyal to his own.", grimtooth, look=beast((0.3, 0.3, 0.32), height=2.3, bulk=1.4)),
    H("yew", "Old Mother Yew", "", WC, INT, R, ["Support"],
      "The oldest tree in the Covenant's forest decided, one spring, to walk. She heals the young, scolds the reckless and "
      "roots the wicked to the spot.",
      "Grandmotherly, patient, creaking.", yew, difficulty=1,
      look=dict(skin=BARK, cloth=(0.28, 0.34, 0.18), leaves=(0.24, 0.4, 0.16), hunch=14, height=2.15, weapon="staff", hood=False,
                antlers=True, wood=(0.32, 0.24, 0.15))),
    H("vix", "Sable Vix", "the Fox Trickster", WC, AGI, M, ["Escape"],
      "Sable Vix has stolen from every court in Velmoragh and been caught by none of them. She is never where you "
      "swung, and she is always laughing.",
      "Playful, sly, unrepentant.", vix, difficulty=3, look=beast((0.75, 0.38, 0.14), height=1.95, bulk=1.0, head=1.2, weapon="sword")),
    H("mossbeard", "Mossbeard", "", WC, STR, M, ["Support"],
      "Mossbeard was a hermit who sat so still for so long that the forest grew over him. When he stood up again he "
      "brought the forest with him.",
      "Kindly, slow-spoken, green.", mossbeard, difficulty=1,
      look=dict(skin=(0.5, 0.42, 0.34), hair=(0.32, 0.45, 0.2), leaves=(0.3, 0.45, 0.2), weapon="club", helmet=False, mane=True,
                fur=(0.3, 0.42, 0.2), wood=(0.35, 0.26, 0.17))),
    H("ravenna", "Ravenna Blackfeather", "", WC, INT, R, ["Nuker"],
      "Ravenna was a witch who took the shape of a crow so often she forgot which was the disguise. Her flock follows her "
      "everywhere, and it is always hungry.",
      "Sharp, restless, cawing laugh.", ravenna, look=dict(hood=False, collar=True, wings=True, weapon="staff", long_hair=True,
                                                           hair=(0.05, 0.05, 0.08), cloth=(0.1, 0.1, 0.12))),
    H("nerissa", "Nerissa", "the Marsh Spirit", WC, INT, R, ["Disabler"],
      "Nerissa is the spirit of the drowned marshes at the forest's edge. Travellers who follow her lights are never seen "
      "again, and the marsh is always a little deeper.",
      "Soft, eerie, sings under her breath.", nerissa, look=dict(skin=(0.6, 0.72, 0.68), long_hair=True, hair=(0.2, 0.35, 0.3), weapon="staff",
                                                                 cloth=(0.18, 0.3, 0.28), glow=(0.4, 1.0, 0.8))),
    H("hollowstag", "The Hollow Stag", "", WC, STR, M, ["Initiator"],
      "The ghost of the first stag ever hunted in the Covenant's forest. It charges through the living as if they were "
      "mist, and the living feel it for days.",
      "Silent, luminous, relentless.", hollowstag, look=dict(antlers=True, skin=(0.75, 0.85, 0.9), armor=(0.6, 0.7, 0.78), glow=(0.6, 0.9, 1.0),
                                                             weapon="club", helmet=False, wood=(0.7, 0.78, 0.82))),
    H("agathe", "Agathe", "the Bramblewitch", WC, INT, R, ["Nuker"],
      "Agathe grows her hedges of thorns around every village that wrongs her. Some villages are still inside them.",
      "Spiteful, clever, holds grudges forever.", agathe, look=dict(hat=True, hood=False, weapon="staff", hair=(0.4, 0.35, 0.3), long_hair=True)),
    H("varkul", "Varkul", "the Blood Moon Berserker", WC, STR, M, ["Carry"],
      "Varkul was bitten under a Blood Moon, and the curse took hold twice as hard. When the rage takes him he does not "
      "stop until the moon sets or the enemy does.",
      "Barely restrained, raw, honest.", varkul, look=beast((0.35, 0.16, 0.14), height=2.35, bulk=1.5, glow=(1.0, 0.2, 0.15))),
    H("isra", "Isra Silverclaw", "", WC, AGI, M, ["Assassin"],
      "Isra hunts rogue werewolves for the Covenant, and her claws are shod in silver. The wounds they leave do not close.",
      "Cold, focused, merciful only to the young.", isra, difficulty=3, look=dict(weapon=None, claws_hands=True, hood=True, cape=True,
                                                                                  accent=(0.85, 0.87, 0.92))),
    H("greenknight", "The Green Knight of Harrowmere", "", WC, STR, M, ["Durable"],
      "The Green Knight offers every challenger a free blow, and returns it a year and a day later. No one has yet survived "
      "the return.",
      "Courtly, jovial, terrifying.", greenknight, look=dict(armor=(0.24, 0.42, 0.2), cloth=(0.2, 0.35, 0.16), weapon="greatsword", helmet=True,
                                                            crest=True, cape=True, leaves=(0.3, 0.5, 0.2))),
    H("sprigg", "Sprigg", "the Mushroom Shaman", WC, INT, R, ["Pusher"],
      "Sprigg speaks for the fungus that runs beneath the whole forest. It has a great deal to say, most of it about "
      "decomposition.",
      "Cheerful, rambling, slightly damp.", sprigg, look=dict(height=1.45, bulk=1.1, hat=True, hood=False, cloth=(0.6, 0.15, 0.1),
                                                            weapon="staff", hunch=6)),
    H("ansa", "Ansa", "the Wolfmother", WC, STR, M, ["Summoner"],
      "Ansa raised a whole pack from abandoned pups and taught them to fight like soldiers. They would die for her, and "
      "several of them have.",
      "Fierce, warm, protective.", ansa, look=dict(mane=True, fur=(0.4, 0.38, 0.36), weapon="mace", helmet=False, long_hair=True,
                                                   hair=(0.6, 0.55, 0.5))),
    H("stormhorn", "Stormhorn", "the Thunder Elk", WC, STR, M, ["Initiator"],
      "Stormhorn is the elk that carries the storms across the forest. Where its hooves strike, thunder follows.",
      "Booming, proud, electric.", stormhorn, look=dict(antlers=True, glow=(0.6, 0.8, 1.0), weapon="hammer", helmet=False, mane=True,
                                                        fur=(0.4, 0.36, 0.32))),
    H("mira", "Mira Thornveil", "", WC, AGI, R, ["Assassin"],
      "Mira grew up in the thorn hedges and learned to kill with what grew there. Her darts are tipped with nightshade and "
      "she has never needed a second one.",
      "Quiet, patient, deadly.", mira, difficulty=3, look=dict(hood=True, weapon="bow", cloth=(0.18, 0.28, 0.18))),
    H("aldric", "Aldric", "the Grovekeeper", WC, INT, R, ["Support"],
      "Aldric keeps the sacred grove at the heart of the forest, where no blood may be spilled. He brings a little of the "
      "grove with him wherever the Covenant fights.",
      "Serene, firm, protective.", aldric, difficulty=1, look=dict(antlers=True, hood=False, weapon="staff", hair=(0.8, 0.78, 0.74),
                                                                   long_hair=True)),
    H("lunara", "Lunara", "the Eclipse Priestess", WC, INT, R, ["Nuker"],
      "Lunara serves the moon in its darkest phase and can call an eclipse at noon. Her beams fall hardest in the dark she "
      "brings.",
      "Mystic, intense, devout.", lunara, look=dict(tiara=True, weapon="staff", long_hair=True, hair=(0.85, 0.85, 0.9), glow=(0.6, 0.8, 1.0),
                                                   cloth=(0.18, 0.2, 0.34))),
]
