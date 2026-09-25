using System;
using System.Collections.Generic;
using System.Linq;
using Bloodfall.Client.Core;
using Bloodfall.Client.Match;
using Bloodfall.Data;
using Bloodfall.Protocol;
using Bloodfall.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Bloodfall.Client.UI.Screens
{
    // ====================================================================== connecting

    public sealed class ConnectingToMatchScreen : UIScreen
    {
        private Label _title, _detail;
        private float _t;

        protected override void OnBuild(VisualElement root)
        {
            root.Add(El.Div("fill", "vignette"));
            var col = El.Div("fill", "col", "center");
            var p = El.Div("panel", "col", "center").Width(560);
            _title = El.Text("", "t-title", "t-center");
            _detail = El.Text("", "t-small", "t-center", "mt-m");
            p.Add(_title);
            p.Add(_detail);
            p.Add(El.Btn("Cancel", () => App.Match?.Leave(), "btn--ghost", "mt-l"));
            col.Add(p);
            root.Add(col);
        }

        public void SetText(string title, string detail) { _title.text = title; _detail.text = detail; }

        public override void Tick(float dt)
        {
            _t += dt;
            var c = App.Match?.Client;
            if (c == null) return;
            string dots = new string('.', 1 + (int)(_t * 2) % 3);
            if (c.State == ClientConnectionState.Handshaking) _title.text = "Verifying match ticket" + dots;
            else if (c.State == ClientConnectionState.Connecting) _title.text = "Connecting to game server" + dots;
        }
    }

    // ====================================================================== hero select

    public sealed class HeroSelectScreen : UIScreen
    {
        private MatchController _mc;
        private Label _timer, _phase;
        private VisualElement _dawn, _dusk, _grid, _detail, _chatLog;
        private Button _lock, _random;
        private string _selected;
        private float _localTimer;
        private TextField _chat;
        private int _lastChat;

        protected override void OnBuild(VisualElement root)
        {
            root.Add(El.Div("fill", "stone-bg"));
            root.Add(El.Div("fill", "vignette"));
            var frame = El.Div("fill", "col");
            frame.style.paddingLeft = 20; frame.style.paddingRight = 20; frame.style.paddingTop = 12; frame.style.paddingBottom = 12;
            var top = El.Div("row", "space-between");
            _phase = El.Text("CHOOSE YOUR HERO", "t-title");
            _timer = El.Text("", "t-display");
            top.Add(_phase);
            top.Add(_timer);
            frame.Add(top);
            var body = El.Div("row", "grow");
            body.style.alignItems = Align.Stretch;
            _dawn = El.Div("panel-thin", "col").Width(300);
            var center = El.Div("col", "grow");
            center.style.marginLeft = 12; center.style.marginRight = 12;
            _dusk = El.Div("panel-thin", "col").Width(300);
            body.Add(_dawn); body.Add(center); body.Add(_dusk);
            var gridPanel = El.Div("panel-thin", "col");
            gridPanel.Add(El.Text("HEROES", "t-subheading"));
            _grid = El.Div("row");
            _grid.style.flexWrap = Wrap.Wrap;
            gridPanel.Add(_grid);
            center.Add(gridPanel);
            _detail = El.Div("panel-thin", "col", "grow", "mt-m");
            center.Add(_detail);
            var actions = El.Div("row", "center", "mt-m");
            _lock = El.Btn("LOCK IN", Lock, "btn--primary", "btn--large");
            _random = El.Btn("Random", () => { _mc.Client.PickHero("random"); }, "btn--ghost");
            actions.Add(_lock);
            actions.Add(_random);
            center.Add(actions);
            var chat = El.Div("card", "col", "mt-m").Height(150);
            var cs = El.Scroll().Grow();
            _chatLog = cs.contentContainer;
            chat.Add(cs);
            _chat = El.Field("", false, "", 200);
            _chat.style.marginBottom = 0;
            _chat.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { if (!string.IsNullOrWhiteSpace(_chat.value)) _mc?.SendChat(_chat.value, true); _chat.value = ""; } });
            chat.Add(_chat);
            center.Add(chat);
            frame.Add(body);
            root.Add(frame);
        }

        public void Bind(MatchController mc)
        {
            _mc = mc;
            _selected = null;
            _lastChat = -1;
            mc.Client.OnMatchState -= OnState;
            mc.Client.OnMatchState += OnState;
            if (mc.Client.MatchState != null) OnState(mc.Client.MatchState);
        }

        private void OnState(MatchStateInfo s)
        {
            if (!Visible) return;
            _localTimer = s.PhaseTimer;
            _phase.text = s.Phase == MatchPhase.WaitingForPlayers ? "WAITING FOR PLAYERS" : "CHOOSE YOUR HERO";
            FillTeam(_dawn, s, Team.Dawn, "THE DAWN");
            FillTeam(_dusk, s, Team.Dusk, "THE DUSK");
            var me = s.Players.FirstOrDefault(p => p.Id == _mc.Client.LocalPlayerId);
            bool locked = me != null && me.HeroLocked;
            if (locked) _selected = me.HeroId;
            _lock.SetEnabled(!locked && _selected != null && !_mc.Client.IsSpectator);
            _random.SetEnabled(!locked && !_mc.Client.IsSpectator);
            _lock.text = locked ? "LOCKED" : "LOCK IN";
            RenderGrid(s, locked);
            RenderDetail();
        }

        private void FillTeam(VisualElement host, MatchStateInfo s, Team team, string title)
        {
            host.Clear();
            host.Add(El.Text(title, "t-heading", team == Team.Dawn ? "t-gold" : "t-red"));
            foreach (var p in s.Players.Where(p => p.Team == team).OrderBy(p => p.Slot))
            {
                var row = El.Div("row", "card", "mb-m");
                var portrait = El.Div("portrait", team == Team.Dawn ? "portrait--dawn" : "portrait--dusk").Size(58, 58);
                if (p.HeroId != null && App.Data.Heroes.TryGetValue(p.HeroId, out var hd)) El.SetImage(portrait, GameText.PortraitPath(hd));
                row.Add(portrait);
                var col = El.Div("col", "ml-m");
                col.Add(El.Text(p.Name + (p.Id == _mc.Client.LocalPlayerId ? " (you)" : ""), p.Id == _mc.Client.LocalPlayerId ? "t-gold" : ""));
                string heroName = p.HeroId != null && App.Data.Heroes.TryGetValue(p.HeroId, out var h2) ? h2.Name : null;
                col.Add(El.Text(p.HeroLocked ? heroName ?? "Locked in" : p.IsBot ? "Bot - picks last" : "Picking…", "t-small", p.HeroLocked ? "t-green" : "t-muted"));
                if (p.Connection == PlayerConnection.Disconnected) col.Add(El.Text("Disconnected", "t-small", "t-red"));
                row.Add(col);
                host.Add(row);
            }
        }

        private void RenderGrid(MatchStateInfo s, bool locked)
        {
            _grid.Clear();
            var taken = new HashSet<string>(s.Players.Where(p => p.HeroLocked && p.HeroId != null && p.Id != _mc.Client.LocalPlayerId).Select(p => p.HeroId));
            int playable = App.Data.PlayableHeroes().Count();
            bool unique = playable >= s.Players.Count;
            foreach (var h in App.Data.PlayableHeroes())
            {
                bool isTaken = unique && taken.Contains(h.Id);
                var tile = El.Div("hero-tile", "portrait", h.Id == _selected ? "hero-tile--selected" : "", isTaken ? "hero-tile--taken" : "", locked && h.Id != _selected ? "hero-tile--locked" : "");
                El.SetImage(tile, GameText.PortraitPath(h));
                var name = El.Text(h.Name, "t-small", "t-center");
                name.style.position = Position.Absolute; name.style.bottom = 0; name.style.left = 0; name.style.right = 0;
                name.style.backgroundColor = new Color(0, 0, 0, 0.7f);
                tile.Add(name);
                var id = h.Id;
                if (!locked && !isTaken) tile.RegisterCallback<ClickEvent>(e => { _selected = id; App.Audio.PlayUi("click"); if (e.clickCount >= 2) Lock(); else OnState(_mc.Client.MatchState); });
                _grid.Add(tile);
            }
        }

        private void RenderDetail()
        {
            _detail.Clear();
            if (_selected == null || !App.Data.Heroes.TryGetValue(_selected, out var h)) { _detail.Add(El.Text("Select a hero to see their abilities.", "t-body")); return; }
            _detail.Add(El.Text($"{h.Name} - {h.Title}", "t-title"));
            _detail.Add(El.Text($"{GameText.Faction(h.Faction)} · {string.Join(", ", h.Roles)} · {GameText.Attribute(h.PrimaryAttribute)} · {h.AttackType}", "t-small"));
            var row = El.Div("row", "mt-m");
            foreach (var aid in h.Abilities)
            {
                if (!App.Data.Abilities.TryGetValue(aid, out var a) || a.Hidden) continue;
                var slot = El.Div("hud-slot", a.IsUltimate ? "hud-slot--ult" : "");
                var icon = El.Img(GameText.AbilityIconPath(a), 48, 48, "icon");
                icon.style.position = Position.Absolute; icon.style.left = 5; icon.style.top = 5;
                slot.Add(icon);
                UI.AttachTooltip(slot, () => AbilityTooltip.Build(a, 0, null));
                row.Add(slot);
            }
            _detail.Add(row);
            if (!string.IsNullOrEmpty(h.Lore)) _detail.Add(El.Text(h.Lore, "t-small", "t-wrap", "mt-m"));
        }

        private void Lock()
        {
            if (_selected == null) return;
            _mc.Client.PickHero(_selected);
            App.Audio.PlayUi("lock_in");
        }

        public override void Tick(float dt)
        {
            _localTimer = Mathf.Max(0, _localTimer - dt);
            _timer.text = Mathf.CeilToInt(_localTimer).ToString();
            _timer.EnableInClassList("t-red", _localTimer <= 10f);
            if (_mc != null && _mc.Client.ChatLog.Count != _lastChat)
            {
                _lastChat = _mc.Client.ChatLog.Count;
                _chatLog.Clear();
                foreach (var m in _mc.Client.ChatLog.Skip(Math.Max(0, _mc.Client.ChatLog.Count - 40)))
                    _chatLog.Add(El.Text((m.TeamOnly ? "[Team] " : "") + m.Name + ": " + m.Text, "chat-line", m.PlayerId < 0 ? "chat-system" : ""));
            }
        }

        public override bool OnEscape()
        {
            UI.Dialog("Leave Match", _mc != null && _mc.Online ? "Leaving now counts as an abandon and may affect your matchmaking." : "Leave this practice match?",
                ("Leave", "btn--danger", () => _mc?.Leave()), ("Stay", "btn--ghost", null));
            return true;
        }
    }

    /// <summary>Ability tooltip with level-aware values.</summary>
    public static class AbilityTooltip
    {
        public static VisualElement Build(AbilityDef a, int level, AbilityView? view)
        {
            var c = El.Div("col");
            c.Add(El.Text(a.Name, "t-heading"));
            c.Add(El.Text(GameText.Slot(a.Slot) + (level > 0 ? $" · Level {level}/{a.MaxLevel}" : ""), "t-small"));
            c.Add(El.Text(a.Description, "t-body", "mt-m"));
            c.Add(El.Text(GameText.AbilityStats(a), "t-small", "t-wrap", "mt-m"));
            if (view.HasValue && view.Value.CanLevel) c.Add(El.Text("Ctrl + key or click + to learn", "t-small", "t-gold", "mt-m"));
            return c;
        }
    }

    // ====================================================================== loading

    public sealed class LoadingScreen : UIScreen
    {
        private MatchController _mc;
        private VisualElement _dawn, _dusk;
        private VisualElement _fill;
        private Label _pct, _tip;
        private float _tipTimer;
        private int _tipIndex;
        private static readonly string[] Tips =
        {
            "Deny allied creeps below half health to rob the enemy of gold and experience.",
            "Towers prefer creeps - until you attack an enemy hero in their range.",
            "Night halves most units' vision. Ambush from the fog.",
            "Neutral camps respawn every minute if their area is empty. Pull them to stack.",
            "The Heart of Vharoth sleeps beneath the ruins. Four seals hold it... for now.",
            "Buyback returns you instantly - at a steep price and a long cooldown.",
            "Hold Alt and click to ping the map for your team.",
            "Shift queues orders: move, cast, attack - one after another.",
        };

        protected override void OnBuild(VisualElement root)
        {
            root.Add(El.Div("fill", "stone-bg"));
            root.Add(El.Div("fill", "vignette"));
            var col = El.Div("fill", "col", "center");
            col.Add(El.Text("VELMORAGH", "t-display"));
            col.Add(El.Text("The Blood Ruins", "t-title"));
            col.Add(El.Text("Once the jewel of the Crimson Court, now a battlefield where the Dawn and the Dusk bleed for the relics buried beneath.", "t-body", "t-center", "mt-m").Width(900));
            var teams = El.Div("row", "mt-l");
            _dawn = El.Div("col", "mr-m").Width(560);
            _dusk = El.Div("col").Width(560);
            teams.Add(_dawn);
            teams.Add(_dusk);
            col.Add(teams);
            var (bar, fill, text) = El.Bar("bar-fill--hp", 22);
            bar.style.width = 900;
            bar.AddToClassList("mt-l");
            _fill = fill;
            _pct = text;
            col.Add(bar);
            _tip = El.Text("", "t-body", "t-center", "mt-l").Width(900);
            col.Add(_tip);
            root.Add(col);
        }

        public void Bind(MatchController mc)
        {
            _mc = mc;
            _tipIndex = UnityEngine.Random.Range(0, Tips.Length);
            _tip.text = "TIP: " + Tips[_tipIndex];
        }

        public override void Tick(float dt)
        {
            if (_mc == null) return;
            El.SetFill(_fill, _mc.LoadProgress);
            _pct.text = _mc.WorldReady ? "Waiting for other players…" : $"Loading {Mathf.RoundToInt(_mc.LoadProgress * 100)}%";
            _tipTimer += dt;
            if (_tipTimer > 7f) { _tipTimer = 0; _tipIndex = (_tipIndex + 1) % Tips.Length; _tip.text = "TIP: " + Tips[_tipIndex]; }
            var s = _mc.Client.MatchState;
            if (s == null) return;
            Fill(_dawn, s, Team.Dawn);
            Fill(_dusk, s, Team.Dusk);
        }

        private void Fill(VisualElement host, MatchStateInfo s, Team team)
        {
            host.Clear();
            foreach (var p in s.Players.Where(p => p.Team == team).OrderBy(p => p.Slot))
            {
                var row = El.Div("row", "card", "mb-m");
                var portrait = El.Div("portrait", team == Team.Dawn ? "portrait--dawn" : "portrait--dusk").Size(48, 48);
                if (p.HeroId != null && App.Data.Heroes.TryGetValue(p.HeroId, out var hd)) El.SetImage(portrait, GameText.PortraitPath(hd));
                row.Add(portrait);
                var col = El.Div("col", "ml-m", "grow");
                string hn = GameText.PlayedAs(App.Data, p.HeroId, p.RtsFaction);
                col.Add(El.Text($"{p.Name}  ·  {hn}"));
                var (bar, fill, _) = El.Bar(team == Team.Dawn ? "bar-fill--xp" : "bar-fill--enemy", 8);
                El.SetFill(fill, p.IsBot ? 1f : p.LoadProgress);
                col.Add(bar);
                row.Add(col);
                host.Add(row);
            }
        }
    }

    // ====================================================================== post game

    public sealed class PostGameScreen : UIScreen
    {
        private Label _banner, _sub, _rewards;
        private VisualElement _board;
        private MatchResult _result;
        private bool _online;
        private string _me;

        protected override void OnBuild(VisualElement root)
        {
            root.Add(El.Div("fill", "vignette"));
            var frame = El.Div("fill", "col", "center");
            _banner = El.Text("", "announcer");
            _sub = El.Text("", "t-title", "t-center");
            frame.Add(_banner);
            frame.Add(_sub);
            _rewards = El.Text("", "t-heading", "t-center", "mt-m");
            frame.Add(_rewards);
            var panel = El.Div("panel", "col", "mt-m");
            panel.style.width = 1500;
            _board = El.Div("col");
            panel.Add(_board);
            frame.Add(panel);
            var row = El.Div("row", "center", "mt-m");
            row.Add(El.Btn("Return to Client", () => App.Flow.GoClient(), "btn--primary", "btn--large"));
            row.Add(El.Btn("Play Again", () => { App.Flow.GoClient(); if (!_online) PracticeDialog.Open(); }, "btn--ghost"));
            frame.Add(row);
            root.Add(frame);
        }

        public void Present(MatchResult r, string localAccountId, bool online)
        {
            _result = r;
            _online = online;
            _me = localAccountId;
            var me = r.Players.FirstOrDefault(p => p.AccountId == localAccountId);
            bool won = me != null && me.Won;
            _banner.text = me == null ? (r.Winner ?? "").ToUpperInvariant() + " VICTORY" : won ? "VICTORY" : "DEFEAT";
            _banner.EnableInClassList("t-gold", won);
            _sub.text = $"{(r.Winner == "Dawn" ? "The Dawn" : "The Dusk")} prevailed · {El.FormatTime(r.DurationSeconds)} · {El.Pretty(r.ModeId)}" + (r.Ranked ? " · Ranked" : "") + (online ? "" : " · Practice (not recorded)");
            _rewards.text = online ? "Recording results…" : "";
            BuildBoard();
            if (online) FetchRewards();
            App.Audio.PlayMusic(won ? "victory" : "defeat");
        }

        private async void FetchRewards()
        {
            // The statistics service computes rating and XP from the server's report; poll briefly for it.
            for (int i = 0; i < 6; i++)
            {
                await System.Threading.Tasks.Task.Delay(1500);
                var d = await App.Backend.GetMatchDetail(_result.MatchId);
                if (!d.Ok) continue;
                var me = d.Value.Players.FirstOrDefault(p => p.AccountId == _me);
                await App.Backend.RefreshProfile();
                var acc = App.Backend.Session.Account;
                _rewards.text = (d.Value.Ranked && me != null ? $"Rating {(me.RatingChange >= 0 ? "+" : "")}{me.RatingChange} · " : "") + (acc != null ? $"Account level {acc.Level} ({acc.Xp}/{acc.XpForNextLevel} XP)" : "Results recorded.");
                return;
            }
            _rewards.text = "Results are still being processed. Check your match history shortly.";
        }

        private void BuildBoard()
        {
            _board.Clear();
            foreach (var team in new[] { "Dawn", "Dusk" })
            {
                _board.Add(El.Text(team == "Dawn" ? "THE DAWN" : "THE DUSK", "t-subheading", "mt-m", team == "Dawn" ? "t-gold" : "t-red"));
                var header = El.Div("table-header");
                foreach (var (l, w) in Cols) header.Add(El.Text(l).Width(w));
                _board.Add(header);
                foreach (var p in _result.Players.Where(p => p.Team == team).OrderBy(p => p.Slot))
                {
                    var row = El.Div("scoreboard-row", p.AccountId == _me ? "list-row--selected" : "");
                    string hero = GameText.PlayedAs(App.Data, p.HeroId, p.RtsFaction);
                    string[] vals =
                    {
                        p.Name + (p.IsBot ? " (bot)" : "") + (p.Abandoned ? " ✖" : ""), hero, p.Level.ToString(), $"{p.Kills}/{p.Deaths}/{p.Assists}", $"{p.LastHits}/{p.Denies}",
                        p.NetWorth.ToString(), p.Gpm.ToString("0"), p.Xpm.ToString("0"), p.HeroDamage.ToString("0"), p.BuildingDamage.ToString("0"), p.Healing.ToString("0"),
                    };
                    for (int i = 0; i < vals.Length; i++) row.Add(El.Text(vals[i]).Width(Cols[i].w));
                    var items = El.Div("row").Width(250);
                    foreach (var it in p.Items) if (!string.IsNullOrEmpty(it) && App.Data.Items.TryGetValue(it, out var idf)) items.Add(El.Img(GameText.ItemIconPath(idf), 34, 28, "portrait"));
                    row.Add(items);
                    if (_online && !p.IsBot && p.AccountId != _me)
                    {
                        var id = p.AccountId; var name = p.Name;
                        row.Add(El.Btn("👍", async () => { var r = await App.Backend.Commend(new Contracts.CommendRequest { MatchId = _result.MatchId, AccountId = id, Kind = "teamwork" }); UI.Toast(r.Ok ? "Commended " + name + "." : r.Message, r.Ok ? ToastKind.Success : ToastKind.Error); }, "btn--icon"));
                        row.Add(El.Btn("⚑", () => ReportDialog.Open(id, name, _result.MatchId), "btn--icon", "btn--danger"));
                    }
                    _board.Add(row);
                }
            }
        }

        private static readonly (string l, float w)[] Cols =
        {
            ("PLAYER", 220), ("HERO", 130), ("LVL", 50), ("K/D/A", 90), ("LH/DN", 80), ("NET WORTH", 100), ("GPM", 60), ("XPM", 60), ("HERO DMG", 90), ("TOWER DMG", 90), ("HEALING", 80), ("ITEMS", 250),
        };

        public override bool OnEscape() { App.Flow.GoClient(); return true; }
    }
}
