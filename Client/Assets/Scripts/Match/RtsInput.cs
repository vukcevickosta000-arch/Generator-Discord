using System;
using System.Collections.Generic;
using System.Linq;
using Bloodfall.Client.Core;
using Bloodfall.Client.Input;
using Bloodfall.Data;
using Bloodfall.Protocol;
using Bloodfall.Simulation;
using UnityEngine;
using NVec2 = System.Numerics.Vector2;

namespace Bloodfall.Client.Match
{
    /// <summary>
    /// RTS controls: click, box and double-click selection, control groups, the smart right-click (move, attack,
    /// harvest, help build, return cargo, rally), attack-move, and building placement with a ghost that checks the
    /// ground locally. Like the MOBA input it only *requests*: the server re-validates every order (ownership, cost,
    /// supply, requirements and placement), so the local checks exist purely for instant feedback.
    /// </summary>
    public sealed class RtsInput
    {
        private readonly MatchWorld _world;
        private readonly ClientSettings _settings;
        public readonly List<int> Selection = new List<int>();
        private readonly Dictionary<int, List<int>> _groups = new Dictionary<int, List<int>>();
        public EntityView Hover { get; private set; }
        public Vector3 CursorWorld { get; private set; }
        public bool CursorOnGround { get; private set; }
        /// <summary>Set by the HUD while a text field has focus (chat) so hotkeys don't fire.</summary>
        public bool KeyboardCaptured;
        /// <summary>Building id being placed (null when not placing).</summary>
        public string Placing { get; private set; }
        public bool PlacementValid { get; private set; }
        public bool AttackMoveArmed { get; private set; }
        public bool RallyArmed { get; private set; }
        /// <summary>Screen-space selection rectangle while dragging (for the HUD to draw).</summary>
        public Rect? DragRect { get; private set; }
        public event Action<string> LocalError;
        public event Action SelectionChanged;

        private Vector2 _dragStart;
        private bool _pressed;
        private float _lastClickTime;
        private int _lastClickId;
        private float _lastGroupTime;
        private int _lastGroup = -1;
        private ModelInstance _ghost;
        private string _ghostKey;
        private GameObject _ghostRing;
        private Mesh _ghostRingMesh;
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private const float DragThreshold = 8f;

        public RtsInput(MatchWorld world, ClientSettings settings)
        {
            _world = world;
            _settings = settings;
        }

        private MatchController Mc => _world.Controller;
        private SnapshotFrame Frame => Mc.Client.Latest;
        private int Me => _world.LocalPlayerId;

        public bool Mine(EntityView v) => v?.State != null && v.State.OwnerPlayer == Me && !v.Dying && !v.State.Has(EntityFlags.Dead);

        /// <summary>Selected views that are still alive and visible.</summary>
        public List<EntityView> SelectedViews()
        {
            var list = new List<EntityView>(Selection.Count);
            foreach (var id in Selection)
                if (_world.TryGetView(id, out var v) && (v.Present || v.Remembered) && !v.Dying && v.State != null && !v.State.Has(EntityFlags.Dead)) list.Add(v);
            return list;
        }

        public List<EntityView> SelectedOwn() => SelectedViews().Where(Mine).ToList();

        // ================================================================== per frame

        public void Update(float dt, bool pointerOverUi)
        {
            var mouse = InputBridge.MousePosition;
            CursorOnGround = _world.Camera.RaycastGround(mouse, out var ground);
            if (CursorOnGround) CursorWorld = ground;
            var prevHover = Hover;
            Hover = pointerOverUi ? null : MatchInput.PickEntity(_world, mouse);
            if (prevHover != null && prevHover != Hover) prevHover.Hovered = false;
            if (Hover != null) Hover.Hovered = true;

            PruneSelection();
            foreach (var v in _world.Views) v.Selected = Selection.Contains(v.Id);
            UpdateGhost();
            UpdateCursor();

            if (Mc.Client.IsSpectator)
            {
                if (!pointerOverUi && InputBridge.GetMouseButtonDown(0)) Select(Hover != null ? new[] { Hover.Id } : new int[0]);
                return;
            }
            if (!KeyboardCaptured) HandleKeys();

            if (InputBridge.GetMouseButtonDown(0) && !pointerOverUi) OnLeftDown(mouse);
            if (_pressed)
            {
                if (Vector2.Distance(mouse, _dragStart) > DragThreshold)
                    DragRect = new Rect(Mathf.Min(mouse.x, _dragStart.x), Mathf.Min(mouse.y, _dragStart.y), Mathf.Abs(mouse.x - _dragStart.x), Mathf.Abs(mouse.y - _dragStart.y));
                if (InputBridge.GetMouseButtonUp(0)) OnLeftUp(mouse);
            }
            if (InputBridge.GetMouseButtonDown(1) && !pointerOverUi) OnRightClick();
        }

