"""Ashen Legion roster (#2-#24 of HERO_ROSTER.md): necromancers, skeletal hosts, plague cults, corpse engines."""
import kitlib as K
from common import AGI, AL, INT, M, R, STR, H

BONE = (0.84, 0.8, 0.7)


def morrowmaw(c):
    K.in_feast(c, "Corpse Furnace", 0.02, 0.08, radius=8, vfx="corpse_consumed", flavor="Every corpse feeds the furnace: ")
    K.point_aoe(c, "Q", "Corpse Catapult", (80, 40), radius=3, rng=10, delay=0.5, vfx="death_bone", dtype="Physical",
                flavor="Hurls a bundle of the dead. ")
    K.zone(c, "W", "Grinding Wheels", (25, 15), radius=3, dur=5, follow=True, dtype="Physical", vfx="bone_cage_chill")
    K.p_mods(c, "E", "Siege Plating", [("Armor", (2, 1)), ("MaxHp", (75, 75))], flavor="Bolted-on plates of bone and iron. ")
    K.ult_transform(c, "Unstoppable Engine", [("MaxHp", (400, 200)), ("Armor", (8, 4)), ("StatusResist", (0.4, 0.1))], dur=(12, 1),
                    cast_vfx="crypt_open", vfx="bone_shard_orbit")


def pallor(c):
    K.in_aura(c, "Plague Carrier", [("HpRegen", -3)], radius=8, team="Enemy", flavor="Her miasma festers every wound: ",
              vfx="fumes_green")
    K.dot(c, "Q", "Contagion", (20, 10), dur=6, spread=3, vfx="fumes_green", status_vfx="fumes_green",
          flavor="The sickness leaps from body to body. ")
    K.zone(c, "W", "Pestilent Cloud", (30, 15), radius=3.5, dur=5, vfx="fumes_green")
    K.debuff(c, "E", "Fever", [("AttackSpeed", (-20, -10)), ("MoveSpeedPct", (-0.1, -0.05))], dur=5, radius=3, rng=9, vfx="fumes_green")
    K.ult_storm(c, "The Great Plague", (50, 25), radius=6, dur=8, vfx="fumes_green", impact="curse_hit",
                flavor="Sister Pallor opens her censer and the sky turns green. ")


def kessrith(c):
    K.in_crit(c, "Headshot", 0.1, 2.0, "The Archer King never misses a skull: ")
    K.cone(c, "Q", "Bone Volley", (60, 35), rng=7, angle=25, dtype="Physical", vfx="death_bone")
    K.skillshot(c, "W", "Piercing Shaft", (90, 50), rng=12, width=0.8, dtype="Physical", vfx="bone_arrow", hit_vfx="hit_bone")
    K.self_buff(c, "E", "King's Aim", [("AttackRange", (1.5, 0.5)), ("BonusDamage", (10, 10))], dur=6, vfx="grave_frost")
    K.ult_storm(c, "Rain of Bones", (60, 30), radius=5, dur=5, dtype="Physical", vfx="bone_cage_chill", impact="death_bone")


def vulgrim(c):
    K.in_mods(c, "Grave Soil", "Dirt under his nails and in his lungs: +2 health regeneration and +1 armor.", [("HpRegen", 2), ("Armor", 1)])
    K.cone(c, "Q", "Dirt Toss", (50, 30), rng=5, angle=35, slow=(0.2, 0.05), dur=2, vfx="death_bone")
    K.disable(c, "W", "Bury Alive", "stun", (1.2, 0.3), dmg=(60, 30), rng=6, vfx="root_burrow", flavor="Shovels an enemy into the ground. ")
    K.self_buff(c, "E", "Dig In", [("Armor", (3, 2)), ("HpRegen", (5, 5))], dur=6, vfx="barkskin_glow")
    K.ult_nuke(c, "Open Grave", (150, 100), radius=4, delay=0.5, status="stun", dur=(1.5, 0.25), vfx="crypt_open",
               flavor="The ground gives way beneath them. ")


