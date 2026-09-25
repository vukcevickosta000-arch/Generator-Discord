using System;
using System.Collections.Generic;
using Bloodfall.Client.Combat;
using Bloodfall.Core;
using Bloodfall.Data;
using Bloodfall.Simulation;
using UnityEngine;

namespace Bloodfall.Client.Match
{
    public sealed class DressingFile
    {
        public List<DressingProp> Props = new List<DressingProp>();
        public List<DressingTree> Trees = new List<DressingTree>();
    }

    public sealed class DressingProp
    {
        public string Type;
        public float X, Y, Rot, Scale = 1f;
        public bool Lit;
    }

    public sealed class DressingTree
    {
        public float X, Y;
        public int V;
    }

    /// <summary>
    /// Renders the battlefield: terrain chunks from the generated heightfield (Tools/mapgen) with the 8-layer splat
    /// shader, river water, GPU-instanced destructible trees, dressing props with flame lights, ground decals, and a
    /// baked minimap image. World units: 1 unit = 1 metre, sim (x, y) → Unity (x, height, y).
    /// </summary>
    public sealed class MapRenderer : IDisposable
    {
        public readonly MapDef Map;
        public readonly NavGrid Grid;
        public GameObject Root { get; private set; }
        public Texture2D MinimapTexture { get; private set; }
        public float WaterLevel { get; private set; } = -0.9f;
        public float Width => Map.Width;
        public float Depth => Map.Height;

        private float[] _heights;
        private int _hw, _hh;
        private float _spacing = 0.5f;
        private readonly Dictionary<int, List<Matrix4x4>> _treeBatches = new Dictionary<int, List<Matrix4x4>>();
        private readonly Dictionary<long, (int variant, int index)> _treeLookup = new Dictionary<long, (int, int)>();
        private readonly Dictionary<int, Mesh> _treeMeshes = new Dictionary<int, Mesh>();
        private Material _treeMat;
        private readonly List<Light> _lights = new List<Light>();
        private Material _terrainMat;
        private DressingFile _dressing;

        public MapRenderer(MapDef map, NavGrid grid)
        {
            Map = map;
            Grid = grid;
        }

        // ------------------------------------------------------------------ loading (incremental)

        public IEnumerator<float> Load(Transform parent)
        {
            Root = new GameObject("Map_" + Map.Id);
            Root.transform.SetParent(parent, false);
            LoadHeights();
            yield return 0.05f;
            BuildTerrainMaterial();
            yield return 0.08f;
            int chunk = 64;
            int cx = Mathf.CeilToInt((_hw - 1) / (float)chunk), cy = Mathf.CeilToInt((_hh - 1) / (float)chunk);
            for (int y = 0; y < cy; y++)
            for (int x = 0; x < cx; x++)
            {
                BuildChunk(x * chunk, y * chunk, chunk);
                yield return 0.08f + 0.3f * (y * cx + x + 1) / (cx * cy);
            }
            BuildWater();
            yield return 0.42f;
            var dressingAsset = Resources.Load<TextAsset>(string.IsNullOrEmpty(Map.DressingFile) ? "Maps/Velmoragh/dressing" : Map.DressingFile);
            _dressing = dressingAsset != null ? JsonMapper.FromJson<DressingFile>(dressingAsset.text) : new DressingFile();
            BuildTrees();
            yield return 0.55f;
            int i = 0;
            foreach (var p in _dressing.Props)
            {
                BuildProp(p);
                if (++i % 20 == 0) yield return 0.55f + 0.2f * i / Math.Max(1, _dressing.Props.Count);
            }
            BakeMinimap();
            yield return 0.8f;
        }

