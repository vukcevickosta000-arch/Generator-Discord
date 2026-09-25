using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Bloodfall.Protocol;
using LiteNetLib;

namespace Bloodfall.Client.Networking
{
    /// <summary>
    /// UDP transport for the Bloodfall game protocol (LiteNetLib). Engine-independent: used by the Unity client and
    /// by the headless end-to-end test client. Poll() must be called from the main thread each frame.
    /// </summary>
    public sealed class LiteNetClientTransport : IClientTransport
    {
        private readonly string _host;
        private readonly int _port;
        private readonly EventBasedNetListener _listener = new EventBasedNetListener();
        private readonly NetManager _net;
        private readonly FragmentAssembler _assembler = new FragmentAssembler();
        private NetPeer _peer;
        private bool _connected;

        public LiteNetClientTransport(string host, int port)
        {
            _host = host;
            _port = port;
            _net = new NetManager(_listener) { AutoRecycle = true, DisconnectTimeout = 10000, UpdateTime = 5 };
            _listener.PeerConnectedEvent += p => { _connected = true; Connected?.Invoke(); };
            _listener.PeerDisconnectedEvent += (p, info) =>
            {
                _connected = false;
                string reason = info.Reason.ToString();
                if (info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0)
                    reason = Encoding.UTF8.GetString(info.AdditionalData.GetRemainingBytes());
                else if (info.Reason == DisconnectReason.ConnectionFailed) reason = "Could not reach the game server.";
                else if (info.Reason == DisconnectReason.Timeout) reason = "Connection to the game server timed out.";
                Disconnected?.Invoke(reason);
            };
            _listener.NetworkReceiveEvent += (p, reader, channel, method) =>
            {
                var bytes = reader.GetRemainingBytes();
                if (bytes.Length > 0 && bytes[0] == Fragments.PartMarker)
                {
                    var whole = _assembler.Add(bytes, 0, bytes.Length);
                    if (whole != null) DataReceived?.Invoke(whole, 0, whole.Length);
                    return;
                }
                DataReceived?.Invoke(bytes, 0, bytes.Length);
            };
        }

        public bool IsConnected => _connected;
        public int RttMs => _peer?.RoundTripTime ?? 0;
        public event Action Connected;
        public event Action<byte[], int, int> DataReceived;
        public event Action<string> Disconnected;

        public void Connect()
        {
            if (!_net.IsRunning) _net.Start();
            _peer = _net.Connect(_host, _port, ProtocolInfo.ConnectionKey);
        }

        public void Send(byte[] data, bool reliable)
        {
            if (_peer == null || !_connected) return;
            _peer.Send(data, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced);
        }

        public void Poll() => _net.PollEvents();

        public void Disconnect()
        {
            _peer?.Disconnect();
            _net.Stop();
            _connected = false;
        }

        /// <summary>Measures round-trip time to a game server with an unconnected ping (region latency display). Blocking.</summary>
        public static int PingServer(string host, int port, int timeoutMs = 1500)
        {
            var listener = new EventBasedNetListener();
            int result = -1;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var net = new NetManager(listener) { UnconnectedMessagesEnabled = true };
            listener.NetworkReceiveUnconnectedEvent += (ep, reader, type) => { if (result < 0) result = (int)sw.ElapsedMilliseconds; };
            try
            {
                if (!net.Start()) return -1;
                var w = new LiteNetLib.Utils.NetDataWriter();
                w.Put("bf-ping");
                sw.Restart();
                net.SendUnconnectedMessage(w, host, port);
                while (result < 0 && sw.ElapsedMilliseconds < timeoutMs)
                {
                    net.PollEvents();
                    System.Threading.Thread.Sleep(2);
                }
                return result;
            }
            catch (Exception) { return -1; }
            finally { net.Stop(); }
        }
    }
}
