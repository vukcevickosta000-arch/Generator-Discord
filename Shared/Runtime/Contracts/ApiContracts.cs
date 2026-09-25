using System;
using System.Collections.Generic;

// Data transfer objects shared by the Unity client and the ASP.NET Core backend.
// Public fields + camelCase JSON (backend: System.Text.Json IncludeFields; client: Bloodfall.Core.JsonMapper).
namespace Bloodfall.Contracts
{
    // ================================================================== common

    public sealed class ApiError
    {
        public string Code;
        public string Message;
        public Dictionary<string, string> Fields;
    }

    public sealed class Paged<T>
    {
        public List<T> Items = new List<T>();
        public int Page;
        public int PageSize;
        public int Total;
    }

    // ================================================================== auth

    public sealed class RegisterRequest
    {
        public string Username;
        public string Email;
        public string Password;
        public string DisplayName;
        public string Region;
        public string Language;
    }

    public sealed class LoginRequest
    {
        /// <summary>Username or e-mail.</summary>
        public string Login;
        public string Password;
        public bool RememberMe;
        public string ClientVersion;
    }

    public sealed class RefreshRequest { public string RefreshToken; }
    public sealed class LogoutRequest { public string RefreshToken; }
    public sealed class ForgotPasswordRequest { public string Email; }
    public sealed class ResetPasswordRequest { public string Token; public string NewPassword; }
    public sealed class VerifyEmailRequest { public string Token; }
    public sealed class AvailabilityResponse { public bool Available; public string Reason; }

    public sealed class AuthResponse
    {
        public string AccessToken;
        public string RefreshToken;
        public int ExpiresIn;
        public AccountSummary Account;
    }

    public sealed class SessionInfo
    {
        public string Id;
        public DateTime CreatedAt;
        public DateTime ExpiresAt;
        public string UserAgent;
        public bool Current;
    }

    // ================================================================== accounts / profiles

    public sealed class AccountSummary
    {
        public string AccountId;
        public string Username;
        public string DisplayName;
        public int Level;
        public int Xp;
        public int XpForNextLevel;
        public int Rating;
        public string Rank;
        public int RankDivision;
        public string ClanTag;
        public string AvatarId;
        public string Region;
        public bool EmailVerified;
        public string Role;
    }

    public sealed class RankInfo
    {
        public string Tier;
        public int Division;
        public int Rating;
        public int Peak;
        public int Games;
        public int Wins;
        public int Losses;
        public string Season;
        public bool Provisional;
        public float ProgressToNext;
    }

    public sealed class ProfileStats
    {
        public int GamesPlayed;
        public int Wins;
        public int Losses;
        public int Abandons;
        public float WinRate;
        public int Kills, Deaths, Assists;
        public float AvgKills, AvgDeaths, AvgAssists;
        public float AvgGpm, AvgXpm;
        public int WardsPlaced;
        public int TowersDestroyed;
        public float HoursPlayed;
    }

    public sealed class HeroStatLine
    {
        public string HeroId;
        public int Games;
        public int Wins;
        public float WinRate;
        public float Kda;
        public float AvgGpm;
        public float AvgXpm;
        public DateTime LastPlayed;
    }

    public sealed class MatchSummary
    {
        public string MatchId;
        public DateTime EndedAt;
        public float DurationSeconds;
        public string ModeId;
        public string MapId;
        public string HeroId;
        /// <summary>RTS matches: the faction played (HeroId is empty).</summary>
        public string RtsFaction;
        public bool Won;
        public bool Abandoned;
        public int Kills, Deaths, Assists;
        public int LastHits, Denies;
        public int NetWorth;
        public float Gpm, Xpm;
        public float HeroDamage, BuildingDamage, Healing;
        public int Wards, Towers;
        public string[] Items = new string[0];
        public int RatingChange;
        public bool Ranked;
    }

    public sealed class Profile
    {
        public AccountSummary Account;
        public DateTime RegisteredAt;
        public string StatusText;
        public string ClanName;
        public RankInfo Rank;
        public ProfileStats Stats;
        public List<HeroStatLine> Heroes = new List<HeroStatLine>();
        public List<string> FavoriteHeroes = new List<string>();
        public List<MatchSummary> RecentMatches = new List<MatchSummary>();
        public List<AchievementView> Achievements = new List<AchievementView>();
        public int Commendations;
        public bool IsFriend;
        public bool IsBlocked;
    }

    public sealed class AchievementView
    {
        public string Id;
        public string Name;
        public string Description;
        public DateTime? UnlockedAt;
    }

