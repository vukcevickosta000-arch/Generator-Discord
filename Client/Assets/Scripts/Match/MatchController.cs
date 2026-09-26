using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bloodfall.Client.Core;
using Bloodfall.Client.Networking;
using Bloodfall.Client.UI;
using Bloodfall.Client.UI.Screens;
using Bloodfall.Contracts;
using Bloodfall.Data;
using Bloodfall.Protocol;
using Bloodfall.Simulation;
using UnityEngine;

namespace Bloodfall.Client.Match
{
    /// <summary>
    /// Owns one match from connection to post-game: the <see cref="GameClient"/> (UDP to a dedicated server, or a
    /// loopback to an in-process <see cref="MatchHost"/> for offline practice), the 3D <see cref="MatchWorld"/>, and
    /// the match screens (hero select, loading, HUD). The client never simulates authoritative state online; offline,
    /// the same server code runs locally.
    /// </summary>
    public sealed class MatchController : IDisposable
    {
        public readonly GameApp App;
        public GameClient Client { get; private set; }
        public readonly MatchHost OfflineHost;
        public readonly bool Online;
        public GameConnectionInfo Connection { get; private set; }
        public MatchWorld World { get; private set; }
        public MatchPhase Phase { get; private set; } = MatchPhase.WaitingForPlayers;
        public GameData Data => App.Data;
        public bool Paused { get; set; }
        public bool Ended { get; private set; }
        public string LocalAccountId { get; }

        private IEnumerator<float> _loader;
        private float _loadProgress;
        private float _sentProgress = -1f;
        private bool _worldReady;
        private float _connectTimer;
        private int _reconnectAttempts;
        private bool _reconnecting;
        private float _endTimer = -1f;
        private MatchResult _result;
        private string _closeError;
        private bool _closed;

        public event Action<NetEvent> EventReceived;
        public event Action<ChatMessage> ChatReceived;
        public event Action PhaseChanged;

        private MatchController(GameApp app, bool online, MatchHost offlineHost, string localAccountId)
        {
            App = app;
            Online = online;
            OfflineHost = offlineHost;
            LocalAccountId = localAccountId;
        }

        // ------------------------------------------------------------------ creation

        public static MatchController StartOnline(GameApp app, GameConnectionInfo info)
        {
            var mc = new MatchController(app, true, null, app.Backend.MyId) { Connection = info };
            mc.ConnectOnline(info);
            app.UI.Show<ConnectingToMatchScreen>().SetText("Connecting to game server", $"{info.Region} · {info.Address}:{info.Port}");
            return mc;
        }

        public static MatchController StartOffline(GameApp app, OfflineMatchOptions o)
        {
            bool rts = app.Data.Modes.TryGetValue(o.ModeId ?? "", out var mode) && mode.Kind == GameModeKind.Rts;
            var cfg = new MatchConfig
            {
                ModeId = o.ModeId,
                MapId = rts ? mode.Map : "map_velmoragh",
                Seed = o.Seed != 0 ? o.Seed : (ulong)DateTime.UtcNow.Ticks,
                AllowCheats = o.Cheats,
                DisableVharoth = o.DisableVharoth,
                PreGameTimeOverride = rts ? app.Data.Rules.RtsPreGameTime : 30f,
            };
            string name = string.IsNullOrWhiteSpace(o.PlayerName) ? "Player" : o.PlayerName.Trim();
            if (rts)
            {
                // War of the Ancients practice: the player against one RTS AI.
                cfg.Players.Add(new PlayerSetup { AccountId = OfflineSession.LocalAccountId, Name = name, Team = Team.Dawn, Slot = 0, RtsFaction = o.RtsFaction });
                cfg.Players.Add(new PlayerSetup { Name = "Warlord (AI)", Team = Team.Dusk, Slot = 0, IsBot = true, BotDifficulty = o.EnemyDifficulty, RtsFaction = o.EnemyRtsFaction });
                return StartHost(app, cfg, name);
            }
            cfg.Players.Add(new PlayerSetup { AccountId = OfflineSession.LocalAccountId, Name = name, Team = Team.Dawn, Slot = 0 });
            string[] botNames = { "Grimwald", "Seraphine", "Oskar the Pale", "Maelis", "Thorne", "Vex", "Corvin", "Ysolde", "Harrow", "Lucan" };
            int n = 0;
            if (o.FillAllies)
                for (int i = 1; i < o.TeamSize; i++)
                    cfg.Players.Add(new PlayerSetup { Name = botNames[n++ % botNames.Length] + " (Bot)", Team = Team.Dawn, Slot = i, IsBot = true, BotDifficulty = o.AllyDifficulty });
            if (o.FillEnemies)
                for (int i = 0; i < o.TeamSize; i++)
                    cfg.Players.Add(new PlayerSetup { Name = botNames[n++ % botNames.Length] + " (Bot)", Team = Team.Dusk, Slot = i, IsBot = true, BotDifficulty = o.EnemyDifficulty });
            return StartHost(app, cfg, name);
        }

