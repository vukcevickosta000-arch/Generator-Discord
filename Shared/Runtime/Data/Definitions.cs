using System;
using System.Collections.Generic;
using System.Numerics;
using Bloodfall.Core;

namespace Bloodfall.Data
{
    /// <summary>JSON-friendly 2D vector: written as [x, y].</summary>
    public struct JVec2 : IJsonCustom
    {
        public float X, Y;
        public JVec2(float x, float y) { X = x; Y = y; }
        public static implicit operator Vector2(JVec2 v) => new Vector2(v.X, v.Y);
        public static implicit operator JVec2(Vector2 v) => new JVec2(v.X, v.Y);
        public void ReadJson(JsonNode node) { X = node[0].AsFloat(); Y = node[1].AsFloat(); }
        public JsonNode WriteJson() { var a = JsonNode.NewArray(); a.Add(JsonNode.From(Math.Round(X, 3))); a.Add(JsonNode.From(Math.Round(Y, 3))); return a; }
        public override string ToString() => $"({X:0.##}, {Y:0.##})";
    }

    public sealed class ModifierDef
    {
        public StatType Stat;
        public LeveledValue Value = new LeveledValue();
        /// <summary>Multiply by the status' current stack count.</summary>
        public bool PerStack;
    }

    public sealed class ScalingDef
    {
        public StatType Stat = StatType.Str;
        public LeveledValue Ratio = new LeveledValue();
    }

    /// <summary>
    /// One node in an ability's effect graph. A flat record: only the fields relevant to <see cref="Type"/> are used.
    /// This keeps authoring in JSON / the editor simple and lets the server interpret every hero generically.
    /// </summary>
    public sealed class EffectDef
    {
        public EffectType Type;
        public EffectTarget Target = EffectTarget.Target;

        // ---- Damage / heal / mana ----
        public LeveledValue Amount = new LeveledValue();
        public DamageType DamageType = DamageType.Magical;
        public List<ScalingDef> Scaling;
        public LeveledValue PctMaxHp;
        public LeveledValue PctCurrentHp;
        public LeveledValue PctMissingHp;
        /// <summary>Heals the caster by this fraction of the damage actually dealt.</summary>
        public LeveledValue HealCasterPct;
        public bool CanCrit;
        public bool IsAttackDamage;
        /// <summary>Damage flags: does not wake / does not break channels etc. (reserved)</summary>
        public bool NoReflect;

        // ---- Status ----
        public string Status;
        public LeveledValue Duration;
        public int Stacks = 1;
        public DispelType DispelStrength = DispelType.Basic;
        public bool DispelDebuffs = true;
        public bool DispelBuffs;

        // ---- Area ----
        public LeveledValue Radius;
        public AreaCenter Center = AreaCenter.Caster;
        public TargetTeam Team = TargetTeam.Enemy;
        public TargetType Types = TargetType.Units;
        public int MaxTargets;
        public bool ExcludePrimary;
        /// <summary>Cone half-angle in degrees (0 = full circle). Cone faces the cast direction.</summary>
        public float ConeAngle;
        public bool PiercesMagicImmunity;
        public List<EffectDef> Effects;

        // ---- Projectile ----
        public ProjectileKind Projectile = ProjectileKind.Tracking;
        public float Speed = 12f;
        public LeveledValue Range;
        public float Width = 0.6f;
        public bool StopOnFirstHit = true;
        public bool Dodgeable = true;
        /// <summary>Linear projectiles: return to the caster after reaching max range (boomerang).</summary>
        public bool Returns;
        public int Bounces;
        public float BounceRange = 6f;
        public List<EffectDef> OnHit;
        public List<EffectDef> OnEnd;

        // ---- Movement (dash / leap / knockback / pull / blink / teleport) ----
        public float Distance;
        public LeveledValue MaxDistance;
        public float MinDistance;
        public float MoveDuration;
        public float Height;
        public bool Invulnerable;
        public bool StopOnUnitCollision;
        public float CollisionRadius = 1f;
        public TargetType CollisionTypes = TargetType.Hero;
        public List<EffectDef> OnCollide;
        public List<EffectDef> OnPass;
        public List<EffectDef> OnArrive;
        /// <summary>For Pull: pull the target all the way to the caster (hook).</summary>
        public bool ToCaster;
        /// <summary>For Blink/Dash: destination relative to the target unit (behind it).</summary>
        public bool BehindTarget;