        private void PruneSelection()
        {
            int before = Selection.Count;
            Selection.RemoveAll(id => !_world.TryGetView(id, out var v) || v.Dying || v.State == null || v.State.Has(EntityFlags.Dead) || (!v.Present && !v.Remembered));
            if (Selection.Count != before) SelectionChanged?.Invoke();
        }

        private void HandleKeys()
        {
            for (int n = 0; n <= 9; n++)
            {
                var key = n == 0 ? KeyCode.Alpha0 : KeyCode.Alpha1 + (n - 1);
                if (!InputBridge.GetKeyDown(key)) continue;
                if (InputBridge.Ctrl) { _groups[n] = SelectedOwn().Select(v => v.Id).ToList(); continue; }
                if (InputBridge.Shift) { var g = _groups.TryGetValue(n, out var cur) ? cur : (_groups[n] = new List<int>()); foreach (var id in SelectedOwn().Select(v => v.Id)) if (!g.Contains(id)) g.Add(id); continue; }
                if (!_groups.TryGetValue(n, out var group) || group.Count == 0) continue;
                Select(group.Where(id => _world.TryGetView(id, out var gv) && Mine(gv)).ToList());
                if (_lastGroup == n && Time.unscaledTime - _lastGroupTime < 0.4f) CenterOnSelection();
                _lastGroup = n;
                _lastGroupTime = Time.unscaledTime;
            }
            if (InputBridge.GetKeyDown(KeyCode.Space)) CenterOnSelection();
        }

        public void CancelModes()
        {
            Placing = null;
            AttackMoveArmed = false;
            RallyArmed = false;
        }

        public void CenterOnSelection()
        {
            var views = SelectedViews();
            if (views.Count == 0) return;
            var c = Vector3.zero;
            foreach (var v in views) c += v.Position;
            _world.Camera.JumpTo(c / views.Count);
        }

        // ================================================================== selection

        public void Select(IList<int> ids, bool add = false)
        {
            if (!add) Selection.Clear();
            foreach (var id in ids) if (!Selection.Contains(id)) Selection.Add(id);
            // Units come first so the command card shows what the player most likely wants.
            Selection.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            SelectionChanged?.Invoke();
        }

        private int Rank(int id)
        {
            if (!_world.TryGetView(id, out var v)) return 9;
            if (!Mine(v)) return 5;
            return v.Kind == UnitKind.Soldier || v.Kind == UnitKind.Hero ? 0 : v.Kind == UnitKind.Worker ? 1 : 2;
        }

        private void OnLeftDown(Vector2 mouse)
        {
            bool queue = InputBridge.Shift;
            if (Placing != null) { ConfirmPlacement(queue); return; }
            if (AttackMoveArmed)
            {
                var units = SelectedOwn().Where(v => !v.IsStructure && v.Kind != UnitKind.Worker).Select(v => v.Id).ToList();
                if (units.Count == 0) units = SelectedOwn().Where(v => !v.IsStructure).Select(v => v.Id).ToList();
                if (Hover != null && !Hover.Dying && !Mine(Hover)) SendGroup(new Order { Type = OrderType.AttackUnit, TargetId = Hover.Id, Queue = queue }, units);
                else if (CursorOnGround) { SendGroup(new Order { Type = OrderType.AttackMove, Point = ToSim(CursorWorld), Queue = queue }, units); _world.Vfx.Play("attack_marker", CursorWorld); }
                if (!queue) AttackMoveArmed = false;
                return;
            }
            if (RallyArmed)
            {
                SetRally();
                RallyArmed = false;
                return;
            }
            _pressed = true;
            _dragStart = mouse;
            DragRect = null;
        }

