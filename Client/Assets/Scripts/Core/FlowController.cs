using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bloodfall.Client.Backend;
using Bloodfall.Client.Match;
using Bloodfall.Client.UI;
using Bloodfall.Client.UI.Screens;
using Bloodfall.Contracts;
using Bloodfall.Protocol;
using UnityEngine;

namespace Bloodfall.Client.Core
{
    public enum FlowState { Boot, Splash, Connecting, Login, Register, Client, Match, PostGame }

    public enum CheckStatus { Pending, Running, Ok, Warning, Failed, Skipped }

    /// <summary>One line of the "Connecting to services" screen. Every check performs a real request.</summary>
    public sealed class ServiceCheck
    {
        public string Id;
        public string Label;
        public CheckStatus Status;
        public string Detail;
    }

    /// <summary>
    /// Top-level client state machine: splash → service checks → login/register → main client → lobby/queue →
    /// match (hero select, loading, game) → post-game → back to the client. Offline practice against bots is a
    /// separate, clearly labelled path that never touches online services.
    /// </summary>
    public sealed class FlowController : ITickable
    {
        private readonly GameApp _app;
        public FlowState State { get; private set; } = FlowState.Boot;
        /// <summary>True when the player chose to continue without online services.</summary>
        public bool OfflineMode { get; private set; }
        public readonly List<ServiceCheck> Checks = new List<ServiceCheck>();
        public event Action ChecksChanged;
        public string ContentWarning { get; private set; }
        private bool _checking;
        private float _statusRefresh;
        private float _awayTimer;
        private Vector2 _lastMouse;
        private bool _away;

        public FlowController(GameApp app)
        {
            _app = app;
        }

        private BackendClient Backend => _app.Backend;
        private UIManager UI => _app.UI;

        public void Start()
        {
            Backend.SessionExpired += OnSessionExpired;
            Backend.MatchReady += OnMatchReady;
            Backend.MatchFound += OnMatchFound;
            Backend.MatchCancelled += reason => { UI.Toast(reason, ToastKind.Info); MatchFoundDialog.Close(); };
            Backend.Notice += n => UI.Toast((string.IsNullOrEmpty(n.Title) ? "" : n.Title + ": ") + n.Message, n.Level == "error" ? ToastKind.Error : ToastKind.Info, 8f);
            Backend.PartyInviteReceived += inv => SocialPrompts.PartyInvite(inv);
            Backend.FriendRequestReceived += req => UI.Toast($"{req.FromName} sent you a friend request.", ToastKind.Info);
            Backend.ClanInviteReceived += inv => SocialPrompts.ClanInvite(inv);
            Backend.RealtimeStatusChanged += connected =>
            {
                if (State == FlowState.Client && !connected) UI.Toast("Lost connection to chat services. Reconnecting…", ToastKind.Error);
            };
            GoSplash();
        }

        // ------------------------------------------------------------------ states

        public void GoSplash()
        {
            State = FlowState.Splash;
            _app.Backdrop.SetActive(true);
            _app.Audio.PlayMusic("menu_theme");
            _app.Audio.PlayAmbience("menu_wind");
            UI.Show<SplashScreen>();
        }

        public void GoConnecting()
        {
            State = FlowState.Connecting;
            _app.Backdrop.SetActive(true);
            UI.Show<ConnectingScreen>();
            _ = RunServiceChecks();
        }

        public void GoLogin()
        {
            State = FlowState.Login;
            OfflineMode = false;
            _app.Backdrop.SetActive(true);
            UI.Show<LoginScreen>();
        }

        public void GoRegister()
        {
            State = FlowState.Register;
            UI.Show<RegisterScreen>();
        }

