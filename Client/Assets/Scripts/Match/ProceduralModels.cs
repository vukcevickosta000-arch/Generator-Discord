using System.Collections.Generic;
using Bloodfall.Data;
using UnityEngine;

namespace Bloodfall.Client.Match
{
    /// <summary>
    /// Procedural stand-in geometry for every model key the game data references. These keep the game fully
    /// playable and readable before (or without) the Blender-authored models: silhouettes differ per role (melee,
    /// ranged, siege, beasts, structures), colours follow the team palette, and bipeds/beasts get a jointed rig the
    /// procedural animator can drive. Meshes are cached per key+team.
    /// </summary>
    public static class ProceduralModels
    {
        private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();

        private enum Weapon { None, Greatsword, Sword, Bow, Crossbow, Staff, Claws, Shovel, Hammer }

        private sealed class Biped
        {
            public float H = 1.8f;
            public float Bulk = 1f;
            public Color Skin = new Color(0.62f, 0.52f, 0.46f);
            public Color Armor = new Color(0.35f, 0.34f, 0.36f);
            public Color Cloth = new Color(0.3f, 0.1f, 0.12f);
            public Color Accent = Color.gray;
            public Color Glow = Color.red;
            public bool Robe, Bone, Helmet = true, Horns, Cape, Shield, Hunched, Hood;
            public Weapon Weapon = Weapon.Sword;
        }

        public static void Build(ModelInstance mi, string key, Team team, UnitKind kind)
        {
            var glow = ModelFactory.TeamGlow(team);
            var prim = ModelFactory.TeamPrimary(team);
            var acc = ModelFactory.TeamAccent(team);
            bool dawn = team == Team.Dawn;
            switch (key)
            {
                // ---------------------------------------------------------------- heroes
                case "hero_vorak":
                    BuildBiped(mi, key + team, new Biped
                    {
                        H = 2.05f, Bulk = 1.3f, Skin = new Color(0.78f, 0.74f, 0.72f), Armor = new Color(0.16f, 0.13f, 0.15f),
                        Cloth = new Color(0.42f, 0.03f, 0.06f), Accent = new Color(0.6f, 0.08f, 0.1f), Glow = new Color(1f, 0.1f, 0.12f),
                        Helmet = true, Horns = true, Cape = true, Weapon = Weapon.Greatsword,
                    });
                    return;
                case "hero_ilyra":
                    BuildBiped(mi, key + team, new Biped
                    {
                        H = 1.8f, Bulk = 0.85f, Skin = new Color(0.85f, 0.78f, 0.76f), Armor = new Color(0.12f, 0.08f, 0.1f),
                        Cloth = new Color(0.36f, 0.02f, 0.08f), Accent = new Color(0.7f, 0.55f, 0.35f), Glow = new Color(1f, 0.15f, 0.2f),
                        Robe = true, Helmet = false, Hood = true, Cape = false, Weapon = Weapon.Staff,
                    });
                    return;
                case "hex_bat":
                    BuildWinged(mi, key, new Color(0.15f, 0.1f, 0.12f), new Color(1f, 0.2f, 0.2f), 0.6f, false);
                    return;
            }

            if (key.StartsWith("creep_"))
            {
                bool elite = key.EndsWith("_elite") || key.Contains("paladin") || key.Contains("crypthorror");
                if (key.Contains("ballista") || key.Contains("catapult")) { BuildSiege(mi, key + team, team, key.Contains("catapult")); return; }
                var b = new Biped
                {
                    H = elite ? 1.9f : 1.6f, Bulk = elite ? 1.2f : 0.95f,
                    Armor = dawn ? new Color(0.7f, 0.68f, 0.62f) : new Color(0.72f, 0.68f, 0.58f),
                    Cloth = acc, Accent = dawn ? ModelFactory.DawnAccent : new Color(0.3f, 0.25f, 0.22f), Glow = glow,
                    Skin = dawn ? new Color(0.72f, 0.58f, 0.48f) : new Color(0.8f, 0.76f, 0.66f), Bone = !dawn,
                    Helmet = dawn, Cape = elite && dawn,
                };
                if (key.Contains("arbalist")) { b.Weapon = Weapon.Crossbow; b.Helmet = true; }
                else if (key.Contains("bonearcher")) b.Weapon = Weapon.Bow;
                else if (key.Contains("paladin")) { b.Weapon = Weapon.Hammer; b.Shield = true; b.H = 2.1f; b.Bulk = 1.35f; b.Cape = true; }
                else if (key.Contains("crypthorror")) { b.Weapon = Weapon.Claws; b.H = 2.3f; b.Bulk = 1.6f; b.Hunched = true; b.Helmet = false; b.Horns = true; }
                else if (key.Contains("thrall")) { b.Weapon = Weapon.Sword; b.Helmet = false; b.Hunched = true; }
                else { b.Weapon = Weapon.Sword; b.Shield = true; }
                BuildBiped(mi, key + team, b);
                return;
            }

            if (key.StartsWith("neutral_"))
            {
                var dark = new Color(0.22f, 0.2f, 0.2f);
                var green = new Color(0.5f, 0.95f, 0.45f);
                if (key.Contains("rat")) { BuildQuadruped(mi, key, new Color(0.3f, 0.26f, 0.24f), green, 0.45f, true); return; }
                if (key.Contains("hound")) { BuildQuadruped(mi, key, new Color(0.78f, 0.74f, 0.64f), new Color(0.4f, 1f, 0.5f), 0.9f, false); return; }
                if (key.Contains("gargoyle")) { BuildWinged(mi, key, new Color(0.34f, 0.33f, 0.32f), new Color(1f, 0.3f, 0.2f), key.Contains("whelp") ? 0.8f : 1.35f, true); return; }
                if (key.Contains("wyrm")) { BuildSerpent(mi, key, key.Contains("nightwyrm") ? new Color(0.12f, 0.1f, 0.16f) : new Color(0.25f, 0.2f, 0.28f), new Color(0.6f, 0.3f, 1f), key.Contains("nightwyrm") ? 1.6f : 0.9f); return; }
                if (key.Contains("gravekeeper"))
                {
                    BuildBiped(mi, key, new Biped { H = 2.3f, Bulk = 1.5f, Skin = new Color(0.5f, 0.52f, 0.46f), Armor = dark, Cloth = new Color(0.18f, 0.16f, 0.14f), Accent = dark, Glow = green, Hood = true, Helmet = false, Weapon = Weapon.Shovel, Hunched = true, Robe = true });
                    return;
                }
                BuildBiped(mi, key, new Biped { H = 1.6f, Bulk = 0.9f, Skin = new Color(0.55f, 0.6f, 0.5f), Armor = dark, Cloth = new Color(0.25f, 0.22f, 0.2f), Accent = dark, Glow = green, Helmet = false, Weapon = Weapon.Claws, Hunched = true, Bone = true });
                return;
            }

            if (key.StartsWith("tower_")) { BuildTower(mi, key, team, TierOf(key)); return; }
            if (key.StartsWith("barracks_")) { BuildBarracks(mi, key, team, key.Contains("ranged")); return; }
            if (key.StartsWith("core_")) { BuildCore(mi, key, team); return; }
            if (key.StartsWith("fountain_")) { BuildFountain(mi, key, team); return; }
            if (key.StartsWith("ward_")) { BuildWard(mi, key, team, key.Contains("sentry")); return; }

            // Unknown key: a neutral obelisk so the entity is still visible and clickable.
            var mb = new MeshBuilder();
            mb.Cylinder(Vector3.zero, Vector3.up * 1.6f, 0.4f, 0.1f, 6, prim);
            mb.Sub(2).Sphere(Vector3.up * 1.8f, Vector3.one * 0.18f, 8, 6, glow);
            ModelFactory.Part(mi.Pivot, "body", Cached(key + "_unknown", mb), ModelFactory.StandardSet(glow, 3), Vector3.zero);
            mi.Height = 2f;
            mi.Rig = new RigParts { Kind = RigKind.Static, Height = 2f };
        }

        private static int TierOf(string key)
        {
            for (int t = 1; t <= 4; t++) if (key.EndsWith("_t" + t)) return t;
            return 1;
        }

        private static Mesh Cached(string key, MeshBuilder mb)
        {
            if (MeshCache.TryGetValue(key, out var m) && m != null) return m;
            m = mb.ToMesh(key);
            MeshCache[key] = m;
            return m;
        }

        private static Mesh CachedBuild(string key, System.Action<MeshBuilder> build)
        {
            if (MeshCache.TryGetValue(key, out var m) && m != null) return m;
            var mb = new MeshBuilder();
            build(mb);
            m = mb.ToMesh(key);
            MeshCache[key] = m;
            return m;
        }

        // ================================================================== bipeds

