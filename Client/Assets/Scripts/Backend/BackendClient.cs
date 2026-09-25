using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bloodfall.Client.Core;
using Bloodfall.Client.Networking;
using Bloodfall.Contracts;
using Bloodfall.Core;
using UnityEngine;

namespace Bloodfall.Client.Backend
{
    public sealed class ChatChannelState
    {
        public string Id;
        public string Name;
        public readonly List<ChatMessageView> Messages = new List<ChatMessageView>();
        public int Unread;
    }

    /// <summary>
    /// Client facade over every Bloodfall service. Holds the live client-side state (friends, presence, party, chat,
    /// lobby, queue, news, status, regions) and raises events when the server pushes changes.
    /// No value here is authoritative: everything shown comes from the services.
    /// </summary>
    public sealed class BackendClient : ITickable, IDisposable
    {
        public readonly ApiClient Api;
        public readonly SessionStore Session = new SessionStore();
        public RealtimeClient Realtime { get; private set; }
        private readonly ClientConfig _config;
        private readonly GameApp _app;

        public bool Offline { get; set; }
        public FriendsResponse Friends { get; private set; } = new FriendsResponse();
        public PartyView Party { get; private set; }
        public readonly List<PartyInviteView> PartyInvites = new List<PartyInviteView>();
        public LobbyView Lobby { get; private set; }
        public QueueStatus Queue { get; private set; } = new QueueStatus { State = "Idle" };
        public MatchFoundPayload PendingMatch { get; private set; }
        public List<NewsArticleView> News { get; private set; } = new List<NewsArticleView>();
        public StatusResponse Status { get; private set; }
        public List<RegionView> Regions { get; private set; } = new List<RegionView>();
        public readonly Dictionary<string, int> RegionPings = new Dictionary<string, int>();
        public Profile MyProfile { get; private set; }
        public ClanView Clan { get; private set; }
        public readonly Dictionary<string, ChatChannelState> Channels = new Dictionary<string, ChatChannelState>();
        public readonly List<ChatMessageView> Whispers = new List<ChatMessageView>();
        public ClientVersionResponse VersionInfo { get; private set; }

        public event Action FriendsChanged;
        public event Action PartyChanged;
        public event Action<PartyInviteView> PartyInviteReceived;
        public event Action<FriendRequestView> FriendRequestReceived;
        public event Action<ChatMessageView> ChatReceived;
        public event Action LobbyChanged;
        public event Action<GameConnectionInfo> MatchReady;
        public event Action QueueChanged;
        public event Action<MatchFoundPayload> MatchFound;
        public event Action<string> MatchCancelled;
        public event Action<NoticePayload> Notice;
        public event Action<ClanInviteView> ClanInviteReceived;
        public event Action SessionExpired;
        public event Action<bool> RealtimeStatusChanged;

        public BackendClient(ClientConfig config, GameApp app)
        {
            _config = config;
            _app = app;
            Api = new ApiClient(config.BackendUrl, Session);
            Api.SessionExpired += () => SessionExpired?.Invoke();
        }

        public string MyId => Session.Account?.AccountId;

        public void Tick(float dt) => Realtime?.Pump();

        // ------------------------------------------------------------------ session

        public async Task<ApiResult<AuthResponse>> Login(string login, string password, bool remember)
        {
            var r = await Api.Post<AuthResponse>("api/auth/login", new LoginRequest { Login = login, Password = password, RememberMe = remember, ClientVersion = _config.ClientVersion }, false);
            if (r.Ok) Session.Apply(r.Value, remember);
            return r;
        }

        public async Task<ApiResult<AuthResponse>> Register(RegisterRequest req)
        {
            var r = await Api.Post<AuthResponse>("api/auth/register", req, false);
            if (r.Ok) Session.Apply(r.Value, true);
            return r;
        }

        public Task<ApiResult<AvailabilityResponse>> CheckUsername(string name) => Api.Get<AvailabilityResponse>("api/auth/check-username?name=" + Uri.EscapeDataString(name ?? ""), false);
        public Task<ApiResult<object>> ForgotPassword(string email) => Api.Post<object>("api/auth/forgot-password", new ForgotPasswordRequest { Email = email }, false);

