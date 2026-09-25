using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Bloodfall.Data
{
    /// <summary>Loads game data from a folder of JSON files (dedicated server, tools, tests).</summary>
    public static class GameDataLoader
    {
        public static GameData FromDirectory(string root)
        {
            var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
                .Select(f => new GameDataFile(Path.GetRelativePath(root, f), File.ReadAllText(f)))
                .ToList();
            return GameData.Load(files);
        }

        /// <summary>Walks up from 'start' to find Shared/Runtime/Resources/GameData (dev convenience).</summary>
        public static string FindDefaultRoot(string start)
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "Shared", "Runtime", "Resources", "GameData");
                if (Directory.Exists(candidate)) return candidate;
                var local = Path.Combine(dir.FullName, "GameData");
                if (Directory.Exists(local) && File.Exists(Path.Combine(local, "rules.json"))) return local;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
