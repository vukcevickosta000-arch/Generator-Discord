using System;
using System.Collections.Generic;
using Bloodfall.Data;
using Bloodfall.Protocol;
using Bloodfall.Simulation;
using UnityEngine;

namespace Bloodfall.Client.Match
{
    /// <summary>
    /// Client-side fog of war for rendering. Enemy units hidden by fog are never sent by the server, so this only
    /// shades terrain/props: it runs the shared <see cref="VisionSystem"/> (same line-of-sight, trees and high-ground
    /// rules as the server) from allied units in the latest snapshot and uploads a smoothed visibility texture.
    /// </summary>
    public sealed class FogOfWar : IDisposable
    {
        private readonly VisionSystem _vision;
        private readonly GameData _data;
        private readonly Texture2D _tex;
        private readonly Color32[] _pixels;
        private readonly float[] _current;
        private readonly float[] _explored;
        private float _timer;
        public bool Enabled = true;
        public readonly int Width, Height;
        private static readonly int FogTexId = Shader.PropertyToID("_BF_FogTex");
        private static readonly int FogParamsId = Shader.PropertyToID("_BF_FogParams");
        private static readonly int FogColorId = Shader.PropertyToID("_BF_FogColor");
        private readonly List<(System.Numerics.Vector2, float, bool)> _sources = new List<(System.Numerics.Vector2, float, bool)>();

        public Texture2D Texture => _tex;
        /// <summary>Black overlay with alpha = darkness (for the minimap): unexplored darkest, remembered dimmer, visible clear.</summary>
        public Texture2D MinimapTexture => _mini;
        private readonly Texture2D _mini;
        private readonly Color32[] _miniPixels;
        private float _miniTimer;
        private float _smoothAccum;
        private bool _miniDirty = true, _enabledShown;

        public FogOfWar(NavGrid grid, GameData data)
        {
            _data = data;
            _vision = new VisionSystem(grid);
            Width = _vision.Width;
            Height = _vision.Height;
            _tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true) { name = "FogOfWar", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _pixels = new Color32[Width * Height];
            _mini = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = "FogMinimap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _miniPixels = new Color32[Width * Height];
            _current = new float[Width * Height];
            _explored = new float[Width * Height];
            // Start fully fogged: only cells whose visibility changes are written afterwards.
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = new Color32(0, 0, 0, 255);
            _tex.SetPixels32(_pixels);
            _tex.Apply(false, false);
            Shader.SetGlobalTexture(FogTexId, _tex);
            Shader.SetGlobalVector(FogParamsId, new Vector4(1f / grid.WorldWidth, 1f / grid.WorldHeight, 1f, 0.42f));
            Shader.SetGlobalColor(FogColorId, new Color(0.62f, 0.66f, 0.85f));
        }

        public void OnTreeDestroyed() => _vision.RebuildStatic();

        public bool IsVisible(Vector3 world)
        {
            if (!Enabled) return true;
            int x = Mathf.Clamp((int)world.x, 0, Width - 1), y = Mathf.Clamp((int)world.z, 0, Height - 1);
            return _current[y * Width + x] > 0.5f;
        }

        public void Update(float dt, SnapshotFrame frame, Team team, bool isNight)
        {
            if (!Enabled || team == Team.None)
            {
                Shader.SetGlobalVector(FogParamsId, new Vector4(1f / Width, 1f / Height, 0f, 0.42f));
                _enabledShown = false;
                return;
            }
            _timer -= dt;
            if (_timer <= 0f && frame != null)
            {
                _timer = 0.1f;
                _sources.Clear();
                // The Blood Moon shrinks every unit's vision (structures keep theirs), matching the server.
                float scale = frame.VharothPhase == (byte)Bloodfall.Simulation.VharothPhase.BloodMoon ? _data.Rules.VharothBloodMoonVision : 1f;
                foreach (var e in frame.Entities)
                {
                    if (e.Team != team || e.Has(EntityFlags.Dead)) continue;
                    float r = VisionRadius(e, isNight) * (e.Has(EntityFlags.Structure) ? 1f : scale);
                    if (r <= 0f) continue;
                    bool flying = e.Has(EntityFlags.Airborne) || IsFlying(e);
                    _sources.Add((e.Position, r, flying));
                }
                int t = team == Team.Dawn ? 0 : 1;
                _vision.UpdateFromSources(t, _sources);
            }
            // Smooth towards the target for soft edges, at most 30 times a second (the texture is bilinear-filtered, so a
            // faster refresh is invisible) and with no upload at all once every cell has settled.
            _smoothAccum += dt;
            if (_smoothAccum < 1f / 30f) return;
            dt = Mathf.Min(_smoothAccum, 0.25f);
            _smoothAccum = 0f;
            int ti = team == Team.Dawn ? 0 : 1;
            var vis = _vision.Visible[ti];
            float k = 1f - Mathf.Exp(-dt * 10f);
            bool changed = false;
            for (int i = 0; i < _current.Length; i++)
            {
                float target = vis[i] > 0 ? 1f : 0f;
                float c = _current[i];
                if (c == target) continue;
                c += (target - c) * k;
                if (Mathf.Abs(target - c) < 0.004f) c = target;
                _current[i] = c;
                if (c > _explored[i]) _explored[i] = c;
                var px = new Color32((byte)(c * 255f), (byte)(_explored[i] * 255f), 0, 255);
                if (px.r != _pixels[i].r || px.g != _pixels[i].g) { _pixels[i] = px; changed = true; }
            }
            if (changed)
            {
                _tex.SetPixels32(_pixels);
                _tex.Apply(false, false);
                _miniDirty = true;
            }
            if (!_enabledShown)
            {
                _enabledShown = true;
                Shader.SetGlobalVector(FogParamsId, new Vector4(1f / Width, 1f / Height, 1f, 0.42f));
            }
            _miniTimer -= dt;
            if (_miniTimer <= 0f && _miniDirty)
            {
                _miniTimer = 0.2f;
                _miniDirty = false;
                for (int i = 0; i < _current.Length; i++)
                {
                    float dark = (1f - _current[i]) * (_explored[i] > 0.5f ? 0.45f : 0.75f);
                    _miniPixels[i] = new Color32(4, 3, 6, (byte)(dark * 255f));
                }
                _mini.SetPixels32(_miniPixels);
                _mini.Apply(false, false);
            }
        }

        private float VisionRadius(EntityState e, bool night)
        {
            if (e.DefId == null) return 8f;
            if (_data.Heroes.TryGetValue(e.DefId, out var h)) return night ? h.VisionNight : h.VisionDay;
            if (_data.Units.TryGetValue(e.DefId, out var u)) return night ? u.VisionNight : u.VisionDay;
            return 8f;
        }

        private bool IsFlying(EntityState e) => e.DefId != null && _data.Units.TryGetValue(e.DefId, out var u) && u.Flying;

        /// <summary>Disables fog shading (spectators, post-game, end of match).</summary>
        public void Reveal()
        {
            Enabled = false;
            _enabledShown = false;
            Shader.SetGlobalVector(FogParamsId, new Vector4(1f / Width, 1f / Height, 0f, 0.42f));
        }

        public void Dispose()
        {
            Shader.SetGlobalVector(FogParamsId, new Vector4(1f / Width, 1f / Height, 0f, 0.42f));
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            if (_mini != null) UnityEngine.Object.Destroy(_mini);
        }
    }
}
