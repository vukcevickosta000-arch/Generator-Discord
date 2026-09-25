using System.Numerics;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    public enum SimEventType : byte
    {
        AttackStart = 1,
        AttackLanded = 2,
        ProjectileLaunch = 3,
        ProjectileHit = 4,
        CastStart = 5,
        CastComplete = 6,
        CastCancelled = 7,
        Damage = 8,
        Heal = 9,
        Death = 10,
        Deny = 11,
        LastHitGold = 12,
        LevelUp = 13,
        StatusApplied = 14,
        StatusRemoved = 15,
        Announcer = 16,
        KillFeed = 17,
        StructureDestroyed = 18,
        ItemPurchased = 19,
        ItemSold = 20,
        Error = 21,
        Ping = 22,
        ZoneCreated = 23,
        ZoneEnded = 24,
        DashStart = 25,
        DashEnd = 26,
        Blink = 27,
        Respawn = 28,
        MatchPhase = 29,
        GoldChange = 30,
        XpGain = 31,
        EffectVisual = 32,
        ChannelStart = 33,
        ChannelEnd = 34,
        Miss = 35,
        Crit = 36,
        Spawn = 37,
        VharothEvent = 38,
        PlayerConnection = 39,
        Chat = 40,
        TreeDestroyed = 41,
        ItemUsed = 42,
        AbilityLeveled = 43,
        DayNight = 44,
        Buyback = 45,
        WaveSpawned = 46,
        Shake = 47,
    }

    /// <summary>
    /// One-shot occurrences produced by the simulation. The server forwards them (visibility filtered) to clients,
    /// which turn them into animation triggers, VFX, sounds, floating text and announcer lines.
    /// </summary>
    public struct SimEvent
    {
        public SimEventType Type;
        public int Tick;
        public int UnitId;
        public int OtherId;
        /// <summary>Ability / item / status / announcer id depending on type.</summary>
        public string Key;
        public float Value;
        public float Value2;
        public Vector2 Point;
        public Vector2 Point2;
        public byte Flags;
        public Team Team;
        /// <summary>-1 = broadcast; otherwise only this player receives it (errors, private gold).</summary>
        public int PlayerId;

        public const byte FlagCrit = 1, FlagMagical = 2, FlagPure = 4, FlagLastHit = 8, FlagDeny = 16, FlagHero = 32, FlagSpell = 64, FlagPhysical = 128;

        public override string ToString() => $"[{Tick}] {Type} u={UnitId} o={OtherId} key={Key} v={Value:0.#} p={PlayerId}";
    }

    /// <summary>Announcer keys (the client maps them to original Bloodfall voice lines and banners).</summary>
    public static class AnnouncerKeys
    {
        public const string FirstBlood = "first_blood";
        public const string DoubleKill = "double_kill";
        public const string TripleKill = "triple_kill";
        public const string QuadKill = "quad_kill";
        public const string Annihilation = "annihilation";
        public const string StreakPrefix = "streak_";           // streak_3 .. streak_10
        public const string Shutdown = "shutdown";
        public const string TeamWipe = "team_wipe";
        public const string TowerDestroyedAlly = "tower_fallen_ally";
        public const string TowerDestroyedEnemy = "tower_fallen_enemy";
        public const string BarracksDestroyedAlly = "barracks_fallen_ally";
        public const string BarracksDestroyedEnemy = "barracks_fallen_enemy";
        public const string Victory = "victory";
        public const string Defeat = "defeat";
        public const string BattleBegins = "battle_begins";
        public const string CreepsSpawned = "creeps_spawned";
        public const string PlayerDisconnected = "player_disconnected";
        public const string PlayerReconnected = "player_reconnected";
        public const string PlayerAbandoned = "player_abandoned";
        public const string VharothTremor = "vharoth_tremor";
        public const string VharothSealBroken = "vharoth_seal_broken";
        public const string VharothAwakened = "vharoth_awakened";
        public const string VharothBloodMoon = "vharoth_blood_moon";
        public const string VharothSlain = "vharoth_slain";
        public const string Denied = "denied";
        public const string Nightfall = "nightfall";
        public const string Daybreak = "daybreak";
        public const string MegaCreeps = "mega_creeps";
    }
}
