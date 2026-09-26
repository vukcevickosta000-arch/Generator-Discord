"""Dawnguard roster (#2-#24 of HERO_ROSTER.md): paladins, monster hunters, battle priests of a zealous crusade."""
import kitlib as K
from common import AGI, DG, INT, M, R, STR, H


def cassia(c):
    K.in_spell_stacks(c, "Zealous Flame", 0.04, 8, 4, vfx="dawn_fervor", flavor="Her faith burns hotter with every prayer: ")
    K.bolt(c, "Q", "Purifying Brand", (90, 45), vfx="dawn_bolt", hit_vfx="dawn_burst_small", usage="nuke")
    K.point_aoe(c, "W", "Pyre of Penance", (80, 40), radius=3, delay=0.5, status="slow_light", dur=2, vfx="dawn_burst")
    K.debuff(c, "E", "Confession", [("MagicResist", (-0.1, -0.05))], dur=6, dmg=(40, 25), vfx="judgment_glow",
             flavor="Burns away the sinner's defences. ")
    K.ult_storm(c, "Cleansing Inferno", (75, 35), radius=5, dur=5, vfx="consecrate_ground", impact="dawn_burst")


def brennock(c):
    K.in_on_hit(c, "Silver Bolts", "Attacks deal 12 bonus pure damage.", [{"type": "Damage", "amount": 12, "damageType": "Pure"}],
                flavor="Silver for the unnatural. ")
    K.skillshot(c, "Q", "Stake Shot", (80, 40), rng=11, width=0.8, pierce=False, status="root", dur=1.5, dtype="Physical",
                vfx="dawn_bolt", hit_vfx="hit_physical", flavor="Pins the first enemy to the ground. ", usage="nuke,stun")
    K.point_aoe(c, "W", "Bear Trap", (60, 35), radius=2, delay=1.0, status="root", dur=2, dtype="Physical", vfx="root_chains")
    K.self_buff(c, "E", "Steady Aim", [("AttackRange", (1, 0.5)), ("CritChance", (0.1, 0.05))], dur=6, vfx="dawn_fervor")
    K.ult_storm(c, "Hunter's Barrage", (70, 35), radius=4.5, dur=5, dtype="Physical", vfx="consecrate_motes", impact="dawn_burst_small")


def oswin(c):
    K.in_mods(c, "Templar's Resolve", "Plate and prayer: +3 armor and +10% status resistance.", [("Armor", 3), ("StatusResist", 0.1)])
    K.strike(c, "Q", "Shield Slam", (70, 40), status="stun", dur=1.0, vfx="judgment_strike")
    K.taunt(c, "W", "Taunting Oath", radius=3.5, dur=(1.5, 0.25), armor=(3, 2))
    K.shield(c, "E", "Aegis Wall", (100, 50), vfx="aegis_shell", cast_vfx="aegis_cast")
    K.ult_rally(c, "Bulwark of Faith", [("Armor", (8, 4)), ("DamageTakenPct", (-0.2, -0.05))], radius=8, dur=(6, 1))


def mercy(c):
    K.in_aura(c, "Field Medic", [("HpRegen", 2)], radius=9, flavor="Wounds close faster near her: ")
    K.heal(c, "Q", "Battle Prayer", (100, 45), hot=(3, 2), vfx="dawn_burst_small")
    K.bolt(c, "W", "Smite", (80, 40), status="ministun", dur=0.5, vfx="dawn_bolt", hit_vfx="judgment_strike")
    K.ally_buff(c, "E", "Blessed Armor", [("Armor", (3, 2)), ("MagicResist", (0.1, 0.05))], dur=6, vfx="aegis_shell", dispel=True)
    K.heal(c, "R", "Mass Absolution", (250, 120), radius=8, rng=10, hot=(8, 4), hot_dur=4, vfx="dawn_burst")


