using System.Collections.Generic;
using Bloodfall.Client.Core;
using Bloodfall.Client.Match;
using Bloodfall.Data;
using Bloodfall.Protocol;
using UnityEngine;
using UnityEngine.UIElements;

namespace Bloodfall.Client.UI.Hud
{
    /// <summary>
    /// Screen-space elements attached to world units: health/mana bars (pooled), hero names and levels, floating
    /// combat text. Everything is re-projected once per frame right after the camera has moved (MatchWorld.CameraMoved),
    /// so bars stay glued to their units while the camera follows the hero. Bars move with translate and fills scale,
    /// so a normal frame changes no layout; styles are only written when their value changes.
    /// </summary>
    public sealed class WorldOverlay
    {
        private enum BarKind { Unit, Hero, Structure }

        private static readonly float[] Widths = { 52f, 96f, 120f };
        private static readonly float[] Heights = { 5f, 9f, 8f };
        private static readonly Color TrailColor = new Color(1f, 0.86f, 0.55f, 0.95f);
        private const float TrailHold = 0.35f, TrailSpeed = 0.9f;

        private sealed class BarView
        {
            public VisualElement Root, HpFill, Trail, ManaFill, Ticks, Shield;
            public Label Name, Level;
            public BarKind Kind;
            // Last values written, so unchanged frames touch no styles.
            public float LastMaxHp = -1f, Frac = -1f, TrailFrac = -1f, ManaFrac = -1f, ShieldLeft = -1f, ShieldWidth = -1f;
            public float TrailWait;
            public Color Color = new Color(-1f, 0f, 0f, 0f);
            public int ShownLevel = -1;
            public string ShownName;
            public Vector2 Pos = new Vector2(float.NaN, float.NaN);

            public void Reset()
            {
                LastMaxHp = Frac = TrailFrac = ManaFrac = ShieldLeft = ShieldWidth = -1f;
                TrailWait = 0f;
                Color = new Color(-1f, 0f, 0f, 0f);
                ShownLevel = -1;
                ShownName = null;
                Pos = new Vector2(float.NaN, float.NaN);
                Ticks?.Clear();
            }
        }

        private sealed class Floater
        {
            public Label Label;
            public Vector3 World;
            public float Age, Life;
            public Vector2 Drift;
        }

        public VisualElement Root { get; private set; }
        private readonly VisualElement _structureLayer, _unitLayer, _heroLayer, _floatLayer;
        private readonly Dictionary<int, BarView> _bars = new Dictionary<int, BarView>();
        private readonly Stack<BarView>[] _pools = { new Stack<BarView>(), new Stack<BarView>(), new Stack<BarView>() };
        private readonly List<Floater> _floaters = new List<Floater>();
        private readonly Stack<Label> _labelPool = new Stack<Label>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _gone = new List<int>();
        private MatchWorld _world;
        private readonly ClientSettings _settings;

        public WorldOverlay(ClientSettings settings)
        {
            _settings = settings;
            Root = Layer();
            // Later layers draw on top: hero bars are never hidden under creep bars, combat text is above all bars.
            _structureLayer = Layer();
            _unitLayer = Layer();
            _heroLayer = Layer();
            _floatLayer = Layer();
            Root.Add(_structureLayer);
            Root.Add(_unitLayer);
            Root.Add(_heroLayer);
            Root.Add(_floatLayer);
        }

        private static VisualElement Layer()
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("fill");
            return e;
        }

        /// <summary>Attaches to a match world; the overlay then redraws after each camera update.</summary>
        public void Bind(MatchWorld world)
        {
            if (_world != null) _world.CameraMoved -= OnCameraMoved;
            _world = world;
            if (_world != null) _world.CameraMoved += OnCameraMoved;
        }

        private void OnCameraMoved(float dt)
        {
            try { Update(dt, _world?.Camera?.Cam); }
            catch (System.Exception e) { Faults.Report("world-overlay", e); }
        }

        private static BarKind KindOf(EntityView v) => v.IsHero ? BarKind.Hero : v.IsStructure ? BarKind.Structure : BarKind.Unit;

        private VisualElement LayerOf(BarKind k) => k == BarKind.Hero ? _heroLayer : k == BarKind.Structure ? _structureLayer : _unitLayer;

