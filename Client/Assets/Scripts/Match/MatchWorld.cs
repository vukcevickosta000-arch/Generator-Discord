using System;
using System.Collections.Generic;
using System.Linq;
using Bloodfall.Client.Core;
using Bloodfall.Data;
using Bloodfall.Protocol;
using Bloodfall.Simulation;
using UnityEngine;

namespace Bloodfall.Client.Match
{
    /// <summary>
    /// The 3D presentation of a running match. Consumes snapshots (interpolated ~100 ms behind the newest one for
    /// smooth motion) and simulation events (animation triggers, effects, sounds). Holds no authority.
    /// </summary>
    public sealed class MatchWorld : IDisposable
    {
        public readonly MatchController Controller;
        public readonly GameData Data;
        public Transform Root { get; private set; }
        public MapRenderer Map { get; private set; }
        public MobaCamera Camera { get; private set; }
        public VfxSystem Vfx { get; private set; }
        public FogOfWar Fog { get; private set; }
        /// <summary>MOBA controls (null in RTS matches).</summary>
        public MatchInput Input { get; private set; }
        /// <summary>RTS controls (null in MOBA matches).</summary>
        public RtsInput Rts { get; private set; }
        public bool IsRts { get; private set; }
        public NavGrid Grid { get; private set; }
        public bool IsNight { get; private set; }
        public float MatchTime { get; private set; }
        public MatchResult EndResult { get; private set; }

        private readonly Dictionary<int, EntityView> _views = new Dictionary<int, EntityView>();
        private readonly List<EntityView> _viewList = new List<EntityView>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        /// <summary>Looping status effects per entity id, keyed by vfx id.</summary>
        private readonly Dictionary<int, Dictionary<string, object>> _statusFx = new Dictionary<int, Dictionary<string, object>>();
        private readonly List<string> _endedFx = new List<string>();
        private Light _sun;
        private float _nightBlend;
        private float _bloodMoonBlend;
        private bool _vharothCorpse;
        private GameObject _post;
        private bool _centeredOnSpawn;
        private readonly Dictionary<int, object> _channelFx = new Dictionary<int, object>();
        private double _renderTick;
        private int _lastLatestTick = -1;
        private ParticleSystem _weather;

        public IEnumerable<EntityView> Views => _viewList;
        public int LocalPlayerId => Controller.Client.LocalPlayerId;
        public Team LocalTeam => Controller.Client.LocalTeam;

        public MatchWorld(MatchController controller)
        {
            Controller = controller;
            Data = controller.Data;
        }

        public bool IsEnemy(Team t) => LocalTeam == Team.None ? t == Team.Dusk : t != LocalTeam;
        public bool TryGetView(int id, out EntityView v) => _views.TryGetValue(id, out v);

        // ------------------------------------------------------------------ load

        public IEnumerator<float> Load()
        {
            var mapId = Controller.Client.Welcome?.MapId ?? "map_velmoragh";
            IsRts = Data.Modes.TryGetValue(Controller.Client.Welcome?.ModeId ?? "", out var modeDef) && modeDef.Kind == GameModeKind.Rts;
            if (!Data.Maps.TryGetValue(mapId, out var mapDef)) mapDef = Data.Maps.Values.First();
            Root = new GameObject("MatchWorld").transform;
            var cells = string.IsNullOrEmpty(mapDef.Grid) ? null : NavGrid.DecodeRle(mapDef.Grid, mapDef.GridWidth * mapDef.GridHeight);
            Grid = cells != null ? new NavGrid(mapDef.GridWidth, mapDef.GridHeight, mapDef.CellSize, cells)
                                 : NavGrid.CreateOpen((int)(mapDef.Width / mapDef.CellSize), (int)(mapDef.Height / mapDef.CellSize), mapDef.CellSize);
            yield return 0.02f;
            Map = new MapRenderer(mapDef, Grid);
            var mapLoad = Map.Load(Root);
            while (mapLoad.MoveNext()) yield return mapLoad.Current * 0.85f;

            var settings = GameApp.Instance.Settings;
            Camera = new MobaCamera(Root, Map, settings);
            _post = RenderPipelineBridge.CreatePostProcessing(Root, settings, PostLook.Match);
            SetupLighting();
            Vfx = new VfxSystem(this, Root);
            Fog = new FogOfWar(Grid, Data);
            if (Controller.Client.IsSpectator) Fog.Reveal();
            if (IsRts) Rts = new RtsInput(this, settings);
            else Input = new MatchInput(this, settings);
            BuildWeather();
            // Start the camera over our base (the fountain, or the RTS start location).
            var team = LocalTeam == Team.None ? Team.Dawn : LocalTeam;
            var baseDef = mapDef.Bases.FirstOrDefault(b => b.Team == team);
            var rtsStart = mapDef.StartLocations.FirstOrDefault(s => s.Team == team);
            var start = baseDef != null ? (System.Numerics.Vector2)baseDef.Fountain
                      : rtsStart != null ? (System.Numerics.Vector2)rtsStart.Position
                      : new System.Numerics.Vector2(mapDef.Width * 0.2f, mapDef.Height * 0.2f);
            Camera.JumpTo(Map.World(start), true);
            yield return 0.95f;
            // Warm up shaders/pools by creating one hidden instance of common effects.
            Vfx.Play("hit_physical", new Vector3(-100, -100, -100));
            yield return 1f;
        }

