using System;
using System.Collections.Generic;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    public sealed class Projectile
    {
        public int Id;
        public ProjectileKind Kind;
        public Unit Source;
        public Team Team;
        public Unit Target;
        public Vector2 Position;
        public Vector2 Direction;
        public Vector2 LastKnownTargetPos;
        public float Speed;
        public float MaxRange;
        public float Travelled;
        public float Width;
        public bool Dodgeable;
        public bool StopOnFirstHit;
        public bool Returning;
        public int BouncesLeft;
        public bool IsAttack;
        public DamageInfo Attack;
        public EffectDef Def;
        public EffectContext Context;
        public string Visual;
        public HashSet<int> Hit;
        public bool Done;
        public bool TargetLost;
    }

    /// <summary>Persistent ground area: delayed blasts (telegraphed), damage fields, walls, vision reveals.</summary>
    public sealed class Zone
    {
        public int Id;
        public EffectDef Def;
        public EffectContext Context;
        public Vector2 Center;
        public float Radius;
        public float Delay;
        public float Remaining;
        public float Interval;
        public float TickTimer;
        public bool FollowCaster;
        public bool IsDelayed;
        public bool Done;
        public Team VisionTeam = Team.None;
        public float VisionRadius;
        public List<(int x, int y)> BlockedCells;
    }

    public sealed partial class Match
    {
        private void LaunchEffectProjectile(EffectDef e, EffectContext ctx)
        {
            var caster = ctx.Caster;
            if (caster == null) return;
            int L = ctx.Level;
            // Bounce / chain: projectiles launched from a previous target originate at that unit.
            Vector2 origin = ctx.Projectile != null ? ctx.Projectile.Position : caster.Position;
            var p = new Projectile
            {
                Id = _nextProjectileId++,
                Kind = e.Projectile,
                Source = caster,
                Team = caster.Team,
                Position = origin,
                Speed = Math.Max(1f, e.Speed),
                Width = e.Width,
                Dodgeable = e.Dodgeable,
                StopOnFirstHit = e.StopOnFirstHit,
                BouncesLeft = e.Bounces,
                Def = e,
                Context = ctx,
                Visual = e.Vfx ?? "projectile_default",
                Hit = new HashSet<int>(),
            };
            if (e.Projectile == ProjectileKind.Tracking)
            {
                var target = ResolveUnit(e, ctx);
                if (e.Target == EffectTarget.Target && ctx.Target == null) target = null;
                if (target == null || target.Dead) return;
                p.Target = target;
                p.LastKnownTargetPos = target.Position;
            }
            else
            {
                var dir = ctx.Direction != Vector2.Zero ? Vector2.Normalize(ctx.Direction) : MathUtil.SafeNormalize(ctx.Point - origin, MathUtil.FromAngle(caster.Facing));
                p.Direction = dir;
                p.MaxRange = e.Range?.Get(L) ?? (ctx.Ability != null ? ctx.Ability.CastRange.Get(L) : 10f);
            }
            Projectiles.Add(p);
            Emit(new SimEvent
            {
                Type = SimEventType.ProjectileLaunch,
                UnitId = caster.Id,
                OtherId = p.Target?.Id ?? 0,
                Key = p.Visual,
                Point = p.Position,
                Point2 = p.Kind == ProjectileKind.Linear ? p.Position + p.Direction * p.MaxRange : p.LastKnownTargetPos,
                Value = p.Speed,
                Value2 = p.Id,
                Flags = (byte)(p.Kind == ProjectileKind.Linear ? 1 : 0),
                PlayerId = -1,
            });
        }

        private void UpdateProjectiles(float dt)
        {
            for (int i = Projectiles.Count - 1; i >= 0; i--)
            {
                var p = Projectiles[i];
                if (!p.Done)
                {
                    if (p.Kind == ProjectileKind.Tracking) UpdateTracking(p, dt);
                    else UpdateLinear(p, dt);
                }
                if (p.Done) Projectiles.RemoveAt(i);
            }
        }

        private void UpdateTracking(Projectile p, float dt)
        {
            var t = p.Target;
            if (t != null && !t.Dead && !p.TargetLost)
            {
                // Dodge: blinking / going invisible makes dodgeable projectiles miss.
                if (p.Dodgeable && (t.IsInvisible && !IsVisibleTo(t, p.Team) || Vector2.Distance(t.Position, p.LastKnownTargetPos) > 6f))
                    p.TargetLost = true;
                else p.LastKnownTargetPos = t.Position;
            }
            else p.TargetLost = true;

            var dest = p.LastKnownTargetPos;
            var to = dest - p.Position;
            float dist = to.Length();
            float step = p.Speed * dt;
            float hitDist = (t != null && !p.TargetLost ? t.Radius : 0f) + 0.15f;
            if (dist - hitDist <= step)
            {
                p.Position = dest;
                p.Done = true;
                if (!p.TargetLost && t != null && !t.Dead) ProjectileImpact(p, t);
                else Emit(new SimEvent { Type = SimEventType.ProjectileHit, Value = p.Id, Point = p.Position, Key = p.Visual, PlayerId = -1 });
                return;
            }
            p.Position += to / dist * step;
        }

        private void ProjectileImpact(Projectile p, Unit t)
        {
            Emit(new SimEvent { Type = SimEventType.ProjectileHit, UnitId = t.Id, OtherId = p.Source?.Id ?? 0, Value = p.Id, Point = p.Position, Key = p.Visual, PlayerId = -1 });
            if (p.IsAttack)
            {
                if (p.Source != null) ApplyAttackHit(p.Source, t, p.Attack);
                return;
            }
            if (t.Team != p.Team && t.IsMagicImmune && !(p.Context.PiercesMagicImmunity || p.Def.PiercesMagicImmunity)) return;
            var ctx = p.Context;
            ctx.Target = t;
            ctx.Point = t.Position;
            ctx.Depth++;
            ctx.Projectile = p;
            ExecuteEffects(p.Def.OnHit, ctx);

            // Chain bounces (e.g. chain lightning).
            if (p.BouncesLeft > 0)
            {
                p.Hit.Add(t.Id);
                var list = RentList();
                UnitsInRadius(t.Position, p.Def.BounceRange, list);
                Unit next = null; float best = float.MaxValue;
                foreach (var c in list)
                {
                    if (p.Hit.Contains(c.Id) || c == t || p.Source == null || !MatchesTeam(p.Source, c, p.Def.Team) || (c.TargetTypeOf() & p.Def.Types) == 0 || c.Invulnerable) continue;
                    float d = Vector2.DistanceSquared(c.Position, t.Position);
                    if (d < best) { best = d; next = c; }
                }
                ReturnList(list);
                if (next != null)
                {
                    var np = new Projectile
                    {
                        Id = _nextProjectileId++, Kind = ProjectileKind.Tracking, Source = p.Source, Team = p.Team, Target = next,
                        Position = t.Position, Speed = p.Speed, Dodgeable = p.Dodgeable, BouncesLeft = p.BouncesLeft - 1, Def = p.Def,
                        Context = p.Context, Visual = p.Visual, Hit = p.Hit, LastKnownTargetPos = next.Position,
                    };
                    Projectiles.Add(np);
                    Emit(new SimEvent { Type = SimEventType.ProjectileLaunch, UnitId = t.Id, OtherId = next.Id, Key = np.Visual, Point = np.Position, Point2 = next.Position, Value = np.Speed, Value2 = np.Id, PlayerId = -1 });
                }
            }
        }

        private void UpdateLinear(Projectile p, float dt)
        {
            float step = p.Speed * dt;
            var start = p.Position;
            p.Position += p.Direction * step;
            p.Travelled += step;

            var list = RentList();
            UnitsInRadius(p.Position, p.Width * 0.5f + step, list);
            var hits = list.ToArray();
            ReturnList(list);
            Array.Sort(hits, (a, b) => Vector2.DistanceSquared(a.Position, start).CompareTo(Vector2.DistanceSquared(b.Position, start)));
            foreach (var u in hits)
            {
                if (p.Hit.Contains(u.Id) || u.Invulnerable || u.HasFlag(StatusFlags.Untargetable)) continue;
                if (p.Source == null || !MatchesTeam(p.Source, u, p.Def.Team) || (u.TargetTypeOf() & p.Def.Types) == 0) continue;
                if (u == p.Source) continue;
                // Swept test against the segment travelled this tick.
                var closest = MathUtil.ClosestPointOnSegment(start, p.Position, u.Position);
                if (Vector2.Distance(closest, u.Position) > p.Width * 0.5f + u.Radius) continue;
                p.Hit.Add(u.Id);
                if (u.Team != p.Team && u.IsMagicImmune && !(p.Context.PiercesMagicImmunity || p.Def.PiercesMagicImmunity)) continue;
                var ctx = p.Context;
                ctx.Target = u;
                ctx.Point = u.Position;
                ctx.Depth++;
                ctx.Projectile = p;
                Emit(new SimEvent { Type = SimEventType.ProjectileHit, UnitId = u.Id, OtherId = p.Source.Id, Value = p.Id, Point = u.Position, Key = p.Visual, PlayerId = -1 });
                ExecuteEffects(p.Def.OnHit, ctx);
                if (p.StopOnFirstHit)
                {
                    p.Done = true;
                    return;
                }
            }

            if (p.Travelled >= p.MaxRange)
            {
                if (p.Def.Returns && !p.Returning && p.Source != null)
                {
                    p.Returning = true;
                    p.Travelled = 0f;
                    p.Hit.Clear();
                    p.Direction = MathUtil.SafeNormalize(p.Source.Position - p.Position, -p.Direction);
                    p.MaxRange = Vector2.Distance(p.Source.Position, p.Position);
                    return;
                }
                p.Done = true;
                var ctx = p.Context; ctx.Point = p.Position; ctx.Target = null; ctx.Depth++; ctx.Projectile = p;
                ExecuteEffects(p.Def.OnEnd, ctx);
                Emit(new SimEvent { Type = SimEventType.ProjectileHit, Value = p.Id, Point = p.Position, Key = p.Visual, PlayerId = -1 });
            }
        }

        // ================================================================== zones

        private void CreateZoneFromEffect(EffectDef e, EffectContext ctx)
        {
            int L = ctx.Level;
            Vector2 center = e.Center == AreaCenter.Caster && ctx.Caster != null ? ctx.Caster.Position
                : e.Center == AreaCenter.Target && ctx.Target != null ? ctx.Target.Position : ctx.Point;
            if (e.Scatter > 0f)
            {
                float a = Rng.NextFloat() * MathUtil.TwoPi, r = e.Scatter * (float)Math.Sqrt(Rng.NextFloat());
                center = Grid.NearestWalkable(center + MathUtil.FromAngle(a) * r);
            }
            var z = new Zone
            {
                Id = _nextZoneId++,
                Def = e,
                Context = ctx,
                Center = center,
                Radius = e.Radius?.Get(L) ?? 3f,
                Delay = e.Type == EffectType.Delayed ? (e.Delay?.Get(L) ?? 1f) : (e.Delay?.Get(L) ?? 0f),
                Remaining = e.Type == EffectType.Delayed ? 0f : (e.ZoneDuration?.Get(L) ?? e.Duration?.Get(L) ?? 3f),
                Interval = Math.Max(0.1f, e.Interval),
                FollowCaster = e.FollowCaster,
                IsDelayed = e.Type == EffectType.Delayed,
            };
            z.TickTimer = 0f;
            if (e.Type == EffectType.Reveal && ctx.Caster != null)
            {
                z.VisionTeam = ctx.Caster.Team;
                z.VisionRadius = z.Radius;
            }
            if (e.Type == EffectType.CreateWall) BuildWall(z, ctx);
            Zones.Add(z);
            Emit(new SimEvent
            {
                Type = SimEventType.ZoneCreated,
                UnitId = ctx.Caster?.Id ?? 0,
                Key = e.Vfx ?? (z.IsDelayed ? "telegraph" : "zone"),
                Point = z.Center,
                Value = z.IsDelayed ? z.Delay : z.Remaining,
                Value2 = z.Radius,
                OtherId = z.Id,
                Flags = (byte)(z.IsDelayed ? 1 : 0),
                Team = ctx.Caster?.Team ?? Team.None,
                PlayerId = -1,
            });
        }

        private void BuildWall(Zone z, EffectContext ctx)
        {
            // Ring wall around the center (radius) — cells on the circle perimeter become blocked.
            z.BlockedCells = new List<(int, int)>();
            float r = z.Radius;
            float circumference = MathUtil.TwoPi * r;
            int steps = Math.Max(12, (int)(circumference / (Grid.CellSize * 0.5f)));
            var seen = new HashSet<int>();
            for (int i = 0; i < steps; i++)
            {
                var p = z.Center + MathUtil.FromAngle(i * MathUtil.TwoPi / steps) * r;
                Grid.ToCell(p, out int cx, out int cy);
                for (int dx = 0; dx <= 1; dx++)
                {
                    int x = cx + dx, y = cy;
                    if (!Grid.InBounds(x, y) || !seen.Add(y * Grid.Width + x)) continue;
                    Grid.AddDynamicBlock(x, y);
                    z.BlockedCells.Add((x, y));
                }
            }
            // Units standing on the wall are nudged inside.
            var list = RentList();
            UnitsInRadius(z.Center, r + 0.8f, list);
            foreach (var u in list)
            {
                if (u.IsStructure) continue;
                if (!Grid.IsWalkable(u.Position))
                {
                    var inward = MathUtil.SafeNormalize(z.Center - u.Position, Vector2.UnitX);
                    u.Position = Grid.NearestWalkable(u.Position + inward * 0.8f, 4);
                }
            }
            ReturnList(list);
        }

        private void UpdateZones(float dt)
        {
            for (int i = Zones.Count - 1; i >= 0; i--)
            {
                var z = Zones[i];
                if (z.FollowCaster && z.Context.Caster != null && !z.Context.Caster.Dead) z.Center = z.Context.Caster.Position;
                if (z.Delay > 0f)
                {
                    z.Delay -= dt;
                    if (z.Delay > 0f) continue;
                    if (z.IsDelayed)
                    {
                        var ctx = z.Context; ctx.Point = z.Center; ctx.Target = null; ctx.Depth++;
                        ExecuteEffects(z.Def.Effects, ctx);
                        EndZone(z);
                        Zones.RemoveAt(i);
                        continue;
                    }
                }
                z.TickTimer -= dt;
                if (z.TickTimer <= 0f && z.Def.Effects != null && z.Def.Type == EffectType.Zone)
                {
                    z.TickTimer += z.Interval;
                    var list = RentList();
                    UnitsInRadius(z.Center, z.Radius, list);
                    var targets = list.ToArray();
                    ReturnList(list);
                    var caster = z.Context.Caster;
                    foreach (var u in targets)
                    {
                        if (caster != null && !MatchesTeam(caster, u, z.Def.Team)) continue;
                        if ((u.TargetTypeOf() & z.Def.Types) == 0) continue;
                        if (caster != null && u.Team != caster.Team && (u.Invulnerable || (u.IsMagicImmune && !z.Def.PiercesMagicImmunity))) continue;
                        var ctx = z.Context; ctx.Target = u; ctx.Point = z.Center; ctx.Depth++;
                        ExecuteEffects(z.Def.Effects, ctx);
                    }
                }
                z.Remaining -= dt;
                if (z.Remaining <= 0f)
                {
                    if (z.Def.OnExpire != null) { var ctx = z.Context; ctx.Point = z.Center; ctx.Target = null; ctx.Depth++; ExecuteEffects(z.Def.OnExpire, ctx); }
                    EndZone(z);
                    Zones.RemoveAt(i);
                }
            }
        }

        private void EndZone(Zone z)
        {
            if (z.BlockedCells != null) foreach (var (x, y) in z.BlockedCells) Grid.RemoveDynamicBlock(x, y);
            Emit(new SimEvent { Type = SimEventType.ZoneEnded, OtherId = z.Id, Point = z.Center, Key = z.Def.Vfx, PlayerId = -1 });
        }
    }
}