        private BarView GetBar(BarKind kind)
        {
            var pool = _pools[(int)kind];
            if (pool.Count > 0)
            {
                var b = pool.Pop();
                b.Reset();
                b.Root.style.display = DisplayStyle.Flex;
                return b;
            }
            bool hero = kind == BarKind.Hero;
            float w = Widths[(int)kind];
            var bar = new BarView { Kind = kind };
            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.width = w;
            root.style.flexDirection = FlexDirection.Column;
            root.style.alignItems = Align.Center;
            if (hero)
            {
                var nameRow = new VisualElement { pickingMode = PickingMode.Ignore };
                nameRow.style.flexDirection = FlexDirection.Row;
                bar.Level = new Label { pickingMode = PickingMode.Ignore };
                bar.Level.AddToClassList("t-small");
                bar.Level.style.backgroundColor = new Color(0.1f, 0.06f, 0.05f, 0.9f);
                bar.Level.style.paddingLeft = 3; bar.Level.style.paddingRight = 3;
                bar.Level.style.marginRight = 3;
                bar.Level.style.color = new Color(1f, 0.85f, 0.5f);
                bar.Name = new Label { pickingMode = PickingMode.Ignore };
                bar.Name.AddToClassList("t-small");
                bar.Name.style.unityTextOutlineWidth = 1;
                bar.Name.style.unityTextOutlineColor = Color.black;
                nameRow.Add(bar.Level);
                nameRow.Add(bar.Name);
                root.Add(nameRow);
            }
            var hp = new VisualElement { pickingMode = PickingMode.Ignore };
            hp.style.width = w;
            hp.style.height = Heights[(int)kind];
            hp.style.backgroundColor = new Color(0.05f, 0.03f, 0.03f, 0.9f);
            hp.style.borderTopWidth = hp.style.borderBottomWidth = hp.style.borderLeftWidth = hp.style.borderRightWidth = 1;
            hp.style.borderTopColor = hp.style.borderBottomColor = hp.style.borderLeftColor = hp.style.borderRightColor = new Color(0, 0, 0, 0.9f);
            hp.style.overflow = Overflow.Hidden;
            // Recent damage stays visible as a pale chip for a moment before draining (reads burst at a glance).
            bar.Trail = Fill(TrailColor);
            hp.Add(bar.Trail);
            bar.HpFill = Fill(Color.clear);
            hp.Add(bar.HpFill);
            bar.Shield = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.Shield.style.position = Position.Absolute;
            bar.Shield.style.top = 0; bar.Shield.style.bottom = 0;
            bar.Shield.style.backgroundColor = new Color(0.88f, 0.9f, 0.96f, 0.9f);
            bar.Shield.style.display = DisplayStyle.None;
            hp.Add(bar.Shield);
            if (hero)
            {
                bar.Ticks = new VisualElement { pickingMode = PickingMode.Ignore };
                bar.Ticks.style.position = Position.Absolute;
                bar.Ticks.style.left = 0; bar.Ticks.style.right = 0; bar.Ticks.style.top = 0; bar.Ticks.style.bottom = 0;
                hp.Add(bar.Ticks);
            }
            root.Add(hp);
            if (hero)
            {
                var mana = new VisualElement { pickingMode = PickingMode.Ignore };
                mana.style.width = w;
                mana.style.height = 4;
                mana.style.backgroundColor = new Color(0.03f, 0.03f, 0.08f, 0.9f);
                mana.style.overflow = Overflow.Hidden;
                bar.ManaFill = Fill(new Color(0.25f, 0.45f, 1f));
                mana.Add(bar.ManaFill);
                root.Add(mana);
            }
            bar.Root = root;
            LayerOf(kind).Add(root);
            return bar;
        }

        /// <summary>A full-size fill that is shortened with a horizontal scale (no layout work when it changes).</summary>
        private static VisualElement Fill(Color c)
        {
            var f = new VisualElement { pickingMode = PickingMode.Ignore };
            f.style.position = Position.Absolute;
            f.style.left = 0; f.style.top = 0; f.style.bottom = 0; f.style.right = 0;
            f.style.transformOrigin = new TransformOrigin(0, 0);
            f.style.backgroundColor = c;
            return f;
        }

        private static void SetFrac(VisualElement fill, ref float shown, float frac)
        {
            if (Mathf.Abs(shown - frac) < 0.001f) return;
            shown = frac;
            fill.style.scale = new Scale(new Vector3(frac, 1f, 1f));
        }

