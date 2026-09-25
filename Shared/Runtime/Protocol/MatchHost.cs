using System;
using System.Collections.Generic;
using System.Linq;
using Bloodfall.Data;
using Bloodfall.Simulation;

namespace Bloodfall.Protocol
{
    /// <summary>A connected peer as seen by the host (LiteNetLib peer on the dedicated server, loopback offline).</summary>
    public interface IHostPeer
    {
        int PeerId { get; }
        void Send(byte[] data, bool reliable);
        void Kick(string reason);
        int RttMs { get; }
    }

    /// <summary>
    /// Authoritative session layer around a <see cref="Match"/>: validates joins (protocol version, content hash,
    /// signed ticket), maps connections to players, applies commands, streams fog-filtered snapshots and events,
    /// handles disconnect grace / reconnect / abandon, and produces the final <see cref="MatchResult"/>.
    /// Runs identically inside the .NET dedicated server and inside the Unity client for offline bot games.
    /// </summary>
    public sealed class MatchHost
    {
        private sealed class Session
        {
            public IHostPeer Peer;
            public int PlayerId = -1;
            public Team Team = Team.None;
            public bool Spectator;
            public bool Welcomed;
            public float ConnectedAt;
            public int CommandsThisSecond;
            public float CommandWindowStart;
            public readonly NetWriter Writer = new NetWriter(8192);
        }

        public readonly Match Match;
        public readonly ContentIndex Index;
        public readonly DateTime StartedUtc = DateTime.UtcNow;
        public string Region = "local";
        public string ServerId = "local";
        public string RequiredClientVersion;
        /// <summary>Validates a ticket string; returns the payload or null + error.</summary>
        public Func<string, (TicketPayload payload, string error)> TicketValidator;
        public event Action<MatchResult> MatchCompleted;
        public event Action<string> Log;
        /// <summary>Hard cap on commands per second per player (anti-spam).</summary>
        public int MaxCommandsPerSecond = 40;
        /// <summary>Earliest match time (seconds) at which a team may concede. Dev servers may lower it.</summary>
        public float ConcedeMinTime = 900f;
        private readonly Dictionary<Team, HashSet<int>> _concedeVotes = new Dictionary<Team, HashSet<int>>();
        private float _concedeStarted = -999f;
        public bool Finished { get; private set; }
        public MatchResult Result { get; private set; }

        private readonly Dictionary<int, Session> _sessions = new Dictionary<int, Session>();
        private float _accumulator;
        private float _realTime;
        private float _stateBroadcastTimer;
        private readonly int _snapshotEvery;
        private readonly List<SimEvent> _eventBuffer = new List<SimEvent>(256);

        public MatchHost(Match match)
        {
            Match = match;
            Index = new ContentIndex(match.Data);
            _snapshotEvery = Math.Max(1, match.Rules.TickRate / Math.Max(1, match.Rules.SnapshotRate));
        }

        public int ConnectedHumans => _sessions.Values.Count(s => s.Welcomed && !s.Spectator);
        public int SpectatorCount => _sessions.Values.Count(s => s.Welcomed && s.Spectator);

        // ------------------------------------------------------------------ connection events

        public void OnPeerConnected(IHostPeer peer)
        {
            _sessions[peer.PeerId] = new Session { Peer = peer, ConnectedAt = _realTime };
        }

        public void OnPeerDisconnected(IHostPeer peer, string reason)
        {
            if (!_sessions.TryGetValue(peer.PeerId, out var s)) return;
            _sessions.Remove(peer.PeerId);
            if (s.PlayerId < 0) return;
            var p = Match.GetPlayer(s.PlayerId);
            if (p == null || p.Connection == PlayerConnection.Abandoned) return;
            // Another session may already have taken over this player (reconnect before timeout).
            if (_sessions.Values.Any(o => o.PlayerId == s.PlayerId)) return;
            p.Connection = PlayerConnection.Disconnected;
            p.DisconnectedAt = Match.Time;
            Match.Announce(AnnouncerKeys.PlayerDisconnected, Team.None, p.Id);
            Match.Emit(new SimEvent { Type = SimEventType.PlayerConnection, OtherId = p.Id, Value = (float)p.Connection, Key = p.Name, PlayerId = -1 });
            Log?.Invoke($"{p.Name} disconnected ({reason})");
        }

