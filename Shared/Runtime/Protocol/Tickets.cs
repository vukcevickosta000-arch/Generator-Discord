using System;
using System.Security.Cryptography;
using System.Text;
using Bloodfall.Core;

namespace Bloodfall.Protocol
{
    /// <summary>Claims carried by a match ticket (issued by the lobby/matchmaking service, verified by game servers).</summary>
    public sealed class TicketPayload
    {
        public string MatchId;
        public string AccountId;
        public string DisplayName;
        public string Team;
        public int Slot;
        public bool Spectator;
        /// <summary>Expiry, unix seconds.</summary>
        public long Exp;
        /// <summary>Random nonce so tickets are unique.</summary>
        public string Nonce;
    }

    /// <summary>
    /// HMAC-SHA256 signed tickets: base64url(json) + "." + base64url(hmac). The signing key is shared between the
    /// backend and dedicated game servers only; clients just carry the opaque string.
    /// (Upgrade path documented in NETWORK_ARCHITECTURE.md: switch to ECDSA so game servers only hold a public key.)
    /// </summary>
    public static class MatchTickets
    {
        public const string OfflinePrefix = "offline:";

        public static string Issue(TicketPayload payload, byte[] key)
        {
            if (string.IsNullOrEmpty(payload.Nonce)) payload.Nonce = Guid.NewGuid().ToString("N").Substring(0, 12);
            var json = JsonMapper.ToJson(payload);
            var body = B64Url(Encoding.UTF8.GetBytes(json));
            using (var h = new HMACSHA256(key))
            {
                var sig = B64Url(h.ComputeHash(Encoding.ASCII.GetBytes(body)));
                return body + "." + sig;
            }
        }

        /// <summary>Returns the payload if the signature is valid and the ticket has not expired; otherwise null.</summary>
        public static TicketPayload Validate(string ticket, byte[] key, long nowUnix, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(ticket)) { error = "Missing ticket."; return null; }
            int dot = ticket.IndexOf('.');
            if (dot <= 0 || dot == ticket.Length - 1) { error = "Malformed ticket."; return null; }
            var body = ticket.Substring(0, dot);
            var sig = ticket.Substring(dot + 1);
            using (var h = new HMACSHA256(key))
            {
                var expected = B64Url(h.ComputeHash(Encoding.ASCII.GetBytes(body)));
                if (!FixedTimeEquals(expected, sig)) { error = "Invalid ticket signature."; return null; }
            }
            TicketPayload payload;
            try { payload = JsonMapper.FromJson<TicketPayload>(Encoding.UTF8.GetString(FromB64Url(body))); }
            catch (Exception) { error = "Unreadable ticket."; return null; }
            if (payload.Exp < nowUnix) { error = "Ticket expired."; return null; }
            return payload;
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static string B64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static byte[] FromB64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }

        public static long UnixNow() => (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
    }
}
