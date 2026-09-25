using System;
using System.Collections.Generic;
using System.Linq;
using Bloodfall.Client.Core;
using Bloodfall.Contracts;
using Bloodfall.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Bloodfall.Client.UI.Screens
{
    /// <summary>
    /// PLAY: matchmaking queues, custom game browser (real lobbies from the lobby service), game creation, the
    /// lobby room and offline practice.
    /// </summary>
    public sealed class PlayPage : ClientPage
    {
        private VisualElement _left, _right;
        private VisualElement _queuePanel;
        private Label _queueTitle, _queueInfo;
        private VisualElement _regionList;
        private readonly HashSet<string> _selectedRegions = new HashSet<string>();
        private VisualElement _browser, _lobbyHost;
        private readonly ServerBrowser _serverBrowser = new ServerBrowser();
        private readonly LobbyPanel _lobbyPanel = new LobbyPanel();
        private bool _lobbyShown;

        protected override void OnBuild(VisualElement root)
        {
            var row = El.Div("row", "grow");
            row.style.alignItems = Align.Stretch;
            _left = El.Div("col").Width(360);
            _left.style.marginRight = 10;
            _right = El.Div("col", "grow");
            row.Add(_left);
            row.Add(_right);
            root.Add(row);

            _left.Add(El.Text("MATCHMAKING", "t-subheading"));
            _left.Add(QueueCard("quick", "Quick Match", "5v5 on Velmoragh. Empty slots are filled with bots after 25 seconds so you are never stuck waiting."));
            _left.Add(QueueCard("unranked", "Unranked 5v5", "Players only. Normal matchmaking by rating, no rating changes."));
            _left.Add(QueueCard("ranked", "Ranked 5v5", "Competitive matches that change your rank. Placement: first 10 matches."));
            var strategy = QueueCard("strategy", "War of the Ancients (RTS)", "Base building, harvesting and armies. In development - not yet playable.");
            strategy.SetEnabled(false);
            _left.Add(strategy);
            _left.Add(PracticeCard());

            _queuePanel = El.Div("card", "col", "card--highlight", "mt-m");
            _queueTitle = El.Text("", "t-heading");
            _queueInfo = El.Text("", "t-small", "t-wrap");
            _queuePanel.Add(_queueTitle);
            _queuePanel.Add(_queueInfo);
            _queuePanel.Add(El.Btn("Leave Queue", async () => await App.Backend.LeaveQueue(), "btn--danger", "btn--small", "mt-m"));
            _queuePanel.Show(false);
            _left.Add(_queuePanel);

            _left.Add(El.Text("REGIONS", "t-subheading", "mt-m"));
            _regionList = El.Div("col");
            _left.Add(_regionList);

            _browser = _serverBrowser.Build();
            _right.Add(_browser);
            _lobbyHost = _lobbyPanel.Build();
            _lobbyHost.Show(false);
            _right.Add(_lobbyHost);

            App.Backend.QueueChanged += () => { if (Root.panel != null) RefreshQueue(); };
            App.Backend.LobbyChanged += () => { if (Root.panel != null) RefreshLobbyState(); };
        }

        private VisualElement QueueCard(string queue, string title, string desc)
        {
            var c = El.Div("card", "col", "mb-m");
            var head = El.Div("row", "space-between");
            head.Add(El.Text(title, "t-heading"));
            var btn = El.Btn("Find Match", () => EnterQueue(queue), "btn--primary", "btn--small");
            head.Add(btn);
            c.Add(head);
            c.Add(El.Text(desc, "t-small", "t-wrap"));
            c.userData = btn;
            return c;
        }

        private VisualElement PracticeCard()
        {
            var c = El.Div("card", "col", "mb-m");
            var head = El.Div("row", "space-between");
            head.Add(El.Text("Practice vs Bots", "t-heading"));
            head.Add(El.Btn("Set Up", PracticeDialog.Open, "btn--small"));
            c.Add(head);
            c.Add(El.Text("Runs entirely on this computer (no servers). Cheats allowed, pause with Esc. Progress is not recorded.", "t-small", "t-wrap"));
            return c;
        }

        private async void EnterQueue(string queue)
        {
            if (Offline) { UI.Error("Offline", "Matchmaking requires the Bloodfall services. Sign in, or use Practice vs Bots."); return; }
            var regions = _selectedRegions.Count > 0 ? _selectedRegions.ToList() : App.Backend.Regions.Select(r => r.Id).ToList();
            var r = await App.Backend.EnterQueue(queue, regions);
            if (!r.Ok) UI.Error("Cannot join queue", r.Message);
            RefreshQueue();
        }

        public override void OnShow()
        {
            RefreshRegions();
            RefreshQueue();
            RefreshLobbyState();
            if (!Offline) _serverBrowser.Refresh();
            _serverBrowser.SetOffline(Offline);
            if (!Offline && App.Backend.Regions.Count > 0 && App.Backend.RegionPings.Count == 0) _ = RefreshPings();
        }

        private async System.Threading.Tasks.Task RefreshPings()
        {
            await App.Backend.RefreshRegions();
            RefreshRegions();
        }

        private void RefreshRegions()
        {
            _regionList.Clear();
            if (Offline) { _regionList.Add(El.Text("Unavailable offline.", "t-small")); return; }
            var regions = App.Backend.Regions;
            if (regions.Count == 0) { _regionList.Add(El.Text("No regions reported by the directory service.", "t-small", "t-wrap")); return; }
            if (_selectedRegions.Count == 0)
                foreach (var r in regions) if (App.Settings.PreferredRegions.Count == 0 || App.Settings.PreferredRegions.Contains(r.Id)) _selectedRegions.Add(r.Id);
            foreach (var r in regions)
            {
                var id = r.Id;
                string ping = App.Backend.RegionPings.TryGetValue(id, out var ms) ? (ms >= 0 ? ms + " ms" : "no reply") : "…";
                var t = El.Check($"{r.Name}  ·  {r.Status}  ·  {r.IdleServers}/{r.Servers} servers free  ·  {ping}", _selectedRegions.Contains(id), on =>
                {
                    if (on) _selectedRegions.Add(id); else _selectedRegions.Remove(id);
                    App.Settings.PreferredRegions = _selectedRegions.ToList();
                    App.Settings.Save();
                });
                _regionList.Add(t);
            }
            _regionList.Add(El.Btn("Refresh Pings", () => _ = RefreshPings(), "btn--small", "btn--ghost"));
        }

        private void RefreshQueue()
        {
            var q = App.Backend.Queue;
            bool inQueue = q != null && q.InQueue;
            _queuePanel.Show(inQueue || (q != null && (q.State == "MatchFound" || q.State == "WaitingForOthers" || q.State == "Starting")));
            foreach (var card in _left.Children())
                if (card.userData is Button b) b.SetEnabled(!inQueue && !Offline && App.Backend.Lobby == null && card.enabledSelf);
            if (q == null) return;
            _queueTitle.text = q.State switch
            {
                "Searching" => "Searching for a match…",
                "MatchFound" => "Match found!",
                "WaitingForOthers" => "Waiting for other players to accept…",
                "Starting" => "Starting match…",
                _ => "Queue",
            };
            _queueInfo.text = $"{El.Pretty(q.Queue)} · {El.FormatTime(q.ElapsedSeconds)} elapsed" +
                              (q.EstimatedWaitSeconds >= 0 ? $" · est. {El.FormatTime(q.EstimatedWaitSeconds)}" : "") +
                              $" · {q.PlayersInQueue} in queue" + (q.PartySize > 1 ? $" · party of {q.PartySize}" : "") +
                              (q.Regions != null && q.Regions.Count > 0 ? "\nRegions: " + string.Join(", ", q.Regions) : "");
        }

        private void RefreshLobbyState()
        {
            bool inLobby = App.Backend.Lobby != null && !Offline;
            _browser.Show(!inLobby);
            _lobbyHost.Show(inLobby);
            if (inLobby) _lobbyPanel.Refresh();
            if (inLobby != _lobbyShown && !inLobby) _serverBrowser.Refresh();
            _lobbyShown = inLobby;
            RefreshQueue();
        }

        private float _tick;

        public override void Tick(float dt)
        {
            _tick -= dt;
            if (_tick <= 0f)
            {
                _tick = 1f;
                var q = App.Backend.Queue;
                if (q != null && q.InQueue) { q.ElapsedSeconds += 1f; RefreshQueue(); }
            }
            _serverBrowser.Tick(dt);
        }
    }

    // ====================================================================== server browser

    public sealed class ServerBrowser
    {
        private GameApp App => GameApp.Instance;
        private VisualElement _root, _rows, _header;
        private TextField _search;
        private Toggle _hideFull, _hideLocked, _hideStarted;
        private DropdownField _modeFilter;
        private Label _count;
        private List<LobbyView> _lobbies = new List<LobbyView>();
        private string _sortColumn = "players";
        private bool _sortDesc = true;
        private string _selectedId;
        private Button _join, _spectate, _create, _refresh;
        private bool _offline;
        private float _autoRefresh = 10f;
        private bool _loading;

        private static readonly (string key, string label, float width)[] Columns =
        {
            ("lock", "", 26), ("name", "GAME NAME", 260), ("host", "HOST", 140), ("mode", "MODE", 140), ("map", "MAP", 120),
            ("players", "PLAYERS", 80), ("region", "REGION", 90), ("ping", "PING", 60), ("status", "STATUS", 100),
        };

        public VisualElement Build()
        {
            _root = El.Div("panel-thin", "col", "grow");
            var head = El.Div("row", "space-between");
            head.Add(El.Text("CUSTOM GAMES", "t-subheading"));
            _count = El.Text("", "t-small");
            head.Add(_count);
            _root.Add(head);

            var filters = El.Div("row", "mt-m");
            filters.style.flexWrap = Wrap.Wrap;
            _search = El.Field("", false, "", 40).Width(220);
            _search.style.marginBottom = 0;
            _search.RegisterValueChangedCallback(_ => Render());
            filters.Add(El.Text("Search", "t-label", "mr-m"));
            filters.Add(_search);
            _hideFull = El.Check("Hide full", false, _ => Render());
            _hideLocked = El.Check("Hide passworded", false, _ => Render());
            _hideStarted = El.Check("Hide in progress", true, _ => Render());
            _modeFilter = El.Dropdown("", new List<string> { "All modes", "5v5", "3v3", "1v1", "Practice" }, 0, _ => Render());
            _modeFilter.style.width = 140;
            filters.Add(_hideFull); filters.Add(_hideLocked); filters.Add(_hideStarted); filters.Add(_modeFilter);
            _root.Add(filters);

            _header = El.Div("table-header", "mt-m");
            foreach (var c in Columns)
            {
                var key = c.key;
                var l = El.Text(c.label, "sortable").Width(c.width);
                l.RegisterCallback<ClickEvent>(_ => { if (_sortColumn == key) _sortDesc = !_sortDesc; else { _sortColumn = key; _sortDesc = key == "players"; } Render(); });
                _header.Add(l);
            }
            _root.Add(_header);
            var scroll = El.Scroll().Grow();
            _rows = scroll.contentContainer;
            _root.Add(scroll);

            var actions = El.Div("row", "mt-m");
            _join = El.Btn("Join", () => Join(false), "btn--primary");
            _spectate = El.Btn("Spectate", () => Join(true));
            _create = El.Btn("Create Game", CreateGameDialog.Open);
            _refresh = El.Btn("Refresh", Refresh, "btn--ghost");
            actions.Add(_join); actions.Add(_spectate); actions.Add(El.Spacer()); actions.Add(_create); actions.Add(_refresh);
            _root.Add(actions);
            UpdateButtons();
            return _root;
        }

        public void SetOffline(bool offline)
        {
            _offline = offline;
            UpdateButtons();
            if (offline)
            {
                _rows.Clear();
                _rows.Add(El.Text("The server browser is unavailable offline. Sign in to browse and create custom games.", "t-body", "m-m"));
                _count.text = "";
            }
        }

        public async void Refresh()
        {
            if (_offline || _loading) return;
            _loading = true;
            _refresh.SetEnabled(false);
            var r = await App.Backend.ListLobbies();
            _loading = false;
            _refresh.SetEnabled(true);
            _autoRefresh = 10f;
            if (!r.Ok)
            {
                _rows.Clear();
                _rows.Add(El.Text("Could not load games: " + r.Message, "t-body", "status-offline", "m-m"));
                return;
            }
            _lobbies = r.Value ?? new List<LobbyView>();
            Render();
        }

        public void Tick(float dt)
        {
            if (_offline || _root == null || _root.resolvedStyle.display == DisplayStyle.None) return;
            _autoRefresh -= dt;
            if (_autoRefresh <= 0f) Refresh();
        }

        private int PingFor(LobbyView l) => App.Backend.RegionPings.TryGetValue(l.Region ?? "", out var p) ? p : -1;

        private void Render()
        {
            if (_offline) return;
            IEnumerable<LobbyView> q = _lobbies;
            var s = _search.value?.Trim();
            if (!string.IsNullOrEmpty(s)) q = q.Where(l => (l.Name ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 || (l.HostName ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
            if (_hideFull.value) q = q.Where(l => l.Players < l.MaxPlayers);
            if (_hideLocked.value) q = q.Where(l => !l.HasPassword);
            if (_hideStarted.value) q = q.Where(l => l.Status == "Waiting");
            string mf = _modeFilter.value;
            if (mf == "5v5") q = q.Where(l => l.TeamSize == 5);
            else if (mf == "3v3") q = q.Where(l => l.TeamSize == 3);
            else if (mf == "1v1") q = q.Where(l => l.TeamSize == 1);
            else if (mf == "Practice") q = q.Where(l => (l.ModeId ?? "").Contains("practice"));
            Func<LobbyView, IComparable> key = _sortColumn switch
            {
                "name" => l => l.Name ?? "",
                "host" => l => l.HostName ?? "",
                "mode" => l => l.ModeName ?? l.ModeId ?? "",
                "map" => l => l.MapId ?? "",
                "region" => l => l.Region ?? "",
                "ping" => l => PingFor(l) < 0 ? 9999 : PingFor(l),
                "status" => l => l.Status ?? "",
                "lock" => l => l.HasPassword,
                _ => l => l.Players,
            };
            var list = (_sortDesc ? q.OrderByDescending(key) : q.OrderBy(key)).ToList();
            _rows.Clear();
            _count.text = $"{list.Count} shown · {_lobbies.Count} total";
            if (list.Count == 0)
            {
                _rows.Add(El.Text(_lobbies.Count == 0 ? "No custom games are open right now. Create one and invite your friends!" : "No games match your filters.", "t-body", "m-m"));
            }
            int i = 0;
            foreach (var l in list)
            {
                var row = El.Div("list-row", i++ % 2 == 1 ? "list-row--alt" : "");
                if (l.LobbyId == _selectedId) row.AddToClassList("list-row--selected");
                string[] vals =
                {
                    l.HasPassword ? "🔒" : "", l.Name, l.HostName, l.ModeName ?? El.Pretty(l.ModeId), El.Pretty(l.MapId), $"{l.Players}/{l.MaxPlayers}" + (l.Spectators > 0 ? $" +{l.Spectators}" : ""),
                    l.Region, PingFor(l) >= 0 ? PingFor(l) + "" : "-", l.Status,
                };
                for (int c = 0; c < Columns.Length; c++) row.Add(El.Text(vals[c] ?? "", "cell").Width(Columns[c].width));
                var lobby = l;
                row.RegisterCallback<ClickEvent>(e =>
                {
                    _selectedId = lobby.LobbyId;
                    if (e.clickCount >= 2) Join(false); else Render();
                });
                _rows.Add(row);
            }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var sel = _lobbies.FirstOrDefault(l => l.LobbyId == _selectedId);
            _join?.SetEnabled(!_offline && sel != null && sel.Status == "Waiting" && sel.Players < sel.MaxPlayers);
            _spectate?.SetEnabled(!_offline && sel != null && sel.Spectators < sel.MaxSpectators);
            _create?.SetEnabled(!_offline);
            _refresh?.SetEnabled(!_offline && !_loading);
        }

        private async void Join(bool spectate)
        {
            var sel = _lobbies.FirstOrDefault(l => l.LobbyId == _selectedId);
            if (sel == null) return;
            string password = null;
            if (sel.HasPassword)
            {
                password = await PasswordPrompt.Ask("Password required", $"\"{sel.Name}\" is password protected.");
                if (password == null) return;
            }
            var r = await App.Backend.JoinLobby(sel.LobbyId, password, spectate);
            if (!r.Ok) App.UI.Error("Cannot join", r.Message);
        }
    }

    // ====================================================================== lobby room

    public sealed class LobbyPanel
    {
        private GameApp App => GameApp.Instance;
        private VisualElement _root, _dawn, _dusk, _specs, _chatLog;
        private Label _title, _info;
        private Button _ready, _start, _leave;
        private TextField _chatInput;

        public VisualElement Build()
        {
            _root = El.Div("panel-thin", "col", "grow");
            var head = El.Div("row", "space-between");
            _title = El.Text("", "t-title");
            head.Add(_title);
            _info = El.Text("", "t-small");
            head.Add(_info);
            _root.Add(head);
            _root.Add(El.Div("divider"));
            var teams = El.Div("row", "grow");
            teams.style.alignItems = Align.FlexStart;
            _dawn = El.Div("col", "grow", "mr-m");
            _dusk = El.Div("col", "grow");
            teams.Add(_dawn);
            teams.Add(_dusk);
            _root.Add(teams);
            _specs = El.Div("row", "mt-m");
            _root.Add(_specs);
            var chat = El.Div("card", "col").Height(150);
            var scroll = El.Scroll().Grow();
            _chatLog = scroll.contentContainer;
            chat.Add(scroll);
            _chatInput = El.Field("", false, "", 300);
            _chatInput.style.marginBottom = 0;
            _chatInput.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                var l = App.Backend.Lobby;
                if (l != null && !string.IsNullOrWhiteSpace(_chatInput.value)) App.Backend.SendChat("lobby:" + l.LobbyId, _chatInput.value.Trim());
                _chatInput.value = "";
            });
            chat.Add(_chatInput);
            _root.Add(chat);
            var actions = El.Div("row", "mt-m");
            _ready = El.Btn("Ready", ToggleReady, "btn--primary");
            _start = El.Btn("Start Game", StartGame, "btn--primary");
            _leave = El.Btn("Leave Lobby", async () => await App.Backend.LeaveLobby(), "btn--danger");
            actions.Add(_ready); actions.Add(_start); actions.Add(El.Spacer()); actions.Add(_leave);
            _root.Add(actions);
            App.Backend.ChatReceived += m => { if (m != null && App.Backend.Lobby != null && m.Channel == "lobby:" + App.Backend.Lobby.LobbyId) RenderChat(); };
            return _root;
        }

        private bool IsHost => App.Backend.Lobby != null && App.Backend.Lobby.HostAccountId == App.Backend.MyId;
        private LobbySlotView MySlot => App.Backend.Lobby?.Slots.FirstOrDefault(s => s.AccountId == App.Backend.MyId);

        public void Refresh()
        {
            var l = App.Backend.Lobby;
            if (l == null) return;
            App.Backend.JoinChannel("lobby:" + l.LobbyId);
            _title.text = l.Name;
            _info.text = $"{l.ModeName ?? El.Pretty(l.ModeId)} · {El.Pretty(l.MapId)} · {l.Region} · {l.PickMode}" + (l.Ranked ? " · Ranked" : "") + (l.HasPassword ? " · 🔒" : "") + $" · {l.Status}";
            FillTeam(_dawn, l, "Dawn", "THE DAWN (Sunforge)");
            FillTeam(_dusk, l, "Dusk", "THE DUSK (Velmoragh)");
            _specs.Clear();
            _specs.Add(El.Text($"Spectators ({l.SpectatorList.Count}/{l.MaxSpectators}): ", "t-label"));
            foreach (var s in l.SpectatorList) _specs.Add(El.Text(s.DisplayName, "pill"));
            var me = MySlot;
            _ready.text = me != null && me.Ready ? "Not Ready" : "Ready";
            _ready.Show(me != null && !IsHost);
            _start.Show(IsHost);
            bool allReady = l.Slots.Where(s => !string.IsNullOrEmpty(s.AccountId) && !s.IsBot && !s.Host).All(s => s.Ready);
            _start.SetEnabled(allReady && l.Status == "Waiting");
            RenderChat();
        }

        private void FillTeam(VisualElement host, LobbyView l, string team, string title)
        {
            host.Clear();
            var header = El.Div("panel-header");
            header.Add(El.Text(title, "t-heading", team == "Dawn" ? "t-gold" : "t-red"));
            host.Add(header);
            for (int i = 0; i < l.TeamSize; i++)
            {
                var slot = l.Slots.FirstOrDefault(s => s.Team == team && s.Slot == i);
                var row = El.Div("list-row").Height(40);
                int slotIndex = i;
                if (slot == null || (string.IsNullOrEmpty(slot.AccountId) && !slot.IsBot))
                {
                    row.Add(El.Text("Open slot", "t-muted", "grow"));
                    row.Add(El.Btn("Take", async () => { var r = await App.Backend.LobbySlot(team, slotIndex); if (!r.Ok) App.UI.Toast(r.Message, ToastKind.Error); }, "btn--small", "btn--ghost"));
                    if (IsHost)
                        row.Add(El.Btn("+ Bot", async () => { var r = await App.Backend.LobbyBot(team, slotIndex, "Normal", false); if (!r.Ok) App.UI.Toast(r.Message, ToastKind.Error); }, "btn--small"));
                }
                else if (slot.IsBot)
                {
                    row.Add(El.Text("🤖 " + (slot.DisplayName ?? "Bot"), "grow"));
                    row.Add(El.Text(slot.BotDifficulty ?? "Normal", "pill"));
                    if (IsHost)
                    {
                        var diffs = new List<string> { "Beginner", "Normal", "Veteran", "Nightmare" };
                        var dd = El.Dropdown("", diffs, Math.Max(0, diffs.IndexOf(slot.BotDifficulty ?? "Normal")), async idx => await App.Backend.LobbyBot(team, slotIndex, diffs[Math.Max(0, idx)], false));
                        dd.style.width = 120;
                        row.Add(dd);
                        row.Add(El.Btn("✖", async () => await App.Backend.LobbyBot(team, slotIndex, null, true), "btn--icon", "btn--danger"));
                    }
                }
                else
                {
                    row.Add(El.Text((slot.Host ? "♛ " : "") + slot.DisplayName, "grow", slot.AccountId == App.Backend.MyId ? "t-gold" : ""));
                    row.Add(El.Text($"Lv {slot.Level}", "t-small", "mr-m"));
                    if (!string.IsNullOrEmpty(slot.Rank)) row.Add(El.Text(El.Pretty(slot.Rank), "pill"));
                    row.Add(El.Text(slot.Host ? "HOST" : slot.Ready ? "READY" : "…", slot.Ready || slot.Host ? "status-online" : "t-muted").Width(60));
                    if (IsHost && slot.AccountId != App.Backend.MyId)
                    {
                        var id = slot.AccountId;
                        row.Add(El.Btn("Kick", async () => await App.Backend.LobbyKick(id), "btn--small", "btn--danger"));
                    }
                }
                host.Add(row);
            }
        }

        private void RenderChat()
        {
            var l = App.Backend.Lobby;
            if (l == null || _chatLog == null) return;
            _chatLog.Clear();
            if (App.Backend.Channels.TryGetValue("lobby:" + l.LobbyId, out var ch))
                foreach (var m in ch.Messages.Skip(Math.Max(0, ch.Messages.Count - 60)))
                    _chatLog.Add(El.Text(m.System ? m.Text : $"{m.FromName}: {m.Text}", "chat-line", m.System ? "chat-system" : ""));
        }

        private async void ToggleReady()
        {
            var me = MySlot;
            var r = await App.Backend.LobbyReady(me == null || !me.Ready);
            if (!r.Ok) App.UI.Toast(r.Message, ToastKind.Error);
        }

        private async void StartGame()
        {
            _start.SetEnabled(false);
            _start.text = "Allocating server…";
            var r = await App.Backend.StartLobby();
            _start.text = "Start Game";
            if (!r.Ok) { App.UI.Error("Cannot start", r.Message); _start.SetEnabled(true); }
        }
    }

    // ====================================================================== dialogs

    public static class CreateGameDialog
    {
        public static void Open()
        {
            var app = GameApp.Instance;
            var d = El.Div("dialog", "col").Width(620);
            d.Add(El.Text("Create Game", "t-title", "t-center"));
            d.Add(El.Div("divider"));
            var name = El.Field("Game name", false, (app.Backend.Session.Account?.DisplayName ?? "Player") + "'s game", 40);
            var password = El.Field("Password (optional)", true, "", 32);
            var modes = app.Data.Modes.Values.Where(m => m.Kind == GameModeKind.Moba).ToList();
            var modeDd = El.Dropdown("Mode", modes.Select(m => m.Name).ToList(), Math.Max(0, modes.FindIndex(m => m.Id == "moba_5v5")), null);
            var regions = app.Backend.Regions;
            var regionDd = El.Dropdown("Region", regions.Count > 0 ? regions.Select(r => r.Name).ToList() : new List<string> { "(no regions online)" }, 0, null);
            var picks = new List<string> { "AllPick", "RandomDraft", "AllRandom", "BlindPick" };
            var pickDd = El.Dropdown("Hero pick", picks, 0, null);
            var bots = new List<string> { "Beginner", "Normal", "Veteran", "Nightmare" };
            var botDd = El.Dropdown("Bot difficulty", bots, 1, null);
            var fill = El.Check("Fill empty slots with bots when starting", true);
            var priv = El.Check("Private (hidden from the server browser)", false);
            var specs = El.SliderField("Spectator slots", 0, 10, 4, null);
            specs.showInputField = true;
            var err = El.Text("", "field-error");
            foreach (var e in new VisualElement[] { name, password, modeDd, regionDd, pickDd, botDd, fill, priv, specs, err }) d.Add(e);
            var row = El.Div("row", "center", "mt-m");
            VisualElement ov = null;
            Button create = null;
            create = El.Btn("Create", async () =>
            {
                if (regions.Count == 0) { err.text = "No game server regions are online right now."; return; }
                create.SetEnabled(false);
                var mode = modes[Math.Max(0, modeDd.index)];
                var r = await app.Backend.CreateLobby(new CreateLobbyRequest
                {
                    Name = name.value, Password = string.IsNullOrEmpty(password.value) ? null : password.value, ModeId = mode.Id, MapId = mode.Map ?? "map_velmoragh",
                    TeamSize = mode.TeamSize, Region = regions[Math.Max(0, regionDd.index)].Id, PickMode = picks[Math.Max(0, pickDd.index)],
                    BotDifficulty = bots[Math.Max(0, botDd.index)], FillWithBots = fill.value, IsPrivate = priv.value, MaxSpectators = Mathf.RoundToInt(specs.value),
                });
                create.SetEnabled(true);
                if (r.Ok) app.UI.CloseOverlay(ov);
                else err.text = r.Message;
            }, "btn--primary");
            row.Add(create);
            row.Add(El.Btn("Cancel", () => app.UI.CloseOverlay(ov), "btn--ghost"));
            d.Add(row);
            ov = app.UI.ShowOverlay(d);
        }
    }

    public static class PracticeDialog
    {
        public static void Open()
        {
            var app = GameApp.Instance;
            var d = El.Div("dialog", "col").Width(560);
            d.Add(El.Text("Practice vs Bots", "t-title", "t-center"));
            d.Add(El.Text("Hosted locally by your client - works offline.", "t-small", "t-center"));
            d.Add(El.Div("divider"));
            var sizes = new List<string> { "5v5", "3v3", "1v1" };
            var size = El.Dropdown("Team size", sizes, 0, null);
            var diffs = new List<string> { "Beginner", "Normal", "Veteran", "Nightmare" };
            var ally = El.Dropdown("Ally bots", diffs, 1, null);
            var enemy = El.Dropdown("Enemy bots", diffs, 1, null);
            var fillAllies = El.Check("Fill my team with bots", true);
            var cheats = El.Check("Enable practice cheats", true);
            var name = El.Field("Your name", false, app.Backend.Session.Account?.DisplayName ?? "Player", 24);
            foreach (var e in new VisualElement[] { name, size, ally, enemy, fillAllies, cheats }) d.Add(e);
            var row = El.Div("row", "center", "mt-m");
            VisualElement ov = null;
            row.Add(El.Btn("Start", () =>
            {
                app.UI.CloseOverlay(ov);
                int ts = size.index == 1 ? 3 : size.index == 2 ? 1 : 5;
                app.Flow.StartOfflineMatch(new OfflineMatchOptions
                {
                    TeamSize = ts, ModeId = "moba_practice", AllyDifficulty = (BotDifficulty)Math.Max(0, ally.index), EnemyDifficulty = (BotDifficulty)Math.Max(0, enemy.index),
                    FillAllies = fillAllies.value, Cheats = cheats.value, PlayerName = name.value,
                });
            }, "btn--primary", "btn--large"));
            row.Add(El.Btn("Cancel", () => app.UI.CloseOverlay(ov), "btn--ghost"));
            d.Add(row);
            ov = app.UI.ShowOverlay(d);
        }
    }

    public static class PasswordPrompt
    {
        public static System.Threading.Tasks.Task<string> Ask(string title, string message)
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<string>();
            var app = GameApp.Instance;
            var d = El.Div("dialog", "col").Width(460);
            d.Add(El.Text(title, "t-title", "t-center"));
            d.Add(El.Text(message, "t-body", "t-center", "mt-m"));
            var pw = El.Field("Password", true);
            d.Add(pw);
            var row = El.Div("row", "center");
            VisualElement ov = null;
            row.Add(El.Btn("OK", () => { app.UI.CloseOverlay(ov); tcs.TrySetResult(pw.value ?? ""); }, "btn--primary"));
            row.Add(El.Btn("Cancel", () => { app.UI.CloseOverlay(ov); tcs.TrySetResult(null); }, "btn--ghost"));
            d.Add(row);
            ov = app.UI.ShowOverlay(d, () => { app.UI.CloseOverlay(ov); tcs.TrySetResult(null); });
            pw.Focus();
            return tcs.Task;
        }
    }
}
