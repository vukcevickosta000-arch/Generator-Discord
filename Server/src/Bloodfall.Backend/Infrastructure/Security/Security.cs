using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Bloodfall.Backend.Infrastructure.Data;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Bloodfall.Backend.Infrastructure.Security
{
    public sealed class BackendOptions
    {
        public string Environment { get; set; } = "Development";
        /// <summary>HMAC key for access tokens (JWT HS256). Production: supply via secret store; min 32 bytes.</summary>
        public string JwtSigningKey { get; set; } = "";
        public string JwtIssuer { get; set; } = "bloodfall-auth";
        public string JwtAudience { get; set; } = "bloodfall";
        public int AccessTokenMinutes { get; set; } = 15;
        public int RefreshTokenDays { get; set; } = 30;
        public int ShortRefreshTokenHours { get; set; } = 12;
        /// <summary>Shared secret dedicated game servers present to the directory/stats APIs.</summary>
        public string GameServerKey { get; set; } = "";
        /// <summary>HMAC key for match tickets; the same value is configured on game servers.</summary>
        public string TicketSigningKey { get; set; } = "";
        public int TicketMinutes { get; set; } = 30;
        public string LatestClientVersion { get; set; } = "0.1.0";
        public string MinimumClientVersion { get; set; } = "0.1.0";
        public bool Maintenance { get; set; }
        public string MaintenanceMessage { get; set; } = "";
        public bool SeedDevelopmentContent { get; set; } = true;
        public string[] Regions { get; set; } = { "eu", "na-east", "na-west", "asia", "oce", "sa", "dev-local" };
        /// <summary>Allow lobbies/matchmaking to fall back to any region if the requested one has no servers (dev).</summary>
        public bool AllowCrossRegionFallback { get; set; } = true;
        public int Argon2MemoryKiB { get; set; } = 65536;
        public int Argon2Iterations { get; set; } = 3;
        public int Argon2Parallelism { get; set; } = 1;
    }

    /// <summary>
    /// Argon2id password hashing (PHC-style string: $argon2id$v=19$m=..,t=..,p=..$salt$hash).
    /// Parameters are stored with the hash so they can be raised later; <see cref="NeedsRehash"/> reports stale hashes.
    /// </summary>
    public sealed class PasswordHasher
    {
        private readonly BackendOptions _o;
        public PasswordHasher(IOptions<BackendOptions> options) { _o = options.Value; }

        public string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Compute(password, salt, _o.Argon2MemoryKiB, _o.Argon2Iterations, _o.Argon2Parallelism);
            return $"$argon2id$v=19$m={_o.Argon2MemoryKiB},t={_o.Argon2Iterations},p={_o.Argon2Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        public bool Verify(string password, string stored)
        {
            try
            {
                var parts = stored.Split('$', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 5 || parts[0] != "argon2id") return false;
                int m = 0, t = 0, p = 0;
                foreach (var kv in parts[2].Split(','))
                {
                    var pair = kv.Split('=');
                    if (pair[0] == "m") m = int.Parse(pair[1]);
                    else if (pair[0] == "t") t = int.Parse(pair[1]);
                    else if (pair[0] == "p") p = int.Parse(pair[1]);
                }
                var salt = Convert.FromBase64String(parts[3]);
                var expected = Convert.FromBase64String(parts[4]);
                var actual = Compute(password, salt, m, t, p);
                return CryptographicOperations.FixedTimeEquals(expected, actual);
            }
            catch (FormatException) { return false; }
        }

        public bool NeedsRehash(string stored) => !stored.Contains($"m={_o.Argon2MemoryKiB},t={_o.Argon2Iterations},p={_o.Argon2Parallelism}");

        private static byte[] Compute(string password, byte[] salt, int memoryKiB, int iterations, int parallelism)
        {
            using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
            {
                Salt = salt,
                MemorySize = memoryKiB,
                Iterations = iterations,
                DegreeOfParallelism = parallelism,
            };
            return argon.GetBytes(32);
        }
    }

    public sealed class TokenService
    {
        private readonly BackendOptions _o;
        private readonly SymmetricSecurityKey _key;

        public TokenService(IOptions<BackendOptions> options)
        {
            _o = options.Value;
            _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.JwtSigningKey));
        }

        public TokenValidationParameters ValidationParameters => new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _o.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = _o.JwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role",
        };

        public (string token, int expiresIn) CreateAccessToken(Account a, Guid sessionFamily)
        {
            var now = DateTime.UtcNow;
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, a.Id.ToString()),
                new Claim("name", a.DisplayName),
                new Claim("usr", a.Username),
                new Claim("role", a.Role.ToString()),
                new Claim("sid", sessionFamily.ToString()),
                new Claim("stamp", a.SecurityStamp.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            };
            var token = new JwtSecurityToken(_o.JwtIssuer, _o.JwtAudience, claims, now, now.AddMinutes(_o.AccessTokenMinutes),
                new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
            return (new JwtSecurityTokenHandler().WriteToken(token), _o.AccessTokenMinutes * 60);
        }

        public static string NewOpaqueToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    public static class ClaimsExtensions
    {
        public static Guid AccountId(this ClaimsPrincipal user)
        {
            var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
        }
        public static string DisplayName(this ClaimsPrincipal user) => user.FindFirst("name")?.Value ?? "";
        public static Guid SessionFamily(this ClaimsPrincipal user) => Guid.TryParse(user.FindFirst("sid")?.Value, out var id) ? id : Guid.Empty;
    }

    /// <summary>Input validation rules shared by registration, profile edits and clans.</summary>
    public static class Validation
    {
        private static readonly Regex UsernameRx = new Regex("^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);
        private static readonly Regex DisplayRx = new Regex(@"^[\p{L}\p{N}_\-\. ]{3,20}$", RegexOptions.Compiled);
        private static readonly Regex EmailRx = new Regex(@"^[^@\s]{1,64}@[^@\s]{1,253}\.[^@\s]{2,}$", RegexOptions.Compiled);
        private static readonly Regex ClanTagRx = new Regex("^[A-Za-z0-9]{2,5}$", RegexOptions.Compiled);
        private static readonly string[] Reserved = { "admin", "administrator", "moderator", "system", "bloodfall", "support", "gm", "staff" };

        public static string Username(string u)
        {
            if (string.IsNullOrWhiteSpace(u)) return "Username is required.";
            if (u.Length < 3) return "Username must be at least 3 characters.";
            if (u.Length > 16) return "Username must be at most 16 characters.";
            if (!UsernameRx.IsMatch(u)) return "Username may only contain letters, numbers and underscores.";
            if (Array.Exists(Reserved, r => string.Equals(r, u, StringComparison.OrdinalIgnoreCase))) return "That username is reserved.";
            return null;
        }

        public static string DisplayName(string d)
        {
            if (string.IsNullOrWhiteSpace(d)) return "Display name is required.";
            d = d.Trim();
            if (d.Length < 3 || d.Length > 20) return "Display name must be 3-20 characters.";
            if (!DisplayRx.IsMatch(d)) return "Display name contains invalid characters.";
            if (Array.Exists(Reserved, r => string.Equals(r, d, StringComparison.OrdinalIgnoreCase))) return "That display name is reserved.";
            return null;
        }

        public static string Email(string e)
        {
            if (string.IsNullOrWhiteSpace(e)) return "Email address is required.";
            if (e.Length > 254 || !EmailRx.IsMatch(e.Trim())) return "Email address is invalid.";
            return null;
        }

        public static string Password(string p, string username = null)
        {
            if (string.IsNullOrEmpty(p)) return "Password is required.";
            if (p.Length < 8) return "Password is too short (minimum 8 characters).";
            if (p.Length > 128) return "Password is too long (maximum 128 characters).";
            int classes = 0;
            if (Regex.IsMatch(p, "[a-z]")) classes++;
            if (Regex.IsMatch(p, "[A-Z]")) classes++;
            if (Regex.IsMatch(p, "[0-9]")) classes++;
            if (Regex.IsMatch(p, "[^A-Za-z0-9]")) classes++;
            if (classes < 2) return "Password must mix at least two of: lowercase, uppercase, digits, symbols.";
            if (!string.IsNullOrEmpty(username) && p.IndexOf(username, StringComparison.OrdinalIgnoreCase) >= 0) return "Password must not contain your username.";
            return null;
        }

        public static string ClanTag(string t) => string.IsNullOrWhiteSpace(t) || !ClanTagRx.IsMatch(t) ? "Clan tag must be 2-5 letters or digits." : null;
        public static string ClanName(string n) => string.IsNullOrWhiteSpace(n) || n.Trim().Length < 3 || n.Trim().Length > 24 ? "Clan name must be 3-24 characters." : null;

        public static string Normalize(string s) => (s ?? "").Trim().ToUpperInvariant();

        /// <summary>Strips control characters from chat / free text.</summary>
        public static string CleanText(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(Math.Min(s.Length, max));
            foreach (var c in s)
            {
                if (sb.Length >= max) break;
                if (char.IsControl(c)) continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