        /// <summary>Restores a remembered session (refresh token rotation).</summary>
        public async Task<bool> TryResumeSession()
        {
            if (!Session.LoadRemembered()) return false;
            if (!await Api.RefreshAsync()) return false;
            var me = await Api.Get<AccountSummary>("api/accounts/me");
            if (!me.Ok) return false;
            Session.Account = me.Value;
            return true;
        }

        public async Task Logout()
        {
            Realtime?.Stop();
            Realtime = null;
            if (!string.IsNullOrEmpty(Session.RefreshToken)) await Api.Post<object>("api/auth/logout", new LogoutRequest { RefreshToken = Session.RefreshToken }, false);
            Session.Clear();
            Friends = new FriendsResponse();
            Party = null;
            Lobby = null;
            Channels.Clear();
            MyProfile = null;
        }

        /// <summary>After login: open the realtime channel and load social state.</summary>
        public async Task EnterClient()
        {
            Realtime?.Stop();
            Realtime = new RealtimeClient(_config.BackendUrl, () => Session.AccessToken);
            Realtime.Message += OnRealtime;
            Realtime.ConnectionChanged += c => RealtimeStatusChanged?.Invoke(c);
            Realtime.Start();
            await Task.WhenAll(RefreshFriends(), RefreshParty(), RefreshProfile(), RefreshClan(), RefreshLobby());
        }

        // ------------------------------------------------------------------ content / status

        public async Task<ApiResult<ClientVersionResponse>> CheckVersion()
        {
            var r = await Api.Get<ClientVersionResponse>("api/content/client-version", false);
            if (r.Ok) VersionInfo = r.Value;
            return r;
        }

        public async Task<ApiResult<StatusResponse>> RefreshStatus()
        {
            var r = await Api.Get<StatusResponse>("api/content/status", false);
            if (r.Ok) { Status = r.Value; Regions = r.Value.Regions ?? Regions; }
            return r;
        }

        public async Task RefreshNews()
        {
            var r = await Api.Get<List<NewsArticleView>>("api/content/news?take=12", false);
            if (r.Ok) News = r.Value;
        }

        public async Task RefreshRegions()
        {
            var r = await Api.Get<List<RegionView>>("api/directory/regions", false);
            if (!r.Ok) return;
            Regions = r.Value;
            // Real latency: unconnected UDP ping to one server per region (off the main thread).
            var targets = Regions.Where(x => !string.IsNullOrEmpty(x.PingHost) && x.PingPort > 0).ToList();
            var results = await Task.Run(() => targets.Select(t => (t.Id, LiteNetClientTransport.PingServer(t.PingHost, t.PingPort, 1200))).ToList());
            foreach (var (id, ms) in results) RegionPings[id] = ms;
        }

        // ------------------------------------------------------------------ profile / social

        public async Task RefreshProfile()
        {
            var r = await Api.Get<Profile>("api/accounts/me/profile");
            if (r.Ok) { MyProfile = r.Value; Session.Account = r.Value.Account; }
        }

        public Task<ApiResult<Profile>> GetProfile(string accountId) => Api.Get<Profile>($"api/accounts/{accountId}/profile");
        public Task<ApiResult<List<MatchSummary>>> GetMatches(string accountId, int page) => Api.Get<List<MatchSummary>>($"api/accounts/{accountId}/matches?page={page}");
        public Task<ApiResult<MatchDetail>> GetMatchDetail(string matchId) => Api.Get<MatchDetail>($"api/stats/matches/{matchId}");
        public Task<ApiResult<LeaderboardResponse>> GetLeaderboard(string category, string region, bool friends) =>
            Api.Get<LeaderboardResponse>($"api/stats/leaderboards?category={category}&region={region}&friends={(friends ? "true" : "false")}");
        public Task<ApiResult<AccountSummary>> UpdateProfile(UpdateProfileRequest r) => Api.Patch<AccountSummary>("api/accounts/me", r);

