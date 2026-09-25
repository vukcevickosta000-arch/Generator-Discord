using System.Collections.Generic;
using Bloodfall.Client.Combat;
using UnityEngine;

namespace Bloodfall.Client.Core
{
    /// <summary>
    /// The animated scene behind every menu: the ruins of Velmoragh under a blood moon, built from parallax layers
    /// (Tools/art/generate_backdrop.py) plus live effects: drifting clouds and fog, rising embers, bat flocks,
    /// flickering castle windows and lightning with thunder. Camera sways with the mouse for depth.
    /// </summary>
    public sealed class MenuBackdrop : ITickable
    {
        private sealed class Layer
        {
            public Transform T;
            public Material Mat;
            public float Depth;
            public float CenterY;     // 0 = bottom of view, 1 = top
            public float HeightFrac;  // fraction of view height
            public float WidthFrac = 1.18f;
            public float Aspect;      // >0 = keep this width/height ratio (moon)
            public float CenterX = 0.5f;
            public float FlashResponse = 1f;
        }

        private GameObject _root;
        private Camera _cam;
        private readonly List<Layer> _layers = new List<Layer>();
        private Layer _lightningLayer;
        private Material _lightningMat;
        private ParticleSystem _bats, _embers, _fogPuffs, _ash;
        private Light _flashLight;
        private bool _active;
        private float _lightningTimer = 5f, _flash, _flashDecay, _secondStrike = -1f, _thunderAt = -1f;
        private float _batTimer = 3f;
        private Vector2 _sway, _swayTarget;
        private float _time;
        private int _lastW, _lastH;

        public bool Active => _active;
        public Camera Camera => _cam;

        public void SetActive(bool active)
        {
            if (active && _root == null) Build();
            _active = active;
            if (_root != null) _root.SetActive(active);
        }

        private void Build()
        {
            _root = new GameObject("MenuBackdrop");
            Object.DontDestroyOnLoad(_root);
            var camGo = new GameObject("BackdropCamera");
            camGo.transform.SetParent(_root.transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.02f, 0.015f, 0.03f);
            _cam.fieldOfView = 38f;
            _cam.nearClipPlane = 1f;
            _cam.farClipPlane = 1000f;
            _cam.depth = -10;
            camGo.AddComponent<AudioListener>().enabled = false;
            var settings = GameApp.Instance != null ? GameApp.Instance.Settings : new ClientSettings();
            RenderPipelineBridge.SetupCamera(_cam, settings);
            RenderPipelineBridge.CreatePostProcessing(_root.transform, settings, PostLook.Menu);

            AddLayer("sky", 900, 0.5f, 1.25f, flash: 1.2f).WidthFrac = 1.3f;
            var moon = AddLayer("moon", 850, 0.72f, 0.3f, flash: 0.2f);
            moon.Aspect = 1f;
            moon.CenterX = 0.76f;
            AddLayer("clouds_far", 700, 0.72f, 0.46f, scroll: 0.004f, flash: 1.6f);
            AddLayer("mountains", 600, 0.34f, 0.5f, flash: 0.9f);
            var castle = AddLayer("castle", 420, 0.40f, 0.9f, flash: 0.6f);
            castle.Mat.SetTexture("_EmissionTex", Tex("castle_windows"));
            castle.Mat.SetFloat("_EmissionStrength", 1.4f);
            castle.Mat.SetFloat("_Flicker", 0.35f);
            AddLayer("clouds_near", 330, 0.45f, 0.38f, scroll: -0.007f, flash: 1.2f, alpha: 0.75f);
            AddLayer("fog", 260, 0.23f, 0.3f, scroll: 0.012f, flash: 0.5f, alpha: 0.8f);
            AddLayer("ruins", 170, 0.25f, 0.5f, flash: 0.35f);
            AddLayer("fog", 120, 0.06f, 0.26f, scroll: -0.018f, flash: 0.3f, alpha: 0.65f);

            // Lightning bolt (hidden until a strike).
            _lightningMat = new Material(Shader.Find("Bloodfall/ParticleAdd") ?? Shader.Find("Sprites/Default"));
            _lightningMat.mainTexture = Tex("../VFX/lightning");
            _lightningLayer = new Layer { Depth = 650, CenterY = 0.75f, HeightFrac = 0.6f, Aspect = 0.25f, CenterX = 0.3f };
            _lightningLayer.T = Quad("lightning", _lightningMat).transform;
            _lightningLayer.T.gameObject.SetActive(false);

            var flashGo = new GameObject("LightningLight");
            flashGo.transform.SetParent(_root.transform, false);
            _flashLight = flashGo.AddComponent<Light>();
            _flashLight.type = LightType.Directional;
            _flashLight.intensity = 0;
            _flashLight.color = new Color(0.75f, 0.7f, 1f);
            _flashLight.cullingMask = 0;

            BuildParticles();
            Layout(true);
        }

        private static Texture2D Tex(string name) => Resources.Load<Texture2D>("Textures/Backdrop/" + name);

