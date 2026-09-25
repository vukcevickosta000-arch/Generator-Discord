using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Bloodfall.Client.Networking;
using Bloodfall.Contracts;
using Bloodfall.Data;
using Bloodfall.Protocol;
using Bloodfall.Simulation;

// End-to-end test of the real service stack:
//   register two accounts -> create custom lobby -> join -> ready -> start -> directory allocates the game server
//   -> both clients connect over UDP with signed tickets -> hero select -> play -> concede -> result reported
//   -> statistics/profile/match history updated.
// Requires: backend on :5080 and a game server started with --dev-concede-anytime.
namespace Bloodfall.E2E
{
    public static class Program
    {
        static readonly JsonSerializerOptions Json = CreateJson();
        static JsonSerializerOptions CreateJson()
        {
            var o = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            o.Converters.Add(new JsonStringEnumConverter());
            return o;
        }

        static int _failures;
        static void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}"); if (!ok) _failures++; }

        public static async Task<int> Main(string[] args)
        {
            string backend = args.Length > 0 ? args[0] : "http://localhost:5080";
            var data = GameDataLoader.FromDirectory(GameDataLoader.FindDefaultRoot(AppContext.BaseDirectory) ?? GameDataLoader.FindDefaultRoot(Environment.CurrentDirectory));
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 6);

            Console.WriteLine("1. Accounts");
            var a = await Register(backend, "Vorakfan" + suffix);
            var b = await Register(backend, "Bloodwitch" + suffix);
            Check(a.token != null && b.token != null, "two accounts registered and logged in");
            await AccountSecurityChecks(backend, "Vorakfan" + suffix, "Sentinel" + suffix);

            Console.WriteLine("2. Custom lobby");
            var lobby = await Post<LobbyView>(a.http, "api/lobbies", new CreateLobbyRequest { Name = "E2E Blood Duel " + suffix, Region = "dev-local", ModeId = "moba_5v5", TeamSize = 1, FillWithBots = false });
            Check(lobby?.LobbyId != null, $"lobby created ({lobby?.LobbyId})");
            var list = await a.http.GetFromJsonAsync<LobbyView[]>("api/lobbies", Json);
            Check(list.Any(l => l.LobbyId == lobby.LobbyId), "lobby visible in server browser");
            var joined = await Post<LobbyView>(b.http, $"api/lobbies/{lobby.LobbyId}/join", new JoinLobbyRequest());
            Check(joined?.Slots.Count(s => s.AccountId != null) == 2, "second player joined");
            await Post<object>(b.http, "api/lobbies/ready", new LobbyReadyRequest { Ready = true });
            var started = await b.http.PostAsJsonAsync("api/lobbies/start", new { }, Json);
            Check(started.StatusCode == System.Net.HttpStatusCode.Forbidden, "non-host cannot start");
            var start = await a.http.PostAsJsonAsync("api/lobbies/start", new { }, Json);
            var startBody = await start.Content.ReadAsStringAsync();
            Check(start.IsSuccessStatusCode, "host started the match: " + (start.IsSuccessStatusCode ? "ok" : startBody));
            if (!start.IsSuccessStatusCode) return Finish();

            var connA = await a.http.GetFromJsonAsync<GameConnectionInfo>("api/lobbies/mine/connection", Json);
            var connB = await b.http.GetFromJsonAsync<GameConnectionInfo>("api/lobbies/mine/connection", Json);
            Check(connA?.Ticket != null && connB?.Ticket != null, $"tickets issued, server {connA?.Address}:{connA?.Port}");
            int ping = LiteNetClientTransport.PingServer(connA.Address, connA.Port);
            Check(ping >= 0, $"unconnected ping to game server: {ping} ms");

            Console.WriteLine("3. Game server");
            var ca = await ConnectWithRetry(data, connA);
            var cb = await ConnectWithRetry(data, connB);
            Check(ca.State == ClientConnectionState.Connected && cb.State == ClientConnectionState.Connected, "both clients connected with valid tickets");
            Check(ca.LocalTeam != cb.LocalTeam, $"players on opposite teams ({ca.LocalTeam} vs {cb.LocalTeam})");