        public async Task RefreshFriends()
        {
            var r = await Api.Get<FriendsResponse>("api/social/friends");
            if (r.Ok) { Friends = r.Value; FriendsChanged?.Invoke(); }
        }

        public Task<ApiResult<object>> AddFriend(string username) => Api.Post<object>("api/social/friends/requests", new AddFriendRequest { Username = username });
        public Task<ApiResult<object>> AcceptFriend(string requestId) => Api.Post<object>($"api/social/friends/requests/{requestId}/accept", null);
        public Task<ApiResult<object>> DeclineFriend(string requestId) => Api.Post<object>($"api/social/friends/requests/{requestId}/decline", null);
        public Task<ApiResult<object>> RemoveFriend(string accountId) => Api.Delete<object>($"api/social/friends/{accountId}");
        public Task<ApiResult<object>> Block(string accountId) => Api.Post<object>($"api/social/blocks/{accountId}", null);
        public Task<ApiResult<object>> Unblock(string accountId) => Api.Delete<object>($"api/social/blocks/{accountId}");
        public Task<ApiResult<object>> Report(ReportRequest r) => Api.Post<object>("api/social/reports", r);
        public Task<ApiResult<object>> Commend(CommendRequest r) => Api.Post<object>("api/social/commend", r);

        public async Task RefreshParty()
        {
            var r = await Api.Get<PartyView>("api/social/party");
            if (r.Ok) { Party = r.Value; PartyChanged?.Invoke(); }
        }

        public Task<ApiResult<PartyView>> InviteToParty(string accountId) => Api.Post<PartyView>("api/social/party/invite", new AccountRef { AccountId = accountId });
        public async Task<ApiResult<PartyView>> AcceptParty(string partyId)
        {
            var r = await Api.Post<PartyView>($"api/social/party/{partyId}/accept", null);
            PartyInvites.RemoveAll(i => i.PartyId == partyId);
            if (r.Ok) { Party = r.Value; PartyChanged?.Invoke(); }
            return r;
        }
        public Task<ApiResult<object>> DeclineParty(string partyId) { PartyInvites.RemoveAll(i => i.PartyId == partyId); return Api.Post<object>($"api/social/party/{partyId}/decline", null); }
        public async Task LeaveParty() { await Api.Post<object>("api/social/party/leave", null); Party = null; PartyChanged?.Invoke(); }
        public Task<ApiResult<object>> KickFromParty(string accountId) => Api.Post<object>("api/social/party/kick", new AccountRef { AccountId = accountId });
        public Task<ApiResult<object>> PromotePartyLeader(string accountId) => Api.Post<object>("api/social/party/promote", new AccountRef { AccountId = accountId });

        public async Task RefreshClan()
        {
            var r = await Api.Get<ClanView>("api/clans/mine");
            if (r.Ok) Clan = r.Value;
        }

        public Task<ApiResult<ClanView>> CreateClan(CreateClanRequest r) => Api.Post<ClanView>("api/clans", r);
        public Task<ApiResult<object>> LeaveClan() => Api.Post<object>("api/clans/leave", null);
        public Task<ApiResult<object>> InviteToClan(string accountId) => Api.Post<object>("api/clans/invite", new AccountRef { AccountId = accountId });
        public Task<ApiResult<ClanView>> AcceptClanInvite(string inviteId) => Api.Post<ClanView>($"api/clans/invites/{inviteId}/accept", null);
        public Task<ApiResult<List<ClanInviteView>>> ClanInvites() => Api.Get<List<ClanInviteView>>("api/clans/invites");
        public Task<ApiResult<ClanView>> SetClanRank(string accountId, string rank) => Api.Post<ClanView>("api/clans/rank", new ClanRankRequest { AccountId = accountId, Rank = rank });
        public Task<ApiResult<object>> KickFromClan(string accountId) => Api.Post<object>("api/clans/kick", new AccountRef { AccountId = accountId });

        // ------------------------------------------------------------------ chat

