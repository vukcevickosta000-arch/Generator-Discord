using System;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    /// <summary>Accumulates stat modifiers from items, statuses and passives before final stats are derived.</summary>
    public sealed class StatAccumulator
    {
        public readonly float[] Add = new float[(int)StatType.Count];
        /// <summary>Product of (1 - v) for multiplicatively stacking stats (magic resist, evasion, status resist).</summary>
        public readonly float[] InvProduct = new float[(int)StatType.Count];

        public void Reset()
        {
            Array.Clear(Add, 0, Add.Length);
            for (int i = 0; i < InvProduct.Length; i++) InvProduct[i] = 1f;
        }

        public void Apply(StatType stat, float value)
        {
            switch (stat)
            {
                case StatType.MagicResist:
                case StatType.Evasion:
                case StatType.StatusResist:
                case StatType.CooldownReduction:
                case StatType.SlowResist:
                    if (value >= 0) InvProduct[(int)stat] *= 1f - Math.Min(0.99f, value);
                    else Add[(int)stat] += value; // reductions are additive
                    break;
                case StatType.CritMultiplier:
                    // Crit multipliers do not add up: the strongest source wins.
                    Add[(int)stat] = Math.Max(Add[(int)stat], value);
                    break;
                case StatType.AllAttributes:
                    Add[(int)StatType.Str] += value; Add[(int)StatType.Agi] += value; Add[(int)StatType.Int] += value;
                    break;
                default:
                    Add[(int)stat] += value;
                    break;
            }
        }

        public float Multiplicative(StatType stat) => 1f - InvProduct[(int)stat] + Add[(int)stat];
    }

    /// <summary>Final derived stats of a unit. Recomputed only when something marks the unit dirty.</summary>
    public sealed class StatSheet
    {
        public float Str, Agi, Int;
        public float BaseStr, BaseAgi, BaseInt;
        public float MaxHp, HpRegen, MaxMana, ManaRegen;
        public float Armor, MagicResist;
        public float DamageMin, DamageMax, BonusDamage;
        public float AttackSpeed;          // IAS points (100 = double speed)
        public float AttackTime;           // seconds between attacks
        public float AttackPoint, AttackBackswing;
        public float AttackRange;
        public float MoveSpeed;
        public float CastRangeBonus;
        public float Lifesteal, SpellVamp, Evasion, CritChance, CritMultiplier;
        public float CooldownReduction, StatusResist, SpellAmp, DamageTakenPct, OutgoingDamagePct, HealAmp;
        public float VisionDay, VisionNight;
        public float ManaCostReduction, HealthCostReduction, SlowResist;
        public float TurnRate;

        public float AverageDamage => (DamageMin + DamageMax) * 0.5f + BonusDamage;
    }
}