        private void OnLeftUp(Vector2 mouse)
        {
            _pressed = false;
            bool add = InputBridge.Shift;
            if (DragRect.HasValue)
            {
                var r = DragRect.Value;
                DragRect = null;
                var cam = _world.Camera.Cam;
                var inside = new List<EntityView>();
                foreach (var v in _world.Views)
                {
                    if (!Mine(v) || !v.Present) continue;
                    var sp = cam.WorldToScreenPoint(v.Point(0.4f));
                    if (sp.z > 0 && r.Contains(new Vector2(sp.x, sp.y))) inside.Add(v);
                }
                // A box takes the army if it holds any; buildings only when nothing else is inside.
                var units = inside.Where(v => !v.IsStructure).ToList();
                Select((units.Count > 0 ? units : inside).Take(Order.MaxGroup).Select(v => v.Id).ToList(), add);
                return;
            }
            if (Hover == null) { if (!add) Select(new int[0]); return; }
            bool doubleClick = Hover.Id == _lastClickId && Time.unscaledTime - _lastClickTime < 0.35f;
            _lastClickId = Hover.Id;
            _lastClickTime = Time.unscaledTime;
            if (!Mine(Hover)) { Select(new[] { Hover.Id }); return; }
            if (doubleClick || InputBridge.Ctrl)
            {
                // Everything of the same kind on screen.
                var cam = _world.Camera.Cam;
                var same = _world.Views.Where(v => Mine(v) && v.Present && v.DefId == Hover.DefId).Where(v =>
                {
                    var sp = cam.WorldToScreenPoint(v.Position);
                    return sp.z > 0 && sp.x >= 0 && sp.y >= 0 && sp.x <= Screen.width && sp.y <= Screen.height;
                }).Take(Order.MaxGroup).Select(v => v.Id).ToList();
                Select(same, add);
                return;
            }
            if (add && Selection.Contains(Hover.Id)) { Selection.Remove(Hover.Id); SelectionChanged?.Invoke(); }
            else Select(new[] { Hover.Id }, add);
        }

        // ================================================================== commands

        private void OnRightClick()
        {
            if (Placing != null || AttackMoveArmed || RallyArmed) { CancelModes(); return; }
            var own = SelectedOwn();
            if (own.Count == 0) return;
            bool queue = InputBridge.Shift;
            var units = own.Where(v => !v.IsStructure).ToList();
            var buildings = own.Where(v => v.IsStructure).ToList();
            var workers = units.Where(v => v.Kind == UnitKind.Worker).ToList();
            var others = units.Where(v => v.Kind != UnitKind.Worker).ToList();
            var h = Hover != null && !Hover.Dying ? Hover : null;
            var point = ToSim(CursorWorld);

            if (buildings.Count > 0 && units.Count == 0) { SetRally(); return; }

            if (h != null && h.State != null && !Mine(h) && h.Kind != UnitKind.Resource && h.Team != _world.LocalTeam)
            {
                SendGroup(new Order { Type = OrderType.AttackUnit, TargetId = h.Id, Queue = queue }, units.Select(v => v.Id));
                return;
            }
            if (h != null && h.Kind == UnitKind.Resource)
            {
                SendGroup(new Order { Type = OrderType.Harvest, TargetId = h.Id, Queue = queue }, workers.Select(v => v.Id));
                Move(others, h.State != null ? h.State.Position : point, queue);
                return;
            }
            if (h != null && Mine(h) && h.State.UnderConstruction && workers.Count > 0)
            {
                SendGroup(new Order { Type = OrderType.Build, TargetId = h.Id, Queue = queue }, workers.Select(v => v.Id));
                Move(others, h.State.Position, queue);
                return;
            }
            if (h != null && Mine(h) && h.Unit != null && (h.Unit.DropOffGold || h.Unit.DropOffLumber) && !h.State.UnderConstruction)
            {
                var carrying = workers.Where(w => w.State.CarryGold > 0 || w.State.CarryLumber > 0).ToList();
                SendGroup(new Order { Type = OrderType.ReturnResources, Queue = queue }, carrying.Select(v => v.Id));
                Move(units.Except(carrying).ToList(), h.State.Position, queue);
                return;
            }
            if (!CursorOnGround) return;
            if (workers.Count > 0 && TreeNear(CursorWorld, 1.4f))
            {
                SendGroup(new Order { Type = OrderType.Harvest, Point = point, Queue = queue }, workers.Select(v => v.Id));
                Move(others, point, queue);
                return;
            }
            Move(units, point, queue);
            _world.Vfx.Play("move_marker", CursorWorld);
        }

