using System;
using System.Collections.Generic;
using System.Linq;
using Bloodfall.Data;
using Bloodfall.Simulation;

namespace Bloodfall.Protocol
{
    /// <summary>
    /// Authoritative end-of-match record. Produced by the game server from simulation state, reported to the
    /// statistics service (server-to-server, authenticated) and shown on the post-game screen. Clients never
    /// submit results.
    /// </summary>
    public sealed class MatchResult
    {
        public string MatchId;
        public string ModeId;
        public string MapId;
        public string Region;
        public bool Ranked;
        public string Winner;
        public float DurationSeconds;
        public DateTime StartedUtc;
        public DateTime EndedUtc;
        public string ContentHash;
        public string ServerId;
        public int[] TeamKills = new int[2];
        public List<MatchPlayerResult> Players = new List<MatchPlayerResult>();
    }

    public sealed class MatchPlayerResult
    {
        public string AccountId;
        public string Name;
        public string Team;
        public int Slot;
        public string HeroId;
        public bool IsBot;
        public bool Won;
        public bool Abandoned;
        public int Level;
        public int Kills, Deaths, Assists, LastHits, Denies, NeutralKills;
        public int GoldEarned, GoldSpent, NetWorth, Xp;
        public float Gpm, Xpm;
        public float HeroDamage, BuildingDamage, Healing, DamageTaken;
        public int WardsPlaced, WardsDestroyed, TowersDestroyed, Buybacks, BestKillStreak;
        public float DisconnectedSeconds;
        public string[] Items = new string[0];
        public List<int> NetWorthTimeline = new List<int>();
        public List<int> XpTimeline = new List<int>();
        /// <summary>Filled in by the statistics service on the post-game screen (rating delta, account XP).</summary>
        public int RatingChange;
        public int AccountXp;
    }

    public static class MatchResultBuilder
    {
        public static MatchResult Build(Match m, DateTime startedUtc, string region, string serverId)
        {
            var r = new MatchResult
            {
                MatchId = m.Config.MatchId,
                ModeId = m.Config.ModeId,
                MapId = m.Config.MapId,
                Region = region,
                Ranked = m.Config.Ranked,
                Winner = m.Winner.ToString(),
                DurationSeconds = Math.Max(0, m.EndTime > 0 ? m.EndTime : m.Time),
                StartedUtc = startedUtc,
                EndedUtc = DateTime.UtcNow,
                ContentHash = m.Data.ContentHash,
                ServerId = serverId,
                TeamKills = new[] { m.TeamKills[0], m.TeamKills[1] },
            };
            float secs = Math.Max(1f, r.DurationSeconds);
            foreach (var p in m.Players)
            {
                r.Players.Add(new MatchPlayerResult
                {
                    AccountId = p.AccountId,
                    Name = p.Name,
                    Team = p.Team.ToString(),
                    Slot = p.Slot,
                    HeroId = p.HeroId,
                    IsBot = p.IsBot,
                    Won = p.Team == m.Winner,
                    Abandoned = p.Connection == PlayerConnection.Abandoned,
                    Level = p.Hero?.Level ?? 1,
                    Kills = p.Kills, Deaths = p.Deaths, Assists = p.Assists, LastHits = p.LastHits, Denies = p.Denies, NeutralKills = p.NeutralKills,
                    GoldEarned = p.GoldEarned, GoldSpent = p.GoldSpent, NetWorth = m.NetWorth(p), Xp = p.Hero?.Xp ?? 0,
                    Gpm = p.GoldEarned / (secs / 60f), Xpm = p.XpEarned / (secs / 60f),
                    HeroDamage = p.HeroDamage, BuildingDamage = p.BuildingDamage, Healing = p.Healing, DamageTaken = p.DamageTaken,
                    WardsPlaced = p.WardsPlaced, WardsDestroyed = p.WardsDestroyed, TowersDestroyed = p.TowersDestroyed,
                    Buybacks = p.Buybacks, BestKillStreak = p.BestKillStreak, DisconnectedSeconds = p.TotalDisconnectedTime,
                    Items = p.FinalItems.Length > 0 ? p.FinalItems : p.Hero?.Inventory?.Where(i => i != null).Select(i => i.Def.Id).ToArray() ?? new string[0],
                    NetWorthTimeline = new List<int>(p.NetWorthTimeline),
                    XpTimeline = new List<int>(p.XpTimeline),
                });
            }
            return r;
        }
    }
}
