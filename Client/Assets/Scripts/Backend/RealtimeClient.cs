using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloodfall.Core;
using UnityEngine;

namespace Bloodfall.Client.Backend
{
    /// <summary>
    /// WebSocket connection to the realtime hub (chat, presence, invites, lobby and matchmaking pushes).
    /// Network I/O runs on a background task; messages are handed to the main thread through a queue drained in
    /// <see cref="Pump"/>. Reconnects automatically with backoff while logged in.
    /// </summary>
    public sealed class RealtimeClient : IDisposable
    {
        private readonly string _url;
        private readonly Func<string> _token;
        private readonly ConcurrentQueue<(string type, JsonNode payload)> _inbox = new ConcurrentQueue<(string, JsonNode)>();
        private readonly ConcurrentQueue<string> _outbox = new ConcurrentQueue<string>();
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private volatile bool _wanted;
        private float _backoff = 1f;
        public bool Connected => _ws != null && _ws.State == WebSocketState.Open;
        public event Action<string, JsonNode> Message;
        public event Action<bool> ConnectionChanged;
        private bool _lastConnected;

        public RealtimeClient(string baseUrl, Func<string> accessToken)
        {
            _url = (baseUrl.StartsWith("https") ? "wss" + baseUrl.Substring(5) : "ws" + baseUrl.Substring(4)) + "/ws";
            _token = accessToken;
        }

        public void Start()
        {
            if (_wanted) return;
            _wanted = true;
            _cts = new CancellationTokenSource();
            Task.Run(() => RunLoop(_cts.Token));
        }

        public void Stop()
        {
            _wanted = false;
            try { _cts?.Cancel(); } catch { }
            try { _ws?.Abort(); } catch { }
        }

        public void Send(string type, object payload)
        {
            var node = JsonNode.NewObject();
            node["type"] = JsonNode.From(type);
            node["payload"] = JsonMapper.ToNode(payload);
            _outbox.Enqueue(Json.Write(node, false));
        }

        /// <summary>Main thread: dispatch received messages.</summary>
        public void Pump()
        {
            bool c = Connected;
            if (c != _lastConnected) { _lastConnected = c; ConnectionChanged?.Invoke(c); }
            while (_inbox.TryDequeue(out var m))
            {
                try { Message?.Invoke(m.type, m.payload); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private async Task RunLoop(CancellationToken ct)
        {
            while (_wanted && !ct.IsCancellationRequested)
            {
                try
                {
                    _ws = new ClientWebSocket();
                    _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    var uri = new Uri(_url + "?access_token=" + Uri.EscapeDataString(_token() ?? ""));
                    await _ws.ConnectAsync(uri, ct);
                    _backoff = 1f;
                    var send = SendLoop(ct);
                    await ReceiveLoop(ct);
                    await send;
                }
                catch (Exception) { }
                finally
                {
                    try { _ws?.Dispose(); } catch { }
                }
                if (!_wanted) break;
                await Task.Delay(TimeSpan.FromSeconds(_backoff));
                _backoff = Math.Min(30f, _backoff * 2f);
            }
        }

        private async Task SendLoop(CancellationToken ct)
        {
            while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                while (_outbox.TryDequeue(out var msg))
                    await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true, ct);
                await Task.Delay(30, ct);
            }
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buf = new byte[32 * 1024];
            var sb = new StringBuilder();
            while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (r.MessageType == WebSocketMessageType.Close) break;
                sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                if (!r.EndOfMessage) continue;
                var text = sb.ToString();
                sb.Clear();
                if (!Json.TryParse(text, out var node, out _)) continue;
                _inbox.Enqueue((node["type"].AsString(), node["payload"]));
            }
        }

        public void Dispose() => Stop();
    }
}
