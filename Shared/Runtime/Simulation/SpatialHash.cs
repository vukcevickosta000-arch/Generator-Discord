using System;
using System.Collections.Generic;
using System.Numerics;

namespace Bloodfall.Simulation
{
    /// <summary>Uniform grid bucket index of units, rebuilt once per tick. O(1) inserts, cheap radius queries.</summary>
    public sealed class SpatialHash
    {
        private readonly float _cell;
        private readonly int _w, _h;
        private readonly List<Unit>[] _buckets;
        private readonly List<int> _usedBuckets = new List<int>(256);

        public SpatialHash(float worldWidth, float worldHeight, float cellSize = 4f)
        {
            _cell = cellSize;
            _w = Math.Max(1, (int)Math.Ceiling(worldWidth / cellSize));
            _h = Math.Max(1, (int)Math.Ceiling(worldHeight / cellSize));
            _buckets = new List<Unit>[_w * _h];
        }

        public void Clear()
        {
            foreach (var b in _usedBuckets) _buckets[b].Clear();
            _usedBuckets.Clear();
        }

        public void Insert(Unit u)
        {
            int x = Math.Max(0, Math.Min(_w - 1, (int)(u.Position.X / _cell)));
            int y = Math.Max(0, Math.Min(_h - 1, (int)(u.Position.Y / _cell)));
            int i = y * _w + x;
            var list = _buckets[i];
            if (list == null) { list = new List<Unit>(8); _buckets[i] = list; }
            if (list.Count == 0) _usedBuckets.Add(i);
            list.Add(u);
        }

        /// <summary>Appends all units whose centre lies within radius (+ their collision radius if includeRadius).</summary>
        public void Query(Vector2 center, float radius, List<Unit> results, bool includeRadius = true)
        {
            float pad = includeRadius ? 2f : 0f;
            int x0 = Math.Max(0, (int)((center.X - radius - pad) / _cell));
            int x1 = Math.Min(_w - 1, (int)((center.X + radius + pad) / _cell));
            int y0 = Math.Max(0, (int)((center.Y - radius - pad) / _cell));
            int y1 = Math.Min(_h - 1, (int)((center.Y + radius + pad) / _cell));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var list = _buckets[y * _w + x];
                    if (list == null) continue;
                    for (int k = 0; k < list.Count; k++)
                    {
                        var u = list[k];
                        float r = includeRadius ? radius + u.Radius : radius;
                        if (Vector2.DistanceSquared(u.Position, center) <= r * r) results.Add(u);
                    }
                }
            }
        }
    }
}