        private void Move(List<EntityView> units, NVec2 point, bool queue)
        {
            if (units.Count == 0) return;
            SendGroup(new Order { Type = OrderType.Move, Point = point, Queue = queue }, units.Select(v => v.Id));
        }

        /// <summary>Right-click / rally button with buildings selected: a point, a vein, a tree line or a unit.</summary>
        private void SetRally()
        {
            var buildings = SelectedOwn().Where(v => v.IsStructure && v.Unit?.Trains != null && v.Unit.Trains.Count > 0).ToList();
            if (buildings.Count == 0) { Error("Select a building that trains units."); return; }
            var h = Hover != null && !Hover.Dying ? Hover : null;
            var o = new Order { Type = OrderType.SetRally, Point = ToSim(CursorWorld), TargetId = h != null && h.Id != buildings[0].Id ? h.Id : 0 };
            SendGroup(o, buildings.Select(v => v.Id));
            _world.Vfx.Play("move_marker", CursorWorld);
        }

        public void ArmRally() { CancelModes(); RallyArmed = true; }
        public void ArmAttackMove() { CancelModes(); AttackMoveArmed = true; }

        public void Stop() => SendGroup(new Order { Type = OrderType.Stop }, SelectedOwn().Where(v => !v.IsStructure).Select(v => v.Id));
        public void Hold() => SendGroup(new Order { Type = OrderType.Hold }, SelectedOwn().Where(v => !v.IsStructure).Select(v => v.Id));

        public void ReturnCargo() =>
            SendGroup(new Order { Type = OrderType.ReturnResources, Queue = InputBridge.Shift },
                SelectedOwn().Where(v => v.Kind == UnitKind.Worker && (v.State.CarryGold > 0 || v.State.CarryLumber > 0)).Select(v => v.Id));

        /// <summary>Trains at the selected building(s) of the right type; the server picks the shortest queue.</summary>
        public void Train(string unitId)
        {
            var buildings = SelectedOwn().Where(v => v.IsStructure && v.Unit?.Trains != null && v.Unit.Trains.Contains(unitId)).ToList();
            if (buildings.Count == 0) return;
            if (!_world.Data.Units.TryGetValue(unitId, out var def)) return;
            if (!Affordable(def, out var err)) { Error(err); return; }
            if (!RequirementsMet(def, out err)) { Error(err); return; }
            SendGroup(new Order { Type = OrderType.Train, ItemId = unitId }, buildings.Select(v => v.Id));
        }

        public void Research(string upgradeId)
        {
            var building = SelectedOwn().FirstOrDefault(v => v.IsStructure && v.State != null && !v.State.UnderConstruction && v.Unit?.Research != null && v.Unit.Research.Contains(upgradeId));
            if (building == null || !_world.Data.Upgrades.TryGetValue(upgradeId, out var up)) return;
            if (!CanResearch(up, out var err)) { Error(err); return; }
            Mc.SendOrder(new Order { Type = OrderType.Research, UnitId = building.Id, ItemId = upgradeId });
        }

        public bool Researched(string upgradeId) => Frame?.Rts?.Upgrades != null && Frame.Rts.Upgrades.Contains(upgradeId);

        /// <summary>Mirrors Match.TryResearch with what the client knows (the server's check is final).</summary>
        public bool CanResearch(UpgradeDef up, out string error)
        {
            error = null;
            var rts = Frame?.Rts;
            if (Researched(up.Id)) { error = "Already researched."; return false; }
            if (Frame != null && Frame.Entities.Any(e => e.OwnerPlayer == Me && e.TrainQueue != null && e.TrainQueue.Contains(up.Id))) { error = "Already being researched."; return false; }
            foreach (var req in up.RequiresUpgrades ?? new List<string>())
            {
                if (Researched(req)) continue;
                error = "Requires " + (_world.Data.Upgrades.TryGetValue(req, out var ru) ? ru.Name : req) + ".";
                return false;
            }
            foreach (var req in up.Requires ?? new List<string>())
            {
                bool have = Frame != null && Frame.Entities.Any(e => e.OwnerPlayer == Me && e.DefId == req && !e.UnderConstruction && !e.Has(EntityFlags.Dead));
                if (have) continue;
                error = "Requires " + (_world.Data.Units.TryGetValue(req, out var rd) ? rd.Name : req) + ".";
                return false;
            }
            if (rts != null && rts.Gold < up.GoldCost) { error = "Not enough blood-iron."; return false; }
            if (rts != null && rts.Lumber < up.LumberCost) { error = "Not enough lumber."; return false; }
            return true;
        }

