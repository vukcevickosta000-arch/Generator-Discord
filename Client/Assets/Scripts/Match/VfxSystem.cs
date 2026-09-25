using System.Collections.Generic;
using Bloodfall.Client.Combat;
using Bloodfall.Data;
using UnityEngine;

namespace Bloodfall.Client.Match
{
    /// <summary>A reusable effect definition: particle emitters plus optional flash light, ground decal and shake.</summary>
    public sealed class VfxRecipe
    {
        public readonly List<(ParticleSpec spec, Vector3 offset, bool scaleWithRadius)> Emitters = new List<(ParticleSpec, Vector3, bool)>();
        public float Duration = 1.5f;
        public Color LightColor;
        public float LightIntensity;
        public float LightRange = 6f;
        public string Decal;
        public Color DecalColor;
        public bool DecalAdditive = true;
        public float DecalSize = 3f;
        public float DecalLife = 3f;
        public float Shake;
        public bool FollowTarget;
        public float HeightFraction = 0.5f;
        public string Sound;

        public VfxRecipe Add(ParticleSpec s, Vector3 offset = default, bool scale = false) { Emitters.Add((s, offset, scale)); return this; }
    }

    /// <summary>
    /// Pooled visual effects for simulation events: impacts, casts, auras, statuses, deaths, projectiles and zones.
    /// Effect keys come from game data (ability/status "vfx" fields); unknown keys fall back to a themed default by
    /// keyword (blood, heal, stun, shadow, holy...) so new content is never invisible.
    /// </summary>
    public sealed class VfxSystem
    {
        private sealed class Instance
        {
            public string Key;
            public GameObject Go;
            public ParticleSystem[] Systems;
            public (ParticleSpec spec, bool scale)[] Specs;
            public Light Light;
            public float LightPeak;
            public float Life, Duration;
            public EntityView Follow;
            public float HeightFraction;
            public bool Loop;
        }

        private sealed class ProjectileView
        {
            public int Id;
            public GameObject Go;
            public Transform Head;
            public TrailRenderer Trail;
            public ParticleSystem Particles;
            public Vector3 Pos;
            public float Speed;
            public int TargetId;
            public Vector3 TargetPoint;
            public bool Linear;
            public float MaxLife;
            public float Life;
            public string Key;
            public bool Arrow;
        }

        private sealed class ZoneView
        {
            public int Id;
            public GameObject Decal;
            public Instance Loop;
            public float Life, Duration;
            public bool Telegraph;
            public Vector2 Center;
            public float Radius;
            public string Key;
            public Mesh DecalMesh;
            public Color Color;
        }

        private readonly MatchWorld _world;
        private readonly Transform _root;
        private readonly Dictionary<string, VfxRecipe> _recipes = new Dictionary<string, VfxRecipe>();
        private readonly Dictionary<string, Stack<Instance>> _pool = new Dictionary<string, Stack<Instance>>();
        private readonly List<Instance> _active = new List<Instance>();
        private readonly Dictionary<int, ProjectileView> _projectiles = new Dictionary<int, ProjectileView>();
        private readonly Stack<ProjectileView> _projectilePool = new Stack<ProjectileView>();
        private readonly Dictionary<int, ZoneView> _zones = new Dictionary<int, ZoneView>();
        private readonly List<(GameObject go, float life, float max, Mesh mesh, Color col)> _decals = new List<(GameObject, float, float, Mesh, Color)>();
        private Mesh _arrowMesh;
        private int _budget = 400;

        public VfxSystem(MatchWorld world, Transform parent)
        {
            _world = world;
            _root = new GameObject("VFX").transform;
            _root.SetParent(parent, false);
            BuildRecipes();
        }

        // ================================================================== recipes

        private static ParticleSpec Burst(string tex, Color a, Color b, int count, float speedMin, float speedMax, float sizeMin, float sizeMax, float life, bool additive = true, float gravity = 0f)
            => new ParticleSpec
            {
                Texture = tex, Additive = additive, Burst = count, LifetimeMin = life * 0.6f, LifetimeMax = life, SpeedMin = speedMin, SpeedMax = speedMax,
                SizeMin = sizeMin, SizeMax = sizeMax, ColorA = a, ColorB = b, Gravity = gravity, Shape = ParticleSystemShapeType.Sphere, ShapeRadius = 0.2f,
                Duration = 0.2f, MaxParticles = Mathf.Max(8, count * 2), WorldSpace = true, SizeOverLifeEnd = 0.4f,
            };

        private static ParticleSpec Flash(Color c, float size, float life = 0.25f) => new ParticleSpec
        {
            Texture = "soft_glow", Additive = true, Burst = 1, LifetimeMin = life, LifetimeMax = life, SizeMin = size, SizeMax = size, ColorA = c,
            Shape = ParticleSystemShapeType.Sphere, ShapeRadius = 0.01f, Duration = 0.1f, MaxParticles = 2, WorldSpace = true, SizeOverLifeEnd = 1.6f,
        };

        private static ParticleSpec GroundRing(string tex, Color c, float size, float life = 0.6f, float grow = 2.2f) => new ParticleSpec
        {
            Texture = tex, Additive = true, Burst = 1, LifetimeMin = life, LifetimeMax = life, SizeMin = size, SizeMax = size, ColorA = c,
            Shape = ParticleSystemShapeType.Sphere, ShapeRadius = 0.01f, Duration = 0.1f, MaxParticles = 2, WorldSpace = true, SizeOverLifeEnd = grow, Horizontal = true,
        };

