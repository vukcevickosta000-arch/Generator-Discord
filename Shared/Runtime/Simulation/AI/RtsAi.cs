using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    /// <summary>
    /// Player-level AI for the RTS mode. It plays through the same entry points as a human (IssueOrder, TryTrain),
    /// so every cost, requirement, placement and supply rule applies to it; it only reads what a player could know:
    /// its own units, enemy units its team can see, and the map layout (start locations and veins are shown to
    /// every player).
    ///
    /// Plan: saturate the main vein with about five workers and keep a lumber crew; build supply ahead of need; raise
    /// a barracks, then siege and elite buildings and a tower; expand to the nearest free vein (clearing its guards
    /// first); keep production running; defend buildings under attack; attack in waves that grow each time and
    /// retreat when a wave is broken.
    /// </summary>
    public sealed class RtsAi
    {
        private readonly Player _p;
        private readonly BotDifficulty _difficulty;
        private readonly UnitDef _hall, _worker, _supply, _barracks, _tower, _siegeHouse, _eliteHouse;
        private readonly List<UnitDef> _line = new List<UnitDef>();
        private readonly Vector2 _home, _enemyHome, _rally;
        private readonly float _thinkInterval;
        private float _nextThink;
        private int _wave;
        private float _waveStartSupply;
        private enum Stance { Gather, Attack, Defend, Clear }
        private Stance _stance = Stance.Gather;
        private Vector2 _objective;
        private float _lastOrderRefresh;
        private float _lastRetarget;
        /// <summary>Enemy buildings we have seen (id → position), forgotten when seen destroyed or found missing.</summary>
        private readonly Dictionary<int, Vector2> _knownEnemyBuildings = new Dictionary<int, Vector2>();
        /// <summary>Largest enemy army (supply) seen recently, decaying slowly when out of sight.</summary>
        private float _enemyArmySeen;
        private float _lastSeenUpdate;
        private float _lastRebalance;

        // Difficulty is efficiency and judgement, never extra resources: how often the AI acts, how well it
        // saturates and queues, how soon it expands, and whether it sizes up the enemy before attacking.
        private bool Beginner => _difficulty == BotDifficulty.Beginner;
        private bool Expert => _difficulty >= BotDifficulty.Veteran;
        private int WorkersPerVein => Beginner ? 3 : 5;
        private readonly HashSet<int> _rallied = new HashSet<int>();
        private int _scoutIndex;

        public string DebugState => $"{_stance} wave {_wave}";

        public RtsAi(Match m, Player p)
        {
            _p = p;
            _difficulty = p.BotDifficulty;
            var d = m.Data;
            var f = p.RtsFaction;
            _hall = d.Units[f.Hall];
            _worker = d.Units[f.Worker];
            var builds = (_worker.Builds ?? new List<string>()).Where(d.Units.ContainsKey).Select(id => d.Units[id]).ToList();
            bool Trains(UnitDef b, Func<UnitDef, bool> pred) => b.Trains != null && b.Trains.Any(t => d.Units.TryGetValue(t, out var u) && pred(u));
            bool Tagged(UnitDef u, string tag) => u.Tags != null && u.Tags.Contains(tag);
            _supply = builds.Where(b => b.SupplyProvided > 0 && (b.Trains == null || b.Trains.Count == 0)).OrderBy(b => b.GoldCost).FirstOrDefault();
            _barracks = builds.FirstOrDefault(b => (b.Requires == null || b.Requires.Count == 0) && Trains(b, u => u.Kind == UnitKind.Soldier && !Tagged(u, "siege") && !Tagged(u, "elite")));
            _tower = builds.FirstOrDefault(b => b.DamageMax > 0);
            _siegeHouse = builds.FirstOrDefault(b => Trains(b, u => Tagged(u, "siege")));
            _eliteHouse = builds.FirstOrDefault(b => Trains(b, u => Tagged(u, "elite")));
            if (_barracks != null) _line.AddRange(_barracks.Trains.Where(d.Units.ContainsKey).Select(id => d.Units[id]));

            var hall = OwnHalls(m).FirstOrDefault();
            _home = hall?.Position ?? Vector2.Zero;
            var enemyStart = m.Map.StartLocations.Where(s => s.Team != p.Team).Select(s => (Vector2)s.Position).ToList();
            var centre = new Vector2(m.Grid.WorldWidth, m.Grid.WorldHeight) * 0.5f;
            _enemyHome = enemyStart.Count > 0 ? enemyStart.OrderBy(s => Vector2.Distance(s, _home)).First() : centre;
            _rally = m.Grid.NearestWalkable(_home + MathUtil.SafeNormalize(centre - _home, Vector2.UnitX) * 12f);
            // Thinking every 0.5 s measured no better than every 1 s (SimRunner), so Nightmare currently plays like
            // Veteran; see TODO T-031.
            _thinkInterval = Beginner ? 2f : 1f;
            _nextThink = p.Id * 0.25f;
        }

        public void Update(Match m)
        {
            if (_p.Eliminated || m.Time < _nextThink) return;
            _nextThink = m.Time + _thinkInterval;
            var halls = OwnHalls(m).ToList();
            var workers = Own(m, UnitKind.Worker);
            if (halls.Count == 0) RebuildHall(m, workers);
            else Economy(m, halls, workers);
            Construction(m, halls, workers);
            Production(m, halls, workers.Count);
            Army(m);
        }

        // ------------------------------------------------------------------ helpers

        private IEnumerable<Unit> OwnHalls(Match m) => m.Units.Where(u => u.Owner == _p && u.IsAlive && u.Kind == UnitKind.Building && u.UnitDef.DropOffGold && !u.UnderConstruction);
        private List<Unit> Own(Match m, UnitKind k) => m.Units.Where(u => u.Owner == _p && u.IsAlive && u.Kind == k).ToList();
        private List<Unit> OwnBuildings(Match m, UnitDef def) => def == null ? new List<Unit>() : m.Units.Where(u => u.Owner == _p && u.IsAlive && u.DefId == def.Id).ToList();
        private int Count(Match m, UnitDef def) => OwnBuildings(m, def).Count;
        private static bool Tagged(Unit u, string tag) => u.UnitDef?.Tags != null && u.UnitDef.Tags.Contains(tag);
        private float Minutes(Match m) => m.MatchSeconds / 60f;

        /// <summary>Veins a hall of ours stands next to.</summary>
        private List<Unit> OwnedVeins(Match m, List<Unit> halls) =>
            m.Units.Where(v => v.Kind == UnitKind.Resource && v.IsAlive && v.ResourceAmount > 0 && halls.Any(h => Vector2.Distance(h.Position, v.Position) < 16f)).ToList();

        // ------------------------------------------------------------------ economy

        private void Economy(Match m, List<Unit> halls, List<Unit> workers)
        {
            var veins = OwnedVeins(m, halls);
            var perVein = veins.ToDictionary(v => v.Id, v => 0);
            int lumber = 0;
            foreach (var w in workers)
            {
                if (w.CurrentOrder.Type != OrderType.Harvest) continue;
                if (w.Harvesting == HarvestKind.Gold && perVein.ContainsKey(w.HarvestNodeId)) perVein[w.HarvestNodeId]++;
                else if (w.Harvesting == HarvestKind.Lumber) lumber++;
            }
            int lumberWanted = LumberWanted(m);
            foreach (var w in workers)
            {
                if (w.CurrentOrder.Type != OrderType.None || w.OrderQueue.Count > 0) continue;
                var under = veins.Where(v => perVein[v.Id] < WorkersPerVein).OrderBy(v => Vector2.Distance(v.Position, w.Position)).FirstOrDefault();
                if (under != null && (lumber >= lumberWanted || perVein[under.Id] < 3))
                {
                    m.IssueOrder(w, new Order { Type = OrderType.Harvest, UnitId = w.Id, TargetId = under.Id });
                    perVein[under.Id]++;
                }
                else if (!SendToLumber(m, w, halls)) { if (under != null) { m.IssueOrder(w, new Order { Type = OrderType.Harvest, UnitId = w.Id, TargetId = under.Id }); perVein[under.Id]++; } }
                else lumber++;
            }
            // Rebalance one worker every two seconds: from the forest to an unsaturated vein when lumber piles up, or
            // from an over-full vein to the forest.
            if (m.Time - _lastRebalance < 2f) return;
            _lastRebalance = m.Time;
            if (lumber > lumberWanted)
            {
                var vein = veins.FirstOrDefault(v => perVein[v.Id] < WorkersPerVein);
                var chopper = workers.FirstOrDefault(w => w.CurrentOrder.Type == OrderType.Harvest && w.Harvesting == HarvestKind.Lumber && !w.IsGathering && w.CarryLumber == 0);
                if (vein != null && chopper != null) m.IssueOrder(chopper, new Order { Type = OrderType.Harvest, UnitId = chopper.Id, TargetId = vein.Id });
            }
            else if (lumber < lumberWanted)
            {
                var spare = workers.FirstOrDefault(w => w.CurrentOrder.Type == OrderType.Harvest && w.Harvesting == HarvestKind.Gold && perVein.TryGetValue(w.HarvestNodeId, out int n) && n > WorkersPerVein && !w.IsGathering && w.CarryGold == 0);
                if (spare != null) SendToLumber(m, spare, halls);
            }
        }

        /// <summary>Lumber crew size: grows with the game, shrinks when lumber piles up (blood-iron is the usual bottleneck).</summary>
        /// <summary>Every hall is gone: without a drop-off no harvest is possible, so raise a new one first.</summary>
        private void RebuildHall(Match m, List<Unit> workers)
        {
            if (workers.Count == 0 || PlannedBuild(m, _hall) || OwnBuildings(m, _hall).Count > 0) return;
            if (!m.CanAfford(_p, _hall, out _)) return;
            var builder = workers[0];
            var vein = m.Units.Where(v => v.Kind == UnitKind.Resource && v.IsAlive && v.ResourceAmount > 0)
                              .OrderBy(v => Vector2.Distance(v.Position, builder.Position)).FirstOrDefault();
            var site = vein != null ? ExpansionSite(m, vein) : builder.Position;
            if (m.CanPlaceBuilding(_p, _hall, site, builder, out _))
                m.IssueOrder(builder, new Order { Type = OrderType.Build, UnitId = builder.Id, ItemId = _hall.Id, Point = site });
        }

        private int LumberWanted(Match m)
        {
            if (Beginner) return 3;
            if (_p.Lumber > 700) return 1;
            if (_p.Lumber > 350) return 2;
            return Math.Min(6, 3 + (int)(Minutes(m) / 4f));
        }

        private bool SendToLumber(Match m, Unit w, List<Unit> halls)
        {
            var near = halls.OrderBy(h => Vector2.Distance(h.Position, w.Position)).FirstOrDefault();
            var from = near?.Position ?? w.Position;
            int best = -1;
            float bd = float.MaxValue;
            m.ForEachTreeNear(from, 24f, i =>
            {
                float d = Vector2.Distance(m.TreePosition(i), from);
                if (d < bd) { bd = d; best = i; }
            });
            if (best < 0) return false;
            m.IssueOrder(w, new Order { Type = OrderType.Harvest, UnitId = w.Id, Point = m.TreePosition(best) });
            return w.CurrentOrder.Type == OrderType.Harvest;
        }

        // ------------------------------------------------------------------ construction

        private void Construction(Match m, List<Unit> halls, List<Unit> workers)
        {
            if (halls.Count == 0 || workers.Count == 0) return;
            float min = Minutes(m);
            // One new building per think; the first wanted one gets the resources.
            var want = NextBuilding(m, halls, workers.Count, min);
            if (want.def == null) return;
            if (!m.CanAfford(_p, want.def, out _) || !m.RequirementsMet(_p, want.def, out _)) return;
            var builder = workers.Where(w => w.CurrentOrder.Type != OrderType.Build && w.CarryGold == 0 && !w.IsGathering)
                                 .OrderBy(w => w.Harvesting == HarvestKind.Lumber ? 0 : 1).ThenBy(w => Vector2.Distance(w.Position, want.at)).FirstOrDefault();
            if (builder == null) return;
            var spot = want.exact ? want.at : FindSpot(m, want.def, halls[0], want.at);
            if (spot == null) return;
            m.IssueOrder(builder, new Order { Type = OrderType.Build, UnitId = builder.Id, ItemId = want.def.Id, Point = spot.Value });
            m.LogLine($"{_p.Name} AI: {want.def.Name} by worker {builder.Id} (supply {_p.SupplyUsed}/{_p.SupplyCap}, order {builder.CurrentOrder.Type})");
            // Go back to mining afterwards (self-building factions free the worker at once).
            var vein = OwnedVeins(m, halls).FirstOrDefault();
            if (builder.CurrentOrder.Type == OrderType.Build && vein != null)
                m.IssueOrder(builder, new Order { Type = OrderType.Harvest, UnitId = builder.Id, TargetId = vein.Id, Queue = true });
        }

        private (UnitDef def, Vector2 at, bool exact) NextBuilding(Match m, List<Unit> halls, int workers, float min)
        {
            var hall = halls[0];
            int barracks = Count(m, _barracks);
            bool pendingSupply = OwnBuildings(m, _supply).Any(b => b.UnderConstruction) || PlannedBuild(m, _supply);
            int production = barracks + Count(m, _siegeHouse) + Count(m, _eliteHouse) + halls.Count;
            int headroom = 2 + 2 * production;
            if (_supply != null && _p.SupplyCap < m.Rules.RtsMaxSupply && _p.SupplyCap - _p.SupplyUsed < headroom && !pendingSupply)
                return (_supply, hall.Position, false);
            if (PlannedBuild(m, null)) return default; // a worker is already walking to a site
            if (_barracks != null && barracks == 0 && (workers >= 7 || min >= 1.2f)) return (_barracks, hall.Position, false);
            if (barracks == 0) return default;
            if (_tower != null && Count(m, _tower) == 0 && min >= 4f && _difficulty != BotDifficulty.Beginner) return (_tower, _rally, false);
            if (_siegeHouse != null && Count(m, _siegeHouse) == 0 && min >= 5f) return (_siegeHouse, hall.Position, false);
            var expansion = ExpansionVein(m, halls, min);
            if (expansion != null) return (_hall, ExpansionSite(m, expansion), true);
            if (_eliteHouse != null && Count(m, _eliteHouse) == 0 && min >= 7f) return (_eliteHouse, hall.Position, false);
            if (barracks < 2 && min >= 6f && _p.Gold > 350) return (_barracks, hall.Position, false);
            if (barracks < 3 && min >= 11f && _p.Gold > 500 && Expert) return (_barracks, hall.Position, false);
            return default;
        }

        private bool PlannedBuild(Match m, UnitDef def) =>
            m.Units.Any(u => u.Owner == _p && u.IsAlive && u.Kind == UnitKind.Worker && u.CurrentOrder.Type == OrderType.Build && u.CurrentOrder.TargetId == 0
                             && (def == null || u.CurrentOrder.ItemId == def.Id));

        /// <summary>The next vein to expand to: after 7 minutes, or earlier when the main vein runs low.</summary>
        private Unit ExpansionVein(Match m, List<Unit> halls, float min)
        {
            if (halls.Count >= 3) return null;
            var owned = OwnedVeins(m, halls);
            bool running = owned.Sum(v => v.ResourceAmount) < 4000;
            float first = Beginner ? 11f : 7f;
            if (!(min >= first && halls.Count < 2) && !(min >= first * 2f && halls.Count < 3) && !running) return null;
            if (OwnBuildings(m, _hall).Any(h => h.UnderConstruction)) return null;
            return m.Units.Where(v => v.Kind == UnitKind.Resource && v.IsAlive && v.ResourceAmount > 2000 && !owned.Contains(v)
                                      && !m.Units.Any(b => b.IsAlive && b.Kind == UnitKind.Building && Vector2.Distance(b.Position, v.Position) < 14f))
                          .OrderBy(v => Vector2.Distance(v.Position, _home)).FirstOrDefault(v => !GuardedByNeutrals(m, v));
        }

        private static bool GuardedByNeutrals(Match m, Unit vein) => m.Units.Any(n => n.IsNeutral && n.IsAlive && Vector2.Distance(n.Position, vein.Position) < 16f);

        private Vector2 ExpansionSite(Match m, Unit vein)
        {
            // A hall 11.5 m from the vein on the side facing our base, like the starting layout.
            var toHome = MathUtil.SafeNormalize(_home - vein.Position, Vector2.UnitX);
            for (int i = 0; i < 16; i++)
            {
                float a = (i % 2 == 0 ? 1 : -1) * (i / 2) * 0.35f;
                var dir = new Vector2(toHome.X * MathF.Cos(a) - toHome.Y * MathF.Sin(a), toHome.X * MathF.Sin(a) + toHome.Y * MathF.Cos(a));
                for (float d = 10f; d <= 12.5f; d += 1.25f)
                {
                    var pos = vein.Position + dir * d;
                    if (m.CanPlaceBuilding(_p, _hall, pos, null, out _)) return pos;
                }
            }
            return vein.Position + toHome * 11.5f;
        }

        /// <summary>
        /// A spot near the anchor where the building fits with a 1.3 m ring of open ground around it, so bases never
        /// wall themselves in, and away from the lane between each hall and its vein.
        /// </summary>
        private Vector2? FindSpot(Match m, UnitDef def, Unit hall, Vector2 anchor)
        {
            var veins = m.Units.Where(v => v.Kind == UnitKind.Resource && v.IsAlive && Vector2.Distance(v.Position, hall.Position) < 16f).ToList();
            var toEnemy = MathUtil.SafeNormalize(_enemyHome - anchor, Vector2.UnitX);
            float baseAngle = MathUtil.AngleOf(toEnemy);
            float start = anchor == hall.Position ? hall.Radius + def.CollisionRadius + 2.5f : 0f;
            for (float r = start; r <= start + 16f; r += 1.5f)
            {
                int steps = Math.Max(1, (int)(MathUtil.TwoPi * Math.Max(r, 1f) / 2f));
                for (int i = 0; i < steps; i++)
                {
                    float a = baseAngle + (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * (MathUtil.TwoPi / steps);
                    var pos = anchor + MathUtil.FromAngle(a) * r;
                    if (!m.CanPlaceBuilding(_p, def, pos, null, out _)) continue;
                    if (veins.Any(v => DistToSegment(pos, hall.Position, v.Position) < def.CollisionRadius + 2.5f)) continue;
                    if (!RingOpen(m, pos, def.CollisionRadius * 0.85f + 1.3f)) continue;
                    return pos;
                }
            }
            return null;
        }

        private static bool RingOpen(Match m, Vector2 pos, float r)
        {
            foreach (int i in m.Grid.CellsInDisc(pos, r))
                if (!m.Grid.IsWalkableCell(i % m.Grid.Width, i / m.Grid.Width)) return false;
            return true;
        }

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.LengthSquared() > 1e-6f ? MathUtil.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        // ------------------------------------------------------------------ production

        private void Production(Match m, List<Unit> halls, int workers)
        {
            var veins = OwnedVeins(m, halls);
            int workersWanted = Math.Min(40, veins.Count * WorkersPerVein + LumberWanted(m) + 1);
            // Keep money for the next building, and for sites workers are walking to (the cost is paid on arrival),
            // before spending it on units.
            var next = halls.Count > 0 ? NextBuilding(m, halls, workers, Minutes(m)).def : null;
            int reserveGold = next?.GoldCost ?? 0, reserveLumber = next?.LumberCost ?? 0;
            foreach (var w in m.Units)
            {
                if (w.Owner != _p || !w.IsAlive || w.CurrentOrder.Type != OrderType.Build || w.CurrentOrder.TargetId != 0) continue;
                if (!m.Data.Units.TryGetValue(w.CurrentOrder.ItemId ?? "", out var site)) continue;
                reserveGold += site.GoldCost;
                reserveLumber += site.LumberCost;
            }
            foreach (var hall in halls)
            {
                if ((hall.TrainQueue?.Count ?? 0) > 0 || workers >= workersWanted) continue;
                if (_p.Gold - _worker.GoldCost < reserveGold) continue;
                if (m.TryTrain(hall, _worker.Id)) workers++;
            }
            foreach (var b in m.Units.Where(u => u.Owner == _p && u.IsAlive && u.Kind == UnitKind.Building && !u.UnderConstruction && u.UnitDef.Trains != null && !u.UnitDef.DropOffGold).ToList())
            {
                if (!_rallied.Contains(b.Id)) { m.IssueOrder(b, new Order { Type = OrderType.SetRally, UnitId = b.Id, Point = _rally }); _rallied.Add(b.Id); }
                if ((b.TrainQueue?.Count ?? 0) >= 1) continue;
                var pick = PickUnit(m, b);
                if (pick == null) continue;
                if (_p.Gold - pick.GoldCost < reserveGold || _p.Lumber - pick.LumberCost < reserveLumber) continue;
                m.TryTrain(b, pick.Id);
            }
        }

        private UnitDef PickUnit(Match m, Unit building)
        {
            var options = building.UnitDef.Trains.Where(m.Data.Units.ContainsKey).Select(id => m.Data.Units[id]).Where(d => m.RequirementsMet(_p, d, out _)).ToList();
            if (options.Count == 0) return null;
            var army = Own(m, UnitKind.Soldier);
            if (options.Any(o => o.Tags != null && o.Tags.Contains("siege")))
                return army.Count(u => Tagged(u, "siege")) < (Minutes(m) > 14f ? 4 : 2) ? options[0] : null;
            if (options.Any(o => o.Tags != null && o.Tags.Contains("elite")))
                return army.Count(u => Tagged(u, "elite")) < 4 ? options[0] : null;
            // Line units: keep melee and ranged even.
            int melee = army.Count(u => u.UnitDef.AttackType == AttackType.Melee && !Tagged(u, "elite"));
            int ranged = army.Count(u => u.UnitDef.AttackType == AttackType.Ranged && !Tagged(u, "siege"));
            var wantType = melee <= ranged ? AttackType.Melee : AttackType.Ranged;
            return options.FirstOrDefault(o => o.AttackType == wantType) ?? options[0];
        }

        // ------------------------------------------------------------------ army

        private void RememberEnemyBuildings(Match m)
        {
            foreach (var u in m.Units)
            {
                if (u.Team == _p.Team || u.Team == Team.Neutral || u.Kind != UnitKind.Building) continue;
                if (u.IsAlive && m.IsVisibleTo(u, _p.Team)) _knownEnemyBuildings[u.Id] = u.Position;
            }
            // Forget buildings that are gone, or whose ground we can see empty.
            foreach (var id in _knownEnemyBuildings.Keys.ToList())
            {
                var u = m.GetUnit(id);
                bool gone = u == null || u.Dead;
                if (gone && m.Vision.IsVisible(_p.Team, _knownEnemyBuildings[id])) _knownEnemyBuildings.Remove(id);
                else if (u != null && u.Dead) _knownEnemyBuildings.Remove(id);
            }
        }

        private void Army(Match m)
        {
            RememberEnemyBuildings(m);
            var army = Own(m, UnitKind.Soldier);
            int supply = army.Sum(u => u.UnitDef.SupplyCost);
            UpdateEnemyArmyEstimate(m);
            var threat = ThreatNearBase(m);
            if (threat != null)
            {
                if (_stance != Stance.Defend) { _stance = Stance.Defend; _lastOrderRefresh = -99f; }
                _objective = threat.Value;
            }
            else if (_stance == Stance.Defend) { _stance = Stance.Gather; _lastOrderRefresh = -99f; }

            if (_stance == Stance.Gather)
            {
                var guarded = m.Units.Where(v => v.Kind == UnitKind.Resource && v.IsAlive && GuardedByNeutrals(m, v)).OrderBy(v => Vector2.Distance(v.Position, _home)).FirstOrDefault();
                if (Minutes(m) >= 5f && supply >= 12 && guarded != null && Vector2.Distance(guarded.Position, _home) < 60f)
                {
                    _stance = Stance.Clear;
                    _objective = guarded.Position;
                    _lastOrderRefresh = -99f;
                }
                else if ((supply >= AttackThreshold() && (Beginner || supply >= 1.25f * _enemyArmySeen))
                         || (_p.SupplyCap >= m.Rules.RtsMaxSupply - 4 && _p.SupplyUsed >= _p.SupplyCap - 4 && supply >= 20))
                {
                    _stance = Stance.Attack;
                    _wave++;
                    _waveStartSupply = supply;
                    _objective = AttackTarget(m);
                    _lastOrderRefresh = -99f;
                }
            }
            else if (_stance == Stance.Clear)
            {
                if (!m.Units.Any(n => n.IsNeutral && n.IsAlive && Vector2.Distance(n.Position, _objective) < 16f) || supply < 6)
                { _stance = Stance.Gather; _lastOrderRefresh = -99f; }
            }
            else if (_stance == Stance.Attack)
            {
                if (supply < _waveStartSupply * 0.35f || (!Beginner && EnemyNearArmy(m, army) > supply * 1.6f))
                { _stance = Stance.Gather; _lastOrderRefresh = -99f; }
                else if (m.Time - _lastRetarget > 5f)
                {
                    var next = AttackTarget(m);
                    if (Vector2.DistanceSquared(next, _objective) > 4f) { _objective = next; _lastOrderRefresh = -99f; }
                }
            }

            if (Expert) Micro(m, army);
            if (m.Time - _lastOrderRefresh < 3f) return;
            _lastOrderRefresh = m.Time;
            var stage = StagePoint(m, army);
            foreach (var u in army)
            {
                var goal = _stance == Stance.Gather ? _rally : _stance == Stance.Attack ? stage : _objective;
                if (_stance == Stance.Gather)
                {
                    // Idle units drift back to the rally point; fighting ones are left alone.
                    if (u.CurrentOrder.Type == OrderType.None && Vector2.Distance(u.Position, _rally) > 6f) m.IssueOrder(u, Order.AttackMoveTo(u.Id, goal));
                    continue;
                }
                if (u.CurrentOrder.Type == OrderType.AttackMove && Vector2.Distance(u.CurrentOrder.Point, goal) < 2f) continue;
                if (u.CurrentOrder.Type == OrderType.AttackUnit || Engaged(m, u)) continue;
                m.IssueOrder(u, Order.AttackMoveTo(u.Id, goal));
            }
        }

        /// <summary>
        /// Veteran and Nightmare micro: ranged units focus the weakest enemy in reach, and badly hurt units step back
        /// toward home while enemies are close (they rejoin at the rally point).
        /// </summary>
        private void Micro(Match m, List<Unit> army)
        {
            var near = new List<Unit>();
            foreach (var u in army)
            {
                near.Clear();
                m.UnitsInRadius(u.Position, u.Stats.AttackRange + u.Radius + 2f, near);
                bool threatened = false;
                Unit weakest = null;
                foreach (var e in near)
                {
                    if (e.Team == _p.Team || e.Team == Team.Neutral || e.IsImmobile || !m.CanAttackTarget(u, e, out bool deny) || deny) continue;
                    threatened = true;
                    if (Vector2.Distance(u.Position, e.Position) > m.AttackReach(u, e)) continue;
                    if (weakest == null || e.Hp < weakest.Hp) weakest = e;
                }
                if (!threatened) continue;
                if (u.HpFraction < 0.25f && u.UnitDef.SupplyCost >= 2)
                {
                    if (u.CurrentOrder.Type != OrderType.Move) m.IssueOrder(u, Order.MoveTo(u.Id, _rally));
                    continue;
                }
                // Never switch targets mid-swing: a cancelled wind-up is lost damage.
                if (u.UnitDef.AttackType != AttackType.Ranged || weakest == null || u.Action == ActionState.AttackWindup) continue;
                var cur = m.GetUnit(u.CurrentOrder.Type == OrderType.AttackUnit ? u.CurrentOrder.TargetId : u.AttackTargetId);
                if (cur == weakest || (cur != null && cur.Hp <= weakest.Hp * 1.3f && Vector2.Distance(u.Position, cur.Position) <= m.AttackReach(u, cur))) continue;
                m.IssueOrder(u, Order.Attack(u.Id, weakest.Id));
            }
        }

        /// <summary>
        /// Attacks advance in stages: the whole army attack-moves to a point a short way ahead of its centre, so fast
        /// units do not arrive alone and the siege keeps up. Close to the objective it goes straight in.
        /// </summary>
        private Vector2 StagePoint(Match m, List<Unit> army)
        {
            if (army.Count == 0) return _objective;
            var marching = army.Where(u => u.CurrentOrder.Type != OrderType.AttackUnit).ToList();
            if (marching.Count == 0) return _objective;
            var c = marching.Aggregate(Vector2.Zero, (s, u) => s + u.Position) / marching.Count;
            float d = Vector2.Distance(c, _objective);
            if (d < 18f) return _objective;
            float spread = marching.Max(u => Vector2.Distance(u.Position, c));
            float step = spread > 10f ? 4f : 14f; // strung out: let the tail catch up
            return m.Grid.NearestWalkable(c + (_objective - c) / d * step);
        }

        /// <summary>A unit mid-swing or fighting under attack-move: a new order now would only cancel its attack.</summary>
        private static bool Engaged(Match m, Unit u)
        {
            if (u.Action == ActionState.AttackWindup || u.Action == ActionState.AttackBackswing) return true;
            var t = u.CurrentOrder.Type == OrderType.AttackMove ? m.GetUnit(u.AttackTargetId) : null;
            return t != null && t.IsAlive && Vector2.Distance(t.Position, u.Position) <= u.AcquisitionRange + t.Radius;
        }

        private int AttackThreshold() => Math.Min(60, (Beginner ? 34 : 26) + 6 * _wave);

        private void UpdateEnemyArmyEstimate(Match m)
        {
            float visible = 0f;
            foreach (var e in m.Units)
                if (e.IsAlive && e.Kind == UnitKind.Soldier && e.Team != _p.Team && e.Team != Team.Neutral && m.IsVisibleTo(e, _p.Team)) visible += e.UnitDef.SupplyCost;
            // What we have not seen for a while may have grown or died: let the memory fade over about two minutes.
            float dt = m.Time - _lastSeenUpdate;
            _lastSeenUpdate = m.Time;
            _enemyArmySeen = Math.Max(visible, _enemyArmySeen * (float)Math.Exp(-dt / 120f));
        }

        /// <summary>Supply of visible enemy soldiers within 14 m of our army's centre.</summary>
        private float EnemyNearArmy(Match m, List<Unit> army)
        {
            if (army.Count == 0) return 0f;
            var c = army.Aggregate(Vector2.Zero, (s, u) => s + u.Position) / army.Count;
            float sum = 0f;
            foreach (var e in m.Units)
                if (e.IsAlive && e.Kind == UnitKind.Soldier && e.Team != _p.Team && e.Team != Team.Neutral && m.IsVisibleTo(e, _p.Team) && Vector2.Distance(e.Position, c) < 14f)
                    sum += e.UnitDef.SupplyCost;
            return sum;
        }

        private Vector2? ThreatNearBase(Match m)
        {
            var mine = m.Units.Where(u => u.Owner == _p && u.IsAlive && u.Kind == UnitKind.Building).ToList();
            foreach (var e in m.Units)
            {
                if (!e.IsAlive || e.Team == _p.Team || e.Team == Team.Neutral || e.IsImmobile || !m.IsVisibleTo(e, _p.Team)) continue;
                if (mine.Any(b => Vector2.Distance(b.Position, e.Position) < 18f)) return e.Position;
            }
            return null;
        }

        /// <summary>
        /// The known enemy building nearest the army; with none known, scout: the enemy start and the ground around
        /// it, then every vein on the map in turn.
        /// </summary>
        private Vector2 AttackTarget(Match m)
        {
            _lastRetarget = m.Time;
            var army = Own(m, UnitKind.Soldier);
            var centroid = army.Count > 0 ? army.Aggregate(Vector2.Zero, (s, u) => s + u.Position) / army.Count : _home;
            if (_knownEnemyBuildings.Count > 0)
                return _knownEnemyBuildings.OrderBy(kv => Vector2.Distance(kv.Value, centroid)).ThenBy(kv => kv.Key).First().Value;
            var spots = ScoutSpots(m);
            if (Vector2.Distance(centroid, spots[_scoutIndex % spots.Count]) < 8f) _scoutIndex++;
            return spots[_scoutIndex % spots.Count];
        }

        private List<Vector2> _scoutSpots;

        private List<Vector2> ScoutSpots(Match m)
        {
            if (_scoutSpots != null) return _scoutSpots;
            _scoutSpots = new List<Vector2> { _enemyHome };
            for (int i = 0; i < 6; i++) _scoutSpots.Add(m.Grid.NearestWalkable(_enemyHome + MathUtil.FromAngle(i * MathUtil.TwoPi / 6f) * 14f));
            _scoutSpots.AddRange(m.Map.ResourceNodes.Select(r => m.Grid.NearestWalkable((Vector2)r.Position + MathUtil.SafeNormalize(_home - (Vector2)r.Position, Vector2.UnitX) * 4f))
                                                    .OrderBy(p => Vector2.Distance(p, _enemyHome)));
            return _scoutSpots;
        }
    }
}