        public void CancelQueue(int buildingId, int slot) => Mc.SendOrder(new Order { Type = OrderType.CancelQueue, UnitId = buildingId, Slot = slot });

        public void BeginPlacement(string buildingId)
        {
            if (!_world.Data.Units.TryGetValue(buildingId, out var def)) return;
            if (!SelectedOwn().Any(v => v.Kind == UnitKind.Worker && v.Unit?.Builds != null && v.Unit.Builds.Contains(buildingId))) { Error("Select a worker that can build that."); return; }
            if (!Affordable(def, out var err)) { Error(err); return; }
            if (!RequirementsMet(def, out err)) { Error(err); return; }
            CancelModes();
            Placing = buildingId;
        }

        private void ConfirmPlacement(bool queue)
        {
            if (!_world.Data.Units.TryGetValue(Placing ?? "", out var def)) { CancelModes(); return; }
            if (!CursorOnGround) return;
            if (!CheckPlacement(def, CursorWorld, out var err)) { Error(err); return; }
            if (!Affordable(def, out err)) { Error(err); CancelModes(); return; }
            // The closest selected worker that can build it takes the job.
            var site = ToSim(CursorWorld);
            var worker = SelectedOwn().Where(v => v.Kind == UnitKind.Worker && v.Unit?.Builds != null && v.Unit.Builds.Contains(def.Id))
                                      .OrderBy(v => NVec2.Distance(v.State.Position, site)).FirstOrDefault();
            if (worker == null) { Error("Select a worker that can build that."); CancelModes(); return; }
            Mc.SendOrder(new Order { Type = OrderType.Build, UnitId = worker.Id, ItemId = def.Id, Point = site, Queue = queue });
            GameApp.Instance?.Audio?.PlayUi("click");
            if (!queue) Placing = null;
        }

        // ================================================================== local checks (feedback only)

        public bool Affordable(UnitDef def, out string error)
        {
            error = null;
            var rts = Frame?.Rts;
            if (rts == null) return true;
            if (rts.Gold < def.GoldCost) { error = "Not enough blood-iron."; return false; }
            if (rts.Lumber < def.LumberCost) { error = "Not enough lumber."; return false; }
            if (def.Kind != UnitKind.Building && rts.SupplyUsed + def.SupplyCost > rts.SupplyCap) { error = "Not enough supply."; return false; }
            return true;
        }

        public bool RequirementsMet(UnitDef def, out string error)
        {
            error = null;
            if (def.Requires == null) return true;
            foreach (var req in def.Requires)
            {
                bool have = Frame != null && Frame.Entities.Any(e => e.OwnerPlayer == Me && e.DefId == req && !e.UnderConstruction && !e.Has(EntityFlags.Dead));
                if (have) continue;
                error = "Requires " + (_world.Data.Units.TryGetValue(req, out var rd) ? rd.Name : req) + ".";
                return false;
            }
            return true;
        }

        /// <summary>Mirrors Match.CanPlaceBuilding with what the client knows (the server's check is final).</summary>
        public bool CheckPlacement(UnitDef def, Vector3 world, out string error)
        {
            error = null;
            var pos = ToSim(world);
            var grid = _world.Grid;
            float r = def.CollisionRadius;
            if (pos.X - r < 1f || pos.Y - r < 1f || pos.X + r > grid.WorldWidth - 1f || pos.Y + r > grid.WorldHeight - 1f) { error = "Can't build outside the battlefield."; return false; }
            foreach (int i in grid.CellsInDisc(pos, r * 0.85f))
            {
                int x = i % grid.Width, y = i / grid.Width;
                if (!grid.IsWalkableCell(x, y) || grid.IsWaterCell(x, y)) { error = "Can't build there."; return false; }
            }
            if (Frame != null)
            {
                foreach (var e in Frame.Entities)
                {
                    if (e.Has(EntityFlags.Dead) || e.DefId == null || !_world.Data.Units.TryGetValue(e.DefId, out var ed)) continue;
                    float d = NVec2.Distance(e.Position, pos);
                    if (e.Kind == UnitKind.Resource && d < r + ed.CollisionRadius + _world.Data.Rules.RtsMineClearance) { error = "Too close to a blood-iron vein."; return false; }
                    if ((e.Kind == UnitKind.Building || e.Kind == UnitKind.Resource) && d < (r + ed.CollisionRadius) * 0.85f) { error = "Can't build there."; return false; }
                    if (e.Team != _world.LocalTeam && e.Kind != UnitKind.Building && e.Kind != UnitKind.Resource && d < r + ed.CollisionRadius) { error = "Something is in the way."; return false; }
                }
            }
            return true;
        }

