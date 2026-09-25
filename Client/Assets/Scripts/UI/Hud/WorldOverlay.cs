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
    /// combat text. Positions are re-projected every frame from the interpolated entity views.
    /// </summary>
    public sealed class WorldOverlay
    {
        private sealed class BarView
        {
            public VisualElement Root, HpFill, ManaFill, Ticks, Shield;
            public Label Name, Level;
            public int EntityId;
            public bool Hero, Big;
            public float LastMaxHp;
        }

        private sealed class Floater
        {
            public Label Label;
            public Vector3 World;
            public float Age, Life;
            public Vector2 Drift;
        }

        public VisualElement Root { get; private set; }
        private readonly Dictionary<int, BarView> _bars = new Dictionary<int, BarView>();
        private readonly Stack<BarView> _pool = new Stack<BarView>();
        private readonly List<Floater> _floaters = new List<Floater>();
        private readonly Stack<Label> _labelPool = new Stack<Label>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private MatchWorld _world;
        private readonly ClientSettings _settings;

        public WorldOverlay(ClientSettings settings)
        {
            _settings = settings;
            Root = new VisualElement { pickingMode = PickingMode.Ignore };
            Root.AddToClassList("fill");
        }

        public void Bind(MatchWorld world) => _world = world;

        private BarView GetBar(EntityView v)
        {
            if (_pool.Count > 0)
            {
                var b = _pool.Pop();
                if (b.Hero == v.IsHero && b.Big == v.IsStructure) { b.Root.style.display = DisplayStyle.Flex; return b; }
                b.Root.RemoveFromHierarchy();
            }
            var bar = new BarView { Hero = v.IsHero, Big = v.IsStructure };
            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.flexDirection = FlexDirection.Column;
            root.style.alignItems = Align.Center;
            float w = v.IsHero ? 96 : v.IsStructure ? 120 : 52;
            if (v.IsHero)
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
            hp.style.height = v.IsHero ? 9 : v.IsStructure ? 8 : 5;
            hp.style.backgroundColor = new Color(0.05f, 0.03f, 0.03f, 0.9f);
            hp.style.borderTopWidth = hp.style.borderBottomWidth = hp.style.borderLeftWidth = hp.style.borderRightWidth = 1;
            hp.style.borderTopColor = hp.style.borderBottomColor = hp.style.borderLeftColor = hp.style.borderRightColor = new Color(0, 0, 0, 0.9f);
            bar.HpFill = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.HpFill.style.position = Position.Absolute;
            bar.HpFill.style.left = 0; bar.HpFill.style.top = 0; bar.HpFill.style.bottom = 0;
            hp.Add(bar.HpFill);
            bar.Shield = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.Shield.style.position = Position.Absolute;
            bar.Shield.style.top = 0; bar.Shield.style.bottom = 0;
            bar.Shield.style.backgroundColor = new Color(0.85f, 0.85f, 0.9f, 0.85f);
            hp.Add(bar.Shield);
            if (v.IsHero)
            {
                bar.Ticks = new VisualElement { pickingMode = PickingMode.Ignore };
                bar.Ticks.style.position = Position.Absolute;
                bar.Ticks.style.left = 0; bar.Ticks.style.right = 0; bar.Ticks.style.top = 0; bar.Ticks.style.bottom = 0;
                bar.Ticks.style.flexDirection = FlexDirection.Row;
                hp.Add(bar.Ticks);
            }
            root.Add(hp);
            if (v.IsHero)
            {
                var mana = new VisualElement { pickingMode = PickingMode.Ignore };
                mana.style.width = w;
                mana.style.height = 4;
                mana.style.backgroundColor = new Color(0.03f, 0.03f, 0.08f, 0.9f);
                bar.ManaFill = new VisualElement { pickingMode = PickingMode.Ignore };
                bar.ManaFill.style.position = Position.Absolute;
                bar.ManaFill.style.left = 0; bar.ManaFill.style.top = 0; bar.ManaFill.style.bottom = 0;
                bar.ManaFill.style.backgroundColor = new Color(0.25f, 0.45f, 1f);
                mana.Add(bar.ManaFill);
                root.Add(mana);
            }
            bar.Root = root;
            Root.Add(root);
            return bar;
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

        public void Update(float dt, Camera cam)
        {
            if (_world == null || cam == null) return;
            var ui = GameApp.Instance.UI;
            _seen.Clear();
            if (_settings.ShowHealthBars)
            {
                foreach (var v in _world.Views)
                {
                    if (!v.Present || v.Dying || v.State == null || !v.Model.Root.activeSelf || v.State.Has(EntityFlags.Dead) || v.State.MaxHp <= 0) continue;
                    if (v.Kind == UnitKind.Ward && _world.IsEnemy(v.Team)) continue;
                    if (v.IsHero && !_settings.ShowAllyHeroBars && !_world.IsEnemy(v.Team) && v.State.OwnerPlayer != _world.LocalPlayerId) continue;
                    var sp = cam.WorldToScreenPoint(v.Point(1f) + Vector3.up * 0.35f);
                    if (sp.z <= 0 || sp.x < -100 || sp.x > Screen.width + 100 || sp.y < -50 || sp.y > Screen.height + 80) continue;
                    _seen.Add(v.Id);
                    if (!_bars.TryGetValue(v.Id, out var bar)) { bar = GetBar(v); bar.EntityId = v.Id; _bars[v.Id] = bar; }
                    var p = ui.ScreenToPanel(new Vector2(sp.x, sp.y));
                    float w = v.IsHero ? 96 : v.IsStructure ? 120 : 52;
                    bar.Root.style.left = p.x - w / 2;
                    bar.Root.style.top = p.y - (v.IsHero ? 30 : 10);
                    var s = v.State;
                    float frac = Mathf.Clamp01(s.Hp / s.MaxHp);
                    bar.HpFill.style.width = Length.Percent(frac * 100f);
                    bar.HpFill.style.backgroundColor = BarColor(v);
                    bar.Shield.style.display = DisplayStyle.None;
                    if (bar.ManaFill != null) bar.ManaFill.style.width = Length.Percent(s.MaxMana > 0 ? Mathf.Clamp01(s.Mana / s.MaxMana) * 100f : 0f);
                    if (bar.Name != null)
                    {
                        var pl = FindPlayer(s.OwnerPlayer);
                        bar.Name.text = pl?.Name ?? "";
                        bar.Name.style.color = BarColor(v);
                        bar.Level.text = s.Level.ToString();
                    }
                    if (bar.Ticks != null && Mathf.Abs(bar.LastMaxHp - s.MaxHp) > 1f)
                    {
                        // Segment ticks every 250 health (bigger ticks every 1000) help read burst thresholds.
                        bar.LastMaxHp = s.MaxHp;
                        bar.Ticks.Clear();
                        int n = Mathf.FloorToInt(s.MaxHp / 250f);
                        for (int i = 1; i <= n && i < 40; i++)
                        {
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
            }
            // Recycle bars for entities not drawn this frame.
            List<int> gone = null;
            foreach (var kv in _bars) if (!_seen.Contains(kv.Key)) (gone ??= new List<int>()).Add(kv.Key);
            if (gone != null)
                foreach (var id in gone)
                {
                    var b = _bars[id];
                    b.Root.style.display = DisplayStyle.None;
                    _bars.Remove(id);
                    _pool.Push(b);
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
                f.Label.style.left = p.x + f.Drift.x * t - 20;
                f.Label.style.top = p.y - 30 - 50 * t + f.Drift.y * t;
                f.Label.style.opacity = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
                float pop = t < 0.12f ? 1.35f - t * 3f : 1f;
                f.Label.style.scale = new Scale(Vector3.one * pop);
            }
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
                l.AddToClassList("floating");
                Root.Add(l);
            }
            l.ClearClassList();
            l.AddToClassList("floating");
            if (!string.IsNullOrEmpty(cls)) l.AddToClassList(cls);
            l.text = text;
            l.style.display = DisplayStyle.None;
            _floaters.Add(new Floater { Label = l, World = world, Life = life, Drift = new Vector2(Random.Range(-18f, 18f), Random.Range(-6f, 6f)) });
        }

        public void Clear()
        {
            foreach (var b in _bars.Values) b.Root.RemoveFromHierarchy();
            _bars.Clear();
            _pool.Clear();
            foreach (var f in _floaters) f.Label.RemoveFromHierarchy();
            _floaters.Clear();
        }
    }
}
