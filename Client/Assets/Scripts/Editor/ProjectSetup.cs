using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
#if BLOODFALL_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Bloodfall.Client.Editor
{
    /// <summary>
    /// One-time project configuration so a fresh clone opens ready to play: URP asset + renderer (forward, HDR, depth
    /// texture for water, soft shadows), linear colour space, player settings, and the Boot scene in the build.
    /// Runs automatically when no render pipeline is assigned; also available from the Bloodfall menu.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        private const string SettingsFolder = "Assets/Settings";
        private const string PipelinePath = SettingsFolder + "/BloodfallURP.asset";
        private const string RendererPath = SettingsFolder + "/BloodfallRenderer.asset";
        private const string BootScene = "Assets/Scenes/Boot.unity";

        static ProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-bloodfallSetup") < 0) return;
                if (GraphicsSettings.defaultRenderPipeline == null || !File.Exists(BootScene)) Run(false);
            };
        }

        [MenuItem("Bloodfall/Setup Project", priority = 0)]
        public static void RunFromMenu() => Run(true);

        public static void Run(bool verbose)
        {
            Directory.CreateDirectory(SettingsFolder);
            Directory.CreateDirectory("Assets/Scenes");
            ConfigurePipeline();
            ConfigurePlayer();
            ConfigureBootScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Bloodfall] Project setup complete: URP pipeline, linear colour space, player settings, Boot scene.");
            if (verbose) EditorUtility.DisplayDialog("Bloodfall", "Project setup complete.\n\nPress Play in any scene to start the client.", "OK");
        }

        private static void ConfigurePipeline()
        {
#if BLOODFALL_URP
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            var rso = new SerializedObject(renderer);
            SetInt(rso, "m_RenderingMode", 0);        // Forward
            SetInt(rso, "m_DepthPrimingMode", 0);
            rso.ApplyModifiedPropertiesWithoutUndo();

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, PipelinePath);
            }
            asset.supportsHDR = true;
            asset.msaaSampleCount = 4;
            asset.renderScale = 1f;
            asset.shadowDistance = 70f;
            asset.shadowCascadeCount = 2;
            asset.supportsCameraDepthTexture = true;
            asset.supportsCameraOpaqueTexture = false;
            asset.maxAdditionalLightsCount = 8;
            var so = new SerializedObject(asset);
            SetInt(so, "m_AdditionalLightsRenderingMode", 1);   // per pixel
            SetInt(so, "m_AdditionalLightsPerObjectLimit", 8);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetInt(so, "m_MainLightShadowmapResolution", 4096);
            SetBool(so, "m_SupportsTerrainHoles", false);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
#else
            Debug.LogWarning("[Bloodfall] Universal Render Pipeline package not installed - install com.unity.render-pipelines.universal (see BUILD_INSTRUCTIONS.md).");
#endif
        }

        private static void SetInt(SerializedObject so, string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; }
        private static void SetBool(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; }

        private static void ConfigurePlayer()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "Bloodfall";
            PlayerSettings.productName = "Bloodfall";
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.gcIncremental = true;
#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Textures/UI/logo_bloodfall_small.png");
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
#endif
#if BLOODFALL_INPUT_SYSTEM
            // Keep both input backends active (InputBridge supports either).
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (ps.Length > 0)
            {
                var so = new SerializedObject(ps[0]);
                SetInt(so, "activeInputHandler", 2);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
#endif
        }

        private static void ConfigureBootScene()
        {
            if (!File.Exists(BootScene))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // The client builds itself at runtime (Bootstrap); the scene only needs to exist.
                EditorSceneManager.SaveScene(scene, BootScene);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScene, true) };
        }
    }
}
