using Microsoft.EntityFrameworkCore;

namespace Bloodfall.Backend.Infrastructure.Data
{
    /// <summary>
    /// Single database for the development backend, with tables grouped by owning service (auth_*, social_*,
    /// stats_*...). Each service only touches its own tables so they can be split into separate databases later.
    /// All queries go through EF Core, which always parameterises values.
    /// </summary>
    public class BloodfallDb : DbContext
    {
        public BloodfallDb(DbContextOptions<BloodfallDb> options) : base(options) { }

        public DbSet<Account> Accounts => Set<Account>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<EmailToken> EmailTokens => Set<EmailToken>();
        public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
        public DbSet<Friendship> Friendships => Set<Friendship>();
        public DbSet<Block> Blocks => Set<Block>();
        public DbSet<Clan> Clans => Set<Clan>();
        public DbSet<ClanMember> ClanMembers => Set<ClanMember>();
        public DbSet<ClanInvite> ClanInvites => Set<ClanInvite>();
        public DbSet<MatchRecord> Matches => Set<MatchRecord>();
        public DbSet<MatchPlayerRecord> MatchPlayers => Set<MatchPlayerRecord>();
        public DbSet<PlayerRating> Ratings => Set<PlayerRating>();
        public DbSet<AccountStats> Stats => Set<AccountStats>();
        public DbSet<HeroStats> HeroStats => Set<HeroStats>();
        public DbSet<AccountAchievement> Achievements => Set<AccountAchievement>();
        public DbSet<AccountCosmetic> Cosmetics => Set<AccountCosmetic>();
        public DbSet<Ban> Bans => Set<Ban>();
        public DbSet<PlayerReport> Reports => Set<PlayerReport>();
        public DbSet<Commendation> Commendations => Set<Commendation>();
        public DbSet<NewsArticle> News => Set<NewsArticle>();
        public DbSet<AuditEntry> Audit => Set<AuditEntry>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Account>(e =>
            {
                e.ToTable("auth_accounts");
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.UsernameNormalized).IsUnique();
                e.HasIndex(x => x.EmailNormalized).IsUnique();
                e.Property(x => x.Username).HasMaxLength(20);
                e.Property(x => x.UsernameNormalized).HasMaxLength(20);
                e.Property(x => x.Email).HasMaxLength(254);
                e.Property(x => x.EmailNormalized).HasMaxLength(254);
                e.Property(x => x.DisplayName).HasMaxLength(24);
                e.Property(x => x.PasswordHash).HasMaxLength(256);
                e.Property(x => x.StatusText).HasMaxLength(80);
            });
            b.Entity<RefreshToken>(e =>
            {
                e.ToTable("auth_refresh_tokens");
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.TokenHash).IsUnique();
                e.HasIndex(x => x.AccountId);
                e.HasIndex(x => x.FamilyId);
            });
            b.Entity<EmailToken>(e => { e.ToTable("auth_email_tokens"); e.HasKey(x => x.Id); e.HasIndex(x => x.TokenHash).IsUnique(); });
            b.Entity<FriendRequest>(e => { e.ToTable("social_friend_requests"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.ToId, x.Status }); e.HasIndex(x => new { x.FromId, x.Status }); });
            b.Entity<Friendship>(e => { e.ToTable("social_friendships"); e.HasKey(x => new { x.AccountId, x.FriendId }); });
            b.Entity<Block>(e => { e.ToTable("social_blocks"); e.HasKey(x => new { x.AccountId, x.BlockedId }); });
            b.Entity<Clan>(e =>
            {
                e.ToTable("social_clans");
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.NameNormalized).IsUnique();
                e.HasIndex(x => x.TagNormalized).IsUnique();
            });
            b.Entity<ClanMember>(e => { e.ToTable("social_clan_members"); e.HasKey(x => new { x.ClanId, x.AccountId }); e.HasIndex(x => x.AccountId).IsUnique(); });
            b.Entity<ClanInvite>(e => { e.ToTable("social_clan_invites"); e.HasKey(x => x.Id); e.HasIndex(x => x.AccountId); });
            b.Entity<MatchRecord>(e => { e.ToTable("stats_matches"); e.HasKey(x => x.Id); e.HasIndex(x => x.EndedAt); });
            b.Entity<MatchPlayerRecord>(e =>
            {
                e.ToTable("stats_match_players");
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.MatchId);
                e.HasIndex(x => new { x.AccountId, x.MatchId });
            });
            b.Entity<PlayerRating>(e => { e.ToTable("stats_ratings"); e.HasKey(x => new { x.AccountId, x.Queue, x.Season }); e.HasIndex(x => new { x.Queue, x.Season, x.Rating }); });
            b.Entity<AccountStats>(e => { e.ToTable("stats_accounts"); e.HasKey(x => x.AccountId); });
            b.Entity<HeroStats>(e => { e.ToTable("stats_heroes"); e.HasKey(x => new { x.AccountId, x.HeroId }); });
            b.Entity<AccountAchievement>(e => { e.ToTable("stats_achievements"); e.HasKey(x => new { x.AccountId, x.AchievementId }); });
            b.Entity<AccountCosmetic>(e => { e.ToTable("accounts_cosmetics"); e.HasKey(x => new { x.AccountId, x.CosmeticId }); });
            b.Entity<Ban>(e => { e.ToTable("moderation_bans"); e.HasKey(x => x.Id); e.HasIndex(x => x.AccountId); });
            b.Entity<PlayerReport>(e => { e.ToTable("moderation_reports"); e.HasKey(x => x.Id); e.HasIndex(x => x.TargetId); });
            b.Entity<Commendation>(e => { e.ToTable("stats_commendations"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.MatchId, x.FromId, x.ToId }).IsUnique(); });
            b.Entity<NewsArticle>(e => { e.ToTable("content_news"); e.HasKey(x => x.Id); e.HasIndex(x => x.PublishedAt); });
            b.Entity<AuditEntry>(e => { e.ToTable("audit_log"); e.HasKey(x => x.Id); e.HasIndex(x => x.AccountId); });
        }
    }
}