def gideon(c):
    K.in_mana_burn(c, "Witch Hunter", 18, "His bolts are blessed against sorcery. ")
    K.purge(c, "Q", "Purge", slow_dur=3, dmg=(60, 30))
    K.mana_burn(c, "W", "Null Bolt", (80, 40), vfx="dawn_burst_small")
    K.p_mods(c, "E", "Warded Coat", [("MagicResist", (0.08, 0.04)), ("StatusResist", (0.05, 0.03))])
    K.ult_bolt(c, "Silence the Witch", (200, 100), status="silence", dur=(3, 0.75), vfx="judgment_strike")


def aurelia(c):
    K.in_aura(c, "Commander's Presence", [("MoveSpeedPct", 0.05)], radius=9, flavor="The host moves at her pace: ")
    K.blink(c, "Q", "Wing Strike", (7, 1), arrive_dmg=(50, 30), vfx="dawn_burst_small")
    K.cone(c, "W", "Judgment Sword", (70, 40), rng=4, angle=40, dtype="Physical", vfx="dawn_burst")
    K.self_buff(c, "E", "Holy Vigor", [("Armor", (3, 2)), ("HpRegen", (5, 5))], dur=5, vfx="dawn_fervor")
    K.ult_leap(c, "Winged Descent", (12, 1), (200, 100), radius=4.5, vfx="crusade_charge", impact="dawn_burst")


def tobias(c):
    K.in_bash(c, "Resounding", 5, 35, 0.5, vfx="howl_wave")
    K.self_aoe(c, "Q", "Great Bell", (70, 35), radius=3.5, status="stun", dur=1.0, vfx="howl_wave")
    K.area_disable(c, "W", "Toll of Warning", "slow_heavy", (2, 0.25), radius=4, dmg=(40, 20), vfx="howl_wave")
    K.strike(c, "E", "Clapper Strike", (80, 40), vfx="wrath_bash")
    K.ult_mass(c, "Cathedral Peal", "stun", dur=(1.75, 0.5), radius=6.5, dmg=(160, 80), vfx="dawn_burst")


def lucan(c):
    K.in_crit(c, "Slayer's Craft", 0.15, 2.0, "He knows where monsters are soft: ")
    K.strike(c, "Q", "Silver Strike", (80, 40), status="slow_heavy", dur=1.5, vfx="judgment_strike")
    K.skillshot(c, "W", "Hunter's Net", (40, 20), rng=9, width=1.2, pierce=False, status="root", dur=1.5, vfx="root_chains",
                hit_vfx="root_chains", usage="nuke,stun")
    K.p_mods(c, "E", "Slayer's Momentum", [("AttackSpeed", (10, 10)), ("MoveSpeedPct", (0.03, 0.02))])
    K.ult_transform(c, "Monster Slayer", [("BonusDamagePct", (0.3, 0.15)), ("Lifesteal", (0.15, 0.05))], dur=(10, 1), cast_vfx="dawn_burst")


def maerith(c):
    K.in_mods(c, "Unshaken Faith", "+15% status resistance and +5% magic resistance.", [("StatusResist", 0.15), ("MagicResist", 0.05)])
    K.disable(c, "Q", "Chains of Faith", "root", (1.5, 0.3), dmg=(60, 30), vfx="root_chains")
    K.debuff(c, "W", "Interrogate", [("Armor", (-2, -1)), ("MagicResist", (-0.08, -0.04))], dur=5, dmg=(50, 30), vfx="judgment_glow")
    K.area_disable(c, "E", "Holy Silence", "silence", (1.5, 0.5), radius=3, rng=9, vfx="silence_rune")
    K.ult_nuke(c, "Tribunal", (180, 90), radius=4.5, status="stun", dur=(1.5, 0.5), vfx="judgment_strike")


def roderic(c):
    K.in_last_stand(c, "Lionheart", 0.3, [("Armor", 8), ("AttackSpeed", 40)], dur=5, cooldown=40, vfx="crusade_aura")
    K.dash(c, "Q", "Cavalry Charge", (10, 1), (50, 25), (80, 40), (1.2, 0.2), vfx="crusade_charge")
    K.area_disable(c, "W", "Lion's Roar", "fear", (1.2, 0.2), radius=3.5, dmg=(40, 25), vfx="howl_wave")
    K.ally_buff(c, "E", "Rally", [("AttackSpeed", (20, 10)), ("MoveSpeedPct", (0.1, 0.03))], dur=6, vfx="crusade_aura")
    K.ult_rally(c, "Charge of the Lions", [("MoveSpeedPct", (0.25, 0.05)), ("BonusDamage", (30, 20))], radius=9, dur=(6, 1))


