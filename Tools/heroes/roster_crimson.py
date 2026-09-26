"""Crimson Court roster (#4-#24 of HERO_ROSTER.md): vampire nobility, blood knights, gargoyles, cursed assassins."""
import kitlib as K
from common import AGI, CC, INT, M, R, STR, H


def sereth(c):
    K.in_crit(c, "Crimson Flourish", 0.15, 1.7, "A duelist's eye for the opening: ")
    K.dash(c, "Q", "Fencer's Lunge", (6, 0.5), (20, 15), (50, 30), (0.3, 0.1), cd=(12, -1))
    K.self_buff(c, "W", "Parry Stance", [("Evasion", (0.3, 0.1)), ("Armor", (4, 2))], dur=3, vfx="barrier", cd=(16, -1.5),
                flavor="Sereth reads every blade. ")
    K.dot(c, "E", "Opened Vein", (15, 10), dur=5, dtype="Physical", cd=(10, -1), mana=(50, 5), flavor="A precise cut that will not close. ")
    K.ult_transform(c, "Crimson Waltz", [("AttackSpeed", (60, 30)), ("MoveSpeedPct", (0.15, 0.05)), ("Lifesteal", (0.15, 0.05))],
                    dur=(8, 1), cast_vfx="bloodlust", flavor="The duel becomes a dance. ")


def isolde(c):
    K.in_aura(c, "Overflowing Chalice", [("HpRegen", 2.5)], radius=9, flavor="The Chalice Queen's cup never runs dry: ")
    K.heal(c, "Q", "Chalice of Renewal", (90, 40), hot=(4, 3), flavor="A draught from the royal chalice. ")
    K.disable(c, "W", "Enthralling Toast", "hex", (1.5, 0.5), rng=7, vfx="hex_puff", flavor="Charmed into a harmless lapdog: ")
    K.ally_buff(c, "E", "Wine of the Court", [("BonusDamage", (10, 10)), ("AttackSpeed", (10, 10))], dur=8, vfx="blood_surge")
    K.heal(c, "R", "Grand Banquet", (200, 100), radius=6, rng=10, hot=(10, 5), hot_dur=5, flavor="The court feasts, and the wounded rise. ")


def grimbane(c):
    K.in_mods(c, "Granite Hide", "Carved from cathedral stone: +3 armor and +10% magic resistance.", [("Armor", 3), ("MagicResist", 0.1)])
    K.leap(c, "Q", "Perch Dive", (8, 1), (70, 40), radius=2.5, dur=(0.8, 0.2), flavor="Grimbane drops from on high. ")
    K.self_buff(c, "W", "Stone Form", [("DamageTakenPct", (-0.3, -0.08)), ("HpRegen", (10, 10))], dur=4, vfx="barkskin_glow",
                flavor="Turns to stone and weathers the storm. ")
    K.knockback(c, "E", "Wing Buffet", (60, 35), radius=3, dist=2.5, vfx="howl_wave")
    K.ult_mass(c, "Petrifying Gaze", "stun", dur=(1.75, 0.5), radius=6, dmg=(150, 75), vfx="grave_chill_cone",
               flavor="Every foe that meets its eyes turns briefly to stone. ")


def castor(c):
    K.in_spell_stacks(c, "Soul Lantern", 0.03, 10, 5, vfx="mana_ember", flavor="Every spell feeds the lantern: ")
    K.bounce(c, "Q", "Will-o'-Wisp", (70, 45), bounces=3, vfx="mana_ember", flavor="A grave-light that hops from soul to soul. ")
    K.point_aoe(c, "W", "Grave Light", (80, 40), radius=3, delay=0.6, status="slow_heavy", dur=2, vfx="consecrate_ground")
    K.hook(c, "E", "Lantern's Lure", (60, 30), rng=(9, 1), vfx="mana_ember", flavor="The lantern's light draws the dead and the living. ")
    K.ult_storm(c, "Procession of Souls", (60, 30), radius=5, dur=6, vfx="consecrate_motes", impact="dawn_burst",
                flavor="The dead walk in procession, freezing all they pass. ")


