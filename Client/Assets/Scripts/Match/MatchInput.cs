using System.Collections.Generic;
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
    /// Translates mouse/keyboard into orders for the local hero. The client only *requests*; the server validates
    /// range, mana, cooldowns, targets and ownership. Local pre-checks exist purely for instant feedback.
    /// </summary>
    public sealed class MatchInput
    {
        private readonly MatchWorld _world;
        private readonly ClientSettings _settings;
        public EntityView Hover { get; private set; }
        public Vector3 CursorWorld { get; private set; }
        public bool CursorOnGround { get; private set; }
        /// <summary>Targeting in progress (ability or item awaiting a click).</summary>
        public bool Targeting => _targetSlot >= 0 || _attackMove;
        public bool AttackMoveArmed => _attackMove;
        public int TargetSlot => _targetSlot;
        public AbilityDef TargetAbility => _targetDef;
        /// <summary>Set by the HUD while a text field has focus (chat) so hotkeys don't fire.</summary>
        public bool KeyboardCaptured;
        public int SelectedId;
        public event System.Action<string> LocalError;

        private int _targetSlot = -1;
        private AbilityDef _targetDef;
        private int _targetLevel;
        private bool _attackMove;
        private GameObject _rangeRing, _aoe, _line;
        private Mesh _rangeMesh, _aoeMesh, _lineMesh;
        private static readonly KeyCode[] Alpha = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6 };

        public MatchInput(MatchWorld world, ClientSettings settings)
        {
            _world = world;
            _settings = settings;
        }

        private MatchController Mc => _world.Controller;
        private PrivateState Me => Mc.Client.Latest?.Me;

        private EntityState MyHero => Mc.LocalHero;

        public void Update(float dt, bool pointerOverUi)
        {
            var mouse = InputBridge.MousePosition;
            CursorOnGround = _world.Camera.RaycastGround(mouse, out var ground);
            if (CursorOnGround) CursorWorld = ground;
            var prevHover = Hover;
            Hover = pointerOverUi ? null : Pick(mouse);
            if (prevHover != null && prevHover != Hover) prevHover.Hovered = false;
            if (Hover != null) Hover.Hovered = true;

            UpdateIndicators();
            UpdateCursor();
            if (Mc.Client.IsSpectator || MyHero == null) { if (!pointerOverUi && InputBridge.GetMouseButtonDown(0)) SelectedId = Hover?.Id ?? 0; return; }

            if (!KeyboardCaptured) HandleHotkeys();
            if (pointerOverUi) return;

            bool queue = InputBridge.Shift;
            if (InputBridge.GetMouseButtonDown(0))
            {
                if (InputBridge.GetKey(KeyBinds.Get(_settings, KeyBinds.Ping)) && CursorOnGround)
                {
                    Send(new Order { Type = OrderType.Ping, UnitId = MyHero.Id, Point = ToSim(CursorWorld), Slot = Hover != null && _world.IsEnemy(Hover.Team) ? (int)PingKind.Attack : (int)PingKind.Normal });
                }
                else if (_targetSlot >= 0) ConfirmTarget(queue);
                else if (_attackMove)
                {
                    if (Hover != null && !Hover.Dying && Hover.Id != MyHero.Id) Send(Order.Attack(MyHero.Id, Hover.Id, queue));
                    else if (CursorOnGround) { Send(Order.AttackMoveTo(MyHero.Id, ToSim(CursorWorld), queue)); _world.Vfx.Play("attack_marker", CursorWorld); }
                    if (!queue) _attackMove = false;
                }
                else SelectedId = Hover?.Id ?? MyHero.Id;
            }
            if (InputBridge.GetMouseButtonDown(1))
            {
                if (Targeting) { CancelTargeting(); return; }
                if (Hover != null && !Hover.Dying && Hover.Id != MyHero.Id)
                {
                    if (_world.IsEnemy(Hover.Team) || (Hover.State != null && Hover.State.Hp < Hover.State.MaxHp * 0.5f && Hover.Kind == UnitKind.Creep))
                        Send(Order.Attack(MyHero.Id, Hover.Id, queue));
                    else Send(new Order { Type = OrderType.Follow, UnitId = MyHero.Id, TargetId = Hover.Id, Queue = queue });
                }
                else if (CursorOnGround)
                {
                    Send(Order.MoveTo(MyHero.Id, ToSim(CursorWorld), queue));
                    _world.Vfx.Play("move_marker", CursorWorld);
                }
            }
        }

        private void HandleHotkeys()
        {
            var hero = MyHero;
            bool levelMod = InputBridge.GetKey(KeyBinds.Get(_settings, KeyBinds.LevelUpModifier));
            (string bind, AbilitySlot slot)[] abilityKeys =
            {
                (KeyBinds.AbilityQ, AbilitySlot.Q), (KeyBinds.AbilityW, AbilitySlot.W), (KeyBinds.AbilityE, AbilitySlot.E),
                (KeyBinds.AbilityR, AbilitySlot.R), (KeyBinds.AbilityD, AbilitySlot.Extra1), (KeyBinds.AbilityF, AbilitySlot.Extra2),
            };
            foreach (var (bind, slot) in abilityKeys)
            {
                if (!InputBridge.GetKeyDown(KeyBinds.Get(_settings, bind))) continue;
                int index = AbilityIndex(slot);
                if (index < 0) continue;
                if (levelMod) Send(Order.LevelUp(hero.Id, index));
                else BeginAbility(index);
            }
            string[] itemBinds = { KeyBinds.Item1, KeyBinds.Item2, KeyBinds.Item3, KeyBinds.Item4, KeyBinds.Item5, KeyBinds.Item6 };
            for (int i = 0; i < 6; i++)
                if (InputBridge.GetKeyDown(KeyBinds.Get(_settings, itemBinds[i]))) UseItem(i);
            if (InputBridge.GetKeyDown(KeyBinds.Get(_settings, KeyBinds.AttackMove))) { CancelTargeting(); _attackMove = true; }
            if (InputBridge.GetKeyDown(KeyBinds.Get(_settings, KeyBinds.Stop))) { CancelTargeting(); Send(Order.StopOrder(hero.Id)); }
            if (InputBridge.GetKeyDown(KeyBinds.Get(_settings, KeyBinds.Hold))) { CancelTargeting(); Send(Order.HoldOrder(hero.Id)); }
            if (InputBridge.GetKeyDown(KeyBinds.Get(_settings, KeyBinds.CameraLock))) _world.Camera.Locked = !_world.Camera.Locked;
            if (InputBridge.GetKeyDown(KeyBinds.Get(_settings, KeyBinds.SelectHero))) { SelectedId = hero.Id; _world.Camera.JumpTo(_world.HeroWorldPosition() ?? _world.Camera.Focus); }
            if (InputBridge.GetKeyDown(KeyBinds.Get(_settings, KeyBinds.Buyback))) Send(new Order { Type = OrderType.Buyback, UnitId = hero.Id });
            if (InputBridge.GetKeyDown(KeyCode.Escape) && Targeting) CancelTargeting();
        }

        public int AbilityIndex(AbilitySlot slot)
        {
            var me = Me;
            if (me?.Abilities == null) return -1;
            for (int i = 0; i < me.Abilities.Length; i++)
                if (me.Abilities[i].Id != null && _world.Data.Abilities.TryGetValue(me.Abilities[i].Id, out var d) && d.Slot == slot && !d.Hidden) return i;
            return -1;
        }

        /// <summary>Starts using an ability (from a hotkey or HUD click).</summary>
        public void BeginAbility(int index)
        {
            var me = Me;
            var hero = MyHero;
            if (me == null || hero == null || index < 0 || index >= me.Abilities.Length) return;
            var view = me.Abilities[index];
            if (!_world.Data.Abilities.TryGetValue(view.Id, out var def)) return;
            if (def.Targeting == TargetingMode.Passive) { Error("That ability is passive."); return; }
            if (view.Level <= 0) { Error("Ability not learned yet."); return; }
            if (view.Cooldown > 0.05f && (def.MaxCharges <= 0 || view.Charges <= 0)) { Error("Ability is on cooldown."); return; }
            if (hero.Mana + 0.01f < view.ManaCost) { Error("Not enough mana."); return; }
            if (hero.Has(EntityFlags.Silenced) && !def.IgnoreSilence) { Error("You are silenced."); return; }
            if (hero.Has(EntityFlags.Stunned) || hero.Has(EntityFlags.Hexed)) { Error("You cannot cast right now."); return; }
            StartTargeting(index, def, view.Level);
        }

        public void UseItem(int slot)
        {
            var me = Me;
            var hero = MyHero;
            if (me == null || hero == null || slot < 0 || slot >= me.Items.Length) return;
            var item = me.Items[slot];
            if (string.IsNullOrEmpty(item.Id) || !_world.Data.Items.TryGetValue(item.Id, out var def)) return;
            if (def.Active == null) { Error("That item has no active ability."); return; }
            if (item.Cooldown > 0.05f) { Error("Item is on cooldown."); return; }
            StartTargeting(Order.ItemSlotBase + slot, def.Active, 1);
        }

        private void StartTargeting(int slot, AbilityDef def, int level)
        {
            var hero = MyHero;
            switch (def.Targeting)
            {
                case TargetingMode.NoTarget:
                    Send(Order.CastNoTargetOrder(hero.Id, slot, InputBridge.Shift));
                    return;
                case TargetingMode.Toggle:
                    Send(new Order { Type = OrderType.ToggleAbility, UnitId = hero.Id, Slot = slot });
                    return;
            }
            _attackMove = false;
            _targetSlot = slot;
            _targetDef = def;
            _targetLevel = Mathf.Max(1, level);
            if (_settings.QuickCast) ConfirmTarget(InputBridge.Shift);
        }

        private void ConfirmTarget(bool queue)
        {
            var hero = MyHero;
            if (_targetDef == null || hero == null) { CancelTargeting(); return; }
            var mode = _targetDef.Targeting;
            bool wantsUnit = mode == TargetingMode.Unit || mode == TargetingMode.UnitOrPoint;
            if (wantsUnit && Hover != null && !Hover.Dying && ValidTeam(_targetDef, Hover))
                Send(Order.CastUnitOrder(hero.Id, _targetSlot, Hover.Id, queue));
            else if (mode == TargetingMode.Unit)
            {
                Error(Hover == null ? "Select a target." : "Invalid target.");
                if (_settings.QuickCast) CancelTargeting();
                return;
            }
            else if (CursorOnGround)
                Send(Order.CastPointOrder(hero.Id, _targetSlot, ToSim(CursorWorld), queue));
            else { Error("Invalid location."); return; }
            if (!queue) CancelTargeting();
        }

        private bool ValidTeam(AbilityDef def, EntityView target)
        {
            bool enemy = _world.IsEnemy(target.Team);
            bool self = target.Id == MyHero?.Id;
            var t = def.TargetTeam;
            if (self) return (t & (TargetTeam.Self | TargetTeam.AllyOrSelf)) != 0 || t == TargetTeam.Any;
            if (enemy) return (t & TargetTeam.Enemy) != 0 || t == TargetTeam.Any;
            return (t & (TargetTeam.Ally | TargetTeam.AllyOrSelf)) != 0 || t == TargetTeam.Any;
        }

        public void CancelTargeting()
        {
            _targetSlot = -1;
            _targetDef = null;
            _attackMove = false;
        }

        private void Error(string msg)
        {
            GameApp.Instance?.Audio?.PlayUi("error");
            LocalError?.Invoke(msg);
        }

        private void Send(Order o) => Mc.SendOrder(o);

        public static NVec2 ToSim(Vector3 w) => new NVec2(w.x, w.z);

        // ------------------------------------------------------------------ picking

        private EntityView Pick(Vector2 mouse)
        {
            EntityView best = null;
            float bestScore = float.MaxValue;
            var cam = _world.Camera.Cam;
            foreach (var v in _world.Views)
            {
                if (!v.Present || v.Dying || !v.Model.Root.activeSelf) continue;
                if (v.State != null && v.State.Has(EntityFlags.Dead)) continue;
                var sp = cam.WorldToScreenPoint(v.Point(0.45f));
                if (sp.z < 0) continue;
                // Projected pick radius from model size.
                var edge = cam.WorldToScreenPoint(v.Point(0.45f) + cam.transform.right * Mathf.Max(v.Radius, v.Height * 0.3f));
                float r = Mathf.Max(16f, Vector2.Distance(sp, edge) * 1.1f);
                float h = Mathf.Abs(cam.WorldToScreenPoint(v.Point(1f)).y - cam.WorldToScreenPoint(v.Position).y) * 0.5f;
                float dx = mouse.x - sp.x, dy = mouse.y - sp.y;
                float d = Mathf.Sqrt(dx * dx + (dy * r / Mathf.Max(r, h)) * (dy * r / Mathf.Max(r, h)));
                if (d > Mathf.Max(r, 12f)) continue;
                float score = d - (v.IsHero ? 12f : 0f) + (v.IsStructure ? 10f : 0f);
                if (score < bestScore) { bestScore = score; best = v; }
            }
            return best;
        }

        // ------------------------------------------------------------------ indicators & cursor

        private void UpdateIndicators()
        {
            var hero = MyHero != null && _world.TryGetView(MyHero.Id, out var hv) ? hv : null;
            bool show = _targetDef != null && hero != null;
            float castRange = show ? _targetDef.CastRange.Get(_targetLevel) : 0f;
            float aoe = show && _targetDef.AoeRadius != null ? _targetDef.AoeRadius.Get(_targetLevel) : 0f;
            float lineWidth = 0f, lineRange = 0f;
            if (show) FindLine(_targetDef.OnCast, _targetLevel, ref lineWidth, ref lineRange);
            if (lineRange <= 0f) lineRange = castRange;

            SetDecal(ref _rangeRing, ref _rangeMesh, show && castRange > 0.5f, "range_ring", hero != null ? new Vector2(hero.Position.x, hero.Position.z) : Vector2.zero, castRange * 2f + 1f, new Color(0.5f, 0.85f, 1f, 0.6f), 0f, 16);
            SetDecal(ref _aoe, ref _aoeMesh, show && aoe > 0.1f && lineWidth <= 0f && CursorOnGround, "aoe_circle", new Vector2(CursorWorld.x, CursorWorld.z), aoe * 2f, new Color(1f, 0.35f, 0.25f, 0.7f), 0f, 10);
            bool line = show && lineWidth > 0f && CursorOnGround;
            if (line)
            {
                var from = new Vector2(hero.Position.x, hero.Position.z);
                var dir = new Vector2(CursorWorld.x, CursorWorld.z) - from;
                if (dir.sqrMagnitude < 0.01f) dir = Vector2.up;
                dir.Normalize();
                var center = from + dir * lineRange * 0.5f;
                float angle = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                if (_line == null)
                {
                    _line = Decals.Create(_world.Root, _world.Map, center, 1f, "line_indicator", Color.white, true, 0f, 12);
                    _lineMesh = _line.GetComponent<MeshFilter>().sharedMesh;
                }
                _line.SetActive(true);
                ConformLine(_lineMesh, center, lineWidth * 2f, lineRange, -angle);
            }
            else if (_line != null) _line.SetActive(false);
        }

        private static void FindLine(List<EffectDef> effects, int level, ref float width, ref float range)
        {
            if (effects == null) return;
            foreach (var e in effects)
            {
                if (e.Type == EffectType.Projectile && e.Projectile == ProjectileKind.Linear)
                {
                    width = Mathf.Max(width, e.Width);
                    if (e.Range != null) range = Mathf.Max(range, e.Range.Get(level));
                }
                if (e.Type == EffectType.Dash && e.StopOnUnitCollision)
                {
                    width = Mathf.Max(width, e.CollisionRadius);
                    if (e.MaxDistance != null) range = Mathf.Max(range, e.MaxDistance.Get(level));
                }
                FindLine(e.Effects, level, ref width, ref range);
            }
        }

        private void ConformLine(Mesh mesh, Vector2 center, float width, float length, float angleDeg)
        {
            // Stretch a conformed square: build with length then scale x by width/length via rotation-aware sampling.
            int resX = 2, resY = 12;
            var v = new Vector3[(resX + 1) * (resY + 1)];
            var uv = new Vector2[v.Length];
            var col = new Color[v.Length];
            float rad = angleDeg * Mathf.Deg2Rad, cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
            for (int y = 0; y <= resY; y++)
            for (int x = 0; x <= resX; x++)
            {
                float u = x / (float)resX, w = y / (float)resY;
                float lx = (u - 0.5f) * width, ly = (w - 0.5f) * length;
                float wx = center.x + lx * cs - ly * sn, wy = center.y + lx * sn + ly * cs;
                int i = y * (resX + 1) + x;
                v[i] = new Vector3(wx, _world.Map.HeightAt(wx, wy) + 0.08f, wy);
                uv[i] = new Vector2(u, 1f - w);
                col[i] = new Color(1f, 0.4f, 0.3f, 0.75f);
            }
            var tris = new int[resX * resY * 6];
            int t = 0;
            for (int y = 0; y < resY; y++)
            for (int x = 0; x < resX; x++)
            {
                int a = y * (resX + 1) + x, b = a + 1, c = a + resX + 1, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
            mesh.Clear();
            mesh.vertices = v; mesh.uv = uv; mesh.colors = col; mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        private void SetDecal(ref GameObject go, ref Mesh mesh, bool show, string tex, Vector2 center, float size, Color color, float rot, int res)
        {
            if (!show)
            {
                if (go != null) go.SetActive(false);
                return;
            }
            if (go == null)
            {
                go = Decals.Create(_world.Root, _world.Map, center, size, tex, color, true, rot, res);
                mesh = go.GetComponent<MeshFilter>().sharedMesh;
            }
            go.SetActive(true);
            Decals.Conform(mesh, _world.Map, center, size, rot, color, res);
        }

        private void UpdateCursor()
        {
            CursorKind k = CursorKind.Default;
            if (_targetSlot >= 0) k = CursorKind.Cast;
            else if (_attackMove) k = CursorKind.Attack;
            else if (Hover != null) k = _world.IsEnemy(Hover.Team) ? CursorKind.Attack : CursorKind.Ally;
            CursorManager.Apply(k);
        }

        public void Dispose()
        {
            CursorManager.Apply(CursorKind.Default);
            if (_rangeRing != null) Object.Destroy(_rangeRing);
            if (_aoe != null) Object.Destroy(_aoe);
            if (_line != null) Object.Destroy(_line);
        }
    }
}