def pilgrim(c):
    K.in_aura(c, "Long Road", [("MoveSpeedPct", 0.06)], radius=9, flavor="Those who walk with him walk further: ")
    K.strike(c, "Q", "Walking Staff", (60, 35), status="stun", dur=0.8, vfx="wrath_bash")
    K.shield(c, "W", "Shelter", (90, 45), vfx="aegis_shell", cast_vfx="aegis_cast")
    K.heal(c, "E", "Pilgrim's Rest", (60, 30), radius=4, rng=8, hot=(4, 2), vfx="mend_pulse")
    K.ult_rally(c, "Sanctuary", [("DamageTakenPct", (-0.35, -0.1))], radius=8, dur=(4, 1), heal=(100, 50), vfx="aegis_shell",
                cast_vfx="consecrate_ground")


def deacon(c):
    K.in_mods(c, "Lamplight", "His lamp pushes back the night: +6 night vision.", [("VisionNight", 6)])
    K.debuff(c, "Q", "Lamp Flash", [("MoveSpeedPct", -0.1)], dur=3, radius=3, rng=9, dmg=(60, 30), vfx="dazzle_sparks", flags="Blinded")
    K.zone(c, "W", "Hallowed Lamp", (20, 10), radius=4, dur=6, slow=0, vfx="consecrate_ground",
           extra=[{"type": "ApplyStatus", "status": "revealed", "duration": 0.6}],
           flavor="True sight: invisible enemies inside are revealed. ")
    K.ally_buff(c, "E", "Guiding Light", [("MoveSpeedPct", (0.15, 0.05)), ("Evasion", (0.1, 0.03))], dur=5, vfx="dawn_fervor")
    K.ult_nuke(c, "Dawn Lantern", (220, 100), radius=5, delay=0.5, status="stun", dur=(1.0, 0.25), vfx="dawn_burst")


def isabeau(c):
    K.in_crit(c, "Sunlit", 0.12, 1.8, "The sun guides her throws: ")
    K.skillshot(c, "Q", "Sun Lance", (90, 45), width=0.9, rng=12, vfx="dawn_bolt", hit_vfx="dawn_burst_small")
    K.debuff(c, "W", "Blinding Flare", [("MoveSpeedPct", -0.1)], dur=3, radius=3, rng=9, vfx="dazzle_sparks", flags="Blinded")
    K.self_buff(c, "E", "Sunstride", [("MoveSpeedPct", (0.15, 0.05)), ("AttackSpeed", (25, 15))], dur=5, vfx="dawn_fervor")
    K.ult_bolt(c, "Noon Spear", (300, 150), status="stun", dur=(1, 0.25), vfx="judgment_strike")


def thorne(c):
    K.in_mods(c, "Fortified", "Built like a wall: +3 armor and +100 health.", [("Armor", 3), ("MaxHp", 100)])
    K.strike(c, "Q", "Rampart Bash", (70, 35), status="stun", dur=0.9, vfx="wrath_bash")
    K.taunt(c, "W", "Hold the Line", radius=4, dur=(1.5, 0.25), armor=(4, 2))
    K.self_buff(c, "E", "Bastion", [("DamageTakenPct", (-0.2, -0.05)), ("SlowResist", 0.3)], dur=5, vfx="aegis_shell")
    K.ult_rally(c, "Wall of Shields", [("Armor", (10, 5))], radius=8, dur=(6, 1), vfx="aegis_shell")