    public sealed class UpdateProfileRequest
    {
        public string DisplayName;
        public string StatusText;
        public string AvatarId;
        public List<string> FavoriteHeroes;
        public string Region;
    }

    // ================================================================== social

    public enum PresenceStatus { Offline, Online, Away, InLobby, FindingMatch, InGame }

    public sealed class FriendView
    {
        public string AccountId;
        public string DisplayName;
        public string Username;
        public int Level;
        public string Rank;
        public PresenceStatus Status;
        public string StatusText;
        public string ActivityDetail;
        public string ClanTag;
        public bool CanSpectate;
    }

    public sealed class FriendRequestView
    {
        public string RequestId;
        public string FromAccountId;
        public string FromName;
        public string ToAccountId;
        public string ToName;
        public DateTime CreatedAt;
        public bool Incoming;
    }

    public sealed class FriendsResponse
    {
        public List<FriendView> Friends = new List<FriendView>();
        public List<FriendRequestView> Requests = new List<FriendRequestView>();
        public List<FriendView> RecentPlayers = new List<FriendView>();
        public List<FriendView> Blocked = new List<FriendView>();
    }

    public sealed class AddFriendRequest { public string Username; }
    public sealed class AccountRef { public string AccountId; }

    public sealed class PartyMemberView
    {
        public string AccountId;
        public string DisplayName;
        public int Level;
        public string Rank;
        public bool Leader;
        public bool Ready;
    }

    public sealed class PartyView
    {
        public string PartyId;
        public string LeaderId;
        public List<PartyMemberView> Members = new List<PartyMemberView>();
        public List<string> PendingInvites = new List<string>();
        public string QueueStatus;
    }

    public sealed class PartyInviteView
    {
        public string PartyId;
        public string FromAccountId;
        public string FromName;
        public DateTime ExpiresAt;
    }

    public sealed class ChatMessageView
    {
        public string Id;
        public string Channel;
        public string FromAccountId;
        public string FromName;
        public string ToAccountId;
        public string Text;
        public DateTime SentAt;
        public bool System;
        public string ClanTag;
    }

    public sealed class ChatChannelView
    {
        public string Id;
        public string Name;
        public string Kind; // global, regional, clan, party, lobby, whisper, match, system
        public int Members;
    }

    public sealed class ReportRequest
    {
        public string AccountId;
        public string Reason;
        public string Details;
        public string MatchId;
    }

    public sealed class CommendRequest
    {
        public string MatchId;
        public string AccountId;
        public string Kind;
    }

    // ================================================================== clans

    public sealed class ClanView
    {
        public string ClanId;
        public string Name;
        public string Tag;
        public string EmblemId;
        public string Description;
        public DateTime CreatedAt;
        public int Rating;
        public int Wins;
        public int Losses;
        public List<ClanMemberView> Members = new List<ClanMemberView>();
    }

    public sealed class ClanMemberView
    {
        public string AccountId;
        public string DisplayName;
        public string Rank; // Leader, Officer, Member, Recruit
        public DateTime JoinedAt;
        public PresenceStatus Status;
        public int Level;
    }

    public sealed class CreateClanRequest { public string Name; public string Tag; public string Description; public string EmblemId; }
    public sealed class ClanRankRequest { public string AccountId; public string Rank; }
    public sealed class ClanInviteView { public string InviteId; public string ClanId; public string ClanName; public string ClanTag; public string FromName; }

    // ================================================================== lobbies / server browser

    public sealed class CreateLobbyRequest
    {
        public string Name;
        public string Password;
        public string Region;
        /// <summary>null = the mode's own map.</summary>
        public string MapId;
        public string ModeId = "moba_5v5";
        public int TeamSize = 5;
        public int MaxSpectators = 4;
        public bool IsPrivate;
        public bool Ranked;
        public string PickMode = "AllPick";
        public string BotDifficulty = "Normal";
        public bool FillWithBots;
        public float GameSpeed = 1f;
        public List<string> Modifiers = new List<string>();
        public int MinRating;
        public int MaxRating;
    }

    public sealed class LobbySlotView
    {
        public string Team;
        public int Slot;
        public string AccountId;
        public string DisplayName;
        public bool IsBot;
        public string BotDifficulty;
        public bool Ready;
        public bool Host;
        public int Level;
        public string Rank;
        public int PingMs;
        /// <summary>RTS lobbies: the slot's faction id, or null for random.</summary>
        public string RtsFaction;
    }

