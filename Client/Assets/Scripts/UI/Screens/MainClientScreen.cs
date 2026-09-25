using System;
using System.Collections.Generic;
using System.Linq;
using Bloodfall.Client.Backend;
using Bloodfall.Client.Core;
using Bloodfall.Contracts;
using UnityEngine;
using UnityEngine.UIElements;

namespace Bloodfall.Client.UI.Screens
{
    /// <summary>A page inside the main client (PLAY, HEROES, ...). Built once, refreshed on show.</summary>
    public abstract class ClientPage
    {
        public VisualElement Root { get; private set; }
        protected GameApp App => GameApp.Instance;
        protected UIManager UI => GameApp.Instance.UI;
        protected bool Offline => App.Flow.OfflineMode;

        public VisualElement Build()
        {
            Root = El.Div("col", "grow");
            Root.style.flexGrow = 1;
            OnBuild(Root);
            return Root;
        }

        protected abstract void OnBuild(VisualElement root);
        public virtual void OnShow() { }
        public virtual void OnHide() { }
        public virtual void Tick(float dt) { }

        /// <summary>Honest placeholder for online-only features while playing offline.</summary>
        protected static VisualElement OfflineNotice(string feature)
        {
            var c = El.Div("card", "col", "center", "m-m");
            c.Add(El.Text("Offline", "t-title", "t-center"));
            c.Add(El.Text(feature + " requires a connection to the Bloodfall services. Sign in to use it.", "t-body", "t-center", "mt-m"));
            c.Add(El.Btn("Sign In", () => GameApp.Instance.Flow.GoLogin(), "btn--primary", "mt-m"));
            return c;
        }
    }

    public enum ClientTab { Play, Heroes, Items, Strategy, Community, Rankings, Profile }

    /// <summary>
    /// The main client after login: navigation tabs, the active page, the social sidebar (friends, party,
    /// requests), the chat dock and a live service status bar.
    /// </summary>
    public sealed class MainClientScreen : UIScreen
    {
        private readonly Dictionary<ClientTab, ClientPage> _pages = new Dictionary<ClientTab, ClientPage>();
        private readonly Dictionary<ClientTab, Button> _tabs = new Dictionary<ClientTab, Button>();
        private VisualElement _content;
        private ClientPage _current;
        private Label _accountName, _accountSub, _statusLine;
        private VisualElement _rankIcon, _levelFill;
        private SocialPanel _social;
        private ChatDock _chat;
        public ClientTab CurrentTab { get; private set; } = ClientTab.Play;

        protected override void OnBuild(VisualElement root)
        {
            root.pickingMode = PickingMode.Ignore;
            var shade = El.Div("fill");
            shade.style.backgroundColor = new Color(0, 0, 0, 0.35f);
            shade.pickingMode = PickingMode.Ignore;
            root.Add(shade);

            var frame = El.Div("fill", "col");
            frame.pickingMode = PickingMode.Ignore;
            root.Add(frame);

            // --- top bar
            var top = El.Div("row", "panel-thin").Height(74);
            top.style.paddingLeft = 16;
            top.style.paddingRight = 16;
            top.Add(El.Img("Textures/UI/logo_bloodfall_small", 190, 62).Cls("shrink0"));
            var nav = El.Div("row", "grow", "center");
            foreach (ClientTab t in Enum.GetValues(typeof(ClientTab)))
            {
                var tab = t;
                var b = El.Tab(t.ToString().ToUpperInvariant(), () => ShowTab(tab), "nav-tab");
                _tabs[t] = b;
                nav.Add(b);
            }
            top.Add(nav);
            var acct = El.Div("row", "shrink0");
            _rankIcon = El.Img("Textures/UI/Ranks/rank_initiate", 48, 48);
            acct.Add(_rankIcon);
            var acctText = El.Div("col", "ml-m").Width(170);
            _accountName = El.Text("", "t-heading");
            _accountSub = El.Text("", "t-small");
            var (bar, fill, _) = El.Bar("bar-fill--xp", 6);
            _levelFill = fill;
            acctText.Add(_accountName);
            acctText.Add(_accountSub);
            acctText.Add(bar.Cls("mt-m"));
            acct.Add(acctText);
            acct.Add(El.Btn("⚙", () => SettingsDialog.Open(), "btn--icon"));
            acct.Add(El.Btn("⏻", ConfirmLogout, "btn--icon", "btn--danger"));
            top.Add(acct);
            frame.Add(top);

            // --- body: page + social sidebar
            var body = El.Div("row", "grow");
            body.style.alignItems = Align.Stretch;
            var left = El.Div("col", "grow");
            _content = El.Div("col", "grow");
            _content.style.paddingLeft = 12;
            _content.style.paddingTop = 8;
            left.Add(_content);
            _chat = new ChatDock();
            left.Add(_chat.Build());
            body.Add(left);
            _social = new SocialPanel();
            body.Add(_social.Build());
            frame.Add(body);

            // --- status bar
            var status = El.Div("row").Height(24);
            status.style.backgroundColor = new Color(0.03f, 0.02f, 0.03f, 0.9f);
            status.style.paddingLeft = 12;
            _statusLine = El.Text("", "t-small");
            status.Add(_statusLine);
            frame.Add(status);

            _pages[ClientTab.Play] = new PlayPage();
            _pages[ClientTab.Heroes] = new HeroesPage();
            _pages[ClientTab.Items] = new ItemsPage();
            _pages[ClientTab.Strategy] = new StrategyPage();
            _pages[ClientTab.Community] = new CommunityPage();
            _pages[ClientTab.Rankings] = new RankingsPage();
            _pages[ClientTab.Profile] = new ProfilePage();
            foreach (var p in _pages.Values) { var r = p.Build(); r.Show(false); _content.Add(r); }

            var b2 = App.Backend;
            b2.FriendsChanged += () => { if (Visible) _social.Refresh(); };
            b2.PartyChanged += () => { if (Visible) _social.Refresh(); };
            b2.ChatReceived += m => _chat.OnMessage(m);
        }

