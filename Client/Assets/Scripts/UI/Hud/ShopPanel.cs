using System.Collections.Generic;
using System.Linq;
using Bloodfall.Client.Core;
using Bloodfall.Client.Match;
using Bloodfall.Client.UI.Screens;
using Bloodfall.Data;
using Bloodfall.Protocol;
using Bloodfall.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Bloodfall.Client.UI.Hud
{
    /// <summary>
    /// In-match shop: categories, recommended build for the hero, search, recipe view. Right-click (or double-click)
    /// buys; the server decides price, placement (inventory near a shop, otherwise stash) and legality.
    /// </summary>
    public sealed class ShopPanel
    {
        public VisualElement Root { get; private set; }
        private VisualElement _grid, _detail, _cats;
        private Label _gold, _where;
        private TextField _search;
        private string _category = "Recommended";
        private string _selected;
        private MatchController _mc;
        private GameApp App => GameApp.Instance;
        private int _lastGold = -1;
        private string _lastHero;

        public bool IsOpen => Root != null && Root.style.display == DisplayStyle.Flex;

        public VisualElement Build()
        {
            Root = El.Div("panel", "col");
            Root.style.position = Position.Absolute;
            Root.style.right = 20;
            Root.style.top = 90;
            Root.style.width = 760;
            Root.style.height = 640;
            var head = El.Div("row", "space-between");
            head.Add(El.Text("MERCHANTS OF VELMORAGH", "t-title"));
            _gold = El.Text("", "t-title", "t-gold");
            head.Add(_gold);
            head.Add(El.Btn("✖", Toggle, "btn--icon"));
            Root.Add(head);
            _where = El.Text("", "t-small");
            Root.Add(_where);
            _search = El.Field("", false, "", 30);
            _search.RegisterValueChangedCallback(_ => Render());
            Root.Add(_search);
            var body = El.Div("row", "grow");
            body.style.alignItems = Align.Stretch;
            _cats = El.Div("col").Width(150);
            body.Add(_cats);
            var scroll = El.Scroll().Grow();
            _grid = El.Div("row");
            _grid.style.flexWrap = Wrap.Wrap;
            _grid.style.alignContent = Align.FlexStart;
            scroll.Add(_grid);
            body.Add(scroll);
            _detail = El.Div("card", "col").Width(250);
            body.Add(_detail);
            Root.Add(body);
            Root.Add(El.Text("Right-click to buy · Items bought away from a shop go to your stash and are delivered periodically.", "t-small"));
            Root.style.display = DisplayStyle.None;
            return Root;
        }

        public void Bind(MatchController mc)
        {
            _mc = mc;
            _cats.Clear();
            foreach (var c in new[] { "Recommended" }.Concat(App.Data.ShopCategories))
            {
                var cc = c;
                var b = El.Btn(c, () => { _category = cc; _search.value = ""; Render(); }, "btn--small");
                b.style.alignSelf = Align.Stretch;
                _cats.Add(b);
            }
        }

        public void Toggle()
        {
            bool open = !IsOpen;
            Root.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            App.Audio.PlayUi(open ? "shop_open" : "panel_close");
            if (open) Render();
        }

        private HeroDef MyHeroDef
        {
            get
            {
                var lp = _mc?.LocalPlayer;
                return lp?.HeroId != null && App.Data.Heroes.TryGetValue(lp.HeroId, out var h) ? h : null;
            }
        }

        private IEnumerable<ItemDef> Items()
        {
            var s = _search.value?.Trim();
            if (!string.IsNullOrEmpty(s)) return App.Data.Items.Values.Where(i => i.Purchasable && i.Name.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(i => i.TotalCost);
            if (_category == "Recommended")
            {
                var h = MyHeroDef;
                var ids = h?.RecommendedItems?.SelectMany(kv => kv.Value).Distinct().ToList() ?? new List<string>();
                return ids.Where(App.Data.Items.ContainsKey).Select(id => App.Data.Items[id]);
            }
            return App.Data.Items.Values.Where(i => i.Purchasable && i.Category == _category).OrderBy(i => i.TotalCost);
        }

        public void Render()
        {
            if (!IsOpen) return;
            var me = _mc.Client.Latest?.Me;
            int gold = me?.Gold ?? 0;
            _grid.Clear();
            var owned = new HashSet<string>(me?.Items.Where(i => !string.IsNullOrEmpty(i.Id)).Select(i => i.Id) ?? Enumerable.Empty<string>());
            if (_category == "Recommended" && string.IsNullOrEmpty(_search.value))
            {
                var h = MyHeroDef;
                if (h?.RecommendedItems != null)
                {
                    foreach (var kv in h.RecommendedItems)
                    {
                        var sec = El.Text(El.Pretty(kv.Key).ToUpperInvariant(), "t-subheading");
                        sec.style.width = Length.Percent(100);
                        _grid.Add(sec);
                        foreach (var id in kv.Value) if (App.Data.Items.TryGetValue(id, out var d)) _grid.Add(Tile(d, gold, owned));
                    }
                }
                else _grid.Add(El.Text("No recommended build for this hero yet.", "t-small"));
            }
            else foreach (var d in Items()) _grid.Add(Tile(d, gold, owned));
            RenderDetail(gold);
            _lastGold = gold;
        }

        private VisualElement Tile(ItemDef d, int gold, HashSet<string> owned)
        {
            var tile = El.Div("shop-item", owned.Contains(d.Id) ? "shop-item--owned" : "", gold < d.TotalCost ? "shop-item--unaffordable" : "");
            El.SetImage(tile, GameText.ItemIconPath(d));
            var cost = El.Text(d.TotalCost.ToString(), "t-small", gold >= d.TotalCost ? "t-gold" : "t-red");
            cost.style.position = Position.Absolute; cost.style.right = 2; cost.style.bottom = 0;
            cost.pickingMode = PickingMode.Ignore;
            tile.Add(cost);
            var id = d.Id;
            tile.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1) Buy(id);
                else { _selected = id; RenderDetail(_mc.Client.Latest?.Me?.Gold ?? 0); if (e.clickCount >= 2) Buy(id); }
            });
            App.UI.AttachTooltip(tile, () => ItemTooltip.Build(App.Data, d, false, _mc.Client.Latest?.Me?.Gold ?? -1));
            return tile;
        }

        private void RenderDetail(int gold)
        {
            _detail.Clear();
            if (_selected == null || !App.Data.Items.TryGetValue(_selected, out var d)) { _detail.Add(El.Text("Select an item to see its recipe.", "t-small", "t-wrap")); return; }
            _detail.Add(ItemTooltip.Build(App.Data, d, true, gold));
            var b = El.Btn($"Buy ({d.TotalCost})", () => Buy(d.Id), "btn--primary");
            b.style.alignSelf = Align.Stretch;
            _detail.Add(b);
        }

        private void Buy(string id)
        {
            var hero = _mc.LocalHero;
            if (hero == null) return;
            _mc.SendOrder(Order.Buy(hero.Id, id));
        }

        public void Tick()
        {
            if (!IsOpen) return;
            var me = _mc.Client.Latest?.Me;
            int gold = me?.Gold ?? 0;
            _gold.text = gold + " gold";
            _where.text = me == null ? "" : me.AtBase ? "At the base shop: purchases go to your inventory." : me.NearSecretShop ? "At a secret shop." : "Away from shops: purchases go to your stash.";
            var heroId = _mc.LocalPlayer?.HeroId;
            if (gold != _lastGold || heroId != _lastHero) { _lastHero = heroId; Render(); }
        }
    }

    /// <summary>Hold-Tab scoreboard (enemy net worth hidden by the server's fog rules).</summary>
    public sealed class Scoreboard
    {
        public VisualElement Root { get; private set; }
        private VisualElement _body;
        private GameApp App => GameApp.Instance;

        public VisualElement Build()
        {
            Root = El.Div("panel", "col");
            Root.style.position = Position.Absolute;
            Root.style.left = Length.Percent(50);
            Root.style.top = 90;
            Root.style.width = 1180;
            Root.style.marginLeft = -590;
            _body = El.Div("col");
            Root.Add(_body);
            Root.style.display = DisplayStyle.None;
            Root.pickingMode = PickingMode.Ignore;
            return Root;
        }

        public void Show(bool show, MatchController mc)
        {
            Root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            _body.Clear();
            var frame = mc.Client.Latest;
            if (frame == null) return;
            _body.Add(El.Text($"THE DAWN  {frame.TeamKills[0]}  —  {frame.TeamKills[1]}  THE DUSK   ·   {El.FormatTime(frame.Time)}", "t-title", "t-center"));
            foreach (var team in new[] { Team.Dawn, Team.Dusk })
            {
                var header = El.Div("table-header", "mt-m");
                foreach (var (l, w) in Cols) header.Add(El.Text(l).Width(w));
                _body.Add(header);
                foreach (var p in frame.Players.Where(p => p.Team == team).OrderBy(p => p.Slot))
                {
                    var row = El.Div("scoreboard-row", p.Id == mc.Client.LocalPlayerId ? "list-row--selected" : "");
                    string hero = p.HeroId != null && App.Data.Heroes.TryGetValue(p.HeroId, out var hd) ? hd.Name : "-";
                    string conn = p.Connection == PlayerConnection.Disconnected ? " (DC)" : p.Connection == PlayerConnection.Abandoned ? " (left)" : p.IsBot ? " (bot)" : "";
                    string[] vals =
                    {
                        p.Name + conn, hero, p.Level.ToString(), $"{p.Kills}/{p.Deaths}/{p.Assists}", $"{p.LastHits}/{p.Denies}",
                        p.NetWorth >= 0 ? p.NetWorth.ToString() : "?", p.Gpm.ToString("0"), p.Xpm.ToString("0"),
                        p.RespawnIn > 0 ? Mathf.CeilToInt(p.RespawnIn) + "s" : "", p.IsBot ? "" : p.PingMs + " ms",
                    };
                    for (int i = 0; i < vals.Length; i++) row.Add(El.Text(vals[i], i == 0 && team == Team.Dawn ? "t-gold" : i == 0 ? "t-red" : "").Width(Cols[i].w));
                    _body.Add(row);
                }
            }
        }

        private static readonly (string l, float w)[] Cols =
        {
            ("PLAYER", 240), ("HERO", 140), ("LVL", 60), ("K/D/A", 110), ("LH/DN", 90), ("NET WORTH", 110), ("GPM", 70), ("XPM", 70), ("RESPAWN", 90), ("PING", 80),
        };
    }
}
