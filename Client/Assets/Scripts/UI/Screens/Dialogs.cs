using System;
using System.Collections.Generic;
using System.Linq;
using Bloodfall.Client.Core;
using Bloodfall.Contracts;
using UnityEngine;
using UnityEngine.UIElements;

namespace Bloodfall.Client.UI.Screens
{
    // ====================================================================== settings

    /// <summary>Settings (graphics, audio, controls with rebinding, gameplay, interface). Applied live and saved locally.</summary>
    public static class SettingsDialog
    {
        private static VisualElement _overlay;

        public static void Open()
        {
            var app = GameApp.Instance;
            if (_overlay != null && _overlay.panel != null) return;
            var s = app.Settings;
            var d = El.Div("dialog", "col");
            d.style.width = 860;
            d.style.height = 720;
            d.Add(El.Text("Settings", "t-title", "t-center"));
            var tabs = El.Div("tabbar", "center", "mt-m");
            d.Add(tabs);
            var body = El.Scroll().Grow();
            body.style.marginTop = 10;
            d.Add(body);
            var pages = new Dictionary<string, Action<VisualElement>>
            {
                ["Graphics"] = c => Graphics(c, s),
                ["Audio"] = c => Audio(c, s),
                ["Controls"] = c => Controls(c, s),
                ["Gameplay"] = c => Gameplay(c, s),
                ["Interface"] = c => Interface(c, s),
            };
            var buttons = new Dictionary<string, Button>();
            void Show(string name)
            {
                foreach (var kv in buttons) kv.Value.EnableInClassList("tab--active", kv.Key == name);
                body.contentContainer.Clear();
                pages[name](body.contentContainer);
            }
            foreach (var name in pages.Keys)
            {
                var n = name;
                buttons[n] = El.Tab(n, () => Show(n));
                tabs.Add(buttons[n]);
            }
            var row = El.Div("row", "center", "mt-m");
            row.Add(El.Btn("Done", Close, "btn--primary"));
            row.Add(El.Btn("Reset to Defaults", () =>
            {
                app.UI.Dialog("Reset Settings", "Restore all settings (including key bindings) to their defaults?", ("Reset", "btn--danger", () =>
                {
                    var fresh = new ClientSettings();
                    foreach (var f in typeof(ClientSettings).GetFields()) f.SetValue(s, f.GetValue(fresh));
                    foreach (var kv in KeyBinds.Defaults) s.KeyBindings[kv.Key] = kv.Value;
                    s.ResolutionWidth = Screen.currentResolution.width;
                    s.ResolutionHeight = Screen.currentResolution.height;
                    s.ApplyGraphics();
                    s.Save();
                    Show("Graphics");
                }), ("Cancel", "btn--ghost", null));
            }, "btn--ghost"));
            d.Add(row);
            _overlay = app.UI.ShowOverlay(d, Close);
            Show("Graphics");
        }

        private static void Close()
        {
            var app = GameApp.Instance;
            app.Settings.Save();
            app.UI.ApplyScale();
            app.UI.CloseOverlay(_overlay);
            _overlay = null;
        }

        private static VisualElement Section(VisualElement c, string title)
        {
            c.Add(El.Text(title, "t-subheading", "mt-m"));
            var s = El.Div("col");
            c.Add(s);
            return s;
        }