        public override void OnShow()
        {
            RefreshAccount();
            _social.Refresh();
            _social.Root.Show(!App.Flow.OfflineMode);
            _chat.Root.Show(!App.Flow.OfflineMode);
            _chat.Refresh();
            ShowTab(CurrentTab);
        }

        public void ShowTab(ClientTab t)
        {
            CurrentTab = t;
            foreach (var kv in _tabs) kv.Value.EnableInClassList("tab--active", kv.Key == t);
            _current?.OnHide();
            foreach (var kv in _pages) kv.Value.Root.Show(kv.Key == t);
            _current = _pages[t];
            _current.OnShow();
        }

        public T Page<T>() where T : ClientPage => _pages.Values.OfType<T>().FirstOrDefault();

        public void RefreshAccount()
        {
            if (App.Flow.OfflineMode)
            {
                _accountName.text = "Offline";
                _accountSub.text = "Practice only";
                El.SetFill(_levelFill, 0);
                return;
            }
            var a = App.Backend.Session.Account;
            if (a == null) return;
            _accountName.text = a.DisplayName + (string.IsNullOrEmpty(a.ClanTag) ? "" : $" [{a.ClanTag}]");
            _accountSub.text = $"Level {a.Level} · {El.Pretty(a.Rank ?? "Initiate")} · {a.Rating}";
            El.SetFill(_levelFill, a.XpForNextLevel > 0 ? a.Xp / (float)a.XpForNextLevel : 0f);
            El.SetImage(_rankIcon, "Textures/UI/Ranks/rank_" + (a.Rank ?? "initiate").ToLowerInvariant().Replace(' ', '_'));
        }

        private void ConfirmLogout()
        {
            if (App.Flow.OfflineMode) { App.Flow.GoLogin(); return; }
            UI.Dialog("Log Out", "Sign out of Bloodfall?", ("Log Out", "btn--danger", () => App.Flow.Logout()), ("Cancel", "btn--ghost", null));
        }

        private float _statusTimer;

        public override void Tick(float dt)
        {
            _current?.Tick(dt);
            _chat.Tick(dt);
            _statusTimer -= dt;
            if (_statusTimer <= 0f)
            {
                _statusTimer = 1f;
                RefreshAccount();
                if (App.Flow.OfflineMode) _statusLine.text = "OFFLINE MODE · online services unavailable · practice matches run locally on this computer";
                else
                {
                    var s = App.Backend.Status;
                    string rt = App.Backend.Realtime != null && App.Backend.Realtime.Connected ? "Chat connected" : "Chat reconnecting…";
                    string regions = string.Join("  ", App.Backend.Regions.Select(r => $"{r.Name}: {r.Status}{(App.Backend.RegionPings.TryGetValue(r.Id, out var ms) && ms >= 0 ? $" {ms} ms" : "")}"));
                    _statusLine.text = s == null ? rt : $"{s.PlayersOnline} online · {s.MatchesInProgress} matches · {rt} · {regions}";
                }
            }
        }