def dren(c):
    K.in_mods(c, "Bone Plating", "Each plate stops a blade: +4 armor.", [("Armor", 4)])
    K.self_aoe(c, "Q", "Ossuary Slam", (60, 35), radius=3, status="slow_light", dur=1.5, vfx="death_bone", dtype="Physical")
    K.shield(c, "W", "Plate Shield", (80, 40), vfx="barrier", cast_vfx="bone_raise")
    K.p_mods(c, "E", "Marrow Harden", [("Armor", (1, 1)), ("MagicResist", (0.04, 0.03))])
    K.ult_transform(c, "Knight of the Ossuary", [("DamageTakenPct", (-0.25, -0.1)), ("BonusDamage", (30, 20))], dur=(8, 1),
                    cast_vfx="bone_raise", vfx="bone_shard_orbit")


def choir(c):
    K.in_aura(c, "Chorus", [("HpRegen", 2), ("ManaRegen", 0.5)], radius=9, flavor="The dead sing for the living: ")
    K.heal(c, "Q", "Lament", (70, 30), radius=4, rng=9, flavor="A mournful verse that knits flesh. ")
    K.area_disable(c, "W", "Discord", "silence", (1.5, 0.5), radius=3, rng=9, dmg=(40, 25), vfx="silence_rune")
    K.ally_buff(c, "E", "Spectral Harmony", [("MoveSpeedPct", (0.15, 0.05)), ("Armor", (2, 1))], dur=6, vfx="moon_glow")
    K.ult_mass(c, "Wail of the Choir", "fear", dur=(1.5, 0.5), radius=6.5, dmg=(140, 70), vfx="fear_skull")


def rotfang(c):
    K.in_feast(c, "Carrion Feast", 0.03, 0.12, radius=6, flavor="Rotfang eats well on a battlefield: ")
    K.leap(c, "Q", "Pounce", (6, 1), (60, 30), radius=1.5, status="slow_heavy", dur=(1.5, 0), vfx="pounce_leap", impact="claw_impact")
    K.strike(c, "W", "Rending Bite", (60, 30), lifesteal=0.6, vfx="claw_impact")
    K.self_buff(c, "E", "Pack Instinct", [("AttackSpeed", (20, 15)), ("MoveSpeedPct", (0.1, 0.05))], dur=6, vfx="howl_fury")
    K.ult_transform(c, "Frenzied Gorge", [("Lifesteal", (0.35, 0.15)), ("AttackSpeed", (50, 25))], dur=(10, 1), cast_vfx="howl_wave")


def varuun(c):
    K.in_last_stand(c, "Phylactery", 0.25, [("DamageTakenPct", -0.5)], dur=3, cooldown=60, vfx="grave_frost",
                    flavor="His soul is elsewhere: ")
    K.skillshot(c, "Q", "Frost Lance", (80, 45), status="slow_heavy", dur=2, vfx="bone_lance", hit_vfx="grave_frost")
    K.disable(c, "W", "Frozen Grasp", "root", (1.2, 0.3), dmg=(60, 40), vfx="grave_frost")
    K.p_mods(c, "E", "Arcane Mastery", [("SpellAmp", (0.05, 0.04)), ("ManaRegen", (1, 0.5))])
    K.ult_storm(c, "Eternal Winter", (70, 35), radius=5.5, dur=6, slow=0.4, vfx="bone_cage_chill", impact="grave_chill_cone")


def yselde(c):
    K.in_spell_stacks(c, "Smouldering", 0.04, 8, 4, vfx="mana_ember", flavor="Her fire builds with every spell: ")
    K.bolt(c, "Q", "Cinder Bolt", (90, 45), vfx="blood_lance", hit_vfx="hit_crit", usage="nuke")
    K.debuff(c, "W", "Choking Ash", [("OutgoingDamagePct", (-0.1, -0.05))], dur=4, radius=3, rng=9, vfx="brew_steam", flags="Blinded")
    K.blink(c, "E", "Firestorm Step", (7, 0), arrive_dmg=(50, 30), vfx="shadowstep")
    K.ult_storm(c, "Ashen Storm", (65, 30), radius=5, dur=6, vfx="brew_steam", impact="structure_collapse")


def grimsoul(c):
    K.in_kill_stacks(c, "Tithe of Souls", "BonusDamage", 3, 15, dur=120, flavor="Every soul reaped sharpens the scythe. ")
    K.cone(c, "Q", "Reaping Arc", (70, 40), rng=3.5, angle=60, dtype="Physical", vfx="blood_rend_sweep")
    K.strike(c, "W", "Harvest", (60, 40), lifesteal=0.4, status="slow_heavy", dur=1.5, vfx="midnight_slash")
    K.self_buff(c, "E", "Soul Price", [("BonusDamagePct", (0.1, 0.05)), ("MoveSpeedPct", (0.08, 0.04))], dur=6, vfx="bloodthirst_aura")
    K.ult_execute(c, "Final Tithe", (150, 75), (0.2, 0.05), rng=(4, 0.5), blink_behind=False, flavor="The reaper collects. ")