        private void LoadHeights()
        {
            var asset = Resources.Load<TextAsset>(string.IsNullOrEmpty(Map.HeightFile) ? "Maps/Velmoragh/height" : Map.HeightFile);
            if (asset == null)
            {
                Debug.LogWarning("Heightfield missing; using a flat map.");
                _hw = (int)(Map.Width / _spacing) + 1;
                _hh = (int)(Map.Height / _spacing) + 1;
                _heights = new float[_hw * _hh];
                return;
            }
            var bytes = asset.bytes;
            _hw = BitConverter.ToInt32(bytes, 0);
            _hh = BitConverter.ToInt32(bytes, 4);
            float hmin = BitConverter.ToSingle(bytes, 8), hmax = BitConverter.ToSingle(bytes, 12);
            _spacing = Map.Width / (_hw - 1);
            _heights = new float[_hw * _hh];
            for (int i = 0; i < _heights.Length; i++)
            {
                ushort v = BitConverter.ToUInt16(bytes, 16 + i * 2);
                _heights[i] = hmin + (hmax - hmin) * (v / 65535f);
            }
            // Water level: just above the average river-bed height.
            double sum = 0; int n = 0;
            for (int y = 0; y < Grid.Height; y += 2)
            for (int x = 0; x < Grid.Width; x += 2)
                if (Grid.IsWaterCell(x, y)) { var c = Grid.CellCenter(x, y); sum += HeightAt(c.X, c.Y); n++; }
            if (n > 0) WaterLevel = (float)(sum / n) + 0.45f;
        }