    public sealed class LobbyView
    {
        public string LobbyId;
        public string Name;
        public string HostAccountId;
        public string HostName;
        public string Region;
        public string MapId;
        public string ModeId;
        public string ModeName;
        public int TeamSize;
        public int Players;
        public int MaxPlayers;
        public int Spectators;
        public int MaxSpectators;
        public bool HasPassword;
        public bool IsPrivate;
        public bool Ranked;
        public string PickMode;
        public string Status; // Waiting, Starting, InProgress, Finished
        public int MinRating;
        public int MaxRating;
        public int AverageRating;
        public float GameSpeed;
        public List<string> Modifiers = new List<string>();
        public List<LobbySlotView> Slots = new List<LobbySlotView>();
        public List<LobbySlotView> SpectatorList = new List<LobbySlotView>();
        public string ServerAddress;
        public int ServerPort;
        public DateTime CreatedAt;
        public string MatchId;
    }

    public sealed class JoinLobbyRequest { public string Password; public bool Spectate; }
    public sealed class LobbySlotRequest { public string Team; public int Slot; }
    public sealed class LobbyReadyRequest { public bool Ready; }
    public sealed class LobbyBotRequest { public string Team; public int Slot; public string Difficulty; public bool Remove; public string Faction; }
    /// <summary>RTS lobbies: pick a faction id, or "random".</summary>
    public sealed class LobbyFactionRequest { public string Faction; }
    public sealed class LobbyKickRequest { public string AccountId; }

    /// <summary>What the client needs to connect to its assigned dedicated game server.</summary>
    public sealed class GameConnectionInfo
    {
        public string MatchId;
        public string Address;
        public int Port;
        public string Ticket;
        public string Region;
        public string ModeId;
        public string MapId;
        public bool Spectator;
    }

    // ================================================================== matchmaking

    public sealed class QueueRequest
    {
        /// <summary>quick, ranked, unranked, strategy</summary>
        public string Queue;
        public List<string> Regions = new List<string>();
        /// <summary>Strategy queue: preferred faction id, or null / "random".</summary>
        public string RtsFaction;
    }

    public sealed class QueueStatus
    {
        public bool InQueue;
        public string Queue;
        public List<string> Regions = new List<string>();
        public float ElapsedSeconds;
        public float EstimatedWaitSeconds = -1;
        public int PlayersInQueue;
        public int PartySize;
        public string State; // Idle, Searching, MatchFound, WaitingForOthers, Starting, Cancelled
        public string PendingMatchId;
        public float AcceptDeadlineSeconds;
        public int Accepted;
        public int Required;
    }

    public sealed class AcceptMatchRequest { public string MatchId; public bool Accept; }

    // ================================================================== directory / regions

    public sealed class RegionView
    {
        public string Id;
        public string Name;
        public string Status; // Online, Degraded, Offline, NoServers
        public int Servers;
        public int IdleServers;
        public int PlayersInGame;
        public string PingHost;
        public int PingPort;
    }

    public sealed class ServerRegistration
    {
        public string ServerId;
        public string Region;
        public string PublicAddress;
        public int Port;
        public int Capacity = 10;
        public string Version;
        public string ContentHash;
    }

    public sealed class ServerHeartbeat
    {
        public string ServerId;
        public string State; // Idle, Allocated, Running, Finished
        public string MatchId;
        public int PlayersConnected;
        public int Spectators;
        public float MatchTime;
        public string Phase;
    }

    public sealed class ServerAssignment
    {
        public string MatchId;
        public string ModeId;
        public string MapId;
        public ulong Seed;
        public bool Ranked;
        public string PickMode;
        public float GameSpeed = 1f;
        public List<AssignedPlayer> Players = new List<AssignedPlayer>();
        public string LobbyId;
    }

    public sealed class AssignedPlayer
    {
        public string AccountId;
        public string DisplayName;
        public string Team;
        public int Slot;
        public bool IsBot;
        public string BotDifficulty;
        public string HeroId;
        /// <summary>RTS: faction id, or null for random.</summary>
        public string RtsFaction;
    }

    public sealed class HeartbeatResponse
    {
        public bool Ok = true;
        public ServerAssignment Assignment;
        public string Message;
    }

    // ================================================================== statistics / leaderboards

    public sealed class MatchReportResponse
    {
        public string MatchId;
        public List<PlayerMatchReward> Rewards = new List<PlayerMatchReward>();
    }

    public sealed class PlayerMatchReward
    {
        public string AccountId;
        public int RatingBefore;
        public int RatingAfter;
        public int AccountXp;
        public int NewLevel;
        public List<string> AchievementsUnlocked = new List<string>();
    }