def ulgar(c):
    K.in_mods(c, "Master Bonewright", "A lifetime of building with the dead: +6 intelligence.", [("Int", 6)])
    K.summon(c, "Q", "Bone Archers", dict(key="bone_archer", name="Bone Archer", model="creep_dusk_bonearcher", hp=260, dmg=(18, 24),
                                          attack="Ranged", rng=5.5, ms=3.3, bat=1.4, tags=["undead"]),
             count=(2, 0), dur=25, vfx="bone_raise", cd=(26, -2))
    K.point_aoe(c, "W", "Splinter Burst", (70, 35), radius=3, delay=0.3, dtype="Physical", vfx="death_bone")
    K.ally_buff(c, "E", "Reinforce", [("Armor", (3, 2)), ("HpRegen", (4, 3))], dur=8, vfx="bone_shard_orbit")
    K.ult_army(c, "Ossuary Bastion", dict(key="bone_colossus", name="Bone Colossus", model="summon_pale_revenant", hp=1000, dmg=(40, 52),
                                          armor=5, ms=3.2, scale=1.25, tags=["undead"]),
               count=(2, 1), dur=25)


def hekra(c):
    K.in_on_hit(c, "Plague Blades", "Attacks deal 15 bonus magical damage.",
                [{"type": "Damage", "amount": 15, "damageType": "Magical", "vfx": "fumes_green"}], flavor="Her blades never quite heal clean. ")
    K.dash(c, "Q", "Scourge Dash", (7, 1), (40, 20), (60, 30), (0.5, 0.1), vfx="veil_shadow")
    K.dot(c, "W", "Festering Wound", (20, 12), dur=5, slow=0.15, vfx="fumes_green", status_vfx="fumes_green")
    K.invis(c, "E", "Plague Veil", dur=(2.5, 0.5), vfx="fumes_green", status_vfx="veil_shadow")
    K.ult_execute(c, "Death Mark", (170, 85), (0.15, 0.05))


def emissary(c):
    K.in_aura(c, "Dread Presence", [("Armor", -2)], radius=8, team="Enemy", flavor="Courage fails near it: ")
    K.disable(c, "Q", "Terrify", "fear", (1.2, 0.3), dmg=(60, 35), vfx="fear_skull")
    K.skillshot(c, "W", "Pale Beam", (80, 40), status="slow_heavy", dur=1.5, vfx="moon_glow", hit_vfx="grave_frost")
    K.debuff(c, "E", "Whispers of the Grave", [("MagicResist", (-0.1, -0.05)), ("MoveSpeedPct", -0.1)], dur=5, vfx="curse_mark")
    K.ult_mass(c, "Terror Incarnate", "fear", dur=(2.5, 0.5), radius=7, dmg=(120, 60), vfx="fear_skull")


def cindermaw(c):
    K.in_thorns(c, "Pyre Heart", 20, "Its body is a bonfire: ")
    K.dash(c, "Q", "Pyre Charge", (8, 1), (40, 20), (70, 35), (0.9, 0.2), vfx="crimson_charge_trail")
    K.zone(c, "W", "Immolation", (30, 15), radius=3, dur=6, follow=True, slow=0, vfx="hemorrhage_pool")
    K.p_mods(c, "E", "Corpse Fuel", [("HpRegen", (2, 2)), ("MaxHp", (50, 50))])
    K.ult_mass(c, "Colossus Stomp", "stun", dur=(1.5, 0.25), radius=5.5, dmg=(180, 90), vfx="bloodfall_impact")


def moth(c):
    K.in_evasion(c, "Dust of Wings", 0.1, "Moth-dust clouds every blow: ")
    K.dot(c, "Q", "Moth Swarm", (15, 10), dur=5, spread=2.5, vfx="dazzle_sparks", status_vfx="dazzle_sparks")
    K.disable(c, "W", "Slumber Dust", "stun", (1.5, 0.5), rng=7, vfx="dazzle_sparks", flavor="Puts an enemy into a heavy doze: ")
    K.shield(c, "E", "Cocoon", (100, 50), dur=5, vfx="barrier", cast_vfx="dazzle_sparks", mods=[("HpRegen", (3, 2))])
    K.ult_storm(c, "Night of Moths", (45, 25), radius=6, dur=7, slow=0.4, vfx="moon_glow", impact="dazzle_sparks")