            // Forged ticket must be rejected.
            var forged = new GameClient(data, new LiteNetClientTransport(connA.Address, connA.Port), new HelloInfo { Ticket = connA.Ticket.Substring(0, connA.Ticket.Length - 3) + "abc", ClientVersion = "0.1.0" });
            forged.Connect();
            await Pump(new[] { forged }, 2f);
            Check(forged.State == ClientConnectionState.Rejected || forged.State == ClientConnectionState.Disconnected, $"forged ticket rejected ({forged.RejectReason ?? forged.DisconnectReason})");

            ca.PickHero("hero_vorak");
            cb.PickHero("hero_ilyra");
            await Pump(new[] { ca, cb }, 1.5f);
            ca.SendLoadProgress(1f);
            cb.SendLoadProgress(1f);
            await Pump(new[] { ca, cb }, 2f);
            Console.WriteLine($"    phase={ca.MatchState?.Phase} timer={ca.MatchState?.PhaseTimer} stateA={ca.State} picks={string.Join(",", ca.MatchState?.Players.Select(p => p.HeroId + ":" + p.HeroLocked) ?? new string[0])} chat={string.Join(" | ", ca.ChatLog.Select(c => c.Text))}");
            Check(ca.Latest != null && cb.Latest != null, "snapshots streaming");
            var heroA = ca.Latest?.Entities.FirstOrDefault(e => e.OwnerPlayer == ca.LocalPlayerId && e.Has(EntityFlags.Hero));
            Check(heroA?.DefId == "hero_vorak", "player A controls Vorak");
            var enemyVisibleAtStart = ca.Latest.Entities.Any(e => e.Has(EntityFlags.Hero) && e.Team != ca.LocalTeam);
            Check(!enemyVisibleAtStart, "fog of war hides the enemy hero at its fountain");

            ca.SendOrder(Order.Buy(heroA.Id, "item_rusted_blade"));
            ca.SendOrder(Order.MoveTo(heroA.Id, heroA.Position + new System.Numerics.Vector2(10, 10)));
            await Pump(new[] { ca, cb }, 2f);
            var movedA = ca.Latest.Entities.First(e => e.Id == heroA.Id);
            Check(System.Numerics.Vector2.Distance(movedA.Position, heroA.Position) > 3, "movement command executed on server");
            Check(ca.Latest.Me.Items[0].Id == "item_rusted_blade", "item purchase validated by server");

            Console.WriteLine("4. Concede -> results");
            cb.SendChat("-ff", false);
            await Pump(new[] { ca, cb }, 4f);
            Check(ca.Result != null && cb.Result != null, $"match ended, winner {ca.Result?.Winner}");
            Check(ca.Result?.Winner == ca.LocalTeam.ToString(), "team that did not concede wins");
            await Task.Delay(2500); // server reports asynchronously

            var profile = await a.http.GetFromJsonAsync<Profile>("api/accounts/me/profile", Json);
            Check(profile?.Stats.GamesPlayed == 1 && profile.Stats.Wins == 1, $"winner profile: {profile?.Stats.GamesPlayed} games / {profile?.Stats.Wins} wins");
            Check(profile?.RecentMatches.FirstOrDefault()?.HeroId == "hero_vorak", "match history shows Vorak");
            var profileB = await b.http.GetFromJsonAsync<Profile>("api/accounts/me/profile", Json);
            Check(profileB?.Stats.Losses == 1, "loser profile records the loss");
            var detail = await a.http.GetFromJsonAsync<MatchDetail>($"api/stats/matches/{ca.Result.MatchId}", Json);
            Check(detail?.Players.Count == 2, "match detail scoreboard stored");
            var me = await a.http.GetFromJsonAsync<AccountSummary>("api/accounts/me", Json);
            Check(me.Xp > 0 || me.Level > 1, $"account XP awarded (level {me.Level}, xp {me.Xp})");

            // The spoofing path: clients cannot post results.
            var spoof = await a.http.PostAsJsonAsync("api/stats/matches", new MatchResult { MatchId = "fake" }, Json);
            Check(spoof.StatusCode == System.Net.HttpStatusCode.Unauthorized, "clients cannot submit match results");

            ca.Disconnect(); cb.Disconnect();

