using System.Collections.Generic;
using Bloodfall.Client.Core;
using Bloodfall.Client.Match;
using Bloodfall.Data;
using Bloodfall.Protocol;
using Bloodfall.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Bloodfall.Client.UI.Hud
{
    /// <summary>
    /// Minimap: baked terrain image + fog of war + pooled unit/structure markers + camera frustum outline + pings.
    /// Left-click (or drag) moves the camera; right-click issues a move order (optional); Alt+click pings.
    /// </summary>
    public sealed class Minimap
    {
        public VisualElement Root { get; private set; }
        private VisualElement _map, _fog, _markers;
        private FrustumElement _frustum;
        private readonly List<VisualElement> _pool = new List<VisualElement>();
        private int _used;
        private MatchWorld _world;
        private bool _dragging;
        private readonly List<(VisualElement el, float age)> _pings = new List<(VisualElement, float)>();
        public const float Size = 250;

        public VisualElement Build()
        {
            Root = El.Div("minimap-frame");
            Root.style.width = Size + 24;
            Root.style.height = Size + 24;
            _map = new VisualElement();
            _map.style.width = Size;
            _map.style.height = Size;
            _map.style.backgroundColor = new Color(0.05f, 0.05f, 0.05f);
            _fog = new VisualElement { pickingMode = PickingMode.Ignore };
            _fog.AddToClassList("fill");
            _map.Add(_fog);
            _markers = new VisualElement { pickingMode = PickingMode.Ignore };
            _markers.AddToClassList("fill");
            _map.Add(_markers);
            _frustum = new FrustumElement { pickingMode = PickingMode.Ignore };
            _frustum.AddToClassList("fill");
            _map.Add(_frustum);
            Root.Add(_map);
            _map.RegisterCallback<PointerDownEvent>(OnDown);
            _map.RegisterCallback<PointerMoveEvent>(e => { if (_dragging) JumpTo(e.localPosition); });
            _map.RegisterCallback<PointerUpEvent>(e => { _dragging = false; _map.ReleasePointer(e.pointerId); });
            return Root;
        }

        public void Bind(MatchWorld world)
        {
            _world = world;
            if (world.Map.MinimapTexture != null) _map.style.backgroundImage = new StyleBackground(world.Map.MinimapTexture);
        }

        private Vector3 ToWorld(Vector2 local)
        {
            float x = local.x / Size * _world.Map.Width;
            float z = (1f - local.y / Size) * _world.Map.Depth;
            return _world.Map.World(x, z);
        }

        private Vector2 ToMap(float x, float z) => new Vector2(x / _world.Map.Width * Size, (1f - z / _world.Map.Depth) * Size);

        private void OnDown(PointerDownEvent e)
        {
            if (_world == null) return;
            var w = ToWorld(e.localPosition);
            var mc = _world.Controller;
            var hero = mc.LocalHero;
            if (e.button == 0 && Input.InputBridge.Alt && hero != null)
            {
                mc.SendOrder(new Order { Type = OrderType.Ping, UnitId = hero.Id, Point = MatchInput.ToSim(w) });
                return;
            }
            if (e.button == 0)
            {
                if (_world.Input != null && _world.Input.Targeting && hero != null)
                {
                    // Minimap-targeted point casts / attack-move.
                    if (_world.Input.AttackMoveArmed) mc.SendOrder(Order.AttackMoveTo(hero.Id, MatchInput.ToSim(w)));
                    else if (_world.Input.TargetSlot >= 0) mc.SendOrder(Order.CastPointOrder(hero.Id, _world.Input.TargetSlot, MatchInput.ToSim(w)));
                    _world.Input.CancelTargeting();
                    return;
                }
                _dragging = true;
                _map.CapturePointer(e.pointerId);
                JumpTo(e.localPosition);
            }
            else if (e.button == 1 && GameApp.Instance.Settings.MinimapRightClickMoves && hero != null)
            {
                mc.SendOrder(Order.MoveTo(hero.Id, MatchInput.ToSim(w), Input.InputBridge.Shift));
            }
        }

        private void JumpTo(Vector2 local)
        {
            local.x = Mathf.Clamp(local.x, 0, Size);
            local.y = Mathf.Clamp(local.y, 0, Size);
            _world.Camera.Locked = false;
            _world.Camera.JumpTo(ToWorld(local), true);
        }

        public void Ping(Vector2 simPoint, Color color)
        {
            var el = new VisualElement { pickingMode = PickingMode.Ignore };
            el.style.position = Position.Absolute;
            el.style.borderTopWidth = el.style.borderBottomWidth = el.style.borderLeftWidth = el.style.borderRightWidth = 2;
            el.style.borderTopColor = el.style.borderBottomColor = el.style.borderLeftColor = el.style.borderRightColor = color;
            el.style.borderTopLeftRadius = el.style.borderTopRightRadius = el.style.borderBottomLeftRadius = el.style.borderBottomRightRadius = 20;
            var p = ToMap(simPoint.x, simPoint.y);
            el.userData = p;
            _markers.Add(el);
            _pings.Add((el, 0f));
        }

        private VisualElement Marker()
        {
            VisualElement m;
            if (_used < _pool.Count) m = _pool[_used];
            else
            {
                m = new VisualElement { pickingMode = PickingMode.Ignore };
                m.style.position = Position.Absolute;
                _markers.Add(m);
                _pool.Add(m);
            }
            _used++;
            m.style.display = DisplayStyle.Flex;
            return m;
        }

        public void Update(float dt)
        {
            if (_world == null) return;
            bool fog = _world.Fog != null && _world.Fog.Enabled && _world.Fog.MinimapTexture != null;
            _fog.style.display = fog ? DisplayStyle.Flex : DisplayStyle.None;
            if (fog && _fog.userData != _world.Fog.MinimapTexture)
            {
                _fog.userData = _world.Fog.MinimapTexture;
                _fog.style.backgroundImage = new StyleBackground(_world.Fog.MinimapTexture);
            }
            _used = 0;
            foreach (var v in _world.Views)
            {
                if (!v.Present || v.State == null || !v.Model.Root.activeSelf) continue;
                if (v.State.Has(EntityFlags.Dead) && !v.IsStructure) continue;
                if (v.Kind == UnitKind.Ward && _world.IsEnemy(v.Team)) continue;
                var m = Marker();
                float size = v.IsHero ? 11 : v.IsStructure ? (v.Kind == UnitKind.Core ? 14 : v.Kind == UnitKind.Tower ? 9 : 10) : 5;
                var p = ToMap(v.Position.x, v.Position.z);
                m.style.left = p.x - size / 2;
                m.style.top = p.y - size / 2;
                m.style.width = size;
                m.style.height = size;
                bool mine = v.State.OwnerPlayer == _world.LocalPlayerId && _world.LocalPlayerId >= 0;
                Color c = v.Team == Team.Neutral ? new Color(0.9f, 0.8f, 0.3f)
                    : mine ? new Color(0.3f, 1f, 0.4f)
                    : v.Team == Team.Dawn ? ModelFactory.DawnGlow : new Color(0.95f, 0.15f, 0.15f);
                if (v.IsStructure && v.State.Has(EntityFlags.Dead)) c = new Color(0.25f, 0.22f, 0.2f);
                m.style.backgroundColor = c;
                float r = v.IsStructure ? 1 : size / 2;
                m.style.borderTopLeftRadius = m.style.borderTopRightRadius = m.style.borderBottomLeftRadius = m.style.borderBottomRightRadius = r;
                m.style.borderTopWidth = m.style.borderBottomWidth = m.style.borderLeftWidth = m.style.borderRightWidth = v.IsHero ? 1.5f : 0f;
                m.style.borderTopColor = m.style.borderBottomColor = m.style.borderLeftColor = m.style.borderRightColor = Color.black;
            }
            for (int i = _used; i < _pool.Count; i++) _pool[i].style.display = DisplayStyle.None;

            for (int i = _pings.Count - 1; i >= 0; i--)
            {
                var (el, age) = _pings[i];
                age += dt;
                if (age > 2.5f) { el.RemoveFromHierarchy(); _pings.RemoveAt(i); continue; }
                _pings[i] = (el, age);
                float s = 8 + (age % 0.8f) * 40f;
                var p = (Vector2)el.userData;
                el.style.left = p.x - s / 2; el.style.top = p.y - s / 2; el.style.width = s; el.style.height = s;
                el.style.opacity = 1f - (age % 0.8f) / 0.8f;
            }

            var corners = _world.Camera.ViewCorners();
            var pts = new Vector2[4];
            for (int i = 0; i < 4; i++) pts[i] = ToMap(corners[i].x, corners[i].z);
            _frustum.SetPoints(pts);
        }
    }

    /// <summary>Draws the camera view trapezoid on the minimap (thin quads via the UI mesh API).</summary>
    public sealed class FrustumElement : VisualElement
    {
        private readonly Vector2[] _pts = new Vector2[4];

        public FrustumElement()
        {
            generateVisualContent += Generate;
        }

        public void SetPoints(Vector2[] p)
        {
            for (int i = 0; i < 4; i++) _pts[i] = p[i];
            MarkDirtyRepaint();
        }

        private void Generate(MeshGenerationContext ctx)
        {
            var mesh = ctx.Allocate(16, 48);
            var col = new Color32(240, 230, 210, 220);
            for (int i = 0; i < 4; i++)
            {
                var a = _pts[i];
                var b = _pts[(i + 1) % 4];
                var d = (b - a);
                var n = d.sqrMagnitude > 0.0001f ? new Vector2(-d.y, d.x).normalized * 0.8f : Vector2.zero;
                mesh.SetNextVertex(new Vertex { position = new Vector3(a.x - n.x, a.y - n.y, Vertex.nearZ), tint = col });
                mesh.SetNextVertex(new Vertex { position = new Vector3(a.x + n.x, a.y + n.y, Vertex.nearZ), tint = col });
                mesh.SetNextVertex(new Vertex { position = new Vector3(b.x + n.x, b.y + n.y, Vertex.nearZ), tint = col });
                mesh.SetNextVertex(new Vertex { position = new Vector3(b.x - n.x, b.y - n.y, Vertex.nearZ), tint = col });
            }
            for (int i = 0; i < 4; i++)
            {
                ushort o = (ushort)(i * 4);
                mesh.SetNextIndex(o); mesh.SetNextIndex((ushort)(o + 1)); mesh.SetNextIndex((ushort)(o + 2));
                mesh.SetNextIndex(o); mesh.SetNextIndex((ushort)(o + 2)); mesh.SetNextIndex((ushort)(o + 3));
                // Both windings so the outline renders regardless of the backend's culling convention.
                mesh.SetNextIndex(o); mesh.SetNextIndex((ushort)(o + 2)); mesh.SetNextIndex((ushort)(o + 1));
                mesh.SetNextIndex(o); mesh.SetNextIndex((ushort)(o + 3)); mesh.SetNextIndex((ushort)(o + 2));
            }
        }
    }
}
