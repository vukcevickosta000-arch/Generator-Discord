using System;
using System.Collections.Generic;
using Bloodfall.Core;
using UnityEngine;

namespace Bloodfall.Client.Core
{
    public enum QualityPreset { Low, Medium, High, Ultra, Custom }
    public enum WindowModeSetting { Fullscreen, Borderless, Windowed }

    /// <summary>Player settings persisted as JSON in PlayerPrefs (graphics, audio, controls, gameplay).</summary>
    public sealed class ClientSettings
    {
        // Graphics
        public int ResolutionWidth;
        public int ResolutionHeight;
        public WindowModeSetting WindowMode = WindowModeSetting.Borderless;
        public float RenderScale = 1f;
        public QualityPreset Quality = QualityPreset.High;
        public int TextureQuality = 0;        // 0 full, 1 half, 2 quarter
        public int ShadowQuality = 2;         // 0 off, 1 low, 2 medium, 3 high
        public int EffectsQuality = 2;        // particle budget
        public int AntiAliasing = 2;          // 0 off, 1 FXAA, 2 MSAA x4
        public bool AmbientOcclusion = true;
        public bool Bloom = true;
        public bool MotionBlur = false;
        public bool VSync = true;
        public int FrameLimit = 0;            // 0 = unlimited
        public float Brightness = 1f;

        // Audio (0..1)
        public float MasterVolume = 0.9f;
        public float MusicVolume = 0.55f;
        public float EffectsVolume = 0.85f;
        public float VoiceVolume = 0.9f;
        public float AnnouncerVolume = 1f;
        public float UiVolume = 0.7f;
        public bool MuteWhenUnfocused = true;

        // Controls
        public float CameraSpeed = 1f;
        public bool EdgeScrolling = true;
        public float EdgeScrollSpeed = 1f;
        public bool InvertDrag = false;
        public float ZoomSpeed = 1f;
        public bool QuickCast = false;
        public bool AutoAttack = true;
        public bool MinimapRightClickMoves = true;
        public bool MinimapOnLeft = true;
        public Dictionary<string, string> KeyBindings = new Dictionary<string, string>();

        // Gameplay / interface
        public bool ShowHealthBars = true;
        public bool ShowDamageNumbers = true;
        public bool ShowAllyHeroBars = true;
        public bool ShowTooltips = true;
        public bool ColorblindMode = false;
        public float UiScale = 1f;
        public bool ShowFps = false;

        // Account convenience (not a secret; the refresh token lives in SecureStore).
        public string LastLogin = "";
        public List<string> PreferredRegions = new List<string>();
        public List<string> FavoriteHeroes = new List<string>();

        private const string PrefKey = "bloodfall.settings.v1";

        public static ClientSettings Load()
        {
            try
            {
                var json = PlayerPrefs.GetString(PrefKey, "");
                if (!string.IsNullOrEmpty(json)) return JsonMapper.FromJson<ClientSettings>(json).WithDefaults();
            }
            catch (Exception e) { Debug.LogWarning("Settings reset: " + e.Message); }
            return new ClientSettings().WithDefaults();
        }

        private ClientSettings WithDefaults()
        {
            if (ResolutionWidth <= 0) { ResolutionWidth = Screen.currentResolution.width; ResolutionHeight = Screen.currentResolution.height; }
            foreach (var kv in KeyBinds.Defaults) if (!KeyBindings.ContainsKey(kv.Key)) KeyBindings[kv.Key] = kv.Value;
            if (PreferredRegions.Count == 0) PreferredRegions.Add("eu");
            return this;
        }

        public void Save()
        {
            PlayerPrefs.SetString(PrefKey, JsonMapper.ToJson(this));
            PlayerPrefs.Save();
        }

        public void ApplyGraphics()
        {
            var mode = WindowMode == WindowModeSetting.Fullscreen ? FullScreenMode.ExclusiveFullScreen
                : WindowMode == WindowModeSetting.Borderless ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (!Application.isEditor && ResolutionWidth > 0) Screen.SetResolution(ResolutionWidth, ResolutionHeight, mode);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FrameLimit > 0 ? FrameLimit : -1;
            int level = Quality == QualityPreset.Custom ? 2 : (int)Quality;
            if (QualitySettings.names.Length > 0) QualitySettings.SetQualityLevel(Mathf.Clamp(level, 0, QualitySettings.names.Length - 1), true);
#if UNITY_2022_2_OR_NEWER
            QualitySettings.globalTextureMipmapLimit = TextureQuality;
#else
            QualitySettings.masterTextureLimit = TextureQuality;
#endif
            QualitySettings.shadows = ShadowQuality == 0 ? UnityEngine.ShadowQuality.Disable : UnityEngine.ShadowQuality.All;
            QualitySettings.shadowResolution = (ShadowResolution)Mathf.Clamp(ShadowQuality, 0, 3);
            QualitySettings.shadowDistance = ShadowQuality >= 3 ? 90 : ShadowQuality == 2 ? 70 : 45;
            QualitySettings.antiAliasing = AntiAliasing == 2 ? 4 : 0;
            RenderPipelineBridge.Apply(this);
        }