def solace(c):
    K.in_aura(c, "Warden's Vigil", [("Armor", 2)], radius=9, flavor="The Deathwarden stands between the living and the grave: ")
    K.shield(c, "Q", "Deathward", (90, 45), vfx="barrier", cast_vfx="judgment_glow")
    K.strike(c, "W", "Grave Rebuke", (70, 35), status="stun", dur=1.0, vfx="judgment_strike")
    K.heal(c, "E", "Last Rites", (80, 40), hot=(3, 2))
    K.ult_cannot_die(c, "Not Today", dur=(3, 0.75), radius=10, flavor="Death is told to wait. ")


def vezmorah(c):
    K.in_thorns(c, "Carapace", 18, "Chitin spikes: ")
    K.skillshot(c, "Q", "Impale", (80, 45), rng=10, width=1.3, speed=18, status="stun", dur=1.0, vfx="root_eruption", hit_vfx="root_eruption")
    K.blink(c, "W", "Burrow", (8, 1), arrive_dmg=(60, 30), vfx="root_burrow")
    K.summon(c, "E", "Chitin Swarm", dict(key="beetle", name="Crypt Beetle", model="neutral_crypt_rat", hp=200, dmg=(12, 16), ms=3.8,
                                          bat=1.0, tags=["beast"]), count=(1, 1), dur=20, vfx="root_burrow", cd=(24, -2))
    K.ult_nuke(c, "Crypt Collapse", (200, 100), radius=5, status="stun", dur=(1.5, 0.5), delay=0.4, vfx="crypt_open")


def ilveth(c):
    K.in_spell_stacks(c, "Skull Familiar", 0.03, 8, 5, vfx="bone_shard_orbit", flavor="The skull whispers spells to her: ")
    K.cone(c, "Q", "Skull Shriek", (80, 40), rng=6, angle=30, status="silence", dur=1.0, vfx="howl_wave")
    K.bolt(c, "W", "Bone Bolt", (90, 50), status="ministun", dur=0.4, vfx="bone_bolt", hit_vfx="hit_bone")
    K.p_mods(c, "E", "Familiar's Secret", [("ManaRegen", (1, 0.75)), ("CooldownReduction", (0.04, 0.02))])
    K.ult_nuke(c, "Chorus of Skulls", (250, 100), radius=3.5, delay=0, status="silence", dur=(2.5, 0.5), vfx="fear_skull")


def wraith(c):
    K.in_evasion(c, "Incorporeal", 0.15, "Blades pass through it: ")
    K.self_buff(c, "Q", "Phase Walk", [("MoveSpeedPct", (0.2, 0.05))], dur=3, flags="Phased", vfx="veil_shadow", usage="escape,buff",
                flavor="Walks through the living. ")
    K.strike(c, "W", "Wraith Touch", (70, 35), dtype="Magical", status="slow_heavy", dur=1.5, vfx="veil_shadow_burst")
    K.blink(c, "E", "Spectral Reach", (8, 1), vfx="shadowstep")
    K.ult_transform(c, "Haunting", [("Evasion", (0.3, 0.1)), ("AttackSpeed", (40, 20))], dur=(8, 1), flags="Phased",
                    cast_vfx="veil_shadow_burst", vfx="veil_shadow")


def ossian(c):
    K.in_mods(c, "Preserved", "Embalmed long ago: +2 health regeneration and +10% status resistance.", [("HpRegen", 2), ("StatusResist", 0.1)])
    K.disable(c, "Q", "Embalming Wrap", "root", (1.5, 0.3), dmg=(50, 30), vfx="root_chains")
    K.zone(c, "W", "Natron Cloud", (20, 15), radius=3, dur=5, slow=0.4, vfx="brew_steam")
    K.debuff(c, "E", "Canopic Seal", [("HealAmp", (-0.25, -0.1)), ("HpRegen", (-3, -2))], dur=6, vfx="curse_mark")
    K.ult_bolt(c, "Mummify", (180, 90), status="stun", dur=(2.5, 0.5), vfx="root_chains")