        private bool TreeNear(Vector3 world, float radius)
        {
            var grid = _world.Grid;
            foreach (int i in grid.CellsInDisc(ToSim(world), radius))
                if (grid.IsTreeCell(i % grid.Width, i / grid.Width)) return true;
            return false;
        }

        // ================================================================== ghost & cursor

        private void UpdateGhost()
        {
            UnitDef def = null;
            bool show = Placing != null && CursorOnGround && _world.Data.Units.TryGetValue(Placing, out def);
            if (!show)
            {
                if (_ghost != null) _ghost.Root.SetActive(false);
                if (_ghostRing != null) _ghostRing.SetActive(false);
                return;
            }
            PlacementValid = CheckPlacement(def, CursorWorld, out _);
            if (_ghost == null || _ghostKey != def.Id)
            {
                if (_ghost != null) UnityEngine.Object.Destroy(_ghost.Root);
                _ghost = ModelFactory.Create(def.Model, _world.LocalTeam, def.Kind, def.ModelScale, def.CollisionRadius, _world.Root);
                _ghost.Root.name = "PlacementGhost";
                _ghostKey = def.Id;
            }
            _ghost.Root.SetActive(true);
            _ghost.Root.transform.position = CursorWorld;
            var rim = PlacementValid ? new Color(0.3f, 1f, 0.4f, 0.9f) : new Color(1f, 0.2f, 0.15f, 0.9f);
            foreach (var rend in _ghost.Renderers)
            {
                if (rend == null) continue;
                rend.GetPropertyBlock(_mpb);
                _mpb.SetFloat(DissolveId, 0.4f);
                _mpb.SetColor(RimColorId, rim);
                rend.SetPropertyBlock(_mpb);
            }
            float size = def.CollisionRadius * 2f;
            var center = new Vector2(CursorWorld.x, CursorWorld.z);
            if (_ghostRing == null)
            {
                _ghostRing = Decals.Create(_world.Root, _world.Map, center, size, "selection_ring", rim, true, 0f, 6);
                _ghostRingMesh = _ghostRing.GetComponent<MeshFilter>().sharedMesh;
            }
            _ghostRing.SetActive(true);
            Decals.Conform(_ghostRingMesh, _world.Map, center, size, 0f, rim, 6);
        }

        private void UpdateCursor()
        {
            CursorKind k = CursorKind.Default;
            if (AttackMoveArmed) k = CursorKind.Attack;
            else if (Placing != null || RallyArmed) k = CursorKind.Cast;
            else if (Hover != null) k = _world.IsEnemy(Hover.Team) && Hover.Team != Team.Neutral ? CursorKind.Attack : CursorKind.Ally;
            CursorManager.Apply(k);
        }

        // ================================================================== helpers

        private void SendGroup(Order o, IEnumerable<int> ids)
        {
            var list = ids.Take(Order.MaxGroup).ToList();
            if (list.Count == 0) return;
            o.UnitId = list[0];
            o.Group = list.Count > 1 ? list.Skip(1).ToArray() : null;
            Mc.SendOrder(o);
        }

        private void Error(string msg)
        {
            GameApp.Instance?.Audio?.PlayUi("error");
            LocalError?.Invoke(msg);
        }

        public static NVec2 ToSim(Vector3 w) => new NVec2(w.x, w.z);

        public void Dispose()
        {
            CursorManager.Apply(CursorKind.Default);
            if (_ghost != null) UnityEngine.Object.Destroy(_ghost.Root);
            if (_ghostRing != null) UnityEngine.Object.Destroy(_ghostRing);
        }
    }
}
