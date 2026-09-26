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
    // ====================================================================== heroes

    public sealed class HeroesPage : ClientPage
    {
        private VisualElement _grid, _detail;
        private Faction? _filter;
        private string _selected;

        protected override void OnBuild(VisualElement root)
        {
            var row = El.Div("row", "grow");
            row.style.alignItems = Align.Stretch;
            var left = El.Div("panel-thin", "col").Width(470);
            var filters = El.Div("row");
            filters.style.flexWrap = Wrap.Wrap;
            filters.Add(El.Btn("All", () => { _filter = null; RenderGrid(); }, "btn--small"));
            foreach (Faction f in Enum.GetValues(typeof(Faction)))
            {
                if (f == Faction.None) continue;
                var ff = f;
                filters.Add(El.Btn(GameText.Faction(f).Replace("The ", ""), () => { _filter = ff; RenderGrid(); }, "btn--small"));
            }
            left.Add(filters);
            var scroll = El.Scroll().Grow();
            _grid = El.Div("row");
            _grid.style.flexWrap = Wrap.Wrap;
            scroll.Add(_grid);
            left.Add(scroll);
            int playable = App.Data.PlayableHeroes().Count();
            left.Add(El.Text($"{playable} heroes playable in this build.", "t-small", "t-wrap", "mt-m"));
            row.Add(left);
            _detail = El.Div("panel-thin", "col", "grow");
            _detail.style.marginLeft = 10;
            var ds = El.Scroll().Grow();
            _detail.Add(ds);
            row.Add(_detail);
            root.Add(row);
        }

        public override void OnShow()
        {
            RenderGrid();
            if (_selected == null) _selected = App.Data.PlayableHeroes().FirstOrDefault()?.Id;
            RenderDetail();
        }

        private void RenderGrid()
        {
            _grid.Clear();
            foreach (var h in App.Data.Heroes.Values.Where(h => h.Playable && (_filter == null || h.Faction == _filter)))
            {
                var tile = El.Div("hero-tile", "hero-tile--compact", "portrait", h.Id == _selected ? "hero-tile--selected" : "");
                El.SetImage(tile, GameText.PortraitPath(h));
                var name = El.Text(h.Name, "t-small", "t-center");
                name.style.position = Position.Absolute;
                name.style.bottom = 0; name.style.left = 0; name.style.right = 0;
                name.style.backgroundColor = new Color(0, 0, 0, 0.7f);
                tile.Add(name);
                var id = h.Id;
                tile.RegisterCallback<ClickEvent>(_ => { _selected = id; RenderGrid(); RenderDetail(); App.Audio.PlayUi("click"); });
                _grid.Add(tile);
            }
        }

        private void RenderDetail()
        {
            var scroll = _detail.Q<ScrollView>();
            var c = scroll.contentContainer;
            c.Clear();
            if (_selected == null || !App.Data.Heroes.TryGetValue(_selected, out var h)) return;
            var head = El.Div("row");
            var portrait = El.Img(GameText.PortraitPath(h), 150, 190, "portrait");
            head.Add(portrait);
            var info = El.Div("col", "ml-m", "grow");
            info.Add(El.Text(h.Name, "t-display"));
            info.Add(El.Text(h.Title, "t-title"));
            info.Add(El.Text($"{GameText.Faction(h.Faction)} · {GameText.Attribute(h.PrimaryAttribute)} · {h.AttackType} · Difficulty {new string('◆', Math.Max(1, h.Difficulty))}", "t-subheading"));
            var roles = El.Div("row", "mt-m");
            foreach (var r in h.Roles) roles.Add(El.Text(r, "pill"));
            info.Add(roles);
            head.Add(info);
            c.Add(head);
            c.Add(El.Div("divider"));
            var stats = El.Div("row");
            stats.style.flexWrap = Wrap.Wrap;
            void Stat(string label, string value) { var s = El.Div("card", "col", "m-s").Width(150); s.Add(El.Text(label, "t-label")); s.Add(El.Text(value, "t-heading")); stats.Add(s); }
            Stat("Strength", $"{h.Str} + {h.StrGain}");
            Stat("Agility", $"{h.Agi} + {h.AgiGain}");
            Stat("Intelligence", $"{h.Int} + {h.IntGain}");
            Stat("Damage", $"{h.DamageMin}-{h.DamageMax}");
            Stat("Attack Range", $"{h.AttackRange:0.#} m");
            Stat("Attack Time", $"{h.BaseAttackTime:0.0#} s");
            Stat("Move Speed", $"{h.MoveSpeed:0.0#} m/s");
            Stat("Armor", $"{h.BaseArmor:0.#}");
            Stat("Vision", $"{h.VisionDay:0} / {h.VisionNight:0} m");
            c.Add(stats);
            c.Add(El.Text("ABILITIES", "t-subheading", "mt-l"));
            foreach (var aid in h.Abilities)
            {
                if (!App.Data.Abilities.TryGetValue(aid, out var a) || a.Hidden) continue;
                var card = El.Div("card", "row", "mb-m");
                card.style.alignItems = Align.FlexStart;
                card.Add(El.Img(GameText.AbilityIconPath(a), 64, 64, "portrait").Cls("shrink0"));
                var col = El.Div("col", "ml-m", "grow");
                col.Add(El.Text($"{a.Name}   [{GameText.Slot(a.Slot)}]", "t-heading"));
                col.Add(El.Text(a.Description, "t-body"));
                col.Add(El.Text(GameText.AbilityStats(a).Replace("\n", " · "), "t-small", "t-wrap", "mt-m"));
                if (!string.IsNullOrEmpty(a.Lore)) col.Add(El.Text(a.Lore, "t-small", "t-muted", "t-wrap", "mt-m"));
                card.Add(col);
                c.Add(card);
            }
            if (!string.IsNullOrEmpty(h.Lore)) { c.Add(El.Text("LORE", "t-subheading", "mt-l")); c.Add(El.Text(h.Lore, "t-body")); }
            if (h.Strengths.Length > 0) { c.Add(El.Text("STRENGTHS", "t-subheading", "mt-l")); c.Add(El.Text("• " + string.Join("\n• ", h.Strengths), "t-body")); }
            if (h.Weaknesses.Length > 0) { c.Add(El.Text("WEAKNESSES", "t-subheading", "mt-l")); c.Add(El.Text("• " + string.Join("\n• ", h.Weaknesses), "t-body")); }
        }
    }

    // ====================================================================== items

    public sealed class ItemsPage : ClientPage
    {
        private VisualElement _list, _detail;
        private string _category;
        private string _selected;
        private TextField _search;

        protected override void OnBuild(VisualElement root)
        {
            var row = El.Div("row", "grow");
            row.style.alignItems = Align.Stretch;
            var cats = El.Div("panel-thin", "col").Width(210);
            cats.Add(El.Text("CATEGORIES", "t-subheading"));
            cats.Add(El.Btn("All Items", () => { _category = null; Render(); }, "btn--small"));
            foreach (var cat in App.Data.ShopCategories)
            {
                var cc = cat;
                cats.Add(El.Btn(cat, () => { _category = cc; Render(); }, "btn--small"));
            }
            row.Add(cats);
            var mid = El.Div("panel-thin", "col").Width(440);
            mid.style.marginLeft = 8;
            _search = El.Field("Search", false, "", 40);
            _search.RegisterValueChangedCallback(_ => Render());
            mid.Add(_search);
            var scroll = El.Scroll().Grow();
            _list = El.Div("row");
            _list.style.flexWrap = Wrap.Wrap;
            scroll.Add(_list);
            mid.Add(scroll);
            mid.Add(El.Text($"{App.Data.Items.Values.Count(i => i.Purchasable)} items in this build.", "t-small"));
            row.Add(mid);
            _detail = El.Div("panel-thin", "col", "grow");
            _detail.style.marginLeft = 8;
            row.Add(_detail);
            root.Add(row);
        }

        public override void OnShow() => Render();

        private void Render()
        {
            _list.Clear();
            var s = _search.value?.Trim();
            foreach (var it in App.Data.Items.Values.Where(i => i.Purchasable && (_category == null || i.Category == _category) && (string.IsNullOrEmpty(s) || i.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)).OrderBy(i => i.TotalCost))
            {
                var tile = El.Div("shop-item", it.Id == _selected ? "shop-item--owned" : "");
                El.SetImage(tile, GameText.ItemIconPath(it));
                var cost = El.Text(it.TotalCost.ToString(), "t-small", "t-gold");
                cost.style.position = Position.Absolute; cost.style.right = 2; cost.style.bottom = 0;
                tile.Add(cost);
                var id = it.Id;
                tile.RegisterCallback<ClickEvent>(_ => { _selected = id; Render(); });
                UI.AttachTooltip(tile, () => ItemTooltip.Build(App.Data, it));
                _list.Add(tile);
            }
            _detail.Clear();
            if (_selected != null && App.Data.Items.TryGetValue(_selected, out var sel)) _detail.Add(ItemTooltip.Build(App.Data, sel, true));
            else _detail.Add(El.Text("Select an item to see its details, recipe and upgrades.", "t-body", "m-m"));
        }
    }

    /// <summary>Item tooltip / detail panel content (shared by the item database and the in-match shop).</summary>
    public static class ItemTooltip
    {
        public static VisualElement Build(GameData data, ItemDef it, bool detailed = false, int ownedGold = -1)
        {
            var c = El.Div("col");
            var head = El.Div("row");
            head.Add(El.Img(GameText.ItemIconPath(it), 48, 40, "portrait"));
            var t = El.Div("col", "ml-m");
            t.Add(El.Text(it.Name, "t-heading"));
            t.Add(El.Text($"{it.TotalCost} gold" + (it.Components != null && it.Components.Count > 0 ? $" (recipe {it.Cost})" : "") + $" · {it.Category}", ownedGold >= 0 && ownedGold < it.TotalCost ? "t-red" : "t-gold"));
            head.Add(t);
            c.Add(head);
            var mods = GameText.Modifiers(it.Modifiers);
            if (!string.IsNullOrEmpty(mods)) c.Add(El.Text(mods, "t-body", "t-green", "mt-m"));
            if (!string.IsNullOrEmpty(it.Description)) c.Add(El.Text(it.Description, "t-body", "mt-m"));
            if (it.Active != null) c.Add(El.Text("Active: " + GameText.AbilityStats(it.Active).Replace("\n", " · "), "t-small", "t-wrap", "mt-m"));
            if (!it.Implemented) c.Add(El.Text("Effect not implemented yet in this build.", "t-small", "t-red", "mt-m"));
            if (detailed)
            {
                if (it.Components != null && it.Components.Count > 0)
                {
                    c.Add(El.Text("RECIPE", "t-subheading", "mt-l"));
                    var r = El.Div("row");
                    foreach (var comp in it.Components) if (data.Items.TryGetValue(comp, out var cd)) r.Add(ComponentTile(cd));
                    if (it.Cost > 0) r.Add(El.Text($"+ {it.Cost} recipe", "t-small", "ml-m"));
                    c.Add(r);
                }
                if (it.BuildsInto.Count > 0)
                {
                    c.Add(El.Text("BUILDS INTO", "t-subheading", "mt-l"));
                    var r = El.Div("row");
                    r.style.flexWrap = Wrap.Wrap;
                    foreach (var up in it.BuildsInto) if (data.Items.TryGetValue(up, out var ud)) r.Add(ComponentTile(ud));
                    c.Add(r);
                }
                if (!string.IsNullOrEmpty(it.Lore)) c.Add(El.Text(it.Lore, "t-small", "t-muted", "t-wrap", "mt-l"));
            }
            return c;
        }

        private static VisualElement ComponentTile(ItemDef d)
        {
            var tile = El.Div("shop-item");
            El.SetImage(tile, GameText.ItemIconPath(d));
            GameApp.Instance.UI.AttachTooltip(tile, () => Build(GameApp.Instance.Data, d));
            return tile;
        }
    }

    // ====================================================================== strategy (RTS)

    public sealed class StrategyPage : ClientPage
    {
        protected override void OnBuild(VisualElement root)
        {
            var p = El.Div("panel", "col", "grow");
            p.Add(El.Text("War of the Ancients", "t-display"));
            p.Add(El.Text("STRATEGY MODE · EARLY ACCESS", "t-subheading"));
            p.Add(El.Div("divider"));
            p.Add(El.Text(
                "Base-building battles between the factions of Velmoragh: gather blood-iron and lumber, raise structures, train " +
                "armies and raze the enemy stronghold. Play against the RTS AI on this computer, or 1v1 through the Strategy queue " +
                "and custom games.", "t-body"));
            var actions = El.Div("row", "mt-m");
            actions.Add(El.Btn("Practice vs AI", () => App.Flow.StartOfflineMatch(new OfflineMatchOptions { ModeId = "rts_1v1", TeamSize = 1, PlayerName = App.Backend.Session.Account?.DisplayName ?? "Player" }), "btn--primary"));
            actions.Add(El.Btn("More options…", PracticeDialog.Open, "btn--ghost"));
            p.Add(actions);
            p.Add(El.Text("WHAT IS IN THIS BUILD", "t-subheading", "mt-l"));
            p.Add(El.Text(
                "• Workers mining blood-iron veins and cutting lumber on the Ashfields map\n• Construction, supply, training queues and rally points\n" +
                "• Four factions: the Dawnguard (healing shrines), the Ashen Legion (raises the fallen), the Crimson Court\n" +
                "  (paid in blood for every kill) and the Wild Covenant (stronger by night, shifters turn into wolves)\n" +
                "• Research: weapon, armour and building upgrades for every faction\n" +
                "• Hero altars: recruit up to three Blood War heroes, level them in battle, revive the fallen\n• RTS bots at four difficulties\n" +
                "• 1v1 lobbies and a Strategy queue on dedicated servers (tested end to end)", "t-body"));
            p.Add(El.Text("CONTROLS", "t-subheading", "mt-l"));
            p.Add(El.Text(
                "Left-click or drag to select · double-click selects all of a kind · Ctrl+1–9 saves a group, 1–9 recalls it\n" +
                "Right-click: move, attack, harvest a vein or trees, help build, return cargo; with a building selected, set its rally point\n" +
                "Command card hotkeys: A attack, S stop, H hold, B build, R return cargo; training and building hotkeys are shown on the buttons", "t-body"));
            p.Add(El.Text("STILL TO COME", "t-subheading", "mt-l"));
            p.Add(El.Text(
                "• Heroes tuned for this mode (they are Blood War's, unchanged) and hero items\n" +
                "• Dedicated Dawnguard and Ashen Legion models (they borrow Blood War's; the Court and Covenant have their own)\n" +
                "The RTS controls and HUD are new and have not yet been play-tested inside Unity; please report anything that misbehaves.", "t-body"));
            var crests = El.Div("row", "center", "mt-l");
            foreach (var c in new[] { "crimson_court", "ashen_legion", "wild_covenant", "dawnguard" }) crests.Add(El.Img("Textures/UI/Crests/crest_" + c, 140, 160));
            p.Add(crests);
            root.Add(p);
        }
    }

    // ====================================================================== community

    public sealed class CommunityPage : ClientPage
    {
        private VisualElement _body;

        protected override void OnBuild(VisualElement root)
        {
            _body = El.Div("row", "grow");
            _body.style.alignItems = Align.Stretch;
            root.Add(_body);
        }

        public override async void OnShow()
        {
            _body.Clear();
            if (Offline) { _body.Add(OfflineNotice("The community hub")); return; }
            await App.Backend.RefreshClan();
            var clanPanel = El.Div("panel-thin", "col", "grow");
            BuildClan(clanPanel);
            _body.Add(clanPanel);
            var right = El.Div("col").Width(420);
            right.style.marginLeft = 8;
            var news = El.Div("panel-thin", "col", "grow");
            news.Add(El.Text("NEWS & PATCH NOTES", "t-subheading"));
            var ns = El.Scroll().Grow();
            if (App.Backend.News.Count == 0) ns.Add(El.Text("No news published.", "t-small"));
            foreach (var n in App.Backend.News)
            {
                var card = El.Div("card", "col", "mb-m", "card--clickable");
                card.Add(El.Text(n.Title, "t-heading"));
                card.Add(El.Text($"{n.Category} · {n.PublishedAt.ToLocalTime():d MMM yyyy}" + (string.IsNullOrEmpty(n.Author) ? "" : " · " + n.Author), "t-small"));
                card.Add(El.Text(n.Summary, "t-body", "mt-m"));
                var article = n;
                card.RegisterCallback<ClickEvent>(_ => UI.Dialog(article.Title, article.Body ?? article.Summary, ("Close", "btn--ghost", null)));
                ns.Add(card);
            }
            news.Add(ns);
            right.Add(news);
            var channels = El.Div("panel-thin", "col", "mt-m");
            channels.Add(El.Text("CHAT CHANNELS", "t-subheading"));
            channels.Add(El.Text("Join a channel with /join <name> in chat. Joined channels appear as chat tabs.", "t-small", "t-wrap"));
            var joinRow = El.Div("row");
            var chName = El.Field("", false, "", 24).Grow();
            chName.style.marginBottom = 0;
            joinRow.Add(chName);
            joinRow.Add(El.Btn("Join", () => { if (!string.IsNullOrWhiteSpace(chName.value)) App.Backend.SendChat("global", "/join " + chName.value.Trim()); chName.value = ""; }, "btn--small"));
            channels.Add(joinRow);
            right.Add(channels);
            _body.Add(right);
        }

        private void BuildClan(VisualElement p)
        {
            var clan = App.Backend.Clan;
            if (clan == null)
            {
                p.Add(El.Text("CLAN", "t-subheading"));
                p.Add(El.Text("You are not in a clan. Create one, or accept an invitation from a clan officer.", "t-body"));
                var name = El.Field("Clan name (3-24 characters)", false, "", 24);
                var tag = El.Field("Tag (2-5 letters)", false, "", 5);
                var desc = El.Field("Description", false, "", 200);
                var err = El.Text("", "field-error");
                p.Add(name); p.Add(tag); p.Add(desc); p.Add(err);
                p.Add(El.Btn("Found Clan", async () =>
                {
                    var r = await App.Backend.CreateClan(new CreateClanRequest { Name = name.value, Tag = tag.value, Description = desc.value });
                    if (r.Ok) { UI.Toast("Clan founded!", ToastKind.Success); OnShow(); } else err.text = r.Message;
                }, "btn--primary"));
                p.Add(El.Text("INVITATIONS", "t-subheading", "mt-l"));
                var invites = El.Div("col");
                p.Add(invites);
                LoadInvites(invites);
                return;
            }
            p.Add(El.Text($"[{clan.Tag}] {clan.Name}", "t-title"));
            p.Add(El.Text($"Rating {clan.Rating} · {clan.Wins}W {clan.Losses}L · founded {clan.CreatedAt.ToLocalTime():d MMM yyyy}", "t-small"));
            if (!string.IsNullOrEmpty(clan.Description)) p.Add(El.Text(clan.Description, "t-body", "mt-m"));
            p.Add(El.Div("divider"));
            var me = clan.Members.FirstOrDefault(m => m.AccountId == App.Backend.MyId);
            bool officer = me != null && (me.Rank == "Leader" || me.Rank == "Officer");
            var scroll = El.Scroll().Grow();
            foreach (var m in clan.Members.OrderBy(m => m.Rank == "Leader" ? 0 : m.Rank == "Officer" ? 1 : m.Rank == "Member" ? 2 : 3).ThenBy(m => m.DisplayName))
            {
                var row = El.Div("list-row");
                row.Add(El.Dot(SocialPanel.DotClass(m.Status)));
                row.Add(El.Text(m.DisplayName, "grow"));
                row.Add(El.Text(m.Rank, "pill"));
                row.Add(El.Text("Lv " + m.Level, "t-small", "mr-m"));
                if (officer && m.AccountId != App.Backend.MyId && m.Rank != "Leader")
                {
                    var id = m.AccountId;
                    if (me.Rank == "Leader") row.Add(El.Btn(m.Rank == "Officer" ? "Demote" : "Promote", async () => { var r = await App.Backend.SetClanRank(id, m.Rank == "Officer" ? "Member" : "Officer"); if (!r.Ok) UI.Toast(r.Message, ToastKind.Error); OnShow(); }, "btn--small"));
                    row.Add(El.Btn("Kick", async () => { var r = await App.Backend.KickFromClan(id); if (!r.Ok) UI.Toast(r.Message, ToastKind.Error); OnShow(); }, "btn--small", "btn--danger"));
                }
                scroll.Add(row);
            }
            p.Add(scroll);
            p.Add(El.Btn("Leave Clan", () => UI.Dialog("Leave Clan", "Leave " + clan.Name + "?", ("Leave", "btn--danger", async () => { await App.Backend.LeaveClan(); OnShow(); }), ("Cancel", "btn--ghost", null)), "btn--danger", "btn--small", "mt-m"));
        }

        private async void LoadInvites(VisualElement host)
        {
            var r = await App.Backend.ClanInvites();
            host.Clear();
            if (!r.Ok || r.Value.Count == 0) { host.Add(El.Text("No pending invitations.", "t-small")); return; }
            foreach (var inv in r.Value)
            {
                var row = El.Div("list-row");
                row.Add(El.Text($"[{inv.ClanTag}] {inv.ClanName} - invited by {inv.FromName}", "grow"));
                var id = inv.InviteId;
                row.Add(El.Btn("Join", async () => { var a = await App.Backend.AcceptClanInvite(id); if (a.Ok) OnShow(); else UI.Toast(a.Message, ToastKind.Error); }, "btn--small"));
                host.Add(row);
            }
        }
    }

    // ====================================================================== rankings

    public sealed class RankingsPage : ClientPage
    {
        private VisualElement _rows;
        private DropdownField _category, _region;
        private Toggle _friends;
        private Label _season;
        private static readonly List<string> Categories = new List<string> { "rating", "wins", "gpm", "xpm", "clans" };

        protected override void OnBuild(VisualElement root)
        {
            var p = El.Div("panel-thin", "col", "grow");
            var filters = El.Div("row");
            _category = El.Dropdown("Leaderboard", new List<string> { "Ranked rating", "Wins", "Best GPM", "Best XPM", "Clans" }, 0, _ => Load());
            _category.style.width = 260;
            _region = El.Dropdown("Region", new List<string> { "Global" }, 0, _ => Load());
            _region.style.width = 220;
            _friends = El.Check("Friends only", false, _ => Load());
            _season = El.Text("", "t-small", "ml-m");
            filters.Add(_category); filters.Add(_region); filters.Add(_friends); filters.Add(_season);
            p.Add(filters);
            var header = El.Div("table-header", "mt-m");
            foreach (var (label, w) in new[] { ("#", 60f), ("PLAYER", 320f), ("RANK", 160f), ("VALUE", 120f), ("GAMES", 100f), ("WIN RATE", 100f) }) header.Add(El.Text(label).Width(w));
            p.Add(header);
            var scroll = El.Scroll().Grow();
            _rows = scroll.contentContainer;
            p.Add(scroll);
            root.Add(p);
        }

        public override void OnShow()
        {
            if (Offline) { _rows.Clear(); _rows.Add(OfflineNotice("Leaderboards")); return; }
            var names = new List<string> { "Global" };
            names.AddRange(App.Backend.Regions.Select(r => r.Name));
            _region.choices = names;
            Load();
        }

        private async void Load()
        {
            if (Offline) return;
            string cat = Categories[Mathf.Clamp(_category.index, 0, Categories.Count - 1)];
            string region = _region.index <= 0 ? "global" : App.Backend.Regions[_region.index - 1].Id;
            var r = await App.Backend.GetLeaderboard(cat, region, _friends.value);
            _rows.Clear();
            if (!r.Ok) { _rows.Add(El.Text("Could not load leaderboard: " + r.Message, "t-body", "status-offline")); return; }
            _season.text = "Season: " + r.Value.Season;
            if (r.Value.Entries.Count == 0) _rows.Add(El.Text("No ranked players yet. Play ranked matches to appear here.", "t-body", "m-m"));
            int i = 0;
            foreach (var e in r.Value.Entries)
            {
                var row = El.Div("list-row", i++ % 2 == 1 ? "list-row--alt" : "", e.AccountId == App.Backend.MyId ? "list-row--selected" : "");
                row.Add(El.Text(e.Position.ToString(), "t-heading").Width(60));
                row.Add(El.Text((string.IsNullOrEmpty(e.ClanTag) ? "" : $"[{e.ClanTag}] ") + e.DisplayName).Width(320));
                row.Add(El.Text(El.Pretty(e.Rank ?? ""), "t-small").Width(160));
                row.Add(El.Text(e.Value.ToString("0"), "t-gold").Width(120));
                row.Add(El.Text(e.Games.ToString()).Width(100));
                row.Add(El.Text((e.WinRate * 100).ToString("0") + "%").Width(100));
                var id = e.AccountId;
                if (cat != "clans") row.RegisterCallback<ClickEvent>(_ => ProfileDialog.Open(id));
                _rows.Add(row);
            }
        }
    }

    // ====================================================================== profile

    public sealed class ProfilePage : ClientPage
    {
        private VisualElement _body;

        protected override void OnBuild(VisualElement root)
        {
            var scroll = El.Scroll().Grow();
            _body = scroll.contentContainer;
            root.Add(scroll);
        }

        public override async void OnShow()
        {
            _body.Clear();
            if (Offline) { _body.Add(OfflineNotice("Your profile")); return; }
            await App.Backend.RefreshProfile();
            var p = App.Backend.MyProfile;
            if (p == null) { _body.Add(El.Text("Could not load your profile.", "t-body")); return; }
            _body.Add(ProfileView.Build(p, true));
        }
    }

    /// <summary>Profile content shared by the Profile tab and the profile dialog.</summary>
    public static class ProfileView
    {
        public static VisualElement Build(Profile p, bool self)
        {
            var app = GameApp.Instance;
            var root = El.Div("col");
            var head = El.Div("panel-thin", "row");
            var rankTier = (p.Rank?.Tier ?? p.Account.Rank ?? "Initiate").ToLowerInvariant().Replace(' ', '_');
            head.Add(El.Img("Textures/UI/Ranks/rank_" + rankTier, 110, 110));
            var info = El.Div("col", "ml-m", "grow");
            info.Add(El.Text((string.IsNullOrEmpty(p.Account.ClanTag) ? "" : $"[{p.Account.ClanTag}] ") + p.Account.DisplayName, "t-title"));
            info.Add(El.Text($"@{p.Account.Username} · Level {p.Account.Level} · member since {p.RegisteredAt.ToLocalTime():MMM yyyy}", "t-small"));
            if (p.Rank != null)
                info.Add(El.Text($"{El.Pretty(p.Rank.Tier)} {(p.Rank.Division > 0 ? "" + p.Rank.Division : "")} · {p.Rank.Rating} rating (peak {p.Rank.Peak})" + (p.Rank.Provisional ? " · placement" : "") + $" · {p.Rank.Wins}W {p.Rank.Losses}L · {p.Rank.Season}", "t-subheading"));
            if (!string.IsNullOrEmpty(p.StatusText)) info.Add(El.Text("\"" + p.StatusText + "\"", "t-body", "t-muted"));
            info.Add(El.Text($"{p.Commendations} commendations", "t-small"));
            head.Add(info);
            if (self)
            {
                head.Add(El.Btn("Edit Profile", () => EditProfile(p), "btn--small"));
            }
            root.Add(head);

            var s = p.Stats ?? new ProfileStats();
            var stats = El.Div("row", "mt-m");
            stats.style.flexWrap = Wrap.Wrap;
            void Stat(string label, string value) { var c = El.Div("card", "col", "m-s").Width(160); c.Add(El.Text(label, "t-label")); c.Add(El.Text(value, "t-heading")); stats.Add(c); }
            Stat("Matches", s.GamesPlayed.ToString());
            Stat("Win rate", (s.WinRate * 100).ToString("0.#") + "%");
            Stat("K / D / A", $"{s.AvgKills:0.#} / {s.AvgDeaths:0.#} / {s.AvgAssists:0.#}");
            Stat("GPM / XPM", $"{s.AvgGpm:0} / {s.AvgXpm:0}");
            Stat("Towers", s.TowersDestroyed.ToString());
            Stat("Wards", s.WardsPlaced.ToString());
            Stat("Abandons", s.Abandons.ToString());
            Stat("Hours", s.HoursPlayed.ToString("0.#"));
            root.Add(stats);

            var cols = El.Div("row", "mt-m");
            cols.style.alignItems = Align.FlexStart;
            var history = El.Div("panel-thin", "col", "grow");
            history.Add(El.Text("RECENT MATCHES", "t-subheading"));
            if (p.RecentMatches.Count == 0) history.Add(El.Text("No matches played yet.", "t-small"));
            foreach (var m in p.RecentMatches)
            {
                var row = El.Div("list-row");
                row.Add(El.Text(m.Won ? "WIN" : m.Abandoned ? "ABANDON" : "LOSS", m.Won ? "status-online" : "status-offline").Width(80));
                string heroName = GameText.PlayedAs(app.Data, m.HeroId, m.RtsFaction);
                row.Add(El.Text(heroName).Width(120));
                row.Add(El.Text($"{m.Kills}/{m.Deaths}/{m.Assists}").Width(80));
                row.Add(El.Text($"{m.LastHits} LH · {m.Gpm:0} GPM").Width(140));
                row.Add(El.Text(El.FormatTime(m.DurationSeconds), "t-small").Width(60));
                row.Add(El.Text(m.Ranked ? (m.RatingChange >= 0 ? "+" : "") + m.RatingChange : "", m.RatingChange >= 0 ? "t-green" : "t-red").Width(50));
                row.Add(El.Text(m.EndedAt.ToLocalTime().ToString("d MMM HH:mm"), "t-small"));
                var id = m.MatchId;
                row.RegisterCallback<ClickEvent>(_ => MatchDetailDialog.Open(id));
                history.Add(row);
            }
            cols.Add(history);
            var side = El.Div("col").Width(380);
            side.style.marginLeft = 8;
            var heroes = El.Div("panel-thin", "col");
            heroes.Add(El.Text("HEROES", "t-subheading"));
            if (p.Heroes.Count == 0) heroes.Add(El.Text("No hero statistics yet.", "t-small"));
            foreach (var h in p.Heroes.OrderByDescending(h => h.Games).Take(10))
            {
                var row = El.Div("list-row");
                row.Add(El.Text(app.Data.Heroes.TryGetValue(h.HeroId ?? "", out var hd) ? hd.Name : El.Pretty(h.HeroId), "grow"));
                row.Add(El.Text($"{h.Games} games · {h.WinRate * 100:0}% · KDA {h.Kda:0.0}", "t-small"));
                heroes.Add(row);
            }
            side.Add(heroes);
            var ach = El.Div("panel-thin", "col", "mt-m");
            ach.Add(El.Text("ACHIEVEMENTS", "t-subheading"));
            foreach (var a in p.Achievements)
            {
                var row = El.Div("list-row");
                row.Add(El.Text(a.UnlockedAt.HasValue ? "✔" : "·", a.UnlockedAt.HasValue ? "t-gold" : "t-muted").Width(20));
                var col = El.Div("col", "grow");
                col.Add(El.Text(a.Name, a.UnlockedAt.HasValue ? "" : "t-muted"));
                col.Add(El.Text(a.Description, "t-small"));
                row.Add(col);
                ach.Add(row);
            }
            side.Add(ach);
            cols.Add(side);
            root.Add(cols);
            return root;
        }

        private static void EditProfile(Profile p)
        {
            var app = GameApp.Instance;
            var d = El.Div("dialog", "col").Width(520);
            d.Add(El.Text("Edit Profile", "t-title", "t-center"));
            var display = El.Field("Display name", false, p.Account.DisplayName, 24);
            var status = El.Field("Status message", false, p.StatusText ?? "", 80);
            var err = El.Text("", "field-error");
            d.Add(display); d.Add(status); d.Add(err);
            var row = El.Div("row", "center");
            VisualElement ov = null;
            row.Add(El.Btn("Save", async () =>
            {
                var r = await app.Backend.UpdateProfile(new UpdateProfileRequest { DisplayName = display.value, StatusText = status.value });
                if (r.Ok) { app.UI.CloseOverlay(ov); await app.Backend.RefreshProfile(); app.UI.Get<MainClientScreen>().Page<ProfilePage>()?.OnShow(); }
                else err.text = r.Message;
            }, "btn--primary"));
            row.Add(El.Btn("Cancel", () => app.UI.CloseOverlay(ov), "btn--ghost"));
            d.Add(row);
            ov = app.UI.ShowOverlay(d);
        }
    }

    public static class ProfileDialog
    {
        public static async void Open(string accountId)
        {
            var app = GameApp.Instance;
            var r = await app.Backend.GetProfile(accountId);
            if (!r.Ok) { app.UI.Error("Profile", r.Message); return; }
            var d = El.Div("dialog", "col");
            d.style.width = 1100;
            d.style.maxHeight = 820;
            var scroll = El.Scroll().Grow();
            scroll.Add(ProfileView.Build(r.Value, accountId == app.Backend.MyId));
            d.Add(scroll);
            var row = El.Div("row", "center", "mt-m");
            VisualElement ov = null;
            if (accountId != app.Backend.MyId && !r.Value.IsFriend)
                row.Add(El.Btn("Add Friend", async () => { var a = await app.Backend.AddFriend(r.Value.Account.Username); app.UI.Toast(a.Ok ? "Friend request sent." : a.Message, a.Ok ? ToastKind.Success : ToastKind.Error); }, "btn--small"));
            if (accountId != app.Backend.MyId)
                row.Add(El.Btn("Report", () => ReportDialog.Open(accountId, r.Value.Account.DisplayName, null), "btn--small", "btn--danger"));
            row.Add(El.Btn("Close", () => app.UI.CloseOverlay(ov), "btn--ghost"));
            d.Add(row);
            ov = app.UI.ShowOverlay(d);
        }
    }

    public static class ReportDialog
    {
        public static void Open(string accountId, string name, string matchId)
        {
            var app = GameApp.Instance;
            var d = El.Div("dialog", "col").Width(520);
            d.Add(El.Text("Report " + name, "t-title", "t-center"));
            var reasons = new List<string> { "Cheating", "Abusive chat", "Griefing / feeding", "Abandoning", "Inappropriate name", "Other" };
            var reason = El.Dropdown("Reason", reasons, 0, null);
            var details = El.Field("Details (optional)", false, "", 500);
            d.Add(reason); d.Add(details);
            var row = El.Div("row", "center");
            VisualElement ov = null;
            row.Add(El.Btn("Submit Report", async () =>
            {
                var r = await app.Backend.Report(new ReportRequest { AccountId = accountId, Reason = reasons[Math.Max(0, reason.index)], Details = details.value, MatchId = matchId });
                app.UI.CloseOverlay(ov);
                app.UI.Toast(r.Ok ? "Report submitted. Thank you." : r.Message, r.Ok ? ToastKind.Success : ToastKind.Error);
            }, "btn--danger"));
            row.Add(El.Btn("Cancel", () => app.UI.CloseOverlay(ov), "btn--ghost"));
            d.Add(row);
            ov = app.UI.ShowOverlay(d);
        }
    }

    public static class MatchDetailDialog
    {
        public static async void Open(string matchId)
        {
            var app = GameApp.Instance;
            var r = await app.Backend.GetMatchDetail(matchId);
            if (!r.Ok) { app.UI.Error("Match", r.Message); return; }
            var m = r.Value;
            var d = El.Div("dialog", "col");
            d.style.width = 1150;
            d.Add(El.Text($"{(m.Winner ?? "?").ToUpperInvariant()} VICTORY", "t-title", "t-center"));
            d.Add(El.Text($"{El.Pretty(m.ModeId)} · {El.Pretty(m.MapId)} · {m.Region} · {El.FormatTime(m.DurationSeconds)} · {m.EndedAt.ToLocalTime():g}" + (m.Ranked ? " · Ranked" : ""), "t-small", "t-center"));
            foreach (var team in new[] { "Dawn", "Dusk" })
            {
                d.Add(El.Text(team == "Dawn" ? "THE DAWN" : "THE DUSK", "t-subheading", "mt-m"));
                var header = El.Div("table-header");
                foreach (var (l, w) in new[] { ("PLAYER", 220f), ("HERO", 130f), ("LVL", 50f), ("K/D/A", 90f), ("LH/DN", 80f), ("NET WORTH", 100f), ("GPM", 60f), ("HERO DMG", 90f), ("ITEMS", 260f) }) header.Add(El.Text(l).Width(w));
                d.Add(header);
                foreach (var p in m.Players.Where(p => p.Team == team))
                {
                    var row = El.Div("list-row");
                    row.Add(El.Text(p.Name + (p.IsBot ? " (bot)" : "") + (p.Abandoned ? " ✖" : "")).Width(220));
                    row.Add(El.Text(GameText.PlayedAs(app.Data, p.HeroId, p.RtsFaction)).Width(130));
                    row.Add(El.Text(p.Level.ToString()).Width(50));
                    row.Add(El.Text($"{p.Kills}/{p.Deaths}/{p.Assists}").Width(90));
                    row.Add(El.Text($"{p.LastHits}/{p.Denies}").Width(80));
                    row.Add(El.Text(p.NetWorth.ToString(), "t-gold").Width(100));
                    row.Add(El.Text(p.Gpm.ToString("0")).Width(60));
                    row.Add(El.Text(p.HeroDamage.ToString("0")).Width(90));
                    var items = El.Div("row").Width(260);
                    foreach (var it in p.Items) if (!string.IsNullOrEmpty(it) && app.Data.Items.TryGetValue(it, out var idf)) items.Add(El.Img(GameText.ItemIconPath(idf), 34, 28, "portrait"));
                    row.Add(items);
                    d.Add(row);
                }
            }
            VisualElement ov = null;
            d.Add(El.Btn("Close", () => app.UI.CloseOverlay(ov), "btn--ghost", "mt-m"));
            ov = app.UI.ShowOverlay(d);
        }
    }
}
