using System.Collections.Generic;
using Bloodfall.Data;
using Bloodfall.Protocol;
using UnityEngine;

namespace Bloodfall.Client.Match
{
    /// <summary>
    /// Presentation of one networked entity: model, animation state derived from the authoritative action state,
    /// interpolated transform, hit flash / hover rim / dissolve via material property blocks, and death handling.
    /// </summary>
    public sealed class EntityView
    {
        public readonly int Id;
        public readonly ModelInstance Model;
        public readonly UnitAnimator Animator;
        public readonly UnitKind Kind;
        public readonly Team Team;
        public readonly string DefId;
        public readonly HeroDef Hero;
        public readonly UnitDef Unit;
        public EntityState State;
        public Vector3 Position;          // world (rendered)
        public float FacingDeg;
        public bool Present = true;       // in the latest snapshot
        public bool Dying;
        public float DeathTimer;
        public float Radius;
        public float Height => Model.Height;
        public bool IsHero => Kind == UnitKind.Hero;
        public bool IsStructure => Kind == UnitKind.Tower || Kind == UnitKind.Barracks || Kind == UnitKind.Core || Kind == UnitKind.Fountain || Kind == UnitKind.Shop;
        public bool Hovered, Selected;
        public string CurrentModelKey;
        public float HitFlash;
        public float FadeIn;
        public readonly Dictionary<string, GameObject> Attachments = new Dictionary<string, GameObject>();

        private int _lastActionTick = int.MinValue;
        private ActionState _lastAction;
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private float _appliedFlash = -1f, _appliedDissolve = -1f;
        private Color _appliedRim = new Color(-1, 0, 0, 0);
        private float _dissolve;
        private static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        private readonly GameData _data;
        private GameObject _selectionRing;
        private Mesh _ringMesh;

        public EntityView(EntityState s, GameData data, Transform parent)
        {
            Id = s.Id;
            Kind = s.Kind;
            Team = s.Team;
            DefId = s.DefId;
            _data = data;
            float scale = 1f, collision = 0.5f;
            string modelKey = s.ModelKey;
            if (s.DefId != null && data.Heroes.TryGetValue(s.DefId, out var h))
            {
                Hero = h;
                scale = h.ModelScale;
                collision = h.CollisionRadius;
                if (string.IsNullOrEmpty(modelKey)) modelKey = h.Model;
            }
            else if (s.DefId != null && data.Units.TryGetValue(s.DefId, out var u))
            {
                Unit = u;
                scale = u.ModelScale;
                collision = u.CollisionRadius;
                if (string.IsNullOrEmpty(modelKey)) modelKey = u.Model;
            }
            CurrentModelKey = modelKey;
            Model = ModelFactory.Create(modelKey, s.Team, s.Kind, scale, collision, parent);
            Model.Root.name = $"{s.DefId}#{s.Id}";
            Animator = UnitAnimator.For(Model);
            Radius = Mathf.Max(collision * scale, IsStructure ? 1.8f : 0.45f);
            State = s;
            Animator.SetState(AnimState.Spawn, 0.5f, 0f);
        }

        public bool IsFlying => Unit != null && Unit.Flying;

        /// <summary>Applies the latest authoritative state (animation decisions only; transform is interpolated).</summary>
        public void ApplyState(EntityState s, GameData data)
        {
            State = s;
            if (Dying) return;
            float impact = 0.4f;
            AnimState anim;
            string variant = null;
            bool restart = false;
            if (s.Has(EntityFlags.Dead)) anim = AnimState.Dead;
            else if (s.Has(EntityFlags.Stunned)) anim = AnimState.Stunned;
            else
            {
                switch (s.Action)
                {
                    case ActionState.AttackWindup:
                    case ActionState.AttackBackswing:
                        anim = AnimState.Attack;
                        impact = Mathf.Max(0.05f, s.AttackPoint);
                        break;
                    case ActionState.CastWindup:
                    case ActionState.CastBackswing:
                        anim = AnimState.Cast;
                        if (s.ActionAbilityId != null && data.Abilities.TryGetValue(s.ActionAbilityId, out var ab))
                        {
                            impact = Mathf.Max(0.05f, ab.CastPoint);
                            variant = ab.CastAnimation;
                            if (variant == "Channel") anim = AnimState.Channel;
                        }
                        break;
                    case ActionState.Channeling: anim = AnimState.Channel; break;
                    case ActionState.Dashing:
                    case ActionState.Airborne: anim = AnimState.Airborne; break;
                    case ActionState.Moving: anim = AnimState.Run; break;
                    default: anim = s.Has(EntityFlags.Moving) ? AnimState.Run : AnimState.Idle; break;
                }
            }
            // A new swing/cast (different start tick) restarts the clip even if the state name is unchanged.
            if ((anim == AnimState.Attack || anim == AnimState.Cast) && (s.ActionStartTick != _lastActionTick || s.Action != _lastAction) &&
                (s.Action == ActionState.AttackWindup || s.Action == ActionState.CastWindup))
                restart = true;
            _lastActionTick = s.ActionStartTick;
            _lastAction = s.Action;
            if (anim == AnimState.Attack || anim == AnimState.Cast)
            {
                // Only the windup is timed; the backswing continues the same clip.
                if (Animator.State == anim && !restart) { Animator.SetState(anim, impact, s.MoveSpeed, variant); return; }
            }
            Animator.SetState(anim, impact, s.MoveSpeed, variant, restart);
        }