        private Layer AddLayer(string tex, float depth, float centerY, float heightFrac, float scroll = 0f, float flash = 1f, float alpha = 1f)
        {
            var shader = Shader.Find("Bloodfall/BackdropLayer") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "Backdrop_" + tex };
            mat.mainTexture = Tex(tex);
            mat.SetColor("_Color", new Color(1, 1, 1, alpha));
            mat.SetFloat("_ScrollX", scroll);
            mat.SetFloat("_FlashResponse", flash);
            // Far layers render first.
            mat.renderQueue = 3000 + (int)(1000 - depth) / 10;
            if (scroll != 0f && mat.mainTexture != null) mat.mainTexture.wrapMode = TextureWrapMode.Repeat;
            var l = new Layer { Mat = mat, Depth = depth, CenterY = centerY, HeightFrac = heightFrac, FlashResponse = flash };
            l.T = Quad(tex, mat).transform;
            _layers.Add(l);
            return l;
        }

        private GameObject Quad(string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root.transform, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        private void BuildParticles()
        {
            var fx = new GameObject("BackdropFX").transform;
            fx.SetParent(_root.transform, false);
            // Embers rising from the burning ruins in the foreground.
            _embers = ParticleFactory.Create("Embers", fx, new ParticleSpec
            {
                Texture = "ember", Additive = true, Rate = 22, LifetimeMin = 5, LifetimeMax = 9, SpeedMin = 3, SpeedMax = 8,
                SizeMin = 0.35f, SizeMax = 1.1f, ColorA = new Color(1f, 0.55f, 0.2f), ColorB = new Color(1f, 0.25f, 0.1f),
                Gravity = -0.12f, Shape = ParticleSystemShapeType.Box, BoxSize = new Vector3(260, 4, 30), Loop = true, Duration = 5,
                MaxParticles = 300, Noise = 2.5f, WorldSpace = true, SizeOverLifeEnd = 0.3f,
            });
            _embers.transform.localPosition = new Vector3(0, -55, 150);
            _embers.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            // Ash flakes drifting across.
            _ash = ParticleFactory.Create("Ash", fx, new ParticleSpec
            {
                Texture = "dot", Rate = 14, LifetimeMin = 10, LifetimeMax = 16, SpeedMin = 2, SpeedMax = 5, SizeMin = 0.15f, SizeMax = 0.4f,
                ColorA = new Color(0.8f, 0.75f, 0.75f, 0.5f), ColorB = new Color(0.5f, 0.45f, 0.45f, 0.35f), Gravity = 0.02f,
                Shape = ParticleSystemShapeType.Box, BoxSize = new Vector3(4, 120, 120), Loop = true, Duration = 5, MaxParticles = 250,
                Noise = 1.5f, WorldSpace = true, RotationSpeed = 90,
            });
            _ash.transform.localPosition = new Vector3(-150, 0, 140);
            _ash.transform.localRotation = Quaternion.Euler(0, 90, 0);
            // Low fog puffs.
            _fogPuffs = ParticleFactory.Create("FogPuffs", fx, new ParticleSpec
            {
                Texture = "fog_puff", Rate = 3, LifetimeMin = 14, LifetimeMax = 20, SpeedMin = 1.5f, SpeedMax = 3.5f, SizeMin = 40, SizeMax = 80,
                ColorA = new Color(0.55f, 0.42f, 0.5f, 0.28f), ColorB = new Color(0.4f, 0.3f, 0.38f, 0.22f),
                Shape = ParticleSystemShapeType.Box, BoxSize = new Vector3(4, 20, 80), Loop = true, Duration = 10, MaxParticles = 60,
                WorldSpace = true, RotationSpeed = 6, SizeOverLifeEnd = 1.4f,
            });
            _fogPuffs.transform.localPosition = new Vector3(-170, -45, 200);
            _fogPuffs.transform.localRotation = Quaternion.Euler(0, 90, 0);
            // Bat flocks (bursts triggered from Tick).
            _bats = ParticleFactory.Create("Bats", fx, new ParticleSpec
            {
                Texture = "bat_sheet", SheetX = 4, SheetY = 1, Rate = 0, LifetimeMin = 9, LifetimeMax = 12, SpeedMin = 22, SpeedMax = 32,
                SizeMin = 2.2f, SizeMax = 4.2f, ColorA = Color.white, ColorB = new Color(0.8f, 0.8f, 0.8f),
                Shape = ParticleSystemShapeType.Sphere, ShapeRadius = 10, Loop = false, Duration = 1, MaxParticles = 40, Noise = 6f,
                WorldSpace = true,
            });
            var tsa = _bats.textureSheetAnimation;
            tsa.cycleCount = 30;
            var main = _bats.main;
            main.startRotation = 0f;
            var vel = _bats.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.y = new ParticleSystem.MinMaxCurve(2f, 6f);
            foreach (var ps in new[] { _embers, _ash, _fogPuffs }) ps.Play();
        }

        // ------------------------------------------------------------------ layout

        private void Layout(bool force)
        {
            if (_cam == null) return;
            if (!force && Screen.width == _lastW && Screen.height == _lastH) return;
            _lastW = Screen.width;
            _lastH = Screen.height;
            float aspect = Mathf.Max(1.2f, (float)Screen.width / Mathf.Max(1, Screen.height));
            foreach (var l in _layers) Place(l, aspect);
            Place(_lightningLayer, aspect);
        }

        private void Place(Layer l, float aspect)
        {
            float viewH = 2f * l.Depth * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float viewW = viewH * aspect;
            float h = viewH * l.HeightFrac;
            float w = l.Aspect > 0 ? h * l.Aspect : viewW * l.WidthFrac;
            l.T.localPosition = new Vector3((l.CenterX - 0.5f) * viewW, (l.CenterY - 0.5f) * viewH, l.Depth);
            l.T.localScale = new Vector3(w, h, 1);
        }

        // ------------------------------------------------------------------ tick

        public void Tick(float dt)
        {
            if (!_active || _root == null) return;
            _time += dt;
            Layout(false);

            // Parallax sway: follow the mouse gently plus a slow breathing drift.
            var m = Input.InputBridge.MousePosition;
            _swayTarget = new Vector2(m.x / Mathf.Max(1, Screen.width) - 0.5f, m.y / Mathf.Max(1, Screen.height) - 0.5f);
            _sway = Vector2.Lerp(_sway, _swayTarget, 1f - Mathf.Exp(-dt * 1.5f));
            var drift = new Vector2(Mathf.Sin(_time * 0.11f), Mathf.Sin(_time * 0.07f) * 0.5f);
            _cam.transform.localPosition = new Vector3(_sway.x * 9f + drift.x * 3f, _sway.y * 4f + drift.y * 2f, 0);
            _cam.transform.localRotation = Quaternion.Euler(-_sway.y * 1.2f, _sway.x * 1.8f, 0);

            UpdateLightning(dt);

            _batTimer -= dt;
            if (_batTimer <= 0f)
            {
                _batTimer = Random.Range(9f, 18f);
                bool fromLeft = Random.value < 0.5f;
                _bats.transform.localPosition = new Vector3(fromLeft ? -190 : 190, Random.Range(-10f, 40f), Random.Range(200f, 320f));
                var main = _bats.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(22, 32);
                _bats.transform.localRotation = Quaternion.Euler(Random.Range(-8f, 4f), fromLeft ? 90 : -90, 0);
                var shape = _bats.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 12;
                shape.radius = 12;
                _bats.Emit(Random.Range(6, 14));
                if (Random.value < 0.6f) GameApp.Instance?.Audio?.Play2D("Ambience", "bats", 0.35f, Audio.AudioBus.Ambience);
            }
        }

        private void UpdateLightning(float dt)
        {
            _lightningTimer -= dt;
            if (_lightningTimer <= 0f)
            {
                _lightningTimer = Random.Range(7f, 16f);
                Strike();
                if (Random.value < 0.5f) _secondStrike = Random.Range(0.12f, 0.3f);
            }
            if (_secondStrike > 0f)
            {
                _secondStrike -= dt;
                if (_secondStrike <= 0f) { _flash = Mathf.Max(_flash, 0.7f); _flashDecay = 4f; }
            }
            if (_thunderAt > 0f)
            {
                _thunderAt -= dt;
                if (_thunderAt <= 0f) GameApp.Instance?.Audio?.Play2D("Ambience", "thunder", 0.8f, Audio.AudioBus.Ambience);
            }
            _flash = Mathf.Max(0f, _flash - dt * _flashDecay);
            float f = _flash * _flash;
            foreach (var l in _layers) l.Mat.SetFloat("_Flash", f);
            _flashLight.intensity = f * 2f;
            bool boltVisible = _flash > 0.35f;
            if (_lightningLayer.T.gameObject.activeSelf != boltVisible) _lightningLayer.T.gameObject.SetActive(boltVisible);
            if (boltVisible) _lightningMat.color = new Color(0.9f, 0.8f, 1f, Mathf.Clamp01(_flash * 1.4f));
        }

        private void Strike()
        {
            _flash = 1f;
            _flashDecay = Random.Range(2.5f, 4f);
            _lightningLayer.CenterX = Random.Range(0.08f, 0.45f);
            _lightningLayer.T.localRotation = Quaternion.Euler(0, 0, Random.Range(-8f, 8f));
            Place(_lightningLayer, Mathf.Max(1.2f, (float)Screen.width / Mathf.Max(1, Screen.height)));
            if (Random.value < 0.5f)
            {
                var s = _lightningLayer.T.localScale;
                _lightningLayer.T.localScale = new Vector3(-s.x, s.y, 1);
            }
            _thunderAt = Random.Range(0.4f, 1.6f);
        }
    }
}
