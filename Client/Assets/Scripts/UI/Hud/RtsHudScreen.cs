using System;
using System.Collections.Generic;
using System.Linq;
using Bloodfall.Client.Core;
using Bloodfall.Client.Input;
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
    /// War of the Ancients HUD: resources and supply, the selection panel (unit, construction, training queue,
    /// cargo, vein contents), the command card with hotkeys and costs, minimap, alerts, chat and the game menu.
    /// Like the MOBA HUD it only displays snapshot state and sends orders through <see cref="RtsInput"/>.
    /// </summary>
    public sealed class RtsHudScreen : UIScreen
    {
        private MatchController _mc;
        private WorldOverlay _overlay;
        private Minimap _minimap;
        private Label _gold, _lumber, _supply, _clock, _dayNight, _error, _announce, _announceSub, _paused, _endBanner, _hint;
        private VisualElement _selection, _card, _dragBox, _chatLog, _menu;
        private TextField _chatInput;
        private bool _chatTeam = true;
        private float _errorTimer, _announceTimer, _attackAlertCooldown;
        private string _cardSignature = "", _selectionSignature = "";
        private bool _buildMenu;
        private int _chatCount;
        private readonly List<CardButton> _buttons = new List<CardButton>();

        private sealed class CardButton
        {
            public string Label, Sub, Tooltip;
            public KeyCode Key;
            public bool Enabled = true;
            public Action Do;
        }

        private RtsInput Rts => _mc?.World?.Rts;

        // ================================================================== build

        protected override void OnBuild(VisualElement root)
        {
            root.pickingMode = PickingMode.Ignore;
            _overlay = new WorldOverlay(App.Settings);
            root.Add(_overlay.Root);

            // Top bar: resources, supply, clock.
            var bar = El.Div("row", "hud-plate");
            bar.style.position = Position.Absolute;
            bar.style.top = 0;
            bar.style.right = 0;
            bar.style.height = 54;
            bar.style.paddingLeft = 18; bar.style.paddingRight = 12;
            bar.style.alignItems = Align.Center;
            _gold = Res(bar, "Blood-iron", "t-red");
            _lumber = Res(bar, "Lumber", "t-gold");
            _supply = Res(bar, "Supply", "");
            _clock = El.Text("0:00", "t-heading").Width(80);
            bar.Add(_clock);
            _dayNight = El.Text("", "t-small").Width(70);
            bar.Add(_dayNight);
            bar.Add(El.Btn("Menu", ToggleMenu, "btn--small"));
            root.Add(bar);

            // Alerts.
            var col = El.Div("col", "center");
            col.style.position = Position.Absolute;
            col.style.top = Length.Percent(16);
            col.style.left = 0; col.style.right = 0;
            col.pickingMode = PickingMode.Ignore;
            _announce = El.Text("", "announcer");
            _announceSub = El.Text("", "announcer-sub");
            col.Add(_announce);
            col.Add(_announceSub);
            root.Add(col);
            _error = El.Text("", "t-heading", "t-red", "t-center");
            _error.style.position = Position.Absolute;
            _error.style.bottom = 240;
            _error.style.left = 0; _error.style.right = 0;
            _error.style.unityTextOutlineWidth = 1;
            _error.style.unityTextOutlineColor = Color.black;
            _error.pickingMode = PickingMode.Ignore;
            root.Add(_error);
            _hint = El.Text("", "t-small", "t-center");
            _hint.style.position = Position.Absolute;
            _hint.style.bottom = 215;
            _hint.style.left = 0; _hint.style.right = 0;
            _hint.pickingMode = PickingMode.Ignore;
            root.Add(_hint);

            // Bottom: minimap (left), selection (centre), command card (right).
            _minimap = new Minimap();
            var mm = _minimap.Build();
            mm.style.position = Position.Absolute;
            mm.style.bottom = 0;
            mm.style.left = 0;
            root.Add(mm);

            var plate = El.Div("hud-plate", "row");
            plate.style.position = Position.Absolute;
            plate.style.bottom = 0;
            plate.style.left = Minimap.Size + 8;
            plate.style.right = 0;
            plate.style.height = 200;
            plate.style.alignItems = Align.Stretch;
            _selection = El.Div("col").Grow();
            _selection.style.paddingLeft = 14; _selection.style.paddingTop = 10; _selection.style.paddingRight = 14;
            plate.Add(_selection);
            _card = El.Div("row").Width(4 * 96 + 16);
            _card.style.flexWrap = Wrap.Wrap;
            _card.style.alignContent = Align.FlexStart;
            _card.style.paddingTop = 8;
            plate.Add(_card);
            root.Add(plate);

            // Box selection rectangle.
            _dragBox = El.Div();
            _dragBox.style.position = Position.Absolute;
            _dragBox.style.borderTopWidth = _dragBox.style.borderBottomWidth = _dragBox.style.borderLeftWidth = _dragBox.style.borderRightWidth = 1;
            var green = new Color(0.4f, 1f, 0.5f, 0.9f);
            _dragBox.style.borderTopColor = _dragBox.style.borderBottomColor = _dragBox.style.borderLeftColor = _dragBox.style.borderRightColor = green;
            _dragBox.style.backgroundColor = new Color(0.4f, 1f, 0.5f, 0.08f);
            _dragBox.pickingMode = PickingMode.Ignore;
            _dragBox.Show(false);
            root.Add(_dragBox);

            BuildChat(root);

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

        private static Label Res(VisualElement bar, string name, string cls)
        {
            var box = El.Div("col").Width(120);
            box.Add(El.Text(name.ToUpperInvariant(), "t-small"));
            var value = string.IsNullOrEmpty(cls) ? El.Text("0", "t-heading") : El.Text("0", "t-heading", cls);
            box.Add(value);
            bar.Add(box);
            return value;
        }

        private void BuildChat(VisualElement root)
        {
            var col = El.Div("col");
            col.style.position = Position.Absolute;
            col.style.bottom = 280;
            col.style.left = 12;
            col.style.width = 520;
            col.pickingMode = PickingMode.Ignore;
            _chatLog = El.Div("col");
            _chatLog.pickingMode = PickingMode.Ignore;
            col.Add(_chatLog);
            _chatInput = El.Field("", false, "", 200);
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

        // ================================================================== bind

        public void Bind(MatchController mc)
        {
            _mc = mc;
            _overlay.Bind(mc.World);
            _minimap.Bind(mc.World);
            mc.EventReceived -= OnEvent;
            mc.EventReceived += OnEvent;
            if (Rts != null)
            {
                Rts.LocalError -= ShowError;
                Rts.LocalError += ShowError;
                Rts.SelectionChanged -= OnSelectionChanged;
                Rts.SelectionChanged += OnSelectionChanged;
            }
            _endBanner.Show(false);
            _chatCount = 0;
            _cardSignature = _selectionSignature = "";
        }

        public override void OnHide()
        {
            if (_mc != null) _mc.EventReceived -= OnEvent;
            _overlay.Clear();
        }

        private void OnSelectionChanged() => _buildMenu = false;

        // ================================================================== events

        private void OnEvent(NetEvent e)
        {
            var world = _mc?.World;
            if (world == null) return;
            switch (e.Type)
            {
                case SimEventType.Error:
                    ShowError(e.Key);
                    break;
                case SimEventType.ResourcesDelivered:
                    if (world.TryGetView(e.UnitId, out var w) && App.Settings.ShowDamageNumbers)
                    {
                        if (e.Value > 0) _overlay.Float(w.Point(1f), "+" + Mathf.RoundToInt(e.Value), "floating--gold", 0.9f);
                        if (e.Value2 > 0) _overlay.Float(w.Point(1f), "+" + Mathf.RoundToInt(e.Value2), "floating--heal", 0.9f);
                    }
                    break;
                case SimEventType.ConstructionComplete:
                    if (world.TryGetView(e.UnitId, out var b) && b.State?.OwnerPlayer == world.LocalPlayerId)
                        Alert((b.Unit?.Name ?? El.Pretty(e.Key)).ToUpperInvariant() + " COMPLETE", "");
                    break;
                case SimEventType.PlayerEliminated:
                {
                    var p = _mc.Players.FirstOrDefault(x => x.Id == e.OtherId);
                    Alert("ELIMINATED", p?.Name ?? "");
                    break;
                }
                case SimEventType.MineDepleted:
                    Alert("A BLOOD-IRON VEIN HAS RUN DRY", "");
                    break;
                case SimEventType.Damage:
                    if (_attackAlertCooldown <= 0f && world.TryGetView(e.UnitId, out var hit) && hit.State?.OwnerPlayer == world.LocalPlayerId
                        && (hit.IsStructure || hit.Kind == UnitKind.Worker) && !(world.Camera.Cam != null && OnScreen(world, hit.Position)))
                    {
                        _attackAlertCooldown = 20f;
                        Alert("YOUR BASE IS UNDER ATTACK", "");
                        _minimap.Ping(new Vector2(hit.Position.x, hit.Position.z), new Color(1f, 0.25f, 0.2f));
                        App.Audio.PlayUi("error");
                    }
                    break;
                case SimEventType.Announcer:
                    if (e.Key == AnnouncerKeys.BattleBegins) Alert("THE WAR BEGINS", "");
                    break;
            }
        }

        private static bool OnScreen(MatchWorld world, Vector3 p)
        {
            var sp = world.Camera.Cam.WorldToScreenPoint(p);
            return sp.z > 0 && sp.x > 0 && sp.y > 0 && sp.x < Screen.width && sp.y < Screen.height;
        }

        private void ShowError(string msg)
        {
            _error.text = msg;
            _errorTimer = 2.2f;
        }

        private void Alert(string text, string sub)
        {
            _announce.text = text;
            _announceSub.text = sub;
            _announceTimer = 3f;
        }

        // ================================================================== per frame

        public override void Tick(float dt)
        {
            if (_mc == null || _mc.World == null) return;
            var world = _mc.World;
            var frame = _mc.Client.Latest;
            _overlay.Update(dt, world.Camera?.Cam);
            _minimap.Update(dt);
            _paused.Show(_mc.Paused);
            _attackAlertCooldown -= dt;

            bool typing = Rts != null && Rts.KeyboardCaptured;
            if (!typing)
            {
                if (InputBridge.GetKeyDown(KeyBinds.Get(App.Settings, KeyBinds.Chat))) OpenChat(!InputBridge.Shift);
                if (InputBridge.GetKeyDown(KeyBinds.Get(App.Settings, KeyBinds.Menu))) ToggleMenu();
                foreach (var b in _buttons)
                    if (b.Key != KeyCode.None && b.Enabled && InputBridge.GetKeyDown(b.Key)) { b.Do(); break; }
            }

            if (_errorTimer > 0) { _errorTimer -= dt; _error.style.opacity = Mathf.Clamp01(_errorTimer * 2f); }
            else _error.text = "";
            if (_announceTimer > 0) { _announceTimer -= dt; float a = Mathf.Clamp01(_announceTimer * 1.5f); _announce.style.opacity = a; _announceSub.style.opacity = a; }
            else { _announce.text = ""; _announceSub.text = ""; }

            UpdateDragBox();
            UpdateChatLog();
            UpdateHint();

            if (_mc.Ended && world.EndResult != null && _endBanner.style.display == DisplayStyle.None)
            {
                bool won = world.EndResult.Players.Any(p => p.AccountId == _mc.LocalAccountId && p.Won);
                _endBanner.text = won ? "VICTORY" : "DEFEAT";
                _endBanner.EnableInClassList("t-gold", won);
                _endBanner.Show(true);
            }
            if (frame == null) return;

            var rts = frame.Rts;
            if (rts != null)
            {
                _gold.text = rts.Gold.ToString();
                _lumber.text = rts.Lumber.ToString();
                _supply.text = $"{rts.SupplyUsed}/{rts.SupplyCap}";
                _supply.EnableInClassList("t-red", rts.SupplyUsed >= rts.SupplyCap);
            }
            _clock.text = El.FormatTime(Mathf.Max(0f, frame.Time));
            _dayNight.text = frame.IsNight ? "Night" : "Day";

            UpdateSelection(frame);
            UpdateCard(frame);
        }

        private void UpdateDragBox()
        {
            var r = Rts?.DragRect;
            if (!r.HasValue) { _dragBox.Show(false); return; }
            var a = UI.ScreenToPanel(new Vector2(r.Value.xMin, r.Value.yMax));
            var b = UI.ScreenToPanel(new Vector2(r.Value.xMax, r.Value.yMin));
            _dragBox.style.left = Mathf.Min(a.x, b.x);
            _dragBox.style.top = Mathf.Min(a.y, b.y);
            _dragBox.style.width = Mathf.Abs(b.x - a.x);
            _dragBox.style.height = Mathf.Abs(b.y - a.y);
            _dragBox.Show(true);
        }

        private void UpdateHint()
        {
            var rts = Rts;
            if (rts == null) { _hint.text = ""; return; }
            if (rts.Placing != null) _hint.text = rts.PlacementValid ? "Left-click to build (Shift to queue more) · Right-click or Esc to cancel" : "Can't build here";
            else if (rts.AttackMoveArmed) _hint.text = "Left-click a target or the ground to attack-move · Esc to cancel";
            else if (rts.RallyArmed) _hint.text = "Left-click to set the rally point (a vein or trees send new workers to work)";
            else _hint.text = "";
        }

        // ================================================================== selection panel

        private void UpdateSelection(SnapshotFrame frame)
        {
            var views = Rts?.SelectedViews() ?? new List<EntityView>();
            // Cheap signature: rebuild only when what is shown changes (values below are refreshed in place).
            var sig = string.Join(",", views.Select(v => v.Id)) + "|" + (views.Count == 1 ? QueueSig(views[0]) : "");
            if (sig != _selectionSignature)
            {
                _selectionSignature = sig;
                _selection.Clear();
                if (views.Count == 1) BuildSingle(views[0]);
                else if (views.Count > 1) BuildMulti(views);
                else _selection.Add(El.Text("Select units with a click or by dragging a box. Ctrl+1–9 saves a group, 1–9 recalls it.", "t-small", "t-wrap"));
            }
            if (views.Count == 1) RefreshSingle(views[0]);
        }

        private static string QueueSig(EntityView v)
        {
            var s = v.State;
            if (s == null) return "";
            return (s.UnderConstruction ? "c" : "") + string.Join(";", s.TrainQueue ?? new string[0]);
        }

        private Label _selHp, _selDetail;
        private VisualElement _selHpFill, _selProgressFill;

        private void BuildSingle(EntityView v)
        {
            var s = v.State;
            var name = v.Unit?.Name ?? v.Hero?.Name ?? El.Pretty(v.DefId);
            var owner = _mc.Players.FirstOrDefault(p => p.Id == s.OwnerPlayer);
            var head = El.Div("row", "space-between");
            head.Add(El.Text(name, "t-heading"));
            head.Add(El.Text(owner != null ? owner.Name : v.Kind == UnitKind.Resource ? "Resource" : v.Team == Team.Neutral ? "Neutral" : "", "t-small"));
            _selection.Add(head);
            if (!string.IsNullOrEmpty(v.Unit?.Description)) _selection.Add(El.Text(v.Unit.Description, "t-small", "t-wrap"));
            if (v.Kind != UnitKind.Resource)
            {
                var (hpBar, hpFill, hpText) = El.Bar("bar-fill--hp", 18);
                _selHpFill = hpFill; _selHp = hpText;
                hpBar.style.marginTop = 6;
                _selection.Add(hpBar);
            }
            else { _selHpFill = null; _selHp = null; }
            _selDetail = El.Text("", "t-small", "t-wrap");
            _selDetail.style.marginTop = 4;
            _selection.Add(_selDetail);
            _selProgressFill = null;
            bool mine = s.OwnerPlayer == _mc.World.LocalPlayerId;
            if (s.UnderConstruction || (s.TrainQueue != null && s.TrainQueue.Length > 0))
            {
                var (pBar, pFill, pText) = El.Bar("bar-fill--xp", 10);
                pText.text = "";
                _selProgressFill = pFill;
                pBar.style.marginTop = 6;
                _selection.Add(pBar);
            }
            if (mine && s.TrainQueue != null && s.TrainQueue.Length > 0)
            {
                var row = El.Div("row");
                row.style.marginTop = 6;
                for (int i = 0; i < s.TrainQueue.Length; i++)
                {
                    int slot = i;
                    var id = s.TrainQueue[i];
                    var tile = El.Btn(Short(_mc.Data.Units.TryGetValue(id ?? "", out var qd) ? qd.Name : id), () => Rts?.CancelQueue(v.Id, slot), "btn--small");
                    tile.style.width = 70;
                    UI.AttachTooltip(tile, qd?.Name ?? id, "Click to cancel and get a full refund.");
                    row.Add(tile);
                }
                _selection.Add(row);
            }
        }

        private void RefreshSingle(EntityView v)
        {
            var s = v.State;
            if (s == null) return;
            if (_selHpFill != null)
            {
                El.SetFill(_selHpFill, s.MaxHp > 0 ? s.Hp / s.MaxHp : 0f);
                _selHp.text = $"{Mathf.CeilToInt(s.Hp)} / {Mathf.RoundToInt(s.MaxHp)}";
            }
            string detail;
            if (v.Kind == UnitKind.Resource) detail = $"Blood-iron left: {s.ResourceAmount:N0}";
            else if (s.UnderConstruction) detail = $"Under construction: {Mathf.RoundToInt(s.BuildProgress * 100)}%";
            else if (s.TrainQueue != null && s.TrainQueue.Length > 0) detail = $"Training {(_mc.Data.Units.TryGetValue(s.TrainQueue[0] ?? "", out var td) ? td.Name : s.TrainQueue[0])}: {Mathf.RoundToInt(s.TrainProgress * 100)}%";
            else if (v.Kind == UnitKind.Worker)
                detail = s.CarryGold > 0 ? $"Carrying {s.CarryGold} blood-iron" : s.CarryLumber > 0 ? $"Carrying {s.CarryLumber} lumber" : s.Action == ActionState.Working ? "Working" : "Idle";
            else detail = $"Damage {Mathf.RoundToInt(s.Damage)} · Armor {s.Armor:0.#}";
            if (v.Unit != null && v.Unit.SupplyProvided > 0 && !s.UnderConstruction) detail += $" · Provides {v.Unit.SupplyProvided} supply";
            if (v.Remembered) detail += " (last seen)";
            _selDetail.text = detail;
            if (_selProgressFill != null) El.SetFill(_selProgressFill, s.UnderConstruction ? s.BuildProgress : s.TrainProgress);
        }

        private void BuildMulti(List<EntityView> views)
        {
            _selection.Add(El.Text($"{views.Count} selected", "t-heading"));
            var grid = El.Div("row");
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.marginTop = 6;
            foreach (var v in views.Take(30))
            {
                var id = v.Id;
                var tile = El.Btn(Short(v.Unit?.Name ?? v.Hero?.Name ?? v.DefId), () =>
                {
                    if (InputBridge.Shift) { Rts.Selection.Remove(id); _selectionSignature = ""; }
                    else Rts.Select(new[] { id });
                }, "btn--small");
                tile.style.width = 64;
                float hp = v.State != null && v.State.MaxHp > 0 ? v.State.Hp / v.State.MaxHp : 1f;
                tile.style.borderBottomColor = hp > 0.6f ? new Color(0.3f, 0.9f, 0.3f) : hp > 0.3f ? new Color(0.95f, 0.8f, 0.2f) : new Color(0.95f, 0.25f, 0.2f);
                tile.style.borderBottomWidth = 3;
                grid.Add(tile);
            }
            _selection.Add(grid);
        }

        private static string Short(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            var words = name.Split(' ');
            var last = words[words.Length - 1];
            return last.Length <= 8 ? last : last.Substring(0, 7) + ".";
        }

        // ================================================================== command card

        private void UpdateCard(SnapshotFrame frame)
        {
            var rts = Rts;
            _buttons.Clear();
            var own = rts?.SelectedOwn() ?? new List<EntityView>();
            if (own.Count > 0 && !_mc.Client.IsSpectator) Fill(own, frame);
            // Rebuild the visual card only when its content (labels, availability) changed.
            var sig = string.Join("|", _buttons.Select(b => b.Label + b.Sub + b.Enabled));
            if (sig == _cardSignature) return;
            _cardSignature = sig;
            _card.Clear();
            foreach (var b in _buttons)
            {
                var btn = new Button(() => { if (b.Enabled) b.Do(); }) { text = "" };
                btn.AddToClassList("btn");
                btn.AddToClassList("btn--small");
                btn.style.width = 92; btn.style.height = 58;
                btn.style.marginRight = 4; btn.style.marginBottom = 4;
                btn.style.flexDirection = FlexDirection.Column;
                btn.Add(El.Text(b.Label, "t-small"));
                var sub = El.Text((b.Sub ?? "") + (b.Key != KeyCode.None ? $" [{KeyName(b.Key)}]" : ""), "t-small");
                sub.style.opacity = 0.75f;
                btn.Add(sub);
                btn.SetEnabled(b.Enabled);
                if (!string.IsNullOrEmpty(b.Tooltip)) UI.AttachTooltip(btn, b.Label, b.Tooltip);
                _card.Add(btn);
            }
        }

        private void Fill(List<EntityView> own, SnapshotFrame frame)
        {
            var rts = Rts;
            var primary = own[0];
            var data = _mc.Data;
            if (primary.IsStructure)
            {
                var s = primary.State;
                if (!s.UnderConstruction && primary.Unit?.Trains != null)
                {
                    foreach (var id in primary.Unit.Trains)
                    {
                        if (!data.Units.TryGetValue(id, out var d)) continue;
                        bool can = rts.Affordable(d, out var why) & rts.RequirementsMet(d, out var req);
                        var unitId = id;
                        Add(d.Name, Cost(d), Key(d.Hotkey), () => rts.Train(unitId), can,
                            $"{d.Description}\n{Cost(d)} · {d.SupplyCost} supply · {d.BuildTime:0} s" + (req != null ? "\n" + req : why != null ? "\n" + why : ""));
                    }
                    Add("Rally", "", KeyCode.Y, rts.ArmRally, true, "Set where new units gather. A vein or trees put new workers to work.");
                }
                if (s.UnderConstruction) Add("Cancel", "75% refund", KeyCode.X, () => rts.CancelQueue(primary.Id, 0), true, "Cancel construction and recover 75% of the cost.");
                else if (s.TrainQueue != null && s.TrainQueue.Length > 0) Add("Cancel", "last in queue", KeyCode.X, () => rts.CancelQueue(primary.Id, -1), true, "Cancel the last queued unit (full refund).");
                return;
            }
            bool workers = own.Any(v => v.Kind == UnitKind.Worker);
            if (_buildMenu && workers)
            {
                var worker = own.First(v => v.Kind == UnitKind.Worker);
                foreach (var id in worker.Unit?.Builds ?? new List<string>())
                {
                    if (!data.Units.TryGetValue(id, out var d)) continue;
                    bool can = rts.Affordable(d, out var why) & rts.RequirementsMet(d, out var req);
                    var bid = id;
                    Add(d.Name, Cost(d), Key(d.Hotkey), () => { rts.BeginPlacement(bid); _buildMenu = false; }, can,
                        $"{d.Description}\n{Cost(d)} · {d.BuildTime:0} s" + (d.SelfBuilds ? " · rises on its own" : "") + (req != null ? "\n" + req : why != null ? "\n" + why : ""));
                }
                Add("Back", "Esc", KeyCode.None, () => _buildMenu = false, true, "");
                return;
            }
            Add("Attack", "", KeyCode.A, rts.ArmAttackMove, true, "Attack a target, or attack-move to a point.");
            Add("Stop", "", KeyCode.S, rts.Stop, true, "");
            Add("Hold", "", KeyCode.H, rts.Hold, true, "Hold position.");
            if (workers)
            {
                Add("Build", "", KeyCode.B, () => _buildMenu = true, true, "Open the build menu.");
                bool carrying = own.Any(v => v.Kind == UnitKind.Worker && (v.State.CarryGold > 0 || v.State.CarryLumber > 0));
                Add("Return", "cargo", KeyCode.R, rts.ReturnCargo, carrying, "Take carried resources to the nearest hall, then go back to work.");
                Add("Harvest", "right-click", KeyCode.None, () => { }, true, "Right-click a blood-iron vein or trees to harvest.");
            }
        }

        private void Add(string label, string sub, KeyCode key, Action a, bool enabled, string tooltip) =>
            _buttons.Add(new CardButton { Label = label, Sub = sub, Key = key, Do = a, Enabled = enabled, Tooltip = tooltip });

        private static string Cost(UnitDef d) => d.LumberCost > 0 ? $"{d.GoldCost} / {d.LumberCost}" : $"{d.GoldCost}";

        private static KeyCode Key(string hotkey)
        {
            if (string.IsNullOrEmpty(hotkey) || hotkey.Length != 1) return KeyCode.None;
            char c = char.ToUpperInvariant(hotkey[0]);
            return c >= 'A' && c <= 'Z' ? KeyCode.A + (c - 'A') : KeyCode.None;
        }

        private static string KeyName(KeyCode k) => k.ToString();

        // ================================================================== chat & menu

        private void UpdateChatLog()
        {
            var client = _mc.Client;
            if (client.ChatLog.Count == _chatCount) return;
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
                _chatLog.Add(line);
                while (_chatLog.childCount > 8) _chatLog.RemoveAt(0);
            }
            _chatCount = client.ChatLog.Count;
        }

        private void OpenChat(bool team)
        {
            _chatTeam = team;
            _chatInput.label = team ? "[Team]" : "[All]";
            _chatInput.Show(true);
            _chatInput.value = "";
            _chatInput.Focus();
            if (Rts != null) Rts.KeyboardCaptured = true;
        }

        private void CloseChat()
        {
            _chatInput.Show(false);
            _chatInput.Blur();
            if (Rts != null) Rts.KeyboardCaptured = false;
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
            B("Surrender (-ff)", () => { _mc?.SendChat("-ff", true); ToggleMenu(); });
            B("Leave Match", () => UI.Dialog("Leave Match", _mc != null && _mc.Online && !_mc.Ended
                ? "The match is still running. Leaving now counts as an abandon; the AI takes over your base."
                : "Leave this match?", ("Leave", "btn--danger", () => _mc?.Leave()), ("Stay", "btn--ghost", null)), "btn--danger");
            if (_mc != null && !_mc.Online) _mc.Paused = true;
            _menu = UI.ShowOverlay(d, ToggleMenu);
        }

        public override bool OnEscape()
        {
            var rts = Rts;
            if (rts != null && (rts.Placing != null || rts.AttackMoveArmed || rts.RallyArmed)) { rts.CancelModes(); return true; }
            if (_buildMenu) { _buildMenu = false; return true; }
            ToggleMenu();
            return true;
        }
    }
}
