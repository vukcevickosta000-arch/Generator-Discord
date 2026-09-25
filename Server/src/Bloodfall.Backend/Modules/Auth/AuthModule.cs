using System;
using System.Linq;
using System.Threading.Tasks;
using Bloodfall.Backend.Infrastructure;
using Bloodfall.Backend.Infrastructure.Data;
using Bloodfall.Backend.Infrastructure.Security;
using Bloodfall.Backend.Modules.Accounts;
using Bloodfall.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bloodfall.Backend.Modules.Auth
{
    /// <summary>
    /// Authentication service: registration, login with lockout, rotating refresh tokens with reuse detection,
    /// logout, session listing/revocation, e-mail verification and password reset tokens.
    /// </summary>
    public sealed class AuthService
    {
        private readonly BloodfallDb _db;
        private readonly PasswordHasher _hasher;
        private readonly TokenService _tokens;
        private readonly BackendOptions _o;
        private readonly AccountService _accounts;
        private readonly IEmailSender _email;
        private readonly ILogger<AuthService> _log;

        public AuthService(BloodfallDb db, PasswordHasher hasher, TokenService tokens, IOptions<BackendOptions> o, AccountService accounts, IEmailSender email, ILogger<AuthService> log)
        {
            _db = db; _hasher = hasher; _tokens = tokens; _o = o.Value; _accounts = accounts; _email = email; _log = log;
        }

        public async Task<IResult> Register(RegisterRequest r, HttpContext ctx)
        {
            if (r == null) return Api.BadRequest("invalid_request", "Request body is required.");
            string err;
            if ((err = Validation.Username(r.Username)) != null) return Api.BadRequest("invalid_username", err, "username");
            if ((err = Validation.Email(r.Email)) != null) return Api.BadRequest("invalid_email", err, "email");
            if ((err = Validation.Password(r.Password, r.Username)) != null) return Api.BadRequest("invalid_password", err, "password");
            var display = string.IsNullOrWhiteSpace(r.DisplayName) ? r.Username : r.DisplayName.Trim();
            if ((err = Validation.DisplayName(display)) != null) return Api.BadRequest("invalid_display_name", err, "displayName");

            var un = Validation.Normalize(r.Username);
            var em = Validation.Normalize(r.Email);
            if (await _db.Accounts.AnyAsync(a => a.UsernameNormalized == un)) return Api.Conflict("username_taken", "Username already exists.", "username");
            if (await _db.Accounts.AnyAsync(a => a.EmailNormalized == em)) return Api.Conflict("email_taken", "An account with this email address already exists.", "email");

            var region = _o.Regions.Contains(r.Region) ? r.Region : "eu";
            var account = new Account
            {
                Id = Guid.NewGuid(),
                Username = r.Username,
                UsernameNormalized = un,
                Email = r.Email.Trim(),
                EmailNormalized = em,
                PasswordHash = _hasher.Hash(r.Password),
                DisplayName = display,
                CreatedAt = DateTime.UtcNow,
                Region = region,
                Language = string.IsNullOrWhiteSpace(r.Language) ? "en" : r.Language.Trim().ToLowerInvariant(),
            };
            _db.Accounts.Add(account);
            _db.Stats.Add(new AccountStats { AccountId = account.Id });
            _db.Ratings.Add(new PlayerRating { AccountId = account.Id, Queue = "ranked", Season = "S1" });
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Race with a concurrent registration: unique indexes are the final authority.
                return Api.Conflict("account_exists", "Username or email already exists.");
            }
            await IssueEmailToken(account, EmailTokenPurpose.VerifyEmail);
            Audit(account.Id, "register", ctx);
            _log.LogInformation("Registered account {User} ({Id})", account.Username, account.Id);
            return Api.Ok(await CreateSession(account, true, ctx));
        }

        public async Task<IResult> Login(LoginRequest r, HttpContext ctx)
        {
            if (r == null || string.IsNullOrWhiteSpace(r.Login) || string.IsNullOrEmpty(r.Password))
                return Api.BadRequest("invalid_request", "Enter your username (or email) and password.");
            if (_o.Maintenance) return Api.Error(503, "maintenance", string.IsNullOrEmpty(_o.MaintenanceMessage) ? "Bloodfall services are under maintenance." : _o.MaintenanceMessage);
            var key = Validation.Normalize(r.Login);
            var a = await _db.Accounts.FirstOrDefaultAsync(x => x.UsernameNormalized == key || x.EmailNormalized == key);
            // Uniform error for unknown user / wrong password (no account enumeration). Hash anyway to equalise timing.
            if (a == null) { _hasher.Hash(r.Password); return Api.Error(401, "invalid_credentials", "Incorrect username or password."); }
            if (a.LockoutUntil.HasValue && a.LockoutUntil > DateTime.UtcNow)
                return Api.Error(429, "locked_out", $"Too many failed attempts. Try again in {Math.Ceiling((a.LockoutUntil.Value - DateTime.UtcNow).TotalMinutes)} minute(s).");
            if (!_hasher.Verify(r.Password, a.PasswordHash))
            {
                a.FailedLogins++;
                if (a.FailedLogins >= 8) { a.LockoutUntil = DateTime.UtcNow.AddMinutes(10); a.FailedLogins = 0; }
                await _db.SaveChangesAsync();
                Audit(a.Id, "login_failed", ctx);
                return Api.Error(401, "invalid_credentials", "Incorrect username or password.");
            }
            var ban = await ActiveBan(a.Id);
            if (a.Status == AccountStatus.Banned || ban != null)
                return Api.Error(403, "banned", ban?.ExpiresAt != null ? $"This account is suspended until {ban.ExpiresAt:yyyy-MM-dd HH:mm} UTC. Reason: {ban.Reason}" : $"This account has been banned. {ban?.Reason}");
            if (a.Status == AccountStatus.Suspended && a.SuspendedUntil > DateTime.UtcNow)
                return Api.Error(403, "suspended", $"This account is suspended until {a.SuspendedUntil:yyyy-MM-dd HH:mm} UTC.");
            a.FailedLogins = 0;
            a.LockoutUntil = null;
            a.LastLoginAt = DateTime.UtcNow;
            if (_hasher.NeedsRehash(a.PasswordHash)) a.PasswordHash = _hasher.Hash(r.Password);
            await _db.SaveChangesAsync();
            Audit(a.Id, "login", ctx);
            return Api.Ok(await CreateSession(a, r.RememberMe, ctx));
        }

        public async Task<IResult> Refresh(RefreshRequest r, HttpContext ctx)
        {
            if (string.IsNullOrEmpty(r?.RefreshToken)) return Api.Error(401, "invalid_refresh", "Session expired. Please log in again.");
            var hash = TokenService.Sha256(r.RefreshToken);
            var rt = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
            if (rt == null) return Api.Error(401, "invalid_refresh", "Session expired. Please log in again.");
            if (rt.RevokedAt != null)
            {
                // Reuse of a rotated token: assume theft and revoke the whole family.
                if (rt.ReplacedById != null)
                {
                    var family = await _db.RefreshTokens.Where(t => t.FamilyId == rt.FamilyId && t.RevokedAt == null).ToListAsync();
                    foreach (var f in family) f.RevokedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                    Audit(rt.AccountId, "refresh_reuse_detected", ctx);
                }
                return Api.Error(401, "invalid_refresh", "Session expired. Please log in again.");
            }
            if (rt.ExpiresAt < DateTime.UtcNow) return Api.Error(401, "invalid_refresh", "Session expired. Please log in again.");
            var a = await _db.Accounts.FindAsync(rt.AccountId);
            if (a == null || a.Status == AccountStatus.Banned || await ActiveBan(a.Id) != null) return Api.Error(403, "banned", "This account cannot log in.");

            var raw = TokenService.NewOpaqueToken();
            var next = new RefreshToken
            {
                Id = Guid.NewGuid(), AccountId = a.Id, TokenHash = TokenService.Sha256(raw), FamilyId = rt.FamilyId,
                CreatedAt = DateTime.UtcNow, ExpiresAt = rt.ExpiresAt, UserAgent = rt.UserAgent, Ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "",
            };
            rt.RevokedAt = DateTime.UtcNow;
            rt.ReplacedById = next.Id;
            _db.RefreshTokens.Add(next);
            await _db.SaveChangesAsync();
            var (access, exp) = _tokens.CreateAccessToken(a, rt.FamilyId);
            return Api.Ok(new AuthResponse { AccessToken = access, RefreshToken = raw, ExpiresIn = exp, Account = await _accounts.Summary(a) });
        }

        public async Task<IResult> Logout(LogoutRequest r)
        {
            if (!string.IsNullOrEmpty(r?.RefreshToken))
            {
                var hash = TokenService.Sha256(r.RefreshToken);
                var rt = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
                if (rt != null)
                {
                    var family = await _db.RefreshTokens.Where(t => t.FamilyId == rt.FamilyId && t.RevokedAt == null).ToListAsync();
                    foreach (var f in family) f.RevokedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }
            return Results.NoContent();
        }

        public async Task<IResult> ForgotPassword(ForgotPasswordRequest r)
        {
            var em = Validation.Normalize(r?.Email);
            var a = await _db.Accounts.FirstOrDefaultAsync(x => x.EmailNormalized == em);
            if (a != null) await IssueEmailToken(a, EmailTokenPurpose.ResetPassword);
            // Always the same response: never reveal whether the address exists.
            return Results.Accepted(value: new { message = "If an account exists for that address, a reset link has been sent." });
        }

        public async Task<IResult> ResetPassword(ResetPasswordRequest r)
        {
            if (string.IsNullOrEmpty(r?.Token)) return Api.BadRequest("invalid_token", "Reset link is invalid or has expired.");
            var hash = TokenService.Sha256(r.Token);
            var t = await _db.EmailTokens.FirstOrDefaultAsync(x => x.TokenHash == hash && x.Purpose == EmailTokenPurpose.ResetPassword);
            if (t == null || t.UsedAt != null || t.ExpiresAt < DateTime.UtcNow) return Api.BadRequest("invalid_token", "Reset link is invalid or has expired.");
            var a = await _db.Accounts.FindAsync(t.AccountId);
            string err;
            if ((err = Validation.Password(r.NewPassword, a?.Username)) != null) return Api.BadRequest("invalid_password", err, "newPassword");
            a.PasswordHash = _hasher.Hash(r.NewPassword);
            a.SecurityStamp++;
            t.UsedAt = DateTime.UtcNow;
            foreach (var rt in await _db.RefreshTokens.Where(x => x.AccountId == a.Id && x.RevokedAt == null).ToListAsync()) rt.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Results.NoContent();
        }

        public async Task<IResult> VerifyEmail(VerifyEmailRequest r)
        {
            var hash = TokenService.Sha256(r?.Token ?? "");
            var t = await _db.EmailTokens.FirstOrDefaultAsync(x => x.TokenHash == hash && x.Purpose == EmailTokenPurpose.VerifyEmail);
            if (t == null || t.UsedAt != null || t.ExpiresAt < DateTime.UtcNow) return Api.BadRequest("invalid_token", "Verification link is invalid or has expired.");
            var a = await _db.Accounts.FindAsync(t.AccountId);
            a.EmailVerified = true;
            t.UsedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Results.NoContent();
        }

        public async Task<IResult> CheckUsername(string name)
        {
            var err = Validation.Username(name);
            if (err != null) return Api.Ok(new AvailabilityResponse { Available = false, Reason = err });
            var n = Validation.Normalize(name);
            bool taken = await _db.Accounts.AnyAsync(a => a.UsernameNormalized == n);
            return Api.Ok(new AvailabilityResponse { Available = !taken, Reason = taken ? "Username already exists." : null });
        }

        public async Task<IResult> Sessions(Guid accountId, Guid currentFamily)
        {
            var list = await _db.RefreshTokens.Where(t => t.AccountId == accountId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(t => t.CreatedAt).ToListAsync();
            return Api.Ok(list.GroupBy(t => t.FamilyId).Select(g => new SessionInfo
            {
                Id = g.Key.ToString(), CreatedAt = g.Min(x => x.CreatedAt), ExpiresAt = g.Max(x => x.ExpiresAt),
                UserAgent = g.First().UserAgent, Current = g.Key == currentFamily,
            }).ToList());
        }

        public async Task<IResult> RevokeSession(Guid accountId, Guid family)
        {
            var list = await _db.RefreshTokens.Where(t => t.AccountId == accountId && t.FamilyId == family && t.RevokedAt == null).ToListAsync();
            foreach (var t in list) t.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Results.NoContent();
        }

        private async Task<AuthResponse> CreateSession(Account a, bool remember, HttpContext ctx)
        {
            var raw = TokenService.NewOpaqueToken();
            var family = Guid.NewGuid();
            _db.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(), AccountId = a.Id, TokenHash = TokenService.Sha256(raw), FamilyId = family, CreatedAt = DateTime.UtcNow,
                ExpiresAt = remember ? DateTime.UtcNow.AddDays(_o.RefreshTokenDays) : DateTime.UtcNow.AddHours(_o.ShortRefreshTokenHours),
                UserAgent = Validation.CleanText(ctx.Request.Headers.UserAgent.ToString(), 200), Ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "",
            });
            await _db.SaveChangesAsync();
            var (access, exp) = _tokens.CreateAccessToken(a, family);
            return new AuthResponse { AccessToken = access, RefreshToken = raw, ExpiresIn = exp, Account = await _accounts.Summary(a) };
        }

        private async Task IssueEmailToken(Account a, EmailTokenPurpose purpose)
        {
            var raw = TokenService.NewOpaqueToken();
            _db.EmailTokens.Add(new EmailToken
            {
                Id = Guid.NewGuid(), AccountId = a.Id, Purpose = purpose, TokenHash = TokenService.Sha256(raw),
                ExpiresAt = DateTime.UtcNow.AddHours(purpose == EmailTokenPurpose.ResetPassword ? 1 : 48),
            });
            await _db.SaveChangesAsync();
            await _email.SendAsync(a.Email, purpose == EmailTokenPurpose.ResetPassword ? "Reset your Bloodfall password" : "Verify your Bloodfall account",
                purpose == EmailTokenPurpose.ResetPassword ? $"Your password reset code: {raw}" : $"Your verification code: {raw}", raw);
        }

        private Task<Ban> ActiveBan(Guid id) =>
            _db.Bans.Where(b => b.AccountId == id && (b.ExpiresAt == null || b.ExpiresAt > DateTime.UtcNow)).OrderByDescending(b => b.CreatedAt).FirstOrDefaultAsync();

        private void Audit(Guid id, string action, HttpContext ctx)
        {
            _db.Audit.Add(new AuditEntry { At = DateTime.UtcNow, AccountId = id, Action = action, Ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "" });
            _db.SaveChanges();
        }
    }

    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string body, string token);
    }

    /// <summary>Development e-mail sender: writes messages to the log (no real e-mail infrastructure yet).</summary>
    public sealed class LogEmailSender : IEmailSender
    {
        private readonly ILogger<LogEmailSender> _log;
        public LogEmailSender(ILogger<LogEmailSender> log) { _log = log; }
        public Task SendAsync(string to, string subject, string body, string token)
        {
            _log.LogInformation("[DEV EMAIL] to={To} subject=\"{Subject}\" body=\"{Body}\"", to, subject, body);
            return Task.CompletedTask;
        }
    }

    public static class AuthEndpoints
    {
        public static void MapAuth(this IEndpointRouteBuilder app)
        {
            var g = app.MapGroup("/api/auth").RequireRateLimiting("auth");
            g.MapPost("/register", (RegisterRequest r, AuthService s, HttpContext c) => s.Register(r, c));
            g.MapPost("/login", (LoginRequest r, AuthService s, HttpContext c) => s.Login(r, c));
            g.MapPost("/refresh", (RefreshRequest r, AuthService s, HttpContext c) => s.Refresh(r, c));
            g.MapPost("/logout", (LogoutRequest r, AuthService s) => s.Logout(r));
            g.MapPost("/forgot-password", (ForgotPasswordRequest r, AuthService s) => s.ForgotPassword(r));
            g.MapPost("/reset-password", (ResetPasswordRequest r, AuthService s) => s.ResetPassword(r));
            g.MapPost("/verify-email", (VerifyEmailRequest r, AuthService s) => s.VerifyEmail(r));
            g.MapGet("/check-username", (string name, AuthService s) => s.CheckUsername(name));
            var authed = app.MapGroup("/api/auth").RequireAuthorization();
            authed.MapGet("/sessions", (AuthService s, HttpContext c) => s.Sessions(c.User.AccountId(), c.User.SessionFamily()));
            authed.MapDelete("/sessions/{id:guid}", (Guid id, AuthService s, HttpContext c) => s.RevokeSession(c.User.AccountId(), id));
        }
    }
}
