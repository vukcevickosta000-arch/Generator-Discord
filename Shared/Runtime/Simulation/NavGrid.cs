using System;
using System.Collections.Generic;
using System.Numerics;
using Bloodfall.Core;

namespace Bloodfall.Simulation
{
    /// <summary>
    /// Walkability / height / tree grid shared by pathfinding, vision and the client renderer.
    /// Cell byte layout:
    ///   bit 0      walkable (for unit centres; obstacles are pre-inflated by the map generator)
    ///   bits 1..2  height level (0 = river/low, 1 = lanes/jungle, 2 = high ground, 3 = cliffs/peaks)
    ///   bit 3      tree (blocks vision + movement until destroyed)
    ///   bit 4      structure footprint
    ///   bit 5      vision blocker (tall ruins, cliffs)
    ///   bit 6      water (walkable shallow river)
    ///   bit 7      no-build / special (ramps)
    /// </summary>
    public sealed class NavGrid
    {
        public const byte Walkable = 1;
        public const byte TreeBit = 8;
        public const byte StructureBit = 16;
        public const byte VisionBlockBit = 32;
        public const byte WaterBit = 64;
        public const byte SpecialBit = 128;

        public readonly int Width, Height;
        public readonly float CellSize;
        public readonly byte[] Cells;
        /// <summary>Counts of dynamic blockers per cell (temporary walls, trees destroyed are tracked separately).</summary>
        private readonly byte[] _dynamic;
        private readonly bool[] _treeDestroyed;

        public NavGrid(int width, int height, float cellSize, byte[] cells)
        {
            Width = width; Height = height; CellSize = cellSize;
            Cells = cells ?? new byte[width * height];
            if (Cells.Length != width * height) throw new ArgumentException("Cell data size mismatch");
            _dynamic = new byte[width * height];
            _treeDestroyed = new bool[width * height];
            _open = new BinaryHeap(4096);
            _g = new float[width * height];
            _parent = new int[width * height];
            _stamp = new int[width * height];
            _closedStamp = new int[width * height];
        }

        public static NavGrid CreateOpen(int width, int height, float cellSize, int level = 1)
        {
            var cells = new byte[width * height];
            for (int i = 0; i < cells.Length; i++) cells[i] = (byte)(Walkable | (level << 1));
            return new NavGrid(width, height, cellSize, cells);
        }

        // ---------------------------------------------------------------- encoding

        /// <summary>Decodes base64( repeated [ushort runLength LE][byte value] ).</summary>
        public static byte[] DecodeRle(string base64, int expectedLength)
        {
            var raw = Convert.FromBase64String(base64);
            var result = new byte[expectedLength];
            int o = 0;
            for (int i = 0; i + 2 < raw.Length && o < expectedLength; i += 3)
            {
                int run = raw[i] | (raw[i + 1] << 8);
                byte v = raw[i + 2];
                for (int k = 0; k < run && o < expectedLength; k++) result[o++] = v;
            }
            if (o != expectedLength) throw new FormatException($"Grid RLE produced {o} cells, expected {expectedLength}");
            return result;
        }

        public static string EncodeRle(byte[] cells)
        {
            var output = new List<byte>(cells.Length / 8);
            int i = 0;
            while (i < cells.Length)
            {
                byte v = cells[i];
                int run = 1;
                while (i + run < cells.Length && cells[i + run] == v && run < 65535) run++;
                output.Add((byte)(run & 0xFF)); output.Add((byte)(run >> 8)); output.Add(v);
                i += run;
            }
            return Convert.ToBase64String(output.ToArray());
        }

        // ---------------------------------------------------------------- queries

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public int Index(int x, int y) => y * Width + x;
        public void ToCell(Vector2 p, out int x, out int y) { x = (int)Math.Floor(p.X / CellSize); y = (int)Math.Floor(p.Y / CellSize); }
        public Vector2 CellCenter(int x, int y) => new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);
        public float WorldWidth => Width * CellSize;
        public float WorldHeight => Height * CellSize;

        public bool IsWalkableCell(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            int i = y * Width + x;
            byte c = Cells[i];
            if (_dynamic[i] > 0) return false;
            if ((c & TreeBit) != 0 && !_treeDestroyed[i]) return false;
            return (c & Walkable) != 0 || ((c & TreeBit) != 0 && _treeDestroyed[i]);
        }

        public bool IsWalkable(Vector2 p) { ToCell(p, out int x, out int y); return IsWalkableCell(x, y); }

        public int HeightLevelCell(int x, int y) => InBounds(x, y) ? (Cells[y * Width + x] >> 1) & 3 : 0;
        public int HeightLevel(Vector2 p) { ToCell(p, out int x, out int y); return HeightLevelCell(x, y); }