        private static void BuildBiped(ModelInstance mi, string key, Biped b)
        {
            float H = b.H, k = b.Bulk;
            float hipY = H * 0.5f, shoulderY = H * 0.8f, legLen = hipY, armLen = H * 0.37f;
            var mats = ModelFactory.StandardSet(b.Glow, 3);
            var rig = new RigParts { Kind = RigKind.Biped, Height = H };
            var hips = ModelFactory.Pivot(mi.Pivot, "hips", new Vector3(0, hipY, 0));
            rig.Hips = hips;
            var torso = ModelFactory.Pivot(hips, "torso", Vector3.zero);
            if (b.Hunched) torso.localRotation = Quaternion.Euler(18, 0, 0);
            rig.Torso = torso;

            // Torso (chest, abdomen, pauldrons, cape, robe skirt).
            var torsoMesh = CachedBuild(key + "_torso", mb =>
            {
                float chestW = 0.44f * k, chestD = 0.28f * k;
                float top = shoulderY - hipY;
                if (b.Bone)
                {
                    mb.Cylinder(new Vector3(0, 0.05f, 0), new Vector3(0, top, 0), 0.06f, 0.06f, 6, b.Skin);
                    for (int i = 0; i < 4; i++)
                    {
                        float y = top * (0.45f + i * 0.13f);
                        mb.With(new Vector3(0, y, 0.02f), Quaternion.identity, new Vector3(1, 0.35f, 0.75f), () => mb.Cylinder(Vector3.zero, Vector3.up * 0.05f, chestW * 0.5f, chestW * 0.5f, 10, b.Skin, false));
                    }
                    mb.Sphere(new Vector3(0, 0.05f, 0), new Vector3(0.18f * k, 0.1f, 0.12f * k), 8, 5, b.Skin);
                }
                else
                {
                    mb.Box(new Vector3(0, top * 0.62f, 0), new Vector3(chestW, top * 0.62f, chestD), b.Robe ? b.Cloth : b.Armor, 0.03f);
                    mb.Box(new Vector3(0, top * 0.2f, 0), new Vector3(chestW * 0.8f, top * 0.4f, chestD * 0.9f), b.Cloth);
                    mb.Box(new Vector3(0, 0.02f, 0), new Vector3(chestW * 0.85f, 0.1f, chestD), b.Accent);
                }
                if (!b.Robe && !b.Bone)
                {
                    mb.Sub(1);
                    mb.Sphere(new Vector3(-chestW * 0.62f, top * 0.95f, 0), new Vector3(0.16f, 0.12f, 0.16f) * k, 8, 5, b.Armor * 1.2f);
                    mb.Sphere(new Vector3(chestW * 0.62f, top * 0.95f, 0), new Vector3(0.16f, 0.12f, 0.16f) * k, 8, 5, b.Armor * 1.2f);
                    mb.Box(new Vector3(0, top * 0.66f, -chestD * 0.52f), new Vector3(chestW * 0.7f, top * 0.45f, 0.03f), b.Armor * 1.3f);
                    mb.Sub(2);
                    mb.Sphere(new Vector3(0, top * 0.7f, -chestD * 0.56f), Vector3.one * 0.045f, 6, 4, b.Glow);
                    mb.Sub(0);
                }
                if (b.Cape)
                {
                    mb.With(new Vector3(0, top, chestD * 0.55f), Quaternion.Euler(-8, 0, 0), Vector3.one, () =>
                        mb.Box(new Vector3(0, -H * 0.36f, 0), new Vector3(chestW * 1.1f, H * 0.72f, 0.03f), b.Cloth * 0.9f));
                }
                if (b.Robe)
                    mb.Cylinder(new Vector3(0, 0.02f, 0), new Vector3(0, -hipY * 0.92f, 0), chestW * 0.55f, chestW * 0.95f, 10, b.Cloth, false, true, b.Cloth * 0.7f);
                if (b.Horns && !b.Helmet)
                {
                    mb.Cylinder(new Vector3(-chestW * 0.5f, top * 0.95f, 0), new Vector3(-chestW * 0.9f, top * 1.35f, 0.05f), 0.05f, 0.005f, 5, b.Skin * 0.9f);
                    mb.Cylinder(new Vector3(chestW * 0.5f, top * 0.95f, 0), new Vector3(chestW * 0.9f, top * 1.35f, 0.05f), 0.05f, 0.005f, 5, b.Skin * 0.9f);
                }
            });
            ModelFactory.Part(torso, "torso_mesh", torsoMesh, mats, Vector3.zero);

            // Head.
            var head = ModelFactory.Pivot(torso, "head", new Vector3(0, shoulderY - hipY + 0.04f, 0));
            rig.Head = head;
            var headMesh = CachedBuild(key + "_head", mb =>
            {
                float hr = 0.12f * Mathf.Lerp(1f, k, 0.3f);
                mb.Cylinder(Vector3.zero, new Vector3(0, 0.08f, 0), 0.05f, 0.05f, 6, b.Skin, false);
                mb.Sphere(new Vector3(0, 0.1f + hr, 0), new Vector3(hr * 0.9f, hr * 1.1f, hr), 10, 7, b.Skin);
                mb.Sub(2);
                mb.Sphere(new Vector3(-0.04f, 0.1f + hr * 1.1f, -hr * 0.85f), Vector3.one * 0.018f, 5, 3, b.Glow);
                mb.Sphere(new Vector3(0.04f, 0.1f + hr * 1.1f, -hr * 0.85f), Vector3.one * 0.018f, 5, 3, b.Glow);
                mb.Sub(0);
                if (b.Helmet)
                {
                    mb.Sub(1);
                    mb.Sphere(new Vector3(0, 0.12f + hr, 0.01f), new Vector3(hr * 1.05f, hr * 1.12f, hr * 1.08f), 10, 7, b.Armor * 1.25f, b.Armor * 1.5f);
                    mb.Box(new Vector3(0, 0.1f + hr * 0.9f, -hr * 0.95f), new Vector3(hr * 0.9f, hr * 0.12f, 0.02f), b.Armor * 0.5f);
                    mb.Sub(0);
                    if (b.Horns)
                    {
                        mb.Cylinder(new Vector3(-hr * 0.8f, 0.1f + hr * 1.5f, 0), new Vector3(-hr * 1.9f, 0.1f + hr * 3.1f, 0.08f), 0.045f, 0.004f, 6, new Color(0.85f, 0.8f, 0.7f));
                        mb.Cylinder(new Vector3(hr * 0.8f, 0.1f + hr * 1.5f, 0), new Vector3(hr * 1.9f, 0.1f + hr * 3.1f, 0.08f), 0.045f, 0.004f, 6, new Color(0.85f, 0.8f, 0.7f));
                    }
                }
                if (b.Hood)
                {
                    mb.Cylinder(new Vector3(0, 0.08f, 0.02f), new Vector3(0, 0.1f + hr * 2.6f, 0.08f), hr * 1.35f, 0.01f, 10, b.Cloth * 0.85f, false);
                }
            });
            ModelFactory.Part(head, "head_mesh", headMesh, mats, Vector3.zero);

            // Arms (pivot at shoulder, hanging down -Y).
            float sx = 0.3f * k;
            rig.ArmL = ModelFactory.Pivot(torso, "arm_l", new Vector3(-sx, shoulderY - hipY - 0.04f, 0));
            rig.ArmR = ModelFactory.Pivot(torso, "arm_r", new Vector3(sx, shoulderY - hipY - 0.04f, 0));
            var armMesh = CachedBuild(key + "_arm", mb =>
            {
                float r = 0.06f * k;
                var skinOrArmor = b.Bone ? b.Skin : b.Robe ? b.Cloth : b.Armor;
                mb.Cylinder(Vector3.zero, new Vector3(0, -armLen * 0.5f, 0), r, r * 0.85f, 7, skinOrArmor, false);
                mb.Cylinder(new Vector3(0, -armLen * 0.5f, 0), new Vector3(0, -armLen, 0.02f), r * 0.85f, r * 0.7f, 7, b.Bone ? b.Skin : b.Armor * 1.1f, false);
                mb.Sphere(new Vector3(0, -armLen - 0.03f, 0.02f), Vector3.one * r * 1.05f, 6, 4, b.Skin);
            });
            ModelFactory.Part(rig.ArmL, "arm_l_mesh", armMesh, mats, Vector3.zero);
            ModelFactory.Part(rig.ArmR, "arm_r_mesh", armMesh, mats, Vector3.zero);

            // Legs (pivot at hip).
            rig.LegL = ModelFactory.Pivot(hips, "leg_l", new Vector3(-0.12f * k, 0, 0));
            rig.LegR = ModelFactory.Pivot(hips, "leg_r", new Vector3(0.12f * k, 0, 0));
            var legMesh = CachedBuild(key + "_leg", mb =>
            {
                float r = 0.08f * k;
                var c = b.Bone ? b.Skin : b.Robe ? b.Cloth * 0.8f : b.Armor;
                mb.Cylinder(Vector3.zero, new Vector3(0, -legLen * 0.5f, 0), r, r * 0.8f, 7, c, false);
                mb.Cylinder(new Vector3(0, -legLen * 0.5f, 0), new Vector3(0, -legLen + 0.08f, 0), r * 0.8f, r * 0.7f, 7, b.Bone ? b.Skin : b.Armor * 0.9f, false);
                mb.Box(new Vector3(0, -legLen + 0.05f, -0.04f), new Vector3(r * 1.6f, 0.1f, r * 2.6f), b.Armor * 0.6f);
            });
            ModelFactory.Part(rig.LegL, "leg_l_mesh", legMesh, mats, Vector3.zero);
            ModelFactory.Part(rig.LegR, "leg_r_mesh", legMesh, mats, Vector3.zero);

            // Weapon in the right hand; shield on the left arm.
            if (b.Weapon != Weapon.None)
            {
                rig.Weapon = ModelFactory.Pivot(rig.ArmR, "weapon", new Vector3(0, -armLen - 0.03f, 0.02f));
                var wMesh = CachedBuild(key + "_weapon", mb => BuildWeapon(mb, b));
                ModelFactory.Part(rig.Weapon, "weapon_mesh", wMesh, mats, Vector3.zero);
                var origin = new GameObject("projectile_origin").transform;
                origin.SetParent(rig.Weapon, false);
                origin.localPosition = b.Weapon == Weapon.Staff ? new Vector3(0, 0.9f, 0) : new Vector3(0, 0, -0.5f);
                mi.ProjectileOrigin = origin;
            }
            if (b.Shield)
            {
                var sh = ModelFactory.Pivot(rig.ArmL, "shield", new Vector3(-0.08f, -armLen * 0.7f, -0.05f));
                var sMesh = CachedBuild(key + "_shield", mb =>
                {
                    mb.Sub(1);
                    mb.With(Vector3.zero, Quaternion.Euler(0, 0, 90), new Vector3(1, 1, 1.25f), () => mb.Cylinder(new Vector3(0, -0.03f, 0), new Vector3(0, 0.03f, 0), 0.3f, 0.3f, 10, b.Accent, true, true));
                    mb.Sub(2);
                    mb.Sphere(new Vector3(-0.05f, 0, 0), Vector3.one * 0.06f, 6, 4, b.Glow);
                });
                ModelFactory.Part(sh, "shield_mesh", sMesh, mats, Vector3.zero);
            }
            mi.Rig = rig;
            mi.Height = H + (b.Horns ? 0.25f : 0.05f);
        }

