using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloodfall.Backend.Infrastructure;
using Bloodfall.Backend.Infrastructure.Security;
using Bloodfall.Contracts;
using Bloodfall.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bloodfall.Backend.Modules.Directory
{
    public sealed class GameServerInfo
    {
        public string ServerId;
        public string Region;
        public string Address;
        public int Port;
        public int Capacity;
        public string Version;
        public string ContentHash;
        public string State = "Idle";
        public string MatchId;
        public DateTime LastHeartbeat;
        public DateTime RegisteredAt;
        public int Players;
        public int Spectators;
        public float MatchTime;
        public string Phase;
        public ServerAssignment Pending;
        public DateTime AllocatedAt;
    }

    /// <summary>
    /// Master server / server directory. Dedicated game servers register and heartbeat here; lobbies and the
    /// matchmaker allocate idle servers per region. Live data only: a region with no registered servers is
    /// reported as such (never faked).
    /// </summary>
    public sealed class DirectoryService
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, GameServerInfo> _servers = new();
        private readonly BackendOptions _o;
        private readonly ILogger<DirectoryService> _log;
        public static readonly Dictionary<string, string> RegionNames = new()
        {
            ["eu"] = "Europe", ["na-east"] = "North America East", ["na-west"] = "North America West", ["asia"] = "Asia",
            ["oce"] = "Oceania", ["sa"] = "South America", ["dev-local"] = "Development (Local)",
        };

        public DirectoryService(IOptions<BackendOptions> o, ILogger<DirectoryService> log) { _o = o.Value; _log = log; }

        public event Action<string, string> MatchFinished; // (matchId, serverId)
        public event Action<string> ServerLost;              // matchId of a server that disappeared mid-match

        public void Register(ServerRegistration r)
        {
            lock (_lock)
            {
                _servers.TryGetValue(r.ServerId, out var s);
                s ??= new GameServerInfo { ServerId = r.ServerId, RegisteredAt = DateTime.UtcNow };
                s.Region = RegionNames.ContainsKey(r.Region ?? "") ? r.Region : "dev-local";
                s.Address = r.PublicAddress;
                s.Port = r.Port;
                s.Capacity = r.Capacity;
                s.Version = r.Version;
                s.ContentHash = r.ContentHash;
                s.LastHeartbeat = DateTime.UtcNow;
                s.State = "Idle";
                s.MatchId = null;
                s.Pending = null;
                _servers[r.ServerId] = s;
            }
            _log.LogInformation("Game server {Id} registered in {Region} at {Addr}:{Port}", r.ServerId, r.Region, r.PublicAddress, r.Port);
        }

        public HeartbeatResponse Heartbeat(ServerHeartbeat hb)
        {
            string finishedMatch = null;
            lock (_lock)
            {
                if (!_servers.TryGetValue(hb.ServerId ?? "", out var s)) return new HeartbeatResponse { Ok = false, Message = "unknown server - register first" };
                s.LastHeartbeat = DateTime.UtcNow;
                s.Players = hb.PlayersConnected;
                s.Spectators = hb.Spectators;
                s.MatchTime = hb.MatchTime;
                s.Phase = hb.Phase;
                if (hb.State == "Running" && s.State == "Allocated" && hb.MatchId == s.MatchId) { s.State = "Running"; s.Pending = null; }
                if (hb.State == "Idle" && (s.State == "Running" || (s.State == "Allocated" && s.Pending == null)))
                {
                    finishedMatch = s.MatchId;
                    s.State = "Idle";
                    s.MatchId = null;
                }
                // Hand the assignment to the server (repeated until it confirms Running).
                if (s.State == "Allocated" && s.Pending != null)
                {
                    if ((DateTime.UtcNow - s.AllocatedAt).TotalSeconds > 60) { _log.LogWarning("Server {Id} never started match {Match}", s.ServerId, s.MatchId); s.State = "Idle"; s.Pending = null; finishedMatch = s.MatchId; s.MatchId = null; }
                    else return new HeartbeatResponse { Assignment = s.Pending };
                }
            }
            if (finishedMatch != null) MatchFinished?.Invoke(finishedMatch, hb.ServerId);
            return new HeartbeatResponse();
        }

        /// <summary>Reserves an idle server for a match. Returns null if none is available.</summary>
        public GameServerInfo Allocate(IEnumerable<string> regions, ServerAssignment assignment, string contentHash, out string region)
        {
            region = null;
            Prune();
            lock (_lock)
            {
                var order = regions?.ToList() ?? new List<string>();
                GameServerInfo pick = null;
                foreach (var r in order)
                {
                    pick = _servers.Values.Where(s => s.State == "Idle" && s.Region == r && Fresh(s) && Compatible(s, contentHash)).OrderBy(s => s.RegisteredAt).FirstOrDefault();
                    if (pick != null) break;
                }
                if (pick == null && _o.AllowCrossRegionFallback)
                    pick = _servers.Values.Where(s => s.State == "Idle" && Fresh(s) && Compatible(s, contentHash)).OrderBy(s => s.RegisteredAt).FirstOrDefault();
                if (pick == null) return null;
                pick.State = "Allocated";
                pick.MatchId = assignment.MatchId;
                pick.Pending = assignment;
                pick.AllocatedAt = DateTime.UtcNow;
                region = pick.Region;
                return pick;
            }
        }

        private static bool Compatible(GameServerInfo s, string hash) => string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(s.ContentHash) || s.ContentHash == hash;
        private static bool Fresh(GameServerInfo s) => (DateTime.UtcNow - s.LastHeartbeat).TotalSeconds < 15;

        public void Prune()
        {
            var lost = new List<string>();
            lock (_lock)
            {
                foreach (var s in _servers.Values.Where(s => (DateTime.UtcNow - s.LastHeartbeat).TotalSeconds > 30).ToList())
                {
                    _servers.Remove(s.ServerId);
                    if (s.MatchId != null) lost.Add(s.MatchId);
                    _log.LogWarning("Game server {Id} timed out", s.ServerId);
                }
            }
            foreach (var m in lost) ServerLost?.Invoke(m);
        }

        public GameServerInfo ForMatch(string matchId)
        {
            lock (_lock) return _servers.Values.FirstOrDefault(s => s.MatchId == matchId);
        }

        public List<RegionView> Regions()
        {
            Prune();
            lock (_lock)
            {
                return _o.Regions.Select(r =>
                {
                    var list = _servers.Values.Where(s => s.Region == r).ToList();
                    var fresh = list.Where(Fresh).ToList();
                    var ping = fresh.FirstOrDefault();
                    return new RegionView
                    {
                        Id = r, Name = RegionNames.GetValueOrDefault(r, r), Servers = fresh.Count, IdleServers = fresh.Count(s => s.State == "Idle"),
                        PlayersInGame = fresh.Sum(s => s.Players),
                        Status = fresh.Count == 0 ? "NoServers" : fresh.Count < list.Count ? "Degraded" : "Online",
                        PingHost = ping?.Address, PingPort = ping?.Port ?? 0,
                    };
                }).ToList();
            }
        }

        public List<GameServerInfo> Servers()
        {
            lock (_lock) return _servers.Values.ToList();
        }

        public int MatchesInProgress
        {
            get { lock (_lock) return _servers.Values.Count(s => s.State == "Running"); }
        }
    }

    public sealed class TicketIssuer
    {
        private readonly BackendOptions _o;
        private readonly byte[] _key;
        public TicketIssuer(IOptions<BackendOptions> o) { _o = o.Value; _key = Encoding.UTF8.GetBytes(_o.TicketSigningKey); }

        public string Issue(string matchId, Guid accountId, string displayName, string team, int slot, bool spectator) =>
            MatchTickets.Issue(new TicketPayload
            {
                MatchId = matchId, AccountId = accountId.ToString(), DisplayName = displayName, Team = team, Slot = slot, Spectator = spectator,
                Exp = MatchTickets.UnixNow() + _o.TicketMinutes * 60,
            }, _key);
    }

    public static class DirectoryEndpoints
    {
        public static void MapDirectory(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/directory/regions", (DirectoryService d) => Api.Ok(d.Regions()));
            var servers = app.MapGroup("/api/directory/servers").AddEndpointFilter<GameServerKeyFilter>();
            servers.MapPost("/register", (ServerRegistration r, DirectoryService d) =>
            {
                if (string.IsNullOrWhiteSpace(r?.ServerId) || string.IsNullOrWhiteSpace(r.PublicAddress) || r.Port <= 0) return Api.BadRequest("invalid", "serverId, publicAddress and port are required.");
                d.Register(r);
                return Api.Ok(new { registered = true });
            });
            servers.MapPost("/heartbeat", (ServerHeartbeat hb, DirectoryService d) => Api.Ok(d.Heartbeat(hb)));
            servers.MapGet("/", (DirectoryService d) => Api.Ok(d.Servers().Select(s => new { s.ServerId, s.Region, s.Address, s.Port, s.State, s.MatchId, s.Players, s.Phase }).ToList()));
        }
    }
}
