using System;
using System.Collections.Generic;
using Bloodfall.Core;
using Bloodfall.Data;
using UnityEngine;

namespace Bloodfall.Client.Core
{
    /// <summary>Service endpoints and build info (Resources/Config/client_config.json, overridable from the command line).</summary>
    public sealed class ClientConfig
    {
        public string ClientVersion = "0.1.0";
        public string BackendUrl = "http://localhost:5080";
        public string Environment = "Development";
        public List<string> Regions = new List<string>();

        public bool IsDevelopment => Environment == "Development";

        public static ClientConfig Load()
        {
            var cfg = new ClientConfig();
            var asset = Resources.Load<TextAsset>("Config/client_config");
            if (asset != null)
            {
                try { cfg = JsonMapper.FromJson<ClientConfig>(asset.text); }
                catch (Exception e) { Debug.LogWarning("client_config.json invalid: " + e.Message); }
            }
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-bloodfall-backend") cfg.BackendUrl = args[i + 1];
            var env = System.Environment.GetEnvironmentVariable("BLOODFALL_BACKEND");
            if (!string.IsNullOrEmpty(env)) cfg.BackendUrl = env;
            cfg.BackendUrl = cfg.BackendUrl.TrimEnd('/');
            return cfg;
        }
    }

    /// <summary>Loads the shared gameplay data (identical bytes to the server's, verified by content hash).</summary>
    public static class GameDataProvider
    {
        public static GameData Load()
        {
            var files = new List<GameDataFile>();
            foreach (var t in Resources.LoadAll<TextAsset>("GameData"))
            {
                // Resources strips folders from names; the hash needs stable unique paths, so derive them from content ids.
                files.Add(new GameDataFile(ResourcePath(t), t.text));
            }
            if (files.Count == 0) throw new InvalidOperationException("No game data found in Resources/GameData.");
            return GameData.Load(files);
        }

        private static readonly Dictionary<string, string> KnownPaths = BuildIndex();

        private static Dictionary<string, string> BuildIndex()
        {
            var idx = Resources.Load<TextAsset>("GameDataIndex");
            var map = new Dictionary<string, string>();
            if (idx == null) return map;
            foreach (var line in idx.text.Split('\n'))
            {
                var l = line.Trim();
                if (l.Length == 0) continue;
                var name = System.IO.Path.GetFileNameWithoutExtension(l);
                map[name] = l;
            }
            return map;
        }

        private static string ResourcePath(TextAsset t) => KnownPaths.TryGetValue(t.name, out var p) ? p : t.name + ".json";
    }
}
