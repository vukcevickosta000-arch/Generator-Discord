using System;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    /// <summary>Lane creep: march the lane, fight what it meets, respond to hero aggro (classic creep rules).</summary>
    public sealed class CreepBrain : IUnitBrain
    {
        public void Think(Match m, Unit u, float dt)
        {
            if (u.Dead || u.LaneIndex < 0) return;
            var wps = m.Map.Lanes[u.LaneIndex].Waypoints;

            // Hero aggro draw has priority for a short window.
            Unit target = null;
            if (u.AggroTargetId != 0 && m.Time < u.AggroUntil)
            {
                var a = m.GetUnit(u.AggroTargetId);
                if (a != null && m.CanAttackTarget(u, a, out bool d1) && !d1 && Vector2.Distance(a.Position, u.Position) < u.AcquisitionRange + 3f) target = a;
            }
            if (target == null)
            {
                u.AggroTargetId = 0;
                var cur = m.GetUnit(u.AttackTargetId);
                if (cur != null && m.CanAttackTarget(u, cur, out bool d2) && !d2 && Vector2.Distance(cur.Position, u.Position) <= u.AcquisitionRange + 2f)
                    target = cur;
                else
                    target = FindCreepTarget(m, u);
            }

            if (target != null)
            {
                if (u.CurrentOrder.Type != OrderType.AttackUnit || u.CurrentOrder.TargetId != target.Id)
                {
                    u.AttackTargetId = target.Id;
                    m.IssueOrder(u, Order.Attack(u.Id, target.Id));
                }
                return;
            }

            // Advance along the lane.
            int dir = u.Team == Team.Dawn ? 1 : -1;
            u.WaypointIndex = MathUtil.Clamp(u.WaypointIndex, 0, wps.Count - 1);
            Vector2 wp = wps[u.WaypointIndex];
            if (Vector2.Distance(u.Position, wp) < 3f)
            {
                int next = u.WaypointIndex + dir;
                if (next >= 0 && next < wps.Count) { u.WaypointIndex = next; wp = wps[next]; }
            }
            if (u.CurrentOrder.Type != OrderType.AttackMove || Vector2.DistanceSquared(u.CurrentOrder.Point, wp) > 0.01f)
                m.IssueOrder(u, Order.AttackMoveTo(u.Id, wp));
        }

        private static Unit FindCreepTarget(Match m, Unit u)
        {
            var list = m.UnitsInRadius(u.Position, u.AcquisitionRange);
            Unit best = null;
            float bestScore = float.MaxValue;
            foreach (var t in list)
            {
                if (t.Team == u.Team || t.Team == Team.Neutral || t.Invulnerable || t.Kind == UnitKind.Fountain) continue;
                if (!m.CanAttackTarget(u, t, out bool deny) || deny) continue;
                float d = Vector2.Distance(u.Position, t.Position);
                // Creeps prefer other creeps; heroes are only chosen when closer by a margin.
                float score = d + (t.IsHero ? 3.5f : 0f) + (t.IsStructure ? 2f : 0f);
                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }
    }

    /// <summary>Towers and fountains: hold fire on the current target unless an enemy hero attacks an allied hero.</summary>
    public sealed class TowerBrain : IUnitBrain
    {
        public void Think(Match m, Unit u, float dt)
        {
            if (u.Dead) return;
            Unit target = null;
            if (u.AggroTargetId != 0 && m.Time < u.AggroUntil)
            {
                var a = m.GetUnit(u.AggroTargetId);
                if (a != null && m.CanAttackTarget(u, a, out _) && Vector2.Distance(a.Position, u.Position) <= m.AttackReach(u, a)) target = a;
            }
            if (target == null)
            {
                u.AggroTargetId = 0;
                var cur = m.GetUnit(u.AttackTargetId);
                if (cur != null && m.CanAttackTarget(u, cur, out bool deny) && !deny && Vector2.Distance(cur.Position, u.Position) <= m.AttackReach(u, cur))
                    target = cur;
                else
                    target = FindTowerTarget(m, u);
            }
            if (target == null)
            {
                if (u.CurrentOrder.Type != OrderType.None) m.IssueOrder(u, Order.StopOrder(u.Id));
                u.AttackTargetId = 0;
                return;
            }
            if (u.CurrentOrder.Type != OrderType.AttackUnit || u.CurrentOrder.TargetId != target.Id)
            {
                u.AttackTargetId = target.Id;
                m.IssueOrder(u, Order.Attack(u.Id, target.Id));
            }
        }

        private static Unit FindTowerTarget(Match m, Unit u)
        {
            var list = m.UnitsInRadius(u.Position, u.Stats.AttackRange + u.Radius + 1f);
            Unit best = null;
            float bestScore = float.MaxValue;
            foreach (var t in list)
            {
                if (t.Team == u.Team || t.Team == Team.Neutral || t.Invulnerable || t.IsStructure) continue;
                if (!m.CanAttackTarget(u, t, out bool deny) || deny) continue;
                float d = Vector2.Distance(u.Position, t.Position);
                if (d > m.AttackReach(u, t)) continue;
                // Towers prefer creeps and summons over heroes (heroes can tank only when creeps are absent).
                float score = d + (t.IsHero ? 12f : 0f);
                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }
    }

    /// <summary>Jungle creatures: sleep at camp, retaliate as a group, leash back home.</summary>
    public sealed class NeutralBrain : IUnitBrain
    {
        private float _returningUntil;

        public void Think(Match m, Unit u, float dt)
        {
            if (u.Dead) return;
            float fromHome = Vector2.Distance(u.Position, u.HomePosition);
            if (m.Time < _returningUntil)
            {
                if (fromHome < 0.6f) { _returningUntil = 0; u.Hp = u.Stats.MaxHp; }
                else if (u.CurrentOrder.Type != OrderType.Move) m.IssueOrder(u, Order.MoveTo(u.Id, u.HomePosition));
                return;
            }

            // Retaliate against the most recent attacker.
            if (m.Time - u.LastAttackedTime < 0.5f && u.LastAttackerId != 0 && u.AggroTargetId == 0)
            {
                var attacker = m.GetUnit(u.LastAttackerId);
                if (attacker != null && attacker.Team != Team.Neutral)
                {
                    u.AggroTargetId = attacker.Id;
                    u.AggroUntil = m.Time + 4f;
                    m.AlertCamp(u, attacker);
                }
            }

            var target = u.AggroTargetId != 0 ? m.GetUnit(u.AggroTargetId) : null;
            if (target != null && (target.Dead || !m.CanAttackTarget(u, target, out _))) target = null;
            if (target != null && m.Time - u.LastAttackedTime < 3.5f) u.AggroUntil = m.Time + 4f;
            if (target != null && m.Time > u.AggroUntil) target = null;

            if (target != null && fromHome < u.LeashRange)
            {
                if (u.CurrentOrder.Type != OrderType.AttackUnit || u.CurrentOrder.TargetId != target.Id)
                    m.IssueOrder(u, Order.Attack(u.Id, target.Id));
                return;
            }
            // Leash: go home and ignore aggro while returning.
            if (target != null || fromHome > u.LeashRange)
            {
                u.AggroTargetId = 0;
                _returningUntil = m.Time + 6f;
                m.IssueOrder(u, Order.MoveTo(u.Id, u.HomePosition));
                return;
            }
            u.AggroTargetId = 0;
            if (fromHome > 0.8f && u.CurrentOrder.Type == OrderType.None) m.IssueOrder(u, Order.MoveTo(u.Id, u.HomePosition));
        }
    }

    /// <summary>Summons: if player controlled do nothing unless idle; otherwise guard the summoner and fight nearby enemies.</summary>
    public sealed class SummonBrain : IUnitBrain
    {
        private readonly bool _controlled;
        public SummonBrain(bool controlled) { _controlled = controlled; }

        public void Think(Match m, Unit u, float dt)
        {
            if (u.Dead) return;
            var tags = u.UnitDef?.Tags;
            bool stationary = tags != null && Array.IndexOf(tags, "stationary") >= 0;
            bool guard = tags != null && Array.IndexOf(tags, "guard") >= 0;
            if (stationary)
            {
                // Totems, cauldrons: never move; fight only what is already in reach.
                if (u.Stats.DamageMax <= 0f) return;
                var near = m.FindAttackTarget(u, Math.Max(0.5f, u.Stats.AttackRange + 0.5f));
                if (near != null && u.CurrentOrder.Type != OrderType.AttackUnit) m.IssueOrder(u, Order.Attack(u.Id, near.Id));
                return;
            }
            if (guard)
            {
                // Revenants and wardens hold the ground they were raised on.
                var home = u.HomePosition;
                if (Vector2.Distance(u.Position, home) > 9f) { m.IssueOrder(u, Order.MoveTo(u.Id, home)); return; }
                if (u.CurrentOrder.Type == OrderType.AttackUnit) return;
                var foe = m.FindAttackTarget(u, u.AcquisitionRange + 2f);
                if (foe != null && Vector2.Distance(foe.Position, home) < 10f) m.IssueOrder(u, Order.Attack(u.Id, foe.Id));
                return;
            }
            if (_controlled && u.Owner != null && !u.Owner.IsBot && u.CurrentOrder.Type != OrderType.None) return;
            if (u.CurrentOrder.Type == OrderType.AttackUnit) return;
            var target = m.FindAttackTarget(u, u.AcquisitionRange);
            if (target != null) { m.IssueOrder(u, Order.Attack(u.Id, target.Id)); return; }
            var s = u.Summoner;
            if (s != null && !s.Dead && Vector2.Distance(s.Position, u.Position) > 4f)
                m.IssueOrder(u, Order.MoveTo(u.Id, s.Position + MathUtil.FromAngle(u.Id) * 1.5f));
        }
    }
}