        private static ParticleSpec Rising(string tex, Color a, Color b, float rate, float radius, float speed, float size, float life, bool additive = true) => new ParticleSpec
        {
            Texture = tex, Additive = additive, Rate = rate, LifetimeMin = life * 0.7f, LifetimeMax = life, SpeedMin = speed * 0.6f, SpeedMax = speed,
            SizeMin = size * 0.6f, SizeMax = size, ColorA = a, ColorB = b, Gravity = -0.1f, Shape = ParticleSystemShapeType.Circle, ShapeRadius = radius,
            Duration = 1f, Loop = true, MaxParticles = Mathf.CeilToInt(rate * life * 1.5f) + 4, WorldSpace = true, SizeOverLifeEnd = 0.3f, Noise = 0.3f,
        };

        private static ParticleSpec Sparks(Color c, int count, float speed, float life = 0.35f) => new ParticleSpec
        {
            Texture = "spark", Additive = true, Burst = count, LifetimeMin = life * 0.5f, LifetimeMax = life, SpeedMin = speed * 0.5f, SpeedMax = speed,
            SizeMin = 0.08f, SizeMax = 0.16f, ColorA = c, ColorB = Color.white, Gravity = 1.2f, Shape = ParticleSystemShapeType.Sphere, ShapeRadius = 0.1f,
            Duration = 0.1f, MaxParticles = count * 2, WorldSpace = true, Stretch = true, StretchLength = 3f,
        };

        private static ParticleSpec BloodSpray(int count, float speed) => new ParticleSpec
        {
            Texture = "blood_sheet", SheetX = 2, SheetY = 2, Additive = false, Burst = count, LifetimeMin = 0.35f, LifetimeMax = 0.7f,
            SpeedMin = speed * 0.4f, SpeedMax = speed, SizeMin = 0.15f, SizeMax = 0.4f, ColorA = new Color(0.55f, 0.02f, 0.04f), ColorB = new Color(0.35f, 0f, 0.02f),
            Gravity = 2.2f, Shape = ParticleSystemShapeType.Cone, ShapeAngle = 40f, ShapeRadius = 0.1f, Duration = 0.1f, MaxParticles = count * 2, WorldSpace = true,
        };

        private static ParticleSpec Smoke(Color c, int count, float size, float life, float speed = 1f) => new ParticleSpec
        {
            Texture = "smoke_sheet", SheetX = 4, SheetY = 4, Additive = false, Burst = count, LifetimeMin = life * 0.6f, LifetimeMax = life,
            SpeedMin = speed * 0.3f, SpeedMax = speed, SizeMin = size * 0.6f, SizeMax = size, ColorA = c, ColorB = c * 0.8f, Gravity = -0.05f,
            Shape = ParticleSystemShapeType.Sphere, ShapeRadius = 0.4f, Duration = 0.2f, MaxParticles = count * 2, WorldSpace = true, RotationSpeed = 40, SizeOverLifeEnd = 1.8f,
        };

        private VfxRecipe R(string key)
        {
            var r = new VfxRecipe();
            _recipes[key] = r;
            return r;
        }