def emmerich(c):
    K.in_mods(c, "Tinkerer", "Clever gadgets: 8% cooldown reduction.", [("CooldownReduction", 0.08)])
    K.summon(c, "Q", "Holy Turret", dict(key="turret", name="Brightforge Turret", model="creep_dawn_ballista", hp=350, dmg=(22, 28),
                                         attack="Ranged", rng=6.5, ms=1.2, bat=1.5, armor=4, scale=0.8, tags=["mechanical"]),
             count=(1, 0), dur=30, vfx="dawn_burst_small", cd=(20, -1.5))
    K.point_aoe(c, "W", "Flash Grenade", (70, 35), radius=3, status="ministun", dur=0.5, vfx="dawn_burst")
    K.ally_buff(c, "E", "Brightforge Plating", [("Armor", (4, 2))], dur=8, vfx="aegis_shell")
    K.ult_army(c, "Siege of Light", dict(key="ballista", name="Holy Ballista", model="creep_dawn_ballista", hp=600, dmg=(40, 50),
                                         attack="Ranged", rng=7.5, ms=2.0, bat=1.8, armor=5, tags=["mechanical"]),
               count=(2, 1), dur=30, vfx="dawn_burst")


def solenne(c):
    blind = {"id": "solenne_radiant_arrows_blind", "name": "Dazzled", "isDebuff": True, "dispel": "Basic", "flags": "Blinded",
             "icon": "status_solenne_dazzled", "vfx": "dazzle_sparks"}
    K.in_on_hit(c, "Radiant Arrows", "Attacks have a 20% chance to dazzle the target for 2 s: half of its attacks miss.",
                [{"type": "ApplyStatus", "status": blind["id"], "duration": 2}], chance=0.2, statuses=[blind])
    K.skillshot(c, "Q", "Piercing Light", (80, 40), width=0.9, rng=12, vfx="dawn_bolt", hit_vfx="dawn_burst_small")
    K.debuff(c, "W", "Dazzle", [("MoveSpeedPct", -0.1)], dur=3, dmg=(40, 30), vfx="dazzle_sparks", flags="Blinded")
    K.blink(c, "E", "Radiant Step", (6, 1), vfx="dawn_burst_small")
    K.ult_storm(c, "Sunburst Volley", (70, 35), radius=5, dur=5, vfx="consecrate_motes", impact="dawn_burst")


def halbrecht(c):
    K.in_bash(c, "Sentence", 4, 50, 0.3, vfx="judgment_strike")
    K.strike(c, "Q", "Verdict", (90, 45), vfx="judgment_strike")
    K.debuff(c, "W", "Condemn", [("Armor", (-3, -1)), ("DamageTakenPct", (0.05, 0.03))], dur=6, vfx="judgment_glow")
    K.self_buff(c, "E", "Justicar's Stride", [("MoveSpeedPct", (0.12, 0.04)), ("StatusResist", (0.15, 0.05))], dur=5, vfx="crusade_aura")
    K.ult_execute(c, "Execution", (200, 100), (0.2, 0.05), rng=(3, 0), blink_behind=False, vfx="judgment_strike")


def ignatius(c):
    K.in_aura(c, "Scripture", [("MagicResist", -0.08)], radius=8, team="Enemy", flavor="Heretics tremble at his reading: ")
    K.area_disable(c, "Q", "Sermon", "silence", (1.5, 0.5), radius=3.5, dmg=(50, 30), vfx="silence_rune")
    K.bolt(c, "W", "Admonish", (80, 40), status="slow_heavy", dur=1.5, vfx="dawn_bolt", hit_vfx="judgment_strike")
    K.dot(c, "E", "Penance", (18, 12), dur=5, vfx="judgment_glow", status_vfx="consecrate_motes")
    K.ult_bolt(c, "Excommunication", (180, 90), status="silence", dur=(4, 1), vfx="judgment_strike")


def brynja(c):
    K.in_mods(c, "Shield Wall", "Behind her shield: +2 armor and 8% evasion.", [("Armor", 2), ("Evasion", 0.08)])
    K.dash(c, "Q", "Shield Bash", (5, 0.5), (20, 10), (60, 30), (1.0, 0.2), vfx="crusade_charge")
    K.bounce(c, "W", "Shield Throw", (60, 35), bounces=2, dtype="Physical", vfx="wrath_bash")
    K.shield(c, "E", "Brace", (100, 50), self_only=True, mods=[("Armor", (2, 1))], vfx="aegis_shell", cast_vfx="aegis_cast")
    K.ult_rally(c, "Valkyrie's Wall", [("Armor", (6, 3)), ("DamageTakenPct", (-0.15, -0.05))], radius=8, dur=(6, 1))