        private static void BuildWeapon(MeshBuilder mb, Biped b)
        {
            var steel = new Color(0.62f, 0.62f, 0.66f);
            switch (b.Weapon)
            {
                case Weapon.Greatsword:
                    // Held pointing forward-down; blade along -Z.
                    mb.Sub(0).Cylinder(new Vector3(0, 0, 0.18f), new Vector3(0, 0, -0.05f), 0.03f, 0.03f, 6, new Color(0.2f, 0.1f, 0.08f));
                    mb.Sub(1).Box(new Vector3(0, 0, -0.07f), new Vector3(0.36f, 0.05f, 0.05f), b.Accent);
                    mb.Box(new Vector3(0, 0, -0.75f), new Vector3(0.14f, 0.025f, 1.3f), steel);
                    mb.Sub(2).Box(new Vector3(0, 0.014f, -0.7f), new Vector3(0.03f, 0.01f, 1.1f), b.Glow);
                    break;
                case Weapon.Sword:
                    mb.Sub(0).Cylinder(new Vector3(0, 0, 0.1f), new Vector3(0, 0, -0.03f), 0.025f, 0.025f, 6, new Color(0.25f, 0.15f, 0.1f));
                    mb.Sub(1).Box(new Vector3(0, 0, -0.04f), new Vector3(0.2f, 0.04f, 0.04f), b.Accent);
                    mb.Box(new Vector3(0, 0, -0.42f), new Vector3(0.07f, 0.02f, 0.72f), b.Bone ? new Color(0.5f, 0.45f, 0.4f) : steel);
                    break;
                case Weapon.Hammer:
                    mb.Sub(0).Cylinder(new Vector3(0, 0, 0.2f), new Vector3(0, 0, -0.8f), 0.03f, 0.03f, 6, new Color(0.3f, 0.2f, 0.12f));
                    mb.Sub(1).Box(new Vector3(0, 0, -0.85f), new Vector3(0.34f, 0.18f, 0.18f), b.Accent);
                    mb.Sub(2).Sphere(new Vector3(0, 0, -0.85f), Vector3.one * 0.07f, 6, 4, b.Glow);
                    break;
                case Weapon.Staff:
                    // Vertical staff held upright, crowned with a glowing blood orb.
                    mb.Sub(0).Cylinder(new Vector3(0, -0.7f, 0), new Vector3(0, 0.75f, 0), 0.025f, 0.02f, 6, new Color(0.18f, 0.08f, 0.08f));
                    mb.Sub(1);
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * Mathf.PI * 2f / 3f;
                        mb.Cylinder(new Vector3(0, 0.72f, 0), new Vector3(Mathf.Cos(a) * 0.1f, 0.98f, Mathf.Sin(a) * 0.1f), 0.015f, 0.006f, 5, b.Accent);
                    }
                    mb.Sub(2).Sphere(new Vector3(0, 0.9f, 0), Vector3.one * 0.08f, 8, 6, b.Glow);
                    break;
                case Weapon.Bow:
                    mb.Sub(0);
                    for (int i = -3; i < 3; i++)
                    {
                        float a0 = i / 3f * 0.9f, a1 = (i + 1) / 3f * 0.9f;
                        mb.Cylinder(new Vector3(0, Mathf.Sin(a0) * 0.55f, -Mathf.Cos(a0) * 0.25f + 0.25f), new Vector3(0, Mathf.Sin(a1) * 0.55f, -Mathf.Cos(a1) * 0.25f + 0.25f), 0.02f, 0.02f, 5, b.Skin * 0.8f, false);
                    }
                    break;
                case Weapon.Crossbow:
                    mb.Sub(0).Box(new Vector3(0, 0, -0.25f), new Vector3(0.06f, 0.06f, 0.6f), new Color(0.3f, 0.2f, 0.12f));
                    mb.Sub(1).Box(new Vector3(0, 0, -0.5f), new Vector3(0.6f, 0.03f, 0.05f), steel);
                    break;
                case Weapon.Claws:
                    mb.Sub(0);
                    for (int i = -1; i <= 1; i++)
                        mb.Cylinder(new Vector3(i * 0.03f, 0, 0), new Vector3(i * 0.05f, -0.12f, -0.18f), 0.018f, 0.002f, 4, new Color(0.85f, 0.82f, 0.72f));
                    break;
                case Weapon.Shovel:
                    mb.Sub(0).Cylinder(new Vector3(0, 0, 0.3f), new Vector3(0, 0, -0.9f), 0.03f, 0.03f, 6, new Color(0.3f, 0.22f, 0.15f));
                    mb.Sub(1).Box(new Vector3(0, 0, -1.05f), new Vector3(0.3f, 0.03f, 0.35f), new Color(0.4f, 0.38f, 0.36f));
                    mb.Sub(2).Sphere(new Vector3(0.18f, 0.1f, 0.2f), Vector3.one * 0.08f, 6, 4, b.Glow);
                    break;
            }
            mb.Sub(0);
        }

        // ================================================================== beasts

        private static void BuildQuadruped(ModelInstance mi, string key, Color body, Color glow, float size, bool rat)
        {
            var mats = ModelFactory.StandardSet(glow, 3);
            var rig = new RigParts { Kind = RigKind.Quadruped, Height = size };
            float legLen = size * (rat ? 0.35f : 0.55f);
            var hips = ModelFactory.Pivot(mi.Pivot, "body", new Vector3(0, legLen, 0));
            rig.Hips = hips;
            rig.Torso = hips;
            var bodyMesh = CachedBuild(key + "_body", mb =>
            {
                mb.Sphere(Vector3.zero, new Vector3(size * 0.32f, size * 0.3f, size * 0.7f), 10, 7, body, body * 1.2f);
                if (!rat)
                    for (int i = 0; i < 5; i++)
                        mb.Cylinder(new Vector3(0, size * 0.22f, -size * 0.4f + i * size * 0.2f), new Vector3(0, size * 0.42f, -size * 0.35f + i * size * 0.2f), 0.03f * size, 0.003f, 4, body * 1.3f);
            });
            ModelFactory.Part(hips, "body_mesh", bodyMesh, mats, Vector3.zero);
            rig.Head = ModelFactory.Pivot(hips, "head", new Vector3(0, size * 0.12f, -size * 0.62f));
            var headMesh = CachedBuild(key + "_head", mb =>
            {
                mb.Sphere(new Vector3(0, 0, -size * 0.12f), new Vector3(size * 0.16f, size * 0.15f, size * 0.26f), 8, 6, body);
                mb.Sub(2);
                mb.Sphere(new Vector3(-size * 0.07f, size * 0.06f, -size * 0.3f), Vector3.one * size * 0.035f, 5, 3, glow);
                mb.Sphere(new Vector3(size * 0.07f, size * 0.06f, -size * 0.3f), Vector3.one * size * 0.035f, 5, 3, glow);
                mb.Sub(0);
                if (rat)
                {
                    mb.Sphere(new Vector3(-size * 0.1f, size * 0.14f, -size * 0.05f), new Vector3(size * 0.06f, size * 0.08f, size * 0.02f), 6, 4, body * 1.3f);
                    mb.Sphere(new Vector3(size * 0.1f, size * 0.14f, -size * 0.05f), new Vector3(size * 0.06f, size * 0.08f, size * 0.02f), 6, 4, body * 1.3f);
                }
            });
            ModelFactory.Part(rig.Head, "head_mesh", headMesh, mats, Vector3.zero);
            var legMesh = CachedBuild(key + "_leg", mb => mb.Cylinder(Vector3.zero, new Vector3(0, -legLen, 0), size * 0.07f, size * 0.04f, 6, body * 0.85f));
            rig.LegL = ModelFactory.Pivot(hips, "leg_fl", new Vector3(-size * 0.18f, 0, -size * 0.4f));
            rig.LegR = ModelFactory.Pivot(hips, "leg_fr", new Vector3(size * 0.18f, 0, -size * 0.4f));
            rig.LegBL = ModelFactory.Pivot(hips, "leg_bl", new Vector3(-size * 0.18f, 0, size * 0.4f));
            rig.LegBR = ModelFactory.Pivot(hips, "leg_br", new Vector3(size * 0.18f, 0, size * 0.4f));
            foreach (var l in new[] { rig.LegL, rig.LegR, rig.LegBL, rig.LegBR }) ModelFactory.Part(l, "leg_mesh", legMesh, mats, Vector3.zero);
            rig.Tail = ModelFactory.Pivot(hips, "tail", new Vector3(0, size * 0.05f, size * 0.65f));
            var tailMesh = CachedBuild(key + "_tail", mb => mb.Cylinder(Vector3.zero, new Vector3(0, rat ? 0 : size * 0.2f, size * (rat ? 0.7f : 0.45f)), size * 0.05f, size * 0.01f, 5, body * 0.8f));
            ModelFactory.Part(rig.Tail, "tail_mesh", tailMesh, mats, Vector3.zero);
            mi.Rig = rig;
            mi.Height = legLen + size * 0.5f;
        }

