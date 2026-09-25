using System;
using System.Collections.Generic;
using Bloodfall.Data;
using Bloodfall.Simulation;

namespace Bloodfall.Protocol
{
    /// <summary>Client-side transport (LiteNetLib UDP online, in-process loopback offline).</summary>
    public interface IClientTransport
    {
        bool IsConnected { get; }
        int RttMs { get; }
        void Connect();
        void Send(byte[] data, bool reliable);
        void Poll();
        void Disconnect();
        event Action Connected;
        event Action<byte[], int, int> DataReceived;
        event Action<string> Disconnected;
    }

    public enum ClientConnectionState { Idle, Connecting, Handshaking, Connected, Rejected, Disconnected, Ended }

    /// <summary>
    /// Engine-independent game client: speaks the Bloodfall game protocol and keeps the latest authoritative
    /// state for the presentation layer (Unity) to render. Contains no rendering and no simulation authority.
    /// </summary>
    public sealed class GameClient
    {
        public readonly GameData Data;
        public readonly ContentIndex Index;
        private readonly IClientTransport _transport;
        private readonly HelloInfo _hello;

        public ClientConnectionState State { get; private set; } = ClientConnectionState.Idle;
        public WelcomeInfo Welcome { get; private set; }
        public MatchStateInfo MatchState { get; private set; }
        public MatchResult Result { get; private set; }
        public string RejectReason { get; private set; }
        public string DisconnectReason { get; private set; }
        /// <summary>Most recent snapshots, oldest first (interpolation buffer).</summary>
        public readonly List<SnapshotFrame> Frames = new List<SnapshotFrame>(8);
        public SnapshotFrame Latest => Frames.Count > 0 ? Frames[Frames.Count - 1] : null;
        public readonly Queue<NetEvent> PendingEvents = new Queue<NetEvent>();
        public readonly List<ChatMessage> ChatLog = new List<ChatMessage>();
        public int RttMs { get; private set; }
        public int LocalPlayerId => Welcome?.PlayerId ?? -1;
        public Team LocalTeam => Welcome?.Team ?? Team.None;
        public bool IsSpectator => Welcome?.Spectator ?? false;
        /// <summary>Local clock (seconds) when the latest snapshot arrived; used for interpolation.</summary>
        public double LatestFrameReceivedAt { get; private set; }
        public double ClockSeconds { get; private set; }

        public event Action<WelcomeInfo> OnWelcome;
        public event Action<MatchStateInfo> OnMatchState;
        public event Action<SnapshotFrame> OnSnapshot;
        public event Action<ChatMessage> OnChat;
        public event Action<MatchResult> OnMatchEnd;
        public event Action<string> OnRejected;
        public event Action<string> OnDisconnected;

        private float _pingTimer;

        public GameClient(GameData data, IClientTransport transport, HelloInfo hello)
        {
            Data = data;
            Index = new ContentIndex(data);
            _transport = transport;
            _hello = hello;
            _hello.ContentHash = data.ContentHash;
            _transport.Connected += HandleConnected;
            _transport.DataReceived += HandleData;
            _transport.Disconnected += HandleDisconnected;
        }

        public void Connect()
        {
            State = ClientConnectionState.Connecting;
            _transport.Connect();
        }

        public void Update(float dt)
        {
            ClockSeconds += dt;
            _transport.Poll();
            if (State == ClientConnectionState.Connected)
            {
                _pingTimer -= dt;
                if (_pingTimer <= 0f)
                {
                    _pingTimer = 1f;
                    _transport.Send(Codec.Ping((int)(ClockSeconds * 1000)), false);
                }
            }
        }

        public void SendOrder(Order o)
        {
            if (State != ClientConnectionState.Connected || IsSpectator) return;
            _transport.Send(Codec.Command(o, Index), true);
        }

        public void PickHero(string heroId) { if (State == ClientConnectionState.Connected) _transport.Send(Codec.PickHero(heroId), true); }
        public void SendChat(string text, bool teamOnly) { if (State == ClientConnectionState.Connected) _transport.Send(Codec.Chat(text, teamOnly), true); }
        public void SendLoadProgress(float p) { if (State == ClientConnectionState.Connected) _transport.Send(Codec.LoadProgress(p), true); }

        public void Leave()
        {
            if (State == ClientConnectionState.Connected) _transport.Send(Codec.Leave(), true);
            _transport.Disconnect();
            State = ClientConnectionState.Disconnected;
        }

        public void Disconnect() { _transport.Disconnect(); }

        private void HandleConnected()
        {
            State = ClientConnectionState.Handshaking;
            _transport.Send(Codec.Hello(_hello), true);
        }

        private void HandleDisconnected(string reason)
        {
            if (State == ClientConnectionState.Rejected || State == ClientConnectionState.Ended) return;
            DisconnectReason = reason;
            State = ClientConnectionState.Disconnected;
            OnDisconnected?.Invoke(reason);
        }