        private void SetupLighting()
        {
            var sunGo = new GameObject("Moon");
            sunGo.transform.SetParent(Root, false);
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = 0.85f;
            _sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            RenderSettings.sun = _sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            ApplyDayNight(0f);
        }

        private void ApplyDayNight(float night, float bloodMoon = 0f)
        {
            // Day: overcast, pale gold light through ash clouds. Night: cold blue moonlight with a crimson edge.
            var dayLight = new Color(1f, 0.9f, 0.76f);
            var nightLight = new Color(0.55f, 0.62f, 0.95f);
            _sun.color = Color.Lerp(dayLight, nightLight, night);
            _sun.intensity = Mathf.Lerp(1.35f, 0.75f, night);
            _sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(52f, 64f, night), Mathf.Lerp(-35f, 20f, night), 0f);
            RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.42f, 0.4f, 0.42f), new Color(0.16f, 0.17f, 0.3f), night);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.33f, 0.28f, 0.26f), new Color(0.14f, 0.1f, 0.16f), night);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.18f, 0.14f, 0.12f), new Color(0.06f, 0.04f, 0.06f), night);
            RenderSettings.fogColor = Color.Lerp(new Color(0.2f, 0.18f, 0.2f), new Color(0.05f, 0.05f, 0.1f), night);
            RenderSettings.fogStartDistance = Mathf.Lerp(34f, 26f, night);
            RenderSettings.fogEndDistance = Mathf.Lerp(90f, 70f, night);
            if (bloodMoon > 0f)
            {
                // The Blood Moon: crimson moonlight, red haze, and the fog closes in.
                _sun.color = Color.Lerp(_sun.color, new Color(1f, 0.28f, 0.25f), bloodMoon);
                _sun.intensity = Mathf.Lerp(_sun.intensity, 0.85f, bloodMoon);
                RenderSettings.ambientSkyColor = Color.Lerp(RenderSettings.ambientSkyColor, new Color(0.3f, 0.08f, 0.1f), bloodMoon);
                RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, new Color(0.16f, 0.02f, 0.04f), bloodMoon);
                RenderSettings.fogStartDistance = Mathf.Lerp(RenderSettings.fogStartDistance, 20f, bloodMoon);
                RenderSettings.fogEndDistance = Mathf.Lerp(RenderSettings.fogEndDistance, 58f, bloodMoon);
            }
            if (Camera != null) Camera.Cam.backgroundColor = RenderSettings.fogColor;
        }

        private void BuildWeather()
        {
            // Drifting ash / embers around the camera focus (cosmetic, client-side).
            _weather = Combat.ParticleFactory.Create("Ash", Root, new Combat.ParticleSpec
            {
                Texture = "dot", Rate = 45, LifetimeMin = 5, LifetimeMax = 8, SpeedMin = 0.4f, SpeedMax = 1.2f, SizeMin = 0.05f, SizeMax = 0.12f,
                ColorA = new Color(0.75f, 0.7f, 0.7f, 0.45f), ColorB = new Color(1f, 0.5f, 0.3f, 0.5f), Gravity = 0.01f,
                Shape = ParticleSystemShapeType.Box, BoxSize = new Vector3(60, 1, 45), Loop = true, Duration = 5, MaxParticles = 500, Noise = 0.6f, WorldSpace = true,
            });
            _weather.Play();
        }

        // ------------------------------------------------------------------ frames

        public Vector3? HeroWorldPosition()
        {
            var h = Controller.LocalHero;
            if (h == null) return null;
            return _views.TryGetValue(h.Id, out var v) ? v.Position : Map.World(h.Position);
        }

        private readonly Dictionary<int, EntityState> _prevById = new Dictionary<int, EntityState>();

        public void Tick(float dt)
        {
            var client = Controller.Client;
            var frames = client.Frames;
            if (frames.Count == 0) return;
            var latest = frames[frames.Count - 1];
            MatchTime = latest.Time;

            // Interpolation clock: aim ~3 ticks behind the newest snapshot, adapting smoothly to jitter.
            float tickRate = Mathf.Max(1, client.Welcome?.TickRate ?? 30);
            double target = latest.Tick + (client.ClockSeconds - client.LatestFrameReceivedAt) * tickRate - 3.0;
            if (_lastLatestTick < 0 || Math.Abs(target - _renderTick) > tickRate) _renderTick = target;
            else _renderTick += (dt * tickRate) + (target - _renderTick) * Math.Min(1.0, dt * 2.0);
            _lastLatestTick = latest.Tick;

            SnapshotFrame a = frames[0], b = latest;
            for (int i = 0; i < frames.Count - 1; i++)
            {
                if (frames[i].Tick <= _renderTick && frames[i + 1].Tick >= _renderTick) { a = frames[i]; b = frames[i + 1]; break; }
                if (frames[i + 1].Tick <= _renderTick) { a = frames[i + 1]; b = frames[i + 1]; }
            }
            float t = b.Tick == a.Tick ? 1f : Mathf.Clamp01((float)((_renderTick - a.Tick) / (b.Tick - a.Tick)));

            var prevByid = _prevById;
            prevByid.Clear();
            foreach (var e in a.Entities) prevByid[e.Id] = e;
            _seen.Clear();
            foreach (var e in b.Entities)
            {
                _seen.Add(e.Id);
                if (!_views.TryGetValue(e.Id, out var v))
                {
                    v = new EntityView(e, Data, Root);
                    _views[e.Id] = v;
                    _viewList.Add(v);
                    v.Position = WorldPos(e);
                    v.FacingDeg = FacingToYaw(e.Facing);
                }
                else if (v.NeedsModelSwap(e) && !v.Dying)
                {
                    var pos = v.Position;
                    v.Destroy();
                    _viewList.Remove(v);
                    v = new EntityView(e, Data, Root) { Position = pos, FadeIn = 1f };
                    _views[e.Id] = v;
                    _viewList.Add(v);
                }
                v.Present = true;
                v.Remembered = false;
                if (!v.Model.Root.activeSelf && !e.Has(EntityFlags.Dead)) { v.Model.Root.SetActive(true); v.FadeIn = 0f; v.Position = WorldPos(e); }
                if (v.Dying && !e.Has(EntityFlags.Dead)) { /* respawned hero */ ResetDeath(v, e); }
                var from = prevByid.TryGetValue(e.Id, out var pe) ? pe : e;
                var p = System.Numerics.Vector2.Lerp(from.Position, e.Position, t);
                float h = Mathf.Lerp(from.Height, e.Height, t);
                var target3 = Map.World(p, h + (v.IsFlying ? 1.4f : 0f));
                // Teleports/blinks: snap instead of sliding across the map.
                if ((target3 - v.Position).sqrMagnitude > 64f) v.Position = target3;
                else v.Position = target3;
                v.FacingDeg = Mathf.LerpAngle(FacingToYaw(from.Facing), FacingToYaw(e.Facing), t);
                v.ApplyState(e, Data);
                if (e.Has(EntityFlags.Dead) && !v.Dying) v.BeginDeath();
                UpdateStatusEffects(v, e);
            }
            // Entities that left the snapshot: dead ones play out their death; others (fog, removal) fade away.
            for (int i = _viewList.Count - 1; i >= 0; i--)
            {
                var v = _viewList[i];
                if (_seen.Contains(v.Id)) continue;
                v.Present = false;
                if (IsRts && v.Kind == UnitKind.Building && IsEnemy(v.Team) && !v.Dying)
                {
                    // RTS fog: an enemy building stays where it was last seen until its ground is scouted again.
                    if (!v.Remembered && v.Model.Root.activeSelf) v.Remembered = true;
                    else if (v.Remembered && Fog.IsVisible(v.Position)) { v.Remembered = false; v.Model.Root.SetActive(false); }
                    continue;
                }
                if (!v.Dying && v.Model.Root.activeSelf)
                {
                    bool removed = v.State != null && (v.Kind == UnitKind.Creep || v.Kind == UnitKind.Neutral || v.Kind == UnitKind.Summon || v.Kind == UnitKind.Ward) && v.State.Hp <= 0.5f;
                    if (removed) v.BeginDeath();
                    else v.Model.Root.SetActive(false);
                }
            }

            foreach (var v in _viewList)
            {
                // One broken view (bad model, missing clip) must not freeze every other unit on screen.
                try
                {
                    v.Model.Root.transform.SetPositionAndRotation(v.Position + v.FlinchOffset - Vector3.up * v.ConstructionSink, Quaternion.Euler(0, v.FacingDeg, 0));
                    v.Tick(dt, this);
                }
                catch (Exception e) { Faults.Report("view " + v.DefId, e); }
            }
            for (int i = _viewList.Count - 1; i >= 0; i--)
            {
                var v = _viewList[i];
                if (v.DeathFinished && !(v.IsHero && _seen.Contains(v.Id)))
                {
                    RemoveStatusEffects(v.Id);
                    v.Destroy();
                    _views.Remove(v.Id);
                    _viewList.RemoveAt(i);
                }
                else if (v.DeathFinished && v.IsHero) v.Model.Root.SetActive(false);
            }

            // Day / night transition.
            if (latest.IsNight != IsNight) IsNight = latest.IsNight;
            _nightBlend = Mathf.MoveTowards(_nightBlend, IsNight ? 1f : 0f, dt / 6f);
            _bloodMoonBlend = Mathf.MoveTowards(_bloodMoonBlend, latest.VharothPhase == (byte)VharothPhase.BloodMoon ? 1f : 0f, dt / 6f);
            ApplyDayNight(_nightBlend, _bloodMoonBlend);
            if (!_vharothCorpse && latest.VharothPhase == (byte)VharothPhase.Slain) PlaceVharothCorpse();

            if (EndResult == null) Fog.Update(dt, latest, LocalTeam, IsNight);
            Vfx.Tick(dt);
            Map.Render();
            Map.AnimateLights(Time.time);
            if (_weather != null) _weather.transform.position = Camera.Focus + Vector3.up * 14f;

            if (!_centeredOnSpawn && Controller.LocalHero != null && _views.TryGetValue(Controller.LocalHero.Id, out var hv))
            {
                _centeredOnSpawn = true;
                Camera.JumpTo(hv.Position, true);
            }
            if (!_centeredOnSpawn && IsRts)
            {
                // RTS: centre on our hall and select it, so the first thing on screen is a working base.
                foreach (var v in _viewList)
                {
                    if (v.Kind != UnitKind.Building || v.State?.OwnerPlayer != LocalPlayerId || v.Unit == null || !v.Unit.DropOffGold) continue;
                    _centeredOnSpawn = true;
                    Camera.JumpTo(v.Position, true);
                    Rts?.Select(new[] { v.Id });
                    break;
                }
            }
        }

        private void ResetDeath(EntityView v, EntityState e)
        {
            // Hero respawn: rebuild the view cleanly at the fountain.
            v.Destroy();
            _viewList.Remove(v);
            var nv = new EntityView(e, Data, Root) { Position = WorldPos(e) };
            _views[e.Id] = nv;
            _viewList.Add(nv);
        }

        public void LateTick(float dt)
        {
            var ui = GameApp.Instance.UI;
            bool overUi = ui.PointerOverUI();
            var hero = HeroWorldPosition();
            bool typing = (Input != null && Input.KeyboardCaptured) || (Rts != null && Rts.KeyboardCaptured);
            bool centerHeld = Input != null && !typing && Bloodfall.Client.Input.InputBridge.GetKey(KeyBinds.Get(GameApp.Instance.Settings, KeyBinds.CenterHero));
            bool inputEnabled = !typing;
            Camera.Update(dt, inputEnabled, hero, centerHeld);
            if (EndResult == null && Controller.Phase != MatchPhase.PostGame)
            {
                Input?.Update(dt, overUi);
                Rts?.Update(dt, overUi);
            }
            CameraMoved?.Invoke(dt);
        }

        /// <summary>
        /// Raised every frame once the camera has its final transform. Anything projected to the screen (health bars,
        /// combat text) must be placed here, not in Update, or it trails one frame behind while the camera moves.
        /// </summary>
        public event System.Action<float> CameraMoved;

        private Vector3 WorldPos(EntityState e) => Map.World(e.Position, e.Height);

        public static float FacingToYaw(float facingRad) => 90f - facingRad * Mathf.Rad2Deg;

        // ------------------------------------------------------------------ statuses

        private void UpdateStatusEffects(EntityView v, EntityState e)
        {
            if (v.Dying) return;
            // Stun stars from the flag; other statuses from their data-driven vfx.
            SetStatusFx(v, "stun_stars", e.Has(EntityFlags.Stunned));
            SetStatusFx(v, "magic_immune", e.Has(EntityFlags.MagicImmune));
            foreach (var s in e.Statuses)
            {
                if (s.Id == null || !Data.Statuses.TryGetValue(s.Id, out var def) || string.IsNullOrEmpty(def.Vfx) || def.Vfx == "stun_stars" || def.Vfx == "magic_immune") continue;
                SetStatusFx(v, def.Vfx, true);
            }
            // Remove effects whose status ended (only this entity's own effects are scanned).
            if (!_statusFx.TryGetValue(v.Id, out var mine) || mine.Count == 0) return;
            _endedFx.Clear();
            foreach (var key in mine.Keys)
            {
                if (key == "stun_stars" || key == "magic_immune") continue;
                bool still = false;
                foreach (var s in e.Statuses)
                    if (s.Id != null && Data.Statuses.TryGetValue(s.Id, out var d) && d.Vfx == key) { still = true; break; }
                if (!still) _endedFx.Add(key);
            }
            foreach (var k in _endedFx) { Vfx.Stop(mine[k]); mine.Remove(k); }
        }

        private void SetStatusFx(EntityView v, string key, bool on)
        {
            _statusFx.TryGetValue(v.Id, out var mine);
            bool has = mine != null && mine.ContainsKey(key);
            if (on && !has)
            {
                var h = Vfx.Play(key, v.Point(0.5f), v, 0f, 999f);
                if (h == null) return;
                if (mine == null) _statusFx[v.Id] = mine = new Dictionary<string, object>();
                mine[key] = h;
            }
            else if (!on && has)
            {
                Vfx.Stop(mine[key]);
                mine.Remove(key);
            }
        }

        private void RemoveStatusEffects(int id)
        {
            if (_statusFx.TryGetValue(id, out var mine))
            {
                foreach (var h in mine.Values) Vfx.Stop(h);
                _statusFx.Remove(id);
            }
            if (_channelFx.TryGetValue(id, out var c)) { Vfx.Stop(c); _channelFx.Remove(id); }
        }

        // ------------------------------------------------------------------ events

        private Vector3 EventPoint(NetEvent e, int unitId, float frac = 0.5f)
        {
            if (unitId != 0 && _views.TryGetValue(unitId, out var v)) return v.Point(frac);
            return Map.World(e.Point, 0.8f);
        }

        private string SfxFor(int unitId, bool attack)
        {
            if (!_views.TryGetValue(unitId, out var v)) return null;
            if (attack) return v.Hero?.AttackSfx ?? v.Unit?.AttackSfx;
            return v.Unit?.DeathSfx ?? (v.IsHero ? "death_human" : null);
        }

        public void HandleEvent(NetEvent e)
        {
            var audio = GameApp.Instance.Audio;
            switch (e.Type)
            {
                case SimEventType.AttackStart:
                {
                    if (_views.TryGetValue(e.UnitId, out var v) && v.Hero == null && v.Unit != null && v.Unit.AttackType == AttackType.Melee) { }
                    break;
                }
                case SimEventType.AttackLanded:
                {
                    if (!_views.TryGetValue(e.OtherId, out var target)) break;
                    target.HitFlash = 1f;
                    bool crit = (e.Flags & SimEvent.FlagCrit) != 0;
                    _views.TryGetValue(e.UnitId, out var attacker);
                    if (attacker != null)
                    {
                        // Weight: the target recoils from the blow (more for crits and melee), melee swings leave an arc.
                        bool melee = attacker.Hero != null ? attacker.Hero.AttackType == AttackType.Melee : attacker.Unit != null && attacker.Unit.AttackType == AttackType.Melee;
                        target.Flinch(attacker.Position, crit ? 0.2f : melee ? 0.09f : 0.05f);
                        if (melee && (attacker.IsHero || crit)) Vfx.Play(crit ? "melee_swing_heavy" : "melee_swing", target.Point(0.55f));
                        var me = Controller.LocalHero;
                        if (crit && me != null && (attacker.Id == me.Id || target.Id == me.Id))
                        {
                            attacker.HitStop = target.HitStop = 0.08f;
                            Camera.Shake(0.18f, target.Position);
                        }
                    }
                    string hitFx = crit ? "hit_crit" : target.Unit != null && target.Unit.Tags != null && Array.IndexOf(target.Unit.Tags, "undead") >= 0 ? "hit_bone" : target.IsStructure ? "hit_physical" : "hit_blood";
                    Vfx.Play(hitFx, target.Point(0.55f));
                    var sfx = SfxFor(e.UnitId, true);
                    if (sfx != null) audio.PlaySfx(sfx, target.Position);
                    break;
                }
                case SimEventType.ProjectileLaunch:
                {
                    Vector3 from = _views.TryGetValue(e.UnitId, out var src) && src.Model.ProjectileOrigin != null ? src.Model.ProjectileOrigin.position : Map.World(e.Point, 1.2f);
                    bool linear = (e.Flags & 1) != 0;
                    var to = Map.World(e.Point2, 1f);
                    Vfx.LaunchProjectile((int)e.Value2, e.Key, from, linear ? 0 : e.OtherId, to, e.Value, linear);
                    if (src != null) { var s = SfxFor(e.UnitId, true); if (s != null) audio.PlaySfx(s, src.Position, 0.8f); }
                    break;
                }
                case SimEventType.ProjectileHit:
                    Vfx.ProjectileHit((int)e.Value);
                    if (_views.TryGetValue(e.UnitId, out var hit)) { hit.HitFlash = 0.8f; Vfx.Play(e.Key != null && e.Key.Contains("blood") ? "blood_impact" : "hit_magic", hit.Point(0.55f)); }
                    break;
                case SimEventType.Damage:
                    if (_views.TryGetValue(e.UnitId, out var dv))
                    {
                        dv.HitFlash = Mathf.Max(dv.HitFlash, 0.6f);
                        // Our own hero taking a big hit: a shake scaled by the share of health it cost.
                        if (dv.Id == Controller.LocalHero?.Id && dv.State != null && dv.State.MaxHp > 0)
                        {
                            float share = e.Value / dv.State.MaxHp;
                            if (share >= 0.08f) Camera.Shake(Mathf.Min(0.5f, 0.1f + share * 1.2f), dv.Position);
                        }
                    }
                    if (!string.IsNullOrEmpty(e.Key)) Vfx.Play(e.Key, EventPoint(e, e.UnitId));
                    break;
                case SimEventType.Miss:
                    Vfx.Play("miss", EventPoint(e, e.UnitId, 0.3f));
                    break;
                case SimEventType.CastStart:
                {
                    if (!Data.Abilities.TryGetValue(e.Key ?? "", out var ab)) break;
                    if (_views.TryGetValue(e.UnitId, out var caster))
                    {
                        if (!string.IsNullOrEmpty(ab.CastVfx)) Vfx.Play(ab.CastVfx, caster.Point(0.5f), caster);
                        else Vfx.Play("cast_generic", caster.Point(0.8f), caster);
                        if (!string.IsNullOrEmpty(ab.CastSfx)) audio.PlaySfx(ab.CastSfx, caster.Position);
                        if (!string.IsNullOrEmpty(ab.VoiceLine) && caster.State?.OwnerPlayer == LocalPlayerId) audio.Play2D("Voice", ab.VoiceLine, 0.9f, Audio.AudioBus.Voice);
                    }
                    break;
                }
                case SimEventType.CastComplete:
                    break;
                case SimEventType.ChannelStart:
                    if (_views.TryGetValue(e.UnitId, out var chv))
                    {
                        string key = ChannelVfx(e.Key);
                        var h = Vfx.Play(key, chv.Point(0.6f), chv, 0f, 999f);
                        if (h != null) _channelFx[e.UnitId] = h;
                    }
                    break;
                case SimEventType.ChannelEnd:
                    if (_channelFx.TryGetValue(e.UnitId, out var ch)) { Vfx.Stop(ch); _channelFx.Remove(e.UnitId); }
                    break;
                case SimEventType.EffectVisual:
                    if (!string.IsNullOrEmpty(e.Key))
                    {
                        _views.TryGetValue(e.UnitId, out var ev);
                        Vfx.Play(e.Key, ev != null ? ev.Point(0.5f) : Map.World(e.Point, 0.1f), ev, e.Value);
                    }
                    break;
                case SimEventType.Heal:
                    if (_views.TryGetValue(e.UnitId, out var healed) && e.Value >= 20f) Vfx.Play("heal_blood", healed.Point(0.4f));
                    break;
                case SimEventType.Death:
                {
                    if (!_views.TryGetValue(e.UnitId, out var dead)) break;
                    dead.BeginDeath();
                    string sfx = SfxFor(e.UnitId, false);
                    string fx = dead.IsStructure ? "structure_collapse" : sfx == "death_bone" ? "death_bone" : sfx == "death_large" || dead.IsHero ? "death_large" : "death_human";
                    Vfx.Play(fx, dead.Point(0.4f));
                    if (sfx != null) audio.PlaySfx(sfx, dead.Position);
                    if ((e.Flags & SimEvent.FlagDeny) != 0) Vfx.Play("deny", dead.Point(1.1f));
                    RemoveStatusEffects(dead.Id);
                    break;
                }
                case SimEventType.StructureDestroyed:
                {
                    var p = Map.World(e.Point);
                    Vfx.Play(e.Key != null && e.Key.StartsWith("core") ? "core_destroyed" : "structure_collapse", p + Vector3.up * 2f);
                    audio.PlaySfx(e.Key != null && e.Key.StartsWith("core") ? "core_destroyed" : "structure_collapse", p, 1f, 0.02f);
                    break;
                }
                case SimEventType.LastHitGold:
                    Vfx.Play("gold_coins", Map.World(e.Point, 1.2f));
                    audio.Play2D("Sfx", "gold", 0.6f);
                    break;
                case SimEventType.LevelUp:
                    if (_views.TryGetValue(e.UnitId, out var lv)) { Vfx.Play("level_up", lv.Position, lv); audio.PlaySfx("level_up", lv.Position, 0.9f, 0f); }
                    break;
                case SimEventType.Respawn:
                    Vfx.Play("respawn", Map.World(e.Point));
                    // Follow mode: back on the hero when it respawns.
                    if (GameApp.Instance.Settings.CameraFollowHero && e.UnitId == Controller.LocalHero?.Id) Camera.Locked = true;
                    break;
                case SimEventType.Blink:
                    Vfx.Play(string.IsNullOrEmpty(e.Key) ? "shadowstep" : e.Key, Map.World(e.Point, 0.8f));
                    Vfx.Play(string.IsNullOrEmpty(e.Key) ? "shadowstep" : e.Key, Map.World(e.Point2, 0.8f));
                    break;
                case SimEventType.DashStart:
                    if (_views.TryGetValue(e.UnitId, out var dash) && !string.IsNullOrEmpty(e.Key)) Vfx.Play(e.Key, dash.Point(0.3f), dash, 0f, Mathf.Max(0.3f, e.Value + 0.3f));
                    break;
                case SimEventType.DashEnd:
                    if (!string.IsNullOrEmpty(e.Key) && Vfx.Has(e.Key.Replace("_leap", "_impact"))) Vfx.Play(e.Key.Replace("_leap", "_impact"), Map.World(e.Point, 0.1f));
                    break;
                case SimEventType.ZoneCreated:
                {
                    bool telegraph = e.Key == "telegraph" || (e.Key != null && e.Key.Contains("telegraph"));
                    Vfx.CreateZone(e.OtherId, e.Key, new Vector2(e.Point.X, e.Point.Y), e.Value2, e.Value, telegraph, e.Team);
                    break;
                }
                case SimEventType.ZoneEnded:
                    Vfx.EndZone(e.OtherId, e.Key, Map.World(e.Point));
                    break;
                case SimEventType.Shake:
                    Camera.Shake(e.Value, Map.World(e.Point));
                    break;
                case SimEventType.Ping:
                    Vfx.Play("ping", Map.World(e.Point, 0.1f));
                    audio.Play2D("UI", "ping", 0.8f, Audio.AudioBus.Ui);
                    break;
                case SimEventType.TreeDestroyed:
                    if (Map.DestroyTree(e.Point))
                    {
                        Grid.ClearTreeAt(e.Point); // same cells the server cleared
                        Fog.OnTreeDestroyed();
                        Vfx.Play("hit_bone", Map.World(e.Point, 1f));
                    }
                    break;
                case SimEventType.ItemPurchased:
                    if (_views.TryGetValue(e.UnitId, out var buyer) && buyer.State?.OwnerPlayer == LocalPlayerId) audio.Play2D("UI", "buy", 0.8f, Audio.AudioBus.Ui);
                    break;
                case SimEventType.ItemUsed:
                    break;
                case SimEventType.Buyback:
                    if (_views.TryGetValue(e.UnitId, out var bb)) Vfx.Play("buyback", bb.Position);
                    break;
                case SimEventType.WaveSpawned:
                    if (e.Value <= 1) audio.Play2D("Sfx", "horn", 0.9f);
                    break;
                case SimEventType.DayNight:
                    IsNight = e.Value > 0.5f;
                    break;
                case SimEventType.VharothEvent:
                {
                    var pit = Map.World(Map.Map.BossPit, 0.1f);
                    switch (e.Key)
                    {
                        case "tremors":
                            Vfx.Play("vharoth_tremor", pit);
                            audio.PlaySfx("vharoth_tremor", pit, 1f, 0f);
                            break;
                        case "seal_broken":
                            audio.PlaySfx("seal_break", Map.World(e.Point), 1f, 0f);
                            break;
                        case "awakened":
                            Vfx.Play("vharoth_rise", pit);
                            Camera.Shake(1.2f, pit);
                            audio.PlaySfx("vharoth_awaken", pit, 1f, 0f);
                            break;
                        case "slain":
                            Vfx.Play("vharoth_fall", pit);
                            Camera.Shake(1f, pit);
                            break;
                    }
                    break;
                }
                case SimEventType.Announcer:
                    audio.Announce(e.Key);
                    break;
                // RTS
                case SimEventType.ConstructionComplete:
                {
                    var p = Map.World(e.Point);
                    Vfx.Play("level_up", p);
                    if (_views.TryGetValue(e.UnitId, out var built) && built.State?.OwnerPlayer == LocalPlayerId) audio.Play2D("Sfx", "level_up", 0.7f);
                    break;
                }
                case SimEventType.UnitTrained:
                    audio.Play2D("UI", "notify", 0.6f, Audio.AudioBus.Ui);
                    break;
                case SimEventType.MineDepleted:
                    Vfx.Play("structure_collapse", Map.World(e.Point, 1f));
                    audio.PlaySfx("death_stone", Map.World(e.Point), 1f, 0.02f);
                    break;
                case SimEventType.MatchPhase:
                    if (e.Key == null && (MatchPhase)(int)e.Value == MatchPhase.Playing) audio.Play2D("Sfx", "horn", 1f);
                    break;
            }
        }

        /// <summary>Looping effect shown while a unit channels the given ability.</summary>
        private static string ChannelVfx(string abilityId)
        {
            string k = abilityId ?? "";
            if (k.Contains("exsanguinate")) return "exsanguinate_beam";
            if (k.Contains("waystone")) return "teleport_channel";
            if (k.Contains("break_seal")) return "seal_channel";
            if (k.Contains("grove")) return "grove_channel";
            return "channel_generic";
        }

        /// <summary>Vharoth's remains stay in the pit for the rest of the match (also after a reconnect).</summary>
        private void PlaceVharothCorpse()
        {
            _vharothCorpse = true;
            var pit = Map.Map.BossPit;
            var go = ProceduralModels.Prop("vharoth_corpse", Root, out _, out _);
            if (go == null) return;
            var prefab = Resources.Load<GameObject>("Models/Props/vharoth_corpse");
            if (prefab != null) { UnityEngine.Object.Destroy(go); go = UnityEngine.Object.Instantiate(prefab, Root, false); }
            go.transform.position = Map.World(pit, -0.1f);
            go.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
            Decals.Create(Root, Map, new Vector2(pit.X, pit.Y), 9f, "blood_sheet", new Color(0.35f, 0f, 0.03f, 0.85f), false, 35f);
        }

        public void OnMatchEnded(MatchResult result)
        {
            EndResult = result;
            Fog?.Reveal();
            Input?.CancelTargeting();
            // Focus the camera on the destroyed core.
            foreach (var v in _viewList)
            {
                if (v.Kind == UnitKind.Core && result != null && v.Team.ToString() != result.Winner)
                {
                    Camera.JumpTo(v.Position);
                    break;
                }
            }
            bool won = result != null && result.Players.Any(p => p.AccountId == Controller.LocalAccountId && p.Won);
            var audio = GameApp.Instance.Audio;
            audio.Announce(won ? AnnouncerKeys.Victory : AnnouncerKeys.Defeat, 6f);
            audio.PlayMusic(won ? "victory" : "defeat", 1f);
        }

        public void Dispose()
        {
            Vfx?.Clear();
            Input?.Dispose();
            Rts?.Dispose();
            Fog?.Dispose();
            foreach (var v in _viewList) v.Destroy();
            _viewList.Clear();
            _views.Clear();
            Map?.Dispose();
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            RenderSettings.fog = false;
        }
    }
}