        private static void BuildWinged(ModelInstance mi, string key, Color body, Color glow, float size, bool gargoyle)
        {
            var b = new Biped
            {
                H = 1.5f * size, Bulk = gargoyle ? 1.3f : 0.8f, Skin = body, Armor = body * 0.9f, Cloth = body * 0.8f, Accent = body * 1.1f,
                Glow = glow, Helmet = false, Horns = gargoyle, Hunched = true, Bone = false, Weapon = Weapon.Claws,
            };
            BuildBiped(mi, key, b);
            var rig = mi.Rig;
            rig.Kind = RigKind.Winged;
            float top = b.H * 0.3f;
            rig.WingL = ModelFactory.Pivot(rig.Torso, "wing_l", new Vector3(-0.12f * size, top, 0.12f * size));
            rig.WingR = ModelFactory.Pivot(rig.Torso, "wing_r", new Vector3(0.12f * size, top, 0.12f * size));
            var mats = ModelFactory.StandardSet(glow, 3);
            var wingL = CachedBuild(key + "_wing_l", mb => Wing(mb, -1, size, body));
            var wingR = CachedBuild(key + "_wing_r", mb => Wing(mb, 1, size, body));
            ModelFactory.Part(rig.WingL, "wing_mesh", wingL, mats, Vector3.zero);
            ModelFactory.Part(rig.WingR, "wing_mesh", wingR, mats, Vector3.zero);
            if (!gargoyle)
            {
                // Hex bat: shrink the biped into a small flying body.
                mi.Pivot.localScale = Vector3.one * 0.6f;
                rig.Kind = RigKind.Winged;
            }
        }

        private static void Wing(MeshBuilder mb, int side, float size, Color c)
        {
            float span = 1.1f * size;
            var root = Vector3.zero;
            var tip = new Vector3(side * span, span * 0.35f, span * 0.2f);
            var mid = new Vector3(side * span * 0.55f, span * 0.45f, 0.05f);
            var trail1 = new Vector3(side * span * 0.8f, -span * 0.25f, span * 0.3f);
            var trail2 = new Vector3(side * span * 0.35f, -span * 0.35f, span * 0.25f);
            mb.Cylinder(root, mid, 0.04f * size, 0.03f * size, 5, c * 0.8f, false);
            mb.Cylinder(mid, tip, 0.03f * size, 0.005f, 5, c * 0.8f, false);
            var membrane = c * 0.6f;
            if (side > 0)
            {
                mb.Tri(root, mid, trail2, membrane); mb.Tri(mid, tip, trail1, membrane); mb.Tri(mid, trail1, trail2, membrane);
                mb.Tri(root, trail2, mid, membrane); mb.Tri(mid, trail1, tip, membrane); mb.Tri(mid, trail2, trail1, membrane);
            }
            else
            {
                mb.Tri(root, trail2, mid, membrane); mb.Tri(mid, trail1, tip, membrane); mb.Tri(mid, trail2, trail1, membrane);
                mb.Tri(root, mid, trail2, membrane); mb.Tri(mid, tip, trail1, membrane); mb.Tri(mid, trail1, trail2, membrane);
            }
        }

        private static void BuildSerpent(ModelInstance mi, string key, Color body, Color glow, float size)
        {
            var mats = ModelFactory.StandardSet(glow, 3);
            var rig = new RigParts { Kind = RigKind.Serpent, Height = size * 1.4f };
            var hips = ModelFactory.Pivot(mi.Pivot, "body", new Vector3(0, size * 0.9f, 0));
            rig.Hips = hips; rig.Torso = hips;
            var bodyMesh = CachedBuild(key + "_body", mb =>
            {
                mb.Sphere(Vector3.zero, new Vector3(size * 0.45f, size * 0.4f, size * 0.9f), 10, 7, body, body * 1.4f);
                mb.Cylinder(new Vector3(0, size * 0.1f, -size * 0.6f), new Vector3(0, size * 0.55f, -size * 1.1f), size * 0.22f, size * 0.16f, 8, body);
                for (int i = 0; i < 6; i++)
                    mb.Cylinder(new Vector3(0, size * 0.3f, -size * 0.6f + i * size * 0.26f), new Vector3(0, size * 0.62f, -size * 0.5f + i * size * 0.26f), size * 0.05f, 0.004f, 4, body * 1.6f);
            });
            ModelFactory.Part(hips, "body_mesh", bodyMesh, mats, Vector3.zero);
            rig.Head = ModelFactory.Pivot(hips, "head", new Vector3(0, size * 0.6f, -size * 1.15f));
            var headMesh = CachedBuild(key + "_head", mb =>
            {
                mb.Sphere(new Vector3(0, 0, -size * 0.2f), new Vector3(size * 0.2f, size * 0.17f, size * 0.36f), 8, 6, body);
                mb.Cylinder(new Vector3(-size * 0.1f, size * 0.12f, 0), new Vector3(-size * 0.2f, size * 0.3f, size * 0.25f), size * 0.04f, 0.003f, 5, body * 1.8f);
                mb.Cylinder(new Vector3(size * 0.1f, size * 0.12f, 0), new Vector3(size * 0.2f, size * 0.3f, size * 0.25f), size * 0.04f, 0.003f, 5, body * 1.8f);
                mb.Sub(2);
                mb.Sphere(new Vector3(-size * 0.09f, size * 0.07f, -size * 0.4f), Vector3.one * size * 0.04f, 5, 3, glow);
                mb.Sphere(new Vector3(size * 0.09f, size * 0.07f, -size * 0.4f), Vector3.one * size * 0.04f, 5, 3, glow);
            });
            ModelFactory.Part(rig.Head, "head_mesh", headMesh, mats, Vector3.zero);
            rig.Tail = ModelFactory.Pivot(hips, "tail", new Vector3(0, -size * 0.1f, size * 0.8f));
            var tailMesh = CachedBuild(key + "_tail", mb => mb.Cylinder(Vector3.zero, new Vector3(0, -size * 0.3f, size * 1.4f), size * 0.25f, 0.01f, 8, body));
            ModelFactory.Part(rig.Tail, "tail_mesh", tailMesh, mats, Vector3.zero);
            rig.WingL = ModelFactory.Pivot(hips, "wing_l", new Vector3(-size * 0.3f, size * 0.3f, -size * 0.1f));
            rig.WingR = ModelFactory.Pivot(hips, "wing_r", new Vector3(size * 0.3f, size * 0.3f, -size * 0.1f));
            ModelFactory.Part(rig.WingL, "wing_mesh", CachedBuild(key + "_wing_l", mb => Wing(mb, -1, size * 1.4f, body)), mats, Vector3.zero);
            ModelFactory.Part(rig.WingR, "wing_mesh", CachedBuild(key + "_wing_r", mb => Wing(mb, 1, size * 1.4f, body)), mats, Vector3.zero);
            var legMesh = CachedBuild(key + "_leg", mb => mb.Cylinder(Vector3.zero, new Vector3(0, -size * 0.9f, -size * 0.1f), size * 0.1f, size * 0.06f, 6, body * 0.8f));
            rig.LegL = ModelFactory.Pivot(hips, "leg_l", new Vector3(-size * 0.3f, 0, -size * 0.35f));
            rig.LegR = ModelFactory.Pivot(hips, "leg_r", new Vector3(size * 0.3f, 0, -size * 0.35f));
            rig.LegBL = ModelFactory.Pivot(hips, "leg_bl", new Vector3(-size * 0.3f, 0, size * 0.4f));
            rig.LegBR = ModelFactory.Pivot(hips, "leg_br", new Vector3(size * 0.3f, 0, size * 0.4f));
            foreach (var l in new[] { rig.LegL, rig.LegR, rig.LegBL, rig.LegBR }) ModelFactory.Part(l, "leg_mesh", legMesh, mats, Vector3.zero);
            mi.Rig = rig;
            mi.Height = size * 1.8f;
        }

        // ================================================================== siege