        public void SendChat(string channel, string text, string toAccountId = null) =>
            Realtime?.Send(RealtimeTypes.ChatSend, new ChatSendPayload { Channel = channel, Text = text, ToAccountId = toAccountId });

        public void JoinChannel(string channel) => Realtime?.Send(RealtimeTypes.ChatJoin, new ChatJoinPayload { Channel = channel });
        public void SetAway(bool away) => Realtime?.Send(RealtimeTypes.PresenceSet, new PresenceSetPayload { Status = away ? PresenceStatus.Away : PresenceStatus.Online });

        private ChatChannelState Channel(string id, string name = null)
        {
            if (!Channels.TryGetValue(id, out var c)) { c = new ChatChannelState { Id = id, Name = name ?? id }; Channels[id] = c; }
            if (name != null) c.Name = name;
            return c;
        }

        // ------------------------------------------------------------------ lobbies

        public Task<ApiResult<List<LobbyView>>> ListLobbies() => Api.Get<List<LobbyView>>("api/lobbies");
        public async Task<ApiResult<LobbyView>> CreateLobby(CreateLobbyRequest r)
        {
            var res = await Api.Post<LobbyView>("api/lobbies", r);
            if (res.Ok) { Lobby = res.Value; LobbyChanged?.Invoke(); }
            return res;
        }
        public async Task<ApiResult<LobbyView>> JoinLobby(string id, string password, bool spectate)
        {
            var res = await Api.Post<LobbyView>($"api/lobbies/{id}/join", new JoinLobbyRequest { Password = password, Spectate = spectate });
            if (res.Ok) { Lobby = res.Value; LobbyChanged?.Invoke(); }
            return res;
        }
        public async Task LeaveLobby() { await Api.Post<object>("api/lobbies/leave", null); Lobby = null; LobbyChanged?.Invoke(); }
        public Task<ApiResult<object>> LobbySlot(string team, int slot) => Api.Post<object>("api/lobbies/slot", new LobbySlotRequest { Team = team, Slot = slot });
        public Task<ApiResult<object>> LobbyReady(bool ready) => Api.Post<object>("api/lobbies/ready", new LobbyReadyRequest { Ready = ready });
        public Task<ApiResult<object>> LobbyKick(string accountId) => Api.Post<object>("api/lobbies/kick", new LobbyKickRequest { AccountId = accountId });
        public Task<ApiResult<object>> LobbyBot(string team, int slot, string difficulty, bool remove) => Api.Post<object>("api/lobbies/bots", new LobbyBotRequest { Team = team, Slot = slot, Difficulty = difficulty, Remove = remove });
        public Task<ApiResult<LobbyView>> StartLobby() => Api.Post<LobbyView>("api/lobbies/start", null);
        public async Task RefreshLobby()
        {
            var r = await Api.Get<LobbyView>("api/lobbies/mine");
            if (r.Ok) { Lobby = r.Value; LobbyChanged?.Invoke(); }
        }
        public Task<ApiResult<GameConnectionInfo>> CurrentMatchConnection() => Api.Get<GameConnectionInfo>("api/lobbies/mine/connection");

        // ------------------------------------------------------------------ matchmaking

        public async Task<ApiResult<QueueStatus>> EnterQueue(string queue, List<string> regions)
        {
            var r = await Api.Post<QueueStatus>("api/matchmaking/queue", new QueueRequest { Queue = queue, Regions = regions });
            if (r.Ok) { Queue = r.Value; QueueChanged?.Invoke(); }
            return r;
        }

        public async Task LeaveQueue()
        {
            await Api.Delete<object>("api/matchmaking/queue");
            Queue = new QueueStatus { State = "Idle" };
            PendingMatch = null;
            QueueChanged?.Invoke();
        }

        public Task<ApiResult<object>> RespondToMatch(bool accept) =>
            Api.Post<object>("api/matchmaking/respond", new AcceptMatchRequest { MatchId = PendingMatch?.MatchId, Accept = accept });

        // ------------------------------------------------------------------ realtime dispatch