    public sealed class LeaderboardEntry
    {
        public int Position;
        public string AccountId;
        public string DisplayName;
        public string ClanTag;
        public float Value;
        public string Rank;
        public int Games;
        public float WinRate;
    }

    public sealed class LeaderboardResponse
    {
        public string Category;
        public string Region;
        public string Season;
        public List<LeaderboardEntry> Entries = new List<LeaderboardEntry>();
    }

    public sealed class MatchDetail
    {
        public string MatchId;
        public string ModeId;
        public string MapId;
        public string Region;
        public string Winner;
        public bool Ranked;
        public float DurationSeconds;
        public DateTime EndedAt;
        public int[] TeamKills = new int[2];
        public List<MatchDetailPlayer> Players = new List<MatchDetailPlayer>();
    }

    public sealed class MatchDetailPlayer
    {
        public string AccountId;
        public string Name;
        public string Team;
        public string HeroId;
        public bool IsBot;
        public bool Abandoned;
        public int Level;
        public int Kills, Deaths, Assists, LastHits, Denies;
        public int NetWorth;
        public float Gpm, Xpm, HeroDamage, BuildingDamage, Healing;
        public int Wards, Towers;
        /// <summary>RTS matches: faction and totals.</summary>
        public string RtsFaction;
        public int ResourcesGathered, UnitsTrained, UnitsKilled, BuildingsRazed;
        public string[] Items = new string[0];
        public int RatingChange;
        public List<int> NetWorthTimeline = new List<int>();
    }

    // ================================================================== content / status

    public sealed class NewsArticleView
    {
        public string Id;
        public string Category; // Updates, PatchNotes, HeroSpotlight, Events, ServerNotices, Maintenance
        public string Title;
        public string Summary;
        public string Body;
        public string ImageKey;
        public DateTime PublishedAt;
        public bool Pinned;
        public string Author;
    }

    public sealed class ServiceStatusView
    {
        public string Service;
        public string Status; // Online, Degraded, Offline
        public string Detail;
        public int LatencyMs;
    }

    public sealed class StatusResponse
    {
        public DateTime CheckedAt;
        public string Environment;
        public List<ServiceStatusView> Services = new List<ServiceStatusView>();
        public List<RegionView> Regions = new List<RegionView>();
        public int PlayersOnline;
        public int MatchesInProgress;
    }

    public sealed class ClientVersionResponse
    {
        public string LatestVersion;
        public string MinimumVersion;
        public string ContentHash;
        public string DownloadUrl;
        public bool Maintenance;
        public string MaintenanceMessage;
    }

    // ================================================================== realtime (WebSocket)

    /// <summary>Envelope for every WebSocket message in both directions.</summary>
    public sealed class RealtimeEnvelope
    {
        public string Type;
        public string Id;
        public Bloodfall.Core.JsonNode Payload;
    }

    public static class RealtimeTypes
    {
        // client -> server
        public const string ChatSend = "chat.send";
        public const string ChatJoin = "chat.join";
        public const string ChatLeave = "chat.leave";
        public const string PresenceSet = "presence.set";
        public const string Ping = "ping";
        // server -> client
        public const string ChatMessage = "chat.message";
        public const string ChatHistory = "chat.history";
        public const string PresenceUpdate = "presence.update";
        public const string FriendRequest = "friend.request";
        public const string FriendUpdate = "friend.update";
        public const string PartyInvite = "party.invite";
        public const string PartyUpdate = "party.update";
        public const string LobbyUpdate = "lobby.update";
        public const string LobbyStarted = "lobby.started";
        public const string LobbyClosed = "lobby.closed";
        public const string QueueUpdate = "mm.status";
        public const string MatchFound = "mm.found";
        public const string MatchReady = "mm.ready";
        public const string MatchCancelled = "mm.cancelled";
        public const string ClanUpdate = "clan.update";
        public const string ClanInvite = "clan.invite";
        public const string Notice = "notice";
        public const string SessionRevoked = "session.revoked";
        public const string Pong = "pong";
        public const string Error = "error";
    }

    public sealed class ChatSendPayload { public string Channel; public string Text; public string ToAccountId; }
    public sealed class ChatJoinPayload { public string Channel; }
    public sealed class PresenceSetPayload { public PresenceStatus Status; public string Text; }
    public sealed class PresenceUpdatePayload { public string AccountId; public PresenceStatus Status; public string StatusText; public string ActivityDetail; }
    public sealed class NoticePayload { public string Level; public string Title; public string Message; }
    public sealed class MatchFoundPayload { public string MatchId; public string Queue; public float AcceptSeconds; public int Players; public string Region; }
}
