using System.Collections.Generic;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    public enum PlayerConnection : byte { Connected, Disconnected, Abandoned, Bot }

    /// <summary>Per-player match state: economy, statistics and connection. Owned exclusively by the server.</summary>
    public sealed class Player
    {
        public int Id;
        public string AccountId;
        public string Name;
        public Team Team;
        public int Slot;
        public string HeroId;
        public Unit Hero;
        public bool IsBot;
        public BotDifficulty BotDifficulty = BotDifficulty.Normal;
        public PlayerConnection Connection = PlayerConnection.Connected;
        public float DisconnectedAt;
        public float TotalDisconnectedTime;
        public bool HeroLocked;
        public bool Loaded;
        public float LoadProgress;
        public int PingMs;

        public int Gold;
        public int GoldEarned;
        public int GoldSpent;
        public int XpEarned;
        public int Kills, Deaths, Assists;
        public int LastHits, Denies;
        public int NeutralKills;
        public float HeroDamage, BuildingDamage, Healing, DamageTaken;
        public int WardsPlaced, WardsDestroyed;
        public int TowersDestroyed;
        public int KillStreak;
        public int BestKillStreak;
        public int MultiKillCount;
        public float LastKillTime = -999f;
        public float BuybackCooldownUntil;
        public int Buybacks;
        public int StunsSeconds;
        public readonly List<int> NetWorthTimeline = new List<int>();
        public readonly List<int> XpTimeline = new List<int>();
        public readonly List<string> ItemPurchaseLog = new List<string>();
        public string[] FinalItems = new string[0];

        // RTS economy and statistics (unused in the MOBA modes).
        public RtsFactionDef RtsFaction;
        public int Lumber;
        /// <summary>Supply used by living units plus units queued for training.</summary>
        public int SupplyUsed;
        public int SupplyCap;
        public int GoldMined, LumberHarvested;
        public int LumberSpent;
        public int UnitsTrained, UnitsLost, UnitsKilled;
        public int BuildingsBuilt, BuildingsLost, BuildingsRazed;
        /// <summary>True once the player has lost every building (RTS defeat).</summary>
        public bool Eliminated;
        /// <summary>Completed RTS research.</summary>
        public readonly HashSet<string> Upgrades = new HashSet<string>();
        public int UnitsRaised, BloodPriceEarned;
        public float NextRaiseAt;

        public bool IsActiveHuman => !IsBot && Connection == PlayerConnection.Connected;

        public float Gpm(float matchSeconds) => matchSeconds > 1 ? GoldEarned / (matchSeconds / 60f) : 0f;
        public float Xpm(float matchSeconds) => matchSeconds > 1 ? XpEarned / (matchSeconds / 60f) : 0f;
    }
}
