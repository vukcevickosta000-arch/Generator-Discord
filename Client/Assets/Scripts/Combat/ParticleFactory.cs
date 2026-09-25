using System.Collections.Generic;
using UnityEngine;

namespace Bloodfall.Client.Combat
{
    public struct ParticleSpec
    {
        public string Texture;          // Resources/Textures/VFX/<name>
        public bool Additive;
        public int SheetX, SheetY;      // texture sheet animation (0 = none)
        public float Rate;              // emission per second
        public int Burst;               // one-off burst count
        public float LifetimeMin, LifetimeMax;
        public float SpeedMin, SpeedMax;
        public float SizeMin, SizeMax;
        public Color ColorA, ColorB;
        public Color EndColor;          // colour over lifetime end (alpha fades)
        public float Gravity;
        public ParticleSystemShapeType Shape;
        public float ShapeRadius;
        public float ShapeAngle;
        public Vector3 BoxSize;
        public bool Loop;
        public float Duration;
        public int MaxParticles;
        public float Noise;             // turbulence strength
        public float RotationSpeed;     // degrees/s random
        public bool WorldSpace;
        public float StartDelay;
        public float SizeOverLifeEnd;   // multiplier at end of life (1 = constant)
        public bool Stretch;            // stretched billboard (sparks, rain)
        public float StretchLength;
        public bool Horizontal;         // flat on the ground (rings, decals)
        public float Drag;
        public float SortingFudge;
    }

    /// <summary>
    /// Builds particle systems entirely from code (no prefab assets), so every effect is data we can tune and pool.
    /// Materials are cached per texture/blend combination (GPU batching).
    /// </summary>
    public static class ParticleFactory
    {
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        public static Material GetMaterial(string texture, bool additive)
        {
            string key = texture + (additive ? "+" : "a");
            if (Materials.TryGetValue(key, out var m) && m != null) return m;
            var shader = Shader.Find(additive ? "Bloodfall/ParticleAdd" : "Bloodfall/ParticleAlpha")
                         ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Sprites/Default");
            m = new Material(shader) { name = "VFX_" + key, enableInstancing = true };
            var tex = Resources.Load<Texture2D>("Textures/VFX/" + texture);
            if (tex != null) { m.mainTexture = tex; if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); }
            Materials[key] = m;
            return m;
        }

        public static ParticleSystem Create(string name, Transform parent, ParticleSpec s)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = s.Loop;
            main.duration = s.Duration > 0 ? s.Duration : 1f;
            main.startDelay = s.StartDelay;
            main.startLifetime = new ParticleSystem.MinMaxCurve(Mathf.Max(0.05f, s.LifetimeMin), Mathf.Max(s.LifetimeMin, s.LifetimeMax));
            main.startSpeed = new ParticleSystem.MinMaxCurve(s.SpeedMin, Mathf.Max(s.SpeedMin, s.SpeedMax));
            main.startSize = new ParticleSystem.MinMaxCurve(s.SizeMin, Mathf.Max(s.SizeMin, s.SizeMax));
            main.startColor = new ParticleSystem.MinMaxGradient(s.ColorA, s.ColorB.a > 0 ? s.ColorB : s.ColorA);
            main.gravityModifier = s.Gravity;
            main.maxParticles = s.MaxParticles > 0 ? s.MaxParticles : 200;
            main.simulationSpace = s.WorldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.playOnAwake = false;

            var em = ps.emission;
            em.rateOverTime = s.Rate;
            if (s.Burst > 0) em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)s.Burst) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = s.Shape;
            shape.radius = Mathf.Max(0.01f, s.ShapeRadius);
            shape.angle = s.ShapeAngle;
            if (s.Shape == ParticleSystemShapeType.Box) shape.scale = s.BoxSize == Vector3.zero ? Vector3.one : s.BoxSize;
            if (s.Horizontal || s.Shape == ParticleSystemShapeType.Circle) shape.rotation = new Vector3(-90f, 0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            var endCol = s.EndColor.a > 0 || s.EndColor != default ? s.EndColor : new Color(s.ColorA.r, s.ColorA.g, s.ColorA.b, 0f);
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(endCol.r, endCol.g, endCol.b), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            float endSize = s.SizeOverLifeEnd <= 0 ? 1f : s.SizeOverLifeEnd;
            if (Mathf.Abs(endSize - 1f) > 0.01f)
            {
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, endSize));
            }
            if (s.RotationSpeed > 0)
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-s.RotationSpeed * Mathf.Deg2Rad, s.RotationSpeed * Mathf.Deg2Rad);
            }
            if (s.Noise > 0)
            {
                var n = ps.noise;
                n.enabled = true;
                n.strength = s.Noise;
                n.frequency = 0.5f;
                n.scrollSpeed = 0.3f;
                n.quality = ParticleSystemNoiseQuality.Low;
            }
            if (s.Drag > 0)
            {
                var lim = ps.limitVelocityOverLifetime;
                lim.enabled = true;
                lim.drag = s.Drag;
            }
            if (s.SheetX > 1 || s.SheetY > 1)
            {
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.numTilesX = Mathf.Max(1, s.SheetX);
                tsa.numTilesY = Mathf.Max(1, s.SheetY);
                tsa.cycleCount = 3;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = GetMaterial(s.Texture ?? "soft_glow", s.Additive);
            r.renderMode = s.Stretch ? ParticleSystemRenderMode.Stretch : s.Horizontal ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
            if (s.Stretch) { r.lengthScale = s.StretchLength > 0 ? s.StretchLength : 2f; r.velocityScale = 0.04f; }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = s.SortingFudge;
            return ps;
        }
    }
}