        public override bool OnEscape()
        {
            SettingsDialog.Open();
            return true;
        }
    }

    // ====================================================================== social sidebar

    public sealed class SocialPanel
    {
        public VisualElement Root { get; private set; }
        private VisualElement _party, _requests, _friends;
        private TextField _add;
        private GameApp App => GameApp.Instance;

        public VisualElement Build()
        {
            Root = El.Div("panel-thin", "col").Width(300);
            Root.style.marginRight = 6;
            Root.style.marginTop = 8;
            Root.style.marginBottom = 6;
            Root.Add(El.Text("PARTY", "t-subheading"));
            _party = El.Div("col", "mb-m");
            Root.Add(_party);
            Root.Add(El.Div("divider"));
            var addRow = El.Div("row");
            _add = El.Field("", false, "", 32).Grow();
            _add.style.marginBottom = 0;
            addRow.Add(_add);
            addRow.Add(El.Btn("Add", AddFriend, "btn--small"));
            Root.Add(El.Text("FRIENDS", "t-subheading", "mt-m"));
            Root.Add(addRow);
            _requests = El.Div("col");
            Root.Add(_requests);
            var scroll = El.Scroll().Grow();
            _friends = scroll.contentContainer;
            Root.Add(scroll);
            return Root;
        }

        private async void AddFriend()
        {
            var name = _add.value?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            var r = await App.Backend.AddFriend(name);
            if (r.Ok) { App.UI.Toast("Friend request sent to " + name + ".", ToastKind.Success); _add.value = ""; await App.Backend.RefreshFriends(); }
            else App.UI.Toast(r.Message, ToastKind.Error);
        }

        public void Refresh()
        {
            if (Root == null) return;
            var b = App.Backend;
            _party.Clear();
            var party = b.Party;
            if (party == null || party.Members.Count == 0)
            {
                _party.Add(El.Text("You are not in a party. Invite friends from the list below.", "t-small", "t-wrap"));
            }
            else
            {
                bool leader = party.LeaderId == b.MyId;
                foreach (var m in party.Members)
                {
                    var row = El.Div("list-row");
                    row.Add(El.Text((m.Leader ? "♛ " : "") + m.DisplayName, "grow"));
                    row.Add(El.Text("Lv " + m.Level, "t-small"));
                    if (leader && m.AccountId != b.MyId)
                    {
                        var id = m.AccountId;
                        row.Add(El.Btn("✖", async () => { var r = await b.KickFromParty(id); if (!r.Ok) App.UI.Toast(r.Message, ToastKind.Error); }, "btn--icon"));
                    }
                    _party.Add(row);
                }
                _party.Add(El.Btn("Leave Party", async () => await b.LeaveParty(), "btn--small", "btn--ghost"));
            }

            _requests.Clear();
            foreach (var req in b.Friends.Requests.Where(r => r.Incoming))
            {
                var row = El.Div("list-row");
                row.Add(El.Text(req.FromName + " wants to be friends", "t-small", "grow"));
                var id = req.RequestId;
                row.Add(El.Btn("✔", async () => { await b.AcceptFriend(id); await b.RefreshFriends(); }, "btn--icon"));
                row.Add(El.Btn("✖", async () => { await b.DeclineFriend(id); await b.RefreshFriends(); }, "btn--icon", "btn--danger"));
                _requests.Add(row);
            }
            foreach (var inv in b.PartyInvites.ToList())
            {
                var row = El.Div("list-row");
                row.Add(El.Text("Party invite from " + inv.FromName, "t-small", "grow"));
                var id = inv.PartyId;
                row.Add(El.Btn("✔", async () => { var r = await b.AcceptParty(id); if (!r.Ok) App.UI.Toast(r.Message, ToastKind.Error); Refresh(); }, "btn--icon"));
                row.Add(El.Btn("✖", async () => { await b.DeclineParty(id); Refresh(); }, "btn--icon", "btn--danger"));
                _requests.Add(row);
            }

            _friends.Clear();
            var friends = b.Friends.Friends.OrderBy(f => f.Status == PresenceStatus.Offline ? 1 : 0).ThenBy(f => f.DisplayName).ToList();
            if (friends.Count == 0) _friends.Add(El.Text("No friends yet. Add someone by username.", "t-small", "t-wrap", "mt-m"));
            int online = friends.Count(f => f.Status != PresenceStatus.Offline);
            if (friends.Count > 0) _friends.Add(El.Text($"{online} of {friends.Count} online", "t-small", "mb-m"));
            foreach (var f in friends)
            {
                var row = El.Div("list-row");
                row.Add(El.Dot(DotClass(f.Status)));
                var col = El.Div("col", "grow");
                col.Add(El.Text((string.IsNullOrEmpty(f.ClanTag) ? "" : $"[{f.ClanTag}] ") + f.DisplayName, f.Status == PresenceStatus.Offline ? "t-muted" : ""));
                col.Add(El.Text(StatusText(f), "t-small"));
                row.Add(col);
                var friend = f;
                row.RegisterCallback<ClickEvent>(e => FriendMenu(friend));
                _friends.Add(row);
            }
        }

