using System;
using System.Collections.Generic;
using System.Numerics;

namespace Bloodfall.Simulation
{
    public sealed partial class Match
    {
        // Tree trunks from MapDef.Trees, bucketed on a coarse grid for radius queries (Barkskin, forest abilities).
        private const float TreeBucketSize = 8f;
        private List<Vector2>[] _treeBuckets;
        private int _treeBucketsX, _treeBucketsY;

        /// <summary>Number of standing trees whose trunk lies within <paramref name="radius"/> of <paramref name="p"/>.</summary>
        public int CountTreesNear(Vector2 p, float radius)
        {
            EnsureTreeIndex();
            if (_treeBuckets.Length == 0) return 0;
            int x0 = Math.Max(0, (int)((p.X - radius) / TreeBucketSize)), x1 = Math.Min(_treeBucketsX - 1, (int)((p.X + radius) / TreeBucketSize));
            int y0 = Math.Max(0, (int)((p.Y - radius) / TreeBucketSize)), y1 = Math.Min(_treeBucketsY - 1, (int)((p.Y + radius) / TreeBucketSize));
            float r2 = radius * radius;
            int count = 0;
            for (int by = y0; by <= y1; by++)
                for (int bx = x0; bx <= x1; bx++)
                {
                    var bucket = _treeBuckets[by * _treeBucketsX + bx];
                    if (bucket == null) continue;
                    foreach (var t in bucket)
                    {
                        if (Vector2.DistanceSquared(t, p) > r2) continue;
                        Grid.ToCell(t, out int cx, out int cy);
                        if (Grid.IsTreeCell(cx, cy)) count++;
                    }
                }
            return count;
        }

        private void EnsureTreeIndex()
        {
            if (_treeBuckets != null) return;
            _treeBucketsX = Math.Max(1, (int)Math.Ceiling(Grid.WorldWidth / TreeBucketSize));
            _treeBucketsY = Math.Max(1, (int)Math.Ceiling(Grid.WorldHeight / TreeBucketSize));
            _treeBuckets = new List<Vector2>[_treeBucketsX * _treeBucketsY];
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
                (bucket ??= new List<Vector2>()).Add(pos);
            }
        }
    }
}
