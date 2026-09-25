using System;
using System.Collections.Generic;
using System.Numerics;

namespace Bloodfall.Simulation
{
    public sealed partial class Match
    {
        // Tree trunks from MapDef.Trees, bucketed on a coarse grid for radius queries (Barkskin, forest abilities,
        // RTS lumber harvesting).
        private const float TreeBucketSize = 8f;
        private List<int>[] _treeBuckets;
        private int _treeBucketsX, _treeBucketsY;
        private readonly List<Vector2> _treePos = new List<Vector2>();
        /// <summary>Lumber left in each tree (RTS), lazily filled with RulesDef.RtsTreeLumber.</summary>
        private int[] _treeLumber;

        public int TreeCount { get { EnsureTreeIndex(); return _treePos.Count; } }
        public Vector2 TreePosition(int index) { EnsureTreeIndex(); return _treePos[index]; }

        public bool TreeStanding(int index)
        {
            EnsureTreeIndex();
            if (index < 0 || index >= _treePos.Count) return false;
            Grid.ToCell(_treePos[index], out int cx, out int cy);
            return Grid.IsTreeCell(cx, cy);
        }

        public int TreeLumber(int index)
        {
            EnsureTreeIndex();
            if (!TreeStanding(index)) return 0;
            if (_treeLumber == null)
            {
                _treeLumber = new int[_treePos.Count];
                for (int i = 0; i < _treeLumber.Length; i++) _treeLumber[i] = Rules.RtsTreeLumber;
            }
            return _treeLumber[index];
        }

        /// <summary>Takes up to <paramref name="amount"/> lumber from a tree; the tree falls when it runs out.</summary>
        public int TakeLumber(int index, int amount)
        {
            int left = TreeLumber(index);
            int take = Math.Min(left, Math.Max(0, amount));
            if (take <= 0) return 0;
            _treeLumber[index] = left - take;
            if (_treeLumber[index] <= 0) DestroyTree(index);
            return take;
        }

        /// <summary>Fells a tree: clears its nav-grid cells and tells clients (they clear the same cells).</summary>
        public void DestroyTree(int index)
        {
            if (!TreeStanding(index)) return;
            var p = _treePos[index];
            Grid.ClearTreeAt(p);
            Vision.RebuildStaticAround(p, 2f);
            Emit(new SimEvent { Type = SimEventType.TreeDestroyed, Point = p, Value = index, PlayerId = -1 });
        }

        /// <summary>Number of standing trees whose trunk lies within <paramref name="radius"/> of <paramref name="p"/>.</summary>
        public int CountTreesNear(Vector2 p, float radius)
        {
            int count = 0;
            ForEachTreeNear(p, radius, i => count++);
            return count;
        }

        /// <summary>Visits every standing tree within radius of p (in index order within each bucket).</summary>
        public void ForEachTreeNear(Vector2 p, float radius, Action<int> visit)
        {
            EnsureTreeIndex();
            if (_treePos.Count == 0) return;
            int x0 = Math.Max(0, (int)((p.X - radius) / TreeBucketSize)), x1 = Math.Min(_treeBucketsX - 1, (int)((p.X + radius) / TreeBucketSize));
            int y0 = Math.Max(0, (int)((p.Y - radius) / TreeBucketSize)), y1 = Math.Min(_treeBucketsY - 1, (int)((p.Y + radius) / TreeBucketSize));
            float r2 = radius * radius;
            for (int by = y0; by <= y1; by++)
                for (int bx = x0; bx <= x1; bx++)
                {
                    var bucket = _treeBuckets[by * _treeBucketsX + bx];
                    if (bucket == null) continue;
                    foreach (var i in bucket)
                    {
                        if (Vector2.DistanceSquared(_treePos[i], p) > r2) continue;
                        if (TreeStanding(i)) visit(i);
                    }
                }
        }

        private void EnsureTreeIndex()
        {
            if (_treeBuckets != null) return;
            _treeBucketsX = Math.Max(1, (int)Math.Ceiling(Grid.WorldWidth / TreeBucketSize));
            _treeBucketsY = Math.Max(1, (int)Math.Ceiling(Grid.WorldHeight / TreeBucketSize));
            _treeBuckets = new List<int>[_treeBucketsX * _treeBucketsY];
            if (string.IsNullOrEmpty(Map.Trees)) return;
            byte[] raw;
            try { raw = Convert.FromBase64String(Map.Trees); }
            catch (FormatException) { LogLine("Map trees are not valid base64"); return; }
            // Records of 5 bytes: ushort x*10, ushort y*10 (little endian), byte variant.
            for (int i = 0; i + 5 <= raw.Length; i += 5)
            {
                var pos = new Vector2((raw[i] | raw[i + 1] << 8) / 10f, (raw[i + 2] | raw[i + 3] << 8) / 10f);
                int bx = Math.Min(_treeBucketsX - 1, (int)(pos.X / TreeBucketSize));
                int by = Math.Min(_treeBucketsY - 1, (int)(pos.Y / TreeBucketSize));
                ref var bucket = ref _treeBuckets[by * _treeBucketsX + bx];
                (bucket ??= new List<int>()).Add(_treePos.Count);
                _treePos.Add(pos);
            }
        }
    }
}