        private static void Graphics(VisualElement c, ClientSettings s)
        {
            var sec = Section(c, "DISPLAY");
            var res = Screen.resolutions.Select(r => (r.width, r.height)).Distinct().OrderByDescending(r => r.width * r.height).ToList();
            if (res.Count == 0) res.Add((s.ResolutionWidth, s.ResolutionHeight));
            int cur = Math.Max(0, res.FindIndex(r => r.width == s.ResolutionWidth && r.height == s.ResolutionHeight));
            sec.Add(El.Dropdown("Resolution", res.Select(r => $"{r.width} x {r.height}").ToList(), cur, i => { if (i >= 0) { s.ResolutionWidth = res[i].width; s.ResolutionHeight = res[i].height; s.ApplyGraphics(); } }));
            sec.Add(El.Dropdown("Window mode", new List<string> { "Fullscreen", "Borderless", "Windowed" }, (int)s.WindowMode, i => { s.WindowMode = (WindowModeSetting)Math.Max(0, i); s.ApplyGraphics(); }));
            sec.Add(El.Check("Vertical sync", s.VSync, v => { s.VSync = v; s.ApplyGraphics(); }));
            sec.Add(El.Dropdown("Frame limit", new List<string> { "Unlimited", "60", "120", "144", "240" }, s.FrameLimit switch { 60 => 1, 120 => 2, 144 => 3, 240 => 4, _ => 0 }, i => { s.FrameLimit = i switch { 1 => 60, 2 => 120, 3 => 144, 4 => 240, _ => 0 }; s.ApplyGraphics(); }));
            sec.Add(El.SliderField("Brightness", 0.6f, 1.6f, s.Brightness, v => s.Brightness = v));
            var q = Section(c, "QUALITY");
            q.Add(El.Dropdown("Preset", new List<string> { "Low", "Medium", "High", "Ultra", "Custom" }, (int)s.Quality, i => { if (i >= 0 && i < 4) { s.ApplyPreset((QualityPreset)i); s.ApplyGraphics(); c.Clear(); Graphics(c, s); } }));
            q.Add(El.SliderField("Render scale", 0.5f, 2f, s.RenderScale, v => { s.RenderScale = v; s.Quality = QualityPreset.Custom; s.ApplyGraphics(); }));
            q.Add(El.Dropdown("Texture quality", new List<string> { "Full", "Half", "Quarter" }, s.TextureQuality, i => { s.TextureQuality = Math.Max(0, i); s.Quality = QualityPreset.Custom; s.ApplyGraphics(); }));
            q.Add(El.Dropdown("Shadows", new List<string> { "Off", "Low", "Medium", "High" }, s.ShadowQuality, i => { s.ShadowQuality = Math.Max(0, i); s.Quality = QualityPreset.Custom; s.ApplyGraphics(); }));
            q.Add(El.Dropdown("Effects", new List<string> { "Low", "Medium", "High", "Ultra" }, s.EffectsQuality, i => { s.EffectsQuality = Math.Max(0, i); s.Quality = QualityPreset.Custom; }));
            q.Add(El.Dropdown("Anti-aliasing", new List<string> { "Off", "FXAA", "MSAA 4x" }, s.AntiAliasing, i => { s.AntiAliasing = Math.Max(0, i); s.Quality = QualityPreset.Custom; s.ApplyGraphics(); }));
            q.Add(El.Check("Bloom", s.Bloom, v => { s.Bloom = v; s.Quality = QualityPreset.Custom; }));
            q.Add(El.Check("Ambient occlusion", s.AmbientOcclusion, v => { s.AmbientOcclusion = v; s.Quality = QualityPreset.Custom; }));
            q.Add(El.Check("Motion blur", s.MotionBlur, v => { s.MotionBlur = v; s.Quality = QualityPreset.Custom; }));
            q.Add(El.Text("Post-processing changes apply to the next scene (menu backdrop or match).", "t-small"));
        }

        private static void Audio(VisualElement c, ClientSettings s)
        {
            var sec = Section(c, "VOLUME");
            sec.Add(El.SliderField("Master", 0, 1, s.MasterVolume, v => s.MasterVolume = v));
            sec.Add(El.SliderField("Music", 0, 1, s.MusicVolume, v => s.MusicVolume = v));
            sec.Add(El.SliderField("Effects", 0, 1, s.EffectsVolume, v => s.EffectsVolume = v));
            sec.Add(El.SliderField("Hero voices", 0, 1, s.VoiceVolume, v => s.VoiceVolume = v));
            sec.Add(El.SliderField("Announcer", 0, 1, s.AnnouncerVolume, v => s.AnnouncerVolume = v));
            sec.Add(El.SliderField("Interface", 0, 1, s.UiVolume, v => s.UiVolume = v));
            sec.Add(El.Check("Mute when the window is not focused", s.MuteWhenUnfocused, v => s.MuteWhenUnfocused = v));
        }

