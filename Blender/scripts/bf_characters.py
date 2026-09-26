"""Character specs: every biped model key in the game data, described for bf_humanoid.

Keys match the `model` fields in Shared/Runtime/Resources/GameData. Colours are sRGB. Team-specific keys (creeps) bake
their team colours; shared keys (heroes) carry a `bf_team` armband that Unity tints per team.
"""

BONE = (0.84, 0.8, 0.7)
DAWN = dict(armor=(0.74, 0.72, 0.66), accent=(0.86, 0.66, 0.26), glow=(1.0, 0.78, 0.35), skin=(0.74, 0.6, 0.5),
            tabard=(0.88, 0.82, 0.64), cloth=(0.55, 0.45, 0.3), trim=(0.86, 0.66, 0.26))
DUSK_BONE = dict(skin=(0.82, 0.78, 0.68), armor=(0.36, 0.3, 0.25), accent=(0.55, 0.08, 0.1), glow=(1.0, 0.14, 0.12),
                 cloth=(0.22, 0.08, 0.09), skeletal=True, hunch=8)

SPECS = {
    # ------------------------------------------------------------------ heroes
    "hero_vorak": dict(
        height=2.05, bulk=1.3, stance="heavy", skin=(0.8, 0.76, 0.74), armor=(0.2, 0.16, 0.18), cloth=(0.42, 0.03, 0.06),
        accent=(0.7, 0.12, 0.14), trim=(0.78, 0.6, 0.3), glow=(1.0, 0.12, 0.14), helmet=True, horns=True, cape=True, spiked=True,
        weapon="greatsword", gauntlets=True, sleeve=(0.24, 0.05, 0.07), legs=(0.16, 0.12, 0.13), tabard=(0.5, 0.04, 0.07), team_band=True),
    "hero_ilyra": dict(
        height=1.8, bulk=0.85, stance="caster", skin=(0.88, 0.8, 0.78), cloth=(0.4, 0.03, 0.09), accent=(0.78, 0.6, 0.36),
        glow=(1.0, 0.15, 0.2), robe=True, hood=True, collar=True, weapon="staff", wood=(0.22, 0.08, 0.08), team_band=True,
        hair=(0.12, 0.03, 0.04), long_hair=True),
    "hero_nyxara": dict(
        height=1.85, bulk=0.82, stance="agile", skin=(0.92, 0.86, 0.88), armor=(0.2, 0.08, 0.14), torso=(0.55, 0.06, 0.15),
        cuirass=False, cloth=(0.48, 0.04, 0.13), accent=(0.9, 0.7, 0.72), trim=(0.9, 0.7, 0.72), glow=(1.0, 0.15, 0.35),
        cape=True, cape_color=(0.42, 0.03, 0.1), collar=True, tiara=True, hair=(0.1, 0.05, 0.09), long_hair=True,
        weapon="rapier", steel=(0.85, 0.85, 0.9), sleeve=(0.2, 0.06, 0.12), legs=(0.16, 0.06, 0.1), boots=(0.14, 0.05, 0.09), greaves=False,
        belt=(0.85, 0.66, 0.4),
        pauldrons=False, team_band=True),
    "hero_malgrave": dict(
        height=1.95, bulk=0.95, stance="caster", skin=BONE, cloth=(0.2, 0.22, 0.18), accent=(0.34, 0.55, 0.44), glow=(0.5, 1.0, 0.4),
        robe=True, crown=True, crown_color=BONE, collar=True, weapon="scythe", team_band=True, hunch=6),
    "hero_ardyn": dict(
        height=2.0, bulk=1.25, stance="heavy", skin=(0.8, 0.66, 0.55), armor=(0.86, 0.82, 0.74), cloth=(0.88, 0.84, 0.7),
        accent=(0.95, 0.75, 0.35), trim=(0.95, 0.75, 0.35), glow=(1.0, 0.86, 0.45), helmet=True, crest=True, cape=True,
        cape_color=(0.92, 0.88, 0.76), shield=True, halo=True, weapon="mace", tabard=(0.93, 0.9, 0.8), sleeve=(0.8, 0.76, 0.66),
        legs=(0.7, 0.66, 0.58), team_band=True),
    "hero_fenrax": dict(
        height=1.9, bulk=1.05, stance="agile", skin=(0.72, 0.6, 0.5), armor=(0.32, 0.25, 0.19), torso=(0.3, 0.24, 0.18),
        cuirass=False, cloth=(0.36, 0.3, 0.26), fur=(0.46, 0.43, 0.41), accent=(0.76, 0.8, 0.86), glow=(0.6, 0.8, 1.0),
        mane=True, hair=(0.2, 0.18, 0.18), claws_hands=True, pauldrons=False, greaves=False, boots=(0.26, 0.2, 0.16),
        sleeve=(0.72, 0.6, 0.5), team_band=True),
    "hero_fenrax_moonfang": dict(
        height=2.45, bulk=1.55, stance="beast", hunch=22, head_scale=1.35, belly=(0.48, 0.47, 0.5), skin=(0.25, 0.24, 0.27),
        torso=(0.27, 0.26, 0.29), cuirass=False,
        cloth=(0.3, 0.3, 0.33), fur=(0.33, 0.33, 0.36), accent=(0.76, 0.8, 0.86), glow=(0.6, 0.85, 1.0), muzzle=True, ears=True,
        mane=True, claws_hands=True, pauldrons=False, greaves=False, skirt=False, boots=(0.22, 0.21, 0.24), legs=(0.26, 0.25, 0.28),
        sleeve=(0.26, 0.25, 0.28), belt=(0.2, 0.16, 0.12), team_band=True),
    "hero_morwen": dict(
        height=1.72, bulk=0.85, stance="caster", skin=(0.78, 0.7, 0.6), cloth=(0.19, 0.27, 0.15), accent=(0.62, 0.52, 0.3),
        glow=(0.6, 1.0, 0.3), robe=True, hat=True, hair=(0.58, 0.58, 0.55), long_hair=True, weapon="staff", wood=(0.3, 0.22, 0.14),
        team_band=True, hunch=5),
    "hero_thael": dict(
        height=2.2, bulk=1.45, stance="heavy", skin=(0.38, 0.28, 0.2), armor=(0.3, 0.22, 0.15), armor_mat="bf_matte",
        cloth=(0.2, 0.32, 0.16), fur=(0.22, 0.36, 0.16), accent=(0.46, 0.36, 0.22), trim=(0.3, 0.45, 0.2), glow=(0.55, 1.0, 0.45),
        antlers=True, mane=True, leaves=(0.24, 0.4, 0.16), weapon="club", wood=(0.32, 0.23, 0.15), greaves=False, sleeve=(0.38, 0.28, 0.2),
        legs=(0.3, 0.22, 0.15), boots=(0.26, 0.19, 0.13), team_band=True),

    # ------------------------------------------------------------------ lane creeps
    "creep_dawn_footman": dict(height=1.65, bulk=0.95, weapon="sword", helmet=True, shield=True, **DAWN),
    "creep_dawn_footman_elite": dict(height=1.85, bulk=1.15, weapon="sword", helmet=True, crest=True, shield=True, cape=True,
                                     cape_color=(0.86, 0.8, 0.64), **DAWN),
    "creep_dawn_arbalist": dict(height=1.62, bulk=0.9, weapon="crossbow", helmet=True, stance="heavy", **DAWN),
    "creep_dawn_arbalist_elite": dict(height=1.8, bulk=1.05, weapon="crossbow", helmet=True, cape=True, cape_color=(0.86, 0.8, 0.64), **DAWN),
    "creep_dawn_paladin": dict(height=2.1, bulk=1.35, weapon="hammer", helmet=True, crest=True, shield=True, cape=True, halo=True,
                               cape_color=(0.9, 0.86, 0.72), **DAWN),
    "creep_dusk_thrall": dict(height=1.6, bulk=0.9, weapon="sword", steel=(0.45, 0.4, 0.36), **DUSK_BONE),
    "creep_dusk_thrall_elite": dict(height=1.8, bulk=1.1, weapon="sword", helmet=True, horns=True, shield=True, steel=(0.45, 0.4, 0.36),
                                    **DUSK_BONE),
    "creep_dusk_bonearcher": dict(height=1.62, bulk=0.9, weapon="bow", hood=True, **DUSK_BONE),
    "creep_dusk_bonearcher_elite": dict(height=1.8, bulk=1.05, weapon="bow", hood=True, cape=True, cape_color=(0.3, 0.05, 0.07), **DUSK_BONE),
    "creep_dusk_crypthorror": dict(height=2.3, bulk=1.6, hunch=25, stance="beast", skin=(0.42, 0.44, 0.4), armor=(0.2, 0.16, 0.16),
                                   cloth=(0.25, 0.08, 0.08), accent=(0.6, 0.1, 0.1), glow=(1.0, 0.14, 0.12), horns=True, claws_hands=True,
                                   spiked=True, cuirass=False, torso=(0.4, 0.42, 0.38), sleeve=(0.42, 0.44, 0.4), legs=(0.38, 0.4, 0.36)),

    # ------------------------------------------------------------------ hero summons
    "summon_skeleton_legionnaire": dict(height=1.75, bulk=0.95, weapon="sword", helmet=True, shield=True, skin=BONE, armor=(0.36, 0.31, 0.25),
                                        accent=(0.3, 0.5, 0.4), glow=(0.5, 1.0, 0.4), cloth=(0.2, 0.2, 0.18), skeletal=True, hunch=6),
    "summon_pale_revenant": dict(height=2.1, bulk=1.3, weapon="greatsword", helmet=True, crest=True, cape=True, skin=(0.6, 0.7, 0.72),
                                 armor=(0.7, 0.75, 0.78), cloth=(0.25, 0.3, 0.32), accent=(0.5, 0.6, 0.62), glow=(0.6, 1.0, 0.95),
                                 cape_color=(0.3, 0.38, 0.42), sleeve=(0.55, 0.62, 0.66), legs=(0.4, 0.46, 0.5)),
    "summon_treant": dict(height=2.8, bulk=1.7, hunch=18, stance="beast", skin=(0.34, 0.25, 0.17), torso=(0.3, 0.22, 0.15), cuirass=False,
                          cloth=(0.22, 0.36, 0.16), fur=(0.22, 0.4, 0.15), accent=(0.4, 0.3, 0.2), glow=(0.55, 1.0, 0.45), antlers=True,
                          claws_hands=True, pauldrons=False, greaves=False, skirt=False, sleeve=(0.34, 0.25, 0.17),
                          leaves=(0.24, 0.42, 0.16), bark_ridges=True, head_scale=1.2,
                          legs=(0.3, 0.22, 0.15), boots=(0.26, 0.19, 0.13), belt=(0.22, 0.36, 0.16)),

    # ------------------------------------------------------------------ neutrals
    "neutral_gravekeeper": dict(height=2.3, bulk=1.5, hunch=20, weapon="shovel", robe=True, hood=True, skin=(0.5, 0.53, 0.46),
                                cloth=(0.18, 0.16, 0.14), accent=(0.3, 0.3, 0.28), glow=(0.5, 0.95, 0.45), stance="heavy"),
    "neutral_grave_ghoul": dict(height=1.6, bulk=0.9, hunch=28, stance="beast", skin=(0.55, 0.6, 0.5), cuirass=False, torso=(0.5, 0.55, 0.46),
                                cloth=(0.24, 0.22, 0.2), accent=(0.3, 0.28, 0.26), glow=(0.5, 0.95, 0.45), ears=True, claws_hands=True,
                                pauldrons=False, greaves=False, sleeve=(0.55, 0.6, 0.5), legs=(0.5, 0.55, 0.46), boots=(0.45, 0.5, 0.42)),

    # ------------------------------------------------------------------ Vharoth
    "boss_vharoth": dict(height=6.6, bulk=1.7, hunch=15, stance="beast", skin=(0.36, 0.1, 0.11), armor=(0.14, 0.11, 0.13),
                         cloth=(0.38, 0.03, 0.06), accent=(0.84, 0.78, 0.66), trim=(0.84, 0.78, 0.66), glow=(1.0, 0.16, 0.12),
                         horns=True, crown=True, crown_color=(0.84, 0.78, 0.66), collar=True, mane=True, fur=(0.3, 0.05, 0.07),
                         claws_hands=True, spiked=True, sleeve=(0.36, 0.1, 0.11), legs=(0.3, 0.08, 0.09), tabard=(0.4, 0.03, 0.06),
                         head_scale=1.3, belly=(0.5, 0.14, 0.14)),
}


