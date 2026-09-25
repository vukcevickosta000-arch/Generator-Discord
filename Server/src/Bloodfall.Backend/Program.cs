using System;
using System.IO;
using System.Threading.RateLimiting;
using Bloodfall.Backend.Infrastructure;
using Bloodfall.Backend.Infrastructure.Data;
using Bloodfall.Backend.Infrastructure.Realtime;
using Bloodfall.Backend.Infrastructure.Security;
using Bloodfall.Backend.Modules.Accounts;
using Bloodfall.Backend.Modules.Auth;
using Bloodfall.Backend.Modules.Content;
using Bloodfall.Backend.Modules.Directory;
using Bloodfall.Backend.Modules.Lobbies;
using Bloodfall.Backend.Modules.Matchmaking;
using Bloodfall.Backend.Modules.Social;
using Bloodfall.Backend.Modules.Stats;
using Bloodfall.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// Bloodfall backend host. All services (auth, accounts, social/chat, clans, lobbies, matchmaking, server
// directory, statistics, content) are modules of one deployable. Modules only share contracts and the database
// abstraction, so each can be moved to its own process later (see BACKEND_ARCHITECTURE.md).
var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<BackendOptions>(builder.Configuration.GetSection("Bloodfall"));
var opts = builder.Configuration.GetSection("Bloodfall").Get<BackendOptions>() ?? new BackendOptions();
if (string.IsNullOrEmpty(opts.JwtSigningKey) || opts.JwtSigningKey.Length < 32)
    throw new InvalidOperationException("Bloodfall:JwtSigningKey must be configured (>= 32 characters). Use user-secrets or environment variables; never commit production secrets.");
if (!builder.Environment.IsDevelopment() && (opts.JwtSigningKey.StartsWith("dev-") || opts.GameServerKey.StartsWith("dev-") || opts.TicketSigningKey.StartsWith("dev-")))
    throw new InvalidOperationException("Development keys detected outside the Development environment. Configure real secrets.");

builder.Services.ConfigureHttpJsonOptions(o => JsonConfig.Apply(o.SerializerOptions));

// Database: SQLite for zero-setup development, PostgreSQL for hosted environments.
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var conn = builder.Configuration["Database:ConnectionString"] ?? "Data Source=data/bloodfall.db";
if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddDbContext<BloodfallDb>(o => o.UseNpgsql(conn));
else
{
    var dataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
    Directory.CreateDirectory(dataDir);
    builder.Services.AddDbContext<BloodfallDb>(o => o.UseSqlite(conn.Replace("data/", dataDir + "/")));
}

// Game data (content hash for client version checks, modes/maps for lobby validation).
var gameDataRoot = builder.Configuration["GameDataPath"];
if (string.IsNullOrEmpty(gameDataRoot)) gameDataRoot = GameDataLoader.FindDefaultRoot(AppContext.BaseDirectory) ?? GameDataLoader.FindDefaultRoot(builder.Environment.ContentRootPath);
var gameData = GameDataLoader.FromDirectory(gameDataRoot ?? throw new InvalidOperationException("GameData folder not found; set GameDataPath."));
if (gameData.Errors.Count > 0) throw new InvalidOperationException("Game data errors: " + string.Join("; ", gameData.Errors));
builder.Services.AddSingleton(gameData);

builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<RealtimeHub>();
builder.Services.AddSingleton<PresenceService>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<PartyService>();
builder.Services.AddSingleton<SocialRealtimeHandler>();
builder.Services.AddSingleton<DirectoryService>();
builder.Services.AddSingleton<TicketIssuer>();
builder.Services.AddSingleton<LobbyService>();
builder.Services.AddSingleton<MatchmakingService>();
builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<FriendService>();
builder.Services.AddScoped<ClanService>();
builder.Services.AddScoped<StatsService>();
builder.Services.AddScoped<ContentService>();
builder.Services.AddScoped<GameServerKeyFilter>();
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddHostedService<MatchmakingLoop>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<TokenService>((o, tokens) =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = tokens.ValidationParameters;
});
builder.Services.AddAuthorization();

// Rate limiting: strict on authentication, generous elsewhere (per client IP).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Environment.IsDevelopment() ? 200 : 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetTokenBucketLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new TokenBucketRateLimiterOptions { TokenLimit = 240, TokensPerPeriod = 120, ReplenishmentPeriod = TimeSpan.FromSeconds(10), AutoReplenishment = true, QueueLimit = 0 }));
});

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var hub = app.Services.GetRequiredService<RealtimeHub>();
hub.Handler = app.Services.GetRequiredService<SocialRealtimeHandler>();
app.Map("/ws", (HttpContext ctx) => hub.AcceptAsync(ctx));

app.MapAuth();
app.MapAccounts();
app.MapSocial();
app.MapClans();
app.MapLobbies();
app.MapMatchmaking();
app.MapDirectory();
app.MapStats();
app.MapContent();

app.Logger.LogInformationStartup(opts, provider, gameData);
app.Run();

public partial class Program { }

internal static class StartupLog
{
    public static void LogInformationStartup(this Microsoft.Extensions.Logging.ILogger log, BackendOptions o, string provider, GameData data) =>
        Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(log, "Bloodfall backend starting ({Env}) db={Provider} content={Hash} heroes={Heroes} items={Items}",
            o.Environment, provider, data.ContentHash.Substring(0, 12), data.Heroes.Count, data.Items.Count);
}