        private void OnRealtime(string type, JsonNode p)
        {
            switch (type)
            {
                case RealtimeTypes.PresenceUpdate:
                {
                    var u = JsonMapper.FromNode<PresenceUpdatePayload>(p);
                    var f = Friends.Friends.FirstOrDefault(x => x.AccountId == u.AccountId);
                    if (f != null) { f.Status = u.Status; f.StatusText = u.StatusText; f.ActivityDetail = u.ActivityDetail; FriendsChanged?.Invoke(); }
                    break;
                }
                case RealtimeTypes.FriendRequest:
                {
                    var r = JsonMapper.FromNode<FriendRequestView>(p);
                    Friends.Requests.Add(r);
                    FriendRequestReceived?.Invoke(r);
                    FriendsChanged?.Invoke();
                    break;
                }
                case RealtimeTypes.FriendUpdate:
                    _ = RefreshFriends();
                    break;
                case RealtimeTypes.PartyInvite:
                {
                    var inv = JsonMapper.FromNode<PartyInviteView>(p);
                    PartyInvites.Add(inv);
                    PartyInviteReceived?.Invoke(inv);
                    break;
                }
                case RealtimeTypes.PartyUpdate:
                    Party = p.IsNull || p["partyId"].IsNull ? null : JsonMapper.FromNode<PartyView>(p);
                    PartyChanged?.Invoke();
                    break;
                case RealtimeTypes.ChatHistory:
                {
                    var ch = Channel(p["channel"].AsString(), p["name"].AsString());
                    ch.Messages.Clear();
                    foreach (var m in p["messages"].ArrayValue ?? new List<JsonNode>()) ch.Messages.Add(JsonMapper.FromNode<ChatMessageView>(m));
                    ChatReceived?.Invoke(null);
                    break;
                }
                case RealtimeTypes.ChatMessage:
                {
                    var m = JsonMapper.FromNode<ChatMessageView>(p);
                    if (m.Channel == "whisper") Whispers.Add(m);
                    else
                    {
                        var ch = Channel(m.Channel ?? "system");
                        ch.Messages.Add(m);
                        if (ch.Messages.Count > 200) ch.Messages.RemoveAt(0);
                        ch.Unread++;
                    }
                    ChatReceived?.Invoke(m);
                    break;
                }
                case RealtimeTypes.LobbyUpdate:
                    Lobby = JsonMapper.FromNode<LobbyView>(p);
                    LobbyChanged?.Invoke();
                    break;
                case RealtimeTypes.LobbyClosed:
                    Lobby = null;
                    LobbyChanged?.Invoke();
                    break;
                case RealtimeTypes.LobbyStarted:
                case RealtimeTypes.MatchReady:
                    PendingMatch = null;
                    Queue = new QueueStatus { State = "Starting" };
                    QueueChanged?.Invoke();
                    MatchReady?.Invoke(JsonMapper.FromNode<GameConnectionInfo>(p));
                    break;
                case RealtimeTypes.QueueUpdate:
                    Queue = JsonMapper.FromNode<QueueStatus>(p);
                    QueueChanged?.Invoke();
                    break;
                case RealtimeTypes.MatchFound:
                    PendingMatch = JsonMapper.FromNode<MatchFoundPayload>(p);
                    MatchFound?.Invoke(PendingMatch);
                    break;
                case RealtimeTypes.MatchCancelled:
                    PendingMatch = null;
                    MatchCancelled?.Invoke(p["reason"].AsString("The match was cancelled."));
                    if (!p["requeued"].AsBool()) { Queue = new QueueStatus { State = "Idle" }; QueueChanged?.Invoke(); }
                    break;
                case RealtimeTypes.ClanInvite:
                    ClanInviteReceived?.Invoke(JsonMapper.FromNode<ClanInviteView>(p));
                    break;
                case RealtimeTypes.Notice:
                    Notice?.Invoke(JsonMapper.FromNode<NoticePayload>(p));
                    break;
            }
        }

        public void Dispose() => Realtime?.Stop();
    }
}