        public static string DotClass(PresenceStatus s) => s switch
        {
            PresenceStatus.Online => "dot--online",
            PresenceStatus.Away => "dot--away",
            PresenceStatus.InGame => "dot--ingame",
            PresenceStatus.FindingMatch => "dot--queue",
            PresenceStatus.InLobby => "dot--lobby",
            _ => "dot--offline",
        };

        private static string StatusText(FriendView f)
        {
            if (!string.IsNullOrEmpty(f.ActivityDetail)) return f.ActivityDetail;
            return f.Status switch
            {
                PresenceStatus.InGame => "In a match",
                PresenceStatus.FindingMatch => "Finding a match",
                PresenceStatus.InLobby => "In a lobby",
                PresenceStatus.Away => "Away",
                PresenceStatus.Online => string.IsNullOrEmpty(f.StatusText) ? "Online" : f.StatusText,
                _ => "Offline",
            };
        }

        private void FriendMenu(FriendView f)
        {
            var b = App.Backend;
            var d = El.Div("dialog", "col").Width(420);
            d.Add(El.Text(f.DisplayName, "t-title", "t-center"));
            d.Add(El.Text($"Level {f.Level} · {El.Pretty(f.Rank ?? "")} · {StatusText(f)}", "t-small", "t-center"));
            d.Add(El.Div("divider"));
            VisualElement ov = null;
            void Act(string label, Action a, string style = "") { var btn = El.Btn(label, () => { App.UI.CloseOverlay(ov); a(); }, style); btn.style.alignSelf = Align.Stretch; d.Add(btn); }
            if (f.Status != PresenceStatus.Offline)
            {
                Act("Whisper", () => { var main = App.UI.Get<MainClientScreen>(); ChatDock.Instance?.StartWhisper(f.AccountId, f.DisplayName); });
                Act("Invite to Party", async () => { var r = await b.InviteToParty(f.AccountId); App.UI.Toast(r.Ok ? "Party invite sent." : r.Message, r.Ok ? ToastKind.Success : ToastKind.Error); });
            }
            Act("View Profile", () => ProfileDialog.Open(f.AccountId));
            if (b.Clan != null) Act("Invite to Clan", async () => { var r = await b.InviteToClan(f.AccountId); App.UI.Toast(r.Ok ? "Clan invite sent." : r.Message, r.Ok ? ToastKind.Success : ToastKind.Error); });
            Act("Remove Friend", () => App.UI.Dialog("Remove Friend", $"Remove {f.DisplayName} from your friends?", ("Remove", "btn--danger", async () => { await b.RemoveFriend(f.AccountId); await b.RefreshFriends(); }), ("Cancel", "btn--ghost", null)), "btn--danger");
            Act("Block", async () => { await b.Block(f.AccountId); await b.RefreshFriends(); App.UI.Toast(f.DisplayName + " blocked.", ToastKind.Info); }, "btn--danger");
            Act("Close", () => { }, "btn--ghost");
            ov = App.UI.ShowOverlay(d);
        }
    }

    // ====================================================================== chat dock