def colossus(c):
    K.in_mods(c, "Chained", "Slow but unstoppable: +150 health and +15% status resistance.", [("MaxHp", 150), ("StatusResist", 0.15)])
    K.hook(c, "Q", "Chain Hook", (70, 40), rng=(9, 1))
    K.self_aoe(c, "W", "Chain Sweep", (60, 35), radius=3.5, status="slow_heavy", dur=1.5, dtype="Physical", vfx="blood_rend_sweep")
    K.self_buff(c, "E", "Anchor", [("Armor", (4, 2)), ("SlowResist", (0.2, 0.1))], dur=5, vfx="barrier")
    K.ult_mass(c, "Drag to the Deep", "root", dur=(2, 0.5), radius=6, dmg=(150, 75), vfx="root_chains")


def blightrunner(c):
    K.in_mods(c, "Blight Trail", "Always running: +8% movement speed.", [("MoveSpeedPct", 0.08)])
    K.zone(c, "Q", "Plague Trail", (20, 10), radius=2.5, dur=5, follow=True, vfx="fumes_green")
    K.dash(c, "W", "Blight Dash", (8, 1), (40, 20), (50, 25), (0.4, 0.1), vfx="veil_shadow")
    K.dot(c, "E", "Contaminate", (20, 10), dur=5, slow=0.2, vfx="fumes_green", status_vfx="fumes_green")
    K.ult_transform(c, "Epidemic Sprint", [("MoveSpeedPct", (0.3, 0.1)), ("Evasion", (0.2, 0.1))], dur=(8, 1), cast_vfx="fumes_green")


def sepulchra(c):
    K.in_aura(c, "Queen of Tombs", [("SpellAmp", 0.05)], radius=9, heroes_only=True, flavor="Her court of the dead lends its power: ")
    K.bolt(c, "Q", "Sarcophagus Bolt", (85, 45), vfx="bone_bolt", hit_vfx="curse_hit", usage="nuke")
    K.point_aoe(c, "W", "Tomb Dust", (70, 40), radius=3, delay=0.5, status="slow_heavy", dur=2, vfx="brew_steam")
    K.p_mods(c, "E", "Grave Goods", [("SpellAmp", (0.04, 0.03)), ("MaxMana", (75, 75))])
    K.ult_nuke(c, "Crypt Prison", (150, 100), radius=3.5, status="root", dur=(3, 0.5), delay=0.3, vfx="bone_cage_wall",
               flavor="A crypt rises around them. ")


