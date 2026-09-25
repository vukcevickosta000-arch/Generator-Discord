using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Bloodfall.Client.Match
{
    public enum AnimState { Idle, Run, Attack, Cast, Channel, Stunned, Dead, Airborne, Spawn }

    /// <summary>Drives a unit's animation from authoritative action state (the server decides; we only present).</summary>
    public abstract class UnitAnimator
    {
        public AnimState State { get; protected set; } = AnimState.Idle;
        protected float StateTime;
        /// <summary>Seconds from state start until the hit/release moment (attack point or cast point).</summary>
        protected float ImpactTime = 0.4f;
        protected float MoveSpeed = 3.5f;
        protected string Variant;

        public virtual void SetState(AnimState s, float impactTime, float moveSpeed, string variant = null, bool restart = false)
        {
            MoveSpeed = moveSpeed;
            if (s == State && !restart && variant == Variant) return;
            State = s;
            StateTime = 0f;
            ImpactTime = Mathf.Max(0.05f, impactTime);
            Variant = variant;
            OnStateChanged();
        }

        protected virtual void OnStateChanged() { }
        public abstract void Update(float dt);
        public virtual void Dispose() { }

        public static UnitAnimator For(ModelInstance mi)
        {
            if (mi.Imported && mi.Clips != null && mi.Clips.Count > 0)
            {
                var anim = mi.Root.GetComponentInChildren<Animator>();
                if (anim != null) return new ClipAnimator(anim, mi.Clips);
            }
            return new ProceduralAnimator(mi);
        }
    }

    /// <summary>Playables-based player for imported clips with short crossfades and attack-speed scaling.</summary>
    public sealed class ClipAnimator : UnitAnimator
    {
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private readonly Dictionary<string, AnimationClip> _clips;
        private AnimationClipPlayable _current, _previous;
        private float _fade = 1f;
        private const float FadeTime = 0.15f;
        /// <summary>Convention for authored attack/cast clips: the impact happens at 40% of the clip.</summary>
        public const float ImpactFraction = 0.4f;

        public ClipAnimator(Animator animator, Dictionary<string, AnimationClip> clips)
        {
            _clips = clips;
            animator.applyRootMotion = false;
            _graph = PlayableGraph.Create("unit");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "anim", animator);
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
            OnStateChanged();
        }

        private AnimationClip Find(params string[] names)
        {
            foreach (var n in names)
                if (!string.IsNullOrEmpty(n) && _clips.TryGetValue(n, out var c)) return c;
            return null;
        }

        protected override void OnStateChanged()
        {
            AnimationClip clip;
            float speed = 1f;
            switch (State)
            {
                case AnimState.Run: clip = Find("Run", "Walk", "Idle"); if (clip != null) speed = Mathf.Clamp(MoveSpeed / 3.6f, 0.6f, 1.8f); break;
                case AnimState.Attack:
                    clip = Find(Variant, "Attack" + Random.Range(1, 3), "Attack1", "Attack");
                    if (clip != null) speed = clip.length * ImpactFraction / ImpactTime;
                    break;
                case AnimState.Cast:
                    clip = Find(Variant, "Cast1", "Cast", "Attack1");
                    if (clip != null) speed = clip.length * ImpactFraction / ImpactTime;
                    break;
                case AnimState.Channel: clip = Find("Channel", "Cast1", "Idle"); break;
                case AnimState.Stunned: clip = Find("Stun", "Stunned", "Idle"); break;
                case AnimState.Dead: clip = Find("Death", "Die"); break;
                case AnimState.Airborne: clip = Find("Leap", "Jump", "Run"); break;
                case AnimState.Spawn: clip = Find("Spawn", "Idle"); break;
                default: clip = Find("Idle"); break;
            }
            if (clip == null) return;
            if (_previous.IsValid()) { _graph.Disconnect(_mixer, 1); _previous.Destroy(); }
            if (_current.IsValid())
            {
                _graph.Disconnect(_mixer, 0);
                _previous = _current;
                _graph.Connect(_previous, 0, _mixer, 1);
            }
            _current = AnimationClipPlayable.Create(_graph, clip);
            _current.SetSpeed(Mathf.Clamp(speed, 0.2f, 4f));
            _current.SetApplyFootIK(false);
            _graph.Connect(_current, 0, _mixer, 0);
            _fade = _previous.IsValid() ? 0f : 1f;
            bool loop = State == AnimState.Idle || State == AnimState.Run || State == AnimState.Channel || State == AnimState.Stunned;
            if (!loop) _current.SetDuration(clip.length);
        }

        public override void Update(float dt)
        {
            StateTime += dt;
            if (_fade < 1f)
            {
                _fade = Mathf.Min(1f, _fade + dt / FadeTime);
                _mixer.SetInputWeight(0, _fade);
                _mixer.SetInputWeight(1, 1f - _fade);
            }
            else
            {
                _mixer.SetInputWeight(0, 1f);
                _mixer.SetInputWeight(1, 0f);
            }
            _graph.Evaluate(dt);
        }

        public override void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }

    /// <summary>
    /// Procedural animation for stand-in rigs: gait cycles scaled by move speed, wind-up/strike timed to the server's
    /// attack point, cast gestures, channel tremble, stun sway, collapse on death, wing flaps and spinning crystals.
    /// </summary>
    public sealed class ProceduralAnimator : UnitAnimator
    {
        private readonly ModelInstance _mi;
        private readonly RigParts _r;
        private float _cycle;
        private float _blendRun;
        private readonly float _phaseOffset;
        private readonly Vector3 _hipsBase;
        private readonly Vector3 _spinBase;

        public ProceduralAnimator(ModelInstance mi)
        {
            _mi = mi;
            _r = mi.Rig ?? new RigParts { Kind = RigKind.Static };
            _phaseOffset = Random.value * 10f;
            _hipsBase = _r.Hips != null ? _r.Hips.localPosition : Vector3.zero;
            _spinBase = _r.Spin != null ? _r.Spin.localPosition : Vector3.zero;
        }

        private static void Rot(Transform t, float x, float y = 0, float z = 0)
        {
            if (t != null) t.localRotation = Quaternion.Euler(x, y, z);
        }

        public override void Update(float dt)
        {
            StateTime += dt;
            float time = Time.time + _phaseOffset;
            _blendRun = Mathf.MoveTowards(_blendRun, State == AnimState.Run ? 1f : 0f, dt * 6f);
            float stride = Mathf.Max(0.8f, _r.Height * 0.55f);
            _cycle += dt * (MoveSpeed / stride) * Mathf.PI * _blendRun + dt * 0.001f;

            if (_r.Spin != null)
            {
                _r.Spin.localRotation = Quaternion.Euler(0, time * 40f, 0);
                _r.Spin.localPosition = _spinBase + Vector3.up * Mathf.Sin(time * 1.3f) * 0.2f;
            }
            if (State == AnimState.Dead) { AnimateDeath(); return; }

            switch (_r.Kind)
            {
                case RigKind.Biped:
                case RigKind.Winged:
                    AnimateBiped(time);
                    break;
                case RigKind.Quadruped:
                case RigKind.Serpent:
                    AnimateBeast(time);
                    break;
                case RigKind.Siege:
                    AnimateSiege();
                    break;
            }
            if (_r.WingL != null)
            {
                bool flying = _r.Kind == RigKind.Winged || State == AnimState.Run;
                float flap = Mathf.Sin(time * (flying ? 9f : 2f)) * (flying ? 38f : 8f);
                Rot(_r.WingL, 0, -10, flap + 10);
                Rot(_r.WingR, 0, 10, -flap - 10);
            }
        }

        private float AttackSwing(float windupAngle, float strikeAngle)
        {
            // Wind-up until the impact moment, then a fast strike and recovery.
            float t = StateTime / ImpactTime;
            if (t < 1f) return Mathf.Lerp(0f, windupAngle, Mathf.SmoothStep(0, 1, t));
            float s = Mathf.Clamp01((t - 1f) * 5f);
            float back = Mathf.Lerp(windupAngle, strikeAngle, Mathf.SmoothStep(0, 1, s));
            float rec = Mathf.Clamp01((t - 1.5f) * 2f);
            return Mathf.Lerp(back, 0f, rec);
        }

        private void AnimateBiped(float time)
        {
            float run = _blendRun;
            float legSwing = Mathf.Sin(_cycle) * 34f * run;
            float armSwing = Mathf.Sin(_cycle) * 26f * run;
            float breathe = Mathf.Sin(time * 1.6f) * 2f;
            float lean = 9f * run;
            float bob = Mathf.Abs(Mathf.Sin(_cycle)) * 0.06f * run;
            if (_r.Hips != null) _r.Hips.localPosition = _hipsBase + Vector3.up * bob;
            Rot(_r.LegL, legSwing);
            Rot(_r.LegR, -legSwing);
            float armLx = -armSwing, armRx = armSwing, armLz = -6f, armRz = 6f;
            float torsoX = lean + breathe * 0.5f, headX = 0f, torsoY = 0f;
            switch (State)
            {
                case AnimState.Attack:
                    armRx = AttackSwing(-150f, 35f);
                    torsoY = AttackSwing(-20f, 25f);
                    torsoX += AttackSwing(-6f, 12f);
                    break;
                case AnimState.Cast:
                {
                    float c = AttackSwing(-110f, -60f);
                    armLx = c; armRx = c; armLz = -20f; armRz = 20f;
                    torsoX += AttackSwing(-8f, 6f);
                    break;
                }
                case AnimState.Channel:
                {
                    float tremble = Mathf.Sin(time * 30f) * 3f;
                    armLx = -85f + tremble; armRx = -85f - tremble; armLz = -12f; armRz = 12f;
                    headX = -8f;
                    break;
                }
                case AnimState.Stunned:
                    headX = 25f;
                    torsoX = 14f + Mathf.Sin(time * 3f) * 4f;
                    torsoY = Mathf.Sin(time * 2.2f) * 10f;
                    armLx = 10f; armRx = 10f;
                    break;
                case AnimState.Airborne:
                    Rot(_r.LegL, -40f);
                    Rot(_r.LegR, -10f);
                    armLx = -60f; armRx = -150f;
                    break;
            }
            Rot(_r.Torso, torsoX, torsoY);
            Rot(_r.Head, headX + breathe * 0.3f);
            Rot(_r.ArmL, armLx, 0, armLz);
            Rot(_r.ArmR, armRx, 0, armRz);
        }

        private void AnimateBeast(float time)
        {
            float run = _blendRun;
            float swing = Mathf.Sin(_cycle * 1.4f) * 30f * run;
            Rot(_r.LegL, swing);
            Rot(_r.LegBR, swing);
            Rot(_r.LegR, -swing);
            Rot(_r.LegBL, -swing);
            float bob = Mathf.Abs(Mathf.Sin(_cycle * 1.4f)) * 0.05f * run;
            if (_r.Hips != null) _r.Hips.localPosition = _hipsBase + Vector3.up * (bob + Mathf.Sin(time * 1.5f) * 0.01f);
            float headX = Mathf.Sin(time * 1.2f) * 3f;
            if (State == AnimState.Attack) headX = AttackSwing(-25f, 30f);
            else if (State == AnimState.Cast) headX = AttackSwing(-35f, 10f);
            else if (State == AnimState.Stunned) headX = 20f + Mathf.Sin(time * 3f) * 5f;
            Rot(_r.Head, headX);
            if (_r.Tail != null) Rot(_r.Tail, 0, Mathf.Sin(time * (3f + run * 6f)) * (12f + run * 12f));
        }

        private void AnimateSiege()
        {
            if (_r.Weapon == null) return;
            float x = State == AnimState.Attack ? AttackSwing(35f, -60f) : 0f;
            Rot(_r.Weapon, x);
        }

        private void AnimateDeath()
        {
            float t = Mathf.Clamp01(StateTime / 0.7f);
            float e = 1f - (1f - t) * (1f - t);
            switch (_r.Kind)
            {
                case RigKind.Biped:
                case RigKind.Winged:
                    Rot(_r.Torso, Mathf.Lerp(0, -20f, e));
                    Rot(_r.ArmL, Mathf.Lerp(0, -40f, e), 0, -30f * e);
                    Rot(_r.ArmR, Mathf.Lerp(0, -30f, e), 0, 40f * e);
                    Rot(_r.LegL, -25f * e);
                    Rot(_r.LegR, 15f * e);
                    break;
            }
            // Topple the whole model backwards (structures collapse downward instead).
            if (_r.Kind == RigKind.Static || _r.Kind == RigKind.Floating)
                _mi.Pivot.localPosition = Vector3.down * e * _r.Height * 0.35f;
            else
            {
                _mi.Pivot.localRotation = Quaternion.Euler(-80f * e, 0, 0);
                _mi.Pivot.localPosition = new Vector3(0, 0.1f * e, 0.25f * e);
            }
        }
    }
}
