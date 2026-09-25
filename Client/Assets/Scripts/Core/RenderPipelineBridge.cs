using UnityEngine;
using UnityEngine.Rendering;
#if BLOODFALL_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Bloodfall.Client.Core
{
    /// <summary>
    /// Isolates Universal Render Pipeline specifics (render scale, MSAA, post-processing volumes) so the rest of the
    /// client does not depend on URP types.
    /// </summary>
    public static class RenderPipelineBridge
    {
        public static bool UsingUrp => GraphicsSettings.currentRenderPipeline != null;

        public static void Apply(ClientSettings s)
        {
#if BLOODFALL_URP
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) return;
            urp.renderScale = Mathf.Clamp(s.RenderScale, 0.5f, 2f);
            urp.msaaSampleCount = s.AntiAliasing == 2 ? 4 : 1;
            urp.shadowDistance = s.ShadowQuality >= 3 ? 90 : s.ShadowQuality == 2 ? 70 : s.ShadowQuality == 1 ? 45 : 0;
            urp.supportsHDR = true;
#endif
        }

        /// <summary>Configures a camera for post-processing (URP additional data) and anti-aliasing.</summary>
        public static void SetupCamera(Camera cam, ClientSettings s, bool postProcessing = true)
        {
            cam.allowHDR = true;
            cam.allowMSAA = s.AntiAliasing == 2;
#if BLOODFALL_URP
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = postProcessing;
            data.antialiasing = s.AntiAliasing == 1 ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.None;
            data.renderShadows = s.ShadowQuality > 0;
#endif
        }

        /// <summary>
        /// Creates (or updates) the global post-processing volume: ACES tonemapping, bloom for emissive magic,
        /// slight vignette and colour grading tuned for dark fantasy with readable gameplay.
        /// </summary>
        public static GameObject CreatePostProcessing(Transform parent, ClientSettings s, PostLook look)
        {
#if BLOODFALL_URP
            var go = new GameObject("PostProcessing");
            go.transform.SetParent(parent, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            vol.sharedProfile = profile;
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.active = s.Bloom;
            bloom.intensity.Override(look.BloomIntensity);
            bloom.threshold.Override(look.BloomThreshold);
            bloom.scatter.Override(0.72f);
            bloom.tint.Override(new Color(1f, 0.82f, 0.78f));
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(look.Vignette);
            vig.smoothness.Override(0.45f);
            vig.color.Override(new Color(0.05f, 0f, 0.01f));
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(look.Exposure + Mathf.Log(Mathf.Max(0.1f, s.Brightness), 2f));
            color.contrast.Override(look.Contrast);
            color.saturation.Override(look.Saturation);
            color.colorFilter.Override(look.Filter);
            var lgg = profile.Add<LiftGammaGain>(true);
            lgg.lift.Override(new Vector4(0.98f, 0.96f, 1.02f, -0.02f));
            lgg.gain.Override(new Vector4(1.04f, 0.98f, 0.95f, 0f));
            var grain = profile.Add<FilmGrain>(true);
            grain.intensity.Override(0.12f);
            grain.type.Override(FilmGrainLookup.Thin1);
            if (s.MotionBlur)
            {
                var mb = profile.Add<MotionBlur>(true);
                mb.intensity.Override(0.25f);
            }
            return go;
#else
            return new GameObject("PostProcessing (URP not active)");
#endif
        }
    }

    public struct PostLook
    {
        public float BloomIntensity, BloomThreshold, Vignette, Exposure, Contrast, Saturation;
        public Color Filter;

        public static PostLook Menu => new PostLook { BloomIntensity = 1.4f, BloomThreshold = 0.85f, Vignette = 0.42f, Exposure = 0.1f, Contrast = 18f, Saturation = -8f, Filter = new Color(1f, 0.93f, 0.92f) };
        public static PostLook Match => new PostLook { BloomIntensity = 0.8f, BloomThreshold = 1.05f, Vignette = 0.22f, Exposure = 0.25f, Contrast = 12f, Saturation = 0f, Filter = new Color(1f, 0.97f, 0.96f) };
    }
}