        private void BuildRecipes()
        {
            var blood = new Color(0.9f, 0.05f, 0.08f);
            var bloodDark = new Color(0.45f, 0f, 0.03f);
            var gold = new Color(1f, 0.8f, 0.4f);
            var holy = new Color(1f, 0.9f, 0.6f);
            var shadow = new Color(0.5f, 0.25f, 0.9f);
            var heal = new Color(0.5f, 1f, 0.5f);
            var dust = new Color(0.45f, 0.4f, 0.36f, 0.6f);

            // --- generic combat
            R("hit_physical").Add(Sparks(new Color(1f, 0.8f, 0.5f), 6, 5f)).Add(Flash(new Color(1f, 0.7f, 0.4f), 0.8f, 0.12f)).Duration = 0.6f;
            R("hit_crit").Add(Sparks(new Color(1f, 0.4f, 0.2f), 14, 8f)).Add(BloodSpray(10, 5f)).Add(Flash(new Color(1f, 0.3f, 0.2f), 1.6f, 0.18f)).Duration = 0.9f;
            R("hit_magic").Add(Burst("dot", new Color(0.6f, 0.4f, 1f), new Color(1f, 0.6f, 1f), 10, 1.5f, 3f, 0.08f, 0.18f, 0.5f)).Add(Flash(new Color(0.7f, 0.4f, 1f), 1.1f, 0.15f)).Duration = 0.7f;
            R("hit_blood").Add(BloodSpray(8, 3.5f)).Add(Flash(blood, 0.9f, 0.12f)).Duration = 0.8f;
            R("hit_bone").Add(Burst("dot", new Color(0.9f, 0.85f, 0.75f), new Color(0.7f, 0.65f, 0.55f), 8, 2f, 4f, 0.05f, 0.12f, 0.6f, false, 3f)).Duration = 0.7f;
            R("miss").Add(Burst("dust", dust, dust, 4, 0.5f, 1.2f, 0.4f, 0.7f, 0.5f, false)).Duration = 0.6f;

            // --- deaths
            var dh = R("death_human"); dh.Add(BloodSpray(18, 4f)).Add(Smoke(new Color(0.3f, 0.05f, 0.06f, 0.5f), 3, 1.2f, 1.2f)); dh.Duration = 1.5f; dh.Decal = "blood_sheet"; dh.DecalColor = new Color(0.4f, 0f, 0.02f, 0.8f); dh.DecalAdditive = false; dh.DecalSize = 1.6f; dh.DecalLife = 8f;
            R("death_bone").Add(Burst("dot", new Color(0.85f, 0.8f, 0.7f), new Color(0.6f, 0.55f, 0.45f), 20, 2f, 5f, 0.06f, 0.14f, 0.9f, false, 4f)).Add(Smoke(new Color(0.5f, 0.5f, 0.45f, 0.5f), 4, 1.3f, 1.4f)).Duration = 1.6f;
            var dl = R("death_large"); dl.Add(BloodSpray(30, 6f)).Add(Smoke(new Color(0.25f, 0.05f, 0.06f, 0.6f), 6, 2.2f, 1.8f)); dl.Shake = 0.25f; dl.Duration = 2f;
            var ds = R("structure_collapse");
            ds.Add(Smoke(dust, 14, 4.5f, 3.2f, 2.5f)).Add(Burst("dot", new Color(0.6f, 0.55f, 0.5f), new Color(0.4f, 0.36f, 0.32f), 40, 4f, 10f, 0.15f, 0.4f, 1.6f, false, 6f)).Add(Flash(new Color(1f, 0.6f, 0.3f), 9f, 0.4f));
            ds.Shake = 1.1f; ds.Duration = 4f; ds.LightColor = new Color(1f, 0.55f, 0.25f); ds.LightIntensity = 6f; ds.LightRange = 18f;
            var dc = R("core_destroyed");
            dc.Add(Smoke(dust, 24, 7f, 4f, 4f)).Add(Flash(Color.white, 24f, 0.8f)).Add(GroundRing("shockwave", new Color(1f, 0.7f, 0.4f), 8f, 1.4f, 6f)).Add(Sparks(gold, 60, 18f, 1.2f));
            dc.Shake = 2.2f; dc.Duration = 6f; dc.LightColor = Color.white; dc.LightIntensity = 12f; dc.LightRange = 40f;

            // --- lifecycle
            var lv = R("level_up");
            lv.Add(Rising("spark", gold, holy, 40, 0.8f, 5f, 0.25f, 0.9f)).Add(GroundRing("rune_circle", gold, 2.5f, 1f, 1.4f)).Add(Flash(gold, 3f, 0.4f));
            lv.Duration = 1.4f; lv.FollowTarget = true; lv.HeightFraction = 0f; lv.LightColor = gold; lv.LightIntensity = 3f;
            var rs = R("respawn"); rs.Add(Rising("soft_glow", holy, gold, 30, 1f, 6f, 0.6f, 1.2f)).Add(GroundRing("ring", holy, 3f, 1f, 1.5f)); rs.Duration = 1.6f; rs.HeightFraction = 0f;
            R("spawn").Add(Smoke(new Color(0.2f, 0.15f, 0.2f, 0.6f), 5, 1.4f, 1f)).Duration = 1.2f;
            R("summon").Add(Rising("soft_glow", shadow, blood, 30, 0.8f, 3f, 0.4f, 0.8f)).Add(GroundRing("rune_circle", shadow, 2.5f, 1f, 1.3f)).Duration = 1.3f;
            R("gold_coins").Add(Burst("dot", gold, new Color(1f, 0.95f, 0.6f), 8, 2f, 4f, 0.06f, 0.12f, 0.8f, true, 3f)).Duration = 1f;
            R("deny").Add(Burst("spark", Color.white, new Color(0.8f, 0.8f, 1f), 10, 2f, 4f, 0.08f, 0.14f, 0.5f)).Duration = 0.8f;
            R("buyback").Add(Rising("spark", gold, Color.white, 60, 1.2f, 8f, 0.3f, 1f)).Add(Flash(gold, 5f, 0.5f)).Duration = 1.5f;

            // --- abilities: Vorak
            var cc = R("crimson_charge_trail"); cc.Add(Rising("smoke_sheet", new Color(0.4f, 0.02f, 0.05f, 0.7f), bloodDark, 40, 0.4f, 0.5f, 1f, 0.7f, false)).Add(Rising("ember", blood, bloodDark, 40, 0.4f, 1f, 0.15f, 0.6f)); cc.FollowTarget = true; cc.Duration = 0.9f; cc.HeightFraction = 0.2f;
            var rend = R("blood_rend_sweep"); rend.Add(GroundRing("slash_arc", blood, 5f, 0.35f, 1.3f), default, true).Add(BloodSpray(24, 7f)).Add(Flash(blood, 3f, 0.2f)); rend.Duration = 0.9f; rend.Shake = 0.2f;
            R("blood_impact").Add(BloodSpray(12, 4f)).Add(Flash(blood, 1.4f, 0.15f)).Duration = 0.8f;
            R("blood_impact_heavy").Add(BloodSpray(26, 7f)).Add(Flash(blood, 2.6f, 0.2f)).Add(GroundRing("shockwave", blood, 2f, 0.5f, 2f)).Duration = 1f;
            var wrath = R("wrath_bash"); wrath.Add(Flash(new Color(1f, 0.2f, 0.1f), 2.2f, 0.2f)).Add(GroundRing("shockwave", new Color(1f, 0.3f, 0.2f), 2f, 0.45f, 2.5f)).Add(Sparks(new Color(1f, 0.4f, 0.2f), 16, 8f)); wrath.Shake = 0.3f; wrath.Duration = 0.9f;
            var leap = R("bloodfall_leap"); leap.Add(Rising("smoke_sheet", new Color(0.35f, 0f, 0.04f, 0.8f), bloodDark, 50, 0.3f, 0.6f, 1.2f, 0.8f, false)); leap.FollowTarget = true; leap.Duration = 1.2f; leap.HeightFraction = 0.4f;
            var bfi = R("bloodfall_impact");
            bfi.Add(GroundRing("ground_crack", blood, 7f, 2.2f, 1.1f), default, true).Add(GroundRing("shockwave", new Color(1f, 0.15f, 0.1f), 3f, 0.6f, 3.2f), default, true).Add(BloodSpray(40, 10f)).Add(Smoke(new Color(0.3f, 0.02f, 0.04f, 0.8f), 10, 3f, 1.6f, 3f)).Add(Flash(blood, 7f, 0.35f));
            bfi.Shake = 1f; bfi.Duration = 2.4f; bfi.LightColor = blood; bfi.LightIntensity = 8f; bfi.LightRange = 14f; bfi.Decal = "ground_crack"; bfi.DecalColor = new Color(0.6f, 0.02f, 0.05f, 0.9f); bfi.DecalSize = 7f; bfi.DecalLife = 6f; bfi.DecalAdditive = false;
            var tithe = R("blood_tithe_mark"); tithe.Add(Rising("ember", blood, bloodDark, 10, 0.3f, 0.8f, 0.12f, 0.8f)); tithe.FollowTarget = true; tithe.HeightFraction = 1.05f; tithe.Duration = 999f;
            var feast = R("feast_heal"); feast.Add(Rising("soft_glow", new Color(1f, 0.2f, 0.25f), blood, 20, 0.4f, 2f, 0.35f, 0.7f)); feast.FollowTarget = true; feast.HeightFraction = 0.3f; feast.Duration = 0.9f;
            var ground = R("ground_slam_blood"); ground.Add(GroundRing("shockwave", blood, 3f, 0.5f, 2.5f), default, true).Add(BloodSpray(20, 6f)); ground.Shake = 0.4f; ground.Duration = 1f;
            R("cleave_arc").Add(GroundRing("slash_arc", new Color(1f, 0.85f, 0.7f), 3.5f, 0.25f, 1.2f)).Duration = 0.5f;
            var bl = R("bloodlust"); bl.Add(Rising("ember", blood, new Color(1f, 0.3f, 0.2f), 14, 0.4f, 1.5f, 0.12f, 0.6f)); bl.FollowTarget = true; bl.HeightFraction = 0.3f; bl.Duration = 999f;

            // --- abilities: Ilyra
            var lance = R("blood_lance"); lance.Add(BloodSpray(14, 5f)).Add(Flash(blood, 1.6f, 0.15f)); lance.Duration = 0.8f;
            var tele = R("hemorrhage_telegraph"); tele.Duration = 999f;
            var erupt = R("hemorrhage_eruption"); erupt.Add(BloodSpray(40, 9f), default, true).Add(GroundRing("shockwave", blood, 2f, 0.5f, 2.2f), default, true).Add(Rising("smoke_sheet", new Color(0.4f, 0f, 0.04f, 0.8f), bloodDark, 50, 2f, 3f, 1.6f, 0.9f, false), default, true); erupt.Duration = 1.6f; erupt.Shake = 0.35f;
            var pool = R("hemorrhage_pool"); pool.Add(Rising("ember", blood, bloodDark, 25, 2.5f, 0.8f, 0.14f, 1.2f), default, true).Add(Rising("smoke_sheet", new Color(0.3f, 0f, 0.03f, 0.35f), bloodDark, 6, 2.5f, 0.3f, 2f, 2f, false), default, true); pool.Duration = 999f;
            var off = R("blood_offering"); off.Add(Rising("soft_glow", blood, new Color(1f, 0.4f, 0.4f), 30, 0.5f, 2.5f, 0.4f, 0.8f)).Add(GroundRing("rune_circle", blood, 2.2f, 0.9f, 1.3f)); off.FollowTarget = true; off.HeightFraction = 0f; off.Duration = 1.2f;
            var og = R("offering_glow"); og.Add(Rising("soft_glow", new Color(1f, 0.3f, 0.35f), blood, 8, 0.4f, 1f, 0.35f, 0.8f)); og.FollowTarget = true; og.HeightFraction = 0.4f; og.Duration = 999f;
            var surge = R("blood_surge"); surge.Add(Rising("ember", new Color(1f, 0.2f, 0.25f), blood, 8, 0.35f, 1.2f, 0.1f, 0.6f)); surge.FollowTarget = true; surge.HeightFraction = 0.5f; surge.Duration = 999f;
            var beam = R("exsanguinate_beam"); beam.Add(Rising("soft_glow", blood, bloodDark, 30, 0.3f, 1f, 0.5f, 0.5f)); beam.FollowTarget = true; beam.HeightFraction = 0.6f; beam.Duration = 999f;
            var boil = R("blood_boil"); boil.Add(BloodSpray(20, 5f)).Add(Smoke(new Color(0.4f, 0f, 0.04f, 0.7f), 6, 1.6f, 1.2f)).Add(Flash(blood, 3f, 0.3f)); boil.Duration = 1.4f; boil.Shake = 0.3f;
            R("heal_blood").Add(Rising("soft_glow", new Color(1f, 0.3f, 0.35f), blood, 25, 0.5f, 2.5f, 0.35f, 0.7f)).Duration = 1f;
            R("blood_splash").Add(BloodSpray(10, 3f)).Duration = 0.7f;
            var drip = R("bleed_drip"); drip.Add(new ParticleSpec { Texture = "dot", Rate = 6, LifetimeMin = 0.4f, LifetimeMax = 0.6f, SpeedMin = 0, SpeedMax = 0.2f, SizeMin = 0.05f, SizeMax = 0.09f, ColorA = new Color(0.6f, 0f, 0.03f), Gravity = 2f, Shape = ParticleSystemShapeType.Sphere, ShapeRadius = 0.3f, Loop = true, Duration = 1, MaxParticles = 10, WorldSpace = true }); drip.FollowTarget = true; drip.HeightFraction = 0.6f; drip.Duration = 999f;

            // --- statuses & items
            var stars = R("stun_stars"); stars.Add(new ParticleSpec { Texture = "flare", Additive = true, Rate = 6, LifetimeMin = 0.6f, LifetimeMax = 0.8f, SpeedMin = 0f, SpeedMax = 0.1f, SizeMin = 0.18f, SizeMax = 0.28f, ColorA = new Color(1f, 0.9f, 0.5f), Shape = ParticleSystemShapeType.Circle, ShapeRadius = 0.35f, Loop = true, Duration = 1, MaxParticles = 8, WorldSpace = false, RotationSpeed = 180 }); stars.FollowTarget = true; stars.HeightFraction = 1.08f; stars.Duration = 999f;
            R("stun_flash").Add(Flash(new Color(1f, 0.9f, 0.6f), 1.5f, 0.15f)).Duration = 0.4f;
            var sil = R("silence_rune"); sil.Add(GroundRing("rune_circle", new Color(0.6f, 0.4f, 1f), 0.8f, 1f, 1f)); sil.FollowTarget = true; sil.HeightFraction = 1.15f; sil.Duration = 999f;
            var root = R("root_chains"); root.Add(Rising("dot", new Color(0.5f, 0.45f, 0.4f), new Color(0.3f, 0.28f, 0.25f), 12, 0.5f, 0.3f, 0.08f, 0.6f, false)).Add(GroundRing("ring_thin", new Color(0.6f, 0.5f, 0.4f), 1.5f, 1f, 1f)); root.FollowTarget = true; root.HeightFraction = 0f; root.Duration = 999f;
            var fear = R("fear_skull"); fear.Add(Rising("smoke_sheet", new Color(0.2f, 0.1f, 0.25f, 0.6f), shadow, 10, 0.3f, 0.6f, 0.6f, 0.8f, false)); fear.FollowTarget = true; fear.HeightFraction = 1.1f; fear.Duration = 999f;
            var mi = R("magic_immune"); mi.Add(Rising("soft_glow", new Color(1f, 0.85f, 0.4f), gold, 14, 0.5f, 1.2f, 0.5f, 0.8f)); mi.FollowTarget = true; mi.HeightFraction = 0.2f; mi.Duration = 999f;
            var bar = R("barrier"); bar.Add(Rising("soft_glow", new Color(0.6f, 0.8f, 1f), new Color(0.4f, 0.6f, 1f), 10, 0.6f, 0.6f, 0.6f, 1f)); bar.FollowTarget = true; bar.HeightFraction = 0.2f; bar.Duration = 999f;
            R("barrier_pulse").Add(GroundRing("ring", new Color(0.6f, 0.8f, 1f), 1.5f, 0.6f, 3f)).Duration = 0.8f;
            var bba = R("bloodbound_aura"); bba.Add(Rising("ember", blood, bloodDark, 6, 0.5f, 0.8f, 0.1f, 0.8f)); bba.FollowTarget = true; bba.HeightFraction = 0.1f; bba.Duration = 999f;
            R("drink_potion").Add(Rising("soft_glow", new Color(1f, 0.3f, 0.3f), blood, 20, 0.4f, 1.5f, 0.3f, 0.6f)).Duration = 0.8f;
            R("mana_ember").Add(Rising("ember", new Color(0.4f, 0.6f, 1f), new Color(0.6f, 0.4f, 1f), 20, 0.4f, 1.5f, 0.12f, 0.7f)).Duration = 0.9f;
            R("mend_pulse").Add(GroundRing("ring", heal, 2f, 0.7f, 3f)).Add(Rising("soft_glow", heal, holy, 20, 1f, 1.5f, 0.3f, 0.6f)).Duration = 1f;
            var ls = R("lightning_strike"); ls.Add(new ParticleSpec { Texture = "lightning", Additive = true, Burst = 1, LifetimeMin = 0.25f, LifetimeMax = 0.25f, SizeMin = 6f, SizeMax = 6f, ColorA = new Color(0.7f, 0.8f, 1f), Shape = ParticleSystemShapeType.Sphere, ShapeRadius = 0.01f, Duration = 0.1f, MaxParticles = 2, WorldSpace = true }, new Vector3(0, 3f, 0)).Add(Sparks(new Color(0.6f, 0.8f, 1f), 16, 8f)); ls.Duration = 0.6f; ls.LightColor = new Color(0.6f, 0.7f, 1f); ls.LightIntensity = 6f;
            R("shadowstep").Add(Smoke(new Color(0.15f, 0.08f, 0.2f, 0.8f), 8, 1.4f, 0.8f, 2f)).Add(Burst("spark", shadow, Color.white, 12, 2f, 5f, 0.08f, 0.14f, 0.4f)).Duration = 1f;
            R("ward_plant").Add(GroundRing("ring", gold, 1f, 0.6f, 2f)).Duration = 0.8f;
            R("waystone_arrive").Add(Rising("soft_glow", new Color(0.6f, 0.7f, 1f), holy, 40, 1f, 4f, 0.5f, 1f)).Add(GroundRing("rune_circle", new Color(0.6f, 0.7f, 1f), 3f, 1f, 1.2f)).Duration = 1.4f;
            R("corpse_consumed").Add(Smoke(new Color(0.3f, 0.05f, 0.08f, 0.7f), 6, 1.2f, 1f)).Add(BloodSpray(10, 2f)).Duration = 1.2f;
            R("telegraph").Duration = 999f;
            var zone = R("zone"); zone.Add(Rising("ember", shadow, blood, 12, 2f, 0.8f, 0.12f, 1f), default, true); zone.Duration = 999f;
            var fr = R("fountain_restoration"); fr.Add(Rising("soft_glow", new Color(0.5f, 1f, 0.6f), new Color(0.6f, 0.8f, 1f), 6, 0.4f, 1.2f, 0.25f, 0.8f)); fr.FollowTarget = true; fr.HeightFraction = 0.1f; fr.Duration = 999f;
            var cast = R("cast_generic"); cast.Add(Rising("soft_glow", new Color(1f, 0.5f, 0.5f), blood, 30, 0.2f, 1f, 0.25f, 0.4f)); cast.FollowTarget = true; cast.HeightFraction = 0.8f; cast.Duration = 0.6f;
            var tp = R("teleport_channel"); tp.Add(Rising("soft_glow", new Color(0.6f, 0.7f, 1f), holy, 30, 0.8f, 2.5f, 0.4f, 0.9f)).Add(GroundRing("rune_circle", new Color(0.6f, 0.7f, 1f), 2.4f, 3f, 1f)); tp.FollowTarget = true; tp.HeightFraction = 0f; tp.Duration = 999f;
            var ping = R("ping"); ping.Add(GroundRing("ring", new Color(1f, 0.9f, 0.4f), 1f, 0.8f, 4f)).Add(GroundRing("ring", new Color(1f, 0.9f, 0.4f), 1f, 1.2f, 5f)); ping.Duration = 1.4f; ping.HeightFraction = 0f;
            var move = R("move_marker"); move.Add(GroundRing("ring_thin", new Color(0.4f, 1f, 0.5f), 1.2f, 0.4f, 0.3f)); move.Duration = 0.5f; move.HeightFraction = 0f;
            var atk = R("attack_marker"); atk.Add(GroundRing("ring_thin", new Color(1f, 0.3f, 0.2f), 1.2f, 0.4f, 0.3f)); atk.Duration = 0.5f; atk.HeightFraction = 0f;
            var horn = R("wave_horn"); horn.Duration = 0.1f;
        }