            await StrategyMatch(data, a.http, b.http, suffix);
            return Finish();
        }

        /// <summary>
        /// War of the Ancients over the real stack: faction pick in a lobby, RTS state in snapshots, group orders,
        /// server-side economy, ownership checks, concede, and RTS statistics.
        /// </summary>
        static async Task StrategyMatch(GameData data, HttpClient a, HttpClient b, string suffix)
        {
            Console.WriteLine("5. Strategy (RTS) match");
            var lobby = await Post<LobbyView>(a, "api/lobbies", new CreateLobbyRequest { Name = "E2E Ashfields " + suffix, Region = "dev-local", ModeId = "rts_1v1", FillWithBots = false });
            Check(lobby?.LobbyId != null && lobby.MapId == "map_rts_ashfields" && lobby.TeamSize == 1, $"RTS lobby created on {lobby?.MapId}");
            if (lobby == null) return;
            var bad = await a.PostAsJsonAsync("api/lobbies/faction", new LobbyFactionRequest { Faction = "no_such_faction" }, Json);
            Check((int)bad.StatusCode == 400, "unknown faction rejected");
            await Post<object>(a, "api/lobbies/faction", new LobbyFactionRequest { Faction = "dawnguard" });
            await Post<LobbyView>(b, $"api/lobbies/{lobby.LobbyId}/join", new JoinLobbyRequest());
            await Post<object>(b, "api/lobbies/faction", new LobbyFactionRequest { Faction = "ashen_legion" });
            await Post<object>(b, "api/lobbies/ready", new LobbyReadyRequest { Ready = true });
            var view = await a.GetFromJsonAsync<LobbyView>("api/lobbies/mine", Json);
            Check(view?.Slots.Any(s => s.RtsFaction == "dawnguard") == true && view.Slots.Any(s => s.RtsFaction == "ashen_legion"), "both factions shown in the lobby");
            // The E2E stack has one game server; it becomes free once it has wrapped up the previous match.
            HttpResponseMessage start = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                start = await a.PostAsJsonAsync("api/lobbies/start", new { }, Json);
                if ((int)start.StatusCode != 503) break;
                await Task.Delay(2000);
            }
            Check(start.IsSuccessStatusCode, "RTS match started: " + (start.IsSuccessStatusCode ? "ok" : await start.Content.ReadAsStringAsync()));
            if (!start.IsSuccessStatusCode) return;
            var connA = await a.GetFromJsonAsync<GameConnectionInfo>("api/lobbies/mine/connection", Json);
            var connB = await b.GetFromJsonAsync<GameConnectionInfo>("api/lobbies/mine/connection", Json);
            var ca = await ConnectWithRetry(data, connA);
            var cb = await ConnectWithRetry(data, connB);
            Check(ca.State == ClientConnectionState.Connected && cb.State == ClientConnectionState.Connected, "both clients connected to the RTS match");
            ca.SendLoadProgress(1f);
            cb.SendLoadProgress(1f);
            await Pump(new[] { ca, cb }, 5f);
            var f = ca.Latest;
            Check(f?.Rts != null && f.Rts.Gold == 500 && f.Rts.Lumber == 150 && f.Rts.SupplyUsed == 5 && f.Rts.SupplyCap == 10,
                $"private RTS economy streamed (gold {f?.Rts?.Gold}, lumber {f?.Rts?.Lumber}, supply {f?.Rts?.SupplyUsed}/{f?.Rts?.SupplyCap})");
            if (f?.Rts == null) { ca.Disconnect(); cb.Disconnect(); return; }
            Check(f.Players.Any(p => p.Id == ca.LocalPlayerId && p.RtsFaction == "dawnguard") && f.Players.Any(p => p.Id == cb.LocalPlayerId && p.RtsFaction == "ashen_legion"), "factions from the lobby reached the game server");
            var hall = f.Entities.FirstOrDefault(e => e.OwnerPlayer == ca.LocalPlayerId && e.DefId == "rts_dg_citadel");
            var workers = f.Entities.Where(e => e.OwnerPlayer == ca.LocalPlayerId && e.Kind == UnitKind.Worker).ToList();
            var vein = f.Entities.Where(e => e.Kind == UnitKind.Resource).OrderBy(e => hall == null ? 0 : System.Numerics.Vector2.Distance(e.Position, hall.Position)).FirstOrDefault();
            Check(hall != null && workers.Count == 5 && vein?.ResourceAmount == 12500, $"own hall, 5 workers and a full vein visible ({workers.Count} workers, vein {vein?.ResourceAmount})");
            Check(!f.Entities.Any(e => e.DefId == "rts_al_necropolis"), "fog of war hides the enemy base");
            if (hall == null || workers.Count == 0 || vein == null) { ca.Disconnect(); cb.Disconnect(); return; }

            // One order for the whole selection, then training at the hall.
            ca.SendOrder(new Order { Type = OrderType.Harvest, UnitId = workers[0].Id, TargetId = vein.Id, Group = workers.Skip(1).Select(w => w.Id).ToArray() });
            ca.SendOrder(new Order { Type = OrderType.Train, UnitId = hall.Id, ItemId = "rts_dg_squire" });
            // The opponent tries to use this player's hall.
            cb.SendOrder(new Order { Type = OrderType.Train, UnitId = hall.Id, ItemId = "rts_dg_squire" });
            cb.SendOrder(new Order { Type = OrderType.CancelQueue, UnitId = hall.Id });
            await Pump(new[] { ca, cb }, 1.5f);
            var hallNow = ca.Latest.Entities.First(e => e.Id == hall.Id);
            Check(hallNow.TrainQueue?.Length == 1 && ca.Latest.Rts.Gold <= 500 - 75 + 30, $"training queued and paid (queue {hallNow.TrainQueue?.Length ?? 0}, gold {ca.Latest.Rts.Gold})");
            Check(cb.Latest.Rts.Gold == 500 && hallNow.TrainQueue?.Length == 1, "orders on another player's building are ignored");
            await Pump(new[] { ca, cb }, 12f);
            var fa = ca.Latest;
            int harvesting = fa.Entities.Count(e => e.OwnerPlayer == ca.LocalPlayerId && e.Kind == UnitKind.Worker && (e.Action == ActionState.Working || e.Action == ActionState.Moving || e.CarryGold > 0));
            Check(fa.Rts.Gold > 500 - 75, $"blood-iron mined on the server and streamed (gold {fa.Rts.Gold})");
            Check(harvesting >= 4, $"the group order reached every worker ({harvesting} busy)");
            var veinNow = fa.Entities.FirstOrDefault(e => e.Id == vein.Id);
            Check(veinNow != null && veinNow.ResourceAmount < 12500, $"vein depletes ({veinNow?.ResourceAmount})");

            // Research at the hall: Masonry costs exactly the starting lumber.
            int goldBefore = fa.Rts.Gold;
            ca.SendOrder(new Order { Type = OrderType.Research, UnitId = hall.Id, ItemId = "rts_dg_up_masonry" });
            await Pump(new[] { ca, cb }, 1f);
            var researching = ca.Latest.Entities.First(e => e.Id == hall.Id);
            Check(researching.TrainQueue != null && researching.TrainQueue.Contains("rts_dg_up_masonry") && ca.Latest.Rts.Lumber == 0,
                $"research queued over the wire and paid (queue {string.Join(",", researching.TrainQueue ?? new string[0])}, lumber {ca.Latest.Rts.Lumber}, gold {goldBefore} -> {ca.Latest.Rts.Gold})");

            cb.SendChat("-ff", false);
            await Pump(new[] { ca, cb }, 4f);
            Check(ca.Result?.Winner == ca.LocalTeam.ToString(), $"RTS match ended by concession, winner {ca.Result?.Winner}");
            var rp = ca.Result?.Players.FirstOrDefault(p => p.Team == ca.LocalTeam.ToString());
            Check(rp?.RtsFaction == "dawnguard" && rp.GoldMined > 0, $"result carries faction and economy (mined {rp?.GoldMined})");
            await Task.Delay(2500);
            var profile = await a.GetFromJsonAsync<Profile>("api/accounts/me/profile", Json);
            var last = profile?.RecentMatches.FirstOrDefault();
            Check(last?.ModeId == "rts_1v1" && last.RtsFaction == "dawnguard" && last.Won, "match history shows the RTS win and faction");
            Check(profile?.Heroes.All(h => !string.IsNullOrEmpty(h.HeroId)) == true, "no empty hero statistics from an RTS match");
            ca.Disconnect(); cb.Disconnect();
        }

        static int Finish()
        {
            Console.WriteLine(_failures == 0 ? "E2E PASSED" : $"E2E FAILED ({_failures} checks)");
            return _failures == 0 ? 0 : 1;
        }

        static async Task<(HttpClient http, string token)> Register(string backend, string name)
        {
            var http = new HttpClient { BaseAddress = new Uri(backend + "/") };
            var resp = await http.PostAsJsonAsync("api/auth/register", new RegisterRequest { Username = name, Email = name.ToLowerInvariant() + "@example.com", Password = "Crimson#Moon7", DisplayName = name, Region = "dev-local" }, Json);
            var body = await resp.Content.ReadFromJsonAsync<AuthResponse>(Json);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body?.AccessToken);
            return (http, body?.AccessToken);
        }

        /// <summary>Validation, credential and session-rotation behaviour of the auth service.</summary>
        static async Task AccountSecurityChecks(string backend, string existingName, string freshName)
        {
            var http = new HttpClient { BaseAddress = new Uri(backend + "/") };
            var dup = await http.PostAsJsonAsync("api/auth/register", new RegisterRequest { Username = existingName, Email = "other-" + existingName.ToLowerInvariant() + "@example.com", Password = "Crimson#Moon7" }, Json);
            Check(!dup.IsSuccessStatusCode, $"duplicate username rejected ({(int)dup.StatusCode})");
            var weak = await http.PostAsJsonAsync("api/auth/register", new RegisterRequest { Username = freshName + "x", Email = freshName.ToLowerInvariant() + "x@example.com", Password = "short" }, Json);
            Check((int)weak.StatusCode == 400, $"weak password rejected ({(int)weak.StatusCode})");
            var wrong = await http.PostAsJsonAsync("api/auth/login", new LoginRequest { Login = existingName, Password = "not-the-password" }, Json);
            Check((int)wrong.StatusCode == 401, $"wrong password rejected ({(int)wrong.StatusCode})");
            var ok = await http.PostAsJsonAsync("api/auth/login", new LoginRequest { Login = existingName, Password = "Crimson#Moon7" }, Json);
            Check(ok.IsSuccessStatusCode, "login with correct password");

            // Refresh rotation + reuse detection on a throwaway account.
            var reg = await http.PostAsJsonAsync("api/auth/register", new RegisterRequest { Username = freshName, Email = freshName.ToLowerInvariant() + "@example.com", Password = "Crimson#Moon7" }, Json);
            var first = await reg.Content.ReadFromJsonAsync<AuthResponse>(Json);
            var r1 = await http.PostAsJsonAsync("api/auth/refresh", new RefreshRequest { RefreshToken = first?.RefreshToken }, Json);
            var second = r1.IsSuccessStatusCode ? await r1.Content.ReadFromJsonAsync<AuthResponse>(Json) : null;
            Check(second?.RefreshToken != null && second.RefreshToken != first?.RefreshToken, "refresh token rotated");
            var reuse = await http.PostAsJsonAsync("api/auth/refresh", new RefreshRequest { RefreshToken = first?.RefreshToken }, Json);
            Check((int)reuse.StatusCode == 401, $"reused refresh token rejected ({(int)reuse.StatusCode})");
            var afterReuse = await http.PostAsJsonAsync("api/auth/refresh", new RefreshRequest { RefreshToken = second?.RefreshToken }, Json);
            Check((int)afterReuse.StatusCode == 401, "token family revoked after reuse");
        }

        static async Task<T> Post<T>(HttpClient http, string url, object body)
        {
            var resp = await http.PostAsJsonAsync(url, body, Json);
            if (!resp.IsSuccessStatusCode) { Console.WriteLine($"    {url} -> {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}"); return default; }
            if (resp.Content.Headers.ContentLength == 0) return default;
            return await resp.Content.ReadFromJsonAsync<T>(Json);
        }

        static async Task<GameClient> ConnectWithRetry(GameData data, GameConnectionInfo c)
        {
            for (int attempt = 0; attempt < 15; attempt++)
            {
                var client = new GameClient(data, new LiteNetClientTransport(c.Address, c.Port), new HelloInfo { Ticket = c.Ticket, ClientVersion = "0.1.0" });
                client.Connect();
                await Pump(new[] { client }, 1.2f);
                if (client.State == ClientConnectionState.Connected) return client;
                Console.WriteLine($"    connect attempt {attempt + 1}: {client.State} {client.DisconnectReason ?? client.RejectReason}");
                client.Disconnect();
                await Task.Delay(500);
            }
            return new GameClient(data, new LiteNetClientTransport(c.Address, c.Port), new HelloInfo());
        }

        static async Task Pump(GameClient[] clients, float seconds)
        {
            var end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                foreach (var c in clients) c.Update(0.016f);
                await Task.Delay(16);
            }
        }
    }
}
