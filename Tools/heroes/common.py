"""Shared names for the hero generator and the roster modules."""

STR, AGI, INT = "Strength", "Agility", "Intelligence"
M, R = "Melee", "Ranged"
CC, AL, WC, DG = "CrimsonCourt", "AshenLegion", "WildCovenant", "Dawnguard"


class H:
    """One hero: identity, stats and kit builder (called with a kitlib.Ctx)."""

    def __init__(self, stem, name, title, faction, attr, atk, roles, lore, personality, kit, look=None, stats=None,
                 difficulty=2, strengths=(), weaknesses=(), resource="Mana"):
        self.stem, self.name, self.title, self.faction = stem, name, title, faction
        self.attr, self.atk, self.roles, self.lore, self.personality = attr, atk, roles, lore, personality
        self.kit, self.look, self.stats, self.difficulty = kit, look or {}, stats or {}, difficulty
        self.strengths, self.weaknesses, self.resource = list(strengths), list(weaknesses), resource