        // ---- Delayed / Zone ----
        /// <summary>Random offset (up to this radius) applied to each zone's centre; with Count, scatters several zones.</summary>
        public float Scatter;
        public LeveledValue Delay;
        public float Interval = 1f;
        public LeveledValue ZoneDuration;
        public bool FollowCaster;
        public List<EffectDef> OnTick;
        public List<EffectDef> OnExpire;

        // ---- Spawn ----
        public string UnitId;
        public LeveledValue Count;
        public LeveledValue SummonDuration;
        public bool ControlledByOwner = true;

        // ---- Chance / Execute / misc ----
        public float Chance = 1f;
        public LeveledValue Threshold;
        public LeveledValue CooldownDelta;
        public LeveledValue Gold;
        public string Condition;

        // ---- Presentation hooks (client only; ignored by the simulation) ----
        public string Vfx;
        public string Sfx;
        public float Shake;
        /// <summary>Damage from this effect is credited to the unit that applied the running status (curses, DoTs).</summary>
        public bool CreditStatusSource;
    }

    public sealed class TriggerDef
    {
        public TriggerType On;
        public float Chance = 1f;
        /// <summary>Proc every N events (e.g. every 4th attack).</summary>
        public int EveryN;
        /// <summary>For EveryN: count per target (resets when switching target).</summary>
        public bool SameTarget;
        public LeveledValue Radius;
        public LeveledValue InternalCooldown;
        public TargetType Types = TargetType.All;
        public TargetTeam Team = TargetTeam.Enemy;
        public bool IgnoreIllusions = true;
        /// <summary>Only melee attackers benefit (e.g. cleave).</summary>
        public bool MeleeOnly;
        /// <summary>Uses pseudo-random distribution for chance procs.</summary>
        public bool PseudoRandom = true;
        public float Threshold;
        public List<EffectDef> Effects = new List<EffectDef>();
    }

    public sealed class AuraDef
    {
        public LeveledValue Radius = new LeveledValue(9f);
        public TargetTeam Team = TargetTeam.AllyOrSelf;
        public TargetType Types = TargetType.Units;
        public string Status;
        public bool HeroesOnly;
    }

    public sealed class StatusDef
    {
        public string Id;
        public string Name;
        public string Description;
        public bool IsDebuff;
        public DispelType Dispel = DispelType.Basic;
        public StackingMode Stacking = StackingMode.Refresh;
        public int MaxStacks = 1;
        public StatusFlags Flags;
        public List<ModifierDef> Modifiers;
        public float Interval;
        public List<EffectDef> OnInterval;
        public List<EffectDef> OnApply;
        public List<EffectDef> OnExpire;
        public List<TriggerDef> Triggers;
        /// <summary>Absorbs this much damage before breaking (shields).</summary>
        public LeveledValue Shield;
        public DamageType? ShieldType;
        /// <summary>Fraction of incoming damage redirected / reduced etc. are expressed as modifiers.</summary>
        public bool AffectedByStatusResist = true;
        /// <summary>Removed when the bearer attacks or casts (invisibility veils, hidden stances).</summary>
        public bool BreakOnAction;
        public bool Hidden;
        public bool RemoveOnDeath = true;
        /// <summary>Fear / taunt direction source; forced movement handled by the simulation.</summary>
        public string Icon;
        public string Vfx;
        public string Sfx;
        /// <summary>Transform: replace the unit's model key while active.</summary>
        public string ModelOverride;
        /// <summary>Transform: replacement abilities by slot while active (e.g. Fenrax's beast form).</summary>
        public Dictionary<string, string> AbilityOverrides;
    }

