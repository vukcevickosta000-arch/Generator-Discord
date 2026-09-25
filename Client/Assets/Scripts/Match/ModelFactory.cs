using System.Collections.Generic;
using Bloodfall.Data;
using UnityEngine;

namespace Bloodfall.Client.Match
{
    public enum RigKind { Static, Biped, Quadruped, Winged, Serpent, Siege, Floating }

    /// <summary>Transforms a procedural animator can drive (null entries are simply skipped).</summary>
    public sealed class RigParts
    {
        public RigKind Kind;
        public Transform Hips, Torso, Head, ArmL, ArmR, LegL, LegR, LegBL, LegBR, Weapon, WingL, WingR, Tail, Spin;
        public float Height = 1.8f;
    }

    /// <summary>A spawned visual for an entity: renderers, attachment points and either clips or a procedural rig.</summary>
    public sealed class ModelInstance
    {
        public GameObject Root;
        public Transform Pivot;              // animation offsets (death topple, scale); Root carries position + facing
        public RigParts Rig;
        public Dictionary<string, AnimationClip> Clips;
        public Renderer[] Renderers;
        public float Height = 2f;
        public float Radius = 0.5f;
        public Transform ProjectileOrigin;
        public bool Imported;
    }

    /// <summary>
    /// Resolves model keys from game data (e.g. "hero_vorak", "tower_dusk_t2") to visuals. Imported models are
    /// loaded from Resources/Models/&lt;key&gt; (FBX exported by the Blender pipeline, clips as sub-assets). Anything not
    /// yet authored gets a clearly-procedural stand-in built by <see cref="ProceduralModels"/> so every unit is always
    /// visible and readable (team colours, silhouettes by role).
    /// </summary>
    public static class ModelFactory
    {
        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        private static readonly Dictionary<Material, Material> Converted = new Dictionary<Material, Material>();
        private static readonly HashSet<string> MissingLogged = new HashSet<string>();
        private static Shader _lit;

        public static readonly Color DawnPrimary = new Color(0.86f, 0.80f, 0.64f);
        public static readonly Color DawnAccent = new Color(0.85f, 0.66f, 0.26f);
        public static readonly Color DawnGlow = new Color(1.0f, 0.78f, 0.35f);
        public static readonly Color DuskPrimary = new Color(0.20f, 0.12f, 0.14f);
        public static readonly Color DuskAccent = new Color(0.55f, 0.06f, 0.09f);
        public static readonly Color DuskGlow = new Color(1.0f, 0.12f, 0.12f);
        public static readonly Color NeutralGlow = new Color(0.5f, 0.9f, 0.4f);

        public static Color TeamPrimary(Team t) => t == Team.Dawn ? DawnPrimary : t == Team.Dusk ? DuskPrimary : new Color(0.35f, 0.33f, 0.3f);
        public static Color TeamAccent(Team t) => t == Team.Dawn ? DawnAccent : t == Team.Dusk ? DuskAccent : new Color(0.3f, 0.36f, 0.24f);
        public static Color TeamGlow(Team t) => t == Team.Dawn ? DawnGlow : t == Team.Dusk ? DuskGlow : NeutralGlow;