def veyla(c):
    K.in_attack_slow(c, "Poisoned Edge", 0.15, 2, "Her blades are dipped in nightshade. ")
    K.self_aoe(c, "Q", "Fan of Daggers", (65, 35), radius=3.5, status="slow_light", dur=1.5, dtype="Physical", vfx="thorn_strike")
    K.invis(c, "W", "Whisper Step", dur=(3, 0.75), bonus_dmg=(50, 40))
    K.dot(c, "E", "Venom Dagger", (18, 12), dur=6, slow=0.2, vfx="curse_hit", status_vfx="fumes_green")
    K.ult_execute(c, "Silent Death", (200, 100), (0.15, 0.05), flavor="There is no sound at all. ")


def kaelthorne(c):
    K.in_aura(c, "Baron's Tithe", [("Lifesteal", 0.08)], radius=9, flavor="Every blow pays the Baron's tithe: ")
    K.strike(c, "Q", "Iron Rebuke", (70, 40), status="stun", dur=0.8, vfx="wrath_bash")
    K.shield(c, "W", "Blood Armour", (80, 50), self_only=True, mods=[("Armor", (3, 2))], vfx="barrier", cast_vfx="blood_offering")
    K.self_aoe(c, "E", "Tithe Collector", (50, 30), radius=3.5, heal_pct=0.5, vfx="blood_boil", flavor="He collects what is owed. ")
    K.ult_transform(c, "Iron Dominion", [("Armor", (10, 5)), ("MaxHp", (300, 200)), ("StatusResist", (0.3, 0.1))], dur=(10, 1),
                    cast_vfx="wrath_bash", vfx="bloodbound_aura")


def morthaine(c):
    K.in_lifesteal(c, "Nocturnal Hunger", 0.12, "Every bite feeds the swarm. ")
    K.skillshot(c, "Q", "Bat Volley", (60, 35), rng=10, width=1.2, vfx="blood_lance", hit_vfx="bat_swarm_blink")
    K.blink(c, "W", "Scatter", (8, 0), vfx="bat_swarm_blink", usage="escape", flavor="Morthaine bursts into bats and reforms elsewhere. ")
    K.self_buff(c, "E", "Swarm Frenzy", [("AttackSpeed", (30, 15))], dur=5, vfx="bloodlust")
    K.ult_transform(c, "Become the Swarm", [("Evasion", (0.4, 0.1)), ("MoveSpeedPct", (0.25, 0.05)), ("Lifesteal", (0.2, 0.1))],
                    dur=(8, 1), cast_vfx="bat_swarm_blink", vfx="veil_shadow")


def ashvere(c):
    K.in_mana_burn(c, "Heretic's Brand", 20, "The Inquisitor's touch drains the faithless. ")
    K.disable(c, "Q", "Blood Chains", "root", (1.5, 0.25), dmg=(60, 40), vfx="root_chains")
    K.area_disable(c, "W", "Oath of Silence", "silence", (2, 0.5), radius=3, rng=9, dmg=(50, 30), vfx="silence_rune")
    K.debuff(c, "E", "Judgment Brand", [("MagicResist", (-0.1, -0.05))], dur=6, dmg=(40, 30), vfx="judgment_glow")
    K.ult_nuke(c, "Chains of the Condemned", (150, 75), radius=4.5, status="root", dur=(2, 0.5), vfx="root_eruption",
               flavor="Chains of congealed blood burst from the earth. ")


def dravenne(c):
    K.in_crit(c, "Moonless Night", 0.12, 1.8, "In darkness her bolts find the heart: ")
    K.bolt(c, "Q", "Barbed Bolt", (70, 40), status="slow_heavy", dur=1.5, dtype="Physical", vfx="blood_lance", hit_vfx="hit_physical",
           usage="nuke")
    K.zone(c, "W", "Caltrops", (20, 10), radius=2.5, dur=5, slow=0.4, vfx="bone_cage_chill", dtype="Physical")
    K.self_buff(c, "E", "Hunter's Focus", [("AttackRange", (1, 0.5)), ("AttackSpeed", (20, 15))], dur=6, vfx="velvet_dark_glow")
    K.ult_storm(c, "Blackout Volley", (70, 35), radius=4.5, dur=5, dtype="Physical", vfx="blood_rain_pool", impact="blood_rain_impact")


