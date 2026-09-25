using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bloodfall.Backend.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bloodfall.Backend.Infrastructure.Realtime
{
    public sealed class RealtimeConnection
    {
        public Guid AccountId;
        public string DisplayName;
        public WebSocket Socket;
        public readonly Channel<string> Outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.DropOldest });
        public DateTime ConnectedAt = DateTime.UtcNow;
        public readonly Queue<DateTime> RecentChat = new Queue<DateTime>();
    }

    /// <summary>Server envelope (System.Text.Json flavour of Contracts.RealtimeEnvelope).</summary>
    public sealed class Envelope
    {
        public string Type { get; set; }
        public string Id { get; set; }
        public JsonElement Payload { get; set; }
    }

    public interface IRealtimeHandler
    {
        Task HandleAsync(RealtimeConnection conn, string type, JsonElement payload);
        void OnConnected(RealtimeConnection conn);
        void OnDisconnected(Guid accountId, bool lastConnection);
    }

    /// <summary>
    /// WebSocket hub for push notifications, chat and presence. One account may have several connections
    /// (e.g. client + companion); messages fan out to all of them. Single-node in-memory implementation; the
    /// interface is designed so a Redis backplane can be added for horizontal scaling (see BACKEND_ARCHITECTURE.md).
    /// </summary>
    public sealed class RealtimeHub
    {
        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<RealtimeConnection, byte>> _byAccount = new();
        private readonly ILogger<RealtimeHub> _log;
        private readonly TokenService _tokens;
        public IRealtimeHandler Handler { get; set; }

        public RealtimeHub(ILogger<RealtimeHub> log, TokenService tokens) { _log = log; _tokens = tokens; }

        public int ConnectedAccounts => _byAccount.Count(kv => !kv.Value.IsEmpty);
        public bool IsOnline(Guid accountId) => _byAccount.TryGetValue(accountId, out var set) && !set.IsEmpty;
        public IEnumerable<Guid> OnlineAccounts() => _byAccount.Where(kv => !kv.Value.IsEmpty).Select(kv => kv.Key);

        public void Send(Guid accountId, string type, object payload)
        {
            if (!_byAccount.TryGetValue(accountId, out var set)) return;
            var json = Serialize(type, payload);
            foreach (var c in set.Keys) c.Outbox.Writer.TryWrite(json);
        }

        public void Send(IEnumerable<Guid> accounts, string type, object payload)
        {
            var json = Serialize(type, payload);
            foreach (var id in accounts.Distinct())
                if (_byAccount.TryGetValue(id, out var set))
                    foreach (var c in set.Keys) c.Outbox.Writer.TryWrite(json);
        }

        public void SendRaw(RealtimeConnection c, string type, object payload) => c.Outbox.Writer.TryWrite(Serialize(type, payload));

        private static string Serialize(string type, object payload) =>
            JsonSerializer.Serialize(new { type, payload }, JsonConfig.Options);

        public async Task AcceptAsync(HttpContext ctx)
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            var token = ctx.Request.Query["access_token"].ToString();
            if (string.IsNullOrEmpty(token))
            {
                var auth = ctx.Request.Headers.Authorization.ToString();
                if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = auth.Substring(7);
            }
            ClaimsPrincipal principal;
            try
            {
                principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token, _tokens.ValidationParameters, out _);
            }
            catch (Exception)
            {
                ctx.Response.StatusCode = 401;
                return;
            }
            var socket = await ctx.WebSockets.AcceptWebSocketAsync();
            var conn = new RealtimeConnection { AccountId = principal.AccountId(), DisplayName = principal.DisplayName(), Socket = socket };
            var set = _byAccount.GetOrAdd(conn.AccountId, _ => new ConcurrentDictionary<RealtimeConnection, byte>());
            set[conn] = 0;
            _log.LogInformation("Realtime connected {Account} ({Name})", conn.AccountId, conn.DisplayName);
            try { Handler?.OnConnected(conn); } catch (Exception e) { _log.LogError(e, "OnConnected failed"); }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
            var sendTask = SendLoop(conn, cts.Token);
            try { await ReceiveLoop(conn, cts.Token); }
            catch (Exception e) when (e is WebSocketException || e is OperationCanceledException) { }
            finally
            {
                cts.Cancel();
                set.TryRemove(conn, out _);
                bool last = set.IsEmpty;
                try { Handler?.OnDisconnected(conn.AccountId, last); } catch (Exception e) { _log.LogError(e, "OnDisconnected failed"); }
                _log.LogInformation("Realtime disconnected {Account}", conn.AccountId);
                try { await sendTask; } catch { }
                if (socket.State == WebSocketState.Open)
                    try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
            }
        }

        private async Task ReceiveLoop(RealtimeConnection conn, CancellationToken ct)
        {
            var buffer = new byte[16 * 1024];
            var sb = new StringBuilder();
            while (!ct.IsCancellationRequested && conn.Socket.State == WebSocketState.Open)
            {
                var result = await conn.Socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (sb.Length > 64 * 1024) { sb.Clear(); continue; } // oversized frame: drop
                if (!result.EndOfMessage) continue;
                var text = sb.ToString();
                sb.Clear();
                Envelope env;
                try { env = JsonSerializer.Deserialize<Envelope>(text, JsonConfig.Options); }
                catch (JsonException) { SendRaw(conn, "error", new { message = "Malformed message." }); continue; }
                if (env?.Type == null) continue;
                if (env.Type == "ping") { SendRaw(conn, "pong", new { at = DateTime.UtcNow }); continue; }
                try { if (Handler != null) await Handler.HandleAsync(conn, env.Type, env.Payload); }
                catch (Exception e) { _log.LogWarning(e, "Realtime handler error for {Type}", env.Type); SendRaw(conn, "error", new { message = "Request failed." }); }
            }
        }

        private static async Task SendLoop(RealtimeConnection conn, CancellationToken ct)
        {
            await foreach (var msg in conn.Outbox.Reader.ReadAllAsync(ct))
            {
                if (conn.Socket.State != WebSocketState.Open) break;
                var bytes = Encoding.UTF8.GetBytes(msg);
                await conn.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
        }
    }
}