    public sealed class AbilityDef
    {
        public string Id;
        public string Name;
        public string Description;
        public string Lore;
        public AbilitySlot Slot = AbilitySlot.Q;
        public TargetingMode Targeting = TargetingMode.NoTarget;
        public TargetTeam TargetTeam = TargetTeam.Enemy;
        public TargetType TargetTypes = TargetType.Units;
        public int MaxLevel = 4;
        public int[] LevelRequirements;
        public bool IsUltimate;
        public LeveledValue Cooldown = new LeveledValue();
        public LeveledValue ManaCost = new LeveledValue();
        public LeveledValue HealthCost = new LeveledValue();
        public LeveledValue CastRange = new LeveledValue();
        public float CastPoint = 0.3f;
        public float Backswing = 0.4f;
        public LeveledValue ChannelTime;
        public float ChannelInterval = 0.25f;
        public LeveledValue AoeRadius;
        public bool PiercesMagicImmunity;
        public bool CanTargetMagicImmune;
        /// <summary>Cast without turning to face the target (e.g. self-buffs).</summary>
        public bool IgnoreFacing;
        public bool CastWhileMoving;
        public bool IgnoreSilence;
        public int MaxCharges;
        public LeveledValue ChargeRestoreTime;
        public List<EffectDef> OnCast = new List<EffectDef>();
        public List<EffectDef> OnChannelTick;
        public List<EffectDef> OnChannelEnd;
        public List<EffectDef> OnChannelInterrupted;
        public List<TriggerDef> Triggers;
        public List<ModifierDef> PassiveModifiers;
        public AuraDef Aura;
        /// <summary>Status applied to the caster while a Toggle ability is on.</summary>
        public string ToggleStatus;
        public List<StatusDef> Statuses;
        public string Icon;
        public string CastAnimation = "Cast";
        public string CastVfx;
        public string CastSfx;
        public string VoiceLine;
        /// <summary>Hint for bots on how to use this ability.</summary>
        public string BotUsage;
        /// <summary>Casting this ability does not break BreakOnAction statuses (e.g. the veil ability itself).</summary>
        public bool KeepsStealth;
        /// <summary>The channel breaks when an enemy damages the caster (contested objectives such as Vharoth's seals).</summary>
        public bool InterruptedByDamage;
        /// <summary>Granted to every hero at spawn (kept hidden from the ability bar), e.g. the seal-breaking channel.</summary>
        public bool CommonHeroAbility;
        public bool Hidden;

        public int RequiredHeroLevel(int nextAbilityLevel)
        {
            if (LevelRequirements != null && LevelRequirements.Length > 0)
                return LevelRequirements[Math.Min(LevelRequirements.Length - 1, nextAbilityLevel - 1)];
            if (IsUltimate) return 6 + (nextAbilityLevel - 1) * 6;           // 6 / 12 / 18
            if (Slot == AbilitySlot.Innate) return int.MaxValue;               // innates are not leveled with points
            return 1 + (nextAbilityLevel - 1) * 2;                             // 1 / 3 / 5 / 7
        }
    }

    public sealed class HeroDef
    {
        public string Id;
        public string Name;
        public string Title;
        public Faction Faction;
        public PrimaryAttribute PrimaryAttribute;
        public string[] Roles = Array.Empty<string>();
        public int Difficulty = 1;
        public ResourceType Resource = ResourceType.Mana;
        public AttackType AttackType = AttackType.Melee;
        public string Lore;
        public string Personality;
        public string[] Strengths = Array.Empty<string>();
        public string[] Weaknesses = Array.Empty<string>();
        public string[] Counters = Array.Empty<string>();
        public string[] Synergies = Array.Empty<string>();

        public float BaseHp = 200;
        public float BaseHpRegen = 0.5f;
        public float BaseMana = 75;
        public float BaseManaRegen = 0.2f;
        public float BaseArmor;
        public float MagicResist = 0.25f;
        public float DamageMin = 25;
        public float DamageMax = 30;
        public float AttackRange = 1.6f;
        public float AttackPoint = 0.4f;
        public float AttackBackswing = 0.5f;
        public float BaseAttackTime = 1.7f;
        public float ProjectileSpeed;
        public float MoveSpeed = 3.75f;
        public float TurnRate = 14f;
        public float CollisionRadius = 0.35f;
        public float VisionDay = 22f;
        public float VisionNight = 10f;

        public float Str = 20, StrGain = 2;
        public float Agi = 18, AgiGain = 2;
        public float Int = 16, IntGain = 2;

        public List<string> Abilities = new List<string>();
        public Dictionary<string, List<string>> RecommendedItems;

        public string Model;
        public string Portrait;
        public float ModelScale = 1f;
        public string ProjectileVisual;
        public string AttackSfx;
        public string VoiceSet;
        public string BotProfile;
        /// <summary>True when the hero is fully implemented and selectable.</summary>
        public bool Playable = true;
    }

