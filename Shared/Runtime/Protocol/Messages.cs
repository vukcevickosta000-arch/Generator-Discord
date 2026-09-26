using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bloodfall.Data;
using Bloodfall.Simulation;

namespace Bloodfall.Protocol
{
    public static class ProtocolInfo
    {
        /// <summary>Bump when the wire format changes. Clients with a different version are rejected with a clear message.</summary>
        public const ushort Version = 8;
        public const string ConnectionKey = "bloodfall";
        public const int DefaultGamePort = 27015;
    }

    public enum MsgType : byte
    {
        // client -> server
        Hello = 1,
        Command = 2,
        Chat = 3,
        LoadProgress = 4,
        PickHero = 5,
        Ping = 6,
        Leave = 7,

        // server -> client
        Welcome = 64,
        Reject = 65,
        MatchState = 66,
        Snapshot = 67,
        Events = 68,
        ChatBroadcast = 69,
        Pong = 70,
        MatchEnd = 71,
    }

    /// <summary>
    /// Stable integer ids for every definition, computed identically on both ends from the same (hash-checked)
    /// game data. Lets snapshots send small numbers instead of strings.
    /// </summary>
    public sealed class ContentIndex
    {
        public readonly List<string> Units = new List<string>();
        public readonly List<string> Abilities = new List<string>();
        public readonly List<string> Statuses = new List<string>();
        public readonly List<string> Items = new List<string>();
        public readonly List<string> Upgrades = new List<string>();
        private readonly Dictionary<string, int> _upgrade = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _unit = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _ability = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _status = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _item = new Dictionary<string, int>();

        public ContentIndex(GameData data)
        {
            Units.AddRange(data.Heroes.Keys.Concat(data.Units.Keys).Distinct().OrderBy(s => s, StringComparer.Ordinal));
            Abilities.AddRange(data.Abilities.Keys.OrderBy(s => s, StringComparer.Ordinal));
            Statuses.AddRange(data.Statuses.Keys.OrderBy(s => s, StringComparer.Ordinal));
            Items.AddRange(data.Items.Keys.OrderBy(s => s, StringComparer.Ordinal));
            Upgrades.AddRange(data.Upgrades.Keys.OrderBy(s => s, StringComparer.Ordinal));
            for (int i = 0; i < Units.Count; i++) _unit[Units[i]] = i + 1;
            for (int i = 0; i < Abilities.Count; i++) _ability[Abilities[i]] = i + 1;
            for (int i = 0; i < Statuses.Count; i++) _status[Statuses[i]] = i + 1;
            for (int i = 0; i < Items.Count; i++) _item[Items[i]] = i + 1;
            for (int i = 0; i < Upgrades.Count; i++) _upgrade[Upgrades[i]] = i + 1;
        }

        public int UnitId(string id) => id != null && _unit.TryGetValue(id, out var v) ? v : 0;
        public int AbilityId(string id) => id != null && _ability.TryGetValue(id, out var v) ? v : 0;
        public int StatusId(string id) => id != null && _status.TryGetValue(id, out var v) ? v : 0;
        public int ItemId(string id) => id != null && _item.TryGetValue(id, out var v) ? v : 0;
        public int UpgradeId(string id) => id != null && _upgrade.TryGetValue(id, out var v) ? v : 0;
        public string Unit(int i) => i > 0 && i <= Units.Count ? Units[i - 1] : null;
        public string Ability(int i) => i > 0 && i <= Abilities.Count ? Abilities[i - 1] : null;
        public string Status(int i) => i > 0 && i <= Statuses.Count ? Statuses[i - 1] : null;
        public string Item(int i) => i > 0 && i <= Items.Count ? Items[i - 1] : null;
        public string Upgrade(int i) => i > 0 && i <= Upgrades.Count ? Upgrades[i - 1] : null;
    }

    // ============================================================== client-side views

    [Flags]
    public enum EntityFlags : ushort
    {
        None = 0, Dead = 1, Invulnerable = 2, MagicImmune = 4, Invisible = 8, Stunned = 16, Silenced = 32, Rooted = 64,
        Hexed = 128, Illusion = 256, Airborne = 512, Moving = 1024, Hero = 2048, Structure = 4096, Disarmed = 8192, Channeling = 16384, Feared = 32768,
    }

    public struct StatusView
    {
        public string Id;
        public float Remaining;
        public float Duration;
        public int Stacks;
    }

    public sealed class EntityState
    {
        public int Id;
        public string DefId;
        /// <summary>Model override from a status (hex, transforms); null when the unit shows its definition's model.</summary>
        public string ModelKey;
        public UnitKind Kind;
        public Team Team;
        public EntityFlags Flags;
        public Vector2 Position;
        public float Facing;
        public float Height;
        public float Hp, MaxHp, Mana, MaxMana;
        public int Level;
        public ActionState Action;
        public int ActionStartTick;
        public string ActionAbilityId;
        public int ActionTargetId;
        public float AttackPoint;
        public float AttackTime;
        public float MoveSpeed;
        public int OwnerPlayer = -1;
        public float Armor;
        public float Damage;
        public List<StatusView> Statuses = new List<StatusView>();
        public List<AbilityView> HeroAbilities;
        public string[] HeroItems;
        // RTS
        public bool UnderConstruction;
        public float BuildProgress = 1f;
        /// <summary>Units queued in a building (own team only; null otherwise).</summary>
        public string[] TrainQueue;
        public float TrainProgress;
        /// <summary>Rally point of a friendly building, if set.</summary>
        public Vector2? Rally;
        public int CarryGold, CarryLumber;
        /// <summary>Blood-iron left in a vein.</summary>
        public int ResourceAmount;