        /// <summary>Called after a successful login or registration.</summary>
        public async void OnAuthenticated()
        {
            var overlay = UI.Dialog("Entering Bloodfall", "Loading your account, friends and party…");
            try
            {
                await Backend.EnterClient();
                await Task.WhenAll(Backend.RefreshNews(), Backend.RefreshStatus());
                _ = Backend.RefreshRegions();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            UI.CloseOverlay(overlay);
            GoClient();
            // Rejoin an in-progress match (reconnect after a crash or disconnect).
            var conn = await Backend.CurrentMatchConnection();
            if (conn.Ok && conn.Value != null && !string.IsNullOrEmpty(conn.Value.Ticket))
            {
                UI.Dialog("Match in progress", "You are still part of a running match. Reconnect now? Leaving it for too long counts as an abandon.",
                    ("Reconnect", "btn--primary", () => JoinOnlineMatch(conn.Value)),
                    ("Later", "btn--ghost", null));
            }
        }

        public void GoClient()
        {
            State = FlowState.Client;
            _app.Backdrop.SetActive(true);
            _app.Audio.PlayMusic(OfflineMode ? "menu_theme" : "client_theme");
            UI.Show<MainClientScreen>();
        }

        /// <summary>Continue without services (offline practice only). Online features show why they are unavailable.</summary>
        public void GoOffline()
        {
            OfflineMode = true;
            Backend.Offline = true;
            GoClient();
        }

        public async void Logout()
        {
            await Backend.Logout();
            OfflineMode = false;
            Backend.Offline = false;
            UI.CloseAllOverlays();
            GoLogin();
        }

        private void OnSessionExpired()
        {
            if (State == FlowState.Match || OfflineMode) return;
            UI.CloseAllOverlays();
            UI.Error("Session expired", "Your session has ended. Please log in again.");
            OfflineMode = false;
            GoLogin();
        }

        // ------------------------------------------------------------------ service checks

        private ServiceCheck Check(string id) => Checks.First(c => c.Id == id);

        private void SetCheck(string id, CheckStatus s, string detail = null)
        {
            var c = Check(id);
            c.Status = s;
            c.Detail = detail;
            ChecksChanged?.Invoke();
        }

        /// <summary>
        /// Real start-up checks: local game data, backend reachability + version, service status, remembered
        /// session. Nothing here is simulated; failures are reported with the actual reason.
        /// </summary>
        public async Task RunServiceChecks()
        {
            if (_checking) return;
            _checking = true;
            Checks.Clear();
            Checks.Add(new ServiceCheck { Id = "data", Label = "Game data" });
            Checks.Add(new ServiceCheck { Id = "backend", Label = "Bloodfall services" });
            Checks.Add(new ServiceCheck { Id = "version", Label = "Client version" });
            Checks.Add(new ServiceCheck { Id = "status", Label = "Service status" });
            Checks.Add(new ServiceCheck { Id = "session", Label = "Saved session" });
            ChecksChanged?.Invoke();
            try
            {
                // 1. Local data.
                SetCheck("data", CheckStatus.Running);
                if (_app.Data == null || !string.IsNullOrEmpty(_app.DataError))
                {
                    SetCheck("data", CheckStatus.Failed, _app.DataError ?? "Game data could not be loaded.");
                    return;
                }
                SetCheck("data", CheckStatus.Ok, $"{_app.Data.Heroes.Count} heroes, {_app.Data.Items.Count} items · {_app.Data.ContentHash.Substring(0, 8)}");

                // 2+3. Backend reachable and version compatible.
                SetCheck("backend", CheckStatus.Running, _app.Config.BackendUrl);
                var version = await Backend.CheckVersion();
                if (!version.Ok)
                {
                    SetCheck("backend", CheckStatus.Failed, version.Error?.Message ?? "Could not reach " + _app.Config.BackendUrl);
                    SetCheck("version", CheckStatus.Skipped);
                    SetCheck("status", CheckStatus.Skipped);
                    SetCheck("session", CheckStatus.Skipped);
                    return;
                }
                SetCheck("backend", CheckStatus.Ok, _app.Config.BackendUrl);
                var v = version.Value;
                if (v.Maintenance)
                {
                    SetCheck("version", CheckStatus.Failed, "Maintenance: " + (v.MaintenanceMessage ?? "services are temporarily unavailable."));
                    return;
                }
                if (!string.IsNullOrEmpty(v.MinimumVersion) && CompareVersions(_app.Config.ClientVersion, v.MinimumVersion) < 0)
                {
                    SetCheck("version", CheckStatus.Failed, $"Version {_app.Config.ClientVersion} is too old (minimum {v.MinimumVersion}). Please update.");
                    return;
                }
                ContentWarning = null;
                if (!string.IsNullOrEmpty(v.ContentHash) && v.ContentHash != _app.Data.ContentHash)
                {
                    ContentWarning = "Your game data differs from the servers. Online matches will reject this client until it is updated.";
                    SetCheck("version", CheckStatus.Warning, ContentWarning);
                }
                else SetCheck("version", CheckStatus.Ok, $"{_app.Config.ClientVersion} (latest {v.LatestVersion})");

                // 4. Status.
                SetCheck("status", CheckStatus.Running);
                var status = await Backend.RefreshStatus();
                if (!status.Ok) SetCheck("status", CheckStatus.Warning, status.Error?.Message);
                else
                {
                    var bad = status.Value.Services.Where(s => s.Status != "Online").ToList();
                    if (bad.Count == 0) SetCheck("status", CheckStatus.Ok, $"{status.Value.PlayersOnline} online · {status.Value.MatchesInProgress} matches");
                    else SetCheck("status", CheckStatus.Warning, string.Join(" · ", bad.Select(b => $"{b.Service}: {b.Detail ?? b.Status}")));
                }

                // 5. Remembered session.
                SetCheck("session", CheckStatus.Running);
                bool resumed = await Backend.TryResumeSession();
                SetCheck("session", resumed ? CheckStatus.Ok : CheckStatus.Skipped, resumed ? "Welcome back, " + Backend.Session.Account?.DisplayName : "Not signed in");
                await Task.Delay(350);
                if (State != FlowState.Connecting) return;
                if (resumed) OnAuthenticated();
                else GoLogin();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                var running = Checks.FirstOrDefault(c => c.Status == CheckStatus.Running);
                if (running != null) SetCheck(running.Id, CheckStatus.Failed, e.Message);
            }
            finally
            {
                _checking = false;
                ChecksChanged?.Invoke();
            }
        }

        public bool ChecksFailed => Checks.Any(c => c.Status == CheckStatus.Failed);
        public bool Checking => _checking;

        public static int CompareVersions(string a, string b)
        {
            var pa = (a ?? "0").Split('.', '-');
            var pb = (b ?? "0").Split('.', '-');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int x = i < pa.Length && int.TryParse(pa[i], out var xi) ? xi : 0;
                int y = i < pb.Length && int.TryParse(pb[i], out var yi) ? yi : 0;
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }

        // ------------------------------------------------------------------ matches

        private void OnMatchFound(MatchFoundPayload m)
        {
            if (State != FlowState.Client) return;
            MatchFoundDialog.Open(m);
        }

        private void OnMatchReady(GameConnectionInfo info)
        {
            MatchFoundDialog.Close();
            if (State == FlowState.Match) return;
            JoinOnlineMatch(info);
        }

        public void JoinOnlineMatch(GameConnectionInfo info)
        {
            if (_app.Match != null) return;
            if (!string.IsNullOrEmpty(ContentWarning))
            {
                UI.Error("Update required", ContentWarning);
                return;
            }
            UI.CloseAllOverlays();
            State = FlowState.Match;
            _app.Backdrop.SetActive(false);
            _app.Match = MatchController.StartOnline(_app, info);
        }

        public void StartOfflineMatch(OfflineMatchOptions options)
        {
            if (_app.Match != null) return;
            UI.CloseAllOverlays();
            State = FlowState.Match;
            _app.Backdrop.SetActive(false);
            _app.Match = MatchController.StartOffline(_app, options);
        }

        /// <summary>Called by the match controller once the match has been torn down.</summary>
        public void OnMatchClosed(MatchResult result, string localAccountId, bool wasOnline, string error)
        {
            _app.Match = null;
            _app.Backdrop.SetActive(true);
            if (result != null)
            {
                State = FlowState.PostGame;
                var pg = UI.Show<PostGameScreen>();
                pg.Present(result, localAccountId, wasOnline);
                if (wasOnline) _ = RefreshAfterMatch();
                return;
            }
            GoClient();
            if (!string.IsNullOrEmpty(error)) UI.Error("Disconnected", error);
        }

        private async Task RefreshAfterMatch()
        {
            await Backend.RefreshProfile();
            await Backend.RefreshLobby();
        }

        // ------------------------------------------------------------------ tick

        public void Tick(float dt)
        {
            if (State == FlowState.Client && !OfflineMode)
            {
                _statusRefresh -= dt;
                if (_statusRefresh <= 0f)
                {
                    _statusRefresh = 30f;
                    _ = Backend.RefreshStatus();
                }
                // Automatic "Away" presence after 5 minutes without input.
                var mouse = Input.InputBridge.MousePosition;
                if ((mouse - _lastMouse).sqrMagnitude > 4f || UnityEngine.Input.anyKeyDown)
                {
                    _lastMouse = mouse;
                    _awayTimer = 0f;
                    if (_away) { _away = false; Backend.SetAway(false); }
                }
                else
                {
                    _awayTimer += dt;
                    if (!_away && _awayTimer > 300f) { _away = true; Backend.SetAway(true); }
                }
            }
        }
    }

    /// <summary>Options for an offline practice match hosted inside the client (no services involved).</summary>
    public sealed class OfflineMatchOptions
    {
        public string ModeId = "moba_practice";
        public int TeamSize = 5;
        public Bloodfall.Data.BotDifficulty AllyDifficulty = Bloodfall.Data.BotDifficulty.Normal;
        public Bloodfall.Data.BotDifficulty EnemyDifficulty = Bloodfall.Data.BotDifficulty.Normal;
        public string PlayerName = "Player";
        public bool FillAllies = true;
        public bool FillEnemies = true;
        public bool Cheats = true;
        public bool DisableVharoth;
        public ulong Seed;
        /// <summary>War of the Ancients practice (ModeId "rts_1v1"): factions, null = random.</summary>
        public string RtsFaction;
        public string EnemyRtsFaction;
    }
}