        public bool IsTreeCell(int x, int y) => InBounds(x, y) && (Cells[y * Width + x] & TreeBit) != 0 && !_treeDestroyed[y * Width + x];
        public bool IsVisionBlockerCell(int x, int y) => InBounds(x, y) && (Cells[y * Width + x] & VisionBlockBit) != 0;
        public bool IsWaterCell(int x, int y) => InBounds(x, y) && (Cells[y * Width + x] & WaterBit) != 0;

        public void SetTreeDestroyed(int x, int y, bool destroyed) { if (InBounds(x, y)) _treeDestroyed[y * Width + x] = destroyed; Version++; }
        public void AddDynamicBlock(int x, int y) { if (InBounds(x, y) && _dynamic[y * Width + x] < 255) { _dynamic[y * Width + x]++; Version++; } }
        public void RemoveDynamicBlock(int x, int y) { if (InBounds(x, y) && _dynamic[y * Width + x] > 0) { _dynamic[y * Width + x]--; Version++; } }

        /// <summary>Incremented whenever dynamic walkability changes (used to invalidate cached paths).</summary>
        public int Version { get; private set; }

        /// <summary>Finds the closest walkable point to p (spiral search up to maxRadius cells).</summary>
        public Vector2 NearestWalkable(Vector2 p, int maxRadiusCells = 24)
        {
            ToCell(p, out int cx, out int cy);
            if (IsWalkableCell(cx, cy)) return p;
            float best = float.MaxValue;
            Vector2 bestP = p;
            for (int r = 1; r <= maxRadiusCells; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        int x = cx + dx, y = cy + dy;
                        if (!IsWalkableCell(x, y)) continue;
                        var c = CellCenter(x, y);
                        float d = Vector2.DistanceSquared(c, p);
                        if (d < best) { best = d; bestP = c; }
                    }
                }
                if (best < float.MaxValue) return bestP;
            }
            return p;
        }

        /// <summary>True if a straight walk from a to b stays on walkable cells (supercover traversal).</summary>
        public bool LineWalkable(Vector2 a, Vector2 b)
        {
            ToCell(a, out int x0, out int y0);
            ToCell(b, out int x1, out int y1);
            float fx = a.X / CellSize, fy = a.Y / CellSize;
            float dx = b.X / CellSize - fx, dy = b.Y / CellSize - fy;
            int stepX = Math.Sign(dx), stepY = Math.Sign(dy);
            float tDeltaX = stepX != 0 ? Math.Abs(1f / dx) : float.MaxValue;
            float tDeltaY = stepY != 0 ? Math.Abs(1f / dy) : float.MaxValue;
            float tMaxX = stepX > 0 ? (x0 + 1 - fx) * tDeltaX : stepX < 0 ? (fx - x0) * tDeltaX : float.MaxValue;
            float tMaxY = stepY > 0 ? (y0 + 1 - fy) * tDeltaY : stepY < 0 ? (fy - y0) * tDeltaY : float.MaxValue;
            int x = x0, y = y0;
            int guard = Width + Height + 4;
            while (guard-- > 0)
            {
                if (!IsWalkableCell(x, y)) return false;
                if (x == x1 && y == y1) return true;
                if (Math.Abs(tMaxX - tMaxY) < 1e-6f)
                {
                    // Passing exactly through a corner: both neighbours must be walkable (no corner cutting).
                    if (!IsWalkableCell(x + stepX, y) || !IsWalkableCell(x, y + stepY)) return false;
                    tMaxX += tDeltaX; x += stepX; tMaxY += tDeltaY; y += stepY;
                }
                else if (tMaxX < tMaxY) { tMaxX += tDeltaX; x += stepX; }
                else { tMaxY += tDeltaY; y += stepY; }
            }
            return false;
        }

        /// <summary>Returns the furthest walkable point along a->b before hitting an obstacle.</summary>
        public Vector2 ClampLine(Vector2 a, Vector2 b)
        {
            float len = Vector2.Distance(a, b);
            if (len < 1e-4f) return a;
            var dir = (b - a) / len;
            float step = CellSize * 0.4f;
            Vector2 last = a;
            for (float t = step; t < len + step; t += step)
            {
                var p = a + dir * Math.Min(t, len);
                if (!IsWalkable(p)) return last;
                last = p;
            }
            return b;
        }

        // ---------------------------------------------------------------- A*

        private readonly BinaryHeap _open;
        private readonly float[] _g;
        private readonly int[] _parent;
        private readonly int[] _stamp;
        private readonly int[] _closedStamp;
        private int _searchId;
        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private const float Sqrt2 = 1.41421356f;

        /// <summary>Statistics for profiling.</summary>
        public int LastExpandedNodes { get; private set; }

        /// <summary>
        /// Finds a path from start to goal. If the goal is unreachable the path leads to the reachable cell
        /// closest to the goal (so right-clicking into a forest walks you to its edge, like classic MOBAs).
        /// Returned waypoints exclude the start and are smoothed by line-of-sight.
        /// </summary>
        public bool FindPath(Vector2 start, Vector2 goal, List<Vector2> output, int maxNodes = 40000)
        {
            output.Clear();
            start = NearestWalkable(start, 6);
            ToCell(start, out int sx, out int sy);
            Vector2 goalW = IsWalkable(goal) ? goal : NearestWalkable(goal, 12);
            ToCell(goalW, out int gx, out int gy);
            if (!InBounds(sx, sy)) return false;
            if (!InBounds(gx, gy)) { gx = MathUtil.Clamp(gx, 0, Width - 1); gy = MathUtil.Clamp(gy, 0, Height - 1); }

            if (LineWalkable(start, goalW)) { output.Add(goalW); return true; }

            _searchId++;
            if (_searchId == int.MaxValue) { Array.Clear(_stamp, 0, _stamp.Length); Array.Clear(_closedStamp, 0, _closedStamp.Length); _searchId = 1; }
            _open.Clear();
            int startIdx = Index(sx, sy), goalIdx = Index(gx, gy);
            _g[startIdx] = 0; _parent[startIdx] = -1; _stamp[startIdx] = _searchId;
            _open.Push(startIdx, Heuristic(sx, sy, gx, gy));
            int bestIdx = startIdx; float bestH = Heuristic(sx, sy, gx, gy);
            int expanded = 0;
            bool found = false;

            while (_open.Count > 0)
            {
                int cur = _open.Pop();
                if (_closedStamp[cur] == _searchId) continue;
                _closedStamp[cur] = _searchId;
                if (cur == goalIdx) { found = true; bestIdx = cur; break; }
                if (++expanded > maxNodes) break;
                int cx = cur % Width, cy = cur / Width;
                float h = Heuristic(cx, cy, gx, gy);
                if (h < bestH) { bestH = h; bestIdx = cur; }
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Dx[d], ny = cy + Dy[d];
                    if (!IsWalkableCell(nx, ny)) continue;
                    if (d >= 4 && (!IsWalkableCell(cx + Dx[d], cy) || !IsWalkableCell(cx, cy + Dy[d]))) continue; // no corner cutting
                    int ni = ny * Width + nx;
                    if (_closedStamp[ni] == _searchId) continue;
                    float ng = _g[cur] + (d >= 4 ? Sqrt2 : 1f);
                    if (_stamp[ni] != _searchId || ng < _g[ni])
                    {
                        _stamp[ni] = _searchId;
                        _g[ni] = ng;
                        _parent[ni] = cur;
                        _open.Push(ni, ng + Heuristic(nx, ny, gx, gy));
                    }
                }
            }
            LastExpandedNodes = expanded;

            // Reconstruct.
            var raw = new List<int>(64);
            for (int i = bestIdx; i != -1; i = _parent[i]) { raw.Add(i); if (raw.Count > 20000) break; }
            raw.Reverse();
            // String pulling (LOS smoothing).
            Vector2 anchor = start;
            int k = 1;
            while (k < raw.Count)
            {
                int far = k;
                for (int j = raw.Count - 1; j > k; j--)
                {
                    if (LineWalkable(anchor, CellCenter(raw[j] % Width, raw[j] / Width))) { far = j; break; }
                }
                var p = CellCenter(raw[far] % Width, raw[far] / Width);
                output.Add(p);
                anchor = p;
                k = far + 1;
            }
            if (found)
            {
                if (output.Count > 0) output[output.Count - 1] = goalW; else output.Add(goalW);
            }
            return found;
        }

        private static float Heuristic(int x, int y, int gx, int gy)
        {
            int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
            return (dx + dy) + (Sqrt2 - 2f) * Math.Min(dx, dy);
        }

        private sealed class BinaryHeap
        {
            private int[] _items;
            private float[] _keys;
            public int Count { get; private set; }
            public BinaryHeap(int capacity) { _items = new int[capacity]; _keys = new float[capacity]; }
            public void Clear() => Count = 0;

            public void Push(int item, float key)
            {
                if (Count == _items.Length) { Array.Resize(ref _items, Count * 2); Array.Resize(ref _keys, Count * 2); }
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_keys[p] <= key) break;
                    _items[i] = _items[p]; _keys[i] = _keys[p]; i = p;
                }
                _items[i] = item; _keys[i] = key;
            }

            public int Pop()
            {
                int top = _items[0];
                int last = _items[--Count];
                float lastKey = _keys[Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1;
                    if (l >= Count) break;
                    int r = l + 1;
                    int c = r < Count && _keys[r] < _keys[l] ? r : l;
                    if (_keys[c] >= lastKey) break;
                    _items[i] = _items[c]; _keys[i] = _keys[c]; i = c;
                }
                if (Count > 0) { _items[i] = last; _keys[i] = lastKey; }
                return top;
            }
        }
    }
}