        private static void BuildSiege(ModelInstance mi, string key, Team team, bool catapult)
        {
            var glow = ModelFactory.TeamGlow(team);
            var mats = ModelFactory.StandardSet(glow, 3);
            var wood = team == Team.Dawn ? new Color(0.45f, 0.33f, 0.2f) : new Color(0.25f, 0.2f, 0.18f);
            var trim = ModelFactory.TeamAccent(team);
            var rig = new RigParts { Kind = RigKind.Siege, Height = 1.6f };
            var bodyMesh = CachedBuild(key + "_cart", mb =>
            {
                mb.Box(new Vector3(0, 0.55f, 0), new Vector3(1.1f, 0.3f, 1.8f), wood);
                mb.Sub(1).Box(new Vector3(0, 0.72f, -0.9f), new Vector3(1.15f, 0.08f, 0.1f), trim);
                mb.Box(new Vector3(0, 0.72f, 0.9f), new Vector3(1.15f, 0.08f, 0.1f), trim);
                mb.Sub(0);
                foreach (var z in new[] { -0.6f, 0.6f })
                foreach (var x in new[] { -0.62f, 0.62f })
                    mb.With(new Vector3(x, 0.35f, z), Quaternion.Euler(0, 0, 90), Vector3.one, () => mb.Cylinder(new Vector3(0, -0.06f, 0), new Vector3(0, 0.06f, 0), 0.35f, 0.35f, 10, wood * 0.7f, true, true));
                if (team == Team.Dusk)
                    for (int i = 0; i < 4; i++) mb.Cylinder(new Vector3(-0.5f + i * 0.33f, 0.7f, 0.9f), new Vector3(-0.55f + i * 0.36f, 1.2f, 1.05f), 0.04f, 0.004f, 4, new Color(0.85f, 0.8f, 0.7f));
            });
            ModelFactory.Part(mi.Pivot, "cart", bodyMesh, mats, Vector3.zero);
            rig.Weapon = ModelFactory.Pivot(mi.Pivot, "arm", new Vector3(0, 0.75f, 0.3f));
            var armMesh = CachedBuild(key + "_arm", mb =>
            {
                if (catapult)
                {
                    mb.Box(new Vector3(0, 0.6f, -0.2f), new Vector3(0.12f, 1.3f, 0.12f), wood);
                    mb.Sphere(new Vector3(0, 1.25f, -0.3f), new Vector3(0.25f, 0.15f, 0.25f), 8, 5, wood * 0.8f);
                    mb.Sub(2).Sphere(new Vector3(0, 1.35f, -0.3f), Vector3.one * 0.14f, 6, 4, glow);
                }
                else
                {
                    mb.Box(new Vector3(0, 0.2f, -0.3f), new Vector3(0.15f, 0.15f, 1.4f), wood);
                    mb.Sub(1).Box(new Vector3(0, 0.25f, -0.8f), new Vector3(1.3f, 0.06f, 0.08f), trim);
                    mb.Sub(2).Box(new Vector3(0, 0.3f, -0.4f), new Vector3(0.04f, 0.04f, 1.0f), glow);
                }
            });
            ModelFactory.Part(rig.Weapon, "arm_mesh", armMesh, mats, Vector3.zero);
            mi.Rig = rig;
            mi.Height = 1.7f;
            var o = new GameObject("projectile_origin").transform;
            o.SetParent(rig.Weapon, false);
            o.localPosition = catapult ? new Vector3(0, 1.3f, -0.3f) : new Vector3(0, 0.3f, -1f);
            mi.ProjectileOrigin = o;
        }

        // ================================================================== structures

