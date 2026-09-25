using System;
using System.Collections.Generic;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    public sealed partial class Match
    {
        /// <summary>Facing tolerance before a unit may attack / cast / walk straight (~11.5 degrees).</summary>
        public const float FacingTolerance = 0.2f;

        public void SetAction(Unit u, ActionState state)
        {
            if (u.Action == state) return;
            u.Action = state;
            u.ActionStartTick = Tick;
        }

        // ================================================================== per-unit update

        private void UpdateUnit(Unit u, float dt)
        {
            if (u.Dead) return;
            UpdateCooldowns(u, dt);
            if (u.AttackCooldown > 0f) u.AttackCooldown -= dt;

            // Regeneration.
            if (u.Hp < u.Stats.MaxHp) u.Hp = Math.Min(u.Stats.MaxHp, u.Hp + u.Stats.HpRegen * dt);
            if (u.Mana < u.Stats.MaxMana) u.Mana = Math.Min(u.Stats.MaxMana, u.Mana + u.Stats.ManaRegen * dt);

            if (u.Motion != null) { UpdateMotion(u, dt); return; }
            if (u.IsStructure && u.UnitDef != null && u.UnitDef.DamageMax <= 0) return;

            if ((u.Flags & StatusFlags.HardDisable) != 0)
            {
                SetAction(u, ActionState.Stunned);
                u.IsMoving = false;
                return;
            }
            if (u.Action == ActionState.Stunned) SetAction(u, ActionState.Idle);

            if (u.HasFlag(StatusFlags.Feared)) { UpdateFear(u, dt); return; }
            if (u.HasFlag(StatusFlags.Taunted)) { UpdateTaunt(u, dt); return; }

            var o = u.CurrentOrder;
            switch (o.Type)
            {
                case OrderType.None:
                    UpdateIdle(u, dt);
                    break;
                case OrderType.Move:
                    if (MoveTowardsPoint(u, o.Point, dt, 0.1f)) CompleteOrder(u);
                    break;
                case OrderType.AttackUnit:
                {
                    var t = GetUnit(o.TargetId);
                    bool auto = o.Slot2 == 1;
                    if (auto && t != null && Vector2.Distance(u.Position, t.Position) > u.AcquisitionRange + AttackReach(u, t) + 2f) { CompleteOrder(u); SetAction(u, ActionState.Idle); break; }
                    if (!ProcessAttack(u, t, dt, allowMove: true)) { if (u.Action == ActionState.AttackWindup) SetAction(u, ActionState.Idle); CompleteOrder(u); }
                    break;
                }
                case OrderType.AttackMove:
                    UpdateAttackMove(u, o.Point, dt);
                    break;
                case OrderType.Hold:
                {
                    var t = GetUnit(u.AttackTargetId);
                    if (t == null || !CanAttackTarget(u, t, out _) || Vector2.Distance(u.Position, t.Position) > AttackReach(u, t) || (t.Team == u.Team))
                        t = FindAttackTarget(u, 0f);
                    if (t != null) ProcessAttack(u, t, dt, allowMove: false);
                    else if (u.Action != ActionState.AttackBackswing) SetAction(u, ActionState.Idle);
                    break;
                }
                case OrderType.Follow:
                {
                    var t = GetUnit(o.TargetId);
                    if (t == null) { CompleteOrder(u); break; }
                    MoveTowardsUnit(u, t, dt, 1.8f);
                    break;
                }
                case OrderType.Patrol:
                    UpdateAttackMove(u, o.Point, dt, patrol: true);
                    break;
                case OrderType.CastNoTarget:
                case OrderType.CastUnit:
                case OrderType.CastPoint:
                    ProcessCastOrder(u, dt);
                    break;
                default:
                    CompleteOrder(u);
                    break;
            }
            if (u.CurrentOrder.Type == OrderType.None && u.Action == ActionState.Moving) SetAction(u, ActionState.Idle);
        }

        private void UpdateIdle(Unit u, float dt)
        {
            u.IsMoving = false;
            if (u.Action == ActionState.AttackBackswing || u.Action == ActionState.CastBackswing)
            {
                u.ActionTimer -= dt;
                if (u.ActionTimer <= 0f) SetAction(u, ActionState.Idle);
                return;
            }
            if (u.Action != ActionState.Idle) SetAction(u, ActionState.Idle);
            u.IdleTime += dt;
            // Heroes and summons auto-acquire nearby enemies after a short idle (classic auto-attack behaviour).
            if ((u.IsHero || u.Kind == UnitKind.Summon) && u.Brain == null && u.IdleTime > 0.25f && u.CanAttack)
            {
                var t = FindAttackTarget(u, Math.Min(u.AcquisitionRange, u.Stats.AttackRange + 2.5f));
                if (t != null) u.CurrentOrder = new Order { Type = OrderType.AttackUnit, UnitId = u.Id, TargetId = t.Id, Slot2 = 1 };
            }
        }

        private void UpdateAttackMove(Unit u, Vector2 goal, float dt, bool patrol = false)
        {
            var t = GetUnit(u.AttackTargetId);
            if (t != null && (!CanAttackTarget(u, t, out bool deny) || deny || Vector2.Distance(u.Position, t.Position) > u.AcquisitionRange + AttackReach(u, t) + 1.5f)) t = null;
            if (t == null && (Tick + u.Id) % 3 == 0) t = FindAttackTarget(u, u.AcquisitionRange);
            if (t != null && u.CanAttack)
            {
                u.AttackTargetId = t.Id;
                ProcessAttack(u, t, dt, allowMove: true);
                return;
            }
            if (u.Action == ActionState.AttackWindup) return;
            if (u.Action == ActionState.AttackBackswing) { u.ActionTimer -= dt; if (u.ActionTimer > 0f) return; SetAction(u, ActionState.Idle); }
            u.AttackTargetId = 0;
            if (MoveTowardsPoint(u, goal, dt, 0.4f))
            {
                if (patrol)
                {
                    var o = u.CurrentOrder;
                    (o.Point, o.Point2) = (o.Point2, o.Point);
                    u.CurrentOrder = o;
                    u.Path.Clear();
                }
                else CompleteOrder(u);
            }
        }

        /// <summary>Default target acquisition: closest attackable enemy, preferring non-structures.</summary>
        public Unit FindAttackTarget(Unit u, float range)
        {
            var list = RentList();
            float search = range <= 0f ? u.Stats.AttackRange + 2f : range;
            UnitsInRadius(u.Position, search + 1f, list);
            Unit best = null;
            float bestScore = float.MaxValue;
            foreach (var t in list)
            {
                if (t.Team == u.Team || !CanAttackTarget(u, t, out bool deny) || deny) continue;
                if (t.Invulnerable || t.Kind == UnitKind.Fountain) continue;
                if (u.Team == Team.Neutral && t.Team == Team.Neutral) continue;
                float d = Vector2.Distance(u.Position, t.Position);
                if (range <= 0f && d > AttackReach(u, t)) continue;
                if (d > search + t.Radius) continue;
                float score = d + (t.IsStructure ? 6f : 0f) + (t.Kind == UnitKind.Ward ? 3f : 0f);
                if (score < bestScore) { bestScore = score; best = t; }
            }
            ReturnList(list);
            return best;
        }

        private void UpdateFear(Unit u, float dt)
        {
            Unit source = null;
            foreach (var s in u.Statuses) if ((s.Def.Flags & StatusFlags.Feared) != 0) { source = s.Source; break; }
            var away = source != null ? MathUtil.SafeNormalize(u.Position - source.Position, MathUtil.FromAngle(u.Facing)) : MathUtil.FromAngle(u.Facing);
            if (u.Action == ActionState.CastWindup || u.Action == ActionState.Channeling) InterruptUnit(u);
            MoveTowardsPoint(u, u.Position + away * 4f, dt, 0.1f, allowPathing: false);
        }

        private void UpdateTaunt(Unit u, float dt)
        {
            Unit source = null;
            foreach (var s in u.Statuses) if ((s.Def.Flags & StatusFlags.Taunted) != 0) { source = s.Source; break; }
            if (source == null || source.Dead) return;
            ProcessAttack(u, source, dt, allowMove: true);
        }

        // ================================================================== locomotion

        public bool FaceTowards(Unit u, Vector2 point, float dt)
        {
            var d = point - u.Position;
            if (d.LengthSquared() < 1e-4f) return true;
            float target = MathUtil.AngleOf(d);
            u.Facing = MathUtil.RotateTowards(u.Facing, target, Math.Max(1f, u.Stats.TurnRate) * dt);
            return Math.Abs(MathUtil.AngleDelta(u.Facing, target)) <= FacingTolerance;
        }

        public void StopMoving(Unit u)
        {
            u.IsMoving = false;
            if (u.Action == ActionState.Moving) SetAction(u, ActionState.Idle);
        }

        public void MoveTowardsUnit(Unit u, Unit target, float dt, float stopDist)
        {
            // Re-plan when the target has drifted far from the current path goal.
            if (u.Path.Count > 0 && Vector2.DistanceSquared(u.PathGoal, target.Position) > 1.5f * 1.5f) u.Path.Clear();
            MoveTowardsPoint(u, target.Position, dt, stopDist);
        }

        /// <summary>Walks toward a point using A* paths. Returns true when within stopDist.</summary>
        public bool MoveTowardsPoint(Unit u, Vector2 goal, float dt, float stopDist, bool allowPathing = true)
        {
            u.IdleTime = 0f;
            float distToGoal = Vector2.Distance(u.Position, goal);
            if (distToGoal <= stopDist) { StopMoving(u); return true; }
            if (!u.CanMove) { u.IsMoving = false; return false; }

            if (allowPathing)
            {
                u.RepathTimer -= dt;
                bool needPath = u.Path.Count == 0 || u.PathIndex >= u.Path.Count
                                || Vector2.DistanceSquared(u.PathGoal, goal) > 0.3f * 0.3f
                                || u.PathGridVersion != Grid.Version
                                || (u.StuckTimer > 0.4f && u.RepathTimer <= 0f);
                if (needPath)
                {
                    Grid.FindPath(u.Position, goal, u.Path);
                    u.PathIndex = 0;
                    u.PathGoal = goal;
                    u.PathGridVersion = Grid.Version;
                    u.RepathTimer = 0.5f;
                    u.StuckTimer = 0f;
                    if (u.Path.Count == 0) { StopMoving(u); return true; }
                }
            }
            else
            {
                u.Path.Clear();
                u.Path.Add(goal);
                u.PathIndex = 0;
            }

            Vector2 next = u.Path[u.PathIndex];
            // Skip waypoints we are already on top of.
            while (Vector2.DistanceSquared(u.Position, next) < 0.04f && u.PathIndex < u.Path.Count - 1) next = u.Path[++u.PathIndex];

            var toNext = next - u.Position;
            float target = MathUtil.AngleOf(toNext);
            u.Facing = MathUtil.RotateTowards(u.Facing, target, Math.Max(1f, u.Stats.TurnRate) * dt);
            float delta = Math.Abs(MathUtil.AngleDelta(u.Facing, target));
            SetAction(u, ActionState.Moving);
            u.IsMoving = true;
            if (delta > 1.2f) return false; // turning in place first (big direction changes)

            float step = u.Stats.MoveSpeed * dt;
            float remaining = toNext.Length();
            Vector2 newPos;
            if (step >= remaining)
            {
                newPos = next;
                u.PathIndex++;
            }
            else newPos = u.Position + toNext / remaining * step;

            // Final approach: do not overshoot the stop distance.
            float newDistToGoal = Vector2.Distance(newPos, goal);
            if (newDistToGoal < stopDist && distToGoal > stopDist)
                newPos = goal + MathUtil.SafeNormalize(newPos - goal, Vector2.UnitX) * stopDist;

            if (u.Flying || Grid.IsWalkable(newPos)) u.Position = newPos;
            else
            {
                // Slide along obstacles.
                var slideX = new Vector2(newPos.X, u.Position.Y);
                var slideY = new Vector2(u.Position.X, newPos.Y);
                if (Grid.IsWalkable(slideX)) u.Position = slideX;
                else if (Grid.IsWalkable(slideY)) u.Position = slideY;
                else u.Path.Clear();
            }

            float moved = Vector2.Distance(u.Position, u.LastPosition);
            u.StuckTimer = moved < step * 0.25f ? u.StuckTimer + dt : 0f;
            u.LastPosition = u.Position;

            if (u.PathIndex >= u.Path.Count)
            {
                bool arrived = Vector2.Distance(u.Position, goal) <= Math.Max(stopDist, 0.25f);
                if (arrived || !allowPathing) { StopMoving(u); return arrived || !allowPathing; }
                u.Path.Clear();
                // Unreachable goal: we are at the closest reachable point.
                if (Vector2.Distance(u.Position, goal) > 0.5f && Grid.LineWalkable(u.Position, goal) == false) { StopMoving(u); return true; }
            }
            return false;
        }

        // ================================================================== soft collision

        private readonly List<Unit> _overlapScratch = new List<Unit>(16);

        private void ResolveUnitOverlaps(float dt)
        {
            for (int i = 0; i < Units.Count; i++)
            {
                var a = Units[i];
                if (!a.IsAlive || a.IsStructure || a.Flying || a.HasFlag(StatusFlags.Phased) || a.Motion != null) continue;
                _overlapScratch.Clear();
                Spatial.Query(a.Position, a.Radius + 0.6f, _overlapScratch, true);
                foreach (var b in _overlapScratch)
                {
                    if (b == a || b.Id < a.Id || !b.IsAlive || b.IsStructure || b.Flying || b.HasFlag(StatusFlags.Phased) || b.Motion != null) continue;
                    if (b.Kind == UnitKind.Ward || a.Kind == UnitKind.Ward) continue;
                    var d = a.Position - b.Position;
                    float dist = d.Length();
                    float min = a.Radius + b.Radius;
                    if (dist >= min) continue;
                    var n = dist > 1e-4f ? d / dist : MathUtil.FromAngle((a.Id * 2.39996f) % MathUtil.TwoPi);
                    float overlap = (min - dist) * 0.5f;
                    // Moving units yield to stationary ones less; heroes are heavier than creeps.
                    float wa = Weight(a), wb = Weight(b);
                    float ta = wb / (wa + wb), tb = wa / (wa + wb);
                    var pa = a.Position + n * overlap * ta;
                    var pb = b.Position - n * overlap * tb;
                    if (Grid.IsWalkable(pa)) a.Position = pa;
                    if (Grid.IsWalkable(pb)) b.Position = pb;
                }
            }
        }

        private static float Weight(Unit u)
        {
            float w = u.IsHero ? 2f : u.Kind == UnitKind.Boss ? 20f : 1f;
            if (!u.IsMoving) w *= 1.6f;
            return w;
        }

        // ================================================================== forced motion

        private void StartCasterMotion(EffectDef e, EffectContext ctx)
        {
            var u = ctx.Caster;
            if (u == null || u.Dead) return;
            int L = ctx.Level;
            Vector2 dest = ctx.Target != null && ctx.Target != u ? ctx.Target.Position : ctx.Point;
            var dir = MathUtil.SafeNormalize(dest - u.Position, MathUtil.FromAngle(u.Facing));
            float maxDist = e.MaxDistance?.Get(L) ?? (e.Distance > 0 ? e.Distance : 8f);
            float dist = Math.Min(maxDist, Vector2.Distance(u.Position, dest));
            if (e.Distance > 0 && e.MaxDistance == null) dist = e.Distance;
            if (dist < e.MinDistance) dist = e.MinDistance;
            if (ctx.Target != null && ctx.Target != u) dist = Math.Max(0f, dist - (ctx.Target.Radius + u.Radius));
            dest = u.Position + dir * dist;
            if (e.Type == EffectType.Dash) dest = Grid.ClampLine(u.Position, dest);
            else dest = Grid.NearestWalkable(new Vector2(MathUtil.Clamp(dest.X, 1, Grid.WorldWidth - 1), MathUtil.Clamp(dest.Y, 1, Grid.WorldHeight - 1)));

            float travel = Vector2.Distance(u.Position, dest);
            float duration = e.MoveDuration > 0 ? e.MoveDuration : travel / Math.Max(1f, e.Speed);
            u.Motion = new ForcedMotion
            {
                Kind = e.Type == EffectType.Leap ? MotionKind.Leap : MotionKind.Dash,
                Start = u.Position,
                End = dest,
                Duration = Math.Max(0.05f, duration),
                Height = e.Height,
                Invulnerable = e.Invulnerable,
                Def = e,
                Context = ctx,
                Passed = new HashSet<int>(),
            };
            u.Facing = MathUtil.AngleOf(dir);
            SetAction(u, e.Type == EffectType.Leap ? ActionState.Airborne : ActionState.Dashing);
            RecomputeFlags(u);
            Emit(new SimEvent { Type = SimEventType.DashStart, UnitId = u.Id, Point = u.Position, Point2 = dest, Value = u.Motion.Duration, Value2 = e.Height, Key = e.Vfx, PlayerId = -1 });
        }

        private void StartTargetMotion(EffectDef e, EffectContext ctx, Unit t)
        {
            if (t == null || t.Dead || t.IsStructure || t.Kind == UnitKind.Boss) return;
            bool hostile = ctx.Caster != null && t.Team != ctx.Caster.Team;
            if (hostile && t.IsMagicImmune && !(ctx.PiercesMagicImmunity || e.PiercesMagicImmunity)) return;
            int L = ctx.Level;
            Vector2 dest;
            if (e.Type == EffectType.Pull)
            {
                var caster = ctx.Caster;
                if (e.ToCaster && caster != null)
                {
                    var dirToTarget = MathUtil.SafeNormalize(t.Position - caster.Position, MathUtil.FromAngle(caster.Facing));
                    dest = caster.Position + dirToTarget * (caster.Radius + t.Radius + 0.3f);
                }
                else
                {
                    var from = e.Center == AreaCenter.Point ? ctx.Point : caster?.Position ?? ctx.Point;
                    var dirIn = MathUtil.SafeNormalize(from - t.Position, Vector2.UnitX);
                    float dist = Math.Min(e.Distance > 0 ? e.Distance : 3f, Vector2.Distance(from, t.Position));
                    dest = t.Position + dirIn * dist;
                }
            }
            else
            {
                var from = e.Center == AreaCenter.Point ? ctx.Point : ctx.Caster?.Position ?? ctx.Point;
                var dirOut = MathUtil.SafeNormalize(t.Position - from, ctx.Direction != Vector2.Zero ? Vector2.Normalize(ctx.Direction) : Vector2.UnitX);
                float dist = e.Distance > 0 ? e.Distance : (e.MaxDistance?.Get(L) ?? 2f);
                dest = t.Position + dirOut * dist;
            }
            dest = Grid.ClampLine(t.Position, dest);
            float travel = Vector2.Distance(t.Position, dest);
            float duration = e.MoveDuration > 0 ? e.MoveDuration : travel / Math.Max(1f, e.Speed);
            InterruptUnit(t);
            t.Motion = new ForcedMotion
            {
                Kind = e.Type == EffectType.Pull ? MotionKind.Pull : MotionKind.Knockback,
                Start = t.Position,
                End = dest,
                Duration = Math.Max(0.05f, duration),
                Height = e.Height,
                Def = e,
                Context = ctx,
                Passed = new HashSet<int>(),
            };
            SetAction(t, ActionState.Dashing);
            RecomputeFlags(t);
            Emit(new SimEvent { Type = SimEventType.DashStart, UnitId = t.Id, Point = t.Position, Point2 = dest, Value = t.Motion.Duration, Value2 = e.Height, Key = e.Vfx, PlayerId = -1 });
        }

        private void UpdateMotion(Unit u, float dt)
        {
            var m = u.Motion;
            m.Elapsed += dt;
            float t = MathUtil.Clamp01(m.Elapsed / m.Duration);
            var prev = u.Position;
            u.Position = Vector2.Lerp(m.Start, m.End, t);

            // Dash collisions (caster dashes only).
            if (m.Kind == MotionKind.Dash && m.Def != null && (m.Def.OnCollide != null || m.Def.OnPass != null || m.Def.StopOnUnitCollision))
            {
                var list = RentList();
                UnitsInRadius(u.Position, m.Def.CollisionRadius, list);
                var hits = list.ToArray();
                ReturnList(list);
                foreach (var other in hits)
                {
                    if (other == u || other.Team == u.Team || m.Passed.Contains(other.Id) || other.Invulnerable) continue;
                    if (other.IsStructure || (other.Team == Team.Neutral && other.Kind == UnitKind.Boss && m.Def.StopOnUnitCollision == false)) continue;
                    if ((other.TargetTypeOf() & m.Def.CollisionTypes) != 0 && m.Def.StopOnUnitCollision)
                    {
                        m.Passed.Add(other.Id);
                        var c = m.Context; c.Target = other; c.Point = u.Position; c.Depth++;
                        ExecuteEffects(m.Def.OnCollide, c);
                        m.Collided = true;
                        m.End = u.Position;
                        t = 1f;
                        break;
                    }
                    if (m.Def.OnPass != null && (other.TargetTypeOf() & TargetType.Units) != 0)
                    {
                        m.Passed.Add(other.Id);
                        var c = m.Context; c.Target = other; c.Point = u.Position; c.Depth++;
                        ExecuteEffects(m.Def.OnPass, c);
                    }
                }
            }

            if (m.Kind != MotionKind.Leap && !u.Flying && !Grid.IsWalkable(u.Position))
            {
                u.Position = prev;
                t = 1f;
            }
            if (t >= 1f)
            {
                u.Motion = null;
                u.Position = Grid.NearestWalkable(u.Position);
                u.LastPosition = u.Position;
                u.Path.Clear();
                SetAction(u, ActionState.Idle);
                RecomputeFlags(u);
                Emit(new SimEvent { Type = SimEventType.DashEnd, UnitId = u.Id, Point = u.Position, Key = m.Def?.Vfx, PlayerId = -1 });
                if (m.Def != null && m.Def.OnArrive != null && (m.Kind == MotionKind.Dash || m.Kind == MotionKind.Leap) && !u.Dead)
                {
                    var c = m.Context; c.Point = u.Position; c.Depth++;
                    if (m.Kind == MotionKind.Leap) c.Target = u;
                    ExecuteEffects(m.Def.OnArrive, c);
                }
                // Resume the caster's order after a dash (e.g. keep attacking the charged target).
                if ((m.Kind == MotionKind.Dash || m.Kind == MotionKind.Leap) && u.CurrentOrder.Type >= OrderType.CastNoTarget && u.CurrentOrder.Type <= OrderType.CastPoint)
                    CompleteOrder(u);
            }
        }
    }
}