        private static MatchController StartHost(GameApp app, MatchConfig cfg, string name)
        {
            var host = OfflineSession.CreateHost(app.Data, cfg);
            host.ConcedeMinTime = 0f;
            host.Log += msg => Debug.Log("[OfflineHost] " + msg);
            var mc = new MatchController(app, false, host, OfflineSession.LocalAccountId);
            var link = new LoopbackConnection(host);
            mc.AttachClient(new GameClient(app.Data, link, new HelloInfo { ClientVersion = app.Config.ClientVersion, Ticket = MatchTickets.OfflinePrefix + name }));
            mc.Client.Connect();
            return mc;
        }

        private void ConnectOnline(GameConnectionInfo info)
        {
            Connection = info;
            var transport = new LiteNetClientTransport(info.Address, info.Port);
            AttachClient(new GameClient(App.Data, transport, new HelloInfo { ClientVersion = App.Config.ClientVersion, Ticket = info.Ticket, Spectator = info.Spectator }));
            _connectTimer = 0f;
            Client.Connect();
        }

        private void AttachClient(GameClient c)
        {
            Client = c;
            c.OnWelcome += w =>
            {
                _reconnectAttempts = 0;
                _reconnecting = false;
                if (w.Reconnected) App.UI.Toast("Reconnected to the match.", ToastKind.Success);
            };
            c.OnMatchState += OnMatchState;
            c.OnRejected += reason => Close(null, reason);
            c.OnDisconnected += OnDisconnected;
            c.OnChat += m => ChatReceived?.Invoke(m);
            c.OnMatchEnd += r =>
            {
                _result = r;
                Ended = true;
                _endTimer = 5.5f;
                World?.OnMatchEnded(r);
            };
        }

        // ------------------------------------------------------------------ phases

        private void OnMatchState(MatchStateInfo s)
        {
            var before = Phase;
            Phase = s.Phase;
            switch (s.Phase)
            {
                case MatchPhase.WaitingForPlayers:
                case MatchPhase.HeroSelect:
                    if (!(App.UI.Current is HeroSelectScreen)) App.UI.Show<HeroSelectScreen>().Bind(this);
                    App.Audio.PlayMusic("hero_select");
                    break;
                case MatchPhase.Loading:
                case MatchPhase.PreGame:
                case MatchPhase.Playing:
                    BeginLoading();
                    break;
            }
            if (before != Phase) PhaseChanged?.Invoke();
        }

        private void BeginLoading()
        {
            if (World != null) { if (_worldReady) ShowHud(); return; }
            var loading = App.UI.Show<LoadingScreen>();
            loading.Bind(this);
            App.Audio.PlayMusic("loading");
            World = new MatchWorld(this);
            _loader = World.Load();
            _loadProgress = 0f;
        }

        /// <summary>True for War of the Ancients (RTS) matches.</summary>
        public bool IsRts => World != null ? World.IsRts : Data.Modes.TryGetValue(Client?.Welcome?.ModeId ?? "", out var m) && m.Kind == GameModeKind.Rts;

        private void ShowHud()
        {
            if (IsRts)
            {
                if (App.UI.Current is RtsHudScreen) return;
                App.UI.Show<RtsHudScreen>().Bind(this);
            }
            else
            {
                if (App.UI.Current is HudScreen) return;
                App.UI.Show<HudScreen>().Bind(this);
            }
            App.Audio.PlayMusic(null);
            App.Audio.PlayAmbience("velmoragh_night");
        }

        private void OnDisconnected(string reason)
        {
            if (_closed || Ended) return;
            if (!Online) { Close(null, reason); return; }
            // Online: the server keeps our hero for the reconnect grace period; try to rejoin with a fresh ticket.
            if (_reconnectAttempts >= 5) { Close(null, "Lost connection to the game server: " + reason + "\nYou can rejoin from the main client while the match is running."); return; }
            _reconnectAttempts++;
            _reconnecting = true;
            App.UI.Toast($"Connection lost ({reason}). Reconnecting… ({_reconnectAttempts}/5)", ToastKind.Error, 4f);
            _ = Reconnect();
        }

