using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bloodfall.Client.Editor
{
    /// <summary>
    /// Import settings by folder so generated assets are always correct without manual inspector work:
    /// UI (no mips, crisp), cursors, splat maps (linear + readable), terrain layers (repeat), VFX, backdrop, icons,
    /// and FBX models from the Blender pipeline (generic rig, looping locomotion clips).
    /// </summary>
    public sealed class AssetImportRules : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            var ti = (TextureImporter)assetImporter;
            string p = assetPath.Replace('\\', '/');
            if (!p.Contains("/Resources/")) return;

            if (p.Contains("/Textures/UI/Cursors/"))
            {
                ti.textureType = TextureImporterType.Cursor;
                ti.isReadable = true;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            if (p.Contains("/Textures/UI/") || p.Contains("/Textures/Icons/"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaIsTransparency = true;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.maxTextureSize = 2048;
                return;
            }
            if (p.Contains("/Maps/") && Path.GetFileName(p).StartsWith("splat"))
            {
                // Splat weights are data, not colour: linear, readable (minimap bake), no compression artefacts.
                ti.sRGBTexture = false;
                ti.isReadable = true;
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.alphaIsTransparency = false;
                ti.maxTextureSize = 1024;
                return;
            }
            if (p.Contains("/Textures/Terrain/"))
            {
                ti.sRGBTexture = true;
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.alphaIsTransparency = false;   // alpha = height
                ti.anisoLevel = 8;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }
            if (p.Contains("/Textures/VFX/"))
            {
                ti.mipmapEnabled = true;
                ti.wrapMode = p.Contains("beam") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.alphaIsTransparency = true;
                return;
            }
            if (p.Contains("/Textures/Backdrop/"))
            {
                ti.mipmapEnabled = false;
                string n = Path.GetFileNameWithoutExtension(p);
                ti.wrapMode = n.StartsWith("clouds") || n.StartsWith("fog") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.alphaIsTransparency = true;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.maxTextureSize = 4096;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
            }
        }

        private void OnPreprocessModel()
        {
            string p = assetPath.Replace('\\', '/');
            if (!p.Contains("/Resources/Models/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            bool props = p.Contains("/Models/Props/") || p.Contains("/Models/Trees/");
            mi.animationType = props ? ModelImporterAnimationType.None : ModelImporterAnimationType.Generic;
            mi.importAnimation = !props;
        }

        private void OnPreprocessAnimation()
        {
            string p = assetPath.Replace('\\', '/');
            if (!p.Contains("/Resources/Models/")) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var c in clips)
            {
                string n = c.name.ToLowerInvariant();
                int bar = n.LastIndexOf('|');
                if (bar >= 0) n = n.Substring(bar + 1);
                c.loopTime = n == "idle" || n == "run" || n == "walk" || n == "channel" || n == "stun" || n == "fly";
            }
            mi.clipAnimations = clips;
        }

        /// <summary>Keeps Shared/Runtime/Resources/GameDataIndex.txt in sync when game data files are added or removed.</summary>
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool touched = imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(a => a.Replace('\\', '/').Contains("/Resources/GameData/") && a.EndsWith(".json"));
            if (!touched) return;
            var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.bloodfall.shared/package.json");
            if (pkg == null) return;
            string root = Path.Combine(pkg.resolvedPath, "Runtime", "Resources", "GameData");
            if (!Directory.Exists(root)) return;
            var paths = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
                .Select(f => f.Substring(root.Length + 1).Replace('\\', '/'))
                .OrderBy(s => s, System.StringComparer.Ordinal).ToList();
            string index = string.Join("\n", paths) + "\n";
            string target = Path.Combine(pkg.resolvedPath, "Runtime", "Resources", "GameDataIndex.txt");
            if (File.Exists(target) && File.ReadAllText(target).Replace("\r\n", "\n") == index) return;
            File.WriteAllText(target, index);
            Debug.Log("[Bloodfall] GameDataIndex.txt updated (" + paths.Count + " files).");
            AssetDatabase.Refresh();
        }
    }
}