def seraphel(c):
    K.in_last_stand(c, "Fallen Grace", 0.3, [("Lifesteal", 0.3), ("AttackSpeed", 40)], dur=5, cooldown=40)
    K.blink(c, "Q", "Blood Wings", (7, 1), arrive_dmg=(40, 30), vfx="bat_swarm_blink")
    K.strike(c, "W", "Crimson Talon", (80, 40), status="disarm", dur=2.0, vfx="claw_impact", flavor="Tears the weapon from their grip. ")
    K.cone(c, "E", "Wingbeat", (60, 35), rng=5, angle=40, slow=(0.2, 0.05), dur=2, vfx="howl_wave")
    K.ult_leap(c, "Descent of the Fallen", (10, 1), (180, 100), radius=4, flavor="Seraphel falls from the sky once more. ")


def lysandre(c):
    K.in_thorns(c, "Mirror Shards", 25, "Strike the Bride and the glass cuts back: ")
    K.skillshot(c, "Q", "Shattering Glance", (80, 50), width=1.2, status="slow_light", vfx="dazzle_sparks", hit_vfx="dazzle_sparks")
    K.swap(c, "W", "Mirror Swap", (8, 1), vfx="dazzle_sparks", flavor="The reflection steps out; Lysandre steps in. ")
    K.shield(c, "E", "Silvered Veil", (70, 40), self_only=True, mods=[("MagicResist", (0.1, 0.05))], vfx="barrier", cast_vfx="dazzle_sparks")
    K.ult_nuke(c, "Shatter the Glass", (200, 100), radius=4, delay=0.8, status="silence", dur=(2, 0.5), vfx="dawn_burst",
               flavor="Every mirror breaks at once. ")


def hollowmere(c):
    K.in_mods(c, "Ossified Nave", "A walking cathedral: +3 health regeneration and +2 armor.", [("HpRegen", 3), ("Armor", 2)])
    K.self_aoe(c, "Q", "Bell Toll", (70, 40), radius=4, status="slow_heavy", dur=1.5, vfx="howl_wave")
    K.area_disable(c, "W", "Dread Peal", "fear", (1, 0.25), radius=3.5, dmg=(40, 20), vfx="fear_skull")
    K.zone(c, "E", "Choir of Ruin", (20, 10), radius=3, dur=5, slow=0, follow=True, vfx="curse_echo", flavor="The ruined choir sings around him. ")
    K.ult_mass(c, "Final Toll", "fear", dur=(2, 0.5), radius=6, dmg=(150, 75), vfx="fear_skull", flavor="The last bell rings. ")


def rhaegor(c):
    K.in_kill_stacks(c, "Blood-Iron Edge", "BonusDamage", 2, 20, dur=120, flavor="His blade drinks and grows. ")
    K.cone(c, "Q", "Overhead Cleave", (70, 40), rng=3.5, angle=45, dtype="Physical", vfx="blood_rend_sweep")
    K.debuff(c, "W", "Champion's Challenge", [("Armor", (-3, -1))], dur=6, vfx="crimson_mark_rose", flavor="Marks a worthy foe. ")
    K.self_buff(c, "E", "Iron Momentum", [("MoveSpeedPct", (0.15, 0.05)), ("BonusDamage", (15, 10))], dur=5, vfx="bloodlust")
    K.ult_transform(c, "Trial by Blood", [("BonusDamagePct", (0.3, 0.15)), ("Lifesteal", (0.15, 0.1)), ("StatusResist", (0.25, 0.1))],
                    dur=(10, 1), cast_vfx="blood_impact_heavy")