# ================================================================================================ winged bipeds
SPECS.update({
    "neutral_blood_gargoyle": dict(height=2.2, bulk=1.4, hunch=25, stance="beast", skin=(0.36, 0.34, 0.33), torso=(0.36, 0.34, 0.33),
                                   cuirass=False, cloth=(0.3, 0.28, 0.27), accent=(0.5, 0.1, 0.1), glow=(1.0, 0.25, 0.18), horns=True,
                                   wings=True, wing_color=(0.3, 0.28, 0.27), claws_hands=True, pauldrons=False, greaves=False,
                                   skirt=False, sleeve=(0.36, 0.34, 0.33), legs=(0.34, 0.32, 0.31), boots=(0.3, 0.28, 0.27),
                                   belly=(0.46, 0.44, 0.42), armor_mat="bf_matte"),
    "neutral_gargoyle_whelp": dict(height=1.3, bulk=1.1, hunch=25, stance="beast", skin=(0.4, 0.38, 0.36), torso=(0.4, 0.38, 0.36),
                                   cuirass=False, cloth=(0.32, 0.3, 0.29), accent=(0.5, 0.1, 0.1), glow=(1.0, 0.3, 0.2), horns=True,
                                   wings=True, wing_color=(0.33, 0.31, 0.3), claws_hands=True, pauldrons=False, greaves=False,
                                   skirt=False, sleeve=(0.4, 0.38, 0.36), legs=(0.38, 0.36, 0.34), boots=(0.33, 0.31, 0.3),
                                   head_scale=1.25, armor_mat="bf_matte"),
})