        public void ApplyPreset(QualityPreset p)
        {
            Quality = p;
            switch (p)
            {
                case QualityPreset.Low: RenderScale = 0.8f; TextureQuality = 1; ShadowQuality = 1; EffectsQuality = 0; AntiAliasing = 1; AmbientOcclusion = false; Bloom = false; break;
                case QualityPreset.Medium: RenderScale = 1f; TextureQuality = 0; ShadowQuality = 1; EffectsQuality = 1; AntiAliasing = 1; AmbientOcclusion = false; Bloom = true; break;
                case QualityPreset.High: RenderScale = 1f; TextureQuality = 0; ShadowQuality = 2; EffectsQuality = 2; AntiAliasing = 2; AmbientOcclusion = true; Bloom = true; break;
                case QualityPreset.Ultra: RenderScale = 1.25f; TextureQuality = 0; ShadowQuality = 3; EffectsQuality = 3; AntiAliasing = 2; AmbientOcclusion = true; Bloom = true; break;
            }
        }
    }

    /// <summary>Rebindable actions and their default keys (Unity KeyCode names).</summary>
    public static class KeyBinds
    {
        public const string AbilityQ = "ability_q", AbilityW = "ability_w", AbilityE = "ability_e", AbilityR = "ability_r", AbilityD = "ability_d", AbilityF = "ability_f";
        public const string Item1 = "item_1", Item2 = "item_2", Item3 = "item_3", Item4 = "item_4", Item5 = "item_5", Item6 = "item_6";
        public const string AttackMove = "attack_move", Stop = "stop", Hold = "hold", Shop = "shop", Scoreboard = "scoreboard", CenterHero = "center_hero";
        public const string LevelUpModifier = "level_modifier", Ping = "ping_modifier", Chat = "chat", ChatAll = "chat_all", Courier = "courier";
        public const string Menu = "menu", Buyback = "buyback", SelectHero = "select_hero", CameraLock = "camera_lock";

        public static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            [AbilityQ] = "Q", [AbilityW] = "W", [AbilityE] = "E", [AbilityR] = "R", [AbilityD] = "D", [AbilityF] = "F",
            [Item1] = "Z", [Item2] = "X", [Item3] = "C", [Item4] = "V", [Item5] = "B", [Item6] = "N",
            [AttackMove] = "A", [Stop] = "S", [Hold] = "H", [Shop] = "P", [Scoreboard] = "Tab", [CenterHero] = "Space",
            [LevelUpModifier] = "LeftControl", [Ping] = "LeftAlt", [Chat] = "Return", [ChatAll] = "RightShift",
            [Menu] = "F10", [Buyback] = "F9", [SelectHero] = "F1", [CameraLock] = "Y",
        };

        public static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            [AbilityQ] = "Ability 1", [AbilityW] = "Ability 2", [AbilityE] = "Ability 3", [AbilityR] = "Ultimate", [AbilityD] = "Extra ability 1", [AbilityF] = "Extra ability 2",
            [Item1] = "Item slot 1", [Item2] = "Item slot 2", [Item3] = "Item slot 3", [Item4] = "Item slot 4", [Item5] = "Item slot 5", [Item6] = "Item slot 6",
            [AttackMove] = "Attack / attack-move", [Stop] = "Stop", [Hold] = "Hold position", [Shop] = "Open shop", [Scoreboard] = "Scoreboard (hold)", [CenterHero] = "Center camera on hero",
            [LevelUpModifier] = "Level-up modifier", [Ping] = "Ping modifier", [Chat] = "Team chat", [ChatAll] = "All chat modifier",
            [Menu] = "Game menu", [Buyback] = "Buyback", [SelectHero] = "Select hero", [CameraLock] = "Toggle camera lock",
        };

        public static KeyCode Get(ClientSettings s, string action)
        {
            if (s.KeyBindings.TryGetValue(action, out var name) && Enum.TryParse<KeyCode>(name, out var k)) return k;
            return Defaults.TryGetValue(action, out var d) && Enum.TryParse<KeyCode>(d, out var dk) ? dk : KeyCode.None;
        }
    }
}
