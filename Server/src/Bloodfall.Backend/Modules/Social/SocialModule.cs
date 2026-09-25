using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bloodfall.Backend.Infrastructure;
using Bloodfall.Backend.Infrastructure.Data;
using Bloodfall.Backend.Infrastructure.Realtime;
using Bloodfall.Backend.Infrastructure.Security;
using Bloodfall.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bloodfall.Backend.Modules.Social
{
    // ============================================================================ presence

    public sealed class PresenceService
    {
        private sealed class State
        {
            public PresenceStatus Status;
            public string Text = "";
            public string Detail = "";
            public string Name = "";
            public string Region = "eu";
            public string ClanId;
            public HashSet<Guid> Friends = new HashSet<Guid>();
            public HashSet<Guid> Blocked = new HashSet<Guid>();
            public DateTime LastActivity = DateTime.UtcNow;
        }

        private readonly ConcurrentDictionary<Guid, State> _states = new();
        private readonly RealtimeHub _hub;
        private readonly IServiceScopeFactory _scopes;

        public PresenceService(RealtimeHub hub, IServiceScopeFactory scopes) { _hub = hub; _scopes = scopes; }

        public int OnlineCount => _states.Count(kv => kv.Value.Status != PresenceStatus.Offline);

        public async Task OnConnectedAsync(Guid id)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BloodfallDb>();
            var a = await db.Accounts.FindAsync(id);
            if (a == null) return;
            var st = _states.GetOrAdd(id, _ => new State());
            st.Name = a.DisplayName;
            st.Region = a.Region;
            st.ClanId = a.ClanId?.ToString();
            st.Text = a.StatusText;
            st.Friends = (await db.Friendships.Where(f => f.AccountId == id).Select(f => f.FriendId).ToListAsync()).ToHashSet();
            st.Blocked = (await db.Blocks.Where(b => b.AccountId == id).Select(b => b.BlockedId).ToListAsync()).ToHashSet();
            if (st.Status == PresenceStatus.Offline) st.Status = PresenceStatus.Online;
            Broadcast(id);
        }

        public void OnDisconnected(Guid id)
        {
            if (!_states.TryGetValue(id, out var st)) return;
            st.Status = PresenceStatus.Offline;
            st.Detail = "";
            Broadcast(id);
        }

        public void Set(Guid id, PresenceStatus status, string detail = null, string text = null)
        {
            if (!_states.TryGetValue(id, out var st) || !_hub.IsOnline(id)) return;
            // Being in a lobby/queue/game is set by services; clients may only toggle Online/Away and status text.
            st.Status = status;
            if (detail != null) st.Detail = detail;
            if (text != null) st.Text = Validation.CleanText(text, 80);
            st.LastActivity = DateTime.UtcNow;
            Broadcast(id);
        }

        public PresenceStatus Get(Guid id) => _states.TryGetValue(id, out var st) && _hub.IsOnline(id) ? st.Status : PresenceStatus.Offline;
        public string Detail(Guid id) => _states.TryGetValue(id, out var st) ? st.Detail : "";
        public string Text(Guid id) => _states.TryGetValue(id, out var st) ? st.Text : "";
        public string Name(Guid id) => _states.TryGetValue(id, out var st) ? st.Name : "";
        public string Region(Guid id) => _states.TryGetValue(id, out var st) ? st.Region : "eu";
        public string Clan(Guid id) => _states.TryGetValue(id, out var st) ? st.ClanId : null;
        public bool Blocks(Guid who, Guid other) => _states.TryGetValue(who, out var st) && st.Blocked.Contains(other);

        public void FriendsChanged(Guid a, Guid b, bool added)
        {
            if (_states.TryGetValue(a, out var sa)) { if (added) sa.Friends.Add(b); else sa.Friends.Remove(b); }
            if (_states.TryGetValue(b, out var sb)) { if (added) sb.Friends.Add(a); else sb.Friends.Remove(a); }
        }

        public void BlockChanged(Guid who, Guid other, bool blocked)
        {
            if (_states.TryGetValue(who, out var st)) { if (blocked) st.Blocked.Add(other); else st.Blocked.Remove(other); }
        }

        public void ClanChanged(Guid id, string clanId) { if (_states.TryGetValue(id, out var st)) st.ClanId = clanId; }

        private void Broadcast(Guid id)
        {
            if (!_states.TryGetValue(id, out var st)) return;
            var payload = new PresenceUpdatePayload { AccountId = id.ToString(), Status = _hub.IsOnline(id) ? st.Status : PresenceStatus.Offline, StatusText = st.Text, ActivityDetail = st.Detail };
            _hub.Send(st.Friends.Append(id), RealtimeTypes.PresenceUpdate, payload);
        }
    }

    // ============================================================================ friends / blocks

    public sealed class FriendService
    {
        private readonly BloodfallDb _db;
        private readonly PresenceService _presence;
        private readonly RealtimeHub _hub;

        public FriendService(BloodfallDb db, PresenceService presence, RealtimeHub hub) { _db = db; _presence = presence; _hub = hub; }

        public async Task<FriendsResponse> List(Guid me)
        {
            var friendIds = await _db.Friendships.Where(f => f.AccountId == me).Select(f => f.FriendId).ToListAsync();
            var friends = await _db.Accounts.Where(a => friendIds.Contains(a.Id)).ToListAsync();
            var clanTags = await ClanTags(friends.Where(f => f.ClanId != null).Select(f => f.ClanId.Value));
            var resp = new FriendsResponse();
            foreach (var f in friends)
                resp.Friends.Add(ToView(f, clanTags));
            resp.Friends = resp.Friends.OrderByDescending(f => f.Status != PresenceStatus.Offline).ThenBy(f => f.DisplayName).ToList();

            var reqs = await _db.FriendRequests.Where(r => (r.ToId == me || r.FromId == me) && r.Status == FriendRequestStatus.Pending).ToListAsync();
            var ids = reqs.SelectMany(r => new[] { r.FromId, r.ToId }).Distinct().ToList();
            var names = await _db.Accounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.DisplayName);
            resp.Requests = reqs.Select(r => new FriendRequestView
            {
                RequestId = r.Id.ToString(), FromAccountId = r.FromId.ToString(), FromName = names.GetValueOrDefault(r.FromId), ToAccountId = r.ToId.ToString(),
                ToName = names.GetValueOrDefault(r.ToId), CreatedAt = r.CreatedAt, Incoming = r.ToId == me,
            }).ToList();

            // Recent players: humans from my last 5 matches.
            var lastMatches = await _db.MatchPlayers.Where(p => p.AccountId == me).OrderByDescending(p => p.Id).Select(p => p.MatchId).Take(5).ToListAsync();
            var recentIds = await _db.MatchPlayers.Where(p => lastMatches.Contains(p.MatchId) && p.AccountId != null && p.AccountId != me).Select(p => p.AccountId.Value).Distinct().Take(20).ToListAsync();
            var recent = await _db.Accounts.Where(a => recentIds.Contains(a.Id)).ToListAsync();
            resp.RecentPlayers = recent.Select(a => ToView(a, clanTags)).ToList();

            var blockedIds = await _db.Blocks.Where(b => b.AccountId == me).Select(b => b.BlockedId).ToListAsync();
            resp.Blocked = (await _db.Accounts.Where(a => blockedIds.Contains(a.Id)).ToListAsync()).Select(a => new FriendView { AccountId = a.Id.ToString(), DisplayName = a.DisplayName, Username = a.Username }).ToList();
            return resp;
        }

        private FriendView ToView(Account a, Dictionary<Guid, string> clanTags)
        {
            var status = _presence.Get(a.Id);
            return new FriendView
            {
                AccountId = a.Id.ToString(), DisplayName = a.DisplayName, Username = a.Username, Level = a.Level, Status = status,
                StatusText = a.StatusText, ActivityDetail = status == PresenceStatus.Offline ? "" : _presence.Detail(a.Id),
                ClanTag = a.ClanId != null ? clanTags.GetValueOrDefault(a.ClanId.Value) : null, CanSpectate = status == PresenceStatus.InGame,
            };
        }

        private async Task<Dictionary<Guid, string>> ClanTags(IEnumerable<Guid> clanIds)
        {
            var ids = clanIds.Distinct().ToList();
            return await _db.Clans.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Tag);
        }

        public async Task<IResult> Request(Guid me, string username)
        {
            var n = Validation.Normalize(username);
            var target = await _db.Accounts.FirstOrDefaultAsync(a => a.UsernameNormalized == n || a.DisplayName.ToUpper() == n);
            if (target == null) return Api.NotFound("No player with that name exists.");
            if (target.Id == me) return Api.BadRequest("self", "You cannot add yourself.");
            if (await _db.Friendships.AnyAsync(f => f.AccountId == me && f.FriendId == target.Id)) return Api.Conflict("already_friends", "You are already friends.");
            if (await _db.Blocks.AnyAsync(b => b.AccountId == target.Id && b.BlockedId == me)) return Api.Ok(new { sent = true }); // silent for blocked
            if (await _db.Blocks.AnyAsync(b => b.AccountId == me && b.BlockedId == target.Id)) return Api.BadRequest("blocked", "Unblock this player first.");
            var reverse = await _db.FriendRequests.FirstOrDefaultAsync(r => r.FromId == target.Id && r.ToId == me && r.Status == FriendRequestStatus.Pending);
            if (reverse != null) return await Accept(me, reverse.Id);
            if (await _db.FriendRequests.AnyAsync(r => r.FromId == me && r.ToId == target.Id && r.Status == FriendRequestStatus.Pending))
                return Api.Conflict("already_requested", "Friend request already sent.");
            var req = new FriendRequest { Id = Guid.NewGuid(), FromId = me, ToId = target.Id, CreatedAt = DateTime.UtcNow };
            _db.FriendRequests.Add(req);
            await _db.SaveChangesAsync();
            var from = await _db.Accounts.FindAsync(me);
            _hub.Send(target.Id, RealtimeTypes.FriendRequest, new FriendRequestView { RequestId = req.Id.ToString(), FromAccountId = me.ToString(), FromName = from.DisplayName, ToAccountId = target.Id.ToString(), ToName = target.DisplayName, CreatedAt = req.CreatedAt, Incoming = true });
            return Api.Ok(new { sent = true });
        }

        public async Task<IResult> Accept(Guid me, Guid requestId)
        {
            var r = await _db.FriendRequests.FindAsync(requestId);
            if (r == null || r.ToId != me || r.Status != FriendRequestStatus.Pending) return Api.NotFound("Friend request not found.");
            r.Status = FriendRequestStatus.Accepted;
            if (!await _db.Friendships.AnyAsync(f => f.AccountId == r.FromId && f.FriendId == r.ToId))
            {
                _db.Friendships.Add(new Friendship { AccountId = r.FromId, FriendId = r.ToId, Since = DateTime.UtcNow });
                _db.Friendships.Add(new Friendship { AccountId = r.ToId, FriendId = r.FromId, Since = DateTime.UtcNow });
            }
            await _db.SaveChangesAsync();
            _presence.FriendsChanged(r.FromId, r.ToId, true);
            _hub.Send(new[] { r.FromId, r.ToId }, RealtimeTypes.FriendUpdate, new { accountId = me.ToString(), change = "added" });
            return Results.NoContent();
        }

        public async Task<IResult> Decline(Guid me, Guid requestId)
        {
            var r = await _db.FriendRequests.FindAsync(requestId);
            if (r == null || (r.ToId != me && r.FromId != me) || r.Status != FriendRequestStatus.Pending) return Api.NotFound("Friend request not found.");
            r.Status = r.ToId == me ? FriendRequestStatus.Declined : FriendRequestStatus.Cancelled;
            await _db.SaveChangesAsync();
            _hub.Send(new[] { r.FromId, r.ToId }, RealtimeTypes.FriendUpdate, new { change = "request_closed", requestId = r.Id.ToString() });
            return Results.NoContent();
        }

        public async Task<IResult> Remove(Guid me, Guid other)
        {
            var rows = await _db.Friendships.Where(f => (f.AccountId == me && f.FriendId == other) || (f.AccountId == other && f.FriendId == me)).ToListAsync();
            _db.Friendships.RemoveRange(rows);
            await _db.SaveChangesAsync();
            _presence.FriendsChanged(me, other, false);
            _hub.Send(new[] { me, other }, RealtimeTypes.FriendUpdate, new { accountId = me.ToString(), change = "removed" });
            return Results.NoContent();
        }

        public async Task<IResult> Block(Guid me, Guid other)
        {
            if (me == other) return Api.BadRequest("self", "You cannot block yourself.");
            if (!await _db.Accounts.AnyAsync(a => a.Id == other)) return Api.NotFound("Player not found.");
            if (!await _db.Blocks.AnyAsync(b => b.AccountId == me && b.BlockedId == other))
                _db.Blocks.Add(new Block { AccountId = me, BlockedId = other, CreatedAt = DateTime.UtcNow });
            var rows = await _db.Friendships.Where(f => (f.AccountId == me && f.FriendId == other) || (f.AccountId == other && f.FriendId == me)).ToListAsync();
            _db.Friendships.RemoveRange(rows);
            await _db.SaveChangesAsync();
            _presence.BlockChanged(me, other, true);
            _presence.FriendsChanged(me, other, false);
            return Results.NoContent();
        }

        public async Task<IResult> Unblock(Guid me, Guid other)
        {
            var row = await _db.Blocks.FindAsync(me, other);
            if (row != null) { _db.Blocks.Remove(row); await _db.SaveChangesAsync(); }
            _presence.BlockChanged(me, other, false);
            return Results.NoContent();
        }
    }

    // ============================================================================ parties

    public sealed class PartyService
    {
        public sealed class Party
        {
            public string Id = Guid.NewGuid().ToString("N").Substring(0, 12);
            public Guid Leader;
            public readonly List<Guid> Members = new List<Guid>();
            public readonly Dictionary<Guid, DateTime> Invites = new Dictionary<Guid, DateTime>();
            public string QueueStatus = "Idle";
        }

        public const int MaxSize = 5;
        private readonly object _lock = new object();
        private readonly Dictionary<string, Party> _parties = new();
        private readonly Dictionary<Guid, string> _byAccount = new();
        private readonly RealtimeHub _hub;
        private readonly PresenceService _presence;
        private readonly ChatService _chat;

        public PartyService(RealtimeHub hub, PresenceService presence, ChatService chat) { _hub = hub; _presence = presence; _chat = chat; }

        public Party Of(Guid account)
        {
            lock (_lock) return _byAccount.TryGetValue(account, out var id) && _parties.TryGetValue(id, out var p) ? p : null;
        }

        /// <summary>Members to queue together (solo accounts are a party of one).</summary>
        public List<Guid> MembersOf(Guid account)
        {
            lock (_lock)
            {
                var p = Of(account);
                return p == null ? new List<Guid> { account } : new List<Guid>(p.Members);
            }
        }

        public bool IsLeaderOrSolo(Guid account)
        {
            var p = Of(account);
            return p == null || p.Leader == account;
        }

        public PartyView View(Guid account)
        {
            lock (_lock)
            {
                var p = Of(account);
                if (p == null) return null;
                return new PartyView
                {
                    PartyId = p.Id, LeaderId = p.Leader.ToString(), QueueStatus = p.QueueStatus,
                    Members = p.Members.Select(m => new PartyMemberView { AccountId = m.ToString(), DisplayName = _presence.Name(m), Leader = m == p.Leader }).ToList(),
                    PendingInvites = p.Invites.Where(kv => kv.Value > DateTime.UtcNow).Select(kv => kv.Key.ToString()).ToList(),
                };
            }
        }

        public IResult Invite(Guid from, Guid to)
        {
            if (from == to) return Api.BadRequest("self", "You cannot invite yourself.");
            if (!_hub.IsOnline(to)) return Api.BadRequest("offline", "That player is offline.");
            if (_presence.Blocks(to, from)) return Api.Ok(new { sent = true });
            Party p;
            lock (_lock)
            {
                p = Of(from);
                if (p == null)
                {
                    p = new Party { Leader = from };
                    p.Members.Add(from);
                    _parties[p.Id] = p;
                    _byAccount[from] = p.Id;
                    _chat.Join(from, "party:" + p.Id);
                }
                if (p.Leader != from) return Api.Forbidden("Only the party leader can invite.");
                if (p.Members.Count >= MaxSize) return Api.BadRequest("party_full", "The party is full.");
                if (p.Members.Contains(to)) return Api.Conflict("already_member", "That player is already in your party.");
                p.Invites[to] = DateTime.UtcNow.AddMinutes(2);
            }
            _hub.Send(to, RealtimeTypes.PartyInvite, new PartyInviteView { PartyId = p.Id, FromAccountId = from.ToString(), FromName = _presence.Name(from), ExpiresAt = p.Invites[to] });
            Notify(p);
            return Api.Ok(View(from));
        }

        public IResult Accept(Guid me, string partyId)
        {
            Party p;
            lock (_lock)
            {
                if (!_parties.TryGetValue(partyId ?? "", out p) || !p.Invites.TryGetValue(me, out var exp) || exp < DateTime.UtcNow)
                    return Api.NotFound("That invitation has expired.");
                if (p.Members.Count >= MaxSize) return Api.BadRequest("party_full", "The party is full.");
                LeaveInternal(me);
                p.Invites.Remove(me);
                p.Members.Add(me);
                _byAccount[me] = p.Id;
            }
            _chat.Join(me, "party:" + p.Id);
            _chat.System("party:" + p.Id, $"{_presence.Name(me)} joined the party.");
            Notify(p);
            return Api.Ok(View(me));
        }

        public IResult Decline(Guid me, string partyId)
        {
            Party p;
            lock (_lock)
            {
                if (!_parties.TryGetValue(partyId ?? "", out p)) return Results.NoContent();
                p.Invites.Remove(me);
            }
            Notify(p);
            return Results.NoContent();
        }

        public IResult Leave(Guid me)
        {
            Party p;
            lock (_lock) p = LeaveInternal(me);
            if (p != null) Notify(p);
            _hub.Send(me, RealtimeTypes.PartyUpdate, new { partyId = (string)null });
            return Results.NoContent();
        }

        private Party LeaveInternal(Guid me)
        {
            if (!_byAccount.TryGetValue(me, out var id) || !_parties.TryGetValue(id, out var p)) return null;
            p.Members.Remove(me);
            _byAccount.Remove(me);
            _chat.Leave(me, "party:" + p.Id);
            if (p.Members.Count == 0) { _parties.Remove(p.Id); return null; }
            if (p.Leader == me) p.Leader = p.Members[0];
            _chat.System("party:" + p.Id, $"{_presence.Name(me)} left the party.");
            return p;
        }

        public IResult Kick(Guid me, Guid target)
        {
            Party p;
            lock (_lock)
            {
                p = Of(me);
                if (p == null || p.Leader != me) return Api.Forbidden("Only the party leader can kick.");
                if (!p.Members.Contains(target) || target == me) return Api.NotFound("Player is not in your party.");
                LeaveInternal(target);
            }
            _hub.Send(target, RealtimeTypes.PartyUpdate, new { partyId = (string)null, kicked = true });
            Notify(p);
            return Results.NoContent();
        }

        public IResult Promote(Guid me, Guid target)
        {
            Party p;
            lock (_lock)
            {
                p = Of(me);
                if (p == null || p.Leader != me) return Api.Forbidden("Only the party leader can promote.");
                if (!p.Members.Contains(target)) return Api.NotFound("Player is not in your party.");
                p.Leader = target;
            }
            Notify(p);
            return Results.NoContent();
        }

        public void SetQueueStatus(Guid member, string status)
        {
            var p = Of(member);
            if (p == null) return;
            p.QueueStatus = status;
            Notify(p);
        }

        private void Notify(Party p)
        {
            if (p == null) return;
            var view = View(p.Members.FirstOrDefault());
            _hub.Send(p.Members, RealtimeTypes.PartyUpdate, view);
        }
    }

    // ============================================================================ chat

    public sealed class ChatService
    {
        private sealed class Channel
        {
            public string Id;
            public string Name;
            public string Kind;
            public readonly HashSet<Guid> Members = new HashSet<Guid>();
            public readonly LinkedList<ChatMessageView> History = new LinkedList<ChatMessageView>();
        }

        private readonly object _lock = new object();
        private readonly Dictionary<string, Channel> _channels = new();
        private readonly RealtimeHub _hub;
        private readonly PresenceService _presence;

        public ChatService(RealtimeHub hub, PresenceService presence) { _hub = hub; _presence = presence; }

        private Channel Get(string id, bool create = true)
        {
            if (_channels.TryGetValue(id, out var c)) return c;
            if (!create) return null;
            var kind = id.Contains(':') ? id.Substring(0, id.IndexOf(':')) : id;
            c = new Channel { Id = id, Kind = kind, Name = FriendlyName(id) };
            _channels[id] = c;
            return c;
        }

        private static string FriendlyName(string id)
        {
            if (id == "global") return "Global";
            if (id.StartsWith("region:")) return "Region " + id.Substring(7).ToUpperInvariant();
            if (id.StartsWith("clan:")) return "Clan";
            if (id.StartsWith("party:")) return "Party";
            if (id.StartsWith("lobby:")) return "Lobby";
            if (id.StartsWith("channel:")) return "#" + id.Substring(8);
            return id;
        }

        public void Join(Guid who, string channel)
        {
            List<ChatMessageView> history;
            lock (_lock)
            {
                var c = Get(channel);
                c.Members.Add(who);
                history = c.History.ToList();
            }
            _hub.Send(who, RealtimeTypes.ChatHistory, new { channel, name = FriendlyName(channel), messages = history.Where(m => m.FromAccountId == null || !_presence.Blocks(who, Guid.Parse(m.FromAccountId))).ToList() });
        }

        public void Leave(Guid who, string channel)
        {
            lock (_lock) { if (_channels.TryGetValue(channel, out var c)) { c.Members.Remove(who); if (c.Members.Count == 0 && c.Kind != "global" && c.Kind != "region") _channels.Remove(channel); } }
        }

        public void LeaveAll(Guid who)
        {
            lock (_lock) foreach (var c in _channels.Values) c.Members.Remove(who);
        }

        public List<ChatChannelView> Channels(Guid who)
        {
            lock (_lock) return _channels.Values.Where(c => c.Members.Contains(who)).Select(c => new ChatChannelView { Id = c.Id, Name = c.Name, Kind = c.Kind, Members = c.Members.Count }).ToList();
        }

        public void System(string channel, string text)
        {
            var msg = new ChatMessageView { Id = Guid.NewGuid().ToString("N"), Channel = channel, Text = text, SentAt = DateTime.UtcNow, System = true, FromName = "System" };
            Deliver(channel, msg, Guid.Empty);
        }

        /// <summary>Handles a chat message from a connected client, including slash commands.</summary>
        public string Send(RealtimeConnection conn, ChatSendPayload p)
        {
            var text = Validation.CleanText(p?.Text, 300);
            if (string.IsNullOrEmpty(text)) return null;
            // Flood control: 6 messages per 5 seconds.
            var now = DateTime.UtcNow;
            while (conn.RecentChat.Count > 0 && (now - conn.RecentChat.Peek()).TotalSeconds > 5) conn.RecentChat.Dequeue();
            if (conn.RecentChat.Count >= 6) return "You are sending messages too quickly.";
            conn.RecentChat.Enqueue(now);

            if (text.StartsWith("/"))
            {
                var parts = text.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0].ToLowerInvariant())
                {
                    case "/w":
                    case "/whisper":
                    case "/msg":
                        if (parts.Length < 3) return "Usage: /w <name> <message>";
                        return Whisper(conn, parts[1], parts[2]);
                    case "/join":
                        if (parts.Length < 2) return "Usage: /join <channel>";
                        var name = new string(parts[1].Where(char.IsLetterOrDigit).Take(24).ToArray()).ToLowerInvariant();
                        if (name.Length < 2) return "Invalid channel name.";
                        Join(conn.AccountId, "channel:" + name);
                        return null;
                    case "/leave":
                        if (parts.Length < 2) return "Usage: /leave <channel>";
                        Leave(conn.AccountId, "channel:" + parts[1].ToLowerInvariant());
                        return null;
                    case "/away":
                        _presence.Set(conn.AccountId, PresenceStatus.Away);
                        return null;
                    case "/back":
                        _presence.Set(conn.AccountId, PresenceStatus.Online);
                        return null;
                    default:
                        return "Unknown command. Available: /w, /join, /leave, /away, /back";
                }
            }

            if (!string.IsNullOrEmpty(p.ToAccountId)) return Whisper(conn, p.ToAccountId, text, byId: true);
            var channel = p.Channel ?? "global";
            lock (_lock)
            {
                var c = Get(channel, create: false);
                if (c == null || !c.Members.Contains(conn.AccountId)) return "You are not in that channel.";
            }
            var clanId = _presence.Clan(conn.AccountId);
            var msg = new ChatMessageView { Id = Guid.NewGuid().ToString("N"), Channel = channel, FromAccountId = conn.AccountId.ToString(), FromName = conn.DisplayName, Text = text, SentAt = now };
            Deliver(channel, msg, conn.AccountId);
            return null;
        }

        private string Whisper(RealtimeConnection conn, string target, string text, bool byId = false)
        {
            Guid to = Guid.Empty;
            if (byId) Guid.TryParse(target, out to);
            else
            {
                foreach (var id in _hub.OnlineAccounts())
                    if (string.Equals(_presence.Name(id), target, StringComparison.OrdinalIgnoreCase)) { to = id; break; }
            }
            if (to == Guid.Empty || !_hub.IsOnline(to)) return $"{target} is not online.";
            if (_presence.Blocks(to, conn.AccountId)) return null; // silently dropped
            var msg = new ChatMessageView
            {
                Id = Guid.NewGuid().ToString("N"), Channel = "whisper", FromAccountId = conn.AccountId.ToString(), FromName = conn.DisplayName,
                ToAccountId = to.ToString(), Text = text, SentAt = DateTime.UtcNow,
            };
            _hub.Send(new[] { to, conn.AccountId }, RealtimeTypes.ChatMessage, msg);
            return null;
        }

        private void Deliver(string channel, ChatMessageView msg, Guid from)
        {
            List<Guid> recipients;
            lock (_lock)
            {
                var c = Get(channel);
                c.History.AddLast(msg);
                while (c.History.Count > 60) c.History.RemoveFirst();
                recipients = c.Members.ToList();
            }
            if (from != Guid.Empty) recipients = recipients.Where(r => !_presence.Blocks(r, from)).ToList();
            _hub.Send(recipients, RealtimeTypes.ChatMessage, msg);
        }
    }

    /// <summary>Routes realtime messages and connection lifecycle to presence/chat.</summary>
    public sealed class SocialRealtimeHandler : IRealtimeHandler
    {
        private readonly PresenceService _presence;
        private readonly ChatService _chat;
        private readonly RealtimeHub _hub;
        private readonly IServiceScopeFactory _scopes;
        private readonly Func<PartyService> _parties;

        public SocialRealtimeHandler(PresenceService presence, ChatService chat, RealtimeHub hub, IServiceScopeFactory scopes, IServiceProvider sp)
        {
            _presence = presence; _chat = chat; _hub = hub; _scopes = scopes;
            _parties = () => sp.GetRequiredService<PartyService>();
        }

        public void OnConnected(RealtimeConnection conn)
        {
            Task.Run(async () =>
            {
                await _presence.OnConnectedAsync(conn.AccountId);
                _chat.Join(conn.AccountId, "global");
                _chat.Join(conn.AccountId, "region:" + _presence.Region(conn.AccountId));
                var clan = _presence.Clan(conn.AccountId);
                if (clan != null) _chat.Join(conn.AccountId, "clan:" + clan);
                var party = _parties().Of(conn.AccountId);
                if (party != null) _chat.Join(conn.AccountId, "party:" + party.Id);
            });
        }

        public void OnDisconnected(Guid accountId, bool last)
        {
            if (!last) return;
            _presence.OnDisconnected(accountId);
            _chat.LeaveAll(accountId);
        }

        public Task HandleAsync(RealtimeConnection conn, string type, JsonElement payload)
        {
            switch (type)
            {
                case RealtimeTypes.ChatSend:
                {
                    var p = payload.Deserialize<ChatSendPayload>(JsonConfig.Options);
                    var err = _chat.Send(conn, p);
                    if (err != null) _hub.SendRaw(conn, RealtimeTypes.ChatMessage, new ChatMessageView { Channel = p?.Channel ?? "system", Text = err, System = true, FromName = "System", SentAt = DateTime.UtcNow });
                    break;
                }
                case RealtimeTypes.ChatJoin:
                {
                    var p = payload.Deserialize<ChatJoinPayload>(JsonConfig.Options);
                    if (p?.Channel != null && (p.Channel == "global" || p.Channel.StartsWith("channel:") || p.Channel.StartsWith("region:")))
                        _chat.Join(conn.AccountId, p.Channel);
                    break;
                }
                case RealtimeTypes.ChatLeave:
                {
                    var p = payload.Deserialize<ChatJoinPayload>(JsonConfig.Options);
                    if (p?.Channel != null) _chat.Leave(conn.AccountId, p.Channel);
                    break;
                }
                case RealtimeTypes.PresenceSet:
                {
                    var p = payload.Deserialize<PresenceSetPayload>(JsonConfig.Options);
                    if (p == null) break;
                    var current = _presence.Get(conn.AccountId);
                    // Clients may toggle Online/Away; lobby/queue/game states are owned by the services.
                    var status = p.Status == PresenceStatus.Away ? PresenceStatus.Away : (current == PresenceStatus.Away ? PresenceStatus.Online : current);
                    _presence.Set(conn.AccountId, status, null, p.Text);
                    break;
                }
            }
            return Task.CompletedTask;
        }
    }

    public static class SocialEndpoints
    {
        public static void MapSocial(this IEndpointRouteBuilder app)
        {
            var g = app.MapGroup("/api/social").RequireAuthorization();
            g.MapGet("/friends", async (FriendService s, HttpContext c) => Api.Ok(await s.List(c.User.AccountId())));
            g.MapPost("/friends/requests", (AddFriendRequest r, FriendService s, HttpContext c) => s.Request(c.User.AccountId(), r?.Username));
            g.MapPost("/friends/requests/{id:guid}/accept", (Guid id, FriendService s, HttpContext c) => s.Accept(c.User.AccountId(), id));
            g.MapPost("/friends/requests/{id:guid}/decline", (Guid id, FriendService s, HttpContext c) => s.Decline(c.User.AccountId(), id));
            g.MapDelete("/friends/{id:guid}", (Guid id, FriendService s, HttpContext c) => s.Remove(c.User.AccountId(), id));
            g.MapPost("/blocks/{id:guid}", (Guid id, FriendService s, HttpContext c) => s.Block(c.User.AccountId(), id));
            g.MapDelete("/blocks/{id:guid}", (Guid id, FriendService s, HttpContext c) => s.Unblock(c.User.AccountId(), id));

            g.MapGet("/party", (PartyService s, HttpContext c) => Api.Ok(s.View(c.User.AccountId())));
            g.MapPost("/party/invite", (AccountRef r, PartyService s, HttpContext c) => Guid.TryParse(r?.AccountId, out var to) ? s.Invite(c.User.AccountId(), to) : Api.BadRequest("invalid", "Invalid account."));
            g.MapPost("/party/{id}/accept", (string id, PartyService s, HttpContext c) => s.Accept(c.User.AccountId(), id));
            g.MapPost("/party/{id}/decline", (string id, PartyService s, HttpContext c) => s.Decline(c.User.AccountId(), id));
            g.MapPost("/party/leave", (PartyService s, HttpContext c) => s.Leave(c.User.AccountId()));
            g.MapPost("/party/kick", (AccountRef r, PartyService s, HttpContext c) => Guid.TryParse(r?.AccountId, out var t) ? s.Kick(c.User.AccountId(), t) : Api.BadRequest("invalid", "Invalid account."));
            g.MapPost("/party/promote", (AccountRef r, PartyService s, HttpContext c) => Guid.TryParse(r?.AccountId, out var t) ? s.Promote(c.User.AccountId(), t) : Api.BadRequest("invalid", "Invalid account."));

            g.MapGet("/chat/channels", (ChatService s, HttpContext c) => Api.Ok(s.Channels(c.User.AccountId())));

            g.MapPost("/reports", async (ReportRequest r, BloodfallDb db, HttpContext c) =>
            {
                if (!Guid.TryParse(r?.AccountId, out var target)) return Api.BadRequest("invalid", "Invalid account.");
                var me = c.User.AccountId();
                if (target == me) return Api.BadRequest("self", "You cannot report yourself.");
                int recent = await db.Reports.CountAsync(x => x.ReporterId == me && x.CreatedAt > DateTime.UtcNow.AddDays(-1));
                if (recent >= 10) return Api.Error(429, "report_limit", "You have reached the daily report limit.");
                db.Reports.Add(new PlayerReport { Id = Guid.NewGuid(), ReporterId = me, TargetId = target, Reason = Validation.CleanText(r.Reason, 40), Details = Validation.CleanText(r.Details, 500), MatchId = Validation.CleanText(r.MatchId, 64), CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
                return Api.Ok(new { reported = true });
            });
            g.MapPost("/commend", async (CommendRequest r, BloodfallDb db, HttpContext c) =>
            {
                var me = c.User.AccountId();
                if (!Guid.TryParse(r?.AccountId, out var target) || target == me) return Api.BadRequest("invalid", "Invalid player.");
                bool bothPlayed = await db.MatchPlayers.CountAsync(p => p.MatchId == r.MatchId && (p.AccountId == me || p.AccountId == target)) == 2;
                if (!bothPlayed) return Api.BadRequest("not_in_match", "You can only commend players from your own matches.");
                if (await db.Commendations.AnyAsync(x => x.MatchId == r.MatchId && x.FromId == me && x.ToId == target)) return Api.Conflict("already", "Already commended.");
                db.Commendations.Add(new Commendation { MatchId = r.MatchId, FromId = me, ToId = target, Kind = Validation.CleanText(r.Kind, 20), CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
                return Api.Ok(new { commended = true });
            });
        }
    }
}