        private static void Controls(VisualElement c, ClientSettings s)
        {
            var cam = Section(c, "CAMERA");
            cam.Add(El.SliderField("Camera speed", 0.3f, 2.5f, s.CameraSpeed, v => s.CameraSpeed = v));
            cam.Add(El.Check("Edge scrolling", s.EdgeScrolling, v => s.EdgeScrolling = v));
            cam.Add(El.SliderField("Edge scroll speed", 0.3f, 2.5f, s.EdgeScrollSpeed, v => s.EdgeScrollSpeed = v));
            cam.Add(El.SliderField("Zoom speed", 0.3f, 2.5f, s.ZoomSpeed, v => s.ZoomSpeed = v));
            cam.Add(El.Check("Invert middle-mouse drag", s.InvertDrag, v => s.InvertDrag = v));
            var casting = Section(c, "CASTING");
            casting.Add(El.Check("Quick cast (cast at cursor on key press)", s.QuickCast, v => s.QuickCast = v));
            casting.Add(El.Check("Auto-attack when idle", s.AutoAttack, v => s.AutoAttack = v));
            casting.Add(El.Check("Right-click on minimap moves the hero", s.MinimapRightClickMoves, v => s.MinimapRightClickMoves = v));
            var keys = Section(c, "KEY BINDINGS");
            keys.Add(El.Text("Click a binding, then press the new key. Esc cancels.", "t-small"));
            foreach (var action in KeyBinds.Defaults.Keys)
            {
                var row = El.Div("list-row");
                row.Add(El.Text(KeyBinds.Labels.TryGetValue(action, out var label) ? label : action, "grow"));
                var a = action;
                Button b = null;
                b = El.Btn(KeyBinds.Get(s, a).ToString(), () =>
                {
                    b.text = "Press a key…";
                    b.Focus();
                    EventCallback<KeyDownEvent> handler = null;
                    handler = e =>
                    {
                        if (e.keyCode == KeyCode.None) return;
                        b.UnregisterCallback(handler);
                        if (e.keyCode != KeyCode.Escape)
                        {
                            // Swap with any action already using this key.
                            var clash = s.KeyBindings.FirstOrDefault(kv => kv.Value == e.keyCode.ToString() && kv.Key != a).Key;
                            if (clash != null) s.KeyBindings[clash] = s.KeyBindings[a];
                            s.KeyBindings[a] = e.keyCode.ToString();
                            if (clash != null) { c.Clear(); Controls(c, s); return; }
                        }
                        b.text = KeyBinds.Get(s, a).ToString();
                        e.StopPropagation();
                    };
                    b.RegisterCallback(handler);
                }, "btn--small");
                b.style.minWidth = 140;
                row.Add(b);
                keys.Add(row);
            }
        }

        private static void Gameplay(VisualElement c, ClientSettings s)
        {
            var cam = Section(c, "CAMERA");
            cam.Add(El.Check("Camera follows your hero (pan to look away; your next order brings it back)", s.CameraFollowHero, v => s.CameraFollowHero = v));
            var sec = Section(c, "COMBAT FEEDBACK");
            sec.Add(El.Check("Show health bars", s.ShowHealthBars, v => s.ShowHealthBars = v));
            sec.Add(El.Check("Show allied hero bars", s.ShowAllyHeroBars, v => s.ShowAllyHeroBars = v));
            sec.Add(El.Check("Floating damage numbers", s.ShowDamageNumbers, v => s.ShowDamageNumbers = v));
            sec.Add(El.Check("Colorblind-friendly team colors", s.ColorblindMode, v => s.ColorblindMode = v));
        }

