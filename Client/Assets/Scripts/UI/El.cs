using System;
using System.Collections.Generic;
using Bloodfall.Client.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Bloodfall.Client.UI
{
    /// <summary>
    /// Terse builders for UI Toolkit elements. Screens are built in C# (compile-checked, refactor-safe) and styled by
    /// bloodfall.uss; every interactive element plays the shared hover/click sounds.
    /// </summary>
    public static class El
    {
        public static VisualElement Div(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) e.AddToClassList(c);
            return e;
        }

        public static Label Text(string text, params string[] classes)
        {
            var l = new Label(text ?? "");
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) l.AddToClassList(c);
            return l;
        }

        public static Button Btn(string text, Action onClick, params string[] classes)
        {
            var b = new Button(() =>
            {
                GameApp.Instance?.Audio?.PlayUi("click");
                onClick?.Invoke();
            }) { text = text };
            b.AddToClassList("btn");
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) b.AddToClassList(c);
            b.RegisterCallback<MouseEnterEvent>(_ => { if (b.enabledInHierarchy) GameApp.Instance?.Audio?.PlayUi("hover"); });
            return b;
        }

        public static Button Tab(string text, Action onClick, params string[] classes)
        {
            var b = new Button(() => { GameApp.Instance?.Audio?.PlayUi("tab"); onClick?.Invoke(); }) { text = text };
            b.AddToClassList("tab");
            foreach (var c in classes) b.AddToClassList(c);
            b.RegisterCallback<MouseEnterEvent>(_ => GameApp.Instance?.Audio?.PlayUi("hover"));
            return b;
        }

        public static Label Link(string text, Action onClick)
        {
            var l = Text(text, "link");
            l.RegisterCallback<ClickEvent>(_ => { GameApp.Instance?.Audio?.PlayUi("click"); onClick?.Invoke(); });
            return l;
        }

        public static TextField Field(string label, bool password = false, string value = "", int maxLength = 128)
        {
            var f = new TextField(label) { isPasswordField = password, value = value ?? "", maxLength = maxLength };
            f.AddToClassList("field");
            f.AddToClassList("field--column");
            return f;
        }

        public static Toggle Check(string label, bool value, Action<bool> onChange = null)
        {
            var t = new Toggle(label) { value = value };
            t.AddToClassList("checkbox");
            if (onChange != null) t.RegisterValueChangedCallback(e => { GameApp.Instance?.Audio?.PlayUi("click"); onChange(e.newValue); });
            return t;
        }

        public static Slider SliderField(string label, float min, float max, float value, Action<float> onChange)
        {
            var s = new Slider(label, min, max) { value = value };
            s.AddToClassList("slider");
            s.RegisterValueChangedCallback(e => onChange?.Invoke(e.newValue));
            return s;
        }

        public static DropdownField Dropdown(string label, List<string> choices, int index, Action<int> onChange)
        {
            var d = new DropdownField(label, choices, Mathf.Clamp(index, 0, Math.Max(0, choices.Count - 1)));
            d.AddToClassList("dropdown");
            d.RegisterValueChangedCallback(e => onChange?.Invoke(choices.IndexOf(e.newValue)));
            return d;
        }

        public static VisualElement Img(string resourcePath, float w, float h, params string[] classes)
        {
            var e = Div(classes);
            e.style.width = w;
            e.style.height = h;
            SetImage(e, resourcePath);
            return e;
        }

        private static readonly Dictionary<string, Texture2D> TexCache = new Dictionary<string, Texture2D>();

        public static Texture2D Tex(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return null;
            if (!TexCache.TryGetValue(resourcePath, out var t))
            {
                t = Resources.Load<Texture2D>(resourcePath);
                TexCache[resourcePath] = t;
            }
            return t;
        }

        public static void SetImage(VisualElement e, string resourcePath)
        {
            var t = Tex(resourcePath);
            e.style.backgroundImage = t != null ? new StyleBackground(t) : new StyleBackground(StyleKeyword.None);
        }

        public static ScrollView Scroll(params string[] classes)
        {
            var s = new ScrollView(ScrollViewMode.Vertical);
            s.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            foreach (var c in classes) s.AddToClassList(c);
            return s;
        }

        public static T Cls<T>(this T e, params string[] classes) where T : VisualElement
        {
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) e.AddToClassList(c);
            return e;
        }

        public static T With<T>(this T e, params VisualElement[] children) where T : VisualElement
        {
            foreach (var c in children) if (c != null) e.Add(c);
            return e;
        }

        public static T Size<T>(this T e, float w, float h) where T : VisualElement
        {
            if (w >= 0) e.style.width = w;
            if (h >= 0) e.style.height = h;
            return e;
        }

        public static T Width<T>(this T e, float w) where T : VisualElement { e.style.width = w; return e; }
        public static T Height<T>(this T e, float h) where T : VisualElement { e.style.height = h; return e; }
        public static T Grow<T>(this T e, float g = 1) where T : VisualElement { e.style.flexGrow = g; return e; }
        public static T Show<T>(this T e, bool visible) where T : VisualElement { e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None; return e; }

        public static T OnClick<T>(this T e, Action a) where T : VisualElement
        {
            e.RegisterCallback<ClickEvent>(_ => { GameApp.Instance?.Audio?.PlayUi("click"); a(); });
            return e;
        }

        public static T Hoverable<T>(this T e) where T : VisualElement
        {
            e.RegisterCallback<MouseEnterEvent>(_ => GameApp.Instance?.Audio?.PlayUi("hover"));
            return e;
        }

        public static VisualElement Spacer(float size = -1)
        {
            var s = new VisualElement();
            if (size < 0) s.style.flexGrow = 1; else { s.style.width = size; s.style.height = size; }
            return s;
        }

        /// <summary>Horizontal progress bar (fill 0..1) with optional centred text.</summary>
        public static (VisualElement bar, VisualElement fill, Label text) Bar(string fillClass, float height = 20)
        {
            var bar = Div("bar");
            bar.style.height = height;
            var fill = Div("bar-fill", fillClass);
            var text = Text("", "bar-text");
            bar.Add(fill);
            bar.Add(text);
            return (bar, fill, text);
        }

        public static void SetFill(VisualElement fill, float t) => fill.style.width = Length.Percent(Mathf.Clamp01(t) * 100f);

        public static VisualElement Dot(string statusClass) => Div("dot", statusClass);

        public static string FormatTime(float seconds)
        {
            bool neg = seconds < 0;
            seconds = Mathf.Abs(seconds);
            int m = (int)(seconds / 60), s = (int)(seconds % 60);
            return (neg ? "-" : "") + m + ":" + s.ToString("00");
        }

        public static string Pretty(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var s = id;
            foreach (var p in new[] { "hero_", "item_", "map_", "moba_", "creep_", "neutral_" }) if (s.StartsWith(p)) s = s.Substring(p.Length);
            var parts = s.Split('_');
            for (int i = 0; i < parts.Length; i++) if (parts[i].Length > 0) parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            return string.Join(" ", parts);
        }
    }
}
