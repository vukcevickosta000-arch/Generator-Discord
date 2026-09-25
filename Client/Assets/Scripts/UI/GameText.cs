using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Client.UI
{
    /// <summary>Human-readable text for game data (stats, costs, abilities) used by tooltips and database pages.</summary>
    public static class GameText
    {
        private static readonly HashSet<StatType> Percent = new HashSet<StatType>
        {
            StatType.BonusDamagePct, StatType.MoveSpeedPct, StatType.Lifesteal, StatType.SpellVamp, StatType.Evasion, StatType.CritChance,
            StatType.CooldownReduction, StatType.StatusResist, StatType.SpellAmp, StatType.DamageTakenPct, StatType.OutgoingDamagePct,
            StatType.HealAmp, StatType.ManaCostReduction, StatType.HpRegenPct, StatType.SlowResist, StatType.MaxHpPct, StatType.ArmorPct,
            StatType.HealthCostReduction, StatType.MagicResist, StatType.ManaRegenPct,
        };

        public static string StatName(StatType s) => s switch
        {
            StatType.MaxHp => "Health",
            StatType.HpRegen => "Health Regeneration",
            StatType.MaxMana => "Mana",
            StatType.ManaRegen => "Mana Regeneration",
            StatType.Armor => "Armor",
            StatType.MagicResist => "Magic Resistance",
            StatType.BaseDamage => "Base Damage",
            StatType.BonusDamage => "Damage",
            StatType.BonusDamagePct => "Damage",
            StatType.AttackSpeed => "Attack Speed",
            StatType.AttackRange => "Attack Range (m)",
            StatType.MoveSpeed => "Movement Speed (m/s)",
            StatType.MoveSpeedPct => "Movement Speed",
            StatType.CastRange => "Cast Range (m)",
            StatType.Str => "Strength",
            StatType.Agi => "Agility",
            StatType.Int => "Intelligence",
            StatType.AllAttributes => "All Attributes",
            StatType.Lifesteal => "Lifesteal",
            StatType.SpellVamp => "Spell Lifesteal",
            StatType.Evasion => "Evasion",
            StatType.CritChance => "Critical Strike Chance",
            StatType.CritMultiplier => "Critical Damage (x)",
            StatType.CooldownReduction => "Cooldown Reduction",
            StatType.StatusResist => "Status Resistance",
            StatType.SpellAmp => "Spell Amplification",
            StatType.DamageTakenPct => "Damage Taken",
            StatType.OutgoingDamagePct => "Damage Dealt",
            StatType.HealAmp => "Healing Amplification",
            StatType.HpRegenPct => "Max Health Regeneration",
            StatType.ManaRegenPct => "Mana Regeneration",
            StatType.VisionDay => "Day Vision (m)",
            StatType.VisionNight => "Night Vision (m)",
            _ => El.Pretty(s.ToString()),
        };

        public static string Modifier(ModifierDef m, int level = 1)
        {
            float v = m.Value.Get(level);
            if (Percent.Contains(m.Stat)) return $"{(v >= 0 ? "+" : "")}{v * 100:0.#}% {StatName(m.Stat)}";
            return $"{(v >= 0 ? "+" : "")}{v:0.##} {StatName(m.Stat)}";
        }

        public static string Modifiers(IEnumerable<ModifierDef> mods, int level = 1)
        {
            if (mods == null) return "";
            return string.Join("\n", mods.Select(m => Modifier(m, level)));
        }

        public static string Values(LeveledValue v, string suffix = "")
        {
            if (v == null || v.Values == null || v.Values.Length == 0) return "-";
            return string.Join(" / ", v.Values.Select(x => x.ToString("0.##") + suffix));
        }

        public static string AbilityStats(AbilityDef a)
        {
            var sb = new StringBuilder();
            if (a.Targeting != TargetingMode.Passive)
            {
                if (!a.Cooldown.IsZero) sb.AppendLine("Cooldown: " + Values(a.Cooldown, "s"));
                if (!a.ManaCost.IsZero) sb.AppendLine("Mana: " + Values(a.ManaCost));
                if (!a.HealthCost.IsZero) sb.AppendLine("Health cost: " + Values(a.HealthCost));
                if (!a.CastRange.IsZero) sb.AppendLine("Range: " + Values(a.CastRange, " m"));
                if (a.AoeRadius != null && !a.AoeRadius.IsZero) sb.AppendLine("Radius: " + Values(a.AoeRadius, " m"));
                if (a.ChannelTime != null && !a.ChannelTime.IsZero) sb.AppendLine("Channel: " + Values(a.ChannelTime, "s"));
            }
            sb.Append(a.Targeting == TargetingMode.Passive ? "Passive" : Targeting(a.Targeting));
            if (a.PiercesMagicImmunity) sb.Append(" · Pierces magic immunity");
            return sb.ToString();
        }

        public static string Targeting(TargetingMode m) => m switch
        {
            TargetingMode.NoTarget => "No target",
            TargetingMode.Unit => "Unit target",
            TargetingMode.Point => "Point target",
            TargetingMode.UnitOrPoint => "Unit or point target",
            TargetingMode.Vector => "Vector target",
            TargetingMode.Toggle => "Toggle",
            _ => "Passive",
        };

        public static string Slot(AbilitySlot s) => s switch
        {
            AbilitySlot.Innate => "Innate",
            AbilitySlot.R => "Ultimate",
            _ => s.ToString(),
        };

        public static string ItemIconPath(ItemDef d) => "Textures/Icons/Items/" + (d.Icon ?? d.Id);
        public static string AbilityIconPath(AbilityDef d) => "Textures/Icons/Abilities/" + (d.Icon ?? d.Id);
        public static string StatusIconPath(StatusDef d) => "Textures/Icons/Statuses/" + (d.Icon ?? d.Id);
        public static string PortraitPath(HeroDef h) => "Textures/Icons/Portraits/" + (h.Portrait ?? ("portrait_" + h.Id.Replace("hero_", "")));

        public static string Attribute(PrimaryAttribute a) => a switch { PrimaryAttribute.Strength => "Strength", PrimaryAttribute.Agility => "Agility", _ => "Intelligence" };
        public static string Faction(Faction f) => f switch
        {
            Data.Faction.CrimsonCourt => "The Crimson Court",
            Data.Faction.AshenLegion => "The Ashen Legion",
            Data.Faction.WildCovenant => "The Wild Covenant",
            Data.Faction.Dawnguard => "The Dawnguard",
            _ => "Unaligned",
        };
    }
}