        private async Task Reconnect()
        {
            await Task.Delay(1500 * _reconnectAttempts);
            if (_closed) return;
            var conn = await App.Backend.CurrentMatchConnection();
            if (_closed) return;
            if (conn.Ok && conn.Value != null && !string.IsNullOrEmpty(conn.Value.Ticket)) ConnectOnline(conn.Value);
            else if (Connection != null) ConnectOnline(Connection);
        }

        // ------------------------------------------------------------------ loop

        public void Tick(float dt)
        {
            if (_closed) return;
            if (OfflineHost != null && !Paused) OfflineHost.Update(dt);
            Client.Update(dt);

            if (Online && Client.State == ClientConnectionState.Connecting)
            {
                _connectTimer += dt;
                if (_connectTimer > 15f && !_reconnecting) { Close(null, "Could not connect to the game server (timed out)."); return; }
            }

            if (_loader != null)
            {
                // Spread world construction over frames (~12 ms budget) so the loading screen stays responsive.
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 12)
                {
                    if (!_loader.MoveNext()) { _loader = null; _loadProgress = 1f; _worldReady = true; break; }
                    _loadProgress = _loader.Current;
                }
                if (Math.Abs(_loadProgress - _sentProgress) >= 0.05f || (_loadProgress >= 1f && _sentProgress < 1f))
                {
                    _sentProgress = _loadProgress;
                    Client.SendLoadProgress(_loadProgress);
                }
            }
            if (_worldReady && (Phase == MatchPhase.PreGame || Phase == MatchPhase.Playing || Phase == MatchPhase.PostGame)) ShowHud();

            while (Client.PendingEvents.Count > 0)
            {
                var e = Client.PendingEvents.Dequeue();
                if (_worldReady)
                {
                    try { World.HandleEvent(e); }
                    catch (Exception ex) { Faults.Report("world-event " + e.Type, ex); }
                }
                try { EventReceived?.Invoke(e); } catch (Exception ex) { Faults.Report("hud-event " + e.Type, ex); }
            }
            if (_worldReady) World.Tick(dt);

            if (_endTimer > 0f)
            {
                _endTimer -= dt;
                if (_endTimer <= 0f) Close(_result, null);
            }
        }

        public void LateTick(float dt)
        {
            if (_closed || !_worldReady) return;
            World.LateTick(dt);
        }

        public float LoadProgress => _loadProgress;
        public bool WorldReady => _worldReady;

        // ------------------------------------------------------------------ commands

        public void SendOrder(Order o) => Client.SendOrder(o);
        public void SendChat(string text, bool team) => Client.SendChat(text, team);

        /// <summary>Leave the match (online this abandons if the match is still running).</summary>
        public void Leave()
        {
            Client.Leave();
            Close(Ended ? _result : null, null);
        }

        public void Close(MatchResult result, string error)
        {
            if (_closed) return;
            _closed = true;
            _closeError = error;
            try { Client.Disconnect(); } catch { }
            Dispose();
            App.UI.Discard<HudScreen>();
            App.UI.Discard<RtsHudScreen>();
            App.UI.Discard<HeroSelectScreen>();
            App.UI.Discard<LoadingScreen>();
            App.UI.CloseAllOverlays();
            App.Audio.PlayAmbience(null);
            App.Flow.OnMatchClosed(result, Online ? App.Backend.MyId : OfflineSession.LocalAccountId, Online, _closeError);
        }

        public void Dispose()
        {
            World?.Dispose();
            World = null;
            _worldReady = false;
        }

        // ------------------------------------------------------------------ helpers for UI

        public PlayerView LocalPlayer => Client.Latest?.Players.FirstOrDefault(p => p.Id == Client.LocalPlayerId)
                                          ?? Client.MatchState?.Players.FirstOrDefault(p => p.Id == Client.LocalPlayerId);

        public IEnumerable<PlayerView> Players => Client.Latest?.Players ?? Client.MatchState?.Players ?? Enumerable.Empty<PlayerView>();

        public EntityState LocalHero
        {
            get
            {
                var lp = LocalPlayer;
                if (lp == null || Client.Latest == null) return null;
                foreach (var e in Client.Latest.Entities) if (e.Id == lp.HeroUnitId) return e;
                return null;
            }
        }
    }
}
