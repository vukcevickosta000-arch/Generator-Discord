using System;
using System.Collections.Generic;
using Bloodfall.Client.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Bloodfall.Client.UI
{
    public enum ToastKind { Info, Success, Error }

    /// <summary>Base class for full screens (login, main client, HUD...). Screens build their UI in code.</summary>
    public abstract class UIScreen
    {
        public VisualElement Root { get; private set; }
        protected GameApp App => GameApp.Instance;
        protected UIManager UI => GameApp.Instance.UI;
        public bool Visible => Root != null && Root.style.display != DisplayStyle.None;

        public VisualElement Build()
        {
            Root = new VisualElement();
            Root.AddToClassList("fill");
            Root.pickingMode = PickingMode.Ignore;
            OnBuild(Root);
            return Root;
        }

        protected abstract void OnBuild(VisualElement root);
        public virtual void OnShow() { }
        public virtual void OnHide() { }
        public virtual void Tick(float dt) { }
        /// <summary>Escape pressed while this screen is on top. Return true if handled.</summary>
        public virtual bool OnEscape() => false;
    }

    /// <summary>
    /// Owns the UI Toolkit document. Layers: screens, overlays (dialogs), toasts, tooltip, fader.
    /// PanelSettings are created at runtime so no scene or asset wiring is needed.
    /// </summary>
    public sealed class UIManager : ITickable
    {
        public readonly UIDocument Document;
        public VisualElement Root { get; private set; }
        public VisualElement ScreenLayer { get; private set; }
        public VisualElement OverlayLayer { get; private set; }
        public VisualElement ToastLayer { get; private set; }
        public VisualElement TooltipLayer { get; private set; }
        public VisualElement FadeLayer { get; private set; }
        public UIScreen Current { get; private set; }
        public readonly List<VisualElement> OpenOverlays = new List<VisualElement>();
        private readonly Dictionary<Type, UIScreen> _screens = new Dictionary<Type, UIScreen>();
        private readonly ClientSettings _settings;
        private float _fadeTarget, _fade;
        private VisualElement _tooltip;
        private Label _fps;
        private float _fpsTimer; private int _fpsFrames;

        public UIManager(GameObject host, ClientSettings settings)
        {
            _settings = settings;
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/BloodfallTheme");
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            ps.sortingOrder = 100;
            var go = new GameObject("UI");
            go.transform.SetParent(host.transform, false);
            Document = go.AddComponent<UIDocument>();
            Document.panelSettings = ps;
            BuildRoot();
            ApplyScale();
        }

        private void BuildRoot()
        {
            var docRoot = Document.rootVisualElement;
            docRoot.Clear();
            var sheet = Resources.Load<StyleSheet>("UI/Styles/bloodfall");
            if (sheet != null) docRoot.styleSheets.Add(sheet);
            else Debug.LogError("UI stylesheet missing: Resources/UI/Styles/bloodfall.uss");
            Root = new VisualElement();
            Root.AddToClassList("bf-root");
            Root.AddToClassList("fill");
            Root.pickingMode = PickingMode.Ignore;
            docRoot.Add(Root);
            ScreenLayer = Layer("screens");
            OverlayLayer = Layer("overlays");
            ToastLayer = Layer("toasts");
            ToastLayer.style.alignItems = Align.FlexEnd;
            ToastLayer.style.paddingTop = 80;
            ToastLayer.style.paddingRight = 24;
            TooltipLayer = Layer("tooltip");
            FadeLayer = Layer("fade");
            FadeLayer.style.backgroundColor = new Color(0, 0, 0, 1);
            FadeLayer.style.opacity = 0;
            _fps = new Label { pickingMode = PickingMode.Ignore };
            _fps.AddToClassList("t-small");
            _fps.style.position = Position.Absolute;
            _fps.style.right = 8;
            _fps.style.bottom = 4;
            Root.Add(_fps);
        }

        private VisualElement Layer(string name)
        {
            var e = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            e.AddToClassList("fill");
            Root.Add(e);
            return e;
        }

        public void ApplyScale()
        {
            if (Document.panelSettings != null) Document.panelSettings.scale = Mathf.Clamp(_settings.UiScale, 0.7f, 1.5f);
        }

        public T Get<T>() where T : UIScreen, new()
        {
            if (!_screens.TryGetValue(typeof(T), out var s))
            {
                s = new T();
                ScreenLayer.Add(s.Build());
                s.Root.style.display = DisplayStyle.None;
                _screens[typeof(T)] = s;
            }
            return (T)s;
        }

        public T Show<T>() where T : UIScreen, new()
        {
            var s = Get<T>();
            if (Current == s) return s;
            if (Current != null) { Current.Root.style.display = DisplayStyle.None; Current.OnHide(); }
            Current = s;
            s.Root.style.display = DisplayStyle.Flex;
            s.Root.BringToFront();
            s.OnShow();
            return s;
        }

        /// <summary>Destroys a cached screen so it is rebuilt fresh next time (e.g. HUD per match).</summary>
        public void Discard<T>() where T : UIScreen
        {
            if (!_screens.TryGetValue(typeof(T), out var s)) return;
            if (Current == s) { s.OnHide(); Current = null; }
            s.Root.RemoveFromHierarchy();
            _screens.Remove(typeof(T));
        }

        public void FadeTo(float alpha) => _fadeTarget = alpha;

        public void Tick(float dt)
        {
            Current?.Tick(dt);
            if (Math.Abs(_fade - _fadeTarget) > 0.001f)
            {
                _fade = Mathf.MoveTowards(_fade, _fadeTarget, dt * 2.2f);
                FadeLayer.style.opacity = _fade;
                FadeLayer.pickingMode = _fade > 0.5f ? PickingMode.Position : PickingMode.Ignore;
            }
            if (_tooltip != null) PositionTooltip();
            if (_settings.ShowFps)
            {
                _fpsFrames++;
                _fpsTimer += dt;
                if (_fpsTimer >= 0.5f) { _fps.text = $"{_fpsFrames / _fpsTimer:0} FPS"; _fpsFrames = 0; _fpsTimer = 0; }
            }
            else if (_fps.text.Length > 0) _fps.text = "";
            if (Input.InputBridge.GetKeyDown(KeyCode.Escape)) HandleEscape();
        }

        private void HandleEscape()
        {
            if (OpenOverlays.Count > 0)
            {
                var top = OpenOverlays[OpenOverlays.Count - 1];
                if (top.userData is Action onEscape) { onEscape(); return; }
                CloseOverlay(top);
                return;
            }
            Current?.OnEscape();
        }

        // ------------------------------------------------------------------ overlays / dialogs

        public VisualElement ShowOverlay(VisualElement content, Action onEscape = null, bool shade = true)
        {
            var wrap = new VisualElement();
            wrap.AddToClassList(shade ? "overlay-shade" : "fill");
            if (!shade) { wrap.style.alignItems = Align.Center; wrap.style.justifyContent = Justify.Center; }
            wrap.userData = onEscape;
            wrap.Add(content);
            OverlayLayer.Add(wrap);
            OpenOverlays.Add(wrap);
            GameApp.Instance?.Audio?.PlayUi("panel_open");
            return wrap;
        }

        public void CloseOverlay(VisualElement wrap)
        {
            if (wrap == null) return;
            wrap.RemoveFromHierarchy();
            OpenOverlays.Remove(wrap);
            GameApp.Instance?.Audio?.PlayUi("panel_close");
        }

        public void CloseAllOverlays()
        {
            foreach (var o in OpenOverlays.ToArray()) o.RemoveFromHierarchy();
            OpenOverlays.Clear();
        }

        /// <summary>Modal message box with custom buttons. Returns the overlay (use CloseOverlay to dismiss).</summary>
        public VisualElement Dialog(string title, string message, params (string label, string style, Action action)[] buttons)
        {
            var d = El.Div("dialog", "col");
            d.style.maxWidth = 620;
            d.Add(El.Text(title, "t-title", "t-center"));
            d.Add(El.Div("divider"));
            var msg = El.Text(message, "t-body", "t-center", "mt-m");
            d.Add(msg);
            var row = El.Div("row", "center", "mt-l");
            VisualElement overlay = null;
            foreach (var (label, style, action) in buttons)
            {
                var b = El.Btn(label, () => { CloseOverlay(overlay); action?.Invoke(); }, "btn", style);
                row.Add(b);
            }
            if (buttons.Length == 0) row.Add(El.Btn("Close", () => CloseOverlay(overlay), "btn"));
            d.Add(row);
            overlay = ShowOverlay(d);
            return overlay;
        }

        public void Error(string title, string message) => Dialog(title, message, ("OK", "btn--primary", null));

        public void Toast(string text, ToastKind kind = ToastKind.Info, float seconds = 5f)
        {
            var t = El.Div("toast", kind == ToastKind.Info ? "toast--info" : kind == ToastKind.Success ? "toast--success" : "toast");
            t.Add(El.Text(text, "t-body"));
            t.pickingMode = PickingMode.Ignore;
            ToastLayer.Add(t);
            GameApp.Instance?.Audio?.PlayUi(kind == ToastKind.Error ? "error" : "notify");
            t.schedule.Execute(() => t.RemoveFromHierarchy()).StartingIn((long)(seconds * 1000));
        }

        // ------------------------------------------------------------------ tooltips

        public void AttachTooltip(VisualElement target, Func<VisualElement> build)
        {
            target.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (!_settings.ShowTooltips) return;
                HideTooltip();
                var content = build();
                if (content == null) return;
                _tooltip = El.Div("tooltip");
                _tooltip.pickingMode = PickingMode.Ignore;
                _tooltip.style.position = Position.Absolute;
                _tooltip.Add(content);
                TooltipLayer.Add(_tooltip);
                PositionTooltip();
            });
            target.RegisterCallback<MouseLeaveEvent>(_ => HideTooltip());
            target.RegisterCallback<DetachFromPanelEvent>(_ => HideTooltip());
        }

        public void AttachTooltip(VisualElement target, string title, string body) =>
            AttachTooltip(target, () =>
            {
                var c = El.Div("col");
                c.Add(El.Text(title, "t-heading"));
                if (!string.IsNullOrEmpty(body)) c.Add(El.Text(body, "t-body", "mt-m"));
                return c;
            });

        public void HideTooltip()
        {
            _tooltip?.RemoveFromHierarchy();
            _tooltip = null;
        }

        private void PositionTooltip()
        {
            var panel = Root.panel;
            if (panel == null || _tooltip == null) return;
            var mouse = Input.InputBridge.MousePosition;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(mouse.x, Screen.height - mouse.y));
            float w = _tooltip.resolvedStyle.width, h = _tooltip.resolvedStyle.height;
            float maxX = Root.resolvedStyle.width, maxY = Root.resolvedStyle.height;
            float x = p.x + 18, y = p.y + 18;
            if (!float.IsNaN(w) && x + w > maxX) x = p.x - w - 12;
            if (!float.IsNaN(h) && y + h > maxY) y = p.y - h - 12;
            _tooltip.style.left = x;
            _tooltip.style.top = y;
        }

        /// <summary>Converts a screen-space position (pixels, origin bottom-left) into panel coordinates.</summary>
        public Vector2 ScreenToPanel(Vector2 screen)
        {
            var panel = Root.panel;
            return panel == null ? screen : RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
        }

        /// <summary>True when the pointer is over an interactive UI element (so world clicks should be ignored).</summary>
        public bool PointerOverUI()
        {
            var panel = Root.panel;
            if (panel == null) return false;
            var p = ScreenToPanel(Input.InputBridge.MousePosition);
            var picked = panel.Pick(p);
            return picked != null && picked.pickingMode == PickingMode.Position && picked != Root;
        }
    }
}
