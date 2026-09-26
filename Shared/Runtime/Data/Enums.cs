using System;

namespace Bloodfall.Data
{
    /// <summary>The two MOBA sides. Neutral covers jungle creatures and Vharoth.</summary>
    public enum Team : byte { Dawn = 0, Dusk = 1, Neutral = 2, None = 255 }

    public enum Faction : byte { None, CrimsonCourt, AshenLegion, WildCovenant, Dawnguard }

    public enum UnitKind : byte
    {
        Hero, Creep, Tower, Barracks, Core, Fountain, Shop, Neutral, Boss, Summon, Ward, Illusion,
        // RTS
        Worker, Building,
        /// <summary>Map objectives that are interacted with rather than attacked (Vharoth's seals).</summary>
        Objective,
        /// <summary>RTS: trained combat units (player controlled, no lane behaviour).</summary>
        Soldier,
        /// <summary>RTS: harvestable resource nodes (gold mines). Invulnerable and never attacked.</summary>
        Resource,
        /// <summary>Blood War: a player's flying courier, which carries bought items from the stash to the hero.</summary>
        Courier,
    }

    public enum AttackType : byte { Melee, Ranged }
    public enum DamageType : byte { Physical, Magical, Pure }
    public enum PrimaryAttribute : byte { Strength, Agility, Intelligence }

    public enum ResourceType : byte { Mana, Health, Rage, Blood, Souls, Energy, None }

    [Flags]
    public enum TargetTeam : byte
    {
        None = 0,
        Enemy = 1,
        Ally = 2,
        Self = 4,
        Any = Enemy | Ally | Self,
        AllyNotSelf = Ally,
        AllyOrSelf = Ally | Self,
    }

    [Flags]
    public enum TargetType : ushort
    {
        None = 0,
        Hero = 1,
        Creep = 2,
        Structure = 4,
        Neutral = 8,
        Summon = 16,
        Ward = 32,
        Boss = 64,
        Illusion = 128,
        Courier = 256,
        /// <summary>Map objectives (Vharoth's seals). Not part of Units/All: only abilities that name it can target them.</summary>
        Objective = 512,
        /// <summary>RTS resource nodes. Not part of Units/All.</summary>
        Resource = 1024,
        Basic = Creep | Neutral | Summon | Boss | Illusion | Courier,
        Units = Hero | Basic,
        All = Units | Structure | Ward,
    }

    public enum TargetingMode : byte
    {
        Passive,
        NoTarget,
        Unit,
        Point,
        UnitOrPoint,
        /// <summary>Point-targeted with direction (click-drag). Uses TargetPoint + TargetPoint2.</summary>
        Vector,
        Toggle,
    }

    public enum AbilitySlot : byte { Innate, Q, W, E, R, Extra1, Extra2, Item }

    [Flags]
    public enum StatusFlags : uint
    {
        None = 0,
        Stunned = 1 << 0,
        Rooted = 1 << 1,
        Silenced = 1 << 2,
        Disarmed = 1 << 3,
        Feared = 1 << 4,
        Taunted = 1 << 5,
        Invisible = 1 << 6,
        Revealed = 1 << 7,
        Invulnerable = 1 << 8,
        MagicImmune = 1 << 9,
        Untargetable = 1 << 10,
        Hexed = 1 << 11,
        Frozen = 1 << 12,
        Blinded = 1 << 13,
        Muted = 1 << 14,
        Phased = 1 << 15,
        Airborne = 1 << 16,
        TrueSight = 1 << 17,
        BreakPassives = 1 << 18,
        Unselectable = 1 << 19,
        NoHealthBar = 1 << 20,
        Sleeping = 1 << 21,
        CannotDie = 1 << 22,
        Bloodbound = 1 << 23,
        FlyingVision = 1 << 24,
        Hidden = 1 << 25,

        /// <summary>
        /// Anything that prevents actions and movement entirely. Hex is deliberately excluded: hexed units can still walk
        /// (slowly); hex statuses carry Silenced/Disarmed/Muted to stop everything else.
        /// </summary>
        HardDisable = Stunned | Frozen | Sleeping,
    }

    public enum StatType : byte
    {
        MaxHp, HpRegen, MaxMana, ManaRegen, Armor, MagicResist,
        BaseDamage, BonusDamage, BonusDamagePct,
        AttackSpeed, AttackRange, MoveSpeed, MoveSpeedPct, CastRange,
        Str, Agi, Int, AllAttributes,
        Lifesteal, SpellVamp, Evasion, CritChance, CritMultiplier,
        CooldownReduction, StatusResist, SpellAmp, DamageTakenPct, OutgoingDamagePct,
        HealAmp, VisionDay, VisionNight, ManaCostReduction, HpRegenPct, SlowResist,
        MaxHpPct, BaseAttackTimeOverride, ArmorPct, HealthCostReduction, BonusVision, ManaRegenPct,
        Count
    }

    public enum StackingMode : byte { Refresh, Independent, Intensity, Longest }
    public enum DispelType : byte { None, Basic, Strong }

    public enum EffectType : byte
    {
        Damage, Heal, RestoreMana, ApplyStatus, RemoveStatus, Dispel,
        Area, Projectile, Dash, Leap, Blink, Knockback, Pull,
        Delayed, Zone, SpawnUnit, Teleport, Chance, Sequence,
        Execute, Swap, Reveal, ModifyCooldowns, SpendResource, CreateWall,
        GrantGold, AddCharges, Transform, ReduceMana, Illusion, Kill,
        SummonAtTarget, ConsumeCorpse, Resurrect,
        /// <summary>Re-runs the caster's last ability cast at <c>amount</c> power (Morwen's Witching Hour).</summary>
        EchoCast,
        /// <summary>Makes it night for everyone for <c>duration</c> seconds.</summary>
        ForceNight,
    }

    public enum EffectTarget : byte
    {
        /// <summary>The current target in the effect context (primary target or the unit iterated by an Area).</summary>
        Target,
        Caster,
        /// <summary>For effects that need a location, uses the cast point.</summary>
        Point,
        /// <summary>The unit that originally triggered a passive (attacker, killer, ...).</summary>
        TriggerSource,
        /// <summary>The unit that applied the status whose trigger or interval is running.</summary>
        StatusSource,
    }

    public enum AreaCenter : byte { Caster, Target, Point }

    public enum ProjectileKind : byte { Tracking, Linear }

    public enum TriggerType : byte
    {
        AttackLanded, AttackStart, Attacked, DamageTaken, DamageDealt, Kill, Death,
        NearbyDeath, AbilityCast, Interval, Respawn, SpellHit, LowHealth,
    }

    public enum ItemShop : byte { Main, Secret, Side, None }

    public enum MatchPhase : byte { WaitingForPlayers, HeroSelect, Loading, PreGame, Playing, PostGame, Aborted }

    public enum HeroPickMode : byte { AllPick, SingleDraft, RandomDraft, AllRandom, CaptainsMode, BlindPick }

    public enum GameModeKind : byte { Moba, Rts }

    public enum BotDifficulty : byte { Beginner, Normal, Veteran, Nightmare }

    public enum ActionState : byte
    {
        Idle, Moving, AttackWindup, AttackBackswing, CastWindup, CastBackswing, Channeling,
        Dashing, Airborne, Stunned, Dead, Teleporting,
        /// <summary>RTS workers mining, chopping or constructing.</summary>
        Working,
    }
}
