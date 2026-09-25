using System.Collections.Generic;
using UnityEngine;

namespace Bloodfall.Client.Match
{
    /// <summary>
    /// Builds low-poly procedural meshes (vertex coloured, flat or smooth shaded) for stand-in models, trees, props and
    /// indicators. Submesh 0 is lit geometry; additional submeshes can be registered for emissive parts.
    /// </summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<Vector2> UV = new List<Vector2>();
        private readonly List<List<int>> _submeshes = new List<List<int>> { new List<int>() };
        private int _sub;
        public Matrix4x4 Transform = Matrix4x4.identity;
        /// <summary>Vertex colour alpha (used as wind weight by the foliage shader).</summary>
        public float Weight = 1f;

        public int SubmeshCount => _submeshes.Count;

        /// <summary>Selects (creating if needed) the submesh subsequent geometry goes into.</summary>
        public MeshBuilder Sub(int index)
        {
            while (_submeshes.Count <= index) _submeshes.Add(new List<int>());
            _sub = index;
            return this;
        }

        private int Vert(Vector3 p, Vector3 n, Color c, Vector2 uv)
        {
            V.Add(Transform.MultiplyPoint3x4(p));
            N.Add(Transform.MultiplyVector(n).normalized);
            c.a = Weight;
            C.Add(c);
            UV.Add(uv);
            return V.Count - 1;
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            var t = _submeshes[_sub];
            t.Add(Vert(a, n, col, Vector2.zero));
            t.Add(Vert(b, n, col, Vector2.right));
            t.Add(Vert(c, n, col, Vector2.up));
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            Tri(a, b, c, col);
            Tri(a, c, d, col);
        }

        /// <summary>Axis-aligned box (in local space of the current transform) centred at <paramref name="center"/>.</summary>
        public void Box(Vector3 center, Vector3 size, Color col, float topInset = 0f)
        {
            var h = size * 0.5f;
            float ti = topInset;
            Vector3 p000 = center + new Vector3(-h.x, -h.y, -h.z), p100 = center + new Vector3(h.x, -h.y, -h.z);
            Vector3 p010 = center + new Vector3(-h.x + ti, h.y, -h.z + ti), p110 = center + new Vector3(h.x - ti, h.y, -h.z + ti);
            Vector3 p001 = center + new Vector3(-h.x, -h.y, h.z), p101 = center + new Vector3(h.x, -h.y, h.z);
            Vector3 p011 = center + new Vector3(-h.x + ti, h.y, h.z - ti), p111 = center + new Vector3(h.x - ti, h.y, h.z - ti);
            Quad(p000, p010, p110, p100, col);      // front (-z)
            Quad(p101, p111, p011, p001, col);      // back
            Quad(p001, p011, p010, p000, col);      // left
            Quad(p100, p110, p111, p101, col);      // right
            Quad(p010, p011, p111, p110, col * 1.08f); // top
            Quad(p000, p100, p101, p001, col * 0.7f);  // bottom
        }