def ashgrove(c):
    K.in_spell_stacks(c, "Hallowed Ground", 0.03, 8, 5, vfx="consecrate_motes", flavor="Every rite hallows the ground further: ")
    K.bolt(c, "Q", "Exorcise", (90, 45), vfx="dawn_bolt", hit_vfx="dawn_burst_small", usage="nuke")
    K.purge(c, "W", "Purge Evil", slow_dur=3, dmg=(70, 35))
    K.zone(c, "E", "Holy Water", (25, 12), radius=3, dur=4, vfx="consecrate_ground")
    K.ult_bolt(c, "Banishment", (220, 110), status="stun", dur=(2, 0.5), vfx="dawn_burst", flavor="Casts the target out of this world, briefly. ")


def twins(c):
    K.in_lifesteal(c, "Twin Bond", 0.1, "The twins share every wound and every cure. ")
    K.bounce(c, "Q", "Twin Arrows", (60, 35), bounces=1, dtype="Physical", vfx="dawn_bolt", flavor="Each twin looses an arrow: ")
    K.blink(c, "W", "Mirror Step", (7, 1), vfx="dazzle_sparks")
    K.self_buff(c, "E", "Sibling Fury", [("AttackSpeed", (30, 15))], dur=5, vfx="dawn_fervor")
    K.ult_transform(c, "Blessed Union", [("AttackSpeed", (50, 25)), ("BonusDamage", (30, 20)), ("Evasion", (0.15, 0.05))], dur=(10, 1),
                    cast_vfx="dawn_burst")


def evangeline(c):
    K.in_aura(c, "Martyrdom", [("DamageTakenPct", -0.08)], radius=9, flavor="She takes a share of every blow: ")
    K.shield(c, "Q", "Sacrificial Ward", (100, 50), vfx="aegis_shell", cast_vfx="blood_offering")
    K.strike(c, "W", "Martyr's Blade", (70, 35), lifesteal=0.3, vfx="judgment_strike")
    K.heal(c, "E", "Blood Offering", (80, 40), hot=(3, 2), vfx="blood_offering")
    K.ult_rally(c, "Final Sacrifice", [("DamageTakenPct", (-0.25, -0.05))], radius=9, dur=(5, 1), heal=(250, 100), vfx="aegis_shell",
                cast_vfx="dawn_burst")


def lysander(c):
    K.in_bash(c, "Sunrise Combo", 3, 30, 0.1, vfx="dawn_burst_small")
    K.cone(c, "Q", "Dawn Slash", (70, 35), rng=3.5, angle=50, dtype="Physical", vfx="dawn_burst")
    K.dash(c, "W", "Solar Dash", (8, 1), (40, 20), (60, 30), (0.5, 0.1), vfx="crusade_charge")
    K.p_crit(c, "E", "Rising Sun", (0.12, 0.03), 1.8)
    K.ult_transform(c, "Break of Dawn", [("AttackSpeed", (60, 30)), ("BonusDamage", (30, 20)), ("MoveSpeedPct", 0.1)], dur=(10, 1),
                    cast_vfx="dawn_burst", vfx="dawn_fervor")