        private void HandleData(byte[] data, int offset, int count)
        {
            var r = new NetReader(data, offset, count);
            var type = (MsgType)r.ReadByte();
            switch (type)
            {
                case MsgType.Welcome:
                    Welcome = Codec.ReadWelcome(r);
                    State = ClientConnectionState.Connected;
                    OnWelcome?.Invoke(Welcome);
                    break;
                case MsgType.Reject:
                    RejectReason = r.ReadString();
                    State = ClientConnectionState.Rejected;
                    OnRejected?.Invoke(RejectReason);
                    break;
                case MsgType.MatchState:
                    MatchState = Codec.ReadMatchState(r, Index);
                    OnMatchState?.Invoke(MatchState);
                    break;
                case MsgType.Snapshot:
                {
                    var f = Codec.ReadSnapshot(r, Index);
                    if (Latest != null && f.Tick <= Latest.Tick) return; // out of order (unreliable channel)
                    Frames.Add(f);
                    if (Frames.Count > 6) Frames.RemoveAt(0);
                    LatestFrameReceivedAt = ClockSeconds;
                    OnSnapshot?.Invoke(f);
                    break;
                }
                case MsgType.Events:
                    foreach (var e in Codec.ReadEvents(r)) PendingEvents.Enqueue(e);
                    break;
                case MsgType.ChatBroadcast:
                {
                    var c = Codec.ReadChatBroadcast(r);
                    ChatLog.Add(c);
                    if (ChatLog.Count > 200) ChatLog.RemoveAt(0);
                    OnChat?.Invoke(c);
                    break;
                }
                case MsgType.Pong:
                    RttMs = (int)(ClockSeconds * 1000) - r.ReadInt();
                    break;
                case MsgType.MatchEnd:
                    Result = Codec.ReadMatchEnd(r);
                    State = ClientConnectionState.Ended;
                    OnMatchEnd?.Invoke(Result);
                    break;
            }
        }
    }

    // ================================================================== loopback (offline / tests)

    /// <summary>In-process connection between a <see cref="GameClient"/> and a <see cref="MatchHost"/>.</summary>
    public sealed class LoopbackConnection : IClientTransport, IHostPeer
    {
        private static int _nextId = 1000;
        private readonly MatchHost _host;
        private readonly Queue<byte[]> _toClient = new Queue<byte[]>();
        private bool _connected;
        private string _kickReason;

        public LoopbackConnection(MatchHost host) { _host = host; PeerId = System.Threading.Interlocked.Increment(ref _nextId); }

        public int PeerId { get; }
        public int RttMs => 0;
        public bool IsConnected => _connected;
        public event Action Connected;
        public event Action<byte[], int, int> DataReceived;
        public event Action<string> Disconnected;

        public void Connect()
        {
            _connected = true;
            _host.OnPeerConnected(this);
            Connected?.Invoke();
        }

        // Client -> host: delivered immediately (the host processes it on its next tick).
        void IClientTransport.Send(byte[] data, bool reliable)
        {
            if (_connected) _host.OnPeerMessage(this, data, 0, data.Length);
        }

        // Host -> client: queued until the client polls (keeps call order identical to a network transport).
        void IHostPeer.Send(byte[] data, bool reliable)
        {
            if (_connected) _toClient.Enqueue(data);
        }

        public void Poll()
        {
            while (_toClient.Count > 0)
            {
                var d = _toClient.Dequeue();
                DataReceived?.Invoke(d, 0, d.Length);
            }
            if (_kickReason != null && _connected)
            {
                _connected = false;
                Disconnected?.Invoke(_kickReason);
            }
        }

        public void Kick(string reason)
        {
            _kickReason = reason ?? "Disconnected";
            _host.OnPeerDisconnected(this, _kickReason);
        }

        public void Disconnect()
        {
            if (!_connected) return;
            _host.OnPeerDisconnected(this, "client disconnected");
            _connected = false;
        }
    }

    /// <summary>Helpers for offline (bot / practice) matches hosted in-process by the client.</summary>
    public static class OfflineSession
    {
        public const string LocalAccountId = "local-player";

        public static MatchHost CreateHost(GameData data, MatchConfig cfg)
        {
            var match = new Match(data, cfg);
            var host = new MatchHost(match) { Region = "offline", ServerId = "offline" };
            host.TicketValidator = ticket =>
            {
                if (ticket == null || !ticket.StartsWith(MatchTickets.OfflinePrefix)) return (null, "Offline host only accepts offline tickets.");
                return (new TicketPayload { AccountId = LocalAccountId, DisplayName = ticket.Substring(MatchTickets.OfflinePrefix.Length), MatchId = cfg.MatchId, Exp = long.MaxValue }, null);
            };
            return host;
        }
    }
}
