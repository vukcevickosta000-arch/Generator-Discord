using System;
using System.IO;
using System.Linq;
using Bloodfall.Data;
using Xunit;

namespace Bloodfall.Tests
{
    public class DataTests
    {
        private static string RepoResources()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var p = Path.Combine(dir.FullName, "Shared", "Runtime", "Resources");
                if (Directory.Exists(Path.Combine(p, "GameData"))) return p;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Shared/Runtime/Resources not found");
        }

        [Fact]
        public void GameDataIndexIsCurrent()
        {
            var res = RepoResources();
            var root = Path.Combine(res, "GameData");
            var actual = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).OrderBy(s => s, StringComparer.Ordinal).ToList();
            var index = File.ReadAllLines(Path.Combine(res, "GameDataIndex.txt")).Where(l => l.Trim().Length > 0).ToList();
            Assert.True(actual.SequenceEqual(index), "GameDataIndex.txt is stale - run: python3 Tools/dev/gen_gamedata_index.py");
            var names = actual.Select(Path.GetFileNameWithoutExtension).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        }

        /// <summary>The Unity client sees files by bare name (Resources API) and rebuilds paths from the index;
        /// the resulting content hash must equal the server's or every online connection is rejected.</summary>
        [Fact]
        public void ClientStyleLoadMatchesServerHash()
        {
            var res = RepoResources();
            var root = Path.Combine(res, "GameData");
            var server = GameDataLoader.FromDirectory(root);
            var index = File.ReadAllLines(Path.Combine(res, "GameDataIndex.txt")).Where(l => l.Trim().Length > 0)
                .ToDictionary(l => Path.GetFileNameWithoutExtension(l.Trim()), l => l.Trim());
            // Unity hands TextAssets in arbitrary order with names only.
            var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Reverse()
                .Select(f => new GameDataFile(index[Path.GetFileNameWithoutExtension(f)], File.ReadAllText(f).Replace("\n", "\r\n")))
                .ToList();
            var client = GameData.Load(files);
            Assert.Equal(server.ContentHash, client.ContentHash);
            Assert.Empty(client.Errors);
        }

        [Fact]
        public void EveryModelKeyHasABlenderModel()
        {
            // Blender/scripts/build_models.py exports one FBX per model key; ModelFactory falls back to procedural
            // stand-ins, so a missing file is not fatal in game, but new content should come with its model.
            var d = TestUtil.Data;
            // RepoResources() is <repo>/Shared/Runtime/Resources.
            var models = Path.GetFullPath(Path.Combine(RepoResources(), "..", "..", "..", "Client", "Assets", "Resources", "Models"));
            var keys = d.Heroes.Values.Select(h => h.Model).Concat(d.Units.Values.Select(u => u.Model))
                .Concat(d.Statuses.Values.Select(s => s.ModelOverride)).Where(k => !string.IsNullOrEmpty(k)).Distinct();
            var missing = keys.Where(k => !File.Exists(Path.Combine(models, k + ".fbx"))).ToList();
            Assert.True(missing.Count == 0, "No FBX for: " + string.Join(", ", missing));
        }

        [Fact]
        public void AllModelKeysAndIconsAreDeclared()
        {
            var d = TestUtil.Data;
            foreach (var h in d.Heroes.Values) Assert.False(string.IsNullOrEmpty(h.Model), h.Id + " has no model key");
            foreach (var u in d.Units.Values) Assert.False(string.IsNullOrEmpty(u.Model), u.Id + " has no model key");
            foreach (var i in d.Items.Values.Where(i => i.Purchasable)) Assert.False(string.IsNullOrEmpty(i.Icon), i.Id + " has no icon");
        }
    }
}
