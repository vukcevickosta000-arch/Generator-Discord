using System;
using System.Collections.Generic;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    public sealed class AbilityInstance
    {
        public AbilityDef Def;
        public int Level;
        public float Cooldown;
        public float CooldownTotal;
        public int Charges;
        public float ChargeTimer;
        public bool ToggledOn;
        public bool AutoCast;
        public int Index;
        /// <summary>Set when this ability is an item's active.</summary>
        public ItemInstance Item;

        public bool IsReady => Cooldown <= 0f && (Def.MaxCharges <= 0 || Charges > 0);
        public bool IsPassive => Def.Targeting == TargetingMode.Passive;
    }

    public sealed class ItemInstance
    {
        private static int _nextId = 1;
        public readonly int InstanceId = System.Threading.Interlocked.Increment(ref _nextId);
        public ItemDef Def;
        public int Charges;
        public float PurchaseTime;
        public int PurchaserPlayer;
        public AbilityInstance Active;
        /// <summary>Items bought in the last few seconds can be sold for a full refund.</summary>
        public bool UsedSincePurchase;
    }

    public sealed class StatusInstance
    {
        public StatusDef Def;
        public Unit Source;
        public int Level = 1;
        public float Remaining;
        public float Duration;
        public int Stacks = 1;
        public float IntervalTimer;
        public float ShieldRemaining;
        public bool PiercesMagicImmunity;
        public bool Permanent;
        public int Serial;
        /// <summary>Owning aura / item / ability (so passive sources can refresh without duplicating).</summary>
        public object Origin;
    }

    public sealed class TriggerState
    {
        public int Counter;
        public int LastTargetId;
        public float CooldownUntil;
        public PseudoRandom Prd;
    }

    public enum MotionKind : byte { None, Dash, Leap, Knockback, Pull }

    public sealed class ForcedMotion
    {
        public MotionKind Kind;
        public Vector2 Start, End;
        public float Speed;
        public float Duration, Elapsed;
        public float Height;
        public bool Invulnerable;
        public bool Interrupts = true;
        public EffectDef Def;
        public EffectContext Context;
        public HashSet<int> Passed;
        public Unit Follow;        // pull towards a moving caster
        public bool Collided;
    }

    public struct DamageRecord
    {
        public int PlayerId;
        public float Time;
    }

    /// <summary>
    /// Every simulated entity that can have health: heroes, creeps, towers, neutrals, summons, wards, RTS units.
    /// Plain C# object; the Match iterates units in explicit systems (no per-unit Update callbacks).
    /// </summary>
    public sealed class Unit
    {
        public int Id;
        public UnitKind Kind;
        public Team Team;
        public string DefId;
        public UnitDef UnitDef;
        public HeroDef HeroDef;
        public Player Owner;
        public string Name;

        public Vector2 Position;
        public float Facing;
        public float Radius = 0.3f;
        public bool Flying;
        public float BaseModelHeight;

        public float Hp, Mana;
        public bool Dead;
        public bool Removed;
        public float DeathTime;
        public float RespawnAt;
        public int Level = 1;
        public int Xp;
        public int AbilityPoints;

        public readonly StatSheet Stats = new StatSheet();
        public bool StatsDirty = true;
        public StatusFlags Flags;
        public StatusFlags InnateFlags;
        public readonly List<StatusInstance> Statuses = new List<StatusInstance>();
        public readonly List<AbilityInstance> Abilities = new List<AbilityInstance>();
        public readonly Dictionary<TriggerDef, TriggerState> TriggerStates = new Dictionary<TriggerDef, TriggerState>();

        public ItemInstance[] Inventory;
        public ItemInstance[] Stash;
        public ItemInstance[] Backpack;

        // ---- Orders & action ----
        public Order CurrentOrder;
        public readonly List<Order> OrderQueue = new List<Order>();
        public ActionState Action;
        public float ActionTimer;
        public int ActionStartTick;
        public int ActionAbility = -1;
        public int ActionTargetId;
        public Vector2 ActionPoint;
        public bool ActionPaid;
        public float ChannelRemaining;
        public float ChannelTickTimer;
        public float AttackCooldown;
        public int AttackTargetId;
        public bool AttackIsCrit;
        public ForcedMotion Motion;
        public float IdleTime;
        public float LastAttackedTime = -999f;
        public int LastAttackerId;

        // ---- Movement ----
        public readonly List<Vector2> Path = new List<Vector2>();
        public int PathIndex;
        public Vector2 PathGoal;
        public float RepathTimer;
        public int PathGridVersion;
        public float StuckTimer;
        public Vector2 LastPosition;
        public bool IsMoving;
        public Vector2 HoldPosition;

        // ---- AI / roles ----
        public IUnitBrain Brain;
        public int LaneIndex = -1;
        public int WaypointIndex;
        public string CampId;
        public Vector2 HomePosition;
        public float LeashRange;
        public int AggroTargetId;
        public float AggroUntil;
        public float Lifetime = -1f;
        public Unit Summoner;
        public bool IsIllusion;
        public int CreepUpgradeLevel;
        public bool IsMegaCreep;

        // ---- Structures ----
        public bool Invulnerable => (Flags & StatusFlags.Invulnerable) != 0;
        public List<Unit> ProtectedBy;
        public List<Unit> UnlockedByAny;
        public string StructureId;
        public int Tier;
        public string Lane;
        public string BarracksType;

        // ---- Vision ----
        public readonly bool[] VisibleTo = new bool[3];
        public float VisionOverride = -1f;

        // ---- Bookkeeping ----
        public readonly List<DamageRecord> RecentHeroDamage = new List<DamageRecord>(4);
        public int KillStreak;
        public float LastHitTime;
        public int SpawnTick;
        public float TotalDamageTaken;

        public bool IsHero => Kind == UnitKind.Hero;
        public bool IsStructure => Kind == UnitKind.Tower || Kind == UnitKind.Barracks || Kind == UnitKind.Core || Kind == UnitKind.Fountain || Kind == UnitKind.Building || Kind == UnitKind.Shop;
        public bool IsAlive => !Dead && !Removed;
        public bool IsCreep => Kind == UnitKind.Creep;
        public bool IsNeutral => Kind == UnitKind.Neutral || Kind == UnitKind.Boss;
        public bool CanMove => (Flags & (StatusFlags.Rooted | StatusFlags.HardDisable)) == 0 && Motion == null && Stats.MoveSpeed > 0 && !IsStructure;
        public bool CanAct => (Flags & StatusFlags.HardDisable) == 0 && Motion == null && !Dead;
        public bool CanAttack => CanAct && (Flags & StatusFlags.Disarmed) == 0 && AttackRangeValid;
        public bool CanCast => CanAct && (Flags & StatusFlags.Silenced) == 0;
        public bool CanUseItems => CanAct && (Flags & StatusFlags.Muted) == 0;
        private bool AttackRangeValid => Stats.DamageMax > 0 || Stats.BonusDamage > 0;
        public bool IsMagicImmune => (Flags & StatusFlags.MagicImmune) != 0;
        public bool IsInvisible => (Flags & StatusFlags.Invisible) != 0 && (Flags & StatusFlags.Revealed) == 0;
        public AttackType AttackType => HeroDef != null ? HeroDef.AttackType : UnitDef != null ? UnitDef.AttackType : AttackType.Melee;
        public float ProjectileSpeed => HeroDef != null ? HeroDef.ProjectileSpeed : UnitDef?.ProjectileSpeed ?? 0f;
        public float HpFraction => Stats.MaxHp > 0 ? Hp / Stats.MaxHp : 0f;
        public float AcquisitionRange => UnitDef != null ? UnitDef.AcquisitionRange : 7.5f;
        public string ModelKey
        {
            get
            {
                for (int i = Statuses.Count - 1; i >= 0; i--) if (!string.IsNullOrEmpty(Statuses[i].Def.ModelOverride)) return Statuses[i].Def.ModelOverride;
                return HeroDef?.Model ?? UnitDef?.Model ?? DefId;
            }
        }

        public TargetType TargetTypeOf()
        {
            if (IsIllusion) return TargetType.Illusion | TargetType.Hero;
            switch (Kind)
            {
                case UnitKind.Hero: return TargetType.Hero;
                case UnitKind.Creep: return TargetType.Creep;
                case UnitKind.Neutral: return TargetType.Neutral;
                case UnitKind.Boss: return TargetType.Boss;
                case UnitKind.Summon: return TargetType.Summon;
                case UnitKind.Ward: return TargetType.Ward;
                case UnitKind.Worker: return TargetType.Creep;
                default: return TargetType.Structure;
            }
        }

        public bool HasFlag(StatusFlags f) => (Flags & f) != 0;

        public AbilityInstance GetAbility(int index)
        {
            if (index >= Order.ItemSlotBase)
            {
                int slot = index - Order.ItemSlotBase;
                if (Inventory == null || slot < 0 || slot >= Inventory.Length) return null;
                return Inventory[slot]?.Active;
            }
            return index >= 0 && index < Abilities.Count ? Abilities[index] : null;
        }

        public AbilityInstance FindAbility(string id)
        {
            foreach (var a in Abilities) if (a.Def.Id == id) return a;
            return null;
        }

        public StatusInstance FindStatus(string id)
        {
            foreach (var s in Statuses) if (s.Def.Id == id) return s;
            return null;
        }

        public int CountStatus(string id)
        {
            int n = 0;
            foreach (var s in Statuses) if (s.Def.Id == id) n += s.Def.Stacking == StackingMode.Intensity ? s.Stacks : 1;
            return n;
        }

        public TriggerState GetTriggerState(TriggerDef t)
        {
            if (!TriggerStates.TryGetValue(t, out var st)) { st = new TriggerState(); TriggerStates[t] = st; }
            return st;
        }

        public float DistanceTo(Unit other) => Vector2.Distance(Position, other.Position);
        public float EdgeDistanceTo(Unit other) => Math.Max(0f, Vector2.Distance(Position, other.Position) - Radius - other.Radius);

        public IEnumerable<ItemInstance> EquippedItems()
        {
            if (Inventory == null) yield break;
            foreach (var it in Inventory) if (it != null) yield return it;
        }

        // ------------------------------------------------------------------ stats

        private static readonly StatAccumulator Acc = new StatAccumulator();

        /// <summary>Derives final stats from definition, level, attributes, items, passives and statuses.</summary>
        public void RecomputeStats(RulesDef rules)
        {
            float hpFrac = Stats.MaxHp > 0 ? Hp / Stats.MaxHp : 1f;
            float manaFrac = Stats.MaxMana > 0 ? Mana / Stats.MaxMana : 1f;
            bool first = Stats.MaxHp <= 0;

            var acc = Acc;
            lock (acc)
            {
                acc.Reset();
                // Items (main inventory only).
                if (Inventory != null)
                {
                    var seenUnique = new HashSet<string>();
                    foreach (var it in Inventory)
                    {
                        if (it?.Def.Modifiers == null) continue;
                        if (it.Def.UniquePassiveGroup != null && !seenUnique.Add(it.Def.UniquePassiveGroup)) continue;
                        foreach (var m in it.Def.Modifiers) acc.Apply(m.Stat, m.Value.Get(1) * (it.Def.MaxStack > 1 ? Math.Max(1, it.Charges) : 1));
                    }
                }
                // Passive ability modifiers.
                bool broken = (Flags & StatusFlags.BreakPassives) != 0;
                foreach (var ab in Abilities)
                {
                    if (ab.Level <= 0 || ab.Def.PassiveModifiers == null || broken) continue;
                    foreach (var m in ab.Def.PassiveModifiers) acc.Apply(m.Stat, m.Value.Get(ab.Level));
                }
                // Statuses.
                foreach (var s in Statuses)
                {
                    if (s.Def.Modifiers == null) continue;
                    foreach (var m in s.Def.Modifiers) acc.Apply(m.Stat, m.Value.Get(s.Level) * (m.PerStack ? s.Stacks : 1));
                }

                if (HeroDef != null) ComputeHeroStats(rules, acc);
                else ComputeUnitStats(rules, acc);
            }

            if (first) { Hp = Stats.MaxHp; Mana = Stats.MaxMana; }
            else
            {
                Hp = Math.Min(Stats.MaxHp, Math.Max(Dead ? 0 : 1, hpFrac * Stats.MaxHp));
                if (Dead) Hp = 0;
                Mana = Math.Min(Stats.MaxMana, manaFrac * Stats.MaxMana);
            }
            StatsDirty = false;
        }

        private void ComputeHeroStats(RulesDef r, StatAccumulator a)
        {
            var h = HeroDef;
            var s = Stats;
            int lvl = Math.Max(1, Level);
            s.BaseStr = h.Str + h.StrGain * (lvl - 1);
            s.BaseAgi = h.Agi + h.AgiGain * (lvl - 1);
            s.BaseInt = h.Int + h.IntGain * (lvl - 1);
            s.Str = s.BaseStr + a.Add[(int)StatType.Str];
            s.Agi = s.BaseAgi + a.Add[(int)StatType.Agi];
            s.Int = s.BaseInt + a.Add[(int)StatType.Int];

            s.MaxHp = (h.BaseHp + s.Str * r.StrHp + a.Add[(int)StatType.MaxHp]) * (1f + a.Add[(int)StatType.MaxHpPct]);
            s.HpRegen = h.BaseHpRegen + s.Str * r.StrHpRegen + a.Add[(int)StatType.HpRegen] + s.MaxHp * a.Add[(int)StatType.HpRegenPct];
            s.MaxMana = h.BaseMana + s.Int * r.IntMana + a.Add[(int)StatType.MaxMana];
            s.ManaRegen = h.BaseManaRegen + s.Int * r.IntManaRegen + a.Add[(int)StatType.ManaRegen] + s.MaxMana * a.Add[(int)StatType.ManaRegenPct];
            s.Armor = (h.BaseArmor + s.Agi * r.AgiArmor + a.Add[(int)StatType.Armor]) * (1f + a.Add[(int)StatType.ArmorPct]);
            s.MagicResist = 1f - (1f - h.MagicResist) * a.InvProduct[(int)StatType.MagicResist] + a.Add[(int)StatType.MagicResist];

            float primary = h.PrimaryAttribute == PrimaryAttribute.Strength ? s.Str : h.PrimaryAttribute == PrimaryAttribute.Agility ? s.Agi : s.Int;
            s.DamageMin = h.DamageMin + primary + a.Add[(int)StatType.BaseDamage];
            s.DamageMax = h.DamageMax + primary + a.Add[(int)StatType.BaseDamage];
            s.BonusDamage = a.Add[(int)StatType.BonusDamage] + (s.DamageMin + s.DamageMax) * 0.5f * a.Add[(int)StatType.BonusDamagePct];

            s.AttackSpeed = MathUtil.Clamp(s.Agi * r.AgiAttackSpeed + a.Add[(int)StatType.AttackSpeed], r.MinAttackSpeed, r.MaxAttackSpeed);
            float bat = a.Add[(int)StatType.BaseAttackTimeOverride] > 0 ? a.Add[(int)StatType.BaseAttackTimeOverride] : h.BaseAttackTime;
            float speedFactor = 1f + s.AttackSpeed / 100f;
            s.AttackTime = bat / speedFactor;
            s.AttackPoint = h.AttackPoint / speedFactor;
            s.AttackBackswing = h.AttackBackswing / speedFactor;
            s.AttackRange = h.AttackRange + (h.AttackType == AttackType.Ranged ? a.Add[(int)StatType.AttackRange] : a.Add[(int)StatType.AttackRange] * 0f);

            ComputeShared(r, a, h.MoveSpeed, h.TurnRate, h.VisionDay, h.VisionNight);
        }

        private void ComputeUnitStats(RulesDef r, StatAccumulator a)
        {
            var d = UnitDef;
            var s = Stats;
            s.Str = s.Agi = s.Int = 0;
            float upgradeHp = d.HpPerUpgrade * CreepUpgradeLevel;
            float upgradeDmg = d.DamagePerUpgrade * CreepUpgradeLevel;
            s.MaxHp = (d.MaxHp + upgradeHp + a.Add[(int)StatType.MaxHp]) * (1f + a.Add[(int)StatType.MaxHpPct]);
            s.HpRegen = d.HpRegen + a.Add[(int)StatType.HpRegen] + s.MaxHp * a.Add[(int)StatType.HpRegenPct];
            s.MaxMana = d.MaxMana + a.Add[(int)StatType.MaxMana];
            s.ManaRegen = d.ManaRegen + a.Add[(int)StatType.ManaRegen] + s.MaxMana * a.Add[(int)StatType.ManaRegenPct];
            s.Armor = (d.Armor + a.Add[(int)StatType.Armor]) * (1f + a.Add[(int)StatType.ArmorPct]);
            s.MagicResist = 1f - (1f - d.MagicResist) * a.InvProduct[(int)StatType.MagicResist] + a.Add[(int)StatType.MagicResist];
            s.DamageMin = d.DamageMin + upgradeDmg + a.Add[(int)StatType.BaseDamage];
            s.DamageMax = d.DamageMax + upgradeDmg + a.Add[(int)StatType.BaseDamage];
            s.BonusDamage = a.Add[(int)StatType.BonusDamage] + (s.DamageMin + s.DamageMax) * 0.5f * a.Add[(int)StatType.BonusDamagePct];
            s.AttackSpeed = MathUtil.Clamp(a.Add[(int)StatType.AttackSpeed], r.MinAttackSpeed, r.MaxAttackSpeed);
            float speedFactor = 1f + s.AttackSpeed / 100f;
            s.AttackTime = d.BaseAttackTime / speedFactor;
            s.AttackPoint = d.AttackPoint / speedFactor;
            s.AttackBackswing = d.AttackBackswing / speedFactor;
            s.AttackRange = d.AttackRange + a.Add[(int)StatType.AttackRange];
            ComputeShared(r, a, d.MoveSpeed, d.TurnRate, d.VisionDay, d.VisionNight);
        }

        private void ComputeShared(RulesDef r, StatAccumulator a, float baseMove, float turnRate, float visionDay, float visionNight)
        {
            var s = Stats;
            float slowResist = a.Multiplicative(StatType.SlowResist);
            float pct = a.Add[(int)StatType.MoveSpeedPct];
            if (pct < 0) pct *= 1f - MathUtil.Clamp01(slowResist);
            float move = (baseMove + a.Add[(int)StatType.MoveSpeed]) * (1f + pct);
            s.MoveSpeed = baseMove <= 0 ? 0 : MathUtil.Clamp(move, r.MinMoveSpeed, r.MaxMoveSpeed);
            s.TurnRate = turnRate;
            s.CastRangeBonus = a.Add[(int)StatType.CastRange];
            s.Lifesteal = a.Add[(int)StatType.Lifesteal];
            s.SpellVamp = a.Add[(int)StatType.SpellVamp];
            s.Evasion = a.Multiplicative(StatType.Evasion);
            s.CritChance = a.Add[(int)StatType.CritChance];
            s.CritMultiplier = a.Add[(int)StatType.CritMultiplier] > 0 ? a.Add[(int)StatType.CritMultiplier] : 1.75f;
            s.CooldownReduction = MathUtil.Clamp(a.Multiplicative(StatType.CooldownReduction), 0f, 0.6f);
            s.StatusResist = MathUtil.Clamp(a.Multiplicative(StatType.StatusResist), -1f, r.StatusResistCap);
            s.SpellAmp = a.Add[(int)StatType.SpellAmp] + (HeroDef != null ? s.Int * 0.0007f : 0f);
            s.DamageTakenPct = a.Add[(int)StatType.DamageTakenPct];
            s.OutgoingDamagePct = a.Add[(int)StatType.OutgoingDamagePct];
            s.HealAmp = a.Add[(int)StatType.HealAmp];
            s.VisionDay = Math.Max(0, visionDay + a.Add[(int)StatType.VisionDay] + a.Add[(int)StatType.BonusVision]);
            s.VisionNight = Math.Max(0, visionNight + a.Add[(int)StatType.VisionNight] + a.Add[(int)StatType.BonusVision]);
            s.ManaCostReduction = MathUtil.Clamp01(a.Add[(int)StatType.ManaCostReduction]);
            s.HealthCostReduction = MathUtil.Clamp01(a.Add[(int)StatType.HealthCostReduction]);
            s.SlowResist = slowResist;
        }

        public override string ToString() => $"{Name}#{Id}({Team}, {Hp:0}/{Stats.MaxHp:0})";
    }

    public interface IUnitBrain
    {
        void Think(Match match, Unit unit, float dt);
    }
}