def vesper(c):
    K.in_aura(c, "Quiet Grace", [("ManaRegen", 1.0)], radius=9, flavor="Her silence restores the faithful: ")
    K.disable(c, "Q", "Hush", "silence", (2, 0.5), dmg=(60, 30), vfx="silence_rune")
    K.area_disable(c, "W", "Dirge", "slow_heavy", (2, 0.25), radius=3.5, rng=9, dmg=(60, 40), vfx="curse_echo")
    K.heal(c, "E", "Hymn of Mending", (60, 30), radius=4, rng=8, hot=(5, 3), flavor="A hymn that closes wounds. ")
    K.ult_mass(c, "Requiem", "silence", dur=(3, 0.75), radius=7, dmg=(120, 60), vfx="silence_rune")


def othniel(c):
    K.in_lifesteal(c, "Needlework", 0.1, "He stitches his own wounds with theirs. ")
    K.heal(c, "Q", "Stitch", (80, 40), rng=7, hot=(3, 2), flavor="A few quick stitches. ")
    K.disable(c, "W", "Suture Line", "root", (1.2, 0.3), dmg=(50, 30), vfx="root_chains", flavor="Sews the target to the ground. ")
    K.ally_buff(c, "E", "Graft", [("MaxHp", (100, 75)), ("HpRegen", (3, 2))], dur=10, vfx="mend_pulse", flavor="Borrowed flesh, freshly attached. ")
    K.ult_army(c, "Patchwork Abomination", dict(key="abomination", name="Patchwork Abomination", model="creep_dusk_crypthorror",
                                                 hp=900, dmg=(38, 48), armor=4, ms=3.3, scale=1.1, tags=["undead"]),
               count=(1, 1), dur=30, vfx="bone_raise")


def talon(c):
    K.in_mods(c, "Stone Skin", "Gargoyle hide: +2 armor and +10% status resistance.", [("Armor", 2), ("StatusResist", 0.1)])
    K.leap(c, "Q", "Diving Strike", (8, 1), (60, 35), radius=2, status="slow_heavy", dur=(1.5, 0), vfx="bat_swarm_blink", impact="claw_impact")
    K.bolt(c, "W", "Talon Shot", (80, 40), status="ministun", dur=0.4, dtype="Physical", vfx="bone_shard", hit_vfx="hit_physical")
    K.self_buff(c, "E", "Harrier", [("AttackSpeed", (25, 15)), ("MoveSpeedPct", (0.1, 0.05))], dur=5, vfx="velvet_dark_glow")
    K.ult_bolt(c, "Gargoyle's Descent", (250, 125), status="stun", dur=(1.5, 0.5), vfx="bloodfall_impact")


def corvina(c):
    K.in_spell_stacks(c, "Unkindness", 0.03, 8, 5, vfx="veil_shadow", flavor="Each whisper grows the flock: ")
    K.skillshot(c, "Q", "Raven Swarm", (70, 40), width=1.3, status="slow_light", vfx="bat_swarm_blink", hit_vfx="bat_swarm_blink")
    K.debuff(c, "W", "Whispered Secret", [("MagicResist", -0.15), ("MoveSpeedPct", -0.15)], dur=4, dmg=(40, 25), vfx="curse_mark")
    K.zone(c, "E", "Murder of Crows", (25, 15), radius=3, dur=4, vfx="veil_shadow", impact="bat_swarm_blink")
    K.ult_nuke(c, "Black Wings", (180, 90), radius=4.5, status="silence", dur=(2, 0.5), vfx="bat_swarm_blink")


def malachar(c):
    K.in_thorns(c, "Thornblood", 20, "Blood like brambles: ")
    K.dash(c, "Q", "Bramble Charge", (8, 1), (30, 20), (60, 40), (1.0, 0.2), vfx="crimson_charge_trail")
    K.self_aoe(c, "W", "Thorn Burst", (60, 35), radius=3, status="root", dur=1.0, vfx="thorn_strike")
    K.shield(c, "E", "Barbed Mail", (80, 40), self_only=True, mods=[("Armor", (2, 1))], vfx="barrier", cast_vfx="thorn_strike")
    K.ult_storm(c, "Crown of Thorns", (60, 30), radius=4, dur=6, follow=True, dtype="Physical", vfx="root_lash", impact="thorn_strike",
                flavor="Thorns lash out around him. ")


