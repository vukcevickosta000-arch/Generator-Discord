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
        private MinimapDots _dots;
        private readonly Vector2[] _corners = new Vector2[4];
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
            _dots = new MinimapDots { pickingMode = PickingMode.Ignore };
            _dots.AddToClassList("fill");
            _map.Add(_dots);
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
            _dots.Begin();
            foreach (var v in _world.Views)
            {
                if (!v.Present || v.State == null || !v.Model.Root.activeSelf) continue;
                if (v.State.Has(EntityFlags.Dead) && !v.IsStructure) continue;
                if (v.Kind == UnitKind.Ward && _world.IsEnemy(v.Team)) continue;
                float size = v.IsHero ? 11 : v.IsStructure ? (v.Kind == UnitKind.Core ? 14 : v.Kind == UnitKind.Tower ? 9 : 10) : 5;
                bool mine = v.State.OwnerPlayer == _world.LocalPlayerId && _world.LocalPlayerId >= 0;
                Color c = v.Team == Team.Neutral ? new Color(0.9f, 0.8f, 0.3f)
                    : mine ? new Color(0.3f, 1f, 0.4f)
                    : v.Team == Team.Dawn ? ModelFactory.DawnGlow : new Color(0.95f, 0.15f, 0.15f);
                if (v.IsStructure && v.State.Has(EntityFlags.Dead)) c = new Color(0.25f, 0.22f, 0.2f);
                _dots.Add(ToMap(v.Position.x, v.Position.z), size, c, square: v.IsStructure, outline: v.IsHero, layer: v.IsHero ? 2 : v.IsStructure ? 0 : 1);
            }
            _dots.End();

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
            for (int i = 0; i < 4; i++) _corners[i] = ToMap(corners[i].x, corners[i].z);
            _frustum.SetPoints(_corners);
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
            bool changed = false;
            for (int i = 0; i < 4; i++)
            {
                if ((_pts[i] - p[i]).sqrMagnitude < 0.01f) continue;
                _pts[i] = p[i];
                changed = true;
            }
            if (changed) MarkDirtyRepaint();
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

    /// <summary>
    /// Every unit and structure marker in one mesh: one element and one draw instead of a styled element per unit.
    /// Structures are squares, units and heroes octagons; heroes get a dark outline and draw on top.
    /// </summary>
    public sealed class MinimapDots : VisualElement
    {
        private struct Dot { public Vector2 P; public float Size; public Color32 C; public bool Square, Outline; }

        private readonly List<Dot>[] _layers = { new List<Dot>(), new List<Dot>(), new List<Dot>() };
        private int _hash, _lastHash = int.MinValue;
        private static readonly Vector2[] Octagon = MakeOctagon();

        public MinimapDots() { generateVisualContent += Generate; }

        private static Vector2[] MakeOctagon()
        {
            var o = new Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                float a = (i + 0.5f) * Mathf.PI / 4f;
                o[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) / Mathf.Cos(Mathf.PI / 8f);
            }
            return o;
        }

        public void Begin()
        {
            foreach (var l in _layers) l.Clear();
            _hash = 17;
        }

        public void Add(Vector2 p, float size, Color c, bool square, bool outline, int layer)
        {
            var d = new Dot { P = p, Size = size, C = c, Square = square, Outline = outline };
            _layers[layer].Add(d);
            unchecked
            {
                _hash = _hash * 31 + Mathf.RoundToInt(p.x * 2f);
                _hash = _hash * 31 + Mathf.RoundToInt(p.y * 2f);
                _hash = _hash * 31 + (d.C.r | d.C.g << 8 | d.C.b << 16) + (int)size;
            }
        }

        /// <summary>Repaints only when a marker moved by half a pixel, changed colour or appeared/disappeared.</summary>
        public void End()
        {
            if (_hash == _lastHash) return;
            _lastHash = _hash;
            MarkDirtyRepaint();
        }

        private void Generate(MeshGenerationContext ctx)
        {
            int verts = 0, indices = 0;
            foreach (var l in _layers)
                foreach (var d in l)
                {
                    int n = d.Outline ? 2 : 1;
                    verts += n * (d.Square ? 4 : 9);
                    indices += n * (d.Square ? 12 : 48);
                }
            if (verts == 0 || verts > 60000) return;
            var mesh = ctx.Allocate(verts, indices);
            ushort next = 0;
            var black = new Color32(0, 0, 0, 230);
            foreach (var l in _layers)
                foreach (var d in l)
                {
                    float r = d.Size / 2f;
                    if (d.Outline) Shape(mesh, ref next, d.P, r + 1.5f, black, d.Square);
                    Shape(mesh, ref next, d.P, r, d.C, d.Square);
                }
        }

        private static void Shape(MeshWriteData mesh, ref ushort next, Vector2 c, float r, Color32 col, bool square)
        {
            ushort o = next;
            if (square)
            {
                mesh.SetNextVertex(new Vertex { position = new Vector3(c.x - r, c.y - r, Vertex.nearZ), tint = col });
                mesh.SetNextVertex(new Vertex { position = new Vector3(c.x + r, c.y - r, Vertex.nearZ), tint = col });
                mesh.SetNextVertex(new Vertex { position = new Vector3(c.x + r, c.y + r, Vertex.nearZ), tint = col });
                mesh.SetNextVertex(new Vertex { position = new Vector3(c.x - r, c.y + r, Vertex.nearZ), tint = col });
                // Both windings, as in FrustumElement, so no backend culls the quad.
                mesh.SetNextIndex(o); mesh.SetNextIndex((ushort)(o + 1)); mesh.SetNextIndex((ushort)(o + 2));
                mesh.SetNextIndex(o); mesh.SetNextIndex((ushort)(o + 2)); mesh.SetNextIndex((ushort)(o + 3));
                mesh.SetNextIndex(o); mesh.SetNextIndex((ushort)(o + 2)); mesh.SetNextIndex((ushort)(o + 1));
                mesh.SetNextIndex(o); mesh.SetNextIndex((ushort)(o + 3)); mesh.SetNextIndex((ushort)(o + 2));
                next += 4;
                return;
            }
            mesh.SetNextVertex(new Vertex { position = new Vector3(c.x, c.y, Vertex.nearZ), tint = col });
            for (int i = 0; i < 8; i++)
                mesh.SetNextVertex(new Vertex { position = new Vector3(c.x + Octagon[i].x * r, c.y + Octagon[i].y * r, Vertex.nearZ), tint = col });
            for (int i = 0; i < 8; i++)
            {
                ushort a = (ushort)(o + 1 + i), b = (ushort)(o + 1 + (i + 1) % 8);
                mesh.SetNextIndex(o); mesh.SetNextIndex(a); mesh.SetNextIndex(b);
                mesh.SetNextIndex(o); mesh.SetNextIndex(b); mesh.SetNextIndex(a);
            }
            next += 9;
        }
    }
}