# ================================================================================================ RTS factions (War of the Ancients)
CRIMSON_U = dict(armor=(0.2, 0.12, 0.14), accent=(0.78, 0.58, 0.28), glow=(1.0, 0.15, 0.2), skin=(0.86, 0.78, 0.76),
                 tabard=(0.5, 0.04, 0.08), cloth=(0.38, 0.03, 0.07), trim=(0.8, 0.6, 0.28), sleeve=(0.24, 0.05, 0.07),
                 legs=(0.16, 0.1, 0.11))
WILD_U = dict(skin=(0.72, 0.6, 0.5), armor=(0.32, 0.25, 0.19), armor_mat="bf_matte", cloth=(0.22, 0.32, 0.16), fur=(0.4, 0.34, 0.26),
              accent=(0.46, 0.36, 0.22), trim=(0.62, 0.68, 0.76), glow=(0.6, 0.85, 1.0), leaves=(0.24, 0.4, 0.16),
              sleeve=(0.36, 0.28, 0.2), legs=(0.3, 0.24, 0.17), boots=(0.26, 0.19, 0.13))
SPECS.update({
    # Crimson Court
    "rts_cc_serf": dict(height=1.55, bulk=0.85, weapon="shovel", hood=True, cuirass=False, torso=(0.35, 0.18, 0.16),
                        cloth=(0.3, 0.06, 0.08), skin=(0.8, 0.7, 0.66), accent=(0.5, 0.1, 0.12), glow=(1.0, 0.15, 0.2), hunch=6,
                        pauldrons=False, greaves=False, sleeve=(0.3, 0.14, 0.13), legs=(0.22, 0.12, 0.11)),
    "rts_cc_duelist": dict(CRIMSON_U, height=1.78, bulk=0.9, stance="agile", weapon="rapier", cape=True, cape_color=(0.42, 0.03, 0.08),
                           collar=True, steel=(0.85, 0.85, 0.9)),
    "rts_cc_marksman": dict(CRIMSON_U, height=1.7, bulk=0.9, weapon="crossbow", hood=True, stance="heavy", cape=True,
                            cape_color=(0.3, 0.03, 0.06)),
    "rts_cc_blood_knight": dict(CRIMSON_U, height=2.1, bulk=1.35, stance="heavy", weapon="greatsword", helmet=True, horns=True,
                                spiked=True, cape=True, cape_color=(0.4, 0.03, 0.07), gauntlets=True, armor=(0.16, 0.1, 0.12)),
    # Wild Covenant
    "rts_wc_tender": dict(WILD_U, height=1.5, bulk=0.8, weapon="staff", robe=True, hood=True, wood=(0.32, 0.23, 0.15), hunch=6,
                          cloth=(0.28, 0.36, 0.2)),
    "rts_wc_shifter": dict(WILD_U, height=1.8, bulk=1.0, stance="agile", claws_hands=True, cuirass=False, torso=(0.36, 0.28, 0.2),
                           mane=True, hair=(0.2, 0.16, 0.12), pauldrons=False, greaves=False),
    "rts_wc_shifter_wolf": dict(height=2.2, bulk=1.4, stance="beast", hunch=22, head_scale=1.35, skin=(0.3, 0.24, 0.18),
                                torso=(0.32, 0.25, 0.19), cuirass=False, cloth=(0.3, 0.26, 0.2), fur=(0.36, 0.29, 0.21),
                                accent=(0.62, 0.68, 0.76), glow=(0.6, 0.85, 1.0), muzzle=True, ears=True, mane=True, claws_hands=True,
                                pauldrons=False, greaves=False, skirt=False, boots=(0.26, 0.2, 0.15), legs=(0.3, 0.24, 0.18),
                                sleeve=(0.3, 0.24, 0.18), belt=(0.2, 0.16, 0.12), belly=(0.5, 0.44, 0.36)),
    "rts_wc_thornshot": dict(WILD_U, height=1.72, bulk=0.88, weapon="bow", hood=True, cape=True, cape_color=(0.22, 0.34, 0.16)),
    "rts_wc_werebear": dict(height=2.6, bulk=1.85, stance="beast", hunch=24, head_scale=1.3, skin=(0.3, 0.22, 0.15),
                            torso=(0.33, 0.24, 0.16), cuirass=False, cloth=(0.28, 0.22, 0.16), fur=(0.36, 0.26, 0.17),
                            accent=(0.62, 0.68, 0.76), glow=(0.6, 0.85, 1.0), muzzle=True, ears=True, mane=True, claws_hands=True,
                            armor=(0.3, 0.24, 0.18), armor_mat="bf_matte", greaves=False, skirt=False,
                            boots=(0.26, 0.19, 0.13), legs=(0.32, 0.23, 0.15), sleeve=(0.33, 0.24, 0.16), belly=(0.46, 0.38, 0.3)),
})

