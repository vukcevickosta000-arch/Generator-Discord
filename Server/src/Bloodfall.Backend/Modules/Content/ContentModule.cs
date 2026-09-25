using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bloodfall.Backend.Infrastructure;
using Bloodfall.Backend.Infrastructure.Data;
using Bloodfall.Backend.Infrastructure.Realtime;
using Bloodfall.Backend.Infrastructure.Security;
using Bloodfall.Backend.Modules.Directory;
using Bloodfall.Backend.Modules.Lobbies;
using Bloodfall.Backend.Modules.Matchmaking;
using Bloodfall.Contracts;
using Bloodfall.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bloodfall.Backend.Modules.Content
{
    /// <summary>
    /// News / patch notes (database backed, published by staff) and live service status built from real health
    /// checks. In Development, sample articles are seeded from Content/dev-news.json and flagged DevSample so they
    /// can never leak into production (production seeding is disabled by configuration).
    /// </summary>
    public sealed class ContentService
    {
        private readonly BloodfallDb _db;
        private readonly BackendOptions _o;
        private readonly DirectoryService _directory;
        private readonly RealtimeHub _hub;
        private readonly LobbyService _lobbies;
        private readonly MatchmakingService _mm;
        private readonly GameData _data;

        public ContentService(BloodfallDb db, IOptions<BackendOptions> o, DirectoryService directory, RealtimeHub hub, LobbyService lobbies, MatchmakingService mm, GameData data)
        {
            _db = db; _o = o.Value; _directory = directory; _hub = hub; _lobbies = lobbies; _mm = mm; _data = data;
        }

        public async Task<List<NewsArticleView>> News(string category, int take)
        {
            var q = _db.News.AsQueryable();
            if (!string.IsNullOrEmpty(category)) q = q.Where(n => n.Category == category);
            var list = await q.OrderByDescending(n => n.Pinned).ThenByDescending(n => n.PublishedAt).Take(Math.Clamp(take, 1, 50)).ToListAsync();
            return list.Select(n => new NewsArticleView { Id = n.Id.ToString(), Category = n.Category, Title = n.Title, Summary = n.Summary, Body = n.Body, ImageKey = n.ImageKey, PublishedAt = n.PublishedAt, Pinned = n.Pinned, Author = n.Author }).ToList();
        }

        public async Task<StatusResponse> Status()
        {
            var resp = new StatusResponse { CheckedAt = DateTime.UtcNow, Environment = _o.Environment };
            // Database round trip.
            var sw = Stopwatch.StartNew();
            string dbStatus = "Online", dbDetail = "";
            try { await _db.Accounts.AsNoTracking().Take(1).CountAsync(); }
            catch (Exception e) { dbStatus = "Offline"; dbDetail = e.GetType().Name; }
            sw.Stop();
            resp.Services.Add(new ServiceStatusView { Service = "Authentication", Status = dbStatus, Detail = dbDetail, LatencyMs = (int)sw.ElapsedMilliseconds });
            resp.Services.Add(new ServiceStatusView { Service = "Accounts", Status = dbStatus, LatencyMs = (int)sw.ElapsedMilliseconds });
            resp.Services.Add(new ServiceStatusView { Service = "Chat & Social", Status = "Online", Detail = $"{_hub.ConnectedAccounts} connected" });
            var regions = _directory.Regions();
            resp.Regions = regions;
            int serverCount = regions.Sum(r => r.Servers);
            resp.Services.Add(new ServiceStatusView { Service = "Matchmaking", Status = serverCount > 0 ? "Online" : "Degraded", Detail = serverCount > 0 ? $"{_mm.PlayersSearching} searching" : "No game servers registered" });
            resp.Services.Add(new ServiceStatusView { Service = "Lobbies", Status = "Online", Detail = $"{_lobbies.OpenLobbies} open" });
            foreach (var r in regions.Where(r => r.Servers > 0 || r.Id != "dev-local"))
                resp.Services.Add(new ServiceStatusView { Service = $"{r.Name} Game Servers", Status = r.Status == "NoServers" ? "Offline" : r.Status, Detail = r.Servers > 0 ? $"{r.Servers} servers, {r.IdleServers} idle" : "No servers" });
            resp.PlayersOnline = _hub.ConnectedAccounts;
            resp.MatchesInProgress = _directory.MatchesInProgress;
            return resp;
        }

        public ClientVersionResponse Version() => new ClientVersionResponse
        {
            LatestVersion = _o.LatestClientVersion, MinimumVersion = _o.MinimumClientVersion, ContentHash = _data.ContentHash,
            Maintenance = _o.Maintenance, MaintenanceMessage = _o.MaintenanceMessage,
        };
    }

    /// <summary>Creates the schema on startup and seeds development-only sample news.</summary>
    public sealed class DatabaseInitializer : IHostedService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<DatabaseInitializer> _log;
        private readonly BackendOptions _o;
        public DatabaseInitializer(IServiceProvider sp, ILogger<DatabaseInitializer> log, IOptions<BackendOptions> o) { _sp = sp; _log = log; _o = o.Value; }

        public async Task StartAsync(System.Threading.CancellationToken ct)
        {
            using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(_sp);
            var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<BloodfallDb>(scope.ServiceProvider);
            await db.Database.EnsureCreatedAsync(ct);
            if (_o.SeedDevelopmentContent && !await db.News.AnyAsync(ct))
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Content", "dev-news.json");
                if (File.Exists(path))
                {
                    var items = JsonSerializer.Deserialize<List<NewsArticleView>>(await File.ReadAllTextAsync(path, ct), JsonConfig.Options);
                    int i = 0;
                    foreach (var n in items)
                        db.News.Add(new NewsArticle { Id = Guid.NewGuid(), Category = n.Category, Title = n.Title, Summary = n.Summary, Body = n.Body, ImageKey = n.ImageKey, Pinned = n.Pinned, Author = n.Author, PublishedAt = DateTime.UtcNow.AddDays(-i++ * 2), DevSample = true });
                    await db.SaveChangesAsync(ct);
                    _log.LogInformation("Seeded {Count} development news articles", items.Count);
                }
            }
        }

        public Task StopAsync(System.Threading.CancellationToken ct) => Task.CompletedTask;
    }

    public static class ContentEndpoints
    {
        public static void MapContent(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/content/news", async (string category, int? take, ContentService s) => Api.Ok(await s.News(category, take ?? 12)));
            app.MapGet("/api/content/status", async (ContentService s) => Api.Ok(await s.Status()));
            app.MapGet("/api/content/client-version", (ContentService s) => Api.Ok(s.Version()));
            app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
            app.MapPost("/api/content/news", async (NewsArticleView n, BloodfallDb db, HttpContext c) =>
            {
                if (!c.User.IsInRole("Admin")) return Api.Forbidden("Administrators only.");
                db.News.Add(new NewsArticle { Id = Guid.NewGuid(), Category = n.Category ?? "Updates", Title = Validation.CleanText(n.Title, 120), Summary = Validation.CleanText(n.Summary, 400), Body = n.Body ?? "", ImageKey = n.ImageKey ?? "", Pinned = n.Pinned, Author = c.User.DisplayName(), PublishedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
                return Results.NoContent();
            }).RequireAuthorization();
        }
    }
}
