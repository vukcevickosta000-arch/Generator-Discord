using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bloodfall.Backend.Infrastructure;
using Bloodfall.Backend.Infrastructure.Data;
using Bloodfall.Backend.Infrastructure.Realtime;
using Bloodfall.Backend.Infrastructure.Security;
using Bloodfall.Backend.Modules.Lobbies;
using Bloodfall.Backend.Modules.Social;
using Bloodfall.Contracts;
using Bloodfall.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bloodfall.Backend.Modules.Matchmaking
{
    public sealed class QueueDef
    {
        public string Id;
        public string ModeId;
        public bool Ranked;
        /// <summary>Seconds after which a quick match is completed with bots (0 = never).</summary>
        public float BotFillAfter;
        public bool Available = true;
        public string UnavailableReason;
    }

    /// <summary>
    /// Queue-based matchmaker. Parties queue as one ticket; teams are formed from tickets whose ratings fall inside
    /// a window that widens with wait time. When a match is formed every player must accept within the deadline,
    /// then a dedicated server is allocated and tickets are issued (via the lobby service).
    /// </summary>
    public sealed class MatchmakingService
    {
        private sealed class Ticket
        {
            public string Id = Guid.NewGuid().ToString("N");
            public List<Guid> Members = new List<Guid>();
            public string Queue;
            public List<string> Regions = new List<string>();
            public int Rating;
            public DateTime EnqueuedAt = DateTime.UtcNow;
        }

        private sealed class PendingMatch
        {
            public string Id = Guid.NewGuid().ToString("N");
            public string Queue;
            public List<Ticket> Tickets = new List<Ticket>();
            public HashSet<Guid> Accepted = new HashSet<Guid>();
            public DateTime Deadline;
            public string Region;
            public int BotFill;
            public IEnumerable<Guid> Players => Tickets.SelectMany(t => t.Members);
        }

        public static readonly QueueDef[] Queues =
        {
            new QueueDef { Id = "quick", ModeId = "moba_5v5", BotFillAfter = 25 },
            new QueueDef { Id = "unranked", ModeId = "moba_5v5" },
            new QueueDef { Id = "ranked", ModeId = "moba_5v5_ranked", Ranked = true },
            new QueueDef { Id = "strategy", ModeId = "rts_1v1", Available = false, UnavailableReason = "The Strategy (RTS) queue opens when War of the Ancients mode is released." },
        };

        private readonly object _lock = new object();
        private readonly List<Ticket> _tickets = new();
        private readonly Dictionary<string, PendingMatch> _pending = new();
        private readonly Dictionary<string, List<float>> _recentWaits = new();
        private readonly RealtimeHub _hub;
        private readonly PartyService _parties;
        private readonly PresenceService _presence;
        private readonly LobbyService _lobbies;
        private readonly IServiceScopeFactory _scopes;
        private readonly GameData _data;
        private readonly ILogger<MatchmakingService> _log;

        public MatchmakingService(RealtimeHub hub, PartyService parties, PresenceService presence, LobbyService lobbies, IServiceScopeFactory scopes, GameData data, ILogger<MatchmakingService> log)
        {
            _hub = hub; _parties = parties; _presence = presence; _lobbies = lobbies; _scopes = scopes; _data = data; _log = log;
        }

        public int PlayersSearching { get { lock (_lock) return _tickets.Sum(t => t.Members.Count); } }

        public async Task<IResult> Enqueue(Guid me, QueueRequest r)
        {
            var q = Queues.FirstOrDefault(x => x.Id == r?.Queue);
            if (q == null) return Api.BadRequest("invalid_queue", "Unknown queue.");
            if (!q.Available) return Api.BadRequest("queue_unavailable", q.UnavailableReason);
            if (!_parties.IsLeaderOrSolo(me)) return Api.Forbidden("Only the party leader can start the search.");
            var members = _parties.MembersOf(me);
            if (q.Ranked && members.Count > 1 && members.Count != 5 && members.Count > 2)
                return Api.BadRequest("party_size", "Ranked parties must be solo, duo or a full team of five.");
            foreach (var m in members)
                if (_presence.Get(m) == PresenceStatus.InGame || _presence.Get(m) == PresenceStatus.InLobby)
                    return Api.Conflict("member_busy", $"{_presence.Name(m)} is already in a lobby or game.");
            int rating;
            using (var scope = _scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BloodfallDb>();
                var ratings = await db.Ratings.Where(x => members.Contains(x.AccountId) && x.Queue == "ranked" && x.Season == "S1").Select(x => x.Rating).ToListAsync();
                rating = ratings.Count > 0 ? (int)ratings.Average() : 1500;
            }
            var regions = (r.Regions ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(6).ToList();
            if (regions.Count == 0) regions.Add(_presence.Region(me));
            lock (_lock)
            {
                _tickets.RemoveAll(t => t.Members.Any(members.Contains));
                _tickets.Add(new Ticket { Members = members, Queue = q.Id, Regions = regions, Rating = rating });
            }
            foreach (var m in members) _presence.Set(m, PresenceStatus.FindingMatch, $"Searching: {q.Id}");
            _parties.SetQueueStatus(me, "Searching");
            PushStatus(members);
            return Api.Ok(Status(me));
        }

        public IResult Cancel(Guid me)
        {
            List<Guid> members = null;
            lock (_lock)
            {
                var t = _tickets.FirstOrDefault(x => x.Members.Contains(me));
                if (t != null) { _tickets.Remove(t); members = t.Members; }
            }
            if (members != null)
            {
                foreach (var m in members) { _presence.Set(m, PresenceStatus.Online, ""); _hub.Send(m, RealtimeTypes.MatchCancelled, new { reason = $"{_presence.Name(me)} cancelled the search." }); }
                _parties.SetQueueStatus(me, "Idle");
            }
            return Results.NoContent();
        }

        public IResult Respond(Guid me, AcceptMatchRequest r)
        {
            PendingMatch pm;
            lock (_lock)
            {
                if (!_pending.TryGetValue(r?.MatchId ?? "", out pm) || !pm.Players.Contains(me)) return Api.NotFound("That match is no longer available.");
                if (!r.Accept) { pm.Deadline = DateTime.MinValue; pm.Accepted.Remove(me); DeclineInternal(pm, new[] { me }); return Results.NoContent(); }
                pm.Accepted.Add(me);
            }
            var status = new { matchId = pm.Id, accepted = pm.Accepted.Count, required = pm.Players.Count() };
            _hub.Send(pm.Players, RealtimeTypes.QueueUpdate, AcceptStatus(pm));
            return Api.Ok(status);
        }

        public QueueStatus Status(Guid me)
        {
            lock (_lock)
            {
                var pm = _pending.Values.FirstOrDefault(p => p.Players.Contains(me));
                if (pm != null) return AcceptStatus(pm);
                var t = _tickets.FirstOrDefault(x => x.Members.Contains(me));
                if (t == null) return new QueueStatus { State = "Idle", PlayersInQueue = PlayersInQueueLocked(null) };
                return new QueueStatus
                {
                    InQueue = true, Queue = t.Queue, Regions = t.Regions, ElapsedSeconds = (float)(DateTime.UtcNow - t.EnqueuedAt).TotalSeconds,
                    EstimatedWaitSeconds = Estimate(t.Queue), PlayersInQueue = PlayersInQueueLocked(t.Queue), PartySize = t.Members.Count, State = "Searching",
                };
            }
        }

        private QueueStatus AcceptStatus(PendingMatch pm) => new QueueStatus
        {
            InQueue = true, Queue = pm.Queue, State = "MatchFound", PendingMatchId = pm.Id,
            AcceptDeadlineSeconds = (float)Math.Max(0, (pm.Deadline - DateTime.UtcNow).TotalSeconds), Accepted = pm.Accepted.Count, Required = pm.Players.Count(),
        };

        private int PlayersInQueueLocked(string queue) => _tickets.Where(t => queue == null || t.Queue == queue).Sum(t => t.Members.Count);

        private float Estimate(string queue)
        {
            if (!_recentWaits.TryGetValue(queue, out var list) || list.Count == 0) return -1;
            return list.Average();
        }

        private void PushStatus(IEnumerable<Guid> members)
        {
            foreach (var m in members) _hub.Send(m, RealtimeTypes.QueueUpdate, Status(m));
        }

        /// <summary>One matchmaking pass (called every second by the background service).</summary>
        public void Tick()
        {
            var toNotify = new List<Guid>();
            var formed = new List<PendingMatch>();
            var expired = new List<PendingMatch>();
            var ready = new List<PendingMatch>();
            lock (_lock)
            {
                // Drop tickets whose members went offline.
                _tickets.RemoveAll(t => t.Members.Any(m => !_hub.IsOnline(m)));
                foreach (var q in Queues.Where(q => q.Available))
                {
                    var mode = _data.Modes.TryGetValue(q.ModeId, out var md) ? md : null;
                    if (mode == null) continue;
                    int need = mode.TeamSize * 2;
                    var tickets = _tickets.Where(t => t.Queue == q.Id).OrderBy(t => t.EnqueuedAt).ToList();
                    while (tickets.Count > 0)
                    {
                        var anchor = tickets[0];
                        float waited = (float)(DateTime.UtcNow - anchor.EnqueuedAt).TotalSeconds;
                        int window = 100 + (int)(waited * 15);
                        var region = anchor.Regions.First();
                        var group = new List<Ticket> { anchor };
                        int count = anchor.Members.Count;
                        foreach (var t in tickets.Skip(1))
                        {
                            if (count + t.Members.Count > need) continue;
                            if (!t.Regions.Intersect(anchor.Regions).Any()) continue;
                            if (Math.Abs(t.Rating - anchor.Rating) > window && q.Ranked) continue;
                            group.Add(t);
                            count += t.Members.Count;
                            if (count == need) break;
                        }
                        int botFill = 0;
                        if (count < need)
                        {
                            if (q.BotFillAfter > 0 && waited >= q.BotFillAfter) botFill = need - count;
                            else { tickets.RemoveAt(0); continue; }
                        }
                        foreach (var t in group) { tickets.Remove(t); _tickets.Remove(t); }
                        var common = group.Select(t => (IEnumerable<string>)t.Regions).Aggregate((a, b) => a.Intersect(b)).FirstOrDefault() ?? region;
                        var pm = new PendingMatch { Queue = q.Id, Tickets = group, Deadline = DateTime.UtcNow.AddSeconds(15), Region = common, BotFill = botFill };
                        _pending[pm.Id] = pm;
                        formed.Add(pm);
                        var waits = _recentWaits.TryGetValue(q.Id, out var wl) ? wl : (_recentWaits[q.Id] = new List<float>());
                        waits.Add(waited);
                        if (waits.Count > 30) waits.RemoveAt(0);
                    }
                }
                foreach (var pm in _pending.Values.ToList())
                {
                    if (pm.Accepted.Count == pm.Players.Count()) { _pending.Remove(pm.Id); ready.Add(pm); }
                    else if (DateTime.UtcNow > pm.Deadline) { _pending.Remove(pm.Id); expired.Add(pm); }
                }
                toNotify.AddRange(_tickets.SelectMany(t => t.Members));
            }

            foreach (var pm in formed)
            {
                _log.LogInformation("Match found {Id} in {Queue}: {Players} players (+{Bots} bots)", pm.Id, pm.Queue, pm.Players.Count(), pm.BotFill);
                _hub.Send(pm.Players, RealtimeTypes.MatchFound, new MatchFoundPayload { MatchId = pm.Id, Queue = pm.Queue, AcceptSeconds = 15, Players = pm.Players.Count(), Region = pm.Region });
                _hub.Send(pm.Players, RealtimeTypes.QueueUpdate, AcceptStatus(pm));
            }
            foreach (var pm in expired)
            {
                var decliners = pm.Players.Where(p => !pm.Accepted.Contains(p)).ToList();
                lock (_lock) DeclineInternal(pm, decliners);
            }
            foreach (var pm in ready) Launch(pm);
            foreach (var m in toNotify) _hub.Send(m, RealtimeTypes.QueueUpdate, Status(m));
        }

        /// <summary>Players who did not accept leave the queue; everyone else is put back at the front.</summary>
        private void DeclineInternal(PendingMatch pm, IEnumerable<Guid> decliners)
        {
            _pending.Remove(pm.Id);
            var bad = decliners.ToHashSet();
            foreach (var t in pm.Tickets)
            {
                if (t.Members.Any(bad.Contains))
                {
                    foreach (var m in t.Members) { _presence.Set(m, PresenceStatus.Online, ""); _hub.Send(m, RealtimeTypes.MatchCancelled, new { reason = "A player in your party did not accept. You have been removed from the queue." }); }
                    continue;
                }
                _tickets.Insert(0, t);
                foreach (var m in t.Members) _hub.Send(m, RealtimeTypes.MatchCancelled, new { reason = "Not everyone accepted the match. Returning you to the queue.", requeued = true });
            }
        }

        private void Launch(PendingMatch pm)
        {
            var q = Queues.First(x => x.Id == pm.Queue);
            var mode = _data.Modes[q.ModeId];
            // Team assignment: keep parties together, balance by rating (largest parties first).
            var dawn = new List<Guid>(); var dusk = new List<Guid>();
            int dawnRating = 0, duskRating = 0;
            foreach (var t in pm.Tickets.OrderByDescending(t => t.Members.Count).ThenByDescending(t => t.Rating))
            {
                bool toDawn = dawn.Count + t.Members.Count <= mode.TeamSize && (dusk.Count + t.Members.Count > mode.TeamSize || dawnRating <= duskRating);
                var list = toDawn ? dawn : dusk;
                list.AddRange(t.Members);
                if (toDawn) dawnRating += t.Rating * t.Members.Count; else duskRating += t.Rating * t.Members.Count;
            }
            var players = dawn.Select((id, i) => (id, "Dawn", i)).Concat(dusk.Select((id, i) => (id, "Dusk", i))).ToList();
            var lobby = _lobbies.CreateMatchmade(pm.Queue, mode, players, pm.BotFill, pm.Region);
            var result = _lobbies.BeginMatch(lobby, new[] { pm.Region });
            foreach (var p in pm.Players) _parties.SetQueueStatus(p, "InGame");
            if (lobby.Server == null)
            {
                foreach (var p in pm.Players) { _presence.Set(p, PresenceStatus.Online, ""); _hub.Send(p, RealtimeTypes.MatchCancelled, new { reason = "No game server was available for your region. Please queue again." }); }
                return;
            }
            foreach (var p in pm.Players) _hub.Send(p, RealtimeTypes.MatchReady, _lobbies.Connection(lobby, p, false));
        }
    }

    public sealed class MatchmakingLoop : BackgroundService
    {
        private readonly MatchmakingService _mm;
        private readonly LobbyService _lobbies;
        private readonly ILogger<MatchmakingLoop> _log;
        public MatchmakingLoop(MatchmakingService mm, LobbyService lobbies, ILogger<MatchmakingLoop> log) { _mm = mm; _lobbies = lobbies; _log = log; }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            int n = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _mm.Tick();
                    if (++n % 60 == 0) _lobbies.Cleanup();
                }
                catch (Exception e) { _log.LogError(e, "Matchmaking tick failed"); }
                await Task.Delay(1000, ct).ContinueWith(_ => { });
            }
        }
    }

    public static class MatchmakingEndpoints
    {
        public static void MapMatchmaking(this IEndpointRouteBuilder app)
        {
            var g = app.MapGroup("/api/matchmaking").RequireAuthorization();
            g.MapGet("/queues", () => Api.Ok(MatchmakingService.Queues.Select(q => new { q.Id, q.ModeId, q.Ranked, q.Available, q.UnavailableReason }).ToList()));
            g.MapPost("/queue", (QueueRequest r, MatchmakingService s, HttpContext c) => s.Enqueue(c.User.AccountId(), r));
            g.MapDelete("/queue", (MatchmakingService s, HttpContext c) => s.Cancel(c.User.AccountId()));
            g.MapGet("/status", (MatchmakingService s, HttpContext c) => Api.Ok(s.Status(c.User.AccountId())));
            g.MapPost("/respond", (AcceptMatchRequest r, MatchmakingService s, HttpContext c) => s.Respond(c.User.AccountId(), r));
        }
    }
}
