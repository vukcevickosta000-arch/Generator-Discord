using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloodfall.Client.Core;
using Bloodfall.Data;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Bloodfall.Client.Editor
{
    /// <summary>Editor menu: content validation, art coverage report, builds.</summary>
    public static class BloodfallTools
    {
        [MenuItem("Bloodfall/Validate Game Data", priority = 20)]
        public static void ValidateData()
        {
            GameData data;
            try { data = GameDataProvider.Load(); }
            catch (Exception e) { Debug.LogError("[Bloodfall] Game data failed to load: " + e.Message); return; }
            foreach (var e in data.Errors) Debug.LogError("[GameData] " + e);
            foreach (var w in data.Warnings) Debug.LogWarning("[GameData] " + w);
            Debug.Log($"[Bloodfall] Game data: {data.Heroes.Count} heroes, {data.Abilities.Count} abilities, {data.Items.Count} items, {data.Units.Count} units, " +
                      $"{data.Errors.Count} errors, {data.Warnings.Count} warnings. Content hash {data.ContentHash}");
            ArtCoverage(data);
        }

        /// <summary>Lists which referenced models / icons / portraits exist as authored assets (vs procedural stand-ins).</summary>
        public static void ArtCoverage(GameData data)
        {
            var missing = new List<string>();
            int models = 0, modelsOk = 0, icons = 0, iconsOk = 0;
            void Model(string key) { if (string.IsNullOrEmpty(key)) return; models++; if (Resources.Load<GameObject>("Models/" + key) != null) modelsOk++; else missing.Add("model  " + key); }
            void Icon(string path) { icons++; if (Resources.Load<Texture2D>(path) != null) iconsOk++; else missing.Add("icon   " + path); }
            foreach (var h in data.Heroes.Values) { Model(h.Model); Icon("Textures/Icons/Portraits/" + (h.Portrait ?? "portrait_" + h.Id.Replace("hero_", ""))); }
            foreach (var u in data.Units.Values) Model(u.Model);
            foreach (var a in data.Abilities.Values.Where(a => !a.Hidden && a.Icon != null)) Icon("Textures/Icons/Abilities/" + a.Icon);
            foreach (var i in data.Items.Values.Where(i => i.Purchasable)) Icon("Textures/Icons/Items/" + (i.Icon ?? i.Id));
            Debug.Log($"[Bloodfall] Art coverage: models {modelsOk}/{models} authored (others use procedural stand-ins), icons {iconsOk}/{icons}.\n" + string.Join("\n", missing.Take(200)));
        }

        [MenuItem("Bloodfall/Build/Windows Client (Development)", priority = 40)]
        public static void BuildWindowsDev() => BuildWindows(true);

        [MenuItem("Bloodfall/Build/Windows Client (Release)", priority = 41)]
        public static void BuildWindowsRelease() => BuildWindows(false);

        /// <summary>Command line: Unity -batchmode -quit -projectPath Client -executeMethod Bloodfall.Client.Editor.BloodfallTools.BuildWindowsCli</summary>
        public static void BuildWindowsCli()
        {
            bool dev = Environment.GetCommandLineArgs().Contains("-development");
            var report = BuildWindows(dev);
            EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static BuildReport BuildWindows(bool development)
        {
            ProjectSetup.Run(false);
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Builds", "Windows"));
            Directory.CreateDirectory(outDir);
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(outDir, "Bloodfall.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[Bloodfall] Windows build {report.summary.result}: {options.locationPathName} ({report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalErrors} errors)");
            return report;
        }

        [MenuItem("Bloodfall/Open Persistent Data Folder", priority = 60)]
        public static void OpenData() => EditorUtility.RevealInFinder(Application.persistentDataPath);

        [MenuItem("Bloodfall/Clear Saved Session and Settings", priority = 61)]
        public static void ClearPrefs()
        {
            if (!EditorUtility.DisplayDialog("Bloodfall", "Delete the saved login session and client settings (PlayerPrefs)?", "Delete", "Cancel")) return;
            PlayerPrefs.DeleteAll();
            Debug.Log("[Bloodfall] PlayerPrefs cleared.");
        }
    }
}