        private static void BuildTower(ModelInstance mi, string key, Team team, int tier)
        {
            var glow = ModelFactory.TeamGlow(team);
            var mats = ModelFactory.StandardSet(glow, 3, 4f);
            float s = 1f + (tier - 1) * 0.1f;
            bool dawn = team == Team.Dawn;
            var mesh = CachedBuild(key, mb =>
            {
                if (dawn)
                {
                    var stone = new Color(0.78f, 0.74f, 0.66f);
                    var gold = ModelFactory.DawnAccent;
                    mb.Cylinder(Vector3.zero, new Vector3(0, 1.1f, 0), 1.9f * s, 1.7f * s, 8, stone * 0.8f);
                    mb.Cylinder(new Vector3(0, 1.1f, 0), new Vector3(0, 6.2f * s, 0), 1.25f * s, 0.95f * s, 8, stone);
                    mb.Sub(1).Cylinder(new Vector3(0, 3.4f * s, 0), new Vector3(0, 3.7f * s, 0), 1.2f * s, 1.15f * s, 8, gold);
                    mb.Sub(0).Cylinder(new Vector3(0, 6.2f * s, 0), new Vector3(0, 6.6f * s, 0), 1.4f * s, 1.4f * s, 8, stone * 1.05f);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        mb.Box(new Vector3(Mathf.Cos(a) * 1.25f * s, 6.85f * s, Mathf.Sin(a) * 1.25f * s), new Vector3(0.35f, 0.5f, 0.35f) * s, stone);
                    }
                    mb.Sub(1);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI / 2f + Mathf.PI / 4f;
                        mb.Cylinder(new Vector3(Mathf.Cos(a) * 0.9f * s, 6.6f * s, Mathf.Sin(a) * 0.9f * s), new Vector3(Mathf.Cos(a) * 0.2f * s, 8.4f * s, Mathf.Sin(a) * 0.2f * s), 0.08f * s, 0.05f * s, 5, gold);
                    }
                    mb.Sub(0);
                    // windows
                    mb.Sub(2);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI / 2f;
                        mb.Box(new Vector3(Mathf.Cos(a) * 1.08f * s, 4.8f * s, Mathf.Sin(a) * 1.08f * s), new Vector3(0.25f, 0.6f, 0.25f) * s, glow);
                    }
                    mb.Sub(0);
                }
                else
                {
                    var obsidian = new Color(0.12f, 0.09f, 0.11f);
                    var bone = new Color(0.78f, 0.72f, 0.6f);
                    mb.Cylinder(Vector3.zero, new Vector3(0, 1.0f, 0), 2.0f * s, 1.6f * s, 6, obsidian * 1.3f);
                    mb.Cylinder(new Vector3(0, 1.0f, 0), new Vector3(0, 4.5f * s, 0), 1.2f * s, 0.8f * s, 5, obsidian);
                    mb.Cylinder(new Vector3(0, 4.5f * s, 0), new Vector3(0, 7.8f * s, 0), 0.8f * s, 0.25f * s, 5, obsidian * 1.1f);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        float y = 1.5f + (i % 3) * 1.2f;
                        mb.Cylinder(new Vector3(Mathf.Cos(a) * 1.0f * s, y * s, Mathf.Sin(a) * 1.0f * s), new Vector3(Mathf.Cos(a) * 2.2f * s, (y + 1.3f) * s, Mathf.Sin(a) * 2.2f * s), 0.14f * s, 0.01f, 5, bone);
                    }
                    // claw cradle
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI / 2f;
                        mb.Cylinder(new Vector3(Mathf.Cos(a) * 0.4f * s, 6.4f * s, Mathf.Sin(a) * 0.4f * s), new Vector3(Mathf.Cos(a) * 0.9f * s, 8.3f * s, Mathf.Sin(a) * 0.9f * s), 0.12f * s, 0.02f, 5, bone);
                    }
                    mb.Sub(2);
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * Mathf.PI * 2f / 3f;
                        mb.Box(new Vector3(Mathf.Cos(a) * 0.95f * s, 3.2f * s, Mathf.Sin(a) * 0.95f * s), new Vector3(0.2f, 0.7f, 0.2f) * s, glow);
                    }
                    mb.Sub(0);
                }
            });
            ModelFactory.Part(mi.Pivot, "tower", mesh, mats, Vector3.zero);
            // Floating crystal (spins), origin of tower attacks.
            var spin = ModelFactory.Pivot(mi.Pivot, "crystal", new Vector3(0, (dawn ? 8.2f : 8.1f) * s, 0));
            var crystal = CachedBuild("crystal_" + team, mb => mb.Sub(2).Crystal(new Vector3(0, -0.6f, 0), 0.4f, 1.3f, 6, glow, 0.45f));
            ModelFactory.Part(spin, "crystal_mesh", crystal, mats, Vector3.zero);
            mi.Rig = new RigParts { Kind = RigKind.Static, Spin = spin, Height = 8.8f * s };
            mi.Height = 9f * s;
            mi.ProjectileOrigin = spin;
        }

        private static void BuildBarracks(ModelInstance mi, string key, Team team, bool ranged)
        {
            var glow = ModelFactory.TeamGlow(team);
            var mats = ModelFactory.StandardSet(glow, 3, 3f);
            bool dawn = team == Team.Dawn;
            var mesh = CachedBuild(key, mb =>
            {
                var wall = dawn ? new Color(0.76f, 0.72f, 0.64f) : new Color(0.16f, 0.12f, 0.14f);
                var roof = dawn ? new Color(0.5f, 0.16f, 0.1f) : new Color(0.3f, 0.06f, 0.08f);
                var trim = ModelFactory.TeamAccent(team);
                mb.Box(new Vector3(0, 0.3f, 0), new Vector3(5.2f, 0.6f, 4.2f), wall * 0.8f);
                mb.Box(new Vector3(0, 1.9f, 0), new Vector3(4.4f, 2.6f, 3.6f), wall);
                mb.Roof(new Vector3(0, 3.2f, 0), 4.8f, 4.0f, 1.8f, roof);
                mb.Sub(1).Box(new Vector3(0, 3.25f, -1.85f), new Vector3(4.6f, 0.15f, 0.12f), trim);
                mb.Sub(0);
                // door + glowing sigil
                mb.Box(new Vector3(0, 1.1f, -1.82f), new Vector3(1.2f, 1.8f, 0.1f), wall * 0.35f);
                mb.Sub(2).Box(new Vector3(0, 2.55f, -1.84f), new Vector3(0.5f, 0.5f, 0.05f), glow);
                mb.Sub(0);
                if (ranged)
                {
                    mb.Cylinder(new Vector3(1.9f, 0.6f, 1.4f), new Vector3(1.9f, 5.2f, 1.4f), 0.7f, 0.55f, 6, wall);
                    mb.Cylinder(new Vector3(1.9f, 5.2f, 1.4f), new Vector3(1.9f, 6.6f, 1.4f), 0.75f, 0.02f, 6, roof);
                }
                else
                {
                    mb.Sub(1);
                    mb.Box(new Vector3(-2.4f, 2.4f, -1.2f), new Vector3(0.1f, 2.4f, 0.9f), trim);
                    mb.Box(new Vector3(2.4f, 2.4f, -1.2f), new Vector3(0.1f, 2.4f, 0.9f), trim);
                    mb.Sub(0);
                }
                if (!dawn)
                    for (int i = 0; i < 5; i++)
                        mb.Cylinder(new Vector3(-2f + i, 3.4f, 0), new Vector3(-2.2f + i * 1.1f, 5.2f, 0.2f), 0.1f, 0.01f, 5, new Color(0.8f, 0.74f, 0.62f));
            });
            ModelFactory.Part(mi.Pivot, "barracks", mesh, mats, Vector3.zero);
            mi.Rig = new RigParts { Kind = RigKind.Static, Height = 5f };
            mi.Height = ranged ? 6.8f : 5.2f;
        }

        private static void BuildCore(ModelInstance mi, string key, Team team)
        {
            var glow = ModelFactory.TeamGlow(team);
            var mats = ModelFactory.StandardSet(glow, 3, 5f);
            bool dawn = team == Team.Dawn;
            var mesh = CachedBuild(key, mb =>
            {
                var stone = dawn ? new Color(0.8f, 0.76f, 0.66f) : new Color(0.14f, 0.1f, 0.12f);
                var trim = ModelFactory.TeamAccent(team);
                mb.Cylinder(Vector3.zero, new Vector3(0, 0.6f, 0), 5.2f, 5.0f, 12, stone * 0.8f);
                mb.Cylinder(new Vector3(0, 0.6f, 0), new Vector3(0, 1.2f, 0), 4.2f, 4.0f, 12, stone * 0.9f);
                mb.Sub(1).Cylinder(new Vector3(0, 1.2f, 0), new Vector3(0, 1.4f, 0), 3.4f, 3.4f, 12, trim);
                mb.Sub(0);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f;
                    var p = new Vector3(Mathf.Cos(a) * 3.8f, 1.2f, Mathf.Sin(a) * 3.8f);
                    if (dawn) mb.Cylinder(p, p + Vector3.up * 5f, 0.4f, 0.32f, 8, stone);
                    else mb.Cylinder(p, new Vector3(Mathf.Cos(a) * 1.8f, 7.5f, Mathf.Sin(a) * 1.8f), 0.45f, 0.03f, 5, new Color(0.8f, 0.74f, 0.6f));
                    mb.Sub(2).Sphere(p + Vector3.up * (dawn ? 5.2f : 0.6f), Vector3.one * 0.25f, 6, 4, glow);
                    mb.Sub(0);
                }
            });
            ModelFactory.Part(mi.Pivot, "core", mesh, mats, Vector3.zero);
            var spin = ModelFactory.Pivot(mi.Pivot, "heart", new Vector3(0, 4.6f, 0));
            var crystal = CachedBuild("core_crystal_" + team, mb =>
            {
                mb.Sub(2).Crystal(new Vector3(0, -2.2f, 0), 1.3f, 4.6f, dawn ? 8 : 6, glow, 0.5f);
            });
            ModelFactory.Part(spin, "heart_mesh", crystal, mats, Vector3.zero);
            mi.Rig = new RigParts { Kind = RigKind.Floating, Spin = spin, Height = 8f };
            mi.Height = 8.5f;
        }

        private static void BuildFountain(ModelInstance mi, string key, Team team)
        {
            var glow = ModelFactory.TeamGlow(team);
            var mats = ModelFactory.StandardSet(glow, 3, 2.5f);
            var mesh = CachedBuild(key, mb =>
            {
                var stone = team == Team.Dawn ? new Color(0.78f, 0.74f, 0.68f) : new Color(0.18f, 0.14f, 0.16f);
                mb.Cylinder(Vector3.zero, new Vector3(0, 0.7f, 0), 4.2f, 4.0f, 16, stone * 0.85f);
                mb.Sub(2).Cylinder(new Vector3(0, 0.7f, 0), new Vector3(0, 0.72f, 0), 3.6f, 3.6f, 16, glow);
                mb.Sub(0).Cylinder(new Vector3(0, 0.7f, 0), new Vector3(0, 3.2f, 0), 0.7f, 0.45f, 8, stone);
                mb.Cylinder(new Vector3(0, 3.2f, 0), new Vector3(0, 3.5f, 0), 1.3f, 1.2f, 10, stone);
                mb.Sub(2).Sphere(new Vector3(0, 3.9f, 0), Vector3.one * 0.45f, 10, 7, glow);
                mb.Sub(0);
            });
            ModelFactory.Part(mi.Pivot, "fountain", mesh, mats, Vector3.zero);
            mi.Rig = new RigParts { Kind = RigKind.Static, Height = 4.5f };
            mi.Height = 4.5f;
        }

        private static void BuildWard(ModelInstance mi, string key, Team team, bool sentry)
        {
            var glow = sentry ? new Color(0.4f, 0.7f, 1f) : new Color(1f, 0.85f, 0.35f);
            var mats = ModelFactory.StandardSet(glow, 3, 3f);
            var mesh = CachedBuild(key + team, mb =>
            {
                var wood = new Color(0.3f, 0.22f, 0.16f);
                mb.Cylinder(Vector3.zero, new Vector3(0, 1.3f, 0), 0.1f, 0.07f, 6, wood);
                mb.Sub(1).Cylinder(new Vector3(0, 1.3f, 0), new Vector3(0, 1.5f, 0), 0.18f, 0.14f, 6, ModelFactory.TeamAccent(team));
                mb.Sub(2).Sphere(new Vector3(0, 1.62f, 0), Vector3.one * 0.13f, 8, 6, glow);
            });
            ModelFactory.Part(mi.Pivot, "ward", mesh, mats, Vector3.zero);
            mi.Rig = new RigParts { Kind = RigKind.Static, Height = 1.8f };
            mi.Height = 1.9f;
        }

        // ================================================================== trees

        /// <summary>Tree variants 0-3: black pines, 4-6: dead gnarled trees. Vertex alpha = wind weight.</summary>
        public static Mesh TreeMesh(int variant)
        {
            string key = "tree_" + variant;
            if (MeshCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var rnd = new System.Random(1000 + variant * 7919);
            float R() => (float)rnd.NextDouble();
            var mb = new MeshBuilder();
            if (variant < 4)
            {
                float h = 6.5f + variant * 0.9f + R() * 1.2f;
                var bark = new Color(0.22f, 0.16f, 0.13f);
                mb.Weight = 0f;
                mb.Cylinder(Vector3.zero, new Vector3(0, h * 0.35f, 0), 0.28f, 0.22f, 7, bark, false);
                mb.Weight = 0.15f;
                mb.Cylinder(new Vector3(0, h * 0.35f, 0), new Vector3(0, h, 0), 0.22f, 0.04f, 7, bark, false);
                int tiers = 5 + variant % 2;
                for (int i = 0; i < tiers; i++)
                {
                    float t = i / (float)(tiers - 1);
                    float y = h * (0.28f + 0.62f * t);
                    float r = Mathf.Lerp(2.3f, 0.6f, t) * (0.9f + R() * 0.2f);
                    var needle = Color.Lerp(new Color(0.09f, 0.16f, 0.13f), new Color(0.16f, 0.22f, 0.16f), t) * (0.85f + R() * 0.3f);
                    mb.Weight = 0.35f + t * 0.65f;
                    mb.Cylinder(new Vector3(0, y - 0.3f, 0), new Vector3((R() - 0.5f) * 0.2f, y + h * 0.22f, (R() - 0.5f) * 0.2f), r, 0.02f, 9, needle * 0.8f, false, true, needle * 1.25f);
                }
            }
            else
            {
                var bark = new Color(0.26f, 0.22f, 0.2f);
                float h = 5f + (variant - 4) * 1.2f + R() * 1.5f;
                mb.Weight = 0f;
                mb.Cylinder(Vector3.zero, new Vector3(0, h * 0.5f, 0), 0.35f, 0.2f, 7, bark, false);
                void Branch(Vector3 from, Vector3 dir, float len, float radius, int depth)
                {
                    var to = from + dir * len;
                    mb.Weight = Mathf.Clamp01(from.y / h);
                    mb.Cylinder(from, to, radius, radius * 0.6f, 5, bark * (0.9f + depth * 0.05f), false);
                    if (depth <= 0) return;
                    int n = 2 + (R() < 0.3f ? 1 : 0);
                    for (int i = 0; i < n; i++)
                    {
                        var nd = (dir + new Vector3(R() - 0.5f, R() * 0.6f - 0.1f, R() - 0.5f) * 1.3f).normalized;
                        Branch(to, nd, len * (0.55f + R() * 0.2f), radius * 0.6f, depth - 1);
                    }
                }
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 2.1f + R();
                    Branch(new Vector3(0, h * (0.45f + i * 0.1f), 0), new Vector3(Mathf.Cos(a) * 0.7f, 0.8f, Mathf.Sin(a) * 0.7f).normalized, h * 0.35f, 0.18f, 3);
                }
            }
            var m = mb.ToMesh(key);
            MeshCache[key] = m;
            return m;
        }

        // ================================================================== props

        /// <summary>Static environment dressing. Returns null for decal-only types.</summary>
        public static GameObject Prop(string type, Transform parent, out bool emitsLight, out Color lightColor)
        {
            emitsLight = false;
            lightColor = new Color(1f, 0.55f, 0.25f);
            if (type == "blood_crack_decal") return null;
            var fire = new Color(1f, 0.55f, 0.2f);
            var stone = new Color(0.42f, 0.4f, 0.38f);
            var dark = new Color(0.18f, 0.15f, 0.16f);
            var bone = new Color(0.8f, 0.75f, 0.64f);
            var rust = new Color(0.35f, 0.24f, 0.18f);
            Color glowC = fire;
            Mesh mesh;
            switch (type)
            {
                case "brazier":
                case "bone_brazier":
                    emitsLight = true;
                    glowC = type == "bone_brazier" ? new Color(1f, 0.25f, 0.2f) : fire;
                    lightColor = glowC;
                    mesh = CachedBuild("prop_" + type, mb =>
                    {
                        var c = type == "bone_brazier" ? bone : rust;
                        mb.Cylinder(Vector3.zero, new Vector3(0, 0.15f, 0), 0.4f, 0.35f, 8, dark);
                        mb.Sub(1).Cylinder(new Vector3(0, 0.15f, 0), new Vector3(0, 1.1f, 0), 0.08f, 0.06f, 6, c);
                        mb.Cylinder(new Vector3(0, 1.1f, 0), new Vector3(0, 1.4f, 0), 0.25f, 0.45f, 10, c, false, true);
                        mb.Sub(2).Cylinder(new Vector3(0, 1.36f, 0), new Vector3(0, 1.4f, 0), 0.4f, 0.4f, 10, glowC);
                    });
                    break;
                case "ruin_wall":
                    mesh = CachedBuild("prop_ruin_wall", mb =>
                    {
                        mb.Box(new Vector3(0, 0.9f, 0), new Vector3(0.7f, 1.8f, 4f), stone);
                        mb.Box(new Vector3(0, 2.1f, -0.9f), new Vector3(0.65f, 0.7f, 1.8f), stone * 0.95f);
                        mb.Box(new Vector3(0.1f, 0.2f, 2.4f), new Vector3(0.6f, 0.4f, 0.8f), stone * 0.85f);
                    });
                    break;
                case "ruin_pillar":
                    mesh = CachedBuild("prop_ruin_pillar", mb =>
                    {
                        mb.Box(new Vector3(0, 0.2f, 0), new Vector3(1.1f, 0.4f, 1.1f), stone * 0.9f);
                        mb.Cylinder(new Vector3(0, 0.4f, 0), new Vector3(0, 3.1f, 0), 0.4f, 0.36f, 10, stone);
                        mb.Box(new Vector3(0.6f, 0.15f, 0.5f), new Vector3(0.6f, 0.3f, 0.5f), stone * 0.8f);
                    });
                    break;
                case "gravestone":
                    mesh = CachedBuild("prop_gravestone", mb =>
                    {
                        mb.Box(new Vector3(0, 0.5f, 0), new Vector3(0.6f, 1.0f, 0.15f), stone * 0.8f);
                        mb.Cylinder(new Vector3(0, 1.0f, -0.075f), new Vector3(0, 1.0f, 0.075f), 0.3f, 0.3f, 10, stone * 0.8f, true, true);
                        mb.Box(new Vector3(0, 0.03f, -0.4f), new Vector3(0.7f, 0.06f, 0.9f), new Color(0.24f, 0.2f, 0.16f));
                    });
                    break;
                case "bone_pile":
                    mesh = CachedBuild("prop_bone_pile", mb =>
                    {
                        var r = new System.Random(5);
                        for (int i = 0; i < 14; i++)
                        {
                            var a = new Vector3((float)r.NextDouble() * 1.6f - 0.8f, 0.1f + (float)r.NextDouble() * 0.3f, (float)r.NextDouble() * 1.6f - 0.8f);
                            var b = a + new Vector3((float)r.NextDouble() - 0.5f, 0.1f, (float)r.NextDouble() - 0.5f) * 1.2f;
                            mb.Cylinder(a, b, 0.05f, 0.05f, 5, bone);
                        }
                        mb.Sphere(new Vector3(0.2f, 0.25f, 0.1f), Vector3.one * 0.18f, 8, 6, bone);
                    });
                    break;
                case "pit_chain":
                    mesh = CachedBuild("prop_pit_chain", mb =>
                    {
                        mb.Sub(1).Cylinder(Vector3.zero, new Vector3(0, 3.5f, 0), 0.25f, 0.2f, 6, rust);
                        for (int i = 0; i < 8; i++)
                            mb.With(new Vector3(0, 3.3f - i * 0.1f, 0.25f + i * 0.35f), Quaternion.Euler(i % 2 == 0 ? 0 : 90, 0, 90), Vector3.one, () => mb.Cylinder(new Vector3(0, -0.02f, 0), new Vector3(0, 0.02f, 0), 0.15f, 0.15f, 8, rust * 0.8f));
                    });
                    break;
                case "watch_obelisk":
                case "rune_altar":
                case "vharoth_seal":
                    emitsLight = true;
                    glowC = type == "watch_obelisk" ? new Color(0.5f, 0.6f, 1f) : new Color(1f, 0.15f, 0.2f);
                    lightColor = glowC;
                    mesh = CachedBuild("prop_" + type, mb =>
                    {
                        if (type == "watch_obelisk")
                        {
                            mb.Box(new Vector3(0, 0.25f, 0), new Vector3(1.4f, 0.5f, 1.4f), stone * 0.8f);
                            mb.Cylinder(new Vector3(0, 0.5f, 0), new Vector3(0, 4.2f, 0), 0.5f, 0.2f, 4, dark * 1.4f);
                            mb.Sub(2).Crystal(new Vector3(0, 4.2f, 0), 0.25f, 0.8f, 4, glowC);
                        }
                        else
                        {
                            mb.Cylinder(Vector3.zero, new Vector3(0, 0.6f, 0), 1.3f, 1.2f, 8, dark * 1.5f);
                            mb.Sub(2).Cylinder(new Vector3(0, 0.6f, 0), new Vector3(0, 0.63f, 0), 0.9f, 0.9f, 8, glowC);
                            mb.Sub(0).Box(new Vector3(0, 1.4f, 0), new Vector3(0.5f, 1.6f, 0.3f), stone * 0.7f);
                        }
                    });
                    break;
                case "hanging_cage":
                    mesh = CachedBuild("prop_hanging_cage", mb =>
                    {
                        mb.Sub(1).Cylinder(Vector3.zero, new Vector3(0, 4f, 0), 0.12f, 0.1f, 6, rust);
                        mb.Cylinder(new Vector3(0, 4f, 0), new Vector3(1.4f, 4f, 0), 0.08f, 0.08f, 5, rust);
                        for (int i = 0; i < 6; i++)
                        {
                            float a = i * Mathf.PI / 3f;
                            mb.Cylinder(new Vector3(1.4f + Mathf.Cos(a) * 0.4f, 2.2f, Mathf.Sin(a) * 0.4f), new Vector3(1.4f + Mathf.Cos(a) * 0.3f, 3.4f, Mathf.Sin(a) * 0.3f), 0.03f, 0.03f, 4, rust);
                        }
                        mb.Sub(0).Sphere(new Vector3(1.4f, 2.4f, 0), new Vector3(0.2f, 0.15f, 0.2f), 6, 4, bone);
                    });
                    break;
                case "gargoyle_perch":
                case "colossus_statue":
                    mesh = CachedBuild("prop_" + type, mb =>
                    {
                        float s = type == "colossus_statue" ? 2.2f : 1f;
                        mb.Box(new Vector3(0, 0.6f * s, 0), new Vector3(1.4f, 1.2f, 1.4f) * s, stone * 0.85f);
                        mb.Sphere(new Vector3(0, 1.9f * s, 0), new Vector3(0.55f, 0.75f, 0.45f) * s, 8, 6, stone * 0.7f);
                        mb.Sphere(new Vector3(0, 2.8f * s, -0.15f * s), Vector3.one * 0.3f * s, 8, 6, stone * 0.7f);
                        mb.Cylinder(new Vector3(-0.3f * s, 2.2f * s, 0.2f * s), new Vector3(-1.4f * s, 3.1f * s, 0.5f * s), 0.12f * s, 0.02f * s, 5, stone * 0.65f);
                        mb.Cylinder(new Vector3(0.3f * s, 2.2f * s, 0.2f * s), new Vector3(1.4f * s, 3.1f * s, 0.5f * s), 0.12f * s, 0.02f * s, 5, stone * 0.65f);
                    });
                    break;
                case "ruin_arch":
                case "cathedral_ruin":
                case "crypt_entrance":
                case "vharoth_seal_door":
                    mesh = CachedBuild("prop_" + type, mb =>
                    {
                        float s = type == "cathedral_ruin" ? 2f : 1f;
                        mb.Box(new Vector3(-1.6f * s, 2f * s, 0), new Vector3(0.8f, 4f, 0.8f) * s, stone);
                        mb.Box(new Vector3(1.6f * s, 1.5f * s, 0), new Vector3(0.8f, 3f, 0.8f) * s, stone);
                        mb.Box(new Vector3(-0.4f * s, 4.2f * s, 0), new Vector3(3f, 0.6f, 0.8f) * s, stone * 0.9f);
                        if (type == "crypt_entrance" || type == "vharoth_seal_door")
                        {
                            mb.Box(new Vector3(0, 1.6f * s, 0.1f), new Vector3(2.4f, 3.2f, 0.2f) * s, dark);
                            mb.Sub(2).Box(new Vector3(0, 2.2f * s, -0.02f), new Vector3(0.5f, 0.5f, 0.05f) * s, new Color(1f, 0.1f, 0.15f));
                        }
                    });
                    if (type == "vharoth_seal_door" || type == "crypt_entrance") { emitsLight = true; lightColor = new Color(1f, 0.15f, 0.15f); }
                    glowC = new Color(1f, 0.1f, 0.15f);
                    break;
                case "burning_wagon":
                case "abandoned_trebuchet":
                    emitsLight = type == "burning_wagon";
                    mesh = CachedBuild("prop_" + type, mb =>
                    {
                        var wood = new Color(0.25f, 0.17f, 0.12f);
                        mb.Box(new Vector3(0, 0.6f, 0), new Vector3(1.6f, 0.4f, 3f), wood);
                        mb.With(new Vector3(0.85f, 0.45f, 0.9f), Quaternion.Euler(0, 0, 90), Vector3.one, () => mb.Cylinder(new Vector3(0, -0.06f, 0), new Vector3(0, 0.06f, 0), 0.45f, 0.45f, 10, wood * 0.8f, true, true));
                        mb.With(new Vector3(-0.7f, 0.3f, -0.9f), Quaternion.Euler(20, 0, 70), Vector3.one, () => mb.Cylinder(new Vector3(0, -0.06f, 0), new Vector3(0, 0.06f, 0), 0.45f, 0.45f, 10, wood * 0.8f, true, true));
                        if (type == "abandoned_trebuchet")
                        {
                            mb.Box(new Vector3(0, 2.2f, 0), new Vector3(0.2f, 3f, 0.2f), wood);
                            mb.With(new Vector3(0, 3.5f, 0), Quaternion.Euler(35, 0, 0), Vector3.one, () => mb.Box(new Vector3(0, 0, 0.8f), new Vector3(0.15f, 0.15f, 4.5f), wood * 0.9f));
                        }
                        else mb.Sub(2).Sphere(new Vector3(0, 1.1f, 0.3f), new Vector3(0.6f, 0.3f, 1f), 6, 4, fire);
                    });
                    break;
                case "titan_skeleton":
                    mesh = CachedBuild("prop_titan_skeleton", mb =>
                    {
                        mb.Cylinder(new Vector3(0, 0.5f, -4f), new Vector3(0, 0.8f, 4f), 0.5f, 0.4f, 8, bone);
                        for (int i = 0; i < 8; i++)
                        {
                            float z = -3f + i * 0.85f;
                            mb.Cylinder(new Vector3(0, 0.8f, z), new Vector3(-2.2f, 3.2f - i * 0.15f, z + 0.3f), 0.18f, 0.06f, 6, bone * 0.95f);
                            mb.Cylinder(new Vector3(0, 0.8f, z), new Vector3(2.2f, 3.2f - i * 0.15f, z + 0.3f), 0.18f, 0.06f, 6, bone * 0.95f);
                        }
                        mb.Sphere(new Vector3(0, 1.2f, -5f), new Vector3(1.1f, 1f, 1.4f), 10, 7, bone);
                        mb.Cylinder(new Vector3(-0.7f, 1.8f, -5.2f), new Vector3(-1.8f, 3.6f, -4.2f), 0.2f, 0.02f, 6, bone);
                        mb.Cylinder(new Vector3(0.7f, 1.8f, -5.2f), new Vector3(1.8f, 3.6f, -4.2f), 0.2f, 0.02f, 6, bone);
                    });
                    break;
                case "sun_shrine":
                    emitsLight = true;
                    lightColor = ModelFactory.DawnGlow;
                    glowC = ModelFactory.DawnGlow;
                    mesh = CachedBuild("prop_sun_shrine", mb =>
                    {
                        mb.Cylinder(Vector3.zero, new Vector3(0, 0.4f, 0), 2f, 1.9f, 12, new Color(0.8f, 0.76f, 0.66f));
                        mb.Sub(1).Cylinder(new Vector3(0, 0.4f, 0), new Vector3(0, 2.6f, 0), 0.25f, 0.2f, 8, ModelFactory.DawnAccent);
                        mb.Sub(2).Disc(new Vector3(0, 3.1f, 0), Vector3.back, 0.7f, 16, glowC);
                        mb.Disc(new Vector3(0, 3.1f, 0), Vector3.forward, 0.7f, 16, glowC);
                    });
                    break;
                case "ruined_bridge":
                    mesh = CachedBuild("prop_ruined_bridge", mb =>
                    {
                        mb.Box(new Vector3(0, 0.25f, 0), new Vector3(3.2f, 0.5f, 9f), stone * 0.8f);
                        mb.Box(new Vector3(-1.5f, 0.8f, -1f), new Vector3(0.3f, 0.7f, 6f), stone * 0.75f);
                        mb.Box(new Vector3(1.5f, 0.7f, 1.5f), new Vector3(0.3f, 0.5f, 4f), stone * 0.75f);
                    });
                    break;
                case "castle_backdrop":
                    mesh = CachedBuild("prop_castle_backdrop", mb =>
                    {
                        var c = new Color(0.14f, 0.11f, 0.13f);
                        mb.Box(new Vector3(0, 5f, 0), new Vector3(14f, 10f, 6f), c);
                        mb.Cylinder(new Vector3(-8f, 0, 0), new Vector3(-8f, 16f, 0), 2.4f, 2f, 8, c);
                        mb.Cylinder(new Vector3(-8f, 16f, 0), new Vector3(-8f, 22f, 0), 2.6f, 0.02f, 8, c * 0.8f);
                        mb.Cylinder(new Vector3(8f, 0, 0), new Vector3(8f, 13f, 0), 2.2f, 1.9f, 8, c);
                        mb.Cylinder(new Vector3(8f, 13f, 0), new Vector3(8f, 18f, 0), 2.4f, 0.02f, 8, c * 0.8f);
                        mb.Cylinder(new Vector3(0, 10f, 0), new Vector3(0, 26f, 0), 1.6f, 0.02f, 6, c * 0.9f);
                        mb.Sub(2);
                        for (int i = 0; i < 6; i++) mb.Box(new Vector3(-5f + i * 2f, 7f, -3.05f), new Vector3(0.4f, 0.9f, 0.1f), new Color(1f, 0.55f, 0.25f));
                    });
                    break;
                case "secret_shop":
                case "side_shop":
                case "shop_dawn":
                case "shop_dusk":
                    emitsLight = true;
                    glowC = type == "shop_dawn" ? ModelFactory.DawnGlow : type == "shop_dusk" ? ModelFactory.DuskGlow : new Color(0.6f, 0.4f, 1f);
                    lightColor = glowC;
                    mesh = CachedBuild("prop_" + type, mb =>
                    {
                        var wood = new Color(0.3f, 0.2f, 0.14f);
                        var cloth = type == "shop_dawn" ? new Color(0.6f, 0.5f, 0.3f) : type == "shop_dusk" ? new Color(0.4f, 0.06f, 0.1f) : new Color(0.25f, 0.12f, 0.35f);
                        mb.Box(new Vector3(0, 0.5f, 0), new Vector3(3f, 1f, 1.4f), wood);
                        foreach (var x in new[] { -1.4f, 1.4f })
                            mb.Cylinder(new Vector3(x, 0, -0.6f), new Vector3(x, 2.6f, -0.6f), 0.08f, 0.08f, 5, wood);
                        mb.Roof(new Vector3(0, 2.4f, 0), 3.4f, 2.2f, 0.8f, cloth);
                        mb.Sub(2).Sphere(new Vector3(0.9f, 1.25f, -0.2f), Vector3.one * 0.18f, 8, 6, glowC);
                        mb.Sphere(new Vector3(-0.7f, 1.2f, 0.1f), Vector3.one * 0.12f, 6, 4, glowC);
                    });
                    break;
                default:
                    mesh = CachedBuild("prop_default", mb => mb.Box(new Vector3(0, 0.5f, 0), Vector3.one, stone));
                    break;
            }
            var go = new GameObject(type);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            var mats = ModelFactory.StandardSet(glowC, mesh.subMeshCount, 3f);
            for (int i = 0; i < mats.Length; i++) mats[i] = ModelFactory.VertexMat(i == 1 ? 0.55f : 0.18f, i == 1 ? 0.6f : 0f);
            if (mesh.subMeshCount > 2) mats[2] = ModelFactory.GlowMat(glowC, 3f);
            r.sharedMaterials = mats;
            return go;
        }
    }
}