def elowen(c):
    K.in_evasion(c, "Foresight", 0.12, "She has already seen the blow: ")
    K.point_aoe(c, "Q", "Omen", (100, 55), radius=3, delay=1.2, status="stun", dur=1.0, vfx="blood_boil")
    K.shield(c, "W", "Blessed Fate", (90, 45), vfx="barrier", cast_vfx="dawn_burst_small")
    K.debuff(c, "E", "Hemomancy Reading", [("DamageTakenPct", (0.08, 0.04))], dur=5, vfx="crimson_mark_rose",
             flavor="Reads the target's blood and names its weakness. ")
    K.ult_nuke(c, "Prophecy of Ruin", (300, 125), radius=4, delay=1.5, status="stun", dur=(1.0, 0.25), vfx="bloodfall_impact")


def varric(c):
    K.in_lifesteal(c, "Cursed Blade", 0.15, "The blade feeds on whoever it cuts, its wielder included. ")
    K.strike(c, "Q", "Oathbreaker's Strike", (80, 45), lifesteal=0.5, vfx="blood_impact_heavy")
    K.debuff(c, "W", "Curse of the Oathless", [("Armor", (-3, -1)), ("HealAmp", (-0.2, -0.1))], dur=5, vfx="curse_mark")
    K.self_buff(c, "E", "Unbound", [("StatusResist", (0.2, 0.1)), ("MoveSpeedPct", (0.1, 0.05))], dur=4, dispel_self=True, vfx="bloodlust")
    K.ult_transform(c, "Soul-Drinker", [("Lifesteal", (0.3, 0.15)), ("BonusDamage", (40, 30))], dur=(10, 1), cast_vfx="blood_boil")


def nocturne(c):
    K.in_kill_stacks(c, "Heir's Blood", "Agi", 1, 30, dur=150, flavor="Royal blood awakens with every death he causes. ")
    K.blink(c, "Q", "Royal Lunge", (6, 1), arrive_dmg=(50, 30), radius=2, vfx="midnight_step")
    K.self_aoe(c, "W", "Crimson Flourish", (60, 35), radius=2.5, dtype="Physical", heal_pct=0.3, vfx="midnight_slash")
    K.self_buff(c, "E", "Blood Frenzy", [("AttackSpeed", (30, 15)), ("Lifesteal", (0.1, 0.05))], dur=5, vfx="bloodlust")
    K.ult_execute(c, "Birthright", (180, 90), (0.18, 0.04), flavor="The last heir claims what is his. ")