        public void Tick(float dt, MatchWorld world)
        {
            Animator.Update(dt);
            HitFlash = Mathf.Max(0f, HitFlash - dt * 5f);
            if (FadeIn < 1f) FadeIn = Mathf.Min(1f, FadeIn + dt * 3f);

            if (Dying)
            {
                DeathTimer += dt;
                float corpse = IsStructure ? 4f : IsHero ? 3f : 2.2f;
                _dissolve = Mathf.Clamp01((DeathTimer - corpse) / 1.4f);
            }
            else
            {
                bool invisibleAlly = State != null && State.Has(EntityFlags.Invisible);
                _dissolve = invisibleAlly ? 0.45f : 1f - FadeIn;
            }

            var rim = Selected ? new Color(0.35f, 1f, 0.45f, 0.55f) : Hovered ? (world.IsEnemy(Team) ? new Color(1f, 0.2f, 0.15f, 0.8f) : new Color(0.4f, 0.9f, 1f, 0.6f)) : new Color(0, 0, 0, 0);
            if (Mathf.Abs(_appliedFlash - HitFlash) > 0.01f || Mathf.Abs(_appliedDissolve - _dissolve) > 0.005f || rim != _appliedRim)
            {
                _appliedFlash = HitFlash;
                _appliedDissolve = _dissolve;
                _appliedRim = rim;
                foreach (var r in Model.Renderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetFloat(HitFlashId, HitFlash);
                    _mpb.SetFloat(DissolveId, _dissolve);
                    _mpb.SetColor(RimColorId, rim);
                    r.SetPropertyBlock(_mpb);
                }
            }
            UpdateSelectionRing(world);
        }

        private void UpdateSelectionRing(MatchWorld world)
        {
            bool show = (Selected || (IsHero && State != null && State.OwnerPlayer == world.LocalPlayerId)) && !Dying && Model.Root.activeSelf;
            if (!show)
            {
                if (_selectionRing != null) _selectionRing.SetActive(false);
                return;
            }
            float size = Radius * 2.6f + 0.6f;
            var col = world.IsEnemy(Team) ? new Color(1f, 0.25f, 0.2f, 0.9f) : Selected ? new Color(0.4f, 1f, 0.5f, 0.9f) : new Color(0.4f, 1f, 0.5f, 0.55f);
            if (_selectionRing == null)
            {
                _selectionRing = Decals.Create(Model.Root.transform.parent, world.Map, new Vector2(Position.x, Position.z), size, "selection_ring", col, true, 0f, 4);
                _ringMesh = _selectionRing.GetComponent<MeshFilter>().sharedMesh;
            }
            _selectionRing.SetActive(true);
            Decals.Conform(_ringMesh, world.Map, new Vector2(Position.x, Position.z), size, Time.time * 20f, col, 4);
        }

        /// <summary>Switches the model (hex, polymorph statuses). Rare, so a full rebuild is fine.</summary>
        public bool NeedsModelSwap(EntityState s) => !string.IsNullOrEmpty(s.ModelKey) && s.ModelKey != CurrentModelKey;

        public void BeginDeath()
        {
            if (Dying) return;
            Dying = true;
            DeathTimer = 0f;
            Animator.SetState(AnimState.Dead, 0.5f, 0f, null, true);
        }

        public bool DeathFinished => Dying && DeathTimer > (IsStructure ? 6f : IsHero ? 4.6f : 3.8f);

        public void Destroy()
        {
            Animator.Dispose();
            foreach (var a in Attachments.Values) if (a != null) Object.Destroy(a);
            if (_selectionRing != null) Object.Destroy(_selectionRing);
            if (Model.Root != null) Object.Destroy(Model.Root);
        }

        /// <summary>World position of a point at a fraction of the model height (overhead UI, hit effects).</summary>
        public Vector3 Point(float heightFraction) => Position + Vector3.up * Model.Height * heightFraction;
    }
}