        public float HeightAt(float x, float y)
        {
            if (_heights == null) return 0f;
            float fx = Mathf.Clamp(x / _spacing, 0, _hw - 1.001f), fy = Mathf.Clamp(y / _spacing, 0, _hh - 1.001f);
            int ix = (int)fx, iy = (int)fy;
            float tx = fx - ix, ty = fy - iy;
            float a = _heights[iy * _hw + ix], b = _heights[iy * _hw + ix + 1];
            float c = _heights[(iy + 1) * _hw + ix], d = _heights[(iy + 1) * _hw + ix + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        public Vector3 World(System.Numerics.Vector2 p, float extra = 0f) => new Vector3(p.X, HeightAt(p.X, p.Y) + extra, p.Y);
        public Vector3 World(float x, float y, float extra = 0f) => new Vector3(x, HeightAt(x, y) + extra, y);

        private Vector3 NormalAt(int ix, int iy)
        {
            float l = _heights[iy * _hw + Mathf.Max(0, ix - 1)], r = _heights[iy * _hw + Mathf.Min(_hw - 1, ix + 1)];
            float d = _heights[Mathf.Max(0, iy - 1) * _hw + ix], u = _heights[Mathf.Min(_hh - 1, iy + 1) * _hw + ix];
            return new Vector3(l - r, 2f * _spacing, d - u).normalized;
        }

        private void BuildTerrainMaterial()
        {
            var shader = Shader.Find("Bloodfall/Terrain");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _terrainMat = new Material(shader);
                _terrainMat.color = new Color(0.3f, 0.28f, 0.24f);
                return;
            }
            _terrainMat = new Material(shader) { name = "Terrain_" + Map.Id };
            _terrainMat.SetTexture("_Splat0", Resources.Load<Texture2D>("Maps/Velmoragh/splat0"));
            _terrainMat.SetTexture("_Splat1", Resources.Load<Texture2D>("Maps/Velmoragh/splat1"));
            string[] layers = { "grass", "dirt", "cobble", "rock", "blood_mud", "ash", "leaves", "paving" };
            for (int i = 0; i < layers.Length; i++)
            {
                var t = Resources.Load<Texture2D>("Textures/Terrain/" + layers[i]);
                if (t != null) { t.wrapMode = TextureWrapMode.Repeat; _terrainMat.SetTexture("_L" + i, t); }
            }
            _terrainMat.SetVector("_MapSize", new Vector4(Map.Width, Map.Height, 0, 0));
        }

        private void BuildChunk(int x0, int y0, int size)
        {
            int x1 = Mathf.Min(_hw - 1, x0 + size), y1 = Mathf.Min(_hh - 1, y0 + size);
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            var verts = new Vector3[w * h];
            var normals = new Vector3[w * h];
            var uvs = new Vector2[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int gx = x0 + x, gy = y0 + y;
                int i = y * w + x;
                verts[i] = new Vector3(gx * _spacing, _heights[gy * _hw + gx], gy * _spacing);
                normals[i] = NormalAt(gx, gy);
                uvs[i] = new Vector2(gx / (float)(_hw - 1), gy / (float)(_hh - 1));
            }
            var tris = new int[(w - 1) * (h - 1) * 6];
            int t = 0;
            for (int y = 0; y < h - 1; y++)
            for (int x = 0; x < w - 1; x++)
            {
                int a = y * w + x, b = a + 1, c = a + w, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
            var mesh = new Mesh { name = $"terrain_{x0}_{y0}" };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            var go = new GameObject(mesh.name);
            go.transform.SetParent(Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _terrainMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
        }

        private void BuildWater()
        {
            var shader = Shader.Find("Bloodfall/Water");
            if (shader == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Water";
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(Root.transform, false);
            go.transform.localPosition = new Vector3(Map.Width / 2, WaterLevel, Map.Height / 2);
            go.transform.localScale = new Vector3(Map.Width / 10f, 1, Map.Height / 10f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = new Material(shader) { name = "River" };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ trees

        private static long TreeKey(float x, float y) => ((long)Mathf.RoundToInt(x * 2) << 32) | (uint)Mathf.RoundToInt(y * 2);

        private void BuildTrees()
        {
            var shader = Shader.Find("Bloodfall/Foliage") ?? ModelFactory.LitShader;
            _treeMat = new Material(shader) { name = "Trees", enableInstancing = true };
            _treeMat.SetFloat("_Cutoff", 0f);
            var rnd = new System.Random(77);
            foreach (var t in _dressing.Trees)
            {
                int v = Mathf.Clamp(t.V, 0, 6);
                if (!_treeBatches.TryGetValue(v, out var list)) { list = new List<Matrix4x4>(); _treeBatches[v] = list; }
                float yaw = (float)rnd.NextDouble() * 360f;
                float s = 0.85f + (float)rnd.NextDouble() * 0.35f;
                var pos = World(t.X, t.Y, -0.1f);
                list.Add(Matrix4x4.TRS(pos, Quaternion.Euler(0, yaw, 0), Vector3.one * s));
                _treeLookup[TreeKey(t.X, t.Y)] = (v, list.Count - 1);
            }
            foreach (var v in _treeBatches.Keys) _treeMeshes[v] = ProceduralModels.TreeMesh(v);
        }

        /// <summary>Removes the tree nearest to a point (destroyed trees; also updates vision blockers).</summary>
        public bool DestroyTree(System.Numerics.Vector2 p)
        {
            if (!_treeLookup.TryGetValue(TreeKey(p.X, p.Y), out var entry))
            {
                // Fall back to the nearest tree within 1.5 m.
                float best = 2.25f; long bestKey = 0; bool found = false;
                foreach (var kv in _treeLookup)
                {
                    var m = _treeBatches[kv.Value.variant][kv.Value.index];
                    var pos = m.GetColumn(3);
                    float d = (pos.x - p.X) * (pos.x - p.X) + (pos.z - p.Y) * (pos.z - p.Y);
                    if (d < best) { best = d; bestKey = kv.Key; found = true; }
                }
                if (!found) return false;
                entry = _treeLookup[bestKey];
                _treeLookup.Remove(bestKey);
            }
            else _treeLookup.Remove(TreeKey(p.X, p.Y));
            // Collapse the instance to zero scale (keeps indices stable).
            _treeBatches[entry.variant][entry.index] = Matrix4x4.zero;
            return true;
        }

        private readonly Matrix4x4[] _batchBuffer = new Matrix4x4[1023];

        /// <summary>Draws instanced trees (call every frame).</summary>
        public void Render()
        {
            if (_treeMat == null) return;
            foreach (var kv in _treeBatches)
            {
                var mesh = _treeMeshes[kv.Key];
                var list = kv.Value;
                for (int start = 0; start < list.Count; start += 1023)
                {
                    int n = Mathf.Min(1023, list.Count - start);
                    list.CopyTo(start, _batchBuffer, 0, n);
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        Graphics.DrawMeshInstanced(mesh, sub, _treeMat, _batchBuffer, n, null, UnityEngine.Rendering.ShadowCastingMode.On, true);
                }
            }
        }

        // ------------------------------------------------------------------ props

        private int _lightCount;

        private void BuildProp(DressingProp p)
        {
            if (p.Type == "blood_crack_decal")
            {
                Decals.Create(Root.transform, this, new Vector2(p.X, p.Y), 3.5f * p.Scale, "ground_crack", new Color(0.55f, 0.02f, 0.05f, 0.85f), false, p.Rot);
                return;
            }
            var go = ProceduralModels.Prop(p.Type, Root.transform, out bool emits, out Color lightColor);
            if (go == null) return;
            // Imported prop models (Resources/Models/Props/<type>) replace the procedural one when present.
            var prefab = Resources.Load<GameObject>("Models/Props/" + p.Type);
            if (prefab != null)
            {
                UnityEngine.Object.Destroy(go);
                go = UnityEngine.Object.Instantiate(prefab, Root.transform, false);
            }
            go.transform.localPosition = World(p.X, p.Y, -0.05f);
            go.transform.localRotation = Quaternion.Euler(0, p.Rot, 0);
            go.transform.localScale = Vector3.one * p.Scale;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (emits || p.Lit)
            {
                float flameY = p.Type.Contains("brazier") ? 1.5f : p.Type == "burning_wagon" ? 1.3f : 2f;
                if (p.Type.Contains("brazier") || p.Type == "burning_wagon")
                {
                    var flame = ParticleFactory.Create("flame", go.transform, new ParticleSpec
                    {
                        Texture = "soft_glow", Additive = true, Rate = 18, LifetimeMin = 0.5f, LifetimeMax = 0.9f, SpeedMin = 0.6f, SpeedMax = 1.4f,
                        SizeMin = 0.35f, SizeMax = 0.7f, ColorA = lightColor, ColorB = lightColor * 0.8f, Gravity = -0.25f,
                        Shape = ParticleSystemShapeType.Circle, ShapeRadius = 0.25f, Loop = true, Duration = 2, MaxParticles = 40, Noise = 0.4f,
                        WorldSpace = false, SizeOverLifeEnd = 0.2f,
                    });
                    flame.transform.localPosition = new Vector3(0, flameY, 0);
                    flame.Play();
                    var embers = ParticleFactory.Create("embers", go.transform, new ParticleSpec
                    {
                        Texture = "ember", Additive = true, Rate = 3, LifetimeMin = 1.2f, LifetimeMax = 2.2f, SpeedMin = 0.8f, SpeedMax = 1.8f,
                        SizeMin = 0.06f, SizeMax = 0.12f, ColorA = lightColor, Gravity = -0.1f, Shape = ParticleSystemShapeType.Circle, ShapeRadius = 0.3f,
                        Loop = true, Duration = 2, MaxParticles = 12, Noise = 0.8f, WorldSpace = true,
                    });
                    embers.transform.localPosition = new Vector3(0, flameY, 0);
                    embers.Play();
                }
                // Real-time lights are capped; the rest rely on emissive + bloom and a ground glow decal.
                if (_lightCount < 28)
                {
                    _lightCount++;
                    var lg = new GameObject("light");
                    lg.transform.SetParent(go.transform, false);
                    lg.transform.localPosition = new Vector3(0, flameY + 0.3f, 0);
                    var l = lg.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = lightColor;
                    l.range = 9f;
                    l.intensity = 2.2f;
                    l.shadows = LightShadows.None;
                    _lights.Add(l);
                }
                Decals.Create(Root.transform, this, new Vector2(p.X, p.Y), 5f, "soft_glow", new Color(lightColor.r, lightColor.g, lightColor.b, 0.35f), true, 0f);
            }
        }

        /// <summary>Flickers flame lights (call every frame).</summary>
        public void AnimateLights(float time)
        {
            for (int i = 0; i < _lights.Count; i++)
            {
                var l = _lights[i];
                if (l == null) continue;
                l.intensity = 2.0f + Mathf.PerlinNoise(time * 3f, i * 1.37f) * 0.9f;
            }
        }

        // ------------------------------------------------------------------ minimap

        private void BakeMinimap()
        {
            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "Minimap", wrapMode = TextureWrapMode.Clamp };
            var splat0 = Resources.Load<Texture2D>("Maps/Velmoragh/splat0");
            var splat1 = Resources.Load<Texture2D>("Maps/Velmoragh/splat1");
            Color[] s0 = null, s1 = null;
            int sw = 0, sh = 0;
            try
            {
                if (splat0 != null && splat0.isReadable && splat1 != null && splat1.isReadable)
                {
                    s0 = splat0.GetPixels(); s1 = splat1.GetPixels();
                    sw = splat0.width; sh = splat0.height;
                }
            }
            catch (Exception) { s0 = null; }
            Color[] layerCols =
            {
                new Color(0.2f, 0.22f, 0.13f), new Color(0.3f, 0.23f, 0.17f), new Color(0.36f, 0.34f, 0.32f), new Color(0.33f, 0.31f, 0.31f),
                new Color(0.3f, 0.08f, 0.07f), new Color(0.38f, 0.37f, 0.35f), new Color(0.3f, 0.2f, 0.12f), new Color(0.58f, 0.54f, 0.45f),
            };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float wx = (x + 0.5f) / S * Map.Width, wy = (y + 0.5f) / S * Map.Height;
                Color c;
                if (s0 != null)
                {
                    int sx = Mathf.Clamp((int)((x + 0.5f) / S * sw), 0, sw - 1), sy = Mathf.Clamp((int)((y + 0.5f) / S * sh), 0, sh - 1);
                    var a = s0[sy * sw + sx]; var b = s1[sy * sw + sx];
                    float tot = a.r + a.g + a.b + a.a + b.r + b.g + b.b + b.a + 1e-4f;
                    c = (layerCols[0] * a.r + layerCols[1] * a.g + layerCols[2] * a.b + layerCols[3] * a.a + layerCols[4] * b.r + layerCols[5] * b.g + layerCols[6] * b.b + layerCols[7] * b.a) / tot;
                }
                else c = new Color(0.25f, 0.23f, 0.2f);
                float h = HeightAt(wx, wy);
                float hx = HeightAt(wx + 0.75f, wy) - HeightAt(wx - 0.75f, wy);
                float hy = HeightAt(wx, wy + 0.75f) - HeightAt(wx, wy - 0.75f);
                float shade = Mathf.Clamp(1f + (-hx + hy) * 0.35f, 0.6f, 1.35f);
                c *= shade * (0.85f + h * 0.03f);
                Grid.ToCell(new System.Numerics.Vector2(wx, wy), out int gx, out int gy);
                if (Grid.IsTreeCell(gx, gy)) c = Color.Lerp(c, new Color(0.07f, 0.12f, 0.09f), 0.75f);
                else if (h < WaterLevel - 0.05f) c = Color.Lerp(c, new Color(0.25f, 0.04f, 0.06f), 0.7f);
                else if (!Grid.IsWalkableCell(gx, gy)) c *= 0.65f;
                c.a = 1f;
                px[y * S + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            MinimapTexture = tex;
        }

        public void Dispose()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
            if (MinimapTexture != null) UnityEngine.Object.Destroy(MinimapTexture);
        }
    }

    /// <summary>Terrain-conforming decals (grid mesh sampled on the heightfield).</summary>
    public static class Decals
    {
        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        public static Material Material(string texture, bool additive)
        {
            string key = texture + (additive ? "+" : "");
            if (Mats.TryGetValue(key, out var m) && m != null) return m;
            var shader = Shader.Find("Bloodfall/GroundDecal") ?? Shader.Find("Sprites/Default");
            m = new Material(shader) { name = "Decal_" + key, enableInstancing = true };
            m.mainTexture = Resources.Load<Texture2D>("Textures/VFX/" + texture);
            if (additive)
            {
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            }
            Mats[key] = m;
            return m;
        }

        public static GameObject Create(Transform parent, MapRenderer map, Vector2 center, float size, string texture, Color color, bool additive, float rotation, int resolution = 8)
        {
            var go = new GameObject("decal_" + texture);
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = new Mesh { name = "decal" };
            mr.sharedMaterial = Material(texture, additive);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Conform(mf.sharedMesh, map, center, size, rotation, color, resolution);
            return go;
        }

        /// <summary>Rebuilds a decal mesh at a new position (moving indicators).</summary>
        public static void Conform(Mesh mesh, MapRenderer map, Vector2 center, float size, float rotationDeg, Color color, int res = 8)
        {
            int n = res + 1;
            var v = new Vector3[n * n];
            var uv = new Vector2[n * n];
            var col = new Color[n * n];
            float rad = rotationDeg * Mathf.Deg2Rad;
            float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)res, w = y / (float)res;
                float lx = (u - 0.5f) * size, ly = (w - 0.5f) * size;
                float wx = center.x + lx * cs - ly * sn, wy = center.y + lx * sn + ly * cs;
                v[y * n + x] = new Vector3(wx, (map != null ? map.HeightAt(wx, wy) : 0f) + 0.06f, wy);
                uv[y * n + x] = new Vector2(u, w);
                col[y * n + x] = color;
            }
            var tris = new int[res * res * 6];
            int t = 0;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                int a = y * n + x, b = a + 1, c = a + n, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
            mesh.Clear();
            mesh.vertices = v;
            mesh.uv = uv;
            mesh.colors = col;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }
    }
}
