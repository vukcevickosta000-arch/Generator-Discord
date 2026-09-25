using System;
using System.Collections.Generic;
using System.Numerics;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    /// <summary>
    /// Team fog of war on a 1 m grid. Vision is ray-cast from every friendly unit toward its vision circle's
    /// perimeter; trees and tall ruins block rays, and cells on a higher height level than the viewer are
    /// invisible (high-ground advantage). Flying vision ignores both. Invisible units additionally need true sight.
    ///
    /// The same class runs on the client (for rendering fog from allied units) and on the server (authoritative
    /// snapshot filtering so clients never receive hidden enemies - maphack protection).
    /// </summary>
    public sealed class VisionSystem
    {
        public const float CellSize = 1f;
        public readonly int Width, Height;
        /// <summary>Visible counts per team (0 Dawn, 1 Dusk). Non-zero = visible this update.</summary>
        public readonly byte[][] Visible;
        /// <summary>Ever-seen flags per team (for the explored/unexplored fog look).</summary>
        public readonly bool[][] Explored;
        /// <summary>True sight coverage per team.</summary>
        public readonly bool[][] TrueSight;
        private readonly byte[] _blockers;   // 1 = blocks ground vision
        private readonly byte[] _levels;     // height level per vision cell
        private readonly Match _match;
        private readonly NavGrid _grid;
        private static readonly Dictionary<int, List<(int dx, int dy)[]>> RayCache = new Dictionary<int, List<(int dx, int dy)[]>>();

        public VisionSystem(Match match) : this(match.Grid) { _match = match; }

        public VisionSystem(NavGrid grid)
        {
            _grid = grid;
            Width = (int)Math.Ceiling(grid.WorldWidth / CellSize);
            Height = (int)Math.Ceiling(grid.WorldHeight / CellSize);
            Visible = new[] { new byte[Width * Height], new byte[Width * Height] };
            Explored = new[] { new bool[Width * Height], new bool[Width * Height] };
            TrueSight = new[] { new bool[Width * Height], new bool[Width * Height] };
            _blockers = new byte[Width * Height];
            _levels = new byte[Width * Height];
            RebuildStatic();
        }

        /// <summary>Recomputes blockers from the nav grid (call after trees are destroyed).</summary>
        public void RebuildStatic()
        {
            int ratio = Math.Max(1, (int)Math.Round(CellSize / _grid.CellSize));
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int trees = 0, blockers = 0, maxLevel = 0, total = 0;
                    for (int sy = 0; sy < ratio; sy++)
                    {
                        for (int sx = 0; sx < ratio; sx++)
                        {
                            int gx = x * ratio + sx, gy = y * ratio + sy;
                            if (!_grid.InBounds(gx, gy)) continue;
                            total++;
                            if (_grid.IsTreeCell(gx, gy)) trees++;
                            if (_grid.IsVisionBlockerCell(gx, gy)) blockers++;
                            maxLevel = Math.Max(maxLevel, _grid.HeightLevelCell(gx, gy));
                        }
                    }
                    int i = y * Width + x;
                    _blockers[i] = (byte)((trees * 2 >= Math.Max(1, total) || blockers > 0) ? 1 : 0);
                    _levels[i] = (byte)maxLevel;
                }
            }
        }

        public int LevelAt(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? _levels[y * Width + x] : 0;

        public bool IsVisible(Team team, Vector2 p)
        {
            if (team != Team.Dawn && team != Team.Dusk) return true;
            int x = (int)(p.X / CellSize), y = (int)(p.Y / CellSize);
            if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
            return Visible[(int)team][y * Width + x] > 0;
        }

        public bool HasTrueSight(Team team, Vector2 p)
        {
            if (team != Team.Dawn && team != Team.Dusk) return false;
            int x = (int)(p.X / CellSize), y = (int)(p.Y / CellSize);
            if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
            return TrueSight[(int)team][y * Width + x];
        }

        /// <summary>Server-side full update from all units in the match.</summary>
        public void Update(bool force)
        {
            if (_match == null) return;
            Array.Clear(Visible[0], 0, Visible[0].Length);
            Array.Clear(Visible[1], 0, Visible[1].Length);
            Array.Clear(TrueSight[0], 0, TrueSight[0].Length);
            Array.Clear(TrueSight[1], 0, TrueSight[1].Length);
            bool night = _match.IsNight;
            foreach (var u in _match.Units)
            {
                if (!u.IsAlive || (u.Team != Team.Dawn && u.Team != Team.Dusk)) continue;
                float r = u.VisionOverride >= 0 ? u.VisionOverride : (night ? u.Stats.VisionNight : u.Stats.VisionDay);
                if (r <= 0) continue;
                bool flying = u.Flying || u.HasFlag(StatusFlags.FlyingVision);
                AddVision((int)u.Team, u.Position, r, flying);
                if (u.UnitDef != null && u.UnitDef.TrueSightRadius > 0) AddTrueSight((int)u.Team, u.Position, u.UnitDef.TrueSightRadius);
                if (u.HasFlag(StatusFlags.TrueSight)) AddTrueSight((int)u.Team, u.Position, Math.Min(r, 10f));
            }
            foreach (var z in _match.Zones)
                if (z.VisionTeam == Team.Dawn || z.VisionTeam == Team.Dusk) AddVision((int)z.VisionTeam, z.Center, z.VisionRadius, true);

            // Classify units.
            foreach (var u in _match.Units)
            {
                if (u.Removed) continue;
                for (int t = 0; t < 2; t++)
                {
                    if ((int)u.Team == t) { u.VisibleTo[t] = true; continue; }
                    bool vis = IsVisible((Team)t, u.Position);
                    if (vis && u.IsInvisible && !HasTrueSight((Team)t, u.Position)) vis = false;
                    if (u.HasFlag(StatusFlags.Revealed)) vis = true;
                    if (u.HasFlag(StatusFlags.Hidden)) vis = false;
                    u.VisibleTo[t] = vis;
                }
                u.VisibleTo[2] = true;
            }
        }

        /// <summary>Client-side update: compute fog only from the provided friendly vision sources.</summary>
        public void UpdateFromSources(int team, IEnumerable<(Vector2 pos, float radius, bool flying)> sources)
        {
            Array.Clear(Visible[team], 0, Visible[team].Length);
            foreach (var s in sources) AddVision(team, s.pos, s.radius, s.flying);
        }

        public void AddVision(int team, Vector2 pos, float radius, bool flying)
        {
            int cx = (int)(pos.X / CellSize), cy = (int)(pos.Y / CellSize);
            if (cx < 0 || cy < 0 || cx >= Width || cy >= Height) return;
            int r = Math.Max(1, (int)Math.Round(radius / CellSize));
            var vis = Visible[team];
            var exp = Explored[team];
            int viewerLevel = _levels[cy * Width + cx];
            int center = cy * Width + cx;
            if (vis[center] < 255) vis[center]++;
            exp[center] = true;

            if (flying)
            {
                int r2 = r * r;
                for (int dy = -r; dy <= r; dy++)
                {
                    int y = cy + dy;
                    if (y < 0 || y >= Height) continue;
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = cx + dx;
                        if (x < 0 || x >= Width || dx * dx + dy * dy > r2) continue;
                        int i = y * Width + x;
                        if (vis[i] < 255) vis[i]++;
                        exp[i] = true;
                    }
                }
                return;
            }

            foreach (var ray in GetRays(r))
            {
                for (int k = 0; k < ray.Length; k++)
                {
                    int x = cx + ray[k].dx, y = cy + ray[k].dy;
                    if (x < 0 || y < 0 || x >= Width || y >= Height) break;
                    int i = y * Width + x;
                    // Higher ground is not visible from below.
                    if (_levels[i] > viewerLevel) break;
                    if (vis[i] < 255) vis[i]++;
                    exp[i] = true;
                    if (_blockers[i] != 0) break;
                }
            }
        }

        private void AddTrueSight(int team, Vector2 pos, float radius)
        {
            int cx = (int)(pos.X / CellSize), cy = (int)(pos.Y / CellSize);
            int r = (int)Math.Ceiling(radius / CellSize);
            var ts = TrueSight[team];
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= Width || y >= Height || dx * dx + dy * dy > r * r) continue;
                    ts[y * Width + x] = true;
                }
        }

        /// <summary>Precomputed Bresenham rays from the origin to every perimeter cell of radius r.</summary>
        private static List<(int dx, int dy)[]> GetRays(int r)
        {
            lock (RayCache)
            {
                if (RayCache.TryGetValue(r, out var cached)) return cached;
                var rays = new List<(int, int)[]>();
                int steps = Math.Max(16, (int)(Math.PI * 2 * r * 1.5));
                var seenEnds = new HashSet<(int, int)>();
                for (int s = 0; s < steps; s++)
                {
                    double a = s * Math.PI * 2 / steps;
                    int ex = (int)Math.Round(Math.Cos(a) * r), ey = (int)Math.Round(Math.Sin(a) * r);
                    if (!seenEnds.Add((ex, ey))) continue;
                    rays.Add(Line(0, 0, ex, ey));
                }
                RayCache[r] = rays;
                return rays;
            }
        }

        private static (int, int)[] Line(int x0, int y0, int x1, int y1)
        {
            var pts = new List<(int, int)>();
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            int x = x0, y = y0;
            while (true)
            {
                if (x != x0 || y != y0) pts.Add((x, y));
                if (x == x1 && y == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
            return pts.ToArray();
        }
    }
}