        public static Shader LitShader
        {
            get
            {
                if (_lit == null)
                    _lit = Shader.Find("Bloodfall/Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                return _lit;
            }
        }

        /// <summary>Vertex-coloured lit material (matte, metal or emissive variants shared across all models).</summary>
        public static Material VertexMat(float smoothness, float metallic)
        {
            string key = $"vc_{smoothness:0.00}_{metallic:0.00}";
            if (Mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(LitShader) { name = key, enableInstancing = true };
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            Mats[key] = m;
            return m;
        }

        public static Material GlowMat(Color c, float intensity = 3f)
        {
            string key = $"glow_{ColorUtility.ToHtmlStringRGB(c)}_{intensity:0.0}";
            if (Mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(LitShader) { name = key, enableInstancing = true };
            m.SetColor("_BaseColor", c * 0.4f);
            m.SetColor("_EmissionColor", c * intensity);
            m.EnableKeyword("_EMISSION");
            Mats[key] = m;
            return m;
        }

        /// <summary>Vertex-coloured material tinted with the team accent (imported models' bf_team parts).</summary>
        public static Material TeamMat(Team team)
        {
            string key = "team_" + team;
            if (Mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(LitShader) { name = key, enableInstancing = true };
            m.SetColor("_BaseColor", TeamAccent(team) * 1.4f);
            m.SetFloat("_Smoothness", 0.3f);
            Mats[key] = m;
            return m;
        }

        /// <summary>Standard material set for MeshBuilder meshes: submesh 0 matte, 1 metal, 2 glow.</summary>
        public static Material[] StandardSet(Color glow, int submeshes, float glowIntensity = 3f)
        {
            var arr = new Material[submeshes];
            if (submeshes > 0) arr[0] = VertexMat(0.22f, 0f);
            if (submeshes > 1) arr[1] = VertexMat(0.62f, 0.75f);
            if (submeshes > 2) arr[2] = GlowMat(glow, glowIntensity);
            for (int i = 3; i < submeshes; i++) arr[i] = arr[0];
            return arr;
        }

        public static ModelInstance Create(string modelKey, Team team, UnitKind kind, float scale, float collisionRadius, Transform parent)
        {
            var root = new GameObject(modelKey ?? "unit");
            root.transform.SetParent(parent, false);
            var pivot = new GameObject("pivot").transform;
            pivot.SetParent(root.transform, false);
            var mi = new ModelInstance { Root = root, Pivot = pivot, Radius = Mathf.Max(0.3f, collisionRadius) };

            var prefab = string.IsNullOrEmpty(modelKey) ? null : Resources.Load<GameObject>("Models/" + modelKey);
            if (prefab != null)
            {
                var go = Object.Instantiate(prefab, pivot, false);
                go.transform.localScale = Vector3.one * scale;
                ConvertMaterials(go, team);
                mi.Imported = true;
                mi.Clips = new Dictionary<string, AnimationClip>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var clip in Resources.LoadAll<AnimationClip>("Models/" + modelKey))
                {
                    if (clip.name.StartsWith("__preview__")) continue;
                    // Blender exports "Armature|Idle"; keep the action name.
                    var name = clip.name;
                    int bar = name.LastIndexOf('|');
                    if (bar >= 0) name = name.Substring(bar + 1);
                    mi.Clips[name] = clip;
                }
                var bounds = CalculateBounds(go);
                mi.Height = Mathf.Max(0.5f, bounds.max.y - root.transform.position.y);
                var origin = FindDeep(go.transform, "projectile_origin") ?? FindDeep(go.transform, "hand_r");
                mi.ProjectileOrigin = origin;
            }
            else
            {
                if (!string.IsNullOrEmpty(modelKey) && MissingLogged.Add(modelKey))
                    Debug.Log($"[Models] '{modelKey}' has no imported model yet; using procedural stand-in.");
                // Procedural geometry is authored facing -Z; a flipped child makes it face +Z like imported models.
                var flip = new GameObject("flip").transform;
                flip.SetParent(pivot, false);
                flip.localRotation = Quaternion.Euler(0, 180f, 0);
                mi.Pivot = flip;
                ProceduralModels.Build(mi, modelKey ?? "", team, kind);
                mi.Pivot = pivot;
                pivot.localScale = Vector3.one * scale;
                mi.Height *= scale;
            }
            mi.Renderers = root.GetComponentsInChildren<Renderer>();
            foreach (var r in mi.Renderers)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            if (mi.ProjectileOrigin == null)
            {
                var po = new GameObject("projectile_origin").transform;
                po.SetParent(pivot, false);
                po.localPosition = new Vector3(0.25f, mi.Height * 0.7f / Mathf.Max(0.01f, pivot.localScale.y), 0.4f);
                mi.ProjectileOrigin = po;
            }
            return mi;
        }

        private static void ConvertMaterials(GameObject go, Team team)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    var s = src[i];
                    if (s == null) { dst[i] = VertexMat(0.3f, 0f); continue; }
                    if (s.shader == LitShader) { dst[i] = s; continue; }
                    // Blender pipeline materials (Blender/scripts/bf_lib.py): colours live in vertex colours, the name says how
                    // the surface shades. bf_team is tinted per team, so it is not cached across teams.
                    string lname = s.name.ToLowerInvariant();
                    if (lname.StartsWith("bf_"))
                    {
                        if (lname.StartsWith("bf_glow_") && ColorUtility.TryParseHtmlString("#" + s.name.Substring(8, Mathf.Min(6, s.name.Length - 8)), out var gc)) dst[i] = GlowMat(gc, 3f);
                        else if (lname.StartsWith("bf_metal")) dst[i] = VertexMat(0.62f, 0.75f);
                        else if (lname.StartsWith("bf_team")) dst[i] = TeamMat(team);
                        else dst[i] = VertexMat(0.22f, 0f);
                        continue;
                    }
                    if (!Converted.TryGetValue(s, out var m) || m == null)
                    {
                        m = new Material(LitShader) { name = s.name + " (Bloodfall)", enableInstancing = true };
                        if (s.mainTexture != null) m.SetTexture("_BaseMap", s.mainTexture);
                        if (s.HasProperty("_Color")) m.SetColor("_BaseColor", s.color);
                        if (s.HasProperty("_BumpMap") && s.GetTexture("_BumpMap") != null) m.SetTexture("_BumpMap", s.GetTexture("_BumpMap"));
                        if (s.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", s.GetColor("_EmissionColor"));
                        if (s.HasProperty("_Glossiness")) m.SetFloat("_Smoothness", s.GetFloat("_Glossiness"));
                        if (s.HasProperty("_Metallic")) m.SetFloat("_Metallic", s.GetFloat("_Metallic"));
                        // Blender material named "*team*" takes the team colour.
                        if (s.name.ToLowerInvariant().Contains("team")) m.SetColor("_BaseColor", TeamAccent(team));
                        Converted[s] = m;
                    }
                    dst[i] = m;
                }
                r.sharedMaterials = dst;
            }
        }

        public static Bounds CalculateBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        public static Transform FindDeep(Transform t, string name)
        {
            if (t.name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>Creates a mesh renderer child with the given mesh and materials.</summary>
        public static Transform Part(Transform parent, string name, Mesh mesh, Material[] mats, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            var m = new Material[mesh.subMeshCount];
            for (int i = 0; i < m.Length; i++) m[i] = mats[Mathf.Min(i, mats.Length - 1)];
            r.sharedMaterials = m;
            return go.transform;
        }

        public static Transform Pivot(Transform parent, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }
    }
}