    public sealed class UnitDef
    {
        public string Id;
        public string Name;
        public UnitKind Kind = UnitKind.Creep;
        public AttackType AttackType = AttackType.Melee;
        public float MaxHp = 500;
        public float HpRegen;
        public float MaxMana;
        public float ManaRegen;
        public float Armor;
        public float MagicResist;
        public float DamageMin = 20;
        public float DamageMax = 24;
        public float AttackRange = 1.4f;
        public float AttackPoint = 0.45f;
        public float AttackBackswing = 0.5f;
        public float BaseAttackTime = 1f;
        public float ProjectileSpeed;
        public float MoveSpeed = 3.8f;
        public float TurnRate = 10f;
        public float CollisionRadius = 0.3f;
        public float VisionDay = 10f;
        public float VisionNight = 8f;
        public float AcquisitionRange = 6.5f;
        public int BountyGoldMin;
        public int BountyGoldMax;
        public int BountyXp;
        /// <summary>Gold every allied hero receives when this unit (structure) dies.</summary>
        public int TeamBountyGold;
        public float StructureDamageMultiplier = 1f;
        /// <summary>Damage multiplier dealt TO structures (siege units deal more).</summary>
        public float SiegeMultiplier = 1f;
        public bool Flying;
        public StatusFlags InnateFlags;
        public List<string> Abilities = new List<string>();
        public string Model;
        public float ModelScale = 1f;
        public string ProjectileVisual;
        public string AttackSfx;
        public string DeathSfx;
        public string[] Tags = Array.Empty<string>();
        /// <summary>Per-upgrade scaling applied to lane creeps (every RulesDef.CreepUpgradeInterval).</summary>
        public float HpPerUpgrade;
        public float DamagePerUpgrade;
        public int GoldPerUpgrade;
        /// <summary>Structures: can be denied by allies below this HP fraction (0 = never).</summary>
        public float DenyThreshold;
        /// <summary>Structures: true sight radius (reveals invisible units).</summary>
        public float TrueSightRadius;
        /// <summary>Structures: fortification / backdoor damage reduction when no enemy creeps are nearby.</summary>
        public float BackdoorProtection;
        public float Height = 2f;

        // ---- RTS (ignored by the MOBA modes) ----
        public int SupplyCost;
        public int GoldCost;
        public int LumberCost;
        /// <summary>Seconds to train (units) or construct (buildings).</summary>
        public float BuildTime;
        /// <summary>Buildings: supply cap granted once construction is complete.</summary>
        public int SupplyProvided;
        /// <summary>Buildings: unit ids this building can train.</summary>
        public List<string> Trains;
        /// <summary>Workers: building ids this worker can construct.</summary>
        public List<string> Builds;
        /// <summary>Buildings: upgrade ids researched here.</summary>
        public List<string> Research;
        /// <summary>Completed buildings the owner must have before this can be built or trained (tech tree).</summary>
        public List<string> Requires;
        /// <summary>Buildings: workers may return gold / lumber here.</summary>
        public bool DropOffGold;
        public bool DropOffLumber;
        /// <summary>Workers: resources carried per trip.</summary>
        public int GatherGold;
        public int GatherLumber;
        /// <summary>Workers: seconds spent at a mine / chopping a tree per trip.</summary>
        public float MineTime = 1f;
        public float ChopTime = 6f;
        /// <summary>Buildings: construction advances on its own once placed (the worker is free to leave).</summary>
        public bool SelfBuilds;
        /// <summary>RTS: a status this unit wears at night (a shapeshift, usually with a model override).</summary>
        public string NightForm;
        /// <summary>RTS buildings: recruits heroes (every playable hero) and revives fallen ones.</summary>
        public bool HeroAltar;
        /// <summary>Resource nodes: starting amount (gold in a mine).</summary>
        public int ResourceAmount;
        /// <summary>Command card hotkey (RTS UI).</summary>
        public string Hotkey;
        public string Description;
    }

    public sealed class ItemDef
    {
        public string Id;
        public string Name;
        public string Description;
        public string Lore;
        /// <summary>Base items: price. Recipe items: price of the recipe scroll itself (0 = pure combination).</summary>
        public int Cost;
        public List<string> Components;
        public string Category = "Components";
        public string[] Tags = Array.Empty<string>();
        public ItemShop Shop = ItemShop.Main;
        public List<ModifierDef> Modifiers;
        public List<TriggerDef> Triggers;
        public AuraDef Aura;
        public AbilityDef Active;
        public bool Consumable;
        public int InitialCharges;
        public int MaxStack = 1;
        public bool Purchasable = true;
        /// <summary>Shared restock stock (wards). 0 = unlimited.</summary>
        public int StockMax;
        public float StockRestockTime;
        public int Tier = 1;
        public string Icon;
        /// <summary>Passive of the same name only applies once per hero.</summary>
        public string UniquePassiveGroup;
        public bool DisassembleAllowed;
        public bool Implemented = true;