HEROES = [
    H("cassia", "Sister Cassia", "the Flame Confessor", DG, INT, R, ["Nuker"],
      "Sister Cassia hears the confessions of heretics and answers them with fire. She believes every flame she lights is "
      "a mercy, and she lights a great many.",
      "Fervent, gentle-voiced, unyielding.", cassia, look=dict(hood=True, weapon="staff", glow=(1.0, 0.6, 0.2))),
    H("brennock", "Brennock Vail", "the Stake Hunter", DG, AGI, R, ["Carry"],
      "Brennock has driven a stake through more vampires than any hunter living. He carries silver bolts, bear traps and "
      "a very long list.",
      "Laconic, weathered, grimly amused.", brennock, look=dict(weapon="crossbow", hat=True, hood=False, cape=True)),
    H("oswin", "Oswin", "the High Templar", DG, STR, M, ["Durable"],
      "Oswin commands the Templars who guard the Dawnguard's cathedral. He has held every line he was given, and he "
      "intends to hold this one.",
      "Stern, dutiful, protective.", oswin, difficulty=1, look=dict(weapon="sword", shield=True, helmet=True, crest=True, cape=True)),
    H("mercy", "Mercy Halloway", "the Battle Priest", DG, INT, R, ["Support"],
      "Mercy follows the crusade from battle to battle, healing soldiers where they fall. She has carried more wounded off "
      "the field than any knight has cut down.",
      "Brisk, compassionate, fearless.", mercy, difficulty=1, look=dict(hood=True, weapon="mace", collar=True)),
    H("gideon", "Gideon", "the Witchfinder", DG, AGI, R, ["Anti-mage"],
      "Gideon hunts sorcerers for the Dawnguard, and his blessed bolts unravel their spells mid-cast. He is feared by "
      "witches and trusted by no one.",
      "Suspicious, relentless, dry.", gideon, look=dict(weapon="crossbow", hat=True, hood=False, cloth=(0.15, 0.15, 0.18))),
    H("aurelia", "Aurelia", "the Seraph Commander", DG, STR, M, ["Initiator"],
      "Aurelia leads the winged knights of the dawn. When she dives from the sky the crusade charges after her, and "
      "they have never yet been turned back.",
      "Inspiring, commanding, radiant.", aurelia, look=dict(wings=True, halo=True, weapon="sword", helmet=False, long_hair=True,
                                                            hair=(0.9, 0.8, 0.5))),
    H("tobias", "Brother Tobias", "the Bellringer", DG, STR, M, ["Disabler"],
      "Brother Tobias rang the cathedral bell for forty years and grew arms like oak beams doing it. He carries a bell "
      "into battle now, and it rings very loudly.",
      "Jovial, deafening, devout.", tobias, look=dict(weapon="hammer", helmet=False, hood=True, hunch=6)),
    H("lucan", "Lucan Hale", "the Monster Slayer", DG, AGI, M, ["Carry"],
      "Lucan has slain wyrms, ghouls and a werewolf the size of a barn. He is paid by the head, and he is very rich.",
      "Swaggering, practical, brave.", lucan, look=dict(weapon="sword", cape=True, hood=False, helmet=False, hair=(0.4, 0.28, 0.18))),
    H("maerith", "Maerith", "the Inquisitor", DG, INT, R, ["Disabler"],
      "Maerith binds heretics with chains of pure faith and questions them until they confess. She has never needed "
      "a second session.",
      "Precise, cold, utterly certain.", maerith, look=dict(hood=True, collar=True, weapon="staff")),
    H("roderic", "Roderic", "the Lionheart", DG, STR, M, ["Initiator"],
      "Roderic led the crusade's cavalry until his horse fell under him. He now charges on foot, and nobody has noticed "
      "him slowing down.",
      "Brave, loud, generous.", roderic, look=dict(weapon="sword", shield=True, helmet=True, crest=True, cape=True, mane=True,
                                                   fur=(0.8, 0.6, 0.3))),
    H("pilgrim", "The Pilgrim", "", DG, STR, M, ["Support"],
      "The Pilgrim has walked every road in Velmoragh and sheltered everyone he met along the way. He carries nothing but "
      "his staff, and it has been enough.",
      "Humble, steady, kind.", pilgrim, difficulty=1, look=dict(weapon="club", hood=True, helmet=False, cape=True, pauldrons=False,
                                                                wood=(0.4, 0.3, 0.2))),
    H("deacon", "Deacon Vale", "the Lamplight", DG, INT, R, ["Support"],
      "Deacon Vale carries the lamp that lights the crusade's way through the ruins. Nothing hides from its light for long.",
      "Earnest, soft-spoken, brave in the dark.", deacon, look=dict(hood=False, collar=True, weapon="staff", glow=(1.0, 0.85, 0.5))),
    H("isabeau", "Isabeau", "the Sunlancer", DG, AGI, R, ["Carry"],
      "Isabeau throws lances of sunlight at noon and never misses. The crusade calls her the Noon Spear, and so do the "
      "vampires, very quietly.",
      "Bright, competitive, sure of herself.", isabeau, look=dict(weapon="bow", helmet=False, long_hair=True, hair=(0.95, 0.85, 0.5))),
    H("thorne", "Thorne", "the Wall Warden", DG, STR, M, ["Durable"],
      "Thorne builds walls for the Dawnguard and stands on them when they are finished. He is the wall's last stone, and "
      "the heaviest.",
      "Taciturn, solid, dependable.", thorne, difficulty=1, look=dict(weapon="hammer", shield=True, helmet=True, bulk=1.5)),
    H("emmerich", "Emmerich", "the Brightforge Artificer", DG, INT, R, ["Pusher"],
      "Emmerich builds holy siege engines in the Brightforge, powered by prayer and clockwork. Most of them work.",
      "Excitable, inventive, singed.", emmerich, look=dict(hat=False, hood=False, collar=True, weapon="hammer", hair=(0.6, 0.35, 0.15))),
    H("solenne", "Solenne", "the Radiant Archer", DG, AGI, R, ["Carry"],
      "Solenne's arrows are made of light, and they dazzle whoever they strike. She fights best at dawn, which the crusade "
      "considers only fitting.",
      "Graceful, proud, cheerful.", solenne, look=dict(weapon="bow", hood=False, halo=True, long_hair=True, hair=(0.9, 0.8, 0.55))),
    H("halbrecht", "Halbrecht", "the Justicar", DG, STR, M, ["Carry"],
      "Halbrecht carries out the Dawnguard's sentences. His blade is heavy, his strikes are final, and his mercy is "
      "reserved for the innocent.",
      "Grim, fair, unhurried.", halbrecht, look=dict(weapon="greatsword", helmet=True, cape=True)),
    H("ignatius", "Ignatius", "the Canon", DG, INT, R, ["Disabler"],
      "Canon Ignatius preaches sermons so long and so thunderous that heretics fall silent just to make them stop.",
      "Pompous, learned, tireless.", ignatius, look=dict(crown=True, weapon="staff", collar=True)),
    H("brynja", "Brynja", "the Shieldmaiden", DG, STR, M, ["Durable"],
      "Brynja came south from the fjords to join the crusade and brought her shield with her. She has thrown it at more "
      "enemies than she has swung her sword at.",
      "Boisterous, loyal, fearless.", brynja, look=dict(weapon="sword", shield=True, helmet=True, horns=True, long_hair=True,
                                                        hair=(0.85, 0.7, 0.4))),
    H("ashgrove", "Father Ashgrove", "the Exorcist", DG, INT, R, ["Nuker"],
      "Father Ashgrove has cast out demons, ghosts and one very persistent lich. He carries holy water, a heavy book and a "
      "heavier temper.",
      "Gruff, tired, formidable.", ashgrove, look=dict(hood=False, collar=True, weapon="staff", hair=(0.75, 0.72, 0.7), hunch=6)),
    H("twins", "Castia & Pollan", "the Blessed Twins", DG, AGI, R, ["Carry"],
      "Castia and Pollan were born holding hands and have never let go. The crusade treats them as one soldier; they fight "
      "as one, back to back, sharing every wound.",
      "Finish each other's sentences.", twins, difficulty=2, look=dict(weapon="bow", hood=False, cape=True, hair=(0.6, 0.45, 0.25))),
    H("evangeline", "Evangeline", "the Martyr", DG, STR, M, ["Support"],
      "Evangeline has died twice for the crusade and come back both times. She would do it again, and she makes sure "
      "nobody near her has to.",
      "Serene, selfless, unafraid.", evangeline, look=dict(weapon="sword", halo=True, helmet=False, long_hair=True, hair=(0.3, 0.2, 0.15))),
    H("lysander", "Lysander", "the Dawnbreaker", DG, AGI, M, ["Carry"],
      "Lysander fights in the style of the Dawnbreakers, strike after strike rising like the sun. By the third blow the "
      "fight is usually over.",
      "Energetic, confident, honourable.", lysander, look=dict(weapon="sword", cape=True, helmet=False, hair=(0.95, 0.8, 0.4))),
]