        public void OnPeerMessage(IHostPeer peer, byte[] data, int offset, int count)
        {
            if (!_sessions.TryGetValue(peer.PeerId, out var s) || count <= 0) return;
            try
            {
                var r = new NetReader(data, offset, count);
                var type = (MsgType)r.ReadByte();
                if (!s.Welcomed && type != MsgType.Hello && type != MsgType.Ping) return;
                switch (type)
                {
                    case MsgType.Hello: HandleHello(s, Codec.ReadHello(r)); break;
                    case MsgType.Command: HandleCommand(s, Codec.ReadCommand(r, Index)); break;
                    case MsgType.PickHero:
                    {
                        var p = Match.GetPlayer(s.PlayerId);
                        if (p == null) return;
                        var hero = r.ReadString(64);
                        if (!Match.TryPickHero(p, hero, out var err)) SendSystem(s, err);
                        BroadcastMatchState();
                        break;
                    }
                    case MsgType.LoadProgress:
                    {
                        var p = Match.GetPlayer(s.PlayerId);
                        if (p != null) Match.SetPlayerLoaded(p, r.ReadByte() / 100f);
                        break;
                    }
                    case MsgType.Chat: HandleChat(s, r.ReadBool(), r.ReadString(512)); break;
                    case MsgType.Ping:
                    {
                        int t = r.ReadInt();
                        s.Peer.Send(Codec.Pong(t, Match.Tick), false);
                        var p = Match.GetPlayer(s.PlayerId);
                        if (p != null) p.PingMs = s.Peer.RttMs;
                        break;
                    }
                    case MsgType.Leave:
                    {
                        var p = Match.GetPlayer(s.PlayerId);
                        if (p != null && Match.Phase != MatchPhase.PostGame) Abandon(p, "left the game");
                        s.Peer.Kick("Left the game.");
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                // Malformed input never crashes the server; the offending peer is dropped.
                Log?.Invoke($"Bad packet from peer {peer.PeerId}: {e.Message}");
                s.Peer.Kick("Protocol error.");
            }
        }

        private void HandleHello(Session s, HelloInfo h)
        {
            if (s.Welcomed) return;
            if (h.ProtocolVersion != ProtocolInfo.Version) { Reject(s, $"Client protocol {h.ProtocolVersion} does not match server protocol {ProtocolInfo.Version}. Please update Bloodfall."); return; }
            if (h.ContentHash != Match.Data.ContentHash) { Reject(s, "Your game data does not match the server. Please update Bloodfall."); return; }
            if (!string.IsNullOrEmpty(RequiredClientVersion) && h.ClientVersion != RequiredClientVersion) { Reject(s, $"Client version {h.ClientVersion} is not supported (required {RequiredClientVersion})."); return; }
            if (TicketValidator == null) { Reject(s, "Server has no ticket validator configured."); return; }
            var (payload, error) = TicketValidator(h.Ticket);
            if (payload == null) { Reject(s, error ?? "Invalid match ticket."); return; }
            if (!string.IsNullOrEmpty(payload.MatchId) && payload.MatchId != Match.Config.MatchId) { Reject(s, "This ticket is for a different match."); return; }

            if (payload.Spectator || h.Spectator)
            {
                s.Spectator = true;
                s.Team = Team.None;
                s.Welcomed = true;
                s.Peer.Send(Codec.Welcome(new WelcomeInfo { PlayerId = -1, Team = Team.None, Spectator = true, MatchId = Match.Config.MatchId, MapId = Match.Config.MapId, ModeId = Match.Config.ModeId, TickRate = Match.Rules.TickRate, ServerTick = Match.Tick }), true);
                s.Peer.Send(Codec.MatchState(Match, Team.None, Index), true);
                Log?.Invoke($"Spectator {payload.DisplayName} joined");
                return;
            }

            var player = Match.Players.FirstOrDefault(p => !p.IsBot && p.AccountId == payload.AccountId);
            if (player == null) { Reject(s, "You are not a member of this match."); return; }
            if (player.Connection == PlayerConnection.Abandoned) { Reject(s, "You abandoned this match."); return; }

            // Kick an older session for the same player (reconnect from another client).
            foreach (var other in _sessions.Values.Where(o => o != s && o.PlayerId == player.Id).ToList())
            {
                other.PlayerId = -1;
                other.Peer.Kick("Connected from another location.");
            }
            bool reconnect = player.Connection == PlayerConnection.Disconnected;
            if (reconnect)
            {
                player.TotalDisconnectedTime += Math.Max(0, Match.Time - player.DisconnectedAt);
                Match.Announce(AnnouncerKeys.PlayerReconnected, Team.None, player.Id);
            }
            player.Connection = PlayerConnection.Connected;
            if (!string.IsNullOrEmpty(payload.DisplayName)) player.Name = payload.DisplayName;
            s.PlayerId = player.Id;
            s.Team = player.Team;
            s.Welcomed = true;
            s.Peer.Send(Codec.Welcome(new WelcomeInfo
            {
                PlayerId = player.Id, Team = player.Team, MatchId = Match.Config.MatchId, MapId = Match.Config.MapId, ModeId = Match.Config.ModeId,
                TickRate = Match.Rules.TickRate, ServerTick = Match.Tick, Reconnected = reconnect,
            }), true);
            s.Peer.Send(Codec.MatchState(Match, s.Team, Index), true);
            if (Match.Phase == MatchPhase.PreGame || Match.Phase == MatchPhase.Playing)
                s.Peer.Send(Codec.Snapshot(Match, s.Team, player, Index, s.Writer), false);
            Match.Emit(new SimEvent { Type = SimEventType.PlayerConnection, OtherId = player.Id, Value = (float)player.Connection, Key = player.Name, PlayerId = -1 });
            Log?.Invoke($"{player.Name} {(reconnect ? "reconnected" : "joined")} as {player.Team} slot {player.Slot}");
        }

        private void Reject(Session s, string reason)
        {
            Log?.Invoke($"Rejected peer {s.Peer.PeerId}: {reason}");
            s.Peer.Send(Codec.Reject(reason), true);
            s.Peer.Kick(reason);
        }

        private void HandleCommand(Session s, Order o)
        {
            if (s.Spectator || s.PlayerId < 0) return;
            if (_realTime - s.CommandWindowStart >= 1f) { s.CommandWindowStart = _realTime; s.CommandsThisSecond = 0; }
            if (++s.CommandsThisSecond > MaxCommandsPerSecond) return;
            var p = Match.GetPlayer(s.PlayerId);
            if (p == null || p.Connection != PlayerConnection.Connected) return;
            // Ownership and legality are validated inside Match.ApplyOrder / IssueOrder.
            Match.SubmitOrder(p, o);
        }

        private void HandleChat(Session s, bool teamOnly, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            if ((text == "-ff" || text == "/concede" || text == "/ff") && s.PlayerId >= 0) { VoteConcede(s); return; }
            if (text.StartsWith("-") && Match.Config.AllowCheats && s.PlayerId >= 0 && TryCheat(s, text)) return;
            if (text.Length > 200) text = text.Substring(0, 200);
            var p = Match.GetPlayer(s.PlayerId);
            var msg = new ChatMessage { PlayerId = s.PlayerId, Name = p?.Name ?? "Spectator", TeamOnly = teamOnly, Team = s.Team, Text = text };
            var bytes = Codec.ChatBroadcast(msg);
            foreach (var o in _sessions.Values)
            {
                if (!o.Welcomed) continue;
                if (teamOnly && o.Team != s.Team && !o.Spectator) continue;
                if (s.Spectator && !o.Spectator) continue; // spectator chat stays among spectators
                o.Peer.Send(bytes, true);
            }
        }

        private void SendSystem(Session s, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            s.Peer.Send(Codec.ChatBroadcast(new ChatMessage { PlayerId = -1, Name = "System", Text = text, Team = Team.None }), true);
        }

        /// <summary>
        /// Practice cheats. Only honoured when the match was created with AllowCheats (offline practice / dev lobbies);
        /// dedicated online and ranked matches never enable it, so these are no exploit surface.
        /// </summary>
        private bool TryCheat(Session s, string text)
        {
            var p = Match.GetPlayer(s.PlayerId);
            var hero = p?.Hero;
            if (hero == null) return false;
            var parts = text.Split(' ');
            int Arg(int def) => parts.Length > 1 && int.TryParse(parts[1], out var v) ? Math.Max(0, Math.Min(v, 100000)) : def;
            switch (parts[0].ToLowerInvariant())
            {
                case "-gold":
                    Match.GiveGold(p, Arg(5000), hero.Position, false);
                    break;
                case "-lvlup":
                {
                    int levels = Math.Max(1, Math.Min(Arg(1), 25));
                    for (int i = 0; i < levels; i++) Match.AddXp(hero, Math.Max(1, Match.XpForNextLevel(hero) - hero.Xp));
                    break;
                }
                case "-refresh":
                    foreach (var a in hero.Abilities) { a.Cooldown = 0; if (a.Def.MaxCharges > 0) a.Charges = a.Def.MaxCharges; }
                    if (hero.Inventory != null) foreach (var it in hero.Inventory) if (it?.Active != null) it.Active.Cooldown = 0;
                    hero.Hp = hero.Stats.MaxHp;
                    hero.Mana = hero.Stats.MaxMana;
                    break;
                case "-respawn":
                    if (hero.Dead) Match.RespawnHero(hero);
                    break;
                case "-startgame":
                    if (Match.Phase == MatchPhase.PreGame) Match.Time = -0.05f;
                    break;
                default:
                    SendSystem(s, "Practice cheats: -gold [n], -lvlup [n], -refresh, -respawn, -startgame");
                    return true;
            }
            SendSystem(s, "Cheat applied: " + parts[0]);
            return true;
        }

        /// <summary>Concede vote: every connected human on the team must agree within 60 seconds.</summary>
        private void VoteConcede(Session s)
        {
            var p = Match.GetPlayer(s.PlayerId);
            bool devAnytime = ConcedeMinTime <= 0f && Match.Phase == MatchPhase.PreGame;
            if (p == null || (Match.Phase != MatchPhase.Playing && !devAnytime)) return;
            if (ConcedeMinTime > 0f && Match.Time < ConcedeMinTime) { SendSystem(s, $"You can concede after {ConcedeMinTime / 60f:0} minutes."); return; }
            if (!_concedeVotes.TryGetValue(p.Team, out var votes) || Match.Time - _concedeStarted > 60f)
            {
                votes = new HashSet<int>();
                _concedeVotes[p.Team] = votes;
                _concedeStarted = Match.Time;
            }
            votes.Add(p.Id);
            var needed = Match.Players.Where(x => x.Team == p.Team && !x.IsBot && x.Connection == PlayerConnection.Connected).Select(x => x.Id).ToList();
            foreach (var o in _sessions.Values.Where(o => o.Welcomed && o.Team == p.Team))
                SendSystem(o, $"{p.Name} voted to concede ({votes.Count(v => needed.Contains(v))}/{needed.Count}). Type -ff to agree.");
            if (needed.All(votes.Contains))
            {
                Log?.Invoke($"{p.Team} conceded");
                Match.EndMatch(Simulation.Match.OtherTeam(p.Team));
            }
        }

        public void Abandon(Player p, string reason)
        {
            if (p.Connection == PlayerConnection.Abandoned) return;
            p.Connection = PlayerConnection.Abandoned;
            // The hero keeps fighting under bot control so the team is not left a hero down.
            if (p.Hero != null && p.Hero.Brain == null) p.Hero.Brain = new BotBrain(BotDifficulty.Normal, (ulong)(p.Id + 7));
            // RTS: the AI takes over the whole base.
            Match.AssignRtsAi(p);
            Match.Announce(AnnouncerKeys.PlayerAbandoned, Team.None, p.Id);
            Match.Emit(new SimEvent { Type = SimEventType.PlayerConnection, OtherId = p.Id, Value = (float)p.Connection, Key = p.Name, PlayerId = -1 });
            Log?.Invoke($"{p.Name} abandoned: {reason}");
        }

        // ------------------------------------------------------------------ tick

        /// <summary>Advance by real elapsed seconds. Runs as many fixed simulation steps as needed.</summary>
        public void Update(float realDelta)
        {
            _realTime += realDelta;
            _accumulator += Math.Min(realDelta, 0.25f);
            // Drop sessions that never said hello.
            foreach (var s in _sessions.Values.Where(x => !x.Welcomed && _realTime - x.ConnectedAt > 15f).ToList()) s.Peer.Kick("Handshake timed out.");

            while (_accumulator >= Match.Dt)
            {
                _accumulator -= Match.Dt;
                StepOnce();
            }
            _stateBroadcastTimer -= realDelta;
            if (_stateBroadcastTimer <= 0f && (Match.Phase == MatchPhase.HeroSelect || Match.Phase == MatchPhase.Loading || Match.Phase == MatchPhase.WaitingForPlayers))
            {
                _stateBroadcastTimer = 0.5f;
                BroadcastMatchState();
            }
        }

        private void StepOnce()
        {
            var phaseBefore = Match.Phase;
            Match.Step();
            UpdateConnections();

            _eventBuffer.Clear();
            _eventBuffer.AddRange(Match.Events);
            Match.Events.Clear();
            foreach (var s in _sessions.Values)
            {
                if (!s.Welcomed) continue;
                if (_eventBuffer.Count > 0)
                {
                    var ev = Codec.Events(_eventBuffer, Match, s.Team, s.PlayerId);
                    if (ev != null) s.Peer.Send(ev, true);
                }
            }
            if ((Match.Phase == MatchPhase.Playing || Match.Phase == MatchPhase.PreGame) && Match.Tick % _snapshotEvery == 0)
            {
                foreach (var s in _sessions.Values)
                {
                    if (!s.Welcomed) continue;
                    s.Peer.Send(Codec.Snapshot(Match, s.Team, Match.GetPlayer(s.PlayerId), Index, s.Writer), false);
                }
            }
            if (phaseBefore != Match.Phase) BroadcastMatchState();
            if (Match.Phase == MatchPhase.PostGame && !Finished)
            {
                Finished = true;
                Result = MatchResultBuilder.Build(Match, StartedUtc, Region, ServerId);
                var bytes = Codec.MatchEnd(Result);
                foreach (var s in _sessions.Values) if (s.Welcomed) s.Peer.Send(bytes, true);
                MatchCompleted?.Invoke(Result);
            }
        }

        private void UpdateConnections()
        {
            if (Match.Phase != MatchPhase.Playing && Match.Phase != MatchPhase.PreGame) return;
            foreach (var p in Match.Players)
            {
                if (p.Connection == PlayerConnection.Disconnected && Match.Time - p.DisconnectedAt > Match.Rules.ReconnectGraceSeconds)
                    Abandon(p, "did not reconnect in time");
            }
        }

        private void BroadcastMatchState()
        {
            foreach (var s in _sessions.Values)
                if (s.Welcomed) s.Peer.Send(Codec.MatchState(Match, s.Team, Index), true);
        }

        /// <summary>Admin / dev helper: marks every human as loaded (used by tests and offline mode).</summary>
        public void ForceAllLoaded()
        {
            foreach (var p in Match.Players) p.Loaded = true;
        }
    }
}
