using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    /// <summary>
    /// A bot's RTS hero. The RTS AI moves it with the army. This brain spends its ability points, casts in fights with
    /// the MOBA bot's choices (<see cref="BotBrain.CastInFight"/>), and makes an idle hero take on enemies close by.
    /// </summary>
    public sealed class RtsHeroBrain : IUnitBrain
    {
        private readonly BotBrain _caster;
        private readonly List<Unit> _near = new List<Unit>();

        public RtsHeroBrain(BotDifficulty difficulty, ulong seed) { _caster = new BotBrain(difficulty, seed); }

        public void Think(Match m, Unit u, float dt)
        {
            if (u.Dead) return;
            if (u.AbilityPoints > 0) BotBrain.LevelAbilities(m, u);
            var target = m.GetUnit(u.CurrentOrder.Type == OrderType.AttackUnit ? u.CurrentOrder.TargetId : u.AttackTargetId);
            if (target == null || target.Dead || target.Team == u.Team || target.Kind == UnitKind.Resource) target = NearestEnemy(m, u, 8f);
            if (target == null) return;
            // Spells go to the most valuable enemy in reach, not to whatever is being hit (often a temporary summon).
            if (_caster.CastInFight(m, u, SpellTarget(m, u, 10f) ?? target)) return;
            if (u.CurrentOrder.Type == OrderType.None) m.IssueOrder(u, Order.Attack(u.Id, target.Id));
        }

        /// <summary>Heroes first, then the costliest units; temporary summons and buildings are never worth a spell.</summary>
        private Unit SpellTarget(Match m, Unit u, float range)
        {
            _near.Clear();
            m.UnitsInRadius(u.Position, range, _near);
            Unit best = null;
            float bestScore = float.MinValue;
            foreach (var e in _near)
            {
                if (e.Team == u.Team || e.Team == Team.Neutral || e.IsStructure || e.Kind == UnitKind.Resource || e.Lifetime > 0f || e.IsIllusion) continue;
                if (e.UnitDef?.Tags != null && e.UnitDef.Tags.Contains("summoned")) continue;
                if (!m.IsVisibleTo(e, u.Team) || !m.CanAttackTarget(u, e, out bool deny) || deny) continue;
                float score = (e.IsHero ? 20f + e.Level : e.UnitDef?.SupplyCost ?? 1) * 10f - Vector2.Distance(e.Position, u.Position) - e.HpFraction * 5f;
                if (score > bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        private Unit NearestEnemy(Match m, Unit u, float range)
        {
            _near.Clear();
            m.UnitsInRadius(u.Position, range, _near);
            Unit best = null;
            float bestDist = float.MaxValue;
            foreach (var e in _near)
            {
                if (e.Team == u.Team || e.Team == Team.Neutral || e.Kind == UnitKind.Resource || !m.IsVisibleTo(e, u.Team)) continue;
                if (!m.CanAttackTarget(u, e, out bool deny) || deny) continue;
                float d = Vector2.Distance(e.Position, u.Position) + (e.IsStructure ? 4f : 0f);
                if (d < bestDist) { bestDist = d; best = e; }
            }
            return best;
        }
    }
}
