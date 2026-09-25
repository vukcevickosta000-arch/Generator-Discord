using System;
using System.Collections.Generic;
using System.Linq;
using Bloodfall.Client.Core;
using Bloodfall.Client.Match;
using Bloodfall.Client.UI.Hud;
using Bloodfall.Data;
using Bloodfall.Protocol;
using Bloodfall.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Bloodfall.Client.UI.Screens
{
    /// <summary>
    /// In-match HUD (HoN-style bottom plate): hero portrait and bars, abilities, stats, inventory, gold, minimap,
    /// top bar (score, clock, day/night, hero portraits), kill feed, announcer, chat, shop, scoreboard, game menu.
    /// Everything shown is read from authoritative snapshots; buttons only send orders.
    /// </summary>
    public sealed class HudScreen : UIScreen
    {
        private MatchController _mc;
        private WorldOverlay _overlay;
        private Minimap _minimap;
        private ShopPanel _shop;
        private Scoreboard _scoreboard;
        private VisualElement _plate, _abilityRow, _inventory, _backpack, _stash, _buffs, _topDawn, _topDusk, _killfeed, _chatLog, _deathOverlay, _menu, _targetFrame;
        private VisualElement _portrait, _hpFill, _manaFill, _xpFill;
        private Label _hpText, _manaText, _hpRegen, _manaRegen, _level, _heroName, _gold, _clock, _dawnKills, _duskKills, _dayNight, _stats, _error, _announce, _announceSub, _pregame, _deathText, _targetName, _targetHp, _paused, _endBanner;
        private Button _buyback, _shopBtn;
        private TextField _chatInput;
        private bool _chatTeam = true;
        private float _errorTimer, _announceTimer;
        private string _abilityKey = "";
        private readonly List<SlotView> _abilitySlots = new List<SlotView>();
        private readonly List<SlotView> _itemSlots = new List<SlotView>();
        private int _dragFrom = -1;
        private int _chatCount;
        private float _slowTick;

        private sealed class SlotView
        {
            public VisualElement Root, Icon, CdOverlay, Pips;
            public Label Cd, Key, Cost, Charges;
            public Button LevelUp;
            public int Index;
            public string Id;
        }

        protected override void OnBuild(VisualElement root)
        {
            root.pickingMode = PickingMode.Ignore;
            _overlay = new WorldOverlay(App.Settings);
            root.Add(_overlay.Root);

            BuildTopBar(root);
            BuildKillFeed(root);
            BuildAnnouncer(root);
            BuildBottom(root);
            BuildChat(root);
            BuildDeathOverlay(root);

            _shop = new ShopPanel();
            root.Add(_shop.Build());
            _scoreboard = new Scoreboard();
            root.Add(_scoreboard.Build());

            _targetFrame = El.Div("panel-thin", "col");
            _targetFrame.style.position = Position.Absolute;
            _targetFrame.style.top = 96;
            _targetFrame.style.left = Length.Percent(50);
            _targetFrame.style.width = 260;
            _targetFrame.style.marginLeft = -130;
            _targetFrame.pickingMode = PickingMode.Ignore;
            _targetName = El.Text("", "t-heading", "t-center");
            _targetHp = El.Text("", "t-small", "t-center");
            _targetFrame.Add(_targetName);
            _targetFrame.Add(_targetHp);
            _targetFrame.Show(false);
            root.Add(_targetFrame);

            _paused = El.Text("PAUSED", "announcer");
            _paused.style.position = Position.Absolute;
            _paused.style.top = Length.Percent(40);
            _paused.style.left = 0; _paused.style.right = 0;
            _paused.pickingMode = PickingMode.Ignore;
            _paused.Show(false);
            root.Add(_paused);

            _endBanner = El.Text("", "announcer");
            _endBanner.style.position = Position.Absolute;
            _endBanner.style.top = Length.Percent(35);
            _endBanner.style.left = 0; _endBanner.style.right = 0;
            _endBanner.style.fontSize = 110;
            _endBanner.pickingMode = PickingMode.Ignore;
            _endBanner.Show(false);
            root.Add(_endBanner);
        }

        // ------------------------------------------------------------------ build

        private void BuildTopBar(VisualElement root)
        {
            var bar = El.Div("row", "hud-plate");
            bar.style.position = Position.Absolute;
            bar.style.top = 0;
            bar.style.left = Length.Percent(50);
            bar.style.width = 1000;
            bar.style.marginLeft = -500;
            bar.style.height = 84;
            bar.style.paddingTop = 8; bar.style.paddingBottom = 8;
            bar.style.justifyContent = Justify.Center;
            bar.pickingMode = PickingMode.Ignore;
            _topDawn = El.Div("row").Width(360);
            _topDawn.style.justifyContent = Justify.FlexEnd;
            var center = El.Div("col", "center").Width(220);
            var score = El.Div("row", "center");
            _dawnKills = El.Text("0", "t-title", "t-gold").Width(60);
            _dawnKills.AddToClassList("t-right");
            _clock = El.Text("0:00", "t-title", "t-center").Width(90);
            _duskKills = El.Text("0", "t-title", "t-red").Width(60);
            score.Add(_dawnKills); score.Add(_clock); score.Add(_duskKills);
            center.Add(score);
            _dayNight = El.Text("", "t-small", "t-center");
            center.Add(_dayNight);
            _topDusk = El.Div("row").Width(360);
            bar.Add(_topDawn); bar.Add(center); bar.Add(_topDusk);
            root.Add(bar);

            _pregame = El.Text("", "announcer-sub");
            _pregame.style.position = Position.Absolute;
            _pregame.style.top = 92;
            _pregame.style.left = 0; _pregame.style.right = 0;
            _pregame.pickingMode = PickingMode.Ignore;
            root.Add(_pregame);

            var menuBtn = El.Btn("☰", ToggleMenu, "btn--icon");
            menuBtn.style.position = Position.Absolute;
            menuBtn.style.right = 8;
            menuBtn.style.top = 8;
            root.Add(menuBtn);
        }

        private void BuildKillFeed(VisualElement root)
        {
            _killfeed = El.Div("col");
            _killfeed.style.position = Position.Absolute;
            _killfeed.style.right = 12;
            _killfeed.style.top = 60;
            _killfeed.style.width = 380;
            _killfeed.pickingMode = PickingMode.Ignore;
            root.Add(_killfeed);
        }

        private void BuildAnnouncer(VisualElement root)
        {
            var col = El.Div("col");
            col.style.position = Position.Absolute;
            col.style.top = Length.Percent(18);
            col.style.left = 0; col.style.right = 0;
            col.pickingMode = PickingMode.Ignore;
            _announce = El.Text("", "announcer");
            _announceSub = El.Text("", "announcer-sub");
            col.Add(_announce);
            col.Add(_announceSub);
            root.Add(col);
            _error = El.Text("", "t-heading", "t-red", "t-center");
            _error.style.position = Position.Absolute;
            _error.style.bottom = 250;
            _error.style.left = 0; _error.style.right = 0;
            _error.style.unityTextOutlineWidth = 1;
            _error.style.unityTextOutlineColor = Color.black;
            _error.pickingMode = PickingMode.Ignore;
            root.Add(_error);
        }

        private void BuildBottom(VisualElement root)
        {
            _minimap = new Minimap();
            var mm = _minimap.Build();
            mm.style.position = Position.Absolute;
            mm.style.bottom = 0;
            if (App.Settings.MinimapOnLeft) mm.style.left = 0; else mm.style.right = 0;
            root.Add(mm);

            _plate = El.Div("hud-plate", "row");
            _plate.style.position = Position.Absolute;
            _plate.style.bottom = 0;
            _plate.style.left = Length.Percent(50);
            _plate.style.width = 1150;
            _plate.style.marginLeft = -560;
            _plate.style.height = 210;
            _plate.style.alignItems = Align.FlexEnd;

            // Portrait + level.
            var left = El.Div("col", "center").Width(150);
            _portrait = El.Div("portrait", "portrait--dawn").Size(118, 140);
            _level = El.Text("1", "badge");
            _level.style.position = Position.Absolute;
            _level.style.bottom = -6;
            _level.style.right = -6;
            _level.style.fontSize = 16;
            _portrait.Add(_level);
            left.Add(_portrait);
            _heroName = El.Text("", "t-small", "t-center");
            left.Add(_heroName);
            _plate.Add(left);

            // Center: buffs, bars, abilities.
            var center = El.Div("col").Width(470);
            _buffs = El.Div("row").Height(30);
            center.Add(_buffs);
            var (xpBar, xpFill, _) = El.Bar("bar-fill--xp", 6);
            _xpFill = xpFill;
            center.Add(xpBar);
            var (hpBar, hpFill, hpText) = El.Bar("bar-fill--hp", 22);
            _hpFill = hpFill; _hpText = hpText;
            _hpRegen = El.Text("", "bar-regen");
            hpBar.Add(_hpRegen);
            center.Add(hpBar);
            var (manaBar, manaFill, manaText) = El.Bar("bar-fill--mana", 16);
            _manaFill = manaFill; _manaText = manaText;
            _manaRegen = El.Text("", "bar-regen");
            manaBar.Add(_manaRegen);
            manaBar.style.marginTop = 2;
            center.Add(manaBar);
            _abilityRow = El.Div("row", "center", "mt-m");
            _abilityRow.style.height = 76;
            center.Add(_abilityRow);
            _plate.Add(center);

            // Stats.
            _stats = El.Text("", "t-small").Width(120);
            _stats.style.marginLeft = 8;
            _stats.style.alignSelf = Align.Center;
            _plate.Add(_stats);

            // Inventory + gold.
            var right = El.Div("col").Width(210);
            _inventory = El.Div("row");
            _inventory.style.flexWrap = Wrap.Wrap;
            _inventory.style.width = 168;
            for (int i = 0; i < 6; i++) _inventory.Add(CreateItemSlot(i, false));
            _backpack = El.Div("row");
            for (int i = 6; i < 9; i++) _backpack.Add(CreateItemSlot(i, true));
            right.Add(_inventory);
            right.Add(_backpack);
            var goldRow = El.Div("row");
            _gold = El.Text("0", "t-title", "t-gold").Width(90);
            goldRow.Add(_gold);
            _shopBtn = El.Btn("Shop", () => _shop.Toggle(), "btn--small");
            goldRow.Add(_shopBtn);
            right.Add(goldRow);
            _plate.Add(right);

            _stash = El.Div("row", "panel-thin");
            _stash.style.position = Position.Absolute;
            _stash.style.right = 0;
            _stash.style.top = -64;
            _stash.Add(El.Text("STASH", "t-label", "mr-m"));
            for (int i = 10; i < 16; i++) _stash.Add(CreateItemSlot(i, true));
            _plate.Add(_stash);

            root.Add(_plate);
        }

        private VisualElement CreateItemSlot(int index, bool small)
        {
            var sv = new SlotView { Index = index };
            var slot = El.Div("hud-slot", "hud-slot--item");
            if (small) { slot.style.width = 38; slot.style.height = 32; }
            sv.Root = slot;
            sv.Icon = El.Div("icon", "fill");
            sv.Icon.pickingMode = PickingMode.Ignore;
            slot.Add(sv.Icon);
            sv.CdOverlay = El.Div();
            sv.CdOverlay.style.position = Position.Absolute;
            sv.CdOverlay.style.left = 0; sv.CdOverlay.style.right = 0; sv.CdOverlay.style.bottom = 0;
            sv.CdOverlay.style.backgroundColor = new Color(0, 0, 0, 0.65f);
            sv.CdOverlay.pickingMode = PickingMode.Ignore;
            slot.Add(sv.CdOverlay);
            sv.Cd = El.Text("", "hud-cd");
            sv.Cd.style.fontSize = 14;
            sv.Cd.pickingMode = PickingMode.Ignore;
            slot.Add(sv.Cd);
            sv.Charges = El.Text("", "hud-cost");
            sv.Charges.style.color = Color.white;
            sv.Charges.pickingMode = PickingMode.Ignore;
            slot.Add(sv.Charges);
            if (index < 6)
            {
                string[] binds = { KeyBinds.Item1, KeyBinds.Item2, KeyBinds.Item3, KeyBinds.Item4, KeyBinds.Item5, KeyBinds.Item6 };
                sv.Key = El.Text(KeyName(KeyBinds.Get(App.Settings, binds[index])), "hud-key");
                sv.Key.pickingMode = PickingMode.Ignore;
                slot.Add(sv.Key);
            }
            slot.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0) { _dragFrom = index; }
                else if (e.button == 1) ItemContext(index);
            });
            slot.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0) return;
                var hero = _mc?.LocalHero;
                if (_dragFrom >= 0 && _dragFrom != index && hero != null) _mc.SendOrder(Order.Swap(hero.Id, _dragFrom, index));
                else if (_dragFrom == index && index < 6) _mc.World?.Input?.UseItem(index);
                _dragFrom = -1;
            });
            UI.AttachTooltip(slot, () =>
            {
                var me = _mc?.Client.Latest?.Me;
                if (me == null || index >= me.Items.Length || string.IsNullOrEmpty(me.Items[index].Id) || !App.Data.Items.TryGetValue(me.Items[index].Id, out var d)) return null;
                var c = ItemTooltip.Build(App.Data, d);
                c.Add(El.Text($"Sells for {me.Items[index].SellValue} gold · right-click for options", "t-small", "mt-m"));
                return c;
            });
            _itemSlots.Add(sv);
            return slot;
        }

        private void ItemContext(int index)
        {
            var me = _mc?.Client.Latest?.Me;
            var hero = _mc?.LocalHero;
            if (me == null || hero == null || index >= me.Items.Length || string.IsNullOrEmpty(me.Items[index].Id)) return;
            var it = me.Items[index];
            string name = App.Data.Items.TryGetValue(it.Id, out var d) ? d.Name : it.Id;
            var buttons = new List<(string, string, Action)>
            {
                ($"Sell ({it.SellValue})", "btn--danger", () => _mc.SendOrder(Order.Sell(hero.Id, index))),
            };
            if (index < 6)
            {
                for (int b = 6; b < 9; b++) if (string.IsNullOrEmpty(me.Items[b].Id)) { int target = b; buttons.Add(("Move to backpack", "", () => _mc.SendOrder(Order.Swap(hero.Id, index, target)))); break; }
            }
            else if (index < 9)
            {
                for (int b = 0; b < 6; b++) if (string.IsNullOrEmpty(me.Items[b].Id)) { int target = b; buttons.Add(("Move to inventory", "", () => _mc.SendOrder(Order.Swap(hero.Id, index, target)))); break; }
            }
            buttons.Add(("Cancel", "btn--ghost", null));
            UI.Dialog(name, "", buttons.ToArray());
        }

        private void BuildChat(VisualElement root)
        {
            var col = El.Div("col");
            col.style.position = Position.Absolute;
            col.style.bottom = 290;
            if (App.Settings.MinimapOnLeft) col.style.left = 12; else col.style.right = 12;
            col.style.width = 520;
            col.pickingMode = PickingMode.Ignore;
            _chatLog = El.Div("col");
            _chatLog.pickingMode = PickingMode.Ignore;
            col.Add(_chatLog);
            _chatInput = El.Field("", false, "", 200);
            _chatInput.style.marginBottom = 0;
            _chatInput.Show(false);
            _chatInput.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    var text = _chatInput.value?.Trim();
                    if (!string.IsNullOrEmpty(text)) _mc?.SendChat(text, _chatTeam);
                    CloseChat();
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.Escape) { CloseChat(); e.StopPropagation(); }
            });
            col.Add(_chatInput);
            root.Add(col);
        }

        private void BuildDeathOverlay(VisualElement root)
        {
            _deathOverlay = El.Div("fill", "col", "center");
            _deathOverlay.style.backgroundColor = new Color(0.12f, 0f, 0f, 0.28f);
            _deathOverlay.pickingMode = PickingMode.Ignore;
            _deathText = El.Text("", "announcer-sub");
            _deathOverlay.Add(_deathText);
            _buyback = El.Btn("Buyback", () => { var h = _mc?.LocalHero; if (h != null) _mc.SendOrder(new Order { Type = OrderType.Buyback, UnitId = h.Id }); }, "btn--primary", "mt-m");
            _deathOverlay.Add(_buyback);
            _deathOverlay.Show(false);
            root.Add(_deathOverlay);
        }

        private static string KeyName(KeyCode k)
        {
            var s = k.ToString();
            if (s.StartsWith("Alpha")) return s.Substring(5);
            return s.Length > 3 ? s.Substring(0, 3) : s;
        }

        // ------------------------------------------------------------------ bind

        public void Bind(MatchController mc)
        {
            _mc = mc;
            _overlay.Bind(mc.World);
            _minimap.Bind(mc.World);
            _shop.Bind(mc);
            _abilityKey = "";
            mc.EventReceived -= OnEvent;
            mc.EventReceived += OnEvent;
            if (mc.World?.Input != null)
            {
                mc.World.Input.LocalError -= ShowError;
                mc.World.Input.LocalError += ShowError;
            }
            _endBanner.Show(false);
            _chatCount = 0;
        }

        public override void OnHide()
        {
            if (_mc != null) _mc.EventReceived -= OnEvent;
            _overlay.Clear();
        }

        // ------------------------------------------------------------------ events

        private void OnEvent(NetEvent e)
        {
            var world = _mc.World;
            if (world == null) return;
            bool numbers = App.Settings.ShowDamageNumbers;
            switch (e.Type)
            {
                case SimEventType.Damage:
                {
                    if (!numbers || e.Value < 1f || !world.TryGetView(e.UnitId, out var v)) break;
                    var hero = _mc.LocalHero;
                    bool involvesMe = hero != null && (e.UnitId == hero.Id || e.OtherId == hero.Id);
                    bool crit = (e.Flags & SimEvent.FlagCrit) != 0;
                    if (!involvesMe && !crit) break;
                    string cls = crit ? "floating--crit" : (e.Flags & SimEvent.FlagPure) != 0 ? "floating--pure" : (e.Flags & SimEvent.FlagMagical) != 0 ? "floating--magic" : "";
                    _overlay.Float(v.Point(1f), Mathf.RoundToInt(e.Value) + (crit ? "!" : ""), cls, crit ? 1.3f : 0.9f);
                    break;
                }
                case SimEventType.Heal:
                    if (numbers && e.Value >= 15f && world.TryGetView(e.UnitId, out var hv) && hv.State?.OwnerPlayer == world.LocalPlayerId) _overlay.Float(hv.Point(1f), "+" + Mathf.RoundToInt(e.Value), "floating--heal");
                    break;
                case SimEventType.LastHitGold:
                    _overlay.Float(world.Map.World(e.Point, 2.2f), "+" + Mathf.RoundToInt(e.Value), "floating--gold", 1.4f);
                    break;
                case SimEventType.Deny:
                    _overlay.Float(world.Map.World(e.Point, 2.2f), "!", "floating--deny", 1.2f);
                    break;
                case SimEventType.Miss:
                    if (world.TryGetView(e.UnitId, out var mv)) _overlay.Float(mv.Point(1f), "miss", "floating--miss", 0.8f);
                    break;
                case SimEventType.Error:
                    ShowError(e.Key);
                    App.Audio.PlayUi("error");
                    break;
                case SimEventType.KillFeed:
                    AddKillFeed(e);
                    break;
                case SimEventType.Announcer:
                    Announce(e);
                    break;
                case SimEventType.Ping:
                    _minimap.Ping(new Vector2(e.Point.X, e.Point.Y), e.Team == Team.Dawn ? ModelFactory.DawnGlow : new Color(1f, 0.3f, 0.3f));
                    break;
                case SimEventType.PlayerConnection:
                {
                    var conn = (PlayerConnection)(int)e.Value;
                    AddSystemChat(conn == PlayerConnection.Disconnected ? $"{e.Key} disconnected." : conn == PlayerConnection.Abandoned ? $"{e.Key} abandoned the match. A bot takes over their hero." : $"{e.Key} connected.");
                    break;
                }
            }
        }

        private void ShowError(string msg)
        {
            _error.text = msg;
            _errorTimer = 2.2f;
        }

        private static readonly Dictionary<string, string> AnnounceText = new Dictionary<string, string>
        {
            [AnnouncerKeys.FirstBlood] = "FIRST BLOOD", [AnnouncerKeys.DoubleKill] = "DOUBLE KILL", [AnnouncerKeys.TripleKill] = "TRIPLE KILL",
            [AnnouncerKeys.QuadKill] = "QUAD KILL", [AnnouncerKeys.Annihilation] = "ANNIHILATION", [AnnouncerKeys.Shutdown] = "SHUTDOWN",
            [AnnouncerKeys.TeamWipe] = "TEAM WIPE", [AnnouncerKeys.TowerDestroyedAlly] = "YOUR TOWER HAS FALLEN", [AnnouncerKeys.TowerDestroyedEnemy] = "ENEMY TOWER DESTROYED",
            [AnnouncerKeys.BarracksDestroyedAlly] = "YOUR BARRACKS HAVE FALLEN", [AnnouncerKeys.BarracksDestroyedEnemy] = "ENEMY BARRACKS DESTROYED",
            [AnnouncerKeys.BattleBegins] = "THE BATTLE BEGINS", [AnnouncerKeys.CreepsSpawned] = "", [AnnouncerKeys.Nightfall] = "NIGHT FALLS",
            [AnnouncerKeys.Daybreak] = "DAYBREAK", [AnnouncerKeys.MegaCreeps] = "MEGA CREEPS", [AnnouncerKeys.Denied] = "DENIED",
            [AnnouncerKeys.VharothTremor] = "THE EARTH TREMBLES", [AnnouncerKeys.VharothSealBroken] = "A SEAL IS BROKEN",
            [AnnouncerKeys.VharothAwakened] = "VHAROTH AWAKENS", [AnnouncerKeys.VharothBloodMoon] = "BLOOD MOON", [AnnouncerKeys.VharothSlain] = "VHAROTH HAS FALLEN",
            [AnnouncerKeys.PlayerAbandoned] = "", [AnnouncerKeys.PlayerDisconnected] = "", [AnnouncerKeys.PlayerReconnected] = "",
        };

        private static readonly string[] StreakNames = { "", "", "", "KILLING SPREE", "DOMINATING", "MEGA KILL", "UNSTOPPABLE", "WICKED SICK", "MONSTER KILL", "GODLIKE", "BEYOND GODLIKE" };

        private void Announce(NetEvent e)
        {
            string key = e.Key ?? "";
            string text;
            if (key.StartsWith(AnnouncerKeys.StreakPrefix) && int.TryParse(key.Substring(AnnouncerKeys.StreakPrefix.Length), out int n)) text = StreakNames[Mathf.Clamp(n, 0, StreakNames.Length - 1)];
            else if (!AnnounceText.TryGetValue(key, out text)) text = El.Pretty(key).ToUpperInvariant();
            if (string.IsNullOrEmpty(text)) return;
            string sub = "";
            var pl = _mc.Players.FirstOrDefault(p => p.Id == e.OtherId);
            if (pl != null && (key == AnnouncerKeys.FirstBlood || key.StartsWith(AnnouncerKeys.StreakPrefix) || key.Contains("kill") || key == AnnouncerKeys.Shutdown || key == AnnouncerKeys.Annihilation)) sub = pl.Name;
            _announce.text = text;
            _announceSub.text = sub;
            _announceTimer = 3.2f;
        }

        private void AddKillFeed(NetEvent e)
        {
            var world = _mc.World;
            string victimName = Name(e.UnitId), killerName = e.OtherId != 0 ? Name(e.OtherId) : "The Ruins";
            var killerTeam = e.Team == Team.Dawn ? Team.Dusk : Team.Dawn;
            var row = El.Div("killfeed-row", killerTeam == Team.Dawn ? "killfeed-row--dawn" : "killfeed-row--dusk");
            row.pickingMode = PickingMode.Ignore;
            row.Add(El.Text(killerName, killerTeam == Team.Dawn ? "t-gold" : "t-red"));
            row.Add(El.Text("  ⚔  ", "t-muted"));
            row.Add(El.Text(victimName, e.Team == Team.Dawn ? "t-gold" : "t-red"));
            int assists = string.IsNullOrEmpty(e.Key) ? 0 : e.Key.Split(',').Length;
            if (assists > 0) row.Add(El.Text($"  +{assists}", "t-small"));
            _killfeed.Insert(0, row);
            while (_killfeed.childCount > 6) _killfeed.RemoveAt(_killfeed.childCount - 1);
            row.schedule.Execute(() => row.RemoveFromHierarchy()).StartingIn(12000);
        }

        private string Name(int unitId)
        {
            var world = _mc.World;
            if (world != null && world.TryGetView(unitId, out var v))
            {
                if (v.IsHero)
                {
                    var p = _mc.Players.FirstOrDefault(pl => pl.HeroUnitId == unitId);
                    return (p?.Name ?? "") + (v.Hero != null ? " (" + v.Hero.Name + ")" : "");
                }
                return v.Unit?.Name ?? El.Pretty(v.DefId);
            }
            var pp = _mc.Players.FirstOrDefault(pl => pl.HeroUnitId == unitId);
            return pp?.Name ?? "Unknown";
        }

        private void AddSystemChat(string text)
        {
            var l = El.Text(text, "chat-line", "chat-system");
            l.pickingMode = PickingMode.Ignore;
            AppendChat(l);
        }

        private void AppendChat(VisualElement l)
        {
            _chatLog.Add(l);
            while (_chatLog.childCount > 8) _chatLog.RemoveAt(0);
            l.schedule.Execute(() => { if (!_chatInput.visible || _chatInput.style.display == DisplayStyle.None) l.style.opacity = 0.0f; }).StartingIn(12000);
        }

        // ------------------------------------------------------------------ chat & menu

        private void OpenChat(bool team)
        {
            _chatTeam = team;
            _chatInput.label = team ? "[Team]" : "[All]";
            _chatInput.Show(true);
            _chatInput.value = "";
            _chatInput.Focus();
            foreach (var c in _chatLog.Children()) c.style.opacity = 1f;
            if (_mc?.World?.Input != null) _mc.World.Input.KeyboardCaptured = true;
        }

        private void CloseChat()
        {
            _chatInput.Show(false);
            _chatInput.Blur();
            if (_mc?.World?.Input != null) _mc.World.Input.KeyboardCaptured = false;
        }

        private void ToggleMenu()
        {
            if (_menu != null) { UI.CloseOverlay(_menu); _menu = null; if (_mc != null && !_mc.Online) _mc.Paused = false; return; }
            var d = El.Div("dialog", "col").Width(420);
            d.Add(El.Text("Game Menu", "t-title", "t-center"));
            d.Add(El.Div("divider"));
            void B(string label, Action a, string style = "") { var b = El.Btn(label, a, style); b.style.alignSelf = Align.Stretch; d.Add(b); }
            B("Resume", ToggleMenu, "btn--primary");
            B("Settings", () => SettingsDialog.Open());
            if (_mc != null && !_mc.Online) B(_mc.Paused ? "Unpause" : "Pause", () => { _mc.Paused = !_mc.Paused; ToggleMenu(); ToggleMenu(); });
            if (_mc != null && _mc.Online) B("Vote to Concede (-ff)", () => { _mc.SendChat("-ff", true); ToggleMenu(); });
            B("Leave Match", () => UI.Dialog("Leave Match", _mc != null && _mc.Online && !_mc.Ended
                ? "The match is still running. Leaving now counts as an abandon; your hero is handed to a bot."
                : "Leave this match?", ("Leave", "btn--danger", () => _mc?.Leave()), ("Stay", "btn--ghost", null)), "btn--danger");
            if (_mc != null && !_mc.Online) _mc.Paused = true;
            _menu = UI.ShowOverlay(d, ToggleMenu);
        }

        public override bool OnEscape()
        {
            var input = _mc?.World?.Input;
            if (input != null && input.Targeting) { input.CancelTargeting(); return true; }
            if (_shop.IsOpen) { _shop.Toggle(); return true; }
            ToggleMenu();
            return true;
        }

        // ------------------------------------------------------------------ per-frame

        public override void Tick(float dt)
        {
            if (_mc == null || _mc.World == null) return;
            var client = _mc.Client;
            var frame = client.Latest;
            var world = _mc.World;
            _overlay.Update(dt, world.Camera?.Cam);
            _minimap.Update(dt);
            _shop.Tick();
            _paused.Show(_mc.Paused);

            // Keys handled by the HUD (not orders).
            bool typing = world.Input != null && world.Input.KeyboardCaptured;
            if (!typing)
            {
                if (Input.InputBridge.GetKeyDown(KeyBinds.Get(App.Settings, KeyBinds.Chat))) OpenChat(!Input.InputBridge.Shift);
                if (Input.InputBridge.GetKeyDown(KeyBinds.Get(App.Settings, KeyBinds.Shop))) _shop.Toggle();
                if (Input.InputBridge.GetKeyDown(KeyBinds.Get(App.Settings, KeyBinds.Menu))) ToggleMenu();
                _scoreboard.Show(Input.InputBridge.GetKey(KeyBinds.Get(App.Settings, KeyBinds.Scoreboard)), _mc);
            }

            if (_errorTimer > 0) { _errorTimer -= dt; _error.style.opacity = Mathf.Clamp01(_errorTimer * 2f); }
            else _error.text = "";
            if (_announceTimer > 0) { _announceTimer -= dt; float a = Mathf.Clamp01(_announceTimer * 1.5f); _announce.style.opacity = a; _announceSub.style.opacity = a; }
            else { _announce.text = ""; _announceSub.text = ""; }

            if (client.ChatLog.Count != _chatCount)
            {
                for (int i = Mathf.Max(_chatCount, client.ChatLog.Count - 8); i < client.ChatLog.Count; i++)
                {
                    var m = client.ChatLog[i];
                    var line = El.Div("row");
                    line.pickingMode = PickingMode.Ignore;
                    if (m.PlayerId < 0) line.Add(El.Text(m.Text, "chat-line", "chat-system"));
                    else
                    {
                        line.Add(El.Text((m.TeamOnly ? "[Team] " : "[All] ") + m.Name + ": ", "chat-name", m.Team == Team.Dawn ? "t-gold" : "t-red"));
                        line.Add(El.Text(m.Text, "chat-line"));
                    }
                    AppendChat(line);
                }
                _chatCount = client.ChatLog.Count;
            }

            if (_mc.Ended && world.EndResult != null && _endBanner.style.display == DisplayStyle.None)
            {
                bool won = world.EndResult.Players.Any(p => p.AccountId == _mc.LocalAccountId && p.Won);
                _endBanner.text = won ? "VICTORY" : "DEFEAT";
                _endBanner.EnableInClassList("t-gold", won);
                _endBanner.Show(true);
            }

            if (frame == null) return;
            UpdateTopBar(frame);
            UpdateHeroPanel(frame);
            UpdateTargetFrame();
        }

        private void UpdateTopBar(SnapshotFrame frame)
        {
            _dawnKills.text = frame.TeamKills[0].ToString();
            _duskKills.text = frame.TeamKills[1].ToString();
            _clock.text = El.FormatTime(frame.Time);
            _dayNight.text = (frame.IsNight ? "☾ Night" : "☀ Day") + " · " + El.FormatTime(frame.DayNightRemaining);
            _pregame.text = frame.Time < 0 ? "The battle begins in " + Mathf.CeilToInt(-frame.Time) : "";
            _slowTick -= Time.unscaledDeltaTime;
            if (_slowTick > 0) return;
            _slowTick = 0.25f;
            FillPortraits(_topDawn, frame, Team.Dawn);
            FillPortraits(_topDusk, frame, Team.Dusk);
        }

        private void FillPortraits(VisualElement host, SnapshotFrame frame, Team team)
        {
            host.Clear();
            foreach (var p in frame.Players.Where(p => p.Team == team).OrderBy(p => p.Slot))
            {
                var tile = El.Div("portrait", team == Team.Dawn ? "portrait--dawn" : "portrait--dusk").Size(62, 62);
                tile.style.marginLeft = 3; tile.style.marginRight = 3;
                if (p.HeroId != null && App.Data.Heroes.TryGetValue(p.HeroId, out var hd)) El.SetImage(tile, GameText.PortraitPath(hd));
                if (p.RespawnIn > 0)
                {
                    tile.style.unityBackgroundImageTintColor = new Color(0.35f, 0.3f, 0.3f);
                    var t = El.Text(Mathf.CeilToInt(p.RespawnIn).ToString(), "hud-cd");
                    t.pickingMode = PickingMode.Ignore;
                    tile.Add(t);
                }
                var lvl = El.Text(p.Level.ToString(), "hud-key");
                lvl.pickingMode = PickingMode.Ignore;
                tile.Add(lvl);
                if (p.Connection == PlayerConnection.Disconnected || p.Connection == PlayerConnection.Abandoned)
                {
                    var dc = El.Text(p.Connection == PlayerConnection.Abandoned ? "✖" : "DC", "hud-cost", "hud-cost--hp");
                    tile.Add(dc);
                }
                string tip = $"{p.Name} · Level {p.Level} · {p.Kills}/{p.Deaths}/{p.Assists}";
                UI.AttachTooltip(tile, "Player", tip);
                host.Add(tile);
            }
        }

        private void UpdateHeroPanel(SnapshotFrame frame)
        {
            var me = frame.Me;
            var heroState = _mc.LocalHero;
            var lp = _mc.LocalPlayer;
            _plate.Show(heroState != null && me != null);
            if (heroState == null || me == null) { _deathOverlay.Show(false); return; }
            if (lp?.HeroId != null && App.Data.Heroes.TryGetValue(lp.HeroId, out var hd))
            {
                if (_portrait.userData as string != hd.Id) { El.SetImage(_portrait, GameText.PortraitPath(hd)); _portrait.userData = hd.Id; _heroName.text = hd.Name; }
            }
            _portrait.EnableInClassList("portrait--dusk", heroState.Team == Team.Dusk);
            _level.text = heroState.Level.ToString();
            El.SetFill(_hpFill, heroState.MaxHp > 0 ? heroState.Hp / heroState.MaxHp : 0);
            _hpText.text = $"{Mathf.CeilToInt(heroState.Hp)} / {Mathf.CeilToInt(heroState.MaxHp)}";
            El.SetFill(_manaFill, heroState.MaxMana > 0 ? heroState.Mana / heroState.MaxMana : 0);
            _manaText.text = $"{Mathf.FloorToInt(heroState.Mana)} / {Mathf.FloorToInt(heroState.MaxMana)}";
            int span = Mathf.Max(1, me.XpNextLevel - me.XpLevelStart);
            El.SetFill(_xpFill, Mathf.Clamp01((me.Xp - me.XpLevelStart) / (float)span));
            _gold.text = me.Gold.ToString();
            _stats.text = $"⚔ {heroState.Damage:0}\n⛨ {heroState.Armor:0.#}\n➶ {heroState.MoveSpeed:0.00} m/s\n⏱ {heroState.AttackTime:0.00}s\n{(me.AbilityPoints > 0 ? $"<color=#E2BA6E>{me.AbilityPoints} point(s)</color>" : "")}";
            _stash.Show(me.Items.Skip(10).Take(6).Any(i => !string.IsNullOrEmpty(i.Id)) || me.AtBase);
            UpdateAbilities(me, heroState);
            UpdateItems(me);
            UpdateBuffs(heroState);

            bool dead = heroState.Has(EntityFlags.Dead);
            _deathOverlay.Show(dead);
            if (dead)
            {
                _deathText.text = $"You have been slain. Respawning in {Mathf.CeilToInt(lp?.RespawnIn ?? 0)}";
                _buyback.text = me.BuybackCooldown > 0 ? $"Buyback ({Mathf.CeilToInt(me.BuybackCooldown)}s)" : $"Buyback ({me.BuybackCost} gold)";
                _buyback.SetEnabled(me.BuybackCooldown <= 0 && me.Gold >= me.BuybackCost);
            }
        }

        private void UpdateAbilities(PrivateState me, EntityState hero)
        {
            string key = string.Join(",", me.Abilities.Select(a => a.Id));
            if (key != _abilityKey)
            {
                _abilityKey = key;
                _abilityRow.Clear();
                _abilitySlots.Clear();
                for (int i = 0; i < me.Abilities.Length; i++)
                {
                    if (!App.Data.Abilities.TryGetValue(me.Abilities[i].Id ?? "", out var def) || def.Hidden) continue;
                    _abilityRow.Add(CreateAbilitySlot(i, def));
                }
            }
            foreach (var sv in _abilitySlots)
            {
                if (sv.Index >= me.Abilities.Length) continue;
                var a = me.Abilities[sv.Index];
                if (!App.Data.Abilities.TryGetValue(a.Id ?? "", out var def)) continue;
                bool learned = a.Level > 0;
                bool passive = def.Targeting == TargetingMode.Passive;
                float cd = a.Cooldown;
                bool noMana = learned && !passive && hero.Mana + 0.01f < a.ManaCost;
                sv.Root.EnableInClassList("hud-slot--disabled", !learned || noMana);
                sv.Icon.style.unityBackgroundImageTintColor = !learned ? new Color(0.35f, 0.35f, 0.35f) : noMana ? new Color(0.45f, 0.55f, 1f) : Color.white;
                if (cd > 0.05f && a.CooldownTotal > 0)
                {
                    sv.CdOverlay.style.height = Length.Percent(Mathf.Clamp01(cd / a.CooldownTotal) * 100f);
                    sv.Cd.text = cd >= 1f ? Mathf.CeilToInt(cd).ToString() : cd.ToString("0.0");
                }
                else { sv.CdOverlay.style.height = 0; sv.Cd.text = ""; }
                if (sv.Cost != null) sv.Cost.text = a.ManaCost > 0 ? Mathf.RoundToInt(a.ManaCost).ToString() : a.HealthCost > 0 ? Mathf.RoundToInt(a.HealthCost) + "hp" : "";
                sv.Cost?.EnableInClassList("hud-cost--hp", a.ManaCost <= 0 && a.HealthCost > 0);
                sv.LevelUp.Show(a.CanLevel && me.AbilityPoints > 0);
                sv.Pips.Clear();
                for (int l = 0; l < def.MaxLevel; l++) sv.Pips.Add(El.Div("hud-pip", l < a.Level ? "hud-pip--on" : ""));
            }
        }

        private VisualElement CreateAbilitySlot(int index, AbilityDef def)
        {
            var sv = new SlotView { Index = index, Id = def.Id };
            var slot = El.Div("hud-slot", def.IsUltimate ? "hud-slot--ult" : "");
            sv.Root = slot;
            sv.Icon = El.Div("icon");
            sv.Icon.style.position = Position.Absolute;
            sv.Icon.style.left = 5; sv.Icon.style.top = 5; sv.Icon.style.right = 5; sv.Icon.style.bottom = 5;
            sv.Icon.pickingMode = PickingMode.Ignore;
            El.SetImage(sv.Icon, GameText.AbilityIconPath(def));
            slot.Add(sv.Icon);
            sv.CdOverlay = El.Div();
            sv.CdOverlay.style.position = Position.Absolute;
            sv.CdOverlay.style.left = 5; sv.CdOverlay.style.right = 5; sv.CdOverlay.style.bottom = 5;
            sv.CdOverlay.style.backgroundColor = new Color(0, 0, 0, 0.65f);
            sv.CdOverlay.pickingMode = PickingMode.Ignore;
            slot.Add(sv.CdOverlay);
            sv.Cd = El.Text("", "hud-cd");
            sv.Cd.pickingMode = PickingMode.Ignore;
            slot.Add(sv.Cd);
            string bind = def.Slot switch
            {
                AbilitySlot.Q => KeyBinds.AbilityQ, AbilitySlot.W => KeyBinds.AbilityW, AbilitySlot.E => KeyBinds.AbilityE, AbilitySlot.R => KeyBinds.AbilityR,
                AbilitySlot.Extra1 => KeyBinds.AbilityD, AbilitySlot.Extra2 => KeyBinds.AbilityF, _ => null,
            };
            sv.Key = El.Text(bind != null ? KeyName(KeyBinds.Get(App.Settings, bind)) : "", "hud-key");
            sv.Key.pickingMode = PickingMode.Ignore;
            slot.Add(sv.Key);
            sv.Cost = El.Text("", "hud-cost");
            sv.Cost.pickingMode = PickingMode.Ignore;
            slot.Add(sv.Cost);
            sv.Pips = El.Div("hud-pips");
            sv.Pips.pickingMode = PickingMode.Ignore;
            slot.Add(sv.Pips);
            sv.LevelUp = El.Btn("+", () => { var h = _mc.LocalHero; if (h != null) _mc.SendOrder(Order.LevelUp(h.Id, index)); }, "hud-levelup");
            sv.LevelUp.style.minWidth = 0;
            sv.LevelUp.style.height = 20;
            sv.LevelUp.style.paddingLeft = 0; sv.LevelUp.style.paddingRight = 0;
            sv.LevelUp.style.marginLeft = 0; sv.LevelUp.style.marginRight = 0;
            slot.Add(sv.LevelUp);
            slot.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                if (Input.InputBridge.GetKey(KeyBinds.Get(App.Settings, KeyBinds.LevelUpModifier))) { var h = _mc.LocalHero; if (h != null) _mc.SendOrder(Order.LevelUp(h.Id, index)); }
                else _mc.World?.Input?.BeginAbility(index);
            });
            UI.AttachTooltip(slot, () =>
            {
                var me = _mc.Client.Latest?.Me;
                var view = me != null && index < me.Abilities.Length ? me.Abilities[index] : (AbilityView?)null;
                return AbilityTooltip.Build(def, view?.Level ?? 0, view);
            });
            _abilitySlots.Add(sv);
            return slot;
        }

        private void UpdateItems(PrivateState me)
        {
            foreach (var sv in _itemSlots)
            {
                var it = sv.Index < me.Items.Length ? me.Items[sv.Index] : default;
                if (sv.Id != it.Id)
                {
                    sv.Id = it.Id;
                    if (!string.IsNullOrEmpty(it.Id) && App.Data.Items.TryGetValue(it.Id, out var d)) El.SetImage(sv.Icon, GameText.ItemIconPath(d));
                    else sv.Icon.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                }
                sv.Charges.text = it.Charges > 0 ? it.Charges.ToString() : "";
                if (it.Cooldown > 0.05f && it.CooldownTotal > 0)
                {
                    sv.CdOverlay.style.top = new StyleLength(StyleKeyword.Auto);
                    sv.CdOverlay.style.height = Length.Percent(Mathf.Clamp01(it.Cooldown / it.CooldownTotal) * 100f);
                    sv.Cd.text = Mathf.CeilToInt(it.Cooldown).ToString();
                }
                else { sv.CdOverlay.style.height = 0; sv.Cd.text = ""; }
            }
        }

        private void UpdateBuffs(EntityState hero)
        {
            // Rebuild only when the set changes (cheap: few statuses).
            string key = string.Join(",", hero.Statuses.Select(s => s.Id + ":" + s.Stacks));
            if (_buffs.userData as string == key)
            {
                int i = 0;
                foreach (var s in hero.Statuses)
                {
                    if (!App.Data.Statuses.TryGetValue(s.Id ?? "", out var d) || d.Hidden) continue;
                    if (i < _buffs.childCount && _buffs[i].childCount > 0 && _buffs[i][0] is Label t) t.text = s.Remaining > 0 && s.Remaining < 99 ? Mathf.CeilToInt(s.Remaining).ToString() : s.Stacks > 1 ? s.Stacks.ToString() : "";
                    i++;
                }
                return;
            }
            _buffs.userData = key;
            _buffs.Clear();
            foreach (var s in hero.Statuses)
            {
                if (!App.Data.Statuses.TryGetValue(s.Id ?? "", out var d) || d.Hidden) continue;
                var b = El.Div("buff", d.IsDebuff ? "buff--debuff" : "");
                El.SetImage(b, GameText.StatusIconPath(d));
                var t = El.Text("", "buff-time");
                b.Add(t);
                var def = d;
                UI.AttachTooltip(b, def.Name ?? El.Pretty(def.Id), def.Description);
                _buffs.Add(b);
            }
        }

        private void UpdateTargetFrame()
        {
            var input = _mc.World.Input;
            EntityView v = null;
            if (input != null)
            {
                if (input.Hover != null) v = input.Hover;
                else if (input.SelectedId != 0 && _mc.World.TryGetView(input.SelectedId, out var sel) && sel.Id != _mc.LocalHero?.Id) v = sel;
            }
            if (v == null || v.State == null || v.Dying) { _targetFrame.Show(false); return; }
            _targetFrame.Show(true);
            string name = v.IsHero ? (_mc.Players.FirstOrDefault(p => p.HeroUnitId == v.Id)?.Name + " · " + v.Hero?.Name) : v.Unit?.Name ?? El.Pretty(v.DefId);
            _targetName.text = name;
            _targetName.EnableInClassList("t-red", _mc.World.IsEnemy(v.Team));
            _targetHp.text = $"{Mathf.CeilToInt(v.State.Hp)} / {Mathf.CeilToInt(v.State.MaxHp)}" + (v.State.MaxMana > 0 ? $" · {Mathf.FloorToInt(v.State.Mana)} mana" : "") + $" · armor {v.State.Armor:0.#} · dmg {v.State.Damage:0}";
        }
    }
}
