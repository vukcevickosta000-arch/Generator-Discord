using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Bloodfall.Backend.Infrastructure;
using Bloodfall.Backend.Infrastructure.Data;
using Bloodfall.Backend.Infrastructure.Realtime;
using Bloodfall.Backend.Infrastructure.Security;
using Bloodfall.Backend.Modules.Directory;
using Bloodfall.Backend.Modules.Social;
using Bloodfall.Contracts;
using Bloodfall.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bloodfall.Backend.Modules.Lobbies
{
    public sealed class LobbySlot
    {
        public Guid? AccountId;
        public string Name;
        public bool IsBot;
        public string BotDifficulty = "Normal";
        public bool Ready;
        public int Level;
        public string Rank;
        public int Rating = 1500;
        /// <summary>RTS: faction id, or null for random.</summary>
        public string RtsFaction;
    }

    public sealed class Lobby
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 10);
        public string Name;
        public byte[] PasswordHash;
        public Guid HostId;
        public string Region;
        public string MapId;
        public string ModeId;
        public string ModeName;
        public int TeamSize;
        public int MaxSpectators;
        public bool IsPrivate;
        public bool Ranked;
        public string PickMode;
        public string BotDifficulty;
        public bool FillWithBots;
        public float GameSpeed = 1f;
        public List<string> Modifiers = new List<string>();
        public int MinRating, MaxRating;
        public string Status = "Waiting";
        public readonly LobbySlot[] Dawn;
        public readonly LobbySlot[] Dusk;
        public readonly HashSet<Guid> Spectators = new HashSet<Guid>();
        public DateTime CreatedAt = DateTime.UtcNow;
        public DateTime LastActivity = DateTime.UtcNow;
        public string MatchId;
        public GameServerInfo Server;
        public bool FromMatchmaking;
        public string Queue;

        public Lobby(int teamSize) { Dawn = NewSlots(teamSize); Dusk = NewSlots(teamSize); }
        private static LobbySlot[] NewSlots(int n) { var a = new LobbySlot[n]; for (int i = 0; i < n; i++) a[i] = new LobbySlot(); return a; }
        public IEnumerable<(string team, int slot, LobbySlot s)> AllSlots() =>
            Dawn.Select((s, i) => ("Dawn", i, s)).Concat(Dusk.Select((s, i) => ("Dusk", i, s)));
        public IEnumerable<Guid> Humans() => AllSlots().Where(x => x.s.AccountId != null).Select(x => x.s.AccountId.Value);
        public IEnumerable<Guid> Everyone() => Humans().Concat(Spectators);
        public LobbySlot SlotOf(Guid id) => AllSlots().FirstOrDefault(x => x.s.AccountId == id).s;
    }

    /// <summary>
    /// Custom game lobbies (the server browser lists these) and the lobby stage of matchmade games.
    /// In-memory, single node. Starting a lobby allocates a dedicated server through the directory and issues
    /// signed tickets to every participant.
    /// </summary>
    public sealed class LobbyService
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, Lobby> _lobbies = new();
        private readonly Dictionary<Guid, string> _memberOf = new();
        private readonly RealtimeHub _hub;
        private readonly PresenceService _presence;
        private readonly ChatService _chat;
        private readonly DirectoryService _directory;
        private readonly TicketIssuer _tickets;
        private readonly IServiceScopeFactory _scopes;
        private readonly GameData _data;
        private readonly ILogger<LobbyService> _log;

        public LobbyService(RealtimeHub hub, PresenceService presence, ChatService chat, DirectoryService directory, TicketIssuer tickets, IServiceScopeFactory scopes, GameData data, ILogger<LobbyService> log)
        {
            _hub = hub; _presence = presence; _chat = chat; _directory = directory; _tickets = tickets; _scopes = scopes; _data = data; _log = log;
            _directory.MatchFinished += (matchId, _) => OnMatchFinished(matchId);
            _directory.ServerLost += OnServerLost;
        }

        public int OpenLobbies { get { lock (_lock) return _lobbies.Count; } }

        private static byte[] HashPassword(string p) => string.IsNullOrEmpty(p) ? null : SHA256.HashData(Encoding.UTF8.GetBytes("bf-lobby:" + p));

        private async Task<(int level, string rank, int rating, string name)> PlayerInfo(Guid id)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BloodfallDb>();
            var a = await db.Accounts.FindAsync(id);
            var r = await db.Ratings.FirstOrDefaultAsync(x => x.AccountId == id && x.Queue == "ranked" && x.Season == "S1");
            int rating = r?.Rating ?? 1500;
            return (a?.Level ?? 1, (r?.Games ?? 0) < 10 ? "Unranked" : Ranks.For(rating).tier, rating, a?.DisplayName ?? "Player");
        }

        public LobbyView View(Lobby l, Guid viewer)
        {
            bool member = l.Everyone().Contains(viewer);
            var humans = l.AllSlots().Where(x => x.s.AccountId != null).ToList();
            var v = new LobbyView
            {
                LobbyId = l.Id, Name = l.Name, HostAccountId = l.HostId.ToString(), HostName = l.SlotOf(l.HostId)?.Name ?? _presence.Name(l.HostId), Region = l.Region,
                MapId = l.MapId, ModeId = l.ModeId, ModeName = l.ModeName, TeamSize = l.TeamSize,
                Players = l.AllSlots().Count(x => x.s.AccountId != null || x.s.IsBot), MaxPlayers = l.TeamSize * 2,
                Spectators = l.Spectators.Count, MaxSpectators = l.MaxSpectators, HasPassword = l.PasswordHash != null, IsPrivate = l.IsPrivate,
                Ranked = l.Ranked, PickMode = l.PickMode, Status = l.Status, MinRating = l.MinRating, MaxRating = l.MaxRating,
                AverageRating = humans.Count > 0 ? (int)humans.Average(x => x.s.Rating) : 0, GameSpeed = l.GameSpeed, Modifiers = l.Modifiers,
                CreatedAt = l.CreatedAt, MatchId = member ? l.MatchId : null,
                ServerAddress = member ? l.Server?.Address : null, ServerPort = member ? l.Server?.Port ?? 0 : 0,
            };
            foreach (var (team, slot, s) in l.AllSlots())
                v.Slots.Add(new LobbySlotView { Team = team, Slot = slot, AccountId = s.AccountId?.ToString(), DisplayName = s.IsBot ? $"Bot ({s.BotDifficulty})" : s.Name, IsBot = s.IsBot, BotDifficulty = s.BotDifficulty, Ready = s.Ready, Host = s.AccountId == l.HostId, Level = s.Level, Rank = s.Rank, RtsFaction = s.RtsFaction });
            foreach (var sp in l.Spectators) v.SpectatorList.Add(new LobbySlotView { Team = "Spectator", AccountId = sp.ToString(), DisplayName = _presence.Name(sp) });
            return v;
        }

        public List<LobbyView> List(Guid viewer)
        {
            lock (_lock)
                return _lobbies.Values.Where(l => !l.IsPrivate || l.Everyone().Contains(viewer)).Where(l => !l.FromMatchmaking)
                    .OrderBy(l => l.Status == "Waiting" ? 0 : 1).ThenByDescending(l => l.CreatedAt).Select(l => View(l, viewer)).ToList();
        }

        public LobbyView Get(string id, Guid viewer)
        {
            lock (_lock) return _lobbies.TryGetValue(id ?? "", out var l) && (!l.IsPrivate || l.Everyone().Contains(viewer) || true) ? View(l, viewer) : null;
        }

        public LobbyView Mine(Guid me)
        {
            lock (_lock) return _memberOf.TryGetValue(me, out var id) && _lobbies.TryGetValue(id, out var l) ? View(l, me) : null;
        }

        public async Task<IResult> Create(Guid me, CreateLobbyRequest r)
        {
            if (r == null) return Api.BadRequest("invalid", "Request body required.");
            var name = Validation.CleanText(r.Name, 32);
            if (name.Length < 3) return Api.BadRequest("invalid_name", "Game name must be at least 3 characters.", "name");
            if (!_data.Modes.TryGetValue(r.ModeId ?? "", out var mode)) return Api.BadRequest("invalid_mode", "Unknown game mode.", "modeId");
            if (!_data.Maps.TryGetValue(r.MapId ?? mode.Map, out var map)) return Api.BadRequest("invalid_map", "Unknown map.", "mapId");
            if (r.Ranked) return Api.BadRequest("ranked_custom", "Custom games cannot be ranked. Use the ranked queue.", "ranked");
            int teamSize = Math.Clamp(r.TeamSize <= 0 ? mode.TeamSize : r.TeamSize, 1, 5);
            if (mode.Kind == GameModeKind.Moba && map.Lanes.Count == 0) return Api.BadRequest("invalid_map", "That map is not a Blood War map.", "mapId");
            if (mode.Kind == GameModeKind.Rts)
            {
                // One start location per player on RTS maps.
                int starts = Math.Min(map.StartLocations.Count(s => s.Team == Team.Dawn), map.StartLocations.Count(s => s.Team == Team.Dusk));
                if (starts == 0) return Api.BadRequest("invalid_map", "That map has no RTS start locations.", "mapId");
                teamSize = Math.Min(teamSize, starts);
            }
            var info = await PlayerInfo(me);
            Lobby l;
            lock (_lock)
            {
                if (_memberOf.ContainsKey(me)) return Api.Conflict("already_in_lobby", "Leave your current lobby first.");
                l = new Lobby(teamSize)
                {
                    Name = name, PasswordHash = HashPassword(r.Password), HostId = me, Region = r.Region ?? _presence.Region(me), MapId = r.MapId ?? mode.Map,
                    ModeId = mode.Id, ModeName = mode.Name, TeamSize = teamSize, MaxSpectators = Math.Clamp(r.MaxSpectators, 0, 10), IsPrivate = r.IsPrivate,
                    PickMode = Enum.TryParse<HeroPickMode>(r.PickMode, true, out var pm) ? pm.ToString() : "AllPick",
                    BotDifficulty = Enum.TryParse<BotDifficulty>(r.BotDifficulty, true, out var bd) ? bd.ToString() : "Normal",
                    FillWithBots = r.FillWithBots, GameSpeed = Math.Clamp(r.GameSpeed, 0.5f, 2f), Modifiers = (r.Modifiers ?? new List<string>()).Take(8).Select(m => Validation.CleanText(m, 24)).ToList(),
                    MinRating = Math.Max(0, r.MinRating), MaxRating = r.MaxRating <= 0 ? 0 : r.MaxRating,
                };
                var s = l.Dawn[0];
                s.AccountId = me; s.Name = info.name; s.Level = info.level; s.Rank = info.rank; s.Rating = info.rating; s.Ready = true;
                _lobbies[l.Id] = l;
                _memberOf[me] = l.Id;
            }
            _chat.Join(me, "lobby:" + l.Id);
            _presence.Set(me, PresenceStatus.InLobby, $"In lobby: {l.Name}");
            Broadcast(l);
            return Api.Ok(View(l, me));
        }

        public async Task<IResult> Join(Guid me, string id, JoinLobbyRequest r)
        {
            var info = await PlayerInfo(me);
            Lobby l;
            lock (_lock)
            {
                if (!_lobbies.TryGetValue(id ?? "", out l)) return Api.NotFound("That game no longer exists.");
                if (_memberOf.TryGetValue(me, out var cur) && cur != id) return Api.Conflict("already_in_lobby", "Leave your current lobby first.");
                if (l.Everyone().Contains(me)) return Api.Ok(View(l, me));
                if (l.Status != "Waiting" && !(r?.Spectate ?? false)) return Api.Conflict("in_progress", "That game has already started.");
                if (l.PasswordHash != null)
                {
                    var h = HashPassword(r?.Password);
                    if (h == null || !CryptographicOperations.FixedTimeEquals(h, l.PasswordHash)) return Api.Error(403, "wrong_password", "Incorrect game password.");
                }
                if (l.MinRating > 0 && info.rating < l.MinRating) return Api.Forbidden($"This game requires a rating of at least {l.MinRating}.");
                if (l.MaxRating > 0 && info.rating > l.MaxRating) return Api.Forbidden($"This game is limited to ratings up to {l.MaxRating}.");
                if (r?.Spectate ?? false)
                {
                    if (l.Spectators.Count >= l.MaxSpectators) return Api.Conflict("spectators_full", "No spectator slots available.");
                    l.Spectators.Add(me);
                }
                else
                {
                    // Balance: join the team with fewer humans.
                    int dawn = l.Dawn.Count(s => s.AccountId != null || s.IsBot), dusk = l.Dusk.Count(s => s.AccountId != null || s.IsBot);
                    var team = dawn <= dusk ? l.Dawn : l.Dusk;
                    var free = team.FirstOrDefault(s => s.AccountId == null && !s.IsBot) ?? (team == l.Dawn ? l.Dusk : l.Dawn).FirstOrDefault(s => s.AccountId == null && !s.IsBot);
                    if (free == null) return Api.Conflict("full", "That game is full.");
                    free.AccountId = me; free.Name = info.name; free.Level = info.level; free.Rank = info.rank; free.Rating = info.rating; free.Ready = false;
                }
                _memberOf[me] = l.Id;
                l.LastActivity = DateTime.UtcNow;
            }
            _chat.Join(me, "lobby:" + l.Id);
            _chat.System("lobby:" + l.Id, $"{_presence.Name(me)} joined the game.");
            _presence.Set(me, (r?.Spectate ?? false) && l.Status != "Waiting" ? PresenceStatus.InGame : PresenceStatus.InLobby, $"In lobby: {l.Name}");
            Broadcast(l);
            if (l.Status != "Waiting" && (r?.Spectate ?? false) && l.Server != null)
                _hub.Send(me, RealtimeTypes.LobbyStarted, Connection(l, me, spectator: true));
            return Api.Ok(View(l, me));
        }

        public IResult Leave(Guid me)
        {
            Lobby l;
            lock (_lock)
            {
                if (!_memberOf.TryGetValue(me, out var id) || !_lobbies.TryGetValue(id, out l)) { _memberOf.Remove(me); return Results.NoContent(); }
                RemoveMember(l, me);
            }
            _chat.Leave(me, "lobby:" + l.Id);
            _presence.Set(me, PresenceStatus.Online, "");
            if (_lobbies.ContainsKey(l.Id)) { _chat.System("lobby:" + l.Id, $"{_presence.Name(me)} left the game."); Broadcast(l); }
            else _hub.Send(l.Everyone(), RealtimeTypes.LobbyClosed, new { lobbyId = l.Id });
            return Results.NoContent();
        }

        private void RemoveMember(Lobby l, Guid me)
        {
            _memberOf.Remove(me);
            l.Spectators.Remove(me);
            var s = l.SlotOf(me);
            if (s != null) { s.AccountId = null; s.Name = null; s.Ready = false; }
            if (!l.Humans().Any() && l.Status == "Waiting") { _lobbies.Remove(l.Id); return; }
            if (l.HostId == me && l.Humans().Any()) l.HostId = l.Humans().First();
        }

        public IResult MoveSlot(Guid me, LobbySlotRequest r)
        {
            Lobby l;
            lock (_lock)
            {
                l = LobbyOf(me);
                if (l == null || l.Status != "Waiting") return Api.NotFound("You are not in a waiting lobby.");
                var team = r?.Team == "Dusk" ? l.Dusk : r?.Team == "Dawn" ? l.Dawn : null;
                if (team == null || r.Slot < 0 || r.Slot >= team.Length) return Api.BadRequest("invalid_slot", "Invalid slot.");
                var target = team[r.Slot];
                if (target.AccountId != null || target.IsBot) return Api.Conflict("slot_taken", "That slot is taken.");
                var cur = l.SlotOf(me);
                if (cur == null) { l.Spectators.Remove(me); cur = new LobbySlot { AccountId = me, Name = _presence.Name(me) }; }
                target.AccountId = me; target.Name = cur.Name; target.Level = cur.Level; target.Rank = cur.Rank; target.Rating = cur.Rating; target.Ready = false;
                if (cur != target) { cur.AccountId = null; cur.Name = null; cur.Ready = false; }
            }
            Broadcast(l);
            return Results.NoContent();
        }

        /// <summary>RTS lobbies: a player picks their faction (or "random").</summary>
        public IResult SetFaction(Guid me, LobbyFactionRequest r)
        {
            Lobby l;
            lock (_lock)
            {
                l = LobbyOf(me);
                var s = l?.SlotOf(me);
                if (s == null || l.Status != "Waiting") return Api.NotFound("You are not in a waiting lobby slot.");
                if (!_data.Modes.TryGetValue(l.ModeId, out var mode) || mode.Kind != GameModeKind.Rts) return Api.BadRequest("not_rts", "Factions are only chosen in Strategy games.");
                if (!ValidFaction(r?.Faction, out var faction)) return Api.BadRequest("invalid_faction", "Unknown faction.", "faction");
                s.RtsFaction = faction;
                s.Ready = s.AccountId == l.HostId && s.Ready;
            }
            Broadcast(l);
            return Results.NoContent();
        }

        /// <summary>null / "random" means random; otherwise a playable RTS faction id.</summary>
        private bool ValidFaction(string id, out string faction)
        {
            faction = null;
            if (string.IsNullOrEmpty(id) || id == "random") return true;
            if (!_data.RtsFactions.TryGetValue(id, out var f) || !f.Playable) return false;
            faction = f.Id;
            return true;
        }

        public IResult SetReady(Guid me, bool ready)
        {
            Lobby l;
            lock (_lock)
            {
                l = LobbyOf(me);
                var s = l?.SlotOf(me);
                if (s == null) return Api.NotFound("You are not in a lobby slot.");
                s.Ready = ready;
            }
            Broadcast(l);
            return Results.NoContent();
        }

        public IResult Kick(Guid me, Guid target)
        {
            Lobby l;
            lock (_lock)
            {
                l = LobbyOf(me);
                if (l == null || l.HostId != me) return Api.Forbidden("Only the host can kick players.");
                if (target == me || !l.Everyone().Contains(target)) return Api.NotFound("Player not in lobby.");
                RemoveMember(l, target);
            }
            _chat.Leave(target, "lobby:" + l.Id);
            _hub.Send(target, RealtimeTypes.LobbyClosed, new { lobbyId = l.Id, kicked = true });
            _presence.Set(target, PresenceStatus.Online, "");
            Broadcast(l);
            return Results.NoContent();
        }

        public IResult Bots(Guid me, LobbyBotRequest r)
        {
            Lobby l;
            lock (_lock)
            {
                l = LobbyOf(me);
                if (l == null || l.HostId != me || l.Status != "Waiting") return Api.Forbidden("Only the host can manage bots.");
                var team = r?.Team == "Dusk" ? l.Dusk : l.Dawn;
                if (r == null || r.Slot < 0 || r.Slot >= team.Length) return Api.BadRequest("invalid_slot", "Invalid slot.");
                var s = team[r.Slot];
                if (r.Remove) { if (s.IsBot) { s.IsBot = false; s.Name = null; } }
                else
                {
                    if (s.AccountId != null) return Api.Conflict("slot_taken", "That slot is occupied by a player.");
                    if (!ValidFaction(r.Faction, out var faction)) return Api.BadRequest("invalid_faction", "Unknown faction.", "faction");
                    s.IsBot = true;
                    s.BotDifficulty = Enum.TryParse<BotDifficulty>(r.Difficulty, true, out var d) ? d.ToString() : l.BotDifficulty;
                    s.RtsFaction = faction;
                    s.Ready = true;
                }
            }
            Broadcast(l);
            return Results.NoContent();
        }

        public IResult Start(Guid me)
        {
            Lobby l;
            lock (_lock)
            {
                l = LobbyOf(me);
                if (l == null || l.HostId != me) return Api.Forbidden("Only the host can start the game.");
                if (l.Status != "Waiting") return Api.Conflict("already_started", "The game is already starting.");
                var notReady = l.AllSlots().Where(x => x.s.AccountId != null && !x.s.Ready && x.s.AccountId != l.HostId).Select(x => x.s.Name).ToList();
                if (notReady.Count > 0) return Api.Conflict("not_ready", "Waiting for players to ready up: " + string.Join(", ", notReady));
                if (l.FillWithBots)
                    foreach (var (_, _, s) in l.AllSlots())
                        if (s.AccountId == null && !s.IsBot) { s.IsBot = true; s.BotDifficulty = l.BotDifficulty; s.Ready = true; }
                bool dawnAny = l.Dawn.Any(s => s.AccountId != null || s.IsBot), duskAny = l.Dusk.Any(s => s.AccountId != null || s.IsBot);
                if (!dawnAny || !duskAny) return Api.Conflict("teams_empty", "Both teams need at least one player or bot.");
            }
            return BeginMatch(l, new[] { l.Region });
        }

        /// <summary>Allocates a server and sends every participant their connection info + ticket.</summary>
        public IResult BeginMatch(Lobby l, IEnumerable<string> regions)
        {
            var assignment = new ServerAssignment
            {
                MatchId = Guid.NewGuid().ToString("N"), ModeId = l.ModeId, MapId = l.MapId, Seed = (ulong)RandomNumberGenerator.GetInt32(1, int.MaxValue),
                Ranked = l.Ranked, PickMode = l.PickMode, GameSpeed = l.GameSpeed, LobbyId = l.Id,
            };
            foreach (var (team, slot, s) in l.AllSlots())
            {
                if (s.AccountId == null && !s.IsBot) continue;
                assignment.Players.Add(new AssignedPlayer
                {
                    AccountId = s.AccountId?.ToString(), DisplayName = s.IsBot ? null : s.Name, Team = team, Slot = slot, IsBot = s.IsBot, BotDifficulty = s.BotDifficulty,
                    RtsFaction = s.RtsFaction,
                });
            }
            var server = _directory.Allocate(regions, assignment, _data.ContentHash, out var region);
            if (server == null)
            {
                var msg = $"No game servers are available in {DirectoryService.RegionNames.GetValueOrDefault(l.Region, l.Region)} right now. Please try again shortly.";
                _chat.System("lobby:" + l.Id, msg);
                return Api.Error(503, "no_servers", msg);
            }
            lock (_lock)
            {
                l.Status = "InProgress";
                l.MatchId = assignment.MatchId;
                l.Server = server;
                l.Region = region;
            }
            _log.LogInformation("Lobby {Lobby} starting match {Match} on {Server}", l.Id, l.MatchId, server.ServerId);
            foreach (var id in l.Humans()) { _hub.Send(id, RealtimeTypes.LobbyStarted, Connection(l, id, false)); _presence.Set(id, PresenceStatus.InGame, $"{l.ModeName} - {l.Name}"); }
            foreach (var id in l.Spectators) _hub.Send(id, RealtimeTypes.LobbyStarted, Connection(l, id, true));
            Broadcast(l);
            return Api.Ok(View(l, l.HostId));
        }

        public GameConnectionInfo Connection(Lobby l, Guid id, bool spectator)
        {
            var slot = l.AllSlots().FirstOrDefault(x => x.s.AccountId == id);
            return new GameConnectionInfo
            {
                MatchId = l.MatchId, Address = l.Server.Address, Port = l.Server.Port, Region = l.Region, ModeId = l.ModeId, MapId = l.MapId, Spectator = spectator,
                Ticket = _tickets.Issue(l.MatchId, id, slot.s?.Name ?? _presence.Name(id), slot.team ?? "Spectator", slot.slot, spectator),
            };
        }

        /// <summary>Fresh ticket for (re)connecting to the lobby's running match.</summary>
        public IResult ConnectionFor(Guid me)
        {
            Lobby l;
            lock (_lock) l = LobbyOf(me);
            if (l == null || l.Server == null || l.Status != "InProgress") return Api.NotFound("You have no match in progress.");
            return Api.Ok(Connection(l, me, l.Spectators.Contains(me)));
        }

        /// <summary>Matchmaking creates lobbies through this path (not listed in the browser).</summary>
        public Lobby CreateMatchmade(string queue, GameModeDef mode, List<(Guid id, string team, int slot)> players, int fillBots, string region, IReadOnlyDictionary<Guid, string> factions = null)
        {
            var l = new Lobby(mode.TeamSize)
            {
                Name = mode.Name, HostId = players[0].id, Region = region, MapId = mode.Map, ModeId = mode.Id, ModeName = mode.Name, TeamSize = mode.TeamSize,
                Ranked = mode.Ranked, PickMode = mode.PickMode.ToString(), BotDifficulty = "Normal", FromMatchmaking = true, Queue = queue, MaxSpectators = 0,
            };
            foreach (var p in players)
            {
                var team = p.team == "Dusk" ? l.Dusk : l.Dawn;
                team[p.slot].AccountId = p.id;
                team[p.slot].Name = _presence.Name(p.id);
                team[p.slot].Ready = true;
                if (factions != null && factions.TryGetValue(p.id, out var f)) team[p.slot].RtsFaction = f;
            }
            if (fillBots > 0)
                foreach (var (_, _, s) in l.AllSlots())
                    if (s.AccountId == null) { s.IsBot = true; s.BotDifficulty = "Normal"; s.Ready = true; }
            lock (_lock)
            {
                _lobbies[l.Id] = l;
                foreach (var p in players) _memberOf[p.id] = l.Id;
            }
            return l;
        }

        private Lobby LobbyOf(Guid me) => _memberOf.TryGetValue(me, out var id) && _lobbies.TryGetValue(id, out var l) ? l : null;

        private void OnMatchFinished(string matchId)
        {
            Lobby l;
            lock (_lock)
            {
                l = _lobbies.Values.FirstOrDefault(x => x.MatchId == matchId);
                if (l == null) return;
                l.Status = "Finished";
                _lobbies.Remove(l.Id);
                foreach (var id in l.Everyone()) _memberOf.Remove(id);
            }
            foreach (var id in l.Everyone()) { _presence.Set(id, PresenceStatus.Online, ""); _chat.Leave(id, "lobby:" + l.Id); }
            _hub.Send(l.Everyone(), RealtimeTypes.LobbyClosed, new { lobbyId = l.Id, matchId, finished = true });
        }

        private void OnServerLost(string matchId)
        {
            Lobby l;
            lock (_lock) l = _lobbies.Values.FirstOrDefault(x => x.MatchId == matchId);
            if (l == null) return;
            _hub.Send(l.Everyone(), RealtimeTypes.Notice, new NoticePayload { Level = "error", Title = "Game server lost", Message = "The game server hosting your match stopped responding. The match could not be completed." });
            OnMatchFinished(matchId);
        }

        private void Broadcast(Lobby l)
        {
            foreach (var id in l.Everyone()) _hub.Send(id, RealtimeTypes.LobbyUpdate, View(l, id));
        }

        /// <summary>Removes idle waiting lobbies after 30 minutes without activity.</summary>
        public void Cleanup()
        {
            List<Lobby> stale;
            lock (_lock) stale = _lobbies.Values.Where(l => l.Status == "Waiting" && (DateTime.UtcNow - l.LastActivity).TotalMinutes > 30).ToList();
            foreach (var l in stale) foreach (var id in l.Everyone().ToList()) Leave(id);
        }
    }

    public static class LobbyEndpoints
    {
        public static void MapLobbies(this IEndpointRouteBuilder app)
        {
            var g = app.MapGroup("/api/lobbies").RequireAuthorization();
            g.MapGet("/", (LobbyService s, HttpContext c) => Api.Ok(s.List(c.User.AccountId())));
            g.MapGet("/mine", (LobbyService s, HttpContext c) => Api.Ok(s.Mine(c.User.AccountId())));
            g.MapGet("/mine/connection", (LobbyService s, HttpContext c) => s.ConnectionFor(c.User.AccountId()));
            g.MapGet("/{id}", (string id, LobbyService s, HttpContext c) => s.Get(id, c.User.AccountId()) is LobbyView v ? Api.Ok(v) : Api.NotFound("That game no longer exists."));
            g.MapPost("/", (CreateLobbyRequest r, LobbyService s, HttpContext c) => s.Create(c.User.AccountId(), r));
            g.MapPost("/{id}/join", (string id, JoinLobbyRequest r, LobbyService s, HttpContext c) => s.Join(c.User.AccountId(), id, r));
            g.MapPost("/leave", (LobbyService s, HttpContext c) => s.Leave(c.User.AccountId()));
            g.MapPost("/slot", (LobbySlotRequest r, LobbyService s, HttpContext c) => s.MoveSlot(c.User.AccountId(), r));
            g.MapPost("/ready", (LobbyReadyRequest r, LobbyService s, HttpContext c) => s.SetReady(c.User.AccountId(), r?.Ready ?? true));
            g.MapPost("/faction", (LobbyFactionRequest r, LobbyService s, HttpContext c) => s.SetFaction(c.User.AccountId(), r));
            g.MapPost("/kick", (LobbyKickRequest r, LobbyService s, HttpContext c) => Guid.TryParse(r?.AccountId, out var t) ? s.Kick(c.User.AccountId(), t) : Api.BadRequest("invalid", "Invalid account."));
            g.MapPost("/bots", (LobbyBotRequest r, LobbyService s, HttpContext c) => s.Bots(c.User.AccountId(), r));
            g.MapPost("/start", (LobbyService s, HttpContext c) => s.Start(c.User.AccountId()));
        }
    }
}
