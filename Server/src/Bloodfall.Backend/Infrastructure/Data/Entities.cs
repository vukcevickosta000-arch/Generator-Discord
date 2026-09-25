using System;

namespace Bloodfall.Backend.Infrastructure.Data
{
    public enum AccountStatus { Active = 0, Suspended = 1, Banned = 2 }
    public enum AccountRole { Player = 0, Moderator = 1, Admin = 2 }
    public enum EmailTokenPurpose { VerifyEmail = 0, ResetPassword = 1 }
    public enum FriendRequestStatus { Pending = 0, Accepted = 1, Declined = 2, Cancelled = 3 }
    public enum ClanRank { Recruit = 0, Member = 1, Officer = 2, Leader = 3 }

    public class Account
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = "";
        public string UsernameNormalized { get; set; } = "";
        public string Email { get; set; } = "";
        public string EmailNormalized { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public bool EmailVerified { get; set; }
        public AccountStatus Status { get; set; }
        public DateTime? SuspendedUntil { get; set; }
        public AccountRole Role { get; set; }
        public string Region { get; set; } = "eu";
        public string Language { get; set; } = "en";
        public int Level { get; set; } = 1;
        public int Xp { get; set; }
        public string AvatarId { get; set; } = "avatar_default";
        public string StatusText { get; set; } = "";
        public Guid? ClanId { get; set; }
        public string FavoriteHeroes { get; set; } = "";
        public bool TwoFactorEnabled { get; set; }
        public int FailedLogins { get; set; }
        public DateTime? LockoutUntil { get; set; }
        public int SecurityStamp { get; set; }
    }

    public class RefreshToken
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        /// <summary>SHA-256 of the opaque token (the raw token is never stored).</summary>
        public string TokenHash { get; set; } = "";
        public Guid FamilyId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public Guid? ReplacedById { get; set; }
        public string UserAgent { get; set; } = "";
        public string Ip { get; set; } = "";
    }

    public class EmailToken
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public EmailTokenPurpose Purpose { get; set; }
        public string TokenHash { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public DateTime? UsedAt { get; set; }
    }

    public class FriendRequest
    {
        public Guid Id { get; set; }
        public Guid FromId { get; set; }
        public Guid ToId { get; set; }
        public FriendRequestStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>One row per direction (A->B and B->A) for simple queries.</summary>
    public class Friendship
    {
        public Guid AccountId { get; set; }
        public Guid FriendId { get; set; }
        public DateTime Since { get; set; }
    }

    public class Block
    {
        public Guid AccountId { get; set; }
        public Guid BlockedId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class Clan
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string NameNormalized { get; set; } = "";
        public string Tag { get; set; } = "";
        public string TagNormalized { get; set; } = "";
        public string EmblemId { get; set; } = "emblem_default";
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public int Rating { get; set; } = 1500;
        public int Wins { get; set; }
        public int Losses { get; set; }
    }

    public class ClanMember
    {
        public Guid ClanId { get; set; }
        public Guid AccountId { get; set; }
        public ClanRank Rank { get; set; }
        public DateTime JoinedAt { get; set; }
    }

    public class ClanInvite
    {
        public Guid Id { get; set; }
        public Guid ClanId { get; set; }
        public Guid AccountId { get; set; }
        public Guid InvitedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class MatchRecord
    {
        public string Id { get; set; } = "";
        public string ModeId { get; set; } = "";
        public string MapId { get; set; } = "";
        public string Region { get; set; } = "";
        public bool Ranked { get; set; }
        public string Winner { get; set; } = "";
        public float DurationSeconds { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime EndedAt { get; set; }
        public string ServerId { get; set; } = "";
        public string ContentHash { get; set; } = "";
        public int KillsDawn { get; set; }
        public int KillsDusk { get; set; }
    }

    public class MatchPlayerRecord
    {
        public long Id { get; set; }
        public string MatchId { get; set; } = "";
        public Guid? AccountId { get; set; }
        public string Name { get; set; } = "";
        public string Team { get; set; } = "";
        public int Slot { get; set; }
        public string HeroId { get; set; } = "";
        public bool IsBot { get; set; }
        public bool Won { get; set; }
        public bool Abandoned { get; set; }
        public int Level { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
        public int LastHits { get; set; }
        public int Denies { get; set; }
        public int GoldEarned { get; set; }
        public int NetWorth { get; set; }
        public float Gpm { get; set; }
        public float Xpm { get; set; }
        public float HeroDamage { get; set; }
        public float BuildingDamage { get; set; }
        public float Healing { get; set; }
        public int Wards { get; set; }
        public int Towers { get; set; }
        public string Items { get; set; } = "";
        public string NetWorthTimeline { get; set; } = "";
        public int RatingBefore { get; set; }
        public int RatingChange { get; set; }
        public int AccountXp { get; set; }
    }

    public class PlayerRating
    {
        public Guid AccountId { get; set; }
        public string Queue { get; set; } = "ranked";
        public string Season { get; set; } = "S1";
        public int Rating { get; set; } = 1500;
        public int Peak { get; set; } = 1500;
        public int Games { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
    }

    public class AccountStats
    {
        public Guid AccountId { get; set; }
        public int GamesPlayed { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Abandons { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
        public double SumGpm { get; set; }
        public double SumXpm { get; set; }
        public int WardsPlaced { get; set; }
        public int TowersDestroyed { get; set; }
        public double TotalSeconds { get; set; }
        public float BestGpm { get; set; }
        public float BestXpm { get; set; }
    }

    public class HeroStats
    {
        public Guid AccountId { get; set; }
        public string HeroId { get; set; } = "";
        public int Games { get; set; }
        public int Wins { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
        public double SumGpm { get; set; }
        public double SumXpm { get; set; }
        public DateTime LastPlayed { get; set; }
    }

    public class AccountAchievement
    {
        public Guid AccountId { get; set; }
        public string AchievementId { get; set; } = "";
        public DateTime UnlockedAt { get; set; }
    }

    public class AccountCosmetic
    {
        public Guid AccountId { get; set; }
        public string CosmeticId { get; set; } = "";
        public DateTime AcquiredAt { get; set; }
        public bool Equipped { get; set; }
    }

    public class Ban
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public string Reason { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public Guid? IssuedBy { get; set; }
    }

    public class PlayerReport
    {
        public Guid Id { get; set; }
        public Guid ReporterId { get; set; }
        public Guid TargetId { get; set; }
        public string Reason { get; set; } = "";
        public string Details { get; set; } = "";
        public string MatchId { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public bool Reviewed { get; set; }
    }

    public class Commendation
    {
        public long Id { get; set; }
        public string MatchId { get; set; } = "";
        public Guid FromId { get; set; }
        public Guid ToId { get; set; }
        public string Kind { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class NewsArticle
    {
        public Guid Id { get; set; }
        public string Category { get; set; } = "Updates";
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Body { get; set; } = "";
        public string ImageKey { get; set; } = "";
        public DateTime PublishedAt { get; set; }
        public bool Pinned { get; set; }
        public string Author { get; set; } = "";
        /// <summary>True for development sample content (seeded locally, never in production).</summary>
        public bool DevSample { get; set; }
    }

    public class AuditEntry
    {
        public long Id { get; set; }
        public DateTime At { get; set; }
        public Guid? AccountId { get; set; }
        public string Action { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Ip { get; set; } = "";
    }
}