        public bool Has(EntityFlags f) => (Flags & f) != 0;
        public EntityState Clone()
        {
            var c = (EntityState)MemberwiseClone();
            c.Statuses = new List<StatusView>(Statuses);
            return c;
        }
    }

    public struct AbilityView
    {
        public string Id;
        public int Level;
        public float Cooldown;
        public float CooldownTotal;
        public int Charges;
        public bool Toggled;
        public bool CanLevel;
        public float ManaCost;
        public float HealthCost;
        public bool Ready;
    }

    public struct ItemView
    {
        public string Id;
        public int Charges;
        public float Cooldown;
        public float CooldownTotal;
        public int SellValue;
    }

    public sealed class PlayerView
    {
        public int Id;
        public string Name;
        public string AccountId;
        public Team Team;
        public int Slot;
        public string HeroId;
        public bool HeroLocked;
        public int HeroUnitId;
        public bool IsBot;
        public PlayerConnection Connection;
        public int Level;
        public int Kills, Deaths, Assists, LastHits, Denies;
        /// <summary>-1 when hidden by fog-of-war rules (enemy net worth).</summary>
        public int NetWorth = -1;
        public float RespawnIn;
        public int PingMs;
        public float LoadProgress;
        public float Gpm, Xpm;
        /// <summary>RTS faction id (null in the MOBA).</summary>
        public string RtsFaction;
        public bool Eliminated;
    }

    /// <summary>The viewing player's RTS economy (only sent to that player).</summary>
    public sealed class RtsPrivateState
    {
        public int Gold, Lumber, SupplyUsed, SupplyCap;
        /// <summary>Research the player has completed (upgrade ids).</summary>
        public List<string> Upgrades = new List<string>();
        /// <summary>The player's heroes from altars, alive or awaiting revival (v7).</summary>
        public List<RtsHeroState> Heroes = new List<RtsHeroState>();
    }

    public sealed class RtsHeroState
    {
        public int UnitId;
        public string HeroId;
        public int Level;
        public bool Dead;
        public int Xp, XpLevelStart, XpNextLevel;
        public int AbilityPoints;
        /// <summary>Bit i set: ability i (the hero's own abilities, in order) can be learned or raised now.</summary>
        public int CanLevelMask;
        public bool CanLevel(int index) => index >= 0 && index < 31 && (CanLevelMask & (1 << index)) != 0;
    }

    public sealed class PrivateState
    {
        /// <summary>v8: the player's courier (0 = none), what it is doing, its respawn countdown and items aboard.</summary>
        public int CourierId;
        public byte CourierState;
        public float CourierRespawnIn;
        public int CourierCarried;
        public int Gold;
        public int Xp;
        public int XpLevelStart;
        public int XpNextLevel;
        public int AbilityPoints;
        public int BuybackCost;
        public float BuybackCooldown;
        public bool AtBase;
        public bool NearSecretShop;
        public AbilityView[] Abilities = new AbilityView[0];
        public ItemView[] Items = new ItemView[16];
    }

    public sealed class SnapshotFrame
    {
        public int Tick;
        public float Time;
        public MatchPhase Phase;
        public float PhaseTimer;
        public bool IsNight;
        public float DayNightRemaining;
        public int[] TeamKills = new int[2];
        /// <summary>Vharoth event state (see <see cref="Bloodfall.Simulation.VharothPhase"/>) and seals broken so far.</summary>
        public byte VharothPhase;
        public byte VharothSeals;
        public readonly List<EntityState> Entities = new List<EntityState>();
        public readonly List<PlayerView> Players = new List<PlayerView>();
        public PrivateState Me;
        /// <summary>RTS matches: the viewer's resources and supply.</summary>
        public RtsPrivateState Rts;
    }

    public sealed class WelcomeInfo
    {
        public int PlayerId;
        public Team Team;
        public bool Spectator;
        public string MatchId;
        public string MapId;
        public string ModeId;
        public int TickRate;
        public int ServerTick;
        public bool Reconnected;
    }

    public sealed class MatchStateInfo
    {
        public MatchPhase Phase;
        public float PhaseTimer;
        public List<PlayerView> Players = new List<PlayerView>();
    }

    public sealed class NetEvent
    {
        public SimEventType Type;
        public int Tick;
        public int UnitId;
        public int OtherId;
        public string Key;
        public float Value;
        public float Value2;
        public Vector2 Point;
        public Vector2 Point2;
        public byte Flags;
        public Team Team;
    }

    public sealed class ChatMessage
    {
        public int PlayerId;
        public string Name;
        public bool TeamOnly;
        public string Text;
        public Team Team;
    }
}