        private static void Interface(VisualElement c, ClientSettings s)
        {
            var sec = Section(c, "INTERFACE");
            sec.Add(El.SliderField("UI scale", 0.7f, 1.5f, s.UiScale, v => { s.UiScale = v; GameApp.Instance.UI.ApplyScale(); }));
            sec.Add(El.Check("Show tooltips", s.ShowTooltips, v => s.ShowTooltips = v));
            sec.Add(El.Check("Minimap on the left", s.MinimapOnLeft, v => s.MinimapOnLeft = v));
            sec.Add(El.Check("Show FPS counter", s.ShowFps, v => s.ShowFps = v));
        }
    }

    // ====================================================================== matchmaking

    /// <summary>"Match found" accept/decline prompt with countdown and live accept count.</summary>
    public static class MatchFoundDialog
    {
        private static VisualElement _overlay;
        private static Label _timer, _accepted;
        private static float _deadline;
        private static bool _responded;

        public static void Open(MatchFoundPayload m)
        {
            var app = GameApp.Instance;
            Close();
            _responded = false;
            _deadline = Time.unscaledTime + Mathf.Max(5f, m.AcceptSeconds);
            var d = El.Div("dialog", "col", "center").Width(560);
            d.Add(El.Text("MATCH FOUND", "t-display", "t-center"));
            d.Add(El.Text($"{El.Pretty(m.Queue)} · {m.Players} players · {m.Region}", "t-subheading", "t-center"));
            _timer = El.Text("", "t-title", "t-center", "mt-m");
            _accepted = El.Text("", "t-small", "t-center");
            d.Add(_timer);
            d.Add(_accepted);
            var row = El.Div("row", "center", "mt-l");
            Button accept = null, decline = null;
            accept = El.Btn("ACCEPT", async () =>
            {
                _responded = true; accept.SetEnabled(false); decline.SetEnabled(false); accept.text = "Accepted";
                var r = await app.Backend.RespondToMatch(true);
                if (!r.Ok) app.UI.Toast(r.Message, ToastKind.Error);
            }, "btn--primary", "btn--huge");
            decline = El.Btn("Decline", async () => { _responded = true; await app.Backend.RespondToMatch(false); Close(); }, "btn--ghost");
            row.Add(accept);
            row.Add(decline);
            d.Add(row);
            d.schedule.Execute(Update).Every(200);
            _overlay = app.UI.ShowOverlay(d, () => { });
            app.Audio.PlayUi("match_found");
            // Flash the taskbar / bring attention even when alt-tabbed.
            Application.runInBackground = true;
        }

        private static void Update()
        {
            if (_overlay == null) return;
            float left = Mathf.Max(0, _deadline - Time.unscaledTime);
            _timer.text = Mathf.CeilToInt(left) + "s";
            var q = GameApp.Instance.Backend.Queue;
            if (q != null && q.Required > 0) _accepted.text = $"{q.Accepted}/{q.Required} players accepted";
            if (left <= 0 && !_responded) Close();
        }

        public static void Close()
        {
            if (_overlay != null) GameApp.Instance.UI.CloseOverlay(_overlay);
            _overlay = null;
        }
    }

    // ====================================================================== social prompts

    public static class SocialPrompts
    {
        public static void PartyInvite(PartyInviteView inv)
        {
            var app = GameApp.Instance;
            app.UI.Dialog("Party Invitation", $"{inv.FromName} invited you to their party.",
                ("Join", "btn--primary", async () => { var r = await app.Backend.AcceptParty(inv.PartyId); if (!r.Ok) app.UI.Toast(r.Message, ToastKind.Error); }),
                ("Decline", "btn--ghost", async () => await app.Backend.DeclineParty(inv.PartyId)));
        }

        public static void ClanInvite(ClanInviteView inv)
        {
            var app = GameApp.Instance;
            app.UI.Dialog("Clan Invitation", $"{inv.FromName} invited you to join [{inv.ClanTag}] {inv.ClanName}.",
                ("Join Clan", "btn--primary", async () => { var r = await app.Backend.AcceptClanInvite(inv.InviteId); app.UI.Toast(r.Ok ? "Welcome to " + inv.ClanName + "!" : r.Message, r.Ok ? ToastKind.Success : ToastKind.Error); }),
                ("Later", "btn--ghost", null));
        }
    }
}