        private VfxRecipe Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_recipes.TryGetValue(key, out var r)) return r;
            // Themed fallbacks keep new content visible until it gets a bespoke effect.
            string k = key.ToLowerInvariant();
            string fallback = k.Contains("blood") || k.Contains("bleed") ? "blood_impact"
                : k.Contains("heal") || k.Contains("mend") ? "heal_blood"
                : k.Contains("stun") ? "stun_flash"
                : k.Contains("shadow") || k.Contains("void") || k.Contains("dark") ? "shadowstep"
                : k.Contains("lightning") || k.Contains("storm") ? "lightning_strike"
                : k.Contains("summon") ? "summon"
                : "hit_magic";
            _recipes[key] = _recipes[fallback];
            return _recipes[key];
        }

        // ================================================================== play

        public bool Has(string key) => !string.IsNullOrEmpty(key) && _recipes.ContainsKey(key);

        /// <summary>Plays an effect at a world position, optionally following an entity. Returns a handle for looping effects.</summary>
        public object Play(string key, Vector3 position, EntityView follow = null, float radius = 0f, float duration = -1f)
        {
            var recipe = Resolve(key);
            if (recipe == null) return null;
            if (_active.Count > _budget) return null;
            var inst = Get(key, recipe);
            inst.Follow = recipe.FollowTarget ? follow : null;
            inst.HeightFraction = recipe.HeightFraction;
            inst.Duration = duration > 0 ? duration : recipe.Duration;
            inst.Loop = inst.Duration >= 999f;
            inst.Life = 0f;
            var pos = follow != null && recipe.FollowTarget ? follow.Point(recipe.HeightFraction) : position;
            inst.Go.transform.position = pos;
            inst.Go.SetActive(true);
            for (int i = 0; i < inst.Systems.Length; i++)
            {
                var ps = inst.Systems[i];
                if (inst.Specs[i].scale && radius > 0f)
                {
                    var shape = ps.shape;
                    if (inst.Specs[i].spec.Horizontal) { var main = ps.main; main.startSize = radius * 2f; }
                    else shape.radius = radius;
                }
                ps.Clear(true);
                ps.Play(true);
            }
            if (inst.Light != null) { inst.Light.intensity = recipe.LightIntensity; inst.Light.enabled = true; }
            if (!string.IsNullOrEmpty(recipe.Decal)) SpawnDecal(recipe, new Vector2(position.x, position.z), radius > 0 ? radius * 2f : recipe.DecalSize);
            if (recipe.Shake > 0f) _world.Camera?.Shake(recipe.Shake, position);
            _active.Add(inst);
            return inst;
        }

        public void Stop(object handle)
        {
            if (handle is Instance inst && _active.Contains(inst))
            {
                inst.Loop = false;
                inst.Duration = inst.Life;
                foreach (var ps in inst.Systems) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private Instance Get(string key, VfxRecipe recipe)
        {
            if (_pool.TryGetValue(key, out var stack) && stack.Count > 0) return stack.Pop();
            var go = new GameObject("vfx_" + key);
            go.transform.SetParent(_root, false);
            var systems = new ParticleSystem[recipe.Emitters.Count];
            var specs = new (ParticleSpec, bool)[recipe.Emitters.Count];
            for (int i = 0; i < recipe.Emitters.Count; i++)
            {
                var (spec, offset, scale) = recipe.Emitters[i];
                var ps = ParticleFactory.Create("e" + i, go.transform, spec);
                ps.transform.localPosition = offset;
                if (spec.Shape == ParticleSystemShapeType.Circle) ps.transform.localRotation = Quaternion.identity;
                systems[i] = ps;
                specs[i] = (spec, scale);
            }
            Light light = null;
            if (recipe.LightIntensity > 0f)
            {
                light = new GameObject("light").AddComponent<Light>();
                light.transform.SetParent(go.transform, false);
                light.transform.localPosition = Vector3.up;
                light.type = LightType.Point;
                light.color = recipe.LightColor;
                light.range = recipe.LightRange;
                light.shadows = LightShadows.None;
            }
            return new Instance { Key = key, Go = go, Systems = systems, Specs = specs, Light = light, LightPeak = recipe.LightIntensity };
        }

        private void Release(Instance inst)
        {
            inst.Go.SetActive(false);
            inst.Follow = null;
            if (!_pool.TryGetValue(inst.Key, out var stack)) { stack = new Stack<Instance>(); _pool[inst.Key] = stack; }
            stack.Push(inst);
        }

        private void SpawnDecal(VfxRecipe r, Vector2 center, float size)
        {
            var go = Decals.Create(_root, _world.Map, center, size, r.Decal, r.DecalColor, r.DecalAdditive, Random.Range(0f, 360f), 6);
            _decals.Add((go, 0f, r.DecalLife, go.GetComponent<MeshFilter>().sharedMesh, r.DecalColor));
        }

        // ================================================================== projectiles

        private Mesh ArrowMesh()
        {
            if (_arrowMesh != null) return _arrowMesh;
            var mb = new MeshBuilder();
            mb.Cylinder(new Vector3(0, 0, -0.45f), new Vector3(0, 0, 0.3f), 0.02f, 0.02f, 5, new Color(0.45f, 0.35f, 0.25f));
            mb.Sub(1).Cylinder(new Vector3(0, 0, 0.3f), new Vector3(0, 0, 0.48f), 0.05f, 0.001f, 5, new Color(0.7f, 0.7f, 0.72f));
            mb.Sub(0);
            mb.Quad(new Vector3(0, 0, -0.45f), new Vector3(0, 0.07f, -0.35f), new Vector3(0, 0.07f, -0.25f), new Vector3(0, 0, -0.3f), new Color(0.8f, 0.2f, 0.2f));
            _arrowMesh = mb.ToMesh("arrow");
            return _arrowMesh;
        }

        private static Color ProjectileColor(string key)
        {
            if (string.IsNullOrEmpty(key)) return new Color(1f, 0.8f, 0.5f);
            if (key.Contains("dawn")) return ModelFactory.DawnGlow;
            if (key.Contains("dusk") || key.Contains("blood")) return new Color(1f, 0.12f, 0.15f);
            if (key.Contains("necro") || key.Contains("wyrm")) return new Color(0.5f, 1f, 0.4f);
            if (key.Contains("corpse")) return new Color(0.6f, 0.9f, 0.3f);
            return new Color(1f, 0.85f, 0.6f);
        }

        public void LaunchProjectile(int id, string key, Vector3 from, int targetId, Vector3 targetPoint, float speed, bool linear)
        {
            if (_projectiles.ContainsKey(id)) return;
            var pv = _projectilePool.Count > 0 ? _projectilePool.Pop() : CreateProjectile();
            bool arrow = key != null && (key.Contains("arrow") || key.Contains("bolt_crossbow") || key.Contains("bolt_ballista"));
            pv.Id = id; pv.Key = key; pv.Pos = from; pv.Speed = Mathf.Max(1f, speed); pv.TargetId = targetId; pv.TargetPoint = targetPoint; pv.Linear = linear;
            pv.Life = 0f; pv.MaxLife = linear ? Vector3.Distance(from, targetPoint) / pv.Speed + 0.2f : 8f;
            pv.Arrow = arrow;
            var col = ProjectileColor(key);
            pv.Head.GetComponent<MeshRenderer>().enabled = arrow;
            var main = pv.Particles.main;
            main.startColor = col;
            main.startSize = arrow ? 0.25f : key != null && (key.Contains("catapult") || key.Contains("corpse")) ? 1.2f : 0.7f;
            pv.Trail.startColor = new Color(col.r, col.g, col.b, arrow ? 0.35f : 0.8f);
            pv.Trail.endColor = new Color(col.r, col.g, col.b, 0f);
            pv.Trail.widthMultiplier = arrow ? 0.08f : 0.3f;
            pv.Go.transform.position = from;
            pv.Trail.Clear();
            pv.Go.SetActive(true);
            pv.Particles.Play();
            _projectiles[id] = pv;
        }

        private ProjectileView CreateProjectile()
        {
            var go = new GameObject("projectile");
            go.transform.SetParent(_root, false);
            var head = new GameObject("head");
            head.transform.SetParent(go.transform, false);
            head.AddComponent<MeshFilter>().sharedMesh = ArrowMesh();
            head.AddComponent<MeshRenderer>().sharedMaterials = ModelFactory.StandardSet(Color.white, 2);
            var ps = ParticleFactory.Create("glow", go.transform, new ParticleSpec
            {
                Texture = "soft_glow", Additive = true, Rate = 30, LifetimeMin = 0.12f, LifetimeMax = 0.2f, SpeedMin = 0, SpeedMax = 0.2f, SizeMin = 0.7f, SizeMax = 0.8f,
                ColorA = Color.white, Shape = ParticleSystemShapeType.Sphere, ShapeRadius = 0.05f, Loop = true, Duration = 1, MaxParticles = 12, WorldSpace = false, SizeOverLifeEnd = 0.6f,
            });
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.25f;
            trail.minVertexDistance = 0.1f;
            trail.sharedMaterial = ParticleFactory.GetMaterial("beam", true);
            trail.widthCurve = AnimationCurve.Linear(0, 1, 1, 0);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new ProjectileView { Go = go, Head = head.transform, Trail = trail, Particles = ps };
        }

        public void ProjectileHit(int id)
        {
            if (!_projectiles.TryGetValue(id, out var pv)) return;
            _projectiles.Remove(id);
            RecycleProjectile(pv);
        }

        private void RecycleProjectile(ProjectileView pv)
        {
            pv.Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            pv.Go.SetActive(false);
            _projectilePool.Push(pv);
        }

        // ================================================================== zones

        public void CreateZone(int id, string key, Vector2 center, float radius, float duration, bool telegraph, Team team)
        {
            if (_zones.ContainsKey(id)) return;
            var col = telegraph ? new Color(1f, 0.15f, 0.12f, 0.8f) : new Color(0.75f, 0.05f, 0.08f, 0.7f);
            string tex = telegraph ? "aoe_circle" : key != null && key.Contains("pool") ? "rune_circle" : "aoe_circle";
            var decal = Decals.Create(_root, _world.Map, center, radius * 2f, tex, col, true, 0f, 10);
            var z = new ZoneView { Id = id, Decal = decal, DecalMesh = decal.GetComponent<MeshFilter>().sharedMesh, Life = 0f, Duration = Mathf.Max(0.1f, duration), Telegraph = telegraph, Center = center, Radius = radius, Key = key, Color = col };
            if (!telegraph)
                z.Loop = Play(key ?? "zone", _world.Map.World(center.x, center.y, 0.1f), null, radius) as Instance;
            _zones[id] = z;
        }

        public void EndZone(int id, string endKey, Vector3 at)
        {
            if (!_zones.TryGetValue(id, out var z)) return;
            _zones.Remove(id);
            if (z.Loop != null) Stop(z.Loop);
            if (z.Decal != null) Object.Destroy(z.Decal);
        }

        // ================================================================== tick

        public void Tick(float dt)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var a = _active[i];
                a.Life += dt;
                if (a.Follow != null)
                {
                    if (a.Follow.Dying || !a.Follow.Model.Root.activeInHierarchy) { a.Loop = false; if (a.Duration > a.Life + 0.5f) a.Duration = a.Life + 0.5f; foreach (var ps in a.Systems) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
                    else a.Go.transform.position = a.Follow.Point(a.HeightFraction);
                }
                if (a.Light != null) a.Light.intensity = a.LightPeak * Mathf.Clamp01(1f - a.Life / Mathf.Max(0.2f, Mathf.Min(a.Duration, 1.2f)));
                if (!a.Loop && a.Life >= a.Duration)
                {
                    bool alive = false;
                    foreach (var ps in a.Systems) if (ps.IsAlive(true)) { alive = true; break; }
                    if (!alive || a.Life > a.Duration + 3f)
                    {
                        _active.RemoveAt(i);
                        Release(a);
                    }
                    else foreach (var ps in a.Systems) if (ps.isEmitting) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            foreach (var pv in _projectiles.Values)
            {
                pv.Life += dt;
                Vector3 target = pv.TargetPoint;
                if (!pv.Linear && pv.TargetId != 0 && _world.TryGetView(pv.TargetId, out var tv) && !tv.Dying) target = tv.Point(0.55f);
                var to = target - pv.Pos;
                float step = pv.Speed * dt;
                if (to.magnitude <= step) pv.Pos = target;
                else pv.Pos += to.normalized * step;
                if (pv.Arrow && pv.Linear == false)
                {
                    // Slight ballistic arc for arrows.
                    float d = to.magnitude;
                    pv.Go.transform.position = pv.Pos + Vector3.up * Mathf.Min(1.2f, d * 0.08f);
                }
                else pv.Go.transform.position = pv.Pos;
                if (to.sqrMagnitude > 0.0001f) pv.Head.rotation = Quaternion.LookRotation(to.normalized);
            }
            // Safety: drop projectiles that never received a hit event (e.g. hidden by fog).
            List<int> dead = null;
            foreach (var pv in _projectiles.Values)
                if (pv.Life > pv.MaxLife || (pv.Linear && (pv.Pos - pv.TargetPoint).sqrMagnitude < 0.01f)) (dead ??= new List<int>()).Add(pv.Id);
            if (dead != null) foreach (var id in dead) { var pv = _projectiles[id]; _projectiles.Remove(id); RecycleProjectile(pv); }

            foreach (var z in _zones.Values)
            {
                z.Life += dt;
                if (z.Telegraph)
                {
                    // Telegraph fills in as the eruption approaches.
                    float t = Mathf.Clamp01(z.Life / z.Duration);
                    var c = z.Color;
                    c.a = 0.35f + 0.6f * t + Mathf.Sin(Time.time * 18f) * 0.1f * t;
                    Decals.Conform(z.DecalMesh, _world.Map, z.Center, z.Radius * 2f * (0.85f + 0.15f * t), 0f, c, 10);
                }
            }

            for (int i = _decals.Count - 1; i >= 0; i--)
            {
                var d = _decals[i];
                d.life += dt;
                _decals[i] = d;
                if (d.life >= d.max)
                {
                    Object.Destroy(d.go);
                    _decals.RemoveAt(i);
                }
            }
        }

        public void Clear()
        {
            foreach (var a in _active) Release(a);
            _active.Clear();
            foreach (var pv in _projectiles.Values) RecycleProjectile(pv);
            _projectiles.Clear();
            foreach (var z in _zones.Values) if (z.Decal != null) Object.Destroy(z.Decal);
            _zones.Clear();
        }
    }
}