        /// <summary>Tapered cylinder / cone between two points (smooth sides).</summary>
        public void Cylinder(Vector3 from, Vector3 to, float r0, float r1, int segments, Color col, bool capTop = true, bool capBottom = false, Color? colTop = null)
        {
            var axis = to - from;
            if (axis.sqrMagnitude < 1e-8f) return;
            var dir = axis.normalized;
            var side = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var fwd = Vector3.Cross(dir, side);
            var t = _submeshes[_sub];
            var ct = colTop ?? col;
            float slope = (r0 - r1) / axis.magnitude;
            int start = V.Count;
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var radial = side * Mathf.Cos(a) + fwd * Mathf.Sin(a);
                var n = (radial + dir * slope).normalized;
                Vert(from + radial * r0, n, col, new Vector2(i / (float)segments, 0));
                Vert(to + radial * r1, n, ct, new Vector2(i / (float)segments, 1));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = start + i * 2, b = a + 1, c = a + 2, d = a + 3;
                t.Add(a); t.Add(c); t.Add(b);
                t.Add(c); t.Add(d); t.Add(b);
            }
            if (capTop && r1 > 0.001f) Disc(to, dir, r1, segments, ct);
            if (capBottom && r0 > 0.001f) Disc(from, -dir, r0, segments, col);
        }

        public void Disc(Vector3 center, Vector3 normal, float r, int segments, Color col)
        {
            var side = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var fwd = Vector3.Cross(normal, side);
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var p0 = center + (side * Mathf.Cos(a0) + fwd * Mathf.Sin(a0)) * r;
                var p1 = center + (side * Mathf.Cos(a1) + fwd * Mathf.Sin(a1)) * r;
                Tri(center, p0, p1, col);
            }
        }

        /// <summary>Low-poly UV sphere / ellipsoid.</summary>
        public void Sphere(Vector3 center, Vector3 radii, int lon, int lat, Color col, Color? colTop = null)
        {
            var t = _submeshes[_sub];
            int start = V.Count;
            var top = colTop ?? col;
            for (int y = 0; y <= lat; y++)
            {
                float v = y / (float)lat;
                float phi = v * Mathf.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float u = x / (float)lon;
                    float th = u * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    var p = center + Vector3.Scale(n, radii);
                    Vert(p, Vector3.Scale(n, new Vector3(1 / radii.x, 1 / radii.y, 1 / radii.z)).normalized, Color.Lerp(top, col, v), new Vector2(u, v));
                }
            }
            for (int y = 0; y < lat; y++)
            for (int x = 0; x < lon; x++)
            {
                int a = start + y * (lon + 1) + x, b = a + 1, c = a + lon + 1, d = c + 1;
                t.Add(a); t.Add(b); t.Add(c);
                t.Add(b); t.Add(d); t.Add(c);
            }
        }

        /// <summary>Faceted crystal (elongated octahedron with n sides).</summary>
        public void Crystal(Vector3 baseCenter, float radius, float height, int sides, Color col, float midFrac = 0.35f)
        {
            var mid = baseCenter + Vector3.up * height * midFrac;
            var tip = baseCenter + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = mid + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                var p1 = mid + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                Tri(tip, p1, p0, col);
                Tri(baseCenter, p0, p1, col * 0.8f);
            }
        }

        /// <summary>Gable roof (prism) over a rectangle.</summary>
        public void Roof(Vector3 center, float width, float depth, float height, Color col)
        {
            float hw = width / 2, hd = depth / 2;
            Vector3 a = center + new Vector3(-hw, 0, -hd), b = center + new Vector3(hw, 0, -hd);
            Vector3 c = center + new Vector3(hw, 0, hd), d = center + new Vector3(-hw, 0, hd);
            Vector3 r0 = center + new Vector3(-hw, height, 0), r1 = center + new Vector3(hw, height, 0);
            Quad(a, r0, r1, b, col);
            Quad(c, r1, r0, d, col * 0.9f);
            Tri(a, d, r0, col * 0.8f);
            Tri(b, r1, c, col * 0.8f);
        }

        /// <summary>Flat quad lying on the XZ plane (for decals / shadows).</summary>
        public void Ground(Vector3 center, float size, Color col)
        {
            float h = size / 2;
            var t = _submeshes[_sub];
            int s = V.Count;
            Vert(center + new Vector3(-h, 0, -h), Vector3.up, col, new Vector2(0, 0));
            Vert(center + new Vector3(-h, 0, h), Vector3.up, col, new Vector2(0, 1));
            Vert(center + new Vector3(h, 0, h), Vector3.up, col, new Vector2(1, 1));
            Vert(center + new Vector3(h, 0, -h), Vector3.up, col, new Vector2(1, 0));
            t.Add(s); t.Add(s + 1); t.Add(s + 2);
            t.Add(s); t.Add(s + 2); t.Add(s + 3);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V);
            m.SetNormals(N);
            m.SetColors(C);
            m.SetUVs(0, UV);
            m.subMeshCount = _submeshes.Count;
            for (int i = 0; i < _submeshes.Count; i++) m.SetTriangles(_submeshes[i], i);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        /// <summary>Helper to push a TRS onto the builder transform and restore it after the action.</summary>
        public void With(Vector3 pos, Quaternion rot, Vector3 scale, System.Action body)
        {
            var saved = Transform;
            Transform = saved * Matrix4x4.TRS(pos, rot, scale);
            body();
            Transform = saved;
        }
    }
}