HEROES = [
    H("sereth", "Sereth Vane", "the Crimson Duelist", CC, AGI, M, ["Carry", "Escape"],
      "The finest blade of the old court, Sereth fought four hundred duels and never lost one he meant to win. He serves no "
      "throne now; he travels the ruins looking for the one opponent who can make him bleed.",
      "Courteous, bored, exacting. Salutes every opponent.", sereth, difficulty=2,
      look=dict(weapon="rapier", cape=True, hair=(0.12, 0.08, 0.1), long_hair=True, helmet=False)),
    H("isolde", "Isolde Marrow", "the Chalice Queen", CC, INT, R, ["Support", "Disabler"],
      "Isolde kept the court's great chalice, and with it the loyalty of every noble who drank. The chalice still overflows; "
      "she pours it for those who serve her and for those she means to charm.",
      "Warm, gracious, never without an agenda.", isolde, difficulty=1, look=dict(crown=True, hood=False, long_hair=True, hair=(0.2, 0.05, 0.08))),
    H("grimbane", "Grimbane", "the Gargoyle Sentinel", CC, STR, M, ["Durable", "Disabler"],
      "For nine centuries Grimbane watched the cathedral roof and never moved. When the Blood Moon cracked, the stone "
      "cracked with it, and the sentinel climbed down to guard its masters in person.",
      "Silent, patient, unmovable.", grimbane, difficulty=1,
      look=dict(skin=(0.55, 0.55, 0.58), armor=(0.42, 0.42, 0.45), stance="beast", hunch=12, horns=True, wings=True, helmet=False,
                weapon="club", cape=False, claws_hands=True, wood=(0.45, 0.45, 0.48))),
    H("castor", "Castor Dusk", "the Lamplighter of Graves", CC, INT, R, ["Nuker"],
      "Castor lit the lamps that guide the dead to the ossuary. One night he stopped guiding them and started keeping them, "
      "and now his lantern holds more souls than the crypt.",
      "Soft-spoken, kind to the dead, unkind to the living.", castor, look=dict(hood=True, weapon="staff", glow=(0.6, 0.85, 1.0))),
    H("veyla", "Veyla", "the Whispering Blade", CC, AGI, M, ["Assassin"],
      "Veyla was raised in the court's hidden school, where children learned to kill before they learned to read. "
      "She is the only graduate who ever left it alive, and she did not leave quietly.",
      "Quiet, precise, contemptuous of noise.", veyla, difficulty=3, look=dict(hood=True, weapon="sword", cape=True)),
    H("kaelthorne", "Kaelthorne", "the Iron Baron", CC, STR, M, ["Durable", "Initiator"],
      "The Baron's armour was forged from the blood-iron of his own veins. He collected the Court's tithes for three "
      "hundred years, and no village ever paid late twice.",
      "Blunt, dutiful, merciless in collection.", kaelthorne, look=dict(weapon="hammer", crest=True, spiked=True)),
    H("morthaine", "Morthaine", "the Swarm", CC, AGI, R, ["Carry", "Escape"],
      "Morthaine was a vampire lord who drank so deep of the Blood Moon that his body could no longer hold together. He is a "
      "thousand bats wearing the shape of a man, and each of them is hungry.",
      "Restless, chittering, many-voiced.", morthaine, difficulty=3,
      look=dict(weapon="crossbow", wings=True, hood=False, collar=True, skin=(0.8, 0.76, 0.8))),
    H("ashvere", "Ashvere", "the Renegade Inquisitor", CC, INT, R, ["Disabler"],
      "Ashvere hunted vampires for the Dawnguard until she learned what their blood could do. She kept the chains, "
      "changed the faith, and now binds her former brothers with the very sin she once burned.",
      "Severe, zealous, certain.", ashvere, look=dict(hood=True, collar=True, weapon="staff")),
    H("dravenne", "Dravenne", "the Moonless Huntress", CC, AGI, R, ["Carry"],
      "Dravenne hunts on nights without a moon, when even the Court's own creatures fear the dark. Her crossbow has "
      "brought down wyrms, werewolves and one very surprised saint.",
      "Dry, solitary, amused by prey.", dravenne, look=dict(weapon="crossbow", hood=True)),
    H("seraphel", "Seraphel", "the Fallen", CC, STR, M, ["Initiator"],
      "Once a seraph of the dawn, Seraphel fell into the Crimson Court's embrace and rose with wings of congealed blood. "
      "He still dives on his enemies like an avenging angel; he has only changed whose vengeance it is.",
      "Proud, wounded, magnificent.", seraphel, look=dict(wings=True, halo=True, glow=(1.0, 0.2, 0.2), helmet=False, weapon="sword",
                                                          hair=(0.8, 0.7, 0.5), long_hair=True)),
    H("lysandre", "Lysandre", "the Mirror Bride", CC, INT, R, ["Nuker", "Escape"],
      "Lysandre was married to the Mirror Prince in a hall of a thousand looking-glasses. When he betrayed her she stepped "
      "into the glass and has walked between reflections ever since.",
      "Melancholy, vain, unpredictable.", lysandre, difficulty=3, look=dict(tiara=True, long_hair=True, hair=(0.9, 0.88, 0.9), weapon="staff")),
    H("hollowmere", "Hollowmere", "the Cathedral Horror", CC, STR, M, ["Durable"],
      "When the cathedral of Velmoragh collapsed on its congregation, something rose from the rubble wearing its bells. "
      "Hollowmere tolls for the dead, and anyone who hears it runs.",
      "Mournful, enormous, slow to anger.", hollowmere, look=dict(height=2.3, bulk=1.5, hunch=10, weapon="hammer", helmet=True, horns=True,
                                                                  armor=(0.4, 0.38, 0.35), cape=True)),
    H("rhaegor", "Rhaegor", "the Blood-Iron Champion", CC, STR, M, ["Carry"],
      "Rhaegor fights in the Court's arena, where every victory is paid in blood-iron. His sword is made of the winnings: "
      "it grows heavier with every champion he defeats.",
      "Boastful, generous, loves a crowd.", rhaegor, look=dict(weapon="greatsword", spiked=True, cape=True)),
    H("vesper", "Vesper", "the Silent Chorister", CC, INT, R, ["Support", "Disabler"],
      "Vesper sang in the cathedral choir until the night her voice was taken as payment for a noble's debt. She found "
      "that silence, sung properly, can still a whole battlefield.",
      "Gentle, wordless, resolute.", vesper, difficulty=1, look=dict(hood=True, collar=True, weapon="staff", glow=(0.85, 0.85, 1.0))),
    H("othniel", "Othniel", "the Flesh Tailor", CC, INT, M, ["Support"],
      "Othniel sews the Court's broken soldiers back together, and some of his patients go home with more limbs than "
      "they came in with. His masterpiece walks behind him on a leash.",
      "Fussy, cheerful, deeply unsettling.", othniel, look=dict(robe=True, weapon="sword", collar=False, hood=False, hair=(0.3, 0.28, 0.25))),
    H("talon", "Talon", "the Spire Gargoyle", CC, AGI, R, ["Ganker"],
      "Talon nests on the highest spire in the ruins and hunts anything that crosses the plaza below. Younger and quicker "
      "than Grimbane, it prefers to strike from the sky and be gone before the dust settles.",
      "Territorial, gleeful, impatient.", talon, look=dict(skin=(0.5, 0.5, 0.54), wings=True, horns=True, stance="beast", hunch=14,
                                                           weapon="crossbow", claws_hands=True)),
    H("corvina", "Corvina", "the Raven Courtesan", CC, INT, R, ["Disabler"],
      "Corvina knows every secret in the Court because her ravens hear every whisper. She sells what she learns, and "
      "keeps the best secrets for the day she needs a noble to fall.",
      "Charming, sly, always listening.", corvina, look=dict(hood=False, collar=True, weapon="staff", cloth=(0.1, 0.08, 0.12), long_hair=True,
                                                             hair=(0.05, 0.05, 0.08))),
    H("malachar", "Malachar", "the Thornblood Knight", CC, STR, M, ["Initiator"],
      "Malachar's blood turned to thorns when he drank from the wrong chalice. It hurts him with every heartbeat, and it "
      "hurts anyone who strikes him far more.",
      "Grim, stoic, in constant pain.", malachar, look=dict(weapon="sword", shield=True, spiked=True, crest=True)),
    H("elowen", "Elowen Sable", "the Sanguine Oracle", CC, INT, R, ["Support"],
      "Elowen reads the future in spilled blood and has never once been wrong. She rarely tells anyone what she sees; "
      "she simply stands where the blow will not land.",
      "Serene, distant, faintly sad.", elowen, look=dict(hood=True, weapon="staff", glow=(1.0, 0.3, 0.4))),
    H("varric", "Varric Ashmoor", "the Oathless", CC, STR, M, ["Carry", "Durable"],
      "Varric swore oaths to three kings and broke all three. The blade he stole from the last one is cursed to drink from "
      "every hand that holds it, and he has learned to drink faster than it does.",
      "Cynical, charming, entirely untrustworthy.", varric, look=dict(weapon="greatsword", helmet=False, hair=(0.3, 0.25, 0.2), cape=True)),
    H("nocturne", "Nocturne", "the Last Heir", CC, AGI, M, ["Carry"],
      "Nocturne is the last child of the old royal line, raised in hiding by servants who died to keep the secret. "
      "Royal blood wakes in him slowly, and it wakes faster with every death he causes.",
      "Young, earnest, frightening when roused.", nocturne, look=dict(weapon="rapier", crown=True, cape=True, helmet=False,
                                                                      hair=(0.9, 0.85, 0.8))),
]
