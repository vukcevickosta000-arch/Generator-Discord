using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bloodfall.Backend.Infrastructure;
using Bloodfall.Backend.Infrastructure.Data;
using Bloodfall.Backend.Infrastructure.Security;
using Bloodfall.Backend.Modules.Accounts;
using Bloodfall.Backend.Modules.Social;
using Bloodfall.Contracts;
using Bloodfall.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bloodfall.Backend.Modules.Stats
{
    /// <summary>
    /// Statistics service. The only writer of match history, ratings, profile statistics, account XP and
    /// achievements. Input is the authoritative <see cref="MatchResult"/> posted by a dedicated game server
    /// (server-key authenticated); client-supplied values are never accepted.
    /// </summary>
    public sealed class StatsService
    {
        private readonly BloodfallDb _db;
        private readonly ILogger<StatsService> _log;
        public const string Season = "S1";

        public StatsService(BloodfallDb db, ILogger<StatsService> log) { _db = db; _log = log; }

        public async Task<MatchReportResponse> Ingest(MatchResult r)
        {
            var resp = new MatchReportResponse { MatchId = r.MatchId };
            if (string.IsNullOrEmpty(r.MatchId)) throw new ArgumentException("matchId required");
            if (await _db.Matches.AnyAsync(m => m.Id == r.MatchId))
            {
                // Idempotent: a retried report returns the stored rewards.
                var stored = await _db.MatchPlayers.Where(p => p.MatchId == r.MatchId && p.AccountId != null).ToListAsync();
                resp.Rewards = stored.Select(p => new PlayerMatchReward { AccountId = p.AccountId.ToString(), RatingBefore = p.RatingBefore, RatingAfter = p.RatingBefore + p.RatingChange, AccountXp = p.AccountXp }).ToList();
                return resp;
            }
            bool counts = r.Winner == "Dawn" || r.Winner == "Dusk";
            _db.Matches.Add(new MatchRecord
            {
                Id = r.MatchId, ModeId = r.ModeId ?? "", MapId = r.MapId ?? "", Region = r.Region ?? "", Ranked = r.Ranked && counts, Winner = r.Winner ?? "None",
                DurationSeconds = r.DurationSeconds, StartedAt = r.StartedUtc == default ? DateTime.UtcNow : r.StartedUtc, EndedAt = r.EndedUtc == default ? DateTime.UtcNow : r.EndedUtc,
                ServerId = r.ServerId ?? "", ContentHash = r.ContentHash ?? "", KillsDawn = r.TeamKills?.ElementAtOrDefault(0) ?? 0, KillsDusk = r.TeamKills?.ElementAtOrDefault(1) ?? 0,
            });

            // Ratings (ranked only): team-average Elo.
            var accountIds = r.Players.Where(p => !p.IsBot && Guid.TryParse(p.AccountId, out _)).Select(p => Guid.Parse(p.AccountId)).ToList();
            var ratings = await _db.Ratings.Where(x => accountIds.Contains(x.AccountId) && x.Queue == "ranked" && x.Season == Season).ToDictionaryAsync(x => x.AccountId);
            foreach (var id in accountIds.Where(id => !ratings.ContainsKey(id)))
            {
                var nr = new PlayerRating { AccountId = id, Queue = "ranked", Season = Season };
                _db.Ratings.Add(nr);
                ratings[id] = nr;
            }
            double TeamAvg(string team) => r.Players.Where(p => p.Team == team).Select(p => !p.IsBot && Guid.TryParse(p.AccountId, out var g) && ratings.ContainsKey(g) ? ratings[g].Rating : 1500).DefaultIfEmpty(1500).Average();
            double dawnAvg = TeamAvg("Dawn"), duskAvg = TeamAvg("Dusk");

            foreach (var p in r.Players)
            {
                Guid? acc = !p.IsBot && Guid.TryParse(p.AccountId, out var g) ? g : (Guid?)null;
                var row = new MatchPlayerRecord
                {
                    MatchId = r.MatchId, AccountId = acc, Name = p.Name ?? "", Team = p.Team, Slot = p.Slot, HeroId = p.HeroId ?? "", IsBot = p.IsBot, Won = p.Won,
                    Abandoned = p.Abandoned, Level = p.Level, Kills = p.Kills, Deaths = p.Deaths, Assists = p.Assists, LastHits = p.LastHits, Denies = p.Denies,
                    GoldEarned = p.GoldEarned, NetWorth = p.NetWorth, Gpm = p.Gpm, Xpm = p.Xpm, HeroDamage = p.HeroDamage, BuildingDamage = p.BuildingDamage,
                    Healing = p.Healing, Wards = p.WardsPlaced, Towers = p.TowersDestroyed, Items = string.Join(",", p.Items ?? new string[0]),
                    NetWorthTimeline = string.Join(",", p.NetWorthTimeline ?? new List<int>()),
                    RtsFaction = p.RtsFaction ?? "", ResourcesGathered = p.GoldMined + p.LumberHarvested, UnitsTrained = p.UnitsTrained,
                    UnitsKilled = p.UnitsKilled, BuildingsRazed = p.BuildingsRazed,
                };
                if (acc != null)
                {
                    var reward = new PlayerMatchReward { AccountId = acc.ToString() };
                    var rating = ratings[acc.Value];
                    row.RatingBefore = rating.Rating;
                    reward.RatingBefore = rating.Rating;
                    if (r.Ranked && counts)
                    {
                        double mine = p.Team == "Dawn" ? dawnAvg : duskAvg, theirs = p.Team == "Dawn" ? duskAvg : dawnAvg;
                        double expected = 1.0 / (1.0 + Math.Pow(10, (theirs - mine) / 400.0));
                        double k = rating.Games < 10 ? 50 : 26;
                        int delta = (int)Math.Round(k * ((p.Won ? 1 : 0) - expected));
                        if (p.Abandoned) delta = -(int)k; // abandoning always costs the full K
                        rating.Rating = Math.Max(0, rating.Rating + delta);
                        rating.Peak = Math.Max(rating.Peak, rating.Rating);
                        rating.Games++;
                        if (p.Won) rating.Wins++; else rating.Losses++;
                        row.RatingChange = delta;
                    }
                    reward.RatingAfter = rating.Rating;

                    // Profile aggregates.
                    var st = await _db.Stats.FindAsync(acc.Value);
                    if (st == null) { st = new AccountStats { AccountId = acc.Value }; _db.Stats.Add(st); }
                    if (counts)
                    {
                        st.GamesPlayed++;
                        if (p.Won) st.Wins++; else st.Losses++;
                    }
                    if (p.Abandoned) st.Abandons++;
                    st.Kills += p.Kills; st.Deaths += p.Deaths; st.Assists += p.Assists;
                    st.SumGpm += p.Gpm; st.SumXpm += p.Xpm; st.WardsPlaced += p.WardsPlaced; st.TowersDestroyed += p.TowersDestroyed;
                    st.TotalSeconds += r.DurationSeconds;
                    st.BestGpm = Math.Max(st.BestGpm, p.Gpm); st.BestXpm = Math.Max(st.BestXpm, p.Xpm);

                    // Hero statistics only for matches played with a hero (not RTS games without one).
                    if (!string.IsNullOrEmpty(p.HeroId))
                    {
                        var hs = await _db.HeroStats.FindAsync(acc.Value, p.HeroId);
                        if (hs == null) { hs = new HeroStats { AccountId = acc.Value, HeroId = p.HeroId }; _db.HeroStats.Add(hs); }
                        hs.Games++; if (p.Won) hs.Wins++;
                        hs.Kills += p.Kills; hs.Deaths += p.Deaths; hs.Assists += p.Assists; hs.SumGpm += p.Gpm; hs.SumXpm += p.Xpm; hs.LastPlayed = DateTime.UtcNow;
                    }

                    // Account XP / level.
                    var account = await _db.Accounts.FindAsync(acc.Value);
                    int xp = p.Abandoned ? 0 : 100 + (p.Won ? 60 : 0) + (int)(r.DurationSeconds / 60f * 4);
                    row.AccountXp = xp;
                    reward.AccountXp = xp;
                    if (account != null)
                    {
                        account.Xp += xp;
                        while (account.Xp >= Ranks.XpForLevel(account.Level)) { account.Xp -= Ranks.XpForLevel(account.Level); account.Level++; }
                        reward.NewLevel = account.Level;
                    }
                    reward.AchievementsUnlocked = await Achievements(acc.Value, p, st, rating, r);
                    resp.Rewards.Add(reward);
                }
                _db.MatchPlayers.Add(row);
            }
            await _db.SaveChangesAsync();
            _log.LogInformation("Recorded match {Match}: winner {Winner}, {Players} players, {Dur:0}s", r.MatchId, r.Winner, r.Players.Count, r.DurationSeconds);
            return resp;
        }

        private async Task<List<string>> Achievements(Guid id, MatchPlayerResult p, AccountStats st, PlayerRating rating, MatchResult r)
        {
            var have = (await _db.Achievements.Where(a => a.AccountId == id).Select(a => a.AchievementId).ToListAsync()).ToHashSet();
            var unlocked = new List<string>();
            void Try(string aid, bool cond)
            {
                if (!cond || have.Contains(aid)) return;
                have.Add(aid);
                unlocked.Add(aid);
                _db.Achievements.Add(new AccountAchievement { AccountId = id, AchievementId = aid, UnlockedAt = DateTime.UtcNow });
            }
            Try("first_blood_drawn", p.Won);
            Try("ten_victories", st.Wins >= 10);
            Try("fifty_games", st.GamesPlayed >= 50);
            Try("rampage", p.BestKillStreak >= 10);
            Try("tower_breaker", p.TowersDestroyed >= 3);
            Try("warden", p.WardsPlaced >= 15);
            Try("farmer", p.LastHits >= 300);
            Try("flawless", p.Won && p.Deaths == 0 && r.DurationSeconds > 600);
            Try("ranked_debut", rating.Games >= 10);
            return unlocked;
        }

        public async Task<LeaderboardResponse> Leaderboard(string category, string region, Guid viewer, bool friendsOnly)
        {
            var resp = new LeaderboardResponse { Category = category ?? "rating", Region = region ?? "global", Season = Season };
            IQueryable<Account> accounts = _db.Accounts;
            if (!string.IsNullOrEmpty(region) && region != "global") accounts = accounts.Where(a => a.Region == region);
            if (friendsOnly)
            {
                var ids = await _db.Friendships.Where(f => f.AccountId == viewer).Select(f => f.FriendId).ToListAsync();
                ids.Add(viewer);
                accounts = accounts.Where(a => ids.Contains(a.Id));
            }
            var tagById = await _db.Clans.ToDictionaryAsync(c => c.Id, c => c.Tag);
            switch (resp.Category)
            {
                case "wins":
                case "gpm":
                case "xpm":
                {
                    var rows = await (from s in _db.Stats join a in accounts on s.AccountId equals a.Id where s.GamesPlayed > 0 select new { a, s }).ToListAsync();
                    IEnumerable<(Account a, float v, AccountStats s)> sorted = resp.Category switch
                    {
                        "wins" => rows.Select(x => (x.a, (float)x.s.Wins, x.s)),
                        "gpm" => rows.Select(x => (x.a, x.s.BestGpm, x.s)),
                        _ => rows.Select(x => (x.a, x.s.BestXpm, x.s)),
                    };
                    int pos = 0;
                    foreach (var x in sorted.OrderByDescending(x => x.v).Take(100))
                        resp.Entries.Add(new LeaderboardEntry { Position = ++pos, AccountId = x.a.Id.ToString(), DisplayName = x.a.DisplayName, ClanTag = x.a.ClanId != null ? tagById.GetValueOrDefault(x.a.ClanId.Value) : null, Value = x.v, Games = x.s.GamesPlayed, WinRate = x.s.GamesPlayed > 0 ? x.s.Wins / (float)x.s.GamesPlayed : 0 });
                    break;
                }
                case "clans":
                {
                    int pos = 0;
                    foreach (var c in await _db.Clans.OrderByDescending(c => c.Rating).Take(100).ToListAsync())
                        resp.Entries.Add(new LeaderboardEntry { Position = ++pos, AccountId = c.Id.ToString(), DisplayName = c.Name, ClanTag = c.Tag, Value = c.Rating, Games = c.Wins + c.Losses, WinRate = c.Wins + c.Losses > 0 ? c.Wins / (float)(c.Wins + c.Losses) : 0 });
                    break;
                }
                default:
                {
                    var rows = await (from r in _db.Ratings join a in accounts on r.AccountId equals a.Id where r.Queue == "ranked" && r.Season == Season && r.Games >= 1 orderby r.Rating descending select new { a, r }).Take(100).ToListAsync();
                    int pos = 0;
                    foreach (var x in rows)
                        resp.Entries.Add(new LeaderboardEntry { Position = ++pos, AccountId = x.a.Id.ToString(), DisplayName = x.a.DisplayName, ClanTag = x.a.ClanId != null ? tagById.GetValueOrDefault(x.a.ClanId.Value) : null, Value = x.r.Rating, Rank = x.r.Games < 10 ? "Unranked" : Ranks.For(x.r.Rating).tier, Games = x.r.Games, WinRate = x.r.Games > 0 ? x.r.Wins / (float)x.r.Games : 0 });
                    break;
                }
            }
            return resp;
        }

        public async Task<MatchDetail> Detail(string matchId)
        {
            var m = await _db.Matches.FindAsync(matchId);
            if (m == null) return null;
            var players = await _db.MatchPlayers.Where(p => p.MatchId == matchId).OrderBy(p => p.Team).ThenBy(p => p.Slot).ToListAsync();
            return new MatchDetail
            {
                MatchId = m.Id, ModeId = m.ModeId, MapId = m.MapId, Region = m.Region, Winner = m.Winner, Ranked = m.Ranked, DurationSeconds = m.DurationSeconds, EndedAt = m.EndedAt,
                TeamKills = new[] { m.KillsDawn, m.KillsDusk },
                Players = players.Select(p => new MatchDetailPlayer
                {
                    AccountId = p.AccountId?.ToString(), Name = p.Name, Team = p.Team, HeroId = p.HeroId, IsBot = p.IsBot, Abandoned = p.Abandoned, Level = p.Level,
                    Kills = p.Kills, Deaths = p.Deaths, Assists = p.Assists, LastHits = p.LastHits, Denies = p.Denies, NetWorth = p.NetWorth, Gpm = p.Gpm, Xpm = p.Xpm,
                    HeroDamage = p.HeroDamage, BuildingDamage = p.BuildingDamage, Healing = p.Healing, Wards = p.Wards, Towers = p.Towers,
                    Items = string.IsNullOrEmpty(p.Items) ? new string[0] : p.Items.Split(','), RatingChange = p.RatingChange,
                    RtsFaction = string.IsNullOrEmpty(p.RtsFaction) ? null : p.RtsFaction, ResourcesGathered = p.ResourcesGathered, UnitsTrained = p.UnitsTrained,
                    UnitsKilled = p.UnitsKilled, BuildingsRazed = p.BuildingsRazed,
                    NetWorthTimeline = string.IsNullOrEmpty(p.NetWorthTimeline) ? new List<int>() : p.NetWorthTimeline.Split(',').Select(int.Parse).ToList(),
                }).ToList(),
            };
        }
    }

    public static class StatsEndpoints
    {
        public static void MapStats(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/stats/matches", async (MatchResult r, StatsService s, Directory.DirectoryService directory) =>
            {
                if (r == null || string.IsNullOrEmpty(r.MatchId) || r.Players == null) return Api.BadRequest("invalid", "Match result is incomplete.");
                var resp = await s.Ingest(r);
                // The result is final: release the lobby now instead of waiting for the server's next heartbeat, so
                // players can start another game straight away.
                directory.NotifyMatchReported(r.MatchId);
                return Api.Ok(resp);
            }).AddEndpointFilter<GameServerKeyFilter>();
            var g = app.MapGroup("/api/stats").RequireAuthorization();
            g.MapGet("/leaderboards", async (string category, string region, bool? friends, StatsService s, HttpContext c) => Api.Ok(await s.Leaderboard(category, region, c.User.AccountId(), friends ?? false)));
            g.MapGet("/matches/{id}", async (string id, StatsService s) => await s.Detail(id) is MatchDetail d ? Api.Ok(d) : Api.NotFound("Match not found."));
        }
    }
}