# ================================================================================================ four-legged (bf_beasts)
BEAST_SPECS = {
    "neutral_crypt_rat": dict(height=0.42, length=0.8, width=0.1, head="rat", body=(0.32, 0.27, 0.25), belly=(0.48, 0.4, 0.36),
                              tail=0.7, tail_r=0.025, glow=(0.5, 0.95, 0.45), neck_rise=0.05, head_len=0.2, leg_r=0.035, girth=1.1),
    "neutral_bone_hound": dict(height=0.85, length=1.3, width=0.16, head="hound", skeletal=True, body=(0.82, 0.78, 0.68),
                               glow=(0.4, 1.0, 0.5), spikes=True, spike_color=(0.7, 0.66, 0.56), tail=0.6, neck_rise=0.18, head_len=0.3),
    "summon_spirit_wolf": dict(height=0.9, length=1.35, width=0.17, head="wolf", body=(0.56, 0.7, 0.86), belly=(0.78, 0.87, 0.96),
                               glow=(0.75, 0.92, 1.0), bushy=True, mane=True, mane_color=(0.7, 0.82, 0.95), tail=0.7, tail_r=0.07,
                               neck_rise=0.2, head_len=0.32),
    "hex_toad": dict(height=0.22, length=0.45, width=0.1, head="toad", body=(0.3, 0.42, 0.18), belly=(0.62, 0.66, 0.36),
                     glow=(0.95, 0.85, 0.3), tail=0.0, neck_rise=0.02, head_len=0.12, leg_r=0.03, girth=1.3, claws=False),
    # Blood War courier: a big blood bat that carries the stash to its hero.
    "courier_blood_bat": dict(height=0.34, length=0.72, width=0.18, head="rat", body=(0.28, 0.08, 0.1), belly=(0.46, 0.2, 0.2),
                              ear_color=(0.34, 0.1, 0.12), glow=(1.0, 0.78, 0.35), wings=1.45, membrane=(0.5, 0.06, 0.1), flying=True,
                              tail=0.14, neck_rise=0.08, head_len=0.18, leg_r=0.03, claws=False, girth=1.15),
    "hex_bat": dict(height=0.28, length=0.36, width=0.07, head="rat", body=(0.15, 0.1, 0.12), belly=(0.24, 0.16, 0.18),
                    ear_color=(0.2, 0.12, 0.14), glow=(1.0, 0.2, 0.2), wings=0.62, membrane=(0.22, 0.12, 0.14), flying=True,
                    tail=0.08, neck_rise=0.04, head_len=0.08, leg_r=0.015, claws=False),
    "neutral_wyrmling": dict(height=0.8, length=1.6, width=0.18, head="dragon", body=(0.26, 0.21, 0.3), belly=(0.44, 0.34, 0.44),
                             glow=(0.65, 0.35, 1.0), horn=(0.75, 0.66, 0.84), spikes=True, wings=1.1, membrane=(0.36, 0.22, 0.44),
                             tail=1.2, tail_r=0.07, tail_blade=True, neck_rise=0.35, neck_len=0.15, head_len=0.34),
    "neutral_nightwyrm": dict(height=1.6, length=3.2, width=0.36, head="dragon", body=(0.13, 0.11, 0.17), belly=(0.3, 0.22, 0.36),
                              glow=(0.65, 0.35, 1.0), horn=(0.72, 0.62, 0.84), spikes=True, wings=2.6, membrane=(0.24, 0.12, 0.32),
                              tail=2.6, tail_r=0.14, tail_blade=True, neck_rise=0.7, neck_len=0.35, head_len=0.6, girth=1.1),
}

# The generated roster heroes (Tools/heroes/generate_heroes.py).
try:
    from bf_roster_specs import ROSTER_SPECS
    SPECS.update(ROSTER_SPECS)
except ImportError:
    pass