HEROES = [
    H("morrowmaw", "Morrowmaw", "the Corpse Engine", AL, STR, M, ["Durable", "Pusher"],
      "The Legion's engineers built Morrowmaw from the bodies of an entire garrison and fuelled it with more. It remembers "
      "nothing of the soldiers inside it, only the hunger for more fuel.",
      "Grinding, relentless, speaks in a chorus of creaks.", morrowmaw, difficulty=1,
      look=dict(height=2.35, bulk=1.6, hunch=14, skin=BONE, weapon="club", helmet=True, spiked=True, wood=(0.4, 0.38, 0.36))),
    H("pallor", "Sister Pallor", "the Plague Mother", AL, INT, R, ["Nuker"],
      "Sister Pallor tended the sick in the Legion's hospices until she decided the sickness was the cure. Her censer "
      "spreads a plague she calls mercy.",
      "Tender, smiling, relentless.", pallor, look=dict(hood=True, weapon="staff", cloth=(0.28, 0.32, 0.2), glow=(0.7, 1.0, 0.3))),
    H("kessrith", "Kessrith", "the Bone Archer King", AL, AGI, R, ["Carry"],
      "Kessrith commanded the royal archers of a kingdom that no longer exists. He still leads them, a thousand skeletons "
      "loosing arrows of their own bones.",
      "Regal, precise, faintly nostalgic.", kessrith, look=dict(skeletal=True, skin=BONE, crown=True, weapon="bow", cape=True)),
    H("vulgrim", "Vulgrim", "the Gravedigger", AL, STR, M, ["Disabler"],
      "Vulgrim has dug more graves than any man alive, and he has started filling them himself. His shovel has never "
      "needed sharpening.",
      "Gruff, practical, oddly polite.", vulgrim, difficulty=1, look=dict(weapon="shovel", helmet=False, hood=True, hunch=10, cape=False)),
    H("dren", "Dren", "the Ossuary Knight", AL, STR, M, ["Durable"],
      "Dren guards the Ossuary of the First Legion, where a thousand knights lie in state. When he fell, they gave him "
      "their bones as armour, plate by plate.",
      "Steadfast, formal, proud of his charge.", dren, look=dict(weapon="sword", shield=True, armor=BONE, helmet=True, crest=True)),
    H("choir", "The Hollow Choir", "", AL, INT, R, ["Support"],
      "Twelve ghosts who died singing in the Legion's chapel, bound into one shape by their unfinished hymn. They heal "
      "the Legion's soldiers and wail at its enemies.",
      "Many-voiced, sorrowful, harmonious.", choir, difficulty=1,
      look=dict(hood=True, weapon="staff", skin=(0.7, 0.8, 0.85), cloth=(0.35, 0.4, 0.48), glow=(0.6, 0.9, 1.0))),
    H("rotfang", "Rotfang", "the Carrion Hound", AL, AGI, M, ["Jungler"],
      "Rotfang was the Legion's war-hound until it ate its handler. Now it runs free across the battlefield, growing "
      "fatter and stronger on every corpse.",
      "Ravenous, loyal to no one, oddly playful.", rotfang, look=dict(stance="beast", hunch=22, muzzle=True, ears=True, mane=True,
                                                                      claws_hands=True, skin=(0.42, 0.36, 0.3), weapon=None,
                                                                      head_scale=1.3, height=2.1, bulk=1.2, fur=(0.36, 0.3, 0.24))),
    H("varuun", "Varuun", "the Lich-Magister", AL, INT, R, ["Carry (caster)"],
      "Varuun chaired the Legion's college of necromancy for six centuries and hid his soul in a phylactery none of his "
      "students ever found. His winters are long, and he is patient.",
      "Academic, icy, condescending.", varuun, difficulty=2, look=dict(skeletal=True, skin=BONE, crown=True, weapon="staff",
                                                                       glow=(0.5, 0.8, 1.0))),
    H("yselde", "Yselde", "the Ashcaller", AL, INT, R, ["Nuker"],
      "Yselde burns the Legion's dead so their ashes can be called back as storms. She has burned a great many.",
      "Wry, restless, smells of smoke.", yselde, look=dict(hood=True, weapon="staff", glow=(1.0, 0.5, 0.15))),
    H("grimsoul", "Grimsoul", "the Reaper of Tithes", AL, STR, M, ["Carry"],
      "Grimsoul collects the souls the Legion is owed. Its scythe keeps count, and the count is never finished.",
      "Patient, formal, keeps a ledger.", grimsoul, look=dict(skeletal=True, skin=BONE, hood=True, weapon="scythe", helmet=False, cape=True)),
    H("ulgar", "Ulgar", "the Bonewright", AL, INT, R, ["Pusher"],
      "Ulgar builds with bones the way a mason builds with stone: archers, walls, whole bastions. He considers the living "
      "a waste of good material.",
      "Busy, fussy, proud of his work.", ulgar, look=dict(hood=False, crown=False, collar=True, weapon="staff", hair=(0.5, 0.5, 0.48))),
    H("hekra", "Hekra", "the Scourgeborn", AL, AGI, M, ["Assassin"],
      "Hekra was born in a plague pit and never caught the plague, because the plague caught her first. Her blades carry "
      "it to whoever she is paid to visit.",
      "Mocking, feral, quick.", hekra, difficulty=3, look=dict(weapon="sword", hood=True, cloth=(0.24, 0.3, 0.16), glow=(0.6, 1.0, 0.3))),
    H("emissary", "The Pale Emissary", "", AL, INT, R, ["Disabler"],
      "The Pale Emissary speaks for the Legion at every parley, and no one has ever refused its terms twice. It has no "
      "face beneath its hood, only the dread of everyone who looks.",
      "Cold, formal, terrifying.", emissary, look=dict(hood=True, skin=(0.9, 0.92, 0.95), cloth=(0.8, 0.82, 0.84), weapon="staff",
                                                       glow=(0.85, 0.9, 1.0))),
    H("cindermaw", "Cindermaw", "the Pyre Colossus", AL, STR, M, ["Initiator"],
      "Cindermaw is a funeral pyre that learned to walk. It strides into battle already burning and leaves only ash.",
      "Roaring, hungry, wordless.", cindermaw, look=dict(height=2.4, bulk=1.55, skin=(0.3, 0.2, 0.18), glow=(1.0, 0.45, 0.1), horns=True,
                                                         weapon="club", helmet=False, hunch=10, wood=(0.25, 0.18, 0.15))),
    H("moth", "Mother Moth", "", AL, INT, R, ["Support"],
      "Mother Moth lives in the Legion's lantern-lit catacombs, where her children flutter in clouds that put intruders to "
      "sleep. She tends the Legion's wounded like a grandmother.",
      "Doting, dusty, gently menacing.", moth, difficulty=1, look=dict(hood=True, wings=True, hunch=8, weapon="staff", cloth=(0.45, 0.4, 0.32))),
    H("solace", "Solace", "the Deathwarden", AL, STR, M, ["Support", "Durable"],
      "Solace guards the threshold between life and death and decides who may cross. For the Legion's soldiers the answer "
      "is: not yet.",
      "Calm, merciful, immovable.", solace, look=dict(weapon="mace", shield=True, halo=True, glow=(0.6, 1.0, 0.8), helmet=True)),
    H("vezmorah", "Vezmorah", "the Crypt Lord", AL, STR, M, ["Durable", "Initiator"],
      "Vezmorah ruled the insect crypts beneath the Legion's capital until the necromancers woke him. He burrows through "
      "stone as easily as through the ranks of his enemies.",
      "Clicking, imperious, ancient.", vezmorah, difficulty=2,
      look=dict(stance="beast", hunch=18, skin=(0.25, 0.28, 0.22), armor=(0.2, 0.24, 0.18), horns=True, claws_hands=True, weapon=None,
                spiked=True, height=2.2, bulk=1.4)),
    H("ilveth", "Ilveth", "the Skullspeaker", AL, INT, R, ["Nuker"],
      "Ilveth carries the skull of her old master, who never stops talking. Most of what it says is complaint; some of it "
      "is very powerful magic.",
      "Exasperated, clever, talks back to the skull.", ilveth, look=dict(hood=False, collar=True, weapon="staff", long_hair=True,
                                                                         hair=(0.2, 0.22, 0.2))),
    H("wraith", "Wraith of Eldermoor", "", AL, AGI, M, ["Escape"],
      "Eldermoor was a village swallowed by the Legion's first plague. Its last survivor refused to leave, even after "
      "death, and now it walks through walls and soldiers alike.",
      "Mournful, elusive, lost.", wraith, difficulty=3, look=dict(hood=True, skin=(0.7, 0.8, 0.85), cloth=(0.3, 0.35, 0.42), weapon="sword",
                                                                  glow=(0.6, 0.9, 1.0))),
    H("ossian", "Ossian", "the Grand Embalmer", AL, INT, R, ["Disabler"],
      "Ossian preserved the Legion's kings for burial and preserved himself while he was at it. His wraps hold the dead "
      "together and the living still.",
      "Meticulous, dry, fond of old rituals.", ossian, look=dict(skin=(0.78, 0.72, 0.58), cloth=(0.7, 0.64, 0.5), hood=True, weapon="staff")),
    H("colossus", "The Chained Colossus", "", AL, STR, M, ["Durable"],
      "The Colossus was chained beneath the Legion's fortress as punishment for a crime nobody remembers. The chains came "
      "with it when it broke free, and it has learned to use them.",
      "Silent, slow, enormous.", colossus, look=dict(height=2.5, bulk=1.65, hunch=12, skin=(0.5, 0.48, 0.46), weapon="club",
                                                     helmet=False, wood=(0.35, 0.34, 0.33))),
    H("blightrunner", "Blightrunner", "", AL, AGI, M, ["Escape", "Ganker"],
      "Blightrunner carries the Legion's plagues from battlefield to battlefield faster than any horse. Everything it runs "
      "past sickens.",
      "Twitchy, giggling, never still.", blightrunner, look=dict(hood=True, weapon="sword", hunch=10, cloth=(0.3, 0.35, 0.18),
                                                                 glow=(0.7, 1.0, 0.25))),
    H("sepulchra", "Sepulchra", "the Tomb Queen", AL, INT, R, ["Nuker"],
      "Sepulchra rules the great tombs beneath the Legion's lands, a queen of the dead with a court of mummified nobles. "
      "Those who trespass in her domain are given a crypt of their own.",
      "Regal, cold, gracious to her own.", sepulchra, look=dict(crown=True, weapon="staff", long_hair=True, hair=(0.1, 0.1, 0.12),
                                                               cloth=(0.24, 0.2, 0.26))),
]
