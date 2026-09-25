using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    /// <summary>
    /// Hero bot. Utility-style state selection (retreat / fight / farm lane / push / defend / shop) re-evaluated a few
    /// times per second. Difficulty scales reaction time, last-hit precision, spell usage and aggression.
    /// Bots only use information their team can see (IsVisibleTo) - they do not cheat fog of war.
    /// </summary>
    public sealed class BotBrain : IUnitBrain
    {
        private enum Mode { Lane, Retreat, Fight, Push, Defend, Jungle, Objective }

        private readonly BotDifficulty _difficulty;
        private readonly DeterministicRandom _rng;
        private Mode _mode = Mode.Lane;
        private int _lane = -1;
        private float _nextDecision;
        private float _reaction;
        private float _lastHitSkill;
        private float _aggression;
        private float _spellChance;
        private float _retreatHp;
        private int _buildIndex;
        private List<string> _build;
        private Unit _fightTarget;
        private float _lastShopTime;
        private bool _bossCommitted;

        public string DebugState => _mode.ToString();

        public BotBrain(BotDifficulty difficulty, ulong seed)
        {
            _difficulty = difficulty;
            _rng = new DeterministicRandom(seed);
            switch (difficulty)
            {
                case BotDifficulty.Beginner: _reaction = 0.9f; _lastHitSkill = 0.35f; _aggression = 0.25f; _spellChance = 0.35f; _retreatHp = 0.2f; break;
                case BotDifficulty.Normal: _reaction = 0.45f; _lastHitSkill = 0.65f; _aggression = 0.5f; _spellChance = 0.7f; _retreatHp = 0.3f; break;
                case BotDifficulty.Veteran: _reaction = 0.25f; _lastHitSkill = 0.85f; _aggression = 0.65f; _spellChance = 0.9f; _retreatHp = 0.3f; break;
                default: _reaction = 0.12f; _lastHitSkill = 0.97f; _aggression = 0.8f; _spellChance = 1f; _retreatHp = 0.28f; break;
            }
        }

        public void Think(Match m, Unit u, float dt)
        {
            var p = u.Owner;
            if (p == null) return;
            if (_lane < 0) _lane = AssignLane(m, p);
            if (_build == null) _build = BuildOrder(m, u);

            LevelAbilities(m, u);
            Shop(m, u);
            if (u.Dead)
            {
                if (_difficulty >= BotDifficulty.Veteran && p.Gold > m.BuybackCost(p) * 2.5f && m.MatchSeconds > 1500 && m.Time >= p.BuybackCooldownUntil)
                    m.SubmitOrder(p, new Order { Type = OrderType.Buyback });
                return;
            }
            if (m.Phase == MatchPhase.PreGame && m.Time < -10f)
            {
                // Walk to lane during pre-game.
                MoveToLaneFront(m, u, safe: true);
                return;
            }
            UseConsumables(m, u);
            if (m.Time < _nextDecision && _mode != Mode.Fight) { ExecuteMode(m, u); return; }
            _nextDecision = m.Time + _reaction * (0.7f + _rng.NextFloat() * 0.6f);
            ChooseMode(m, u);
            ExecuteMode(m, u);
        }

        // ------------------------------------------------------------------ decisions

        private void ChooseMode(Match m, Unit u)
        {
            float hp = u.HpFraction;
            var fountain = Fountain(m, u.Team);
            if (_mode == Mode.Retreat)
            {
                bool healed = (hp > 0.9f && (u.Stats.MaxMana <= 0 || u.Mana / Math.Max(1, u.Stats.MaxMana) > 0.6f))
                              || (hp > 0.6f && u.FindStatus("draught_regen") != null);
                if (!healed) return;
            }
            var enemies = VisibleEnemyHeroes(m, u, 11f);
            var allies = AlliedHeroes(m, u, 11f);
            float threat = enemies.Sum(e => e.HpFraction * (1 + e.Level * 0.1f));
            float power = hp * (1 + u.Level * 0.1f) + allies.Sum(a => a.HpFraction * (1 + a.Level * 0.1f));
            bool underEnemyTower = UnderTower(m, u.Position, Match.OtherTeam(u.Team));

            if (hp < _retreatHp || (hp < 0.45f && enemies.Count > allies.Count + 1) || (underEnemyTower && hp < 0.5f && _mode != Mode.Push))
            {
                _mode = Mode.Retreat;
                return;
            }
            // Fight if there is a weak or outnumbered enemy hero.
            var weak = enemies.Where(e => !e.Invulnerable).OrderBy(e => e.HpFraction).FirstOrDefault();
            if (weak != null)
            {
                bool favorable = power * (0.7f + _aggression) > threat || weak.HpFraction < 0.3f + _aggression * 0.15f;
                bool safe = !UnderTower(m, weak.Position, weak.Team) || (weak.HpFraction < 0.2f && hp > 0.5f);
                if (favorable && safe) { _fightTarget = weak; _mode = Mode.Fight; return; }
            }
            // Defend base / towers under pressure.
            var threatened = m.Units.FirstOrDefault(s => s.Team == u.Team && s.IsStructure && !s.Dead && m.Time - s.LastAttackedTime < 3f && s.Kind != UnitKind.Fountain
                                                        && Vector2.Distance(s.Position, u.Position) < 45f);
            if (threatened != null && hp > 0.5f) { _mode = Mode.Defend; _fightTarget = null; return; }
            // Vharoth: one bot per team breaks seals when it is safe; a strong, healthy team takes on the Titan.
            if (enemies.Count == 0 && WantsObjective(m, u)) { _mode = Mode.Objective; return; }
            // Push when the lane has no enemy heroes and we are healthy and past early game.
            _mode = m.MatchSeconds > 600 && enemies.Count == 0 && hp > 0.6f ? Mode.Push : Mode.Lane;
            _ = fountain;
        }

        private void ExecuteMode(Match m, Unit u)
        {
            if (_mode != Mode.Objective) _bossCommitted = false;
            switch (_mode)
            {
                case Mode.Retreat: Retreat(m, u); break;
                case Mode.Fight: Fight(m, u); break;
                case Mode.Defend: Defend(m, u); break;
                case Mode.Push: Farm(m, u, push: true); break;
                case Mode.Objective: Objective(m, u); break;
                default: Farm(m, u, push: false); break;
            }
        }

        // ------------------------------------------------------------------ behaviours

        private void Retreat(Match m, Unit u)
        {
            var fountain = Fountain(m, u.Team);
            // Escape abilities while chased.
            if (u.HpFraction > 0.18f && VisibleEnemyHeroes(m, u, 12f).Count == 0 && HasItem(u, "item_crimson_draught"))
            {
                // Heal up with consumables instead of walking all the way home.
                _mode = Mode.Lane;
                return;
            }
            var chasers = VisibleEnemyHeroes(m, u, 8f);
            if (chasers.Count > 0 && u.HpFraction < 0.4f) TryCast(m, u, "escape", chasers[0]);
            if (Vector2.Distance(u.Position, fountain) > 4f && (u.CurrentOrder.Type != OrderType.Move || Vector2.Distance(u.CurrentOrder.Point, fountain) > 1f))
                m.IssueOrder(u, Order.MoveTo(u.Id, fountain));
        }

        private void Fight(Match m, Unit u)
        {
            var t = _fightTarget;
            if (t == null || t.Dead || !m.IsVisibleTo(t, u.Team) || Vector2.Distance(t.Position, u.Position) > 18f || u.HpFraction < _retreatHp)
            {
                _mode = u.HpFraction < _retreatHp ? Mode.Retreat : Mode.Lane;
                _fightTarget = null;
                return;
            }
            if (u.Action == ActionState.CastWindup || u.Action == ActionState.Channeling || u.Motion != null) return;
            if (_rng.NextFloat() < _spellChance)
            {
                if (TryCast(m, u, "engage", t) || TryCast(m, u, "stun", t) || TryCast(m, u, "ultimate", t) || TryCast(m, u, "nuke", t) || TryCast(m, u, "aoe", t) || TryCast(m, u, "buff", t))
                    return;
            }
            if (u.CurrentOrder.Type != OrderType.AttackUnit || u.CurrentOrder.TargetId != t.Id)
                m.IssueOrder(u, Order.Attack(u.Id, t.Id));
        }

        // ------------------------------------------------------------------ Vharoth

        private bool WantsObjective(Match m, Unit u)
        {
            if (m.VharothState == VharothPhase.Awakened || m.VharothState == VharothPhase.BloodMoon)
            {
                if (m.Vharoth == null || m.Vharoth.Dead) return false;
                var team = m.Players.Where(p => p.Team == u.Team && p.Hero != null && !p.Hero.Dead).Select(p => p.Hero).ToList();
                // Once engaged, stay until personally low; to start, the team must be strong and healthy.
                if (_bossCommitted) return u.HpFraction > _retreatHp + 0.1f && team.Count >= 3;
                return team.Count >= 4 && team.Average(h => h.Level) >= 12f && team.Average(h => h.HpFraction) > 0.6f && u.HpFraction > 0.6f;
            }
            if (u.HpFraction < 0.7f) return false;
            if (m.VharothState == VharothPhase.Tremors)
            {
                // The lowest-slot living bot of the team is the seal breaker.
                var breaker = m.Players.Where(p => p.Team == u.Team && p.IsBot && p.Hero != null && !p.Hero.Dead)
                    .OrderBy(p => p.Slot).FirstOrDefault();
                return breaker == u.Owner && NearestSeal(m, u) != null;
            }
            return false;
        }

        private static Unit NearestSeal(Match m, Unit u) =>
            m.Units.Where(x => x.DefId == Match.SealUnitId && x.IsAlive).OrderBy(x => Vector2.Distance(x.Position, u.Position)).FirstOrDefault();

        private void Objective(Match m, Unit u)
        {
            if (!WantsObjective(m, u)) { _mode = Mode.Lane; return; }
            if (u.Action == ActionState.Channeling || u.Action == ActionState.CastWindup || u.Motion != null) return;
            if (m.VharothState == VharothPhase.Tremors)
            {
                var seal = NearestSeal(m, u);
                int slot = u.Abilities.FindIndex(a => a.Def.Id == "vharoth_break_seal");
                if (seal == null || slot < 0) { _mode = Mode.Lane; return; }
                if (Vector2.Distance(u.Position, seal.Position) > 10f || !m.IsVisibleTo(seal, u.Team))
                {
                    if (u.CurrentOrder.Type != OrderType.Move) m.IssueOrder(u, Order.MoveTo(u.Id, seal.Position));
                    return;
                }
                if (u.CurrentOrder.Type != OrderType.CastUnit) m.IssueOrder(u, Order.CastUnitOrder(u.Id, slot, seal.Id));
                return;
            }
            var boss = m.Vharoth;
            if (boss == null || boss.Dead) { _mode = Mode.Lane; return; }
            if (!_bossCommitted)
            {
                // Gather outside the leash, on the side of our base, until three of us are there.
                Vector2 pit = m.Map.BossPit;
                var rally = pit + MathUtil.SafeNormalize(Fountain(m, u.Team) - pit, Vector2.UnitX) * (m.Rules.VharothLeash + 3f);
                int gathered = m.Players.Count(p => p.Team == u.Team && p.Hero != null && !p.Hero.Dead
                                                    && Vector2.Distance(p.Hero.Position, rally) < 8f);
                int alive = m.Players.Count(p => p.Team == u.Team && p.Hero != null && !p.Hero.Dead);
                if (gathered < Math.Min(3, alive))
                {
                    if (Vector2.Distance(u.Position, rally) > 3f && u.CurrentOrder.Type != OrderType.Move) m.IssueOrder(u, Order.MoveTo(u.Id, rally));
                    return;
                }
                _bossCommitted = true;
            }
            if (Vector2.Distance(u.Position, boss.Position) < 12f && _rng.NextFloat() < _spellChance * 0.5f
                && (TryCast(m, u, "nuke", boss) || TryCast(m, u, "aoe", boss) || TryCast(m, u, "buff", boss)))
                return;
            if (u.CurrentOrder.Type != OrderType.AttackUnit || u.CurrentOrder.TargetId != boss.Id)
                m.IssueOrder(u, Order.Attack(u.Id, boss.Id));
        }

        private void Defend(Match m, Unit u)
        {
            var s = m.Units.Where(x => x.Team == u.Team && x.IsStructure && !x.Dead && m.Time - x.LastAttackedTime < 4f && x.Kind != UnitKind.Fountain)
                .OrderBy(x => Vector2.Distance(x.Position, u.Position)).FirstOrDefault();
            if (s == null) { _mode = Mode.Lane; return; }
            var enemy = VisibleEnemyHeroes(m, u, 14f).FirstOrDefault();
            if (enemy != null && u.HpFraction > 0.4f) { _fightTarget = enemy; _mode = Mode.Fight; return; }
            if (Vector2.Distance(u.Position, s.Position) > 6f) m.IssueOrder(u, Order.AttackMoveTo(u.Id, s.Position));
            else Farm(m, u, push: true);
        }

        private void Farm(Match m, Unit u, bool push)
        {
            if (u.Action == ActionState.AttackWindup) return;
            // Never stand inside enemy tower fire unless creeps are tanking it and we are pushing.
            var danger = TowerThreat(m, u, u.Position);
            if (danger != null && (!push || danger.AttackTargetId == u.Id || !CreepsTankingStructure(m, danger, u.Team)))
            {
                var away = MathUtil.SafeNormalize(u.Position - danger.Position, Vector2.UnitX);
                var safe = m.Grid.NearestWalkable(danger.Position + away * (m.AttackReach(danger, u) + 3f));
                if (u.CurrentOrder.Type != OrderType.Move || Vector2.Distance(u.CurrentOrder.Point, safe) > 1.5f) m.IssueOrder(u, Order.MoveTo(u.Id, safe));
                return;
            }
            float myDmg = u.Stats.AverageDamage;
            // Last hit / deny candidates near the lane front.
            Unit lastHit = null, deny = null, harass = null, pushTarget = null;
            float bestLh = float.MaxValue;
            var list = m.UnitsInRadius(u.Position, 9f).ToArray();
            foreach (var c in list)
            {
                if (c.Dead || c.IsHero || c.Kind == UnitKind.Ward) continue;
                float effective = myDmg * MathUtil.ArmorMultiplier(c.Stats.Armor);
                // Account for projectile travel time for ranged heroes.
                float travel = u.ProjectileSpeed > 0 ? Vector2.Distance(u.Position, c.Position) / u.ProjectileSpeed : 0f;
                float incoming = c.IsCreep ? IncomingDps(m, c) * (u.Stats.AttackPoint + travel) : 0f;
                float margin = 1f + (1f - _lastHitSkill) * 0.9f * (_rng.NextFloat() - 0.3f);
                bool unsafeTarget = TowerThreat(m, u, c.Position) != null && !push;
                if (c.Team != u.Team && c.Team != Team.Neutral && !c.IsStructure && !unsafeTarget)
                {
                    if (c.Hp - incoming * _lastHitSkill <= effective * margin && c.Hp < bestLh) { bestLh = c.Hp; lastHit = c; }
                    if (push && pushTarget == null) pushTarget = c;
                }
                else if (c.Team == u.Team && c.IsCreep && c.HpFraction < m.Rules.CreepDenyThreshold && c.Hp - incoming <= effective * margin && _lastHitSkill > 0.5f)
                    deny = c;
                else if (push && c.IsStructure && c.Team != u.Team && !c.Invulnerable && CreepsTankingStructure(m, c, u.Team))
                    pushTarget = c;
            }
            if (lastHit != null) { AttackIfNotAlready(m, u, lastHit); return; }
            if (deny != null) { AttackIfNotAlready(m, u, deny); return; }

            // Harass visible enemy heroes that stray into range (not under their tower).
            var enemyHero = VisibleEnemyHeroes(m, u, u.Stats.AttackRange + 2.5f).FirstOrDefault();
            if (enemyHero != null && !UnderTower(m, enemyHero.Position, enemyHero.Team) && _rng.NextFloat() < _aggression * 0.6f)
                harass = enemyHero;
            if (harass != null) { AttackIfNotAlready(m, u, harass); return; }

            if (push)
            {
                if (_rng.NextFloat() < _spellChance * 0.2f && u.Mana > u.Stats.MaxMana * 0.6f) TryCast(m, u, "farm", pushTarget);
                if (pushTarget != null) { AttackIfNotAlready(m, u, pushTarget); return; }
            }
            MoveToLaneFront(m, u, safe: !push);
        }

        private static float IncomingDps(Match m, Unit c)
        {
            float dps = 0f;
            foreach (var a in m.UnitsInRadius(c.Position, 7f))
            {
                if (a.Team == c.Team || a.Dead || a.IsHero) continue;
                if (a.AttackTargetId == c.Id && a.Stats.AttackTime > 0) dps += a.Stats.AverageDamage * MathUtil.ArmorMultiplier(c.Stats.Armor) / a.Stats.AttackTime;
            }
            return dps;
        }

        private static bool CreepsTankingStructure(Match m, Unit structure, Team myTeam)
        {
            foreach (var c in m.UnitsInRadius(structure.Position, 10f))
                if (c.Team == myTeam && c.IsCreep && !c.Dead) return true;
            return false;
        }

        private static void AttackIfNotAlready(Match m, Unit u, Unit t)
        {
            if (u.CurrentOrder.Type == OrderType.AttackUnit && u.CurrentOrder.TargetId == t.Id) return;
            m.IssueOrder(u, Order.Attack(u.Id, t.Id));
        }

        private void MoveToLaneFront(Match m, Unit u, bool safe)
        {
            var lane = m.Map.Lanes[_lane];
            var wps = lane.Waypoints;
            // Front = the most advanced allied creep in the lane; otherwise our outer tower.
            Vector2 front = FrontPosition(m, u);
            Vector2 enemyDir = LocalLaneDirection(wps, front, u.Team);
            float backoff = u.AttackType == AttackType.Ranged ? 4.5f : 2.5f;
            if (safe) backoff += 1.5f;
            var goal = m.Grid.NearestWalkable(front - enemyDir * backoff);
            // Pull back along the lane until the spot is outside enemy tower fire.
            for (int i = 0; i < 12 && TowerThreat(m, u, goal) != null; i++) goal = m.Grid.NearestWalkable(goal - enemyDir * 2.5f);
            if (Vector2.Distance(u.Position, goal) > 1.5f &&
                (u.CurrentOrder.Type != OrderType.Move || Vector2.Distance(u.CurrentOrder.Point, goal) > 2f))
                m.IssueOrder(u, Order.MoveTo(u.Id, goal));
            else if (Vector2.Distance(u.Position, goal) <= 1.5f && u.CurrentOrder.Type == OrderType.Move)
                m.IssueOrder(u, Order.StopOrder(u.Id));
        }

        /// <summary>Direction toward the enemy base along the lane segment nearest to 'p'.</summary>
        private static Vector2 LocalLaneDirection(List<JVec2> wps, Vector2 p, Team team)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < wps.Count - 1; i++)
            {
                var c = MathUtil.ClosestPointOnSegment(wps[i], wps[i + 1], p);
                float d = Vector2.DistanceSquared(c, p);
                if (d < bd) { bd = d; best = i; }
            }
            var dir = MathUtil.SafeNormalize((Vector2)wps[best + 1] - (Vector2)wps[best], Vector2.UnitX);
            return team == Team.Dawn ? dir : -dir;
        }

        /// <summary>Returns an enemy tower that would shoot a hero standing at 'pos' (null if safe).</summary>
        private static Unit TowerThreat(Match m, Unit u, Vector2 pos)
        {
            foreach (var t in m.Units)
            {
                if ((t.Kind != UnitKind.Tower && t.Kind != UnitKind.Fountain) || t.Dead || t.Team == u.Team || t.Team == Team.Neutral) continue;
                float reach = t.Stats.AttackRange + t.Radius + u.Radius + 1.5f;
                if (Vector2.DistanceSquared(t.Position, pos) > reach * reach) continue;
                return t;
            }
            return null;
        }

        private void UseConsumables(Match m, Unit u)
        {
            if (u.Dead || u.Inventory == null || m.AtBase(u)) return;
            bool enemyNear = VisibleEnemyHeroes(m, u, 9f).Count > 0;
            for (int s = 0; s < u.Inventory.Length; s++)
            {
                var it = u.Inventory[s];
                if (it?.Active == null || !it.Active.IsReady) continue;
                string id = it.Def.Id;
                if (id == "item_crimson_draught" && u.HpFraction < 0.55f && !enemyNear && u.FindStatus("draught_regen") == null)
                { m.IssueOrder(u, Order.CastNoTargetOrder(u.Id, Order.ItemSlotBase + s)); return; }
                if (id == "item_mana_ember" && u.Stats.MaxMana > 0 && u.Mana / u.Stats.MaxMana < 0.4f && !enemyNear && u.FindStatus("ember_regen") == null)
                { m.IssueOrder(u, Order.CastNoTargetOrder(u.Id, Order.ItemSlotBase + s)); return; }
            }
        }

        private Vector2 FrontPosition(Match m, Unit u)
        {
            var wps = m.Map.Lanes[_lane].Waypoints;
            Unit best = null;
            int bestIdx = u.Team == Team.Dawn ? -1 : int.MaxValue;
            float bestProgress = float.MinValue;
            foreach (var c in m.Units)
            {
                if (c.Dead || !c.IsCreep || c.Team != u.Team || c.LaneIndex != _lane) continue;
                // Progress = waypoint index then closeness to the next waypoint.
                int idx = c.WaypointIndex;
                float progress = (u.Team == Team.Dawn ? idx : wps.Count - 1 - idx) * 1000f - Vector2.Distance(c.Position, wps[MathUtil.Clamp(idx, 0, wps.Count - 1)]);
                if (progress > bestProgress) { bestProgress = progress; best = c; }
            }
            _ = bestIdx;
            if (best != null) return best.Position;
            // No creeps: stand near the furthest allied tower of the lane.
            var laneName = m.Map.Lanes[_lane].Name;
            var tower = m.Units.Where(s => s.Kind == UnitKind.Tower && s.Team == u.Team && !s.Dead && s.Lane == laneName)
                .OrderBy(s => s.Tier).FirstOrDefault();
            return tower?.Position ?? (u.Team == Team.Dawn ? (Vector2)wps[1] : (Vector2)wps[wps.Count - 2]);
        }

        // ------------------------------------------------------------------ abilities

        /// <summary>Uses an ability whose BotUsage tag matches the intent. Returns true if an order was issued.</summary>
        private bool TryCast(Match m, Unit u, string intent, Unit target)
        {
            for (int i = 0; i < u.Abilities.Count; i++)
            {
                var ab = u.Abilities[i];
                var d = ab.Def;
                if (ab.Level <= 0 || d.Targeting == TargetingMode.Passive || !ab.IsReady) continue;
                string usage = d.BotUsage ?? "";
                bool match = usage.Contains(intent) || (intent == "ultimate" && d.IsUltimate && usage.Length > 0 && !usage.Contains("escape"));
                if (!match) continue;
                if (u.Mana < m.ManaCostOf(u, ab) || (m.HealthCostOf(u, ab) > 0 && u.HpFraction < (intent == "farm" ? 0.75f : 0.35f))) continue;
                if (d.IsUltimate && intent != "ultimate" && target != null && target.HpFraction > 0.7f && _difficulty < BotDifficulty.Nightmare) continue;
                float range = m.EffectiveCastRange(u, ab);
                switch (d.Targeting)
                {
                    case TargetingMode.NoTarget:
                    {
                        float r = d.AoeRadius?.Get(ab.Level) ?? 4f;
                        if (target != null && usage.Contains("aoe") && Vector2.Distance(target.Position, u.Position) > r) continue;
                        m.IssueOrder(u, Order.CastNoTargetOrder(u.Id, i));
                        return true;
                    }
                    case TargetingMode.Unit:
                    {
                        Unit t = usage.Contains("ally") || usage.Contains("self") ? u : target;
                        if (t == null) continue;
                        if (!m.ValidTarget(u, ab, t, out _)) continue;
                        if (Vector2.Distance(t.Position, u.Position) > range + 4f) continue;
                        m.IssueOrder(u, Order.CastUnitOrder(u.Id, i, t.Id));
                        return true;
                    }
                    case TargetingMode.Point:
                    case TargetingMode.UnitOrPoint:
                    case TargetingMode.Vector:
                    {
                        Vector2 point;
                        // Blinks tagged "escape,engage" flee when escaping and jump onto the target when engaging.
                        if (intent == "escape" || (usage.Contains("escape") && !usage.Contains("engage")))
                        {
                            var fountain = Fountain(m, u.Team);
                            point = u.Position + MathUtil.SafeNormalize(fountain - u.Position, Vector2.UnitX) * Math.Max(3f, range);
                        }
                        else
                        {
                            if (target == null) continue;
                            if (Vector2.Distance(target.Position, u.Position) > range + 2f) continue;
                            // Lead moving targets slightly (better bots predict more).
                            var lead = target.IsMoving ? MathUtil.FromAngle(target.Facing) * target.Stats.MoveSpeed * 0.35f * _lastHitSkill : Vector2.Zero;
                            point = target.Position + lead;
                        }
                        m.IssueOrder(u, Order.CastPointOrder(u.Id, i, point));
                        return true;
                    }
                }
            }
            return false;
        }

        private static void LevelAbilities(Match m, Unit u)
        {
            int guard = 4;
            while (u.AbilityPoints > 0 && guard-- > 0)
            {
                // Ultimate first whenever possible, then the lowest-level basic ability (Q > W > E on ties).
                AbilityInstance pick = u.Abilities.FirstOrDefault(a => a.Def.IsUltimate && m.CanLevelAbility(u, a));
                if (pick == null)
                    pick = u.Abilities.Where(a => !a.Def.IsUltimate && m.CanLevelAbility(u, a)).OrderBy(a => a.Level).ThenBy(a => a.Index).FirstOrDefault();
                if (pick == null) return;
                m.IssueOrder(u, Order.LevelUp(u.Id, pick.Index));
            }
        }

        private void Shop(Match m, Unit u)
        {
            var p = u.Owner;
            if (_build == null || _buildIndex >= _build.Count || m.Time - _lastShopTime < 1f) return;
            _lastShopTime = m.Time;
            // Skip items already owned.
            while (_buildIndex < _build.Count && OwnsItem(u, _build[_buildIndex])) _buildIndex++;
            if (_buildIndex >= _build.Count) return;
            if (!m.Data.Items.TryGetValue(_build[_buildIndex], out var def)) { _buildIndex++; return; }
            int cost = m.PurchaseCost(u, def, out _, out bool secret);
            if (secret) { _buildIndex++; return; }
            if (p.Gold >= cost)
            {
                m.IssueOrder(u, Order.Buy(u.Id, def.Id));
                _buildIndex++;
            }
            // Healing potion when low and at base.
            if (u.HpFraction < 0.5f && m.AtBase(u) && p.Gold > 150 && m.Data.Items.ContainsKey("item_crimson_draught") && !OwnsItem(u, "item_crimson_draught"))
                m.IssueOrder(u, Order.Buy(u.Id, "item_crimson_draught"));
        }

        private static bool HasItem(Unit u, string id) => u.Inventory != null && u.Inventory.Any(i => i?.Def.Id == id);

        private static bool OwnsItem(Unit u, string id)
        {
            if (u.Inventory == null) return false;
            return u.Inventory.Any(i => i?.Def.Id == id) || u.Stash.Any(i => i?.Def.Id == id) || u.Backpack.Any(i => i?.Def.Id == id);
        }

        private static List<string> BuildOrder(Match m, Unit u)
        {
            var rec = u.HeroDef?.RecommendedItems;
            var list = new List<string>();
            if (rec != null)
                foreach (var key in new[] { "start", "early", "core", "late" })
                    if (rec.TryGetValue(key, out var items)) list.AddRange(items);
            return list;
        }

        // ------------------------------------------------------------------ helpers

        private static int AssignLane(Match m, Player p)
        {
            int lanes = m.Map.Lanes.Count;
            if (lanes == 0) return 0;
            // Slot 0 mid, 1 top, 2 bot, 3 top, 4 bot (lane order in map: top, mid, bot).
            int[] order = { 1, 0, 2, 0, 2 };
            int idx = order[Math.Max(0, p.Slot) % order.Length];
            return Math.Min(idx, lanes - 1);
        }

        private static Vector2 Fountain(Match m, Team team)
        {
            var b = m.Map.Bases.FirstOrDefault(x => x.Team == team);
            return b != null ? (Vector2)b.Fountain : Vector2.Zero;
        }

        private static List<Unit> VisibleEnemyHeroes(Match m, Unit u, float radius)
        {
            var result = new List<Unit>();
            foreach (var o in m.UnitsInRadius(u.Position, radius))
                if (o.IsHero && o.Team != u.Team && !o.Dead && m.IsVisibleTo(o, u.Team)) result.Add(o);
            return result;
        }

        private static List<Unit> AlliedHeroes(Match m, Unit u, float radius)
        {
            var result = new List<Unit>();
            foreach (var o in m.UnitsInRadius(u.Position, radius))
                if (o.IsHero && o.Team == u.Team && o != u && !o.Dead) result.Add(o);
            return result;
        }

        private static bool UnderTower(Match m, Vector2 pos, Team towerTeam)
        {
            foreach (var t in m.UnitsInRadius(pos, 11f))
                if (t.Kind == UnitKind.Tower && t.Team == towerTeam && !t.Dead && Vector2.Distance(t.Position, pos) <= t.Stats.AttackRange + 1.5f) return true;
            return false;
        }
    }
}