    public sealed class ChatDock
    {
        public static ChatDock Instance { get; private set; }
        public VisualElement Root { get; private set; }
        private VisualElement _tabs;
        private ScrollView _log;
        private TextField _input;
        private string _channel = "global";
        private string _whisperTo, _whisperName;
        private GameApp App => GameApp.Instance;
        private bool _dirty = true;

        public VisualElement Build()
        {
            Instance = this;
            Root = El.Div("panel-thin", "col").Height(230);
            Root.style.marginTop = 6;
            Root.style.marginBottom = 6;
            Root.style.marginRight = 6;
            _tabs = El.Div("tabbar");
            Root.Add(_tabs);
            _log = El.Scroll().Grow();
            Root.Add(_log);
            var row = El.Div("row");
            _input = El.Field("", false, "", 300).Grow();
            _input.style.marginBottom = 0;
            _input.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Send(); e.StopPropagation(); }
            });
            row.Add(_input);
            row.Add(El.Btn("Send", Send, "btn--small"));
            Root.Add(row);
            return Root;
        }

        public void StartWhisper(string accountId, string name)
        {
            _whisperTo = accountId;
            _whisperName = name;
            _channel = "whisper";
            _dirty = true;
            _input.Focus();
        }

        private void Send()
        {
            var text = _input.value?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            if (_channel == "whisper" && _whisperTo != null) App.Backend.SendChat(null, text, _whisperTo);
            else App.Backend.SendChat(_channel, text);
            _input.value = "";
            _input.Focus();
        }

        public void OnMessage(ChatMessageView m) => _dirty = true;
        public void Refresh() => _dirty = true;

        public void Tick(float dt)
        {
            if (!_dirty || Root == null || Root.style.display == DisplayStyle.None) return;
            _dirty = false;
            var b = App.Backend;
            _tabs.Clear();
            foreach (var ch in b.Channels.Values.OrderBy(c => c.Id == "global" ? 0 : c.Id.StartsWith("region") ? 1 : 2))
            {
                var id = ch.Id;
                if (id == _channel) ch.Unread = 0;
                var t = El.Tab(ch.Name + (ch.Unread > 0 ? $" ({ch.Unread})" : ""), () => { _channel = id; _dirty = true; }, "btn--small");
                t.style.height = 30; t.style.minWidth = 80;
                t.EnableInClassList("tab--active", id == _channel);
                _tabs.Add(t);
            }
            var w = El.Tab(_whisperName != null ? "@" + _whisperName : "Whispers", () => { _channel = "whisper"; _dirty = true; }, "btn--small");
            w.style.height = 30; w.style.minWidth = 80;
            w.EnableInClassList("tab--active", _channel == "whisper");
            _tabs.Add(w);

            _log.Clear();
            IEnumerable<ChatMessageView> msgs = _channel == "whisper" ? b.Whispers : b.Channels.TryGetValue(_channel, out var c) ? c.Messages : Enumerable.Empty<ChatMessageView>();
            foreach (var m in msgs.Skip(Math.Max(0, msgs.Count() - 120)))
            {
                var line = El.Div("row");
                line.style.flexWrap = Wrap.Wrap;
                line.Add(El.Text(m.SentAt.ToLocalTime().ToString("HH:mm") + " ", "chat-time"));
                if (m.System) line.Add(El.Text(m.Text, "chat-line", "chat-system"));
                else
                {
                    string cls = _channel == "whisper" ? "chat-whisper" : m.Channel != null && m.Channel.StartsWith("party") ? "chat-party" : m.Channel != null && m.Channel.StartsWith("clan") ? "chat-clan" : "";
                    var name = El.Text((string.IsNullOrEmpty(m.ClanTag) ? "" : $"[{m.ClanTag}] ") + m.FromName + (m.ToAccountId != null && m.FromAccountId == b.MyId ? " → " : "") + ": ", "chat-name");
                    var from = m.FromAccountId;
                    var fromName = m.FromName;
                    if (from != b.MyId) name.RegisterCallback<ClickEvent>(_ => StartWhisper(from, fromName));
                    line.Add(name);
                    line.Add(El.Text(m.Text, "chat-line", cls));
                }
                _log.Add(line);
            }
            _log.schedule.Execute(() => _log.scrollOffset = new Vector2(0, float.MaxValue)).StartingIn(10);
        }
    }
}
