using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bloodfall.Backend.Infrastructure;
using Bloodfall.Backend.Infrastructure.Data;
using Bloodfall.Backend.Infrastructure.Security;
using Bloodfall.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Bloodfall.Backend.Modules.Accounts
{
    /// <summary>Account service: profiles, statistics, match history. All numbers come from stored match data.</summary>
    public sealed class AccountService
    {
        private readonly BloodfallDb _db;
        public const string Season = "S1";

        public AccountService(BloodfallDb db) { _db = db; }

        public async Task<AccountSummary> Summary(Account a)
        {
            var rating = await _db.Ratings.FirstOrDefaultAsync(r => r.AccountId == a.Id && r.Queue == "ranked" && r.Season == Season);
            var (tier, div, _) = Ranks.For(rating?.Rating ?? 1500);
            string tag = null;
            if (a.ClanId != null) tag = await _db.Clans.Where(c => c.Id == a.ClanId).Select(c => c.Tag).FirstOrDefaultAsync();
            bool provisional = (rating?.Games ?? 0) < 10;
            return new AccountSummary
            {
                AccountId = a.Id.ToString(),
                Username = a.Username,
                DisplayName = a.DisplayName,
                Level = a.Level,
                Xp = a.Xp,
                XpForNextLevel = Ranks.XpForLevel(a.Level),
                Rating = rating?.Rating ?? 1500,
                Rank = provisional ? "Unranked" : tier,
                RankDivision = provisional ? 0 : div,
                ClanTag = tag,
                AvatarId = a.AvatarId,
                Region = a.Region,
                EmailVerified = a.EmailVerified,
                Role = a.Role.ToString(),
            };
        }

        public async Task<Profile> BuildProfile(Guid accountId, Guid viewer)
        {
            var a = await _db.Accounts.FindAsync(accountId);
            if (a == null) return null;
            var p = new Profile
            {
                Account = await Summary(a),
                RegisteredAt = a.CreatedAt,
                StatusText = a.StatusText,
                FavoriteHeroes = string.IsNullOrEmpty(a.FavoriteHeroes) ? new List<string>() : a.FavoriteHeroes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            };
            if (a.ClanId != null) p.ClanName = await _db.Clans.Where(c => c.Id == a.ClanId).Select(c => c.Name).FirstOrDefaultAsync();
            var rating = await _db.Ratings.FirstOrDefaultAsync(r => r.AccountId == a.Id && r.Queue == "ranked" && r.Season == Season) ?? new PlayerRating();
            var (tier, div, progress) = Ranks.For(rating.Rating);
            p.Rank = new RankInfo
            {
                Tier = rating.Games < 10 ? "Unranked" : tier, Division = rating.Games < 10 ? 0 : div, Rating = rating.Rating, Peak = rating.Peak,
                Games = rating.Games, Wins = rating.Wins, Losses = rating.Losses, Season = Season, Provisional = rating.Games < 10, ProgressToNext = progress,
            };
            var s = await _db.Stats.FindAsync(a.Id) ?? new AccountStats();
            int g = Math.Max(1, s.GamesPlayed);
            p.Stats = new ProfileStats
            {
                GamesPlayed = s.GamesPlayed, Wins = s.Wins, Losses = s.Losses, Abandons = s.Abandons,
                WinRate = s.GamesPlayed > 0 ? s.Wins / (float)s.GamesPlayed : 0f,
                Kills = s.Kills, Deaths = s.Deaths, Assists = s.Assists,
                AvgKills = s.Kills / (float)g, AvgDeaths = s.Deaths / (float)g, AvgAssists = s.Assists / (float)g,
                AvgGpm = (float)(s.SumGpm / g), AvgXpm = (float)(s.SumXpm / g),
                WardsPlaced = s.WardsPlaced, TowersDestroyed = s.TowersDestroyed, HoursPlayed = (float)(s.TotalSeconds / 3600.0),
            };
            p.Heroes = (await _db.HeroStats.Where(h => h.AccountId == a.Id).OrderByDescending(h => h.Games).Take(20).ToListAsync())
                .Select(h => new HeroStatLine
                {
                    HeroId = h.HeroId, Games = h.Games, Wins = h.Wins, WinRate = h.Games > 0 ? h.Wins / (float)h.Games : 0,
                    Kda = (h.Kills + h.Assists) / (float)Math.Max(1, h.Deaths), AvgGpm = (float)(h.SumGpm / Math.Max(1, h.Games)),
                    AvgXpm = (float)(h.SumXpm / Math.Max(1, h.Games)), LastPlayed = h.LastPlayed,
                }).ToList();
            p.RecentMatches = await History(a.Id, 0, 10);
            var unlocked = await _db.Achievements.Where(x => x.AccountId == a.Id).ToListAsync();
            p.Achievements = AchievementCatalog.All.Select(def => new AchievementView
            {
                Id = def.Id, Name = def.Name, Description = def.Description,
                UnlockedAt = unlocked.FirstOrDefault(u => u.AchievementId == def.Id)?.UnlockedAt,
            }).ToList();
            p.Commendations = await _db.Commendations.CountAsync(c => c.ToId == a.Id);
            if (viewer != Guid.Empty && viewer != a.Id)
            {
                p.IsFriend = await _db.Friendships.AnyAsync(f => f.AccountId == viewer && f.FriendId == a.Id);
                p.IsBlocked = await _db.Blocks.AnyAsync(b => b.AccountId == viewer && b.BlockedId == a.Id);
            }
            return p;
        }

        public async Task<List<MatchSummary>> History(Guid accountId, int page, int pageSize)
        {
            var rows = await (from mp in _db.MatchPlayers
                              join m in _db.Matches on mp.MatchId equals m.Id
                              where mp.AccountId == accountId
                              orderby m.EndedAt descending
                              select new { mp, m }).Skip(page * pageSize).Take(pageSize).ToListAsync();
            return rows.Select(x => new MatchSummary
            {
                MatchId = x.m.Id, EndedAt = x.m.EndedAt, DurationSeconds = x.m.DurationSeconds, ModeId = x.m.ModeId, MapId = x.m.MapId, Ranked = x.m.Ranked,
                HeroId = x.mp.HeroId, Won = x.mp.Won, Abandoned = x.mp.Abandoned, Kills = x.mp.Kills, Deaths = x.mp.Deaths, Assists = x.mp.Assists,
                LastHits = x.mp.LastHits, Denies = x.mp.Denies, NetWorth = x.mp.NetWorth, Gpm = x.mp.Gpm, Xpm = x.mp.Xpm,
                HeroDamage = x.mp.HeroDamage, BuildingDamage = x.mp.BuildingDamage, Healing = x.mp.Healing, Wards = x.mp.Wards, Towers = x.mp.Towers,
                Items = string.IsNullOrEmpty(x.mp.Items) ? new string[0] : x.mp.Items.Split(','), RatingChange = x.mp.RatingChange,
            }).ToList();
        }

        public async Task<IResult> UpdateProfile(Guid id, UpdateProfileRequest r)
        {
            var a = await _db.Accounts.FindAsync(id);
            if (a == null) return Api.NotFound("Account not found.");
            if (r.DisplayName != null)
            {
                var err = Validation.DisplayName(r.DisplayName);
                if (err != null) return Api.BadRequest("invalid_display_name", err, "displayName");
                a.DisplayName = r.DisplayName.Trim();
                a.SecurityStamp++;
            }
            if (r.StatusText != null) a.StatusText = Validation.CleanText(r.StatusText, 80);
            if (r.AvatarId != null) a.AvatarId = Validation.CleanText(r.AvatarId, 40);
            if (r.FavoriteHeroes != null) a.FavoriteHeroes = string.Join(",", r.FavoriteHeroes.Take(5).Select(h => Validation.CleanText(h, 40)));
            if (r.Region != null) a.Region = Validation.CleanText(r.Region, 16);
            await _db.SaveChangesAsync();
            return Api.Ok(await Summary(a));
        }

        public async Task<IResult> Search(string q, Guid viewer)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length < 2) return Api.Ok(new List<FriendView>());
            var n = Validation.Normalize(q);
            var hits = await _db.Accounts.Where(a => a.UsernameNormalized.StartsWith(n) || a.DisplayName.ToUpper().StartsWith(n)).Take(20).ToListAsync();
            return Api.Ok(hits.Select(a => new FriendView { AccountId = a.Id.ToString(), DisplayName = a.DisplayName, Username = a.Username, Level = a.Level }).ToList());
        }
    }

    public sealed record AchievementDef(string Id, string Name, string Description);

    public static class AchievementCatalog
    {
        public static readonly AchievementDef[] All =
        {
            new("first_blood_drawn", "Blood Drawn", "Win your first match."),
            new("ten_victories", "Veteran of Velmoragh", "Win 10 matches."),
            new("fifty_games", "Oathsworn", "Play 50 matches."),
            new("rampage", "Carnage Unbound", "Reach a 10-kill streak in one match."),
            new("tower_breaker", "Spire Breaker", "Destroy 3 towers in one match."),
            new("warden", "Eyes in the Dark", "Place 15 wards in one match."),
            new("farmer", "Harvest of Souls", "Last hit 300 units in one match."),
            new("flawless", "Untouched", "Win a match without dying."),
            new("ranked_debut", "Blood Oath", "Complete your ranked placement matches."),
        };
    }

    public static class AccountEndpoints
    {
        public static void MapAccounts(this IEndpointRouteBuilder app)
        {
            var g = app.MapGroup("/api/accounts").RequireAuthorization();
            g.MapGet("/me", async (AccountService s, BloodfallDb db, HttpContext c) =>
            {
                var a = await db.Accounts.FindAsync(c.User.AccountId());
                return a == null ? Api.NotFound("Account not found.") : Api.Ok(await s.Summary(a));
            });
            g.MapGet("/me/profile", async (AccountService s, HttpContext c) => Api.Ok(await s.BuildProfile(c.User.AccountId(), c.User.AccountId())));
            g.MapPatch("/me", (UpdateProfileRequest r, AccountService s, HttpContext c) => s.UpdateProfile(c.User.AccountId(), r));
            g.MapGet("/{id:guid}/profile", async (Guid id, AccountService s, HttpContext c) =>
            {
                var p = await s.BuildProfile(id, c.User.AccountId());
                return p == null ? Api.NotFound("Player not found.") : Api.Ok(p);
            });
            g.MapGet("/{id:guid}/matches", async (Guid id, int? page, AccountService s) => Api.Ok(await s.History(id, Math.Max(0, page ?? 0), 20)));
            g.MapGet("/search", (string q, AccountService s, HttpContext c) => s.Search(q, c.User.AccountId()));
        }
    }
}