        [JsonIgnore] public int TotalCost;
        [JsonIgnore] public List<string> BuildsInto = new List<string>();
    }

    public sealed class LaneDef
    {
        public string Name;
        /// <summary>Waypoints from the Dawn base to the Dusk base. Dusk creeps walk the reverse order.</summary>
        public List<JVec2> Waypoints = new List<JVec2>();
    }

    public sealed class StructurePlacement
    {
        public string Id;
        public string UnitId;
        public Team Team;
        public JVec2 Position;
        public float Facing;
        public string Lane;
        public int Tier;
        /// <summary>Structures that must ALL be destroyed before this one becomes vulnerable.</summary>
        public List<string> ProtectedBy;
        /// <summary>Alternative rule: this structure becomes vulnerable once ANY of these is destroyed (tier 4 towers).</summary>
        public List<string> UnlockedByAny;
        /// <summary>Barracks: which creep type this barracks upgrades when destroyed ("melee"/"ranged").</summary>
        public string BarracksType;
    }

    public sealed class CampPlacement
    {
        public string Id;
        public string CampType;
        public JVec2 Position;
        /// <summary>Area that must be free of units for the camp to respawn (blocking).</summary>
        public float SpawnBoxRadius = 3f;
        public Team Side = Team.Neutral;
        /// <summary>RTS: the camp attacks anyone who walks into its ground (false: it only fights back when attacked).</summary>
        public bool Guards = true;
    }

    public sealed class CampTypeDef
    {
        public string Id;
        public string Name;
        public string Difficulty = "Small";
        public List<List<string>> Variants = new List<List<string>>();
    }

    public sealed class TeamBaseDef
    {
        public Team Team;
        public JVec2 Fountain;
        public float FountainRadius = 9f;
        public JVec2 HeroSpawn;
        public JVec2 ShopPosition;
    }

    public sealed class MapDef
    {
        public string Id;
        public string Name;
        public string Subtitle;
        public string Description;
        public float Width = 192;
        public float Height = 192;
        public float CellSize = 0.5f;
        public int GridWidth;
        public int GridHeight;
        /// <summary>Base64 of run-length encoded cell bytes (see NavGrid for bit layout).</summary>
        public string Grid;
        public List<LaneDef> Lanes = new List<LaneDef>();
        public List<StructurePlacement> Structures = new List<StructurePlacement>();
        public List<TeamBaseDef> Bases = new List<TeamBaseDef>();
        public List<CampPlacement> Camps = new List<CampPlacement>();
        public List<JVec2> SecretShops = new List<JVec2>();
        public List<JVec2> SideShops = new List<JVec2>();
        public List<JVec2> RuneSpots = new List<JVec2>();
        public List<JVec2> WardSpots = new List<JVec2>();
        public JVec2 BossPit;
        public List<JVec2> VharothSeals = new List<JVec2>();
        /// <summary>RTS: start locations (a hall and workers are placed at each player's).</summary>
        public List<RtsStartDef> StartLocations = new List<RtsStartDef>();
        /// <summary>RTS: gold mines and other harvestable nodes.</summary>
        public List<ResourceNodePlacement> ResourceNodes = new List<ResourceNodePlacement>();
        /// <summary>Tree trunks: base64 of 5-byte records (ushort x*10, ushort y*10 little endian, byte variant).</summary>
        public string Trees;
        public string DressingFile;
        public string HeightFile;
        public float HeightScale = 1f;
    }

    public sealed class ExperienceDef
    {
        /// <summary>Total XP required to reach level index+1 (index 0 = level 1 = 0 XP).</summary>
        public int[] Cumulative;
        public int[] HeroKillXpByLevel;
    }