        private Color BarColor(EntityView v)
        {
            bool mine = v.State != null && v.State.OwnerPlayer == _world.LocalPlayerId && _world.LocalPlayerId >= 0;
            if (v.Team == Team.Neutral) return new Color(0.95f, 0.8f, 0.2f);
            if (mine) return new Color(0.3f, 1f, 0.3f);
            bool enemy = _world.IsEnemy(v.Team);
            if (_settings.ColorblindMode) return enemy ? new Color(1f, 0.55f, 0.1f) : new Color(0.2f, 0.6f, 1f);
            return enemy ? new Color(0.9f, 0.15f, 0.12f) : new Color(0.25f, 0.8f, 0.25f);
        }

        private bool Wanted(EntityView v)
        {
            if (!v.Present || v.Dying || v.State == null || !v.Model.Root.activeSelf || v.State.Has(EntityFlags.Dead) || v.State.MaxHp <= 0) return false;
            if (v.Kind == UnitKind.Ward && _world.IsEnemy(v.Team)) return false;
            if (v.IsHero && !_settings.ShowAllyHeroBars && !_world.IsEnemy(v.Team) && v.State.OwnerPlayer != _world.LocalPlayerId) return false;
            return true;
        }

        public void Update(float dt, Camera cam)
        {
            if (_world == null || cam == null) return;
            var ui = GameApp.Instance.UI;
            _seen.Clear();
            if (_settings.ShowHealthBars)
            {
                foreach (var v in _world.Views)
                {
                    if (!Wanted(v)) continue;
                    var sp = cam.WorldToScreenPoint(v.Point(1f) + Vector3.up * 0.35f);
                    if (sp.z <= 0 || sp.x < -100 || sp.x > Screen.width + 100 || sp.y < -50 || sp.y > Screen.height + 80) continue;
                    _seen.Add(v.Id);
                    var kind = KindOf(v);
                    if (_bars.TryGetValue(v.Id, out var bar) && bar.Kind != kind) { Recycle(bar); _bars.Remove(v.Id); bar = null; }
                    if (bar == null) { bar = GetBar(kind); _bars[v.Id] = bar; }
                    Draw(bar, v, ui.ScreenToPanel(new Vector2(sp.x, sp.y)), dt);
                }
            }
            // Recycle bars for entities not drawn this frame.
            _gone.Clear();
            foreach (var kv in _bars) if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (var id in _gone)
            {
                Recycle(_bars[id]);
                _bars.Remove(id);
            }

            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                var f = _floaters[i];
                f.Age += dt;
                if (f.Age >= f.Life)
                {
                    f.Label.style.display = DisplayStyle.None;
                    _labelPool.Push(f.Label);
                    _floaters.RemoveAt(i);
                    continue;
                }
                var sp = cam.WorldToScreenPoint(f.World);
                if (sp.z <= 0) { f.Label.style.display = DisplayStyle.None; continue; }
                var p = ui.ScreenToPanel(new Vector2(sp.x, sp.y));
                float t = f.Age / f.Life;
                f.Label.style.display = DisplayStyle.Flex;
                f.Label.style.translate = new Translate(Mathf.Round(p.x + f.Drift.x * t - 20), Mathf.Round(p.y - 30 - 50 * t + f.Drift.y * t));
                f.Label.style.opacity = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
                float pop = t < 0.12f ? 1.35f - t * 3f : 1f;
                f.Label.style.scale = new Scale(Vector3.one * pop);
            }
        }

        private void Draw(BarView bar, EntityView v, Vector2 p, float dt)
        {
            var s = v.State;
            float w = Widths[(int)bar.Kind];
            var pos = new Vector2(Mathf.Round(p.x - w / 2), Mathf.Round(p.y - (bar.Kind == BarKind.Hero ? 30 : 10)));
            if (pos != bar.Pos)
            {
                bar.Pos = pos;
                bar.Root.style.translate = new Translate(pos.x, pos.y);
            }

            float frac = Mathf.Clamp01(s.Hp / s.MaxHp);
            // Damage chip: holds for a moment after each hit, then drains towards the current health; healing snaps it.
            if (bar.Frac >= 0f && frac < bar.Frac - 0.0005f) bar.TrailWait = 0f;
            SetFrac(bar.HpFill, ref bar.Frac, frac);
            float trail = bar.TrailFrac < 0f || frac >= bar.TrailFrac ? frac : bar.TrailFrac;
            if (trail > frac)
            {
                bar.TrailWait += dt;
                if (bar.TrailWait > TrailHold) trail = Mathf.Max(frac, trail - Mathf.Max(0.002f, TrailSpeed * dt));
            }
            else bar.TrailWait = 0f;
            SetFrac(bar.Trail, ref bar.TrailFrac, trail);

            var color = BarColor(v);
            if (color != bar.Color)
            {
                bar.Color = color;
                bar.HpFill.style.backgroundColor = color;
                if (bar.Name != null) bar.Name.style.color = color;
            }

            float sf = s.Shield > 0f ? Mathf.Clamp01(s.Shield / s.MaxHp) : 0f;
            float left = sf > 0f ? Mathf.Min(frac, 1f - sf) : 0f;
            if (Mathf.Abs(sf - bar.ShieldWidth) > 0.002f || Mathf.Abs(left - bar.ShieldLeft) > 0.002f)
            {
                bar.ShieldWidth = sf;
                bar.ShieldLeft = left;
                bar.Shield.style.display = sf > 0f ? DisplayStyle.Flex : DisplayStyle.None;
                bar.Shield.style.left = Length.Percent(left * 100f);
                bar.Shield.style.width = Length.Percent(sf * 100f);
            }

            if (bar.ManaFill != null) SetFrac(bar.ManaFill, ref bar.ManaFrac, s.MaxMana > 0 ? Mathf.Clamp01(s.Mana / s.MaxMana) : 0f);
            if (bar.Name != null)
            {
                string name = FindPlayer(s.OwnerPlayer)?.Name ?? "";
                if (name != bar.ShownName) { bar.ShownName = name; bar.Name.text = name; }
                if (s.Level != bar.ShownLevel) { bar.ShownLevel = s.Level; bar.Level.text = s.Level.ToString(); }
            }
            if (bar.Ticks != null && Mathf.Abs(bar.LastMaxHp - s.MaxHp) > 1f)
            {
                // Segment ticks every 250 health (bigger ticks every 1000) help read burst thresholds.
                bar.LastMaxHp = s.MaxHp;
                bar.Ticks.Clear();
                int n = Mathf.FloorToInt(s.MaxHp / 250f);
                for (int i = 1; i <= n && i < 40; i++)
                {
                    if (i * 250f >= s.MaxHp) break;
                    var t = new VisualElement { pickingMode = PickingMode.Ignore };
                    t.style.position = Position.Absolute;
                    t.style.left = Length.Percent(i * 250f / s.MaxHp * 100f);
                    t.style.width = 1;
                    t.style.top = 0;
                    t.style.bottom = i % 4 == 0 ? 0 : 4;
                    t.style.backgroundColor = new Color(0, 0, 0, 0.8f);
                    bar.Ticks.Add(t);
                }
            }
        }

        private void Recycle(BarView b)
        {
            b.Root.style.display = DisplayStyle.None;
            _pools[(int)b.Kind].Push(b);
        }

        private PlayerView FindPlayer(int id)
        {
            foreach (var p in _world.Controller.Players) if (p.Id == id) return p;
            return null;
        }

        /// <summary>Spawns floating combat text above a world point.</summary>
        public void Float(Vector3 world, string text, string cls, float life = 1.1f)
        {
            if (_floaters.Count > 80) return;
            var l = _labelPool.Count > 0 ? _labelPool.Pop() : null;
            if (l == null)
            {
                l = new Label { pickingMode = PickingMode.Ignore };
                l.style.left = 0;
                l.style.top = 0;
                _floatLayer.Add(l);
            }
            l.ClearClassList();
            l.AddToClassList("floating");
            if (!string.IsNullOrEmpty(cls)) l.AddToClassList(cls);
            l.text = text;
            l.style.display = DisplayStyle.None;
            _floaters.Add(new Floater { Label = l, World = world, Life = life, Drift = new Vector2(Random.Range(-18f, 18f), Random.Range(-6f, 6f)) });
        }

        /// <summary>Drops every bar and text (pooled ones included) and detaches from the world.</summary>
        public void Clear()
        {
            Bind(null);
            _bars.Clear();
            foreach (var pool in _pools) pool.Clear();
            _floaters.Clear();
            _labelPool.Clear();
            _structureLayer.Clear();
            _unitLayer.Clear();
            _heroLayer.Clear();
            _floatLayer.Clear();
        }
    }
}
