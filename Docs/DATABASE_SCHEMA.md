# Database Schema

- EF Core (`Server/src/Bloodfall.Backend/Infrastructure/Data`).
- Providers: **SQLite** in development (`data/bloodfall.db`) and **PostgreSQL** in production.
- The schema is created with `EnsureCreated` at start-up (see §3 for the migration plan).
- Table prefixes group ownership by module. Every query goes through EF Core LINQ, which is parameterised.

## 1. Tables

### auth_

| Table | Key | Columns | Indexes |
|---|---|---|---|
| `auth_accounts` | `Id` (Guid) | Username, UsernameNormalized, Email, EmailNormalized, **PasswordHash (Argon2id)**, DisplayName, CreatedAt, LastLoginAt, EmailVerified, Status (Active/Suspended/Banned), SuspendedUntil, Role (Player/Moderator/Admin), Region, Language, Level, Xp, AvatarId, StatusText, ClanId, FavoriteHeroes, TwoFactorEnabled, FailedLogins, LockoutUntil, SecurityStamp | unique UsernameNormalized, unique EmailNormalized |
| `auth_refresh_tokens` | `Id` | AccountId, **TokenHash (SHA-256)**, FamilyId, CreatedAt, ExpiresAt, RevokedAt, ReplacedById, UserAgent, Ip | unique TokenHash; AccountId; FamilyId |
| `auth_email_tokens` | `Id` | AccountId, Purpose (VerifyEmail/ResetPassword), TokenHash, ExpiresAt, UsedAt | unique TokenHash |

### social_

| Table | Key | Columns | Indexes |
|---|---|---|---|
| `social_friend_requests` | `Id` | FromId, ToId, Status (Pending/Accepted/Declined), CreatedAt | (ToId, Status), (FromId, Status) |
| `social_friendships` | (AccountId, FriendId) | Since (stored in both directions) | PK |
| `social_blocks` | (AccountId, BlockedId) | CreatedAt | PK |
| `social_clans` | `Id` | Name, NameNormalized, Tag, TagNormalized, EmblemId, Description, CreatedAt, Rating, Wins, Losses | unique NameNormalized, unique TagNormalized |
| `social_clan_members` | (ClanId, AccountId) | Rank (Leader/Officer/Member/Recruit), JoinedAt | unique AccountId (one clan per account) |
| `social_clan_invites` | `Id` | ClanId, AccountId, InvitedBy, CreatedAt | AccountId |

### stats_

| Table | Key | Columns | Indexes |
|---|---|---|---|
| `stats_matches` | `Id` (match id) | ModeId, MapId, Region, Ranked, Winner, DurationSeconds, StartedAt, EndedAt, ServerId, ContentHash, KillsDawn, KillsDusk | EndedAt |
| `stats_match_players` | `Id` (long) | MatchId, AccountId (null for bots), Name, Team, Slot, HeroId, IsBot, Won, Abandoned, Level, K/D/A, LastHits, Denies, GoldEarned, NetWorth, Gpm, Xpm, HeroDamage, BuildingDamage, Healing, Wards, Towers, Items (csv), NetWorthTimeline (csv), RtsFaction, ResourcesGathered, UnitsTrained, UnitsKilled, BuildingsRazed (RTS; empty/zero in the MOBA), RatingBefore, RatingChange, AccountXp | MatchId; (AccountId, MatchId) |
| `stats_ratings` | (AccountId, Queue, Season) | Rating (1500 start), Peak, Games, Wins, Losses | (Queue, Season, Rating) |
| `stats_accounts` | `AccountId` | GamesPlayed, Wins, Losses, Abandons, Kills, Deaths, Assists, SumGpm, SumXpm, WardsPlaced, TowersDestroyed, TotalSeconds, BestGpm, BestXpm | PK |
| `stats_heroes` | (AccountId, HeroId) | Games, Wins, Kills, Deaths, Assists, SumGpm, SumXpm, LastPlayed (only matches played with a hero) | PK |
| `stats_achievements` | (AccountId, AchievementId) | UnlockedAt | PK |
| `stats_commendations` | `Id` | MatchId, FromId, ToId, Kind, CreatedAt | unique (MatchId, FromId, ToId) |

### Other tables

| Table | Key | Columns | Indexes |
|---|---|---|---|
| `accounts_cosmetics` | (AccountId, CosmeticId) | AcquiredAt, Equipped (no store exists; reserved) | PK |
| `moderation_bans` | `Id` | AccountId, Reason, CreatedAt, ExpiresAt, IssuedBy | AccountId |
| `moderation_reports` | `Id` | ReporterId, TargetId, Reason, Details, MatchId, CreatedAt, Reviewed | TargetId |
| `content_news` | `Id` | Category, Title, Summary, Body, ImageKey, PublishedAt, Pinned, Author, DevSample | PublishedAt |
| `audit_log` | `Id` (long) | At, AccountId, Action, Detail, Ip | AccountId |

## 2. Data that is *not* in the database

The following live in memory (fast, per-process) and are rebuilt from clients reconnecting:

- presence, parties, chat channels and history
- lobbies, matchmaking queues
- the game-server directory

Scaling past one backend instance moves them to Redis (TODO: SERVER_DEPLOYMENT.md §4).

## 3. Migrations plan

- Development uses `EnsureCreated`: delete `data/bloodfall.db` after schema changes.
- Before any public deployment, switch to EF Core migrations with `dotnet ef migrations add Initial` and apply them
  with `dotnet ef database update` or a migration bundle.
- The table and column names above are the contract.

## 4. Privacy

- Stored personal data: email, username, display name, IP addresses (in refresh tokens and the audit log).
- Account deletion means removing the account row and anonymising `stats_match_players.AccountId`/`Name`. The endpoint
  is not implemented yet (see TODO).
