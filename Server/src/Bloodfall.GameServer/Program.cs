using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Bloodfall.Contracts;
using Bloodfall.Data;
using Bloodfall.Protocol;
using Bloodfall.Simulation;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Bloodfall.GameServer
{
    public sealed class ServerConfig
    {
        public int Port = ProtocolInfo.DefaultGamePort;
        public string Region = "dev-local";
        public string PublicAddress = "127.0.0.1";
        public string BackendUrl = "http://localhost:5080";
        public string ServerKey = Environment.GetEnvironmentVariable("BLOODFALL_SERVER_KEY") ?? "dev-only-game-server-key";
        public string TicketKey = Environment.GetEnvironmentVariable("BLOODFALL_TICKET_KEY") ?? "dev-only-ticket-signing-key-0123456789";
        public string ServerId = Environment.MachineName.ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        public string GameDataPath;
        public string RequiredClientVersion;
        public int MaxPlayers = 10;
        /// <summary>Seconds to wait for the first human to connect after an assignment before giving up.</summary>
        public float JoinTimeout = 120f;
        /// <summary>Development only: allow conceding at any time (integration tests).</summary>
        public bool DevConcedeAnytime;

        public static ServerConfig Parse(string[] args)
        {
            var c = new ServerConfig();
            foreach (var a in args) if (a == "--dev-concede-anytime") c.DevConcedeAnytime = true;
            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "--port": c.Port = int.Parse(args[++i]); break;
                    case "--region": c.Region = args[++i]; break;
                    case "--public-address": c.PublicAddress = args[++i]; break;
                    case "--backend": c.BackendUrl = args[++i].TrimEnd('/'); break;
                    case "--server-key": c.ServerKey = args[++i]; break;
                    case "--ticket-key": c.TicketKey = args[++i]; break;
                    case "--id": c.ServerId = args[++i]; break;
                    case "--gamedata": c.GameDataPath = args[++i]; break;
                    case "--client-version": c.RequiredClientVersion = args[++i]; break;
                }
            }
            return c;
        }
    }

    /// <summary>LiteNetLib peer wrapped for the transport-agnostic MatchHost.</summary>
    internal sealed class LitePeer : IHostPeer
    {
        private readonly NetPeer _peer;
        private readonly NetManager _net;
        private ushort _fragmentId;
        public LitePeer(NetPeer peer, NetManager net) { _peer = peer; _net = net; }
        public int PeerId => _peer.Id;
        public int RttMs => _peer.RoundTripTime;

        public void Send(byte[] data, bool reliable)
        {
            if (_peer.ConnectionState != ConnectionState.Connected) return;
            try
            {
                if (reliable) { _peer.Send(data, 0, DeliveryMethod.ReliableOrdered); return; }
                if (!Fragments.NeedsSplit(data)) { _peer.Send(data, 0, DeliveryMethod.Sequenced); return; }
                foreach (var part in Fragments.Split(data, _fragmentId++)) _peer.Send(part, 0, DeliveryMethod.Unreliable);
            }
            catch (TooBigPacketException e)
            {
                // Never let one oversized packet take the server down; fall back to the reliable channel.
                Console.WriteLine($"Oversized packet ({data.Length} bytes) to peer {PeerId}: {e.Message}");
                _peer.Send(data, 0, DeliveryMethod.ReliableOrdered);
            }
        }

        public void Kick(string reason) => _net.DisconnectPeer(_peer, Encoding.UTF8.GetBytes(reason ?? "Disconnected"));
    }

    public sealed class GameServerApp
    {
        private readonly ServerConfig _cfg;
        private readonly GameData _data;
        private readonly HttpClient _http;
        private readonly EventBasedNetListener _listener = new EventBasedNetListener();
        private readonly NetManager _net;
        private readonly Dictionary<int, LitePeer> _peers = new Dictionary<int, LitePeer>();
        private readonly JsonSerializerOptions _json;
        private readonly byte[] _ticketKey;
        private MatchHost _host;
        private ServerAssignment _assignment;
        private string _state = "Idle";
        private DateTime _assignedAt;
        private DateTime _finishedAt;
        private bool _resultReported;
        private bool _registered;
        private volatile bool _running = true;

        public GameServerApp(ServerConfig cfg, GameData data)
        {
            _cfg = cfg;
            _data = data;
            _ticketKey = Encoding.UTF8.GetBytes(cfg.TicketKey);
            _http = new HttpClient { BaseAddress = new Uri(cfg.BackendUrl + "/"), Timeout = TimeSpan.FromSeconds(5) };
            _http.DefaultRequestHeaders.Add("X-Bloodfall-Server-Key", cfg.ServerKey);
            _json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            _json.Converters.Add(new JsonStringEnumConverter());
            _net = new NetManager(_listener) { AutoRecycle = true, UnconnectedMessagesEnabled = true, UpdateTime = 5, DisconnectTimeout = 10000 };
            _listener.ConnectionRequestEvent += OnConnectionRequest;
            _listener.PeerConnectedEvent += peer =>
            {
                var lp = new LitePeer(peer, _net);
                _peers[peer.Id] = lp;
                _host?.OnPeerConnected(lp);
            };
            _listener.PeerDisconnectedEvent += (peer, info) =>
            {
                if (_peers.TryGetValue(peer.Id, out var lp)) { _peers.Remove(peer.Id); _host?.OnPeerDisconnected(lp, info.Reason.ToString()); }
            };
            _listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                if (!_peers.TryGetValue(peer.Id, out var lp) || _host == null) return;
                var bytes = reader.GetRemainingBytes();
                _host.OnPeerMessage(lp, bytes, 0, bytes.Length);
            };
            // Unconnected ping so clients can measure region latency before joining.
            _listener.NetworkReceiveUnconnectedEvent += (endPoint, reader, type) =>
            {
                var s = reader.GetString(64);
                if (s == "bf-ping") _net.SendUnconnectedMessage(Encoding.UTF8.GetBytes("bf-pong:" + _cfg.Region + ":" + _state), endPoint);
            };
        }

        private void OnConnectionRequest(ConnectionRequest request)
        {
            if (_host == null || _host.Finished) { request.Reject(Encoding.UTF8.GetBytes(_host == null ? "No match is running on this server yet." : "This match has ended.")); return; }
            if (_peers.Count >= _cfg.MaxPlayers + 12) { request.Reject(Encoding.UTF8.GetBytes("Server full.")); return; }
            request.AcceptIfKey(ProtocolInfo.ConnectionKey);
        }

        public async Task RunAsync()
        {
            if (!_net.Start(_cfg.Port)) throw new InvalidOperationException($"Could not bind UDP port {_cfg.Port}");
            Log($"Bloodfall game server {_cfg.ServerId} listening on UDP {_cfg.Port} (region {_cfg.Region}, backend {_cfg.BackendUrl}, content {_data.ContentHash.Substring(0, 12)})");
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; _running = false; };
            _ = Task.Run(HeartbeatLoop);

            var sw = Stopwatch.StartNew();
            double last = sw.Elapsed.TotalSeconds;
            while (_running)
            {
                _net.PollEvents();
                double now = sw.Elapsed.TotalSeconds;
                float dt = (float)(now - last);
                last = now;
                if (_host != null)
                {
                    try
                    {
                        lock (this) _host.Update(dt * (_assignment?.GameSpeed ?? 1f));
                        CheckLifecycle();
                    }
                    catch (Exception e)
                    {
                        // A simulation fault must not silently kill every match on the machine: log and end this match.
                        Log($"FATAL match error: {e}");
                        ResetToIdle("internal error");
                    }
                }
                // ~200 Hz polling keeps latency low; the simulation itself advances at the fixed tick rate.
                Thread.Sleep(_host != null ? 5 : 20);
            }
            _net.Stop();
        }

        private void CheckLifecycle()
        {
            if (_host.Finished && !_resultReported)
            {
                _resultReported = true;
                _finishedAt = DateTime.UtcNow;
                _ = ReportResult(_host.Result);
            }
            if (_host.Finished && (DateTime.UtcNow - _finishedAt).TotalSeconds > 20) ResetToIdle("match complete");
            // Nobody showed up.
            if (!_host.Finished && _host.Match.Phase == MatchPhase.HeroSelect && _host.ConnectedHumans == 0 && (DateTime.UtcNow - _assignedAt).TotalSeconds > _cfg.JoinTimeout)
                ResetToIdle("no players connected");
        }

        private void ResetToIdle(string reason)
        {
            Log($"Returning to idle: {reason}");
            foreach (var p in _peers.Values.ToList()) p.Kick("Server shutting down match.");
            _peers.Clear();
            _host = null;
            _assignment = null;
            _state = "Idle";
        }

        private void StartMatch(ServerAssignment a)
        {
            if (a == null || (_assignment != null && _assignment.MatchId == a.MatchId)) return;
            var cfg = new MatchConfig
            {
                MatchId = a.MatchId, ModeId = a.ModeId, MapId = a.MapId, Seed = a.Seed == 0 ? (ulong)Environment.TickCount64 : a.Seed, Ranked = a.Ranked,
                PickMode = Enum.TryParse<HeroPickMode>(a.PickMode, true, out var pm) ? pm : HeroPickMode.AllPick,
            };
            foreach (var p in a.Players)
            {
                cfg.Players.Add(new PlayerSetup
                {
                    AccountId = p.AccountId, Name = p.IsBot ? $"{(p.Team == "Dawn" ? "Sunward" : "Nightbound")} Bot {p.Slot + 1}" : p.DisplayName ?? "Player",
                    Team = p.Team == "Dusk" ? Team.Dusk : Team.Dawn, Slot = p.Slot, IsBot = p.IsBot, HeroId = p.HeroId, RtsFaction = p.RtsFaction,
                    BotDifficulty = Enum.TryParse<BotDifficulty>(p.BotDifficulty, true, out var bd) ? bd : BotDifficulty.Normal,
                });
            }
            var match = new Match(_data, cfg);
            var host = new MatchHost(match) { Region = _cfg.Region, ServerId = _cfg.ServerId, RequiredClientVersion = _cfg.RequiredClientVersion };
            if (_cfg.DevConcedeAnytime) host.ConcedeMinTime = 0f;
            host.TicketValidator = t =>
            {
                var payload = MatchTickets.Validate(t, _ticketKey, MatchTickets.UnixNow(), out var err);
                return (payload, err);
            };
            host.Log += Log;
            lock (this)
            {
                _assignment = a;
                _host = host;
                _assignedAt = DateTime.UtcNow;
                _resultReported = false;
                _state = "Running";
            }
            Log($"Match {a.MatchId} assigned: {a.ModeId} on {a.MapId}, {a.Players.Count(p => !p.IsBot)} players + {a.Players.Count(p => p.IsBot)} bots");
        }

        private async Task ReportResult(MatchResult r)
        {
            Log($"Match {r.MatchId} finished: {r.Winner} wins after {r.DurationSeconds:0}s");
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var resp = await _http.PostAsJsonAsync("api/stats/matches", r, _json);
                    if (resp.IsSuccessStatusCode) { Log("Result reported to statistics service"); return; }
                    Log($"Result report failed: {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}");
                }
                catch (Exception e) { Log($"Result report error: {e.Message}"); }
                await Task.Delay(2000 * (attempt + 1));
            }
        }

        private async Task HeartbeatLoop()
        {
            while (_running)
            {
                try
                {
                    if (!_registered)
                    {
                        var reg = new ServerRegistration { ServerId = _cfg.ServerId, Region = _cfg.Region, PublicAddress = _cfg.PublicAddress, Port = _cfg.Port, Capacity = _cfg.MaxPlayers, Version = "0.1.0", ContentHash = _data.ContentHash };
                        var resp = await _http.PostAsJsonAsync("api/directory/servers/register", reg, _json);
                        _registered = resp.IsSuccessStatusCode;
                        Log(_registered ? "Registered with server directory" : $"Registration failed: {(int)resp.StatusCode}");
                    }
                    else
                    {
                        ServerHeartbeat hb;
                        lock (this)
                        {
                            hb = new ServerHeartbeat
                            {
                                ServerId = _cfg.ServerId,
                                State = _host == null ? "Idle" : "Running",
                                MatchId = _assignment?.MatchId,
                                PlayersConnected = _host?.ConnectedHumans ?? 0,
                                Spectators = _host?.SpectatorCount ?? 0,
                                MatchTime = _host?.Match.Time ?? 0,
                                Phase = _host?.Match.Phase.ToString(),
                            };
                        }
                        var resp = await _http.PostAsJsonAsync("api/directory/servers/heartbeat", hb, _json);
                        if (resp.IsSuccessStatusCode)
                        {
                            var body = await resp.Content.ReadFromJsonAsync<HeartbeatResponse>(_json);
                            if (body != null && !body.Ok) _registered = false;
                            if (body?.Assignment != null && _host == null) StartMatch(body.Assignment);
                        }
                        else if ((int)resp.StatusCode == 401) Log("Backend rejected the server key.");
                    }
                }
                catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
                {
                    if (_registered) Log($"Backend unreachable: {e.Message}");
                    _registered = false;
                }
                await Task.Delay(_host == null ? 1000 : 2000);
            }
        }

        private static void Log(string s) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {s}");
    }

    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var cfg = ServerConfig.Parse(args);
            var root = cfg.GameDataPath ?? GameDataLoader.FindDefaultRoot(AppContext.BaseDirectory) ?? GameDataLoader.FindDefaultRoot(Environment.CurrentDirectory);
            if (root == null) { Console.Error.WriteLine("GameData not found. Use --gamedata <path>."); return 2; }
            var data = GameDataLoader.FromDirectory(root);
            if (data.Errors.Count > 0) { foreach (var e in data.Errors) Console.Error.WriteLine("DATA ERROR: " + e); return 3; }
            await new GameServerApp(cfg, data).RunAsync();
            return 0;
        }
    }
}