    /// <summary>Global balance rules. Everything numeric about the MOBA economy lives here.</summary>
    public sealed class RulesDef
    {
        public int TickRate = 30;
        public int SnapshotRate = 20;
        public int MaxHeroLevel = 25;
        public int StartingGold = 600;
        public float PassiveGoldPerSecond = 0.5f;
        /// <summary>Couriers: seconds to respawn at the fountain after being killed, and the hand-over distance.</summary>
        public string CourierUnit = "courier_blood_bat";
        public float CourierRespawnTime = 60f;
        public float CourierReach = 2.2f;
        public float PreGameTime = 60f;
        public float HeroSelectTime = 60f;
        public float CreepWaveInterval = 30f;
        public int SiegeEveryNWaves = 5;
        public float CreepUpgradeInterval = 450f;
        public float NeutralSpawnTime = 60f;
        public float NeutralRespawnInterval = 60f;
        public float XpShareRadius = 12.5f;
        public float DenyXpFactor = 0.5f;
        public float CreepDenyThreshold = 0.5f;
        public float AssistWindow = 15f;
        public float AssistRadius = 16f;
        public int HeroKillBaseGold = 110;
        public int HeroKillGoldPerLevel = 8;
        public int[] StreakBonusGold = { 0, 0, 0, 60, 120, 180, 240, 300, 360, 420, 480 };
        public int AssistGoldBase = 60;
        public int AssistGoldPerLevel = 6;
        public int FirstBloodBonus = 150;
        public float DeathGoldLossPerLevel = 25f;
        public float RespawnBase = 6f;
        public float RespawnPerLevel = 2.6f;
        public float BuybackBaseCost = 150f;
        public float BuybackCostPerLevel = 12f;
        public float BuybackCostPerMinute = 6f;
        public float BuybackCooldown = 360f;
        public float SellFraction = 0.5f;
        public float SellFullRefundWindow = 10f;
        public float ShopRange = 11f;
        public float FountainRegenPctPerSecond = 0.06f;
        public float FountainManaRegenPctPerSecond = 0.06f;
        public float DayLength = 300f;
        public float NightLength = 300f;
        public float StrHp = 19f;
        public float StrHpRegen = 0.03f;
        public float AgiArmor = 0.14f;
        public float AgiAttackSpeed = 1f;
        public float IntMana = 13f;
        public float IntManaRegen = 0.04f;
        public float MinAttackSpeed = -80f;
        public float MaxAttackSpeed = 500f;
        public float MinMoveSpeed = 1.3f;
        public float MaxMoveSpeed = 6.9f;
        public float StatusResistCap = 0.8f;
        public int InventorySlots = 6;
        public int StashSlots = 6;
        public int BackpackSlots = 3;
        public float ReconnectGraceSeconds = 300f;
        public float AbandonAfterSeconds = 300f;
        public float VharothMinTime = 1500f;
        /// <summary>Seconds after awakening before the Blood Moon rises (if Vharoth still lives).</summary>
        public float VharothBloodMoonDelay = 180f;
        /// <summary>Vision multiplier for everyone during the Blood Moon.</summary>
        public float VharothBloodMoonVision = 0.7f;
        /// <summary>Gold for every hero of the team that breaks a seal.</summary>
        public int VharothSealGold = 100;
        /// <summary>How far Vharoth follows targets from the centre of his pit before returning.</summary>
        public float VharothLeash = 12f;
        /// <summary>Seconds a reincarnation relic takes to revive its holder.</summary>
        public float ReincarnationDelay = 4f;
        /// <summary>Lane creep unit ids keyed "Dawn.melee", "Dusk.ranged", "Dawn.siege", "Dawn.melee.super", "Dawn.melee.mega"...</summary>
        public Dictionary<string, string> CreepUnits = new Dictionary<string, string>();
        public int WaveMelee = 3;
        public int WaveRanged = 1;
        public float FirstWaveTime = 0f;
        public float StashDeliveryInterval = 0.5f;
        public ExperienceDef Experience = new ExperienceDef();
        /// <summary>RTS: upkeep thresholds etc. are defined in the RTS mode file.</summary>
        public float RtsGameSpeed = 1f;
        public int RtsStartingGold = 500;
        public int RtsStartingLumber = 150;
        public int RtsMaxSupply = 100;
        public float RtsPreGameTime = 3f;
        /// <summary>Lumber in every tree; the tree falls when it is exhausted.</summary>
        public int RtsTreeLumber = 50;
        /// <summary>Workers that can mine one gold mine at the same time.</summary>
        public int RtsMineSlots = 1;
        public int RtsTrainQueueMax = 5;
        /// <summary>Fraction of the cost refunded when a building under construction is cancelled.</summary>
        public float RtsConstructionRefund = 0.75f;
        /// <summary>Hit points a building starts construction with (fraction of its maximum).</summary>
        public float RtsBuildStartHp = 0.1f;
        /// <summary>Construction speed each additional builder adds (the first builder is 1).</summary>
        public float RtsExtraBuilderRate = 0.5f;
        /// <summary>Free space kept between a gold mine and any building footprint.</summary>
        public float RtsMineClearance = 1.5f;
        /// <summary>Seconds a destroyed RTS building's ruin stays before it is removed.</summary>
        public float RtsRuinRemoveDelay = 3f;
        /// <summary>RTS hero altars: heroes a player may own at once (alive, dead or being recruited).</summary>
        public int RtsMaxHeroes = 3;
        /// <summary>Recruiting cost of a player's first, second and third hero (blood-iron, lumber).</summary>
        public int[] RtsHeroGold = { 200, 350, 500 };
        public int[] RtsHeroLumber = { 50, 100, 150 };
        public float RtsHeroTrainTime = 45f;
        public int RtsHeroSupply = 5;
        /// <summary>Reviving a fallen hero at an altar: base + per hero level.</summary>
        public int RtsReviveGold = 100;
        public int RtsReviveGoldPerLevel = 30;
        public float RtsReviveTime = 20f;
        public float RtsReviveTimePerLevel = 3f;
        /// <summary>Experience a player's RTS unit gives the enemy heroes nearby when it dies (per supply; buildings; halls).</summary>
        public int RtsXpPerSupply = 20;
        public int RtsBuildingXp = 60;
        public int RtsHallXp = 150;
    }

    public sealed class GameModeDef
    {
        public string Id;
        public string Name;
        public string Description;
        public GameModeKind Kind = GameModeKind.Moba;
        public string Map;
        public int TeamSize = 5;
        public HeroPickMode PickMode = HeroPickMode.AllPick;
        public bool Ranked;
        public bool AllowBots = true;
        public float GameSpeed = 1f;
        public Dictionary<string, float> RuleOverrides;
    }

    public sealed class RtsStartDef
    {
        public Team Team;
        public JVec2 Position;
        public float Facing;
    }

    public sealed class ResourceNodePlacement
    {
        public string Id;
        public string UnitId;
        public JVec2 Position;
        /// <summary>0 = the unit definition's ResourceAmount.</summary>
        public int Amount;
    }

    /// <summary>An RTS faction: what a player starts with and which content belongs to it.</summary>
    public sealed class RtsFactionDef
    {
        public string Id;
        public Faction Faction;
        public string Name;
        public string Description;
        /// <summary>Main building every player starts with (also the resource drop-off).</summary>
        public string Hall;
        public string Worker;
        public int StartingWorkers = 5;
        public List<string> StartingUnits = new List<string>();
        /// <summary>Faction mechanic, one line for the UI.</summary>
        public string Mechanic;
        public bool Playable = true;
        /// <summary>Ashen Legion: fallen living units near one of the faction's soldiers rise as this unit.</summary>
        public string RaiseUnit;
        public float RaiseRadius = 8f;
        /// <summary>Seconds a raised unit lasts (0 = permanent).</summary>
        public float RaiseLifetime;
        /// <summary>Minimum seconds between two raises for one player.</summary>
        public float RaiseCooldown;
        /// <summary>Wild Covenant: status every faction soldier has while it is night.</summary>
        public string NightStatus;
        /// <summary>Crimson Court: fraction of a slain enemy unit's blood-iron cost paid to the killer's owner.</summary>
        public float BloodPrice;
    }

    /// <summary>RTS research: a permanent status for every current and future unit it applies to.</summary>
    public sealed class UpgradeDef
    {
        public string Id;
        public string Name;
        public string Description;
        public string Hotkey;
        public int GoldCost;
        public int LumberCost;
        public float ResearchTime = 30f;
        /// <summary>Completed buildings required to start the research.</summary>
        public List<string> Requires;
        /// <summary>Upgrades that must be researched first (earlier levels).</summary>
        public List<string> RequiresUpgrades;
        /// <summary>Unit ids, unit tags, or: soldier, melee, ranged, siege, worker, building.</summary>
        public List<string> AppliesTo = new List<string>();
        public string Status;
    }

    public sealed class FactionDef
    {
        public Faction Id;
        public string Name;
        public string Motto;
        public string Description;
        public string PrimaryColor;
        public string SecondaryColor;
        public string Crest;
    }
}
