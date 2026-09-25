using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    /// <summary>
    /// RTS mode ("War of the Ancients"): every player starts with a hall and workers, harvests gold (shown to players
    /// as blood-iron) from mines (blood-iron veins) and lumber from trees, constructs buildings, trains units within a supply cap and wins by razing every enemy
    /// building. All of it runs through the same authoritative simulation as the MOBA: the client only sends
    /// orders, and every cost, requirement, placement and supply check happens here.
    /// </summary>
    public sealed partial class Match
    {
        public bool IsRts => Mode.Kind == GameModeKind.Rts;

        /// <summary>How close a worker must stand to a trunk to chop it.</summary>
        public const float ChopReach = 1.6f;
        /// <summary>Extra distance (beyond both radii) at which a worker counts as touching a mine or drop-off.</summary>
        private const float TouchMargin = 0.45f;

        private readonly List<Vector2> _rtsPathScratch = new List<Vector2>(64);
        private readonly Dictionary<int, RtsAi> _rtsAi = new Dictionary<int, RtsAi>();

        /// <summary>The AI running a bot (or abandoned) player's side, if any.</summary>
        public RtsAi RtsAiOf(Player p) => p != null && _rtsAi.TryGetValue(p.Id, out var ai) ? ai : null;

        /// <summary>Hands a player's base to the RTS AI (bots at start; humans who abandon).</summary>
        public void AssignRtsAi(Player p)
        {
            if (!IsRts || p == null || p.RtsFaction == null || _rtsAi.ContainsKey(p.Id)) return;
            _rtsAi[p.Id] = new RtsAi(this, p);
        }

        // =================================================================== setup

        private void SpawnRtsStart()
        {
            foreach (var rn in Map.ResourceNodes)
            {
                if (!Data.Units.TryGetValue(rn.UnitId ?? "", out var def)) continue;
                var node = CreateUnit(def, Team.Neutral, rn.Position);
                node.ResourceAmount = rn.Amount > 0 ? rn.Amount : def.ResourceAmount;
                node.StructureId = rn.Id;
                BlockFootprint(node);
            }
            var perTeam = new Dictionary<Team, int>();
            foreach (var p in Players.OrderBy(p => p.Slot).ThenBy(p => p.Id))
            {
                p.RtsFaction = PickRtsFaction(p);
                p.Gold = Rules.RtsStartingGold;
                p.Lumber = Rules.RtsStartingLumber;
                p.HeroLocked = true;
                perTeam.TryGetValue(p.Team, out int n);
                perTeam[p.Team] = n + 1;
                var starts = Map.StartLocations.Where(s => s.Team == p.Team).ToList();
                if (starts.Count == 0 || p.RtsFaction == null) { LogLine($"No RTS start for player {p.Id} ({p.Team})"); continue; }
                SpawnRtsBase(p, starts[n % starts.Count]);
            }
            foreach (var u in _spawnQueue) { Units.Add(u); UnitById[u.Id] = u; }
            _spawnQueue.Clear();
            foreach (var u in Units) RecomputeFlags(u);
            UpdateSupply();
            foreach (var p in Players) if (p.IsBot) AssignRtsAi(p);
        }

        private RtsFactionDef PickRtsFaction(Player p)
        {
            string wanted = p.Id < Config.Players.Count ? Config.Players[p.Id].RtsFaction : null;
            if (!string.IsNullOrEmpty(wanted) && Data.RtsFactions.TryGetValue(wanted, out var f) && f.Playable) return f;
            var pool = Data.RtsFactionOrder.Select(id => Data.RtsFactions[id]).Where(x => x.Playable).ToList();
            return pool.Count > 0 ? pool[Rng.Range(0, pool.Count)] : null;
        }

        private void SpawnRtsBase(Player p, RtsStartDef start)
        {
            var f = p.RtsFaction;
            if (!Data.Units.TryGetValue(f.Hall, out var hallDef) || !Data.Units.TryGetValue(f.Worker, out var workerDef)) return;
            var hall = CreateUnit(hallDef, p.Team, start.Position, start.Facing * MathUtil.Deg2Rad, p);
            BlockFootprint(hall);
            // Workers line up between the hall and its gold mine.
            var mine = NearestMine(hall.Position, 30f);
            var dir = mine != null ? MathUtil.SafeNormalize(mine.Position - hall.Position, Vector2.UnitX) : MathUtil.FromAngle(hall.Facing);
            var side = new Vector2(-dir.Y, dir.X);
            int count = Math.Max(0, f.StartingWorkers);
            for (int i = 0; i < count; i++)
            {
                var pos = hall.Position + dir * (hall.Radius + 1.2f) + side * ((i - (count - 1) / 2f) * 0.9f);
                CreateUnit(workerDef, p.Team, Grid.NearestWalkable(pos), MathUtil.AngleOf(dir), p);
            }
            int k = 0;
            foreach (var id in f.StartingUnits ?? new List<string>())
            {
                if (!Data.Units.TryGetValue(id, out var d)) continue;
                var pos = hall.Position - dir * (hall.Radius + 1.5f) + side * ((k++ - 1) * 1.1f);
                CreateUnit(d, p.Team, Grid.NearestWalkable(pos), MathUtil.AngleOf(dir), p);
            }
        }

        // =================================================================== per tick

        private bool _rtsNightApplied;

        private void UpdateRts(float dt)
        {
            if (IsNight != _rtsNightApplied) { _rtsNightApplied = IsNight; ApplyNightStatuses(); }
            for (int i = 0; i < Units.Count; i++)
            {
                var b = Units[i];
                if (b.Dead || b.Removed || b.Kind != UnitKind.Building) continue;
                if (b.UnderConstruction) UpdateConstruction(b, dt);
                else if (b.TrainQueue != null && b.TrainQueue.Count > 0) UpdateTraining(b, dt);
                b.BuilderCount = 0;
            }
            UpdateSupply();
        }

        /// <summary>
        /// Runs at the start of the tick, right after units spawned last tick joined the world, so the AI never
        /// misses a building it placed a moment ago.
        /// </summary>
        private readonly List<RtsAi> _aiOrder = new List<RtsAi>();

        /// <summary>Bots take turns going first, tick by tick, so neither gets first pick of ground or targets.</summary>
        private void UpdateRtsAi()
        {
            _aiOrder.Clear();
            _aiOrder.AddRange(_rtsAi.Values);
            if ((Tick & 1) == 1) _aiOrder.Reverse();
            foreach (var ai in _aiOrder) ai.Update(this);
        }

        /// <summary>Supply used (living units + training queues) and supply cap (completed buildings).</summary>
        private void UpdateSupply()
        {
            foreach (var p in Players) { p.SupplyUsed = 0; p.SupplyCap = 0; }
            foreach (var u in Units)
            {
                var p = u.Owner;
                if (p == null || u.Dead || u.Removed || u.UnitDef == null) continue;
                if (u.Kind == UnitKind.Building)
                {
                    if (!u.UnderConstruction) p.SupplyCap += u.UnitDef.SupplyProvided;
                    if (u.TrainQueue != null)
                        foreach (var q in u.TrainQueue)
                            if (!Data.Upgrades.ContainsKey(q) && Data.Units.TryGetValue(q, out var qd)) p.SupplyUsed += qd.SupplyCost;
                }
                else p.SupplyUsed += u.UnitDef.SupplyCost;
            }
            foreach (var p in Players) p.SupplyCap = Math.Min(p.SupplyCap, Rules.RtsMaxSupply);
        }

        // =================================================================== orders (validated when issued)

        /// <summary>Immediate RTS orders (buildings). Returns true if the order was an RTS one and has been handled.</summary>
        private bool TryImmediateRtsOrder(Unit unit, Order o)
        {
            switch (o.Type)
            {
                case OrderType.Train: TryTrain(unit, o.ItemId); return true;
                case OrderType.SetRally: SetRally(unit, o); return true;
                case OrderType.CancelQueue: CancelQueue(unit, o.Slot); return true;
                case OrderType.Research: TryResearch(unit, o.ItemId); return true;
                default: return false;
            }
        }

        /// <summary>Checks worker orders before they are queued, so the player hears about problems immediately.</summary>
        private bool ValidateWorkerOrder(Unit u, Order o, out string error)
        {
            error = null;
            var def = u.UnitDef;
            if (!IsRts || u.Kind != UnitKind.Worker || def == null || u.Owner == null) { error = "Only workers can do that."; return false; }
            switch (o.Type)
            {
                case OrderType.Harvest:
                    if (o.TargetId != 0)
                    {
                        var node = GetUnit(o.TargetId);
                        if (node == null || node.Kind != UnitKind.Resource || node.Dead) { error = "Can't harvest that."; return false; }
                        if (def.GatherGold <= 0) { error = "This worker can't mine blood-iron."; return false; }
                        if (node.ResourceAmount <= 0) { error = "That vein is exhausted."; return false; }
                        return true;
                    }
                    if (def.GatherLumber <= 0) { error = "This worker can't harvest lumber."; return false; }
                    if (CountTreesNear(o.Point, 6f) == 0) { error = "There are no trees there."; return false; }
                    return true;
                case OrderType.ReturnResources:
                    if (u.CarryGold <= 0 && u.CarryLumber <= 0) { error = "Nothing to return."; return false; }
                    return true;
                case OrderType.Build:
                    if (o.TargetId != 0)
                    {
                        var site = GetUnit(o.TargetId);
                        if (site == null || site.Owner != u.Owner || !site.UnderConstruction) { error = "Nothing to build there."; return false; }
                        if (site.UnitDef.SelfBuilds) { error = "That building rises on its own."; return false; }
                        return true;
                    }
                    if (def.Builds == null || !def.Builds.Contains(o.ItemId ?? "") || !Data.Units.TryGetValue(o.ItemId, out var bd)) { error = "This worker can't build that."; return false; }
                    if (!RequirementsMet(u.Owner, bd, out error)) return false;
                    if (!CanAfford(u.Owner, bd, out error)) return false;
                    return CanPlaceBuilding(u.Owner, bd, o.Point, u, out error);
            }
            return true;
        }

        public bool CanAfford(Player p, UnitDef d, out string error)
        {
            error = null;
            if (p.Gold < d.GoldCost) { error = "Not enough blood-iron."; return false; }
            if (p.Lumber < d.LumberCost) { error = "Not enough lumber."; return false; }
            return true;
        }

        private void Pay(Player p, UnitDef d)
        {
            p.Gold -= d.GoldCost;
            p.Lumber -= d.LumberCost;
            p.GoldSpent += d.GoldCost;
            p.LumberSpent += d.LumberCost;
        }

        private void Refund(Player p, UnitDef d, float fraction)
        {
            int g = (int)(d.GoldCost * fraction), l = (int)(d.LumberCost * fraction);
            p.Gold += g;
            p.Lumber += l;
            p.GoldSpent -= g;
            p.LumberSpent -= l;
        }

        public bool RequirementsMet(Player p, UnitDef d, out string error)
        {
            error = null;
            if (d.Requires == null) return true;
            foreach (var req in d.Requires)
            {
                if (HasCompletedBuilding(p, req)) continue;
                error = "Requires " + (Data.Units.TryGetValue(req, out var rd) ? rd.Name : req) + ".";
                return false;
            }
            return true;
        }

        public bool HasCompletedBuilding(Player p, string defId)
        {
            foreach (var u in Units)
                if (u.Owner == p && u.DefId == defId && !u.Dead && !u.Removed && !u.UnderConstruction) return true;
            return false;
        }

        // =================================================================== construction

        /// <summary>
        /// A building fits if its footprint lies on open walkable ground (no trees, cliffs, water edges, other
        /// buildings or mines), leaves room around gold mines and has no enemy or neutral unit standing in it.
        /// </summary>
        public bool CanPlaceBuilding(Player p, UnitDef def, Vector2 pos, Unit builder, out string error)
        {
            error = null;
            float r = def.CollisionRadius;
            if (pos.X - r < 1f || pos.Y - r < 1f || pos.X + r > Grid.WorldWidth - 1f || pos.Y + r > Grid.WorldHeight - 1f) { error = "Can't build outside the battlefield."; return false; }
            foreach (int i in Grid.CellsInDisc(pos, FootprintRadius(def)))
            {
                int x = i % Grid.Width, y = i / Grid.Width;
                if (!Grid.IsWalkableCell(x, y) || Grid.IsWaterCell(x, y)) { error = "Can't build there."; return false; }
            }
            var list = RentList();
            UnitsInRadius(pos, r + 6f, list);
            try
            {
                foreach (var u in list)
                {
                    if (u == builder || u.Dead) continue;
                    float d = Vector2.Distance(u.Position, pos);
                    if (u.Kind == UnitKind.Resource && d < r + u.Radius + Rules.RtsMineClearance) { error = "Too close to a blood-iron vein."; return false; }
                    if (u.IsImmobile || u.Flying) continue;
                    if (u.Team != p.Team && d < r + u.Radius) { error = "Something is in the way."; return false; }
                }
            }
            finally { ReturnList(list); }
            return true;
        }

        private static float FootprintRadius(UnitDef def) => def.CollisionRadius * 0.85f;

        private void BlockFootprint(Unit u)
        {
            if (u.Footprint != null || u.UnitDef == null) return;
            u.Footprint = Grid.CellsInDisc(u.Position, FootprintRadius(u.UnitDef));
            foreach (int i in u.Footprint) Grid.AddDynamicBlockIndex(i);
        }

        private void ReleaseFootprint(Unit u)
        {
            if (u.Footprint == null) return;
            foreach (int i in u.Footprint) Grid.RemoveDynamicBlockIndex(i);
            u.Footprint = null;
        }

        private Unit PlaceBuilding(Player p, UnitDef def, Vector2 pos)
        {
            Pay(p, def);
            float facing = MathUtil.AngleOf(new Vector2(Grid.WorldWidth, Grid.WorldHeight) * 0.5f - pos);
            var b = CreateUnit(def, p.Team, pos, facing, p);
            b.UnderConstruction = true;
            b.BuildProgress = 0f;
            b.Hp = Math.Max(1f, b.Stats.MaxHp * Rules.RtsBuildStartHp);
            BlockFootprint(b);
            // Own units caught in the footprint step aside.
            var list = RentList();
            UnitsInRadius(pos, def.CollisionRadius + 1f, list);
            foreach (var u in list)
                if (!u.IsImmobile && !u.Flying && u != b && !Grid.IsWalkable(u.Position)) { u.Position = Grid.NearestWalkable(u.Position); u.Path.Clear(); }
            ReturnList(list);
            LogLine($"{p.Name} started {def.Name}");
            return b;
        }

        private void UpdateBuild(Unit u, float dt)
        {
            var o = u.CurrentOrder;
            var site = o.TargetId != 0 ? GetUnit(o.TargetId) : null;
            if (site == null)
            {
                if (o.TargetId != 0 || !Data.Units.TryGetValue(o.ItemId ?? "", out var def)) { StopWorking(u); CompleteOrder(u); return; }
                float reach = def.CollisionRadius + u.Radius + 0.6f;
                float dist = Vector2.Distance(u.Position, o.Point);
                if (dist > reach)
                {
                    if (MoveTowardsPoint(u, o.Point, dt, reach - 0.2f) && Vector2.Distance(u.Position, o.Point) > reach + 0.3f)
                    { EmitError(u, "Can't reach the build site."); CompleteOrder(u); }
                    return;
                }
                StopMoving(u);
                // Re-check on arrival: the ground, the purse and the tech tree may all have changed on the way.
                if (!RequirementsMet(u.Owner, def, out var err) || !CanAfford(u.Owner, def, out err) || !CanPlaceBuilding(u.Owner, def, o.Point, u, out err))
                { EmitError(u, err); CompleteOrder(u); return; }
                site = PlaceBuilding(u.Owner, def, o.Point);
                if (def.SelfBuilds) { CompleteOrder(u); return; }
                o.TargetId = site.Id;
                u.CurrentOrder = o;
            }
            if (site.Dead || !site.UnderConstruction || site.Owner != u.Owner || site.UnitDef.SelfBuilds) { StopWorking(u); CompleteOrder(u); return; }
            float r = site.Radius + u.Radius + 0.5f;
            if (Vector2.Distance(u.Position, site.Position) > r)
            {
                if (!ApproachObject(u, NodeKey(site), site.Position, FootprintRadius(site.UnitDef), r, dt))
                { EmitError(u, "Can't reach the building."); CompleteOrder(u); }
                return;
            }
            StopMoving(u);
            FaceTowards(u, site.Position, dt);
            SetAction(u, ActionState.Working);
            site.BuilderCount++;
        }

        private void UpdateConstruction(Unit b, float dt)
        {
            var def = b.UnitDef;
            float rate = def.SelfBuilds ? 1f : b.BuilderCount > 0 ? 1f + Rules.RtsExtraBuilderRate * (b.BuilderCount - 1) : 0f;
            if (rate <= 0f) return;
            float step = Math.Min(1f - b.BuildProgress, rate * dt / Math.Max(0.1f, def.BuildTime));
            b.BuildProgress += step;
            b.Hp = Math.Min(b.Stats.MaxHp, b.Hp + b.Stats.MaxHp * (1f - Rules.RtsBuildStartHp) * step);
            if (b.BuildProgress >= 1f - 1e-5f) CompleteConstruction(b);
        }

        private void CompleteConstruction(Unit b)
        {
            b.UnderConstruction = false;
            b.BuildProgress = 1f;
            if (b.UnitDef.DamageMax > 0) b.Brain = new TowerBrain();
            if (b.Owner != null) b.Owner.BuildingsBuilt++;
            Emit(new SimEvent { Type = SimEventType.ConstructionComplete, UnitId = b.Id, Key = b.DefId, Point = b.Position, Team = b.Team, PlayerId = -1 });
            LogLine($"{b.Owner?.Name} completed {b.Name}");
        }

        private void StopWorking(Unit u)
        {
            u.GatherTimer = 0f;
            if (u.Action == ActionState.Working) SetAction(u, ActionState.Idle);
        }

        // =================================================================== training

        public bool TryTrain(Unit b, string unitId)
        {
            var p = b?.Owner;
            if (p == null || !IsRts || b.Kind != UnitKind.Building) return false;
            string err = null;
            if (b.Dead) return false;
            if (b.UnderConstruction) err = "The building is not finished.";
            else if (b.UnitDef.Trains == null || !b.UnitDef.Trains.Contains(unitId ?? "") || !Data.Units.TryGetValue(unitId, out _)) err = "Can't train that here.";
            else if ((b.TrainQueue?.Count ?? 0) >= Rules.RtsTrainQueueMax) err = "The training queue is full.";
            if (err == null)
            {
                var d = Data.Units[unitId];
                if (RequirementsMet(p, d, out err) && CanAfford(p, d, out err))
                {
                    if (p.SupplyUsed + d.SupplyCost > p.SupplyCap) err = p.SupplyCap >= Rules.RtsMaxSupply ? "Supply limit reached." : "Not enough supply. Build more supply structures.";
                    else
                    {
                        Pay(p, d);
                        p.SupplyUsed += d.SupplyCost; // several orders can arrive in one tick
                        (b.TrainQueue ??= new List<string>()).Add(unitId);
                        return true;
                    }
                }
            }
            EmitError(b, err);
            return false;
        }

        private void UpdateTraining(Unit b, float dt)
        {
            if (Data.Upgrades.TryGetValue(b.TrainQueue[0], out var up))
            {
                b.TrainProgress += dt / Math.Max(0.1f, up.ResearchTime);
                if (b.TrainProgress < 1f) return;
                b.TrainProgress = 0f;
                b.TrainQueue.RemoveAt(0);
                CompleteUpgrade(b.Owner, up, b);
                return;
            }
            if (!Data.Units.TryGetValue(b.TrainQueue[0], out var d)) { b.TrainQueue.RemoveAt(0); return; }
            b.TrainProgress += dt / Math.Max(0.1f, d.BuildTime);
            if (b.TrainProgress < 1f) return;
            b.TrainProgress = 0f;
            b.TrainQueue.RemoveAt(0);
            var toward = b.HasRally ? b.RallyPoint : new Vector2(Grid.WorldWidth, Grid.WorldHeight) * 0.5f;
            var dir = MathUtil.SafeNormalize(toward - b.Position, MathUtil.FromAngle(b.Facing));
            var pos = Grid.NearestWalkable(b.Position + dir * (b.Radius + d.CollisionRadius + 0.3f));
            var u = CreateUnit(d, b.Team, pos, MathUtil.AngleOf(dir), b.Owner);
            b.Owner.UnitsTrained++;
            EmitPrivate(new SimEvent { Type = SimEventType.UnitTrained, UnitId = u.Id, OtherId = b.Id, Key = d.Id, Point = pos, Team = b.Team }, b.Owner.Id);
            SendToRally(b, u);
        }

        private void SendToRally(Unit b, Unit u)
        {
            var target = b.RallyTargetId != 0 ? GetUnit(b.RallyTargetId) : null;
            if (u.Kind == UnitKind.Worker)
            {
                if (target != null && target.Kind == UnitKind.Resource && target.ResourceAmount > 0) { IssueOrder(u, new Order { Type = OrderType.Harvest, UnitId = u.Id, TargetId = target.Id }); return; }
                if (b.HasRally && target == null && CountTreesNear(b.RallyPoint, 2.5f) > 0) { IssueOrder(u, new Order { Type = OrderType.Harvest, UnitId = u.Id, Point = b.RallyPoint }); return; }
                if (!b.HasRally && (b.UnitDef.DropOffGold))
                {
                    // Default rally for halls: the nearest gold mine.
                    var mine = NearestMine(b.Position, 20f);
                    if (mine != null) { IssueOrder(u, new Order { Type = OrderType.Harvest, UnitId = u.Id, TargetId = mine.Id }); return; }
                }
            }
            if (target != null && target != b && target.Kind != UnitKind.Resource)
            {
                if (target.Team != u.Team && target.Team != Team.Neutral) IssueOrder(u, Order.Attack(u.Id, target.Id));
                else IssueOrder(u, new Order { Type = OrderType.Follow, UnitId = u.Id, TargetId = target.Id });
                return;
            }
            if (b.HasRally) IssueOrder(u, Order.MoveTo(u.Id, b.RallyPoint));
        }

        private void SetRally(Unit b, Order o)
        {
            if (!IsRts || b.Kind != UnitKind.Building || b.Owner == null) return;
            if (b.UnitDef.Trains == null || b.UnitDef.Trains.Count == 0) { EmitError(b, "This building trains nothing."); return; }
            if (o.TargetId == b.Id) { b.HasRally = false; b.RallyTargetId = 0; return; }
            var target = o.TargetId != 0 ? GetUnit(o.TargetId) : null;
            b.HasRally = true;
            b.RallyTargetId = target?.Id ?? 0;
            b.RallyPoint = target?.Position ?? ClampToMap(o.Point);
        }

        private Vector2 ClampToMap(Vector2 p) => new Vector2(MathUtil.Clamp(p.X, 0.5f, Grid.WorldWidth - 0.5f), MathUtil.Clamp(p.Y, 0.5f, Grid.WorldHeight - 0.5f));

        /// <summary>Cancels a building under construction (partial refund) or one queued unit (full refund).</summary>
        private void CancelQueue(Unit b, int slot)
        {
            if (!IsRts || b.Kind != UnitKind.Building || b.Owner == null || b.Dead) return;
            if (b.UnderConstruction)
            {
                Refund(b.Owner, b.UnitDef, Rules.RtsConstructionRefund);
                ReleaseFootprint(b);
                b.Removed = true;
                Emit(new SimEvent { Type = SimEventType.Death, UnitId = b.Id, Point = b.Position, Team = b.Team, Key = "cancel", PlayerId = -1 });
                UpdateSupply();
                return;
            }
            if (b.TrainQueue == null || b.TrainQueue.Count == 0) return;
            int i = slot < 0 || slot >= b.TrainQueue.Count ? b.TrainQueue.Count - 1 : slot;
            if (Data.Upgrades.TryGetValue(b.TrainQueue[i], out var up)) { b.Owner.Gold += up.GoldCost; b.Owner.Lumber += up.LumberCost; b.Owner.GoldSpent -= up.GoldCost; b.Owner.LumberSpent -= up.LumberCost; }
            else if (Data.Units.TryGetValue(b.TrainQueue[i], out var d)) Refund(b.Owner, d, 1f);
            b.TrainQueue.RemoveAt(i);
            if (i == 0) b.TrainProgress = 0f;
            UpdateSupply();
        }

        // =================================================================== harvesting

        public Unit NearestMine(Vector2 p, float maxDist)
        {
            Unit best = null;
            float bd = maxDist;
            foreach (var u in Units)
            {
                if (u.Kind != UnitKind.Resource || !u.IsAlive || u.ResourceAmount <= 0) continue;
                float d = Vector2.Distance(u.Position, p);
                if (d < bd) { bd = d; best = u; }
            }
            return best;
        }

        public Unit NearestDropOff(Player p, Vector2 from, HarvestKind kind)
        {
            Unit best = null;
            float bd = float.MaxValue;
            foreach (var u in Units)
            {
                if (u.Owner != p || u.Kind != UnitKind.Building || !u.IsAlive || u.UnderConstruction) continue;
                if (kind == HarvestKind.Gold ? !u.UnitDef.DropOffGold : !u.UnitDef.DropOffLumber) continue;
                float d = Vector2.Distance(u.Position, from);
                if (d < bd) { bd = d; best = u; }
            }
            return best;
        }

        private int MinersAt(Unit mine)
        {
            int n = 0;
            var list = RentList();
            UnitsInRadius(mine.Position, mine.Radius + 2f, list);
            foreach (var w in list)
                if (w.Kind == UnitKind.Worker && w.IsGathering && w.Harvesting == HarvestKind.Gold && w.HarvestNodeId == mine.Id) n++;
            ReturnList(list);
            return n;
        }

        private void UpdateHarvest(Unit u, float dt)
        {
            var o = u.CurrentOrder;
            // Keep the worker's assignment in sync with the order it is running (queued orders, rally orders).
            if (o.TargetId != 0)
            {
                if (u.Harvesting != HarvestKind.Gold || u.HarvestNodeId != o.TargetId) { u.Harvesting = HarvestKind.Gold; u.HarvestNodeId = o.TargetId; u.GatherTimer = 0f; }
            }
            else if (u.Harvesting != HarvestKind.Lumber || Vector2.DistanceSquared(u.HarvestAnchor, o.Point) > 0.01f)
            {
                u.Harvesting = HarvestKind.Lumber;
                u.HarvestAnchor = o.Point;
                u.HarvestTree = -1;
                u.GatherTimer = 0f;
            }
            if (!u.IsGathering && (u.CarryGold > 0 || u.CarryLumber > 0)) u.Returning = true;
            if (u.Returning) { UpdateReturn(u, dt); return; }
            if (u.Harvesting == HarvestKind.Gold) UpdateMining(u, dt);
            else UpdateChopping(u, dt);
        }

        /// <summary>
        /// Walks a worker to the side of a blocked object (vein, building, tree) that faces it. Aiming at the object's
        /// centre would let the path search pick whichever free cell it scans first, often on the far side, and that
        /// choice differs between mirrored bases. Returns false when the object turned out to be unreachable.
        /// </summary>
        private bool ApproachObject(Unit u, int key, Vector2 centre, float blockedRadius, float reach, float dt)
        {
            if (u.ApproachFor != key) { u.ApproachFor = key; u.ApproachTries = 0; SetApproach(u, centre, blockedRadius); }
            bool done = MoveTowardsPoint(u, u.ApproachGoal, dt, 0.1f);
            if (Vector2.Distance(u.Position, centre) <= reach) return true;
            if (done)
            {
                // Stopped short (blocked, or the spot was snapped away): re-aim from here a couple of times, then give up.
                if (++u.ApproachTries > 2) return false;
                SetApproach(u, centre, blockedRadius);
            }
            return true;
        }

        private void SetApproach(Unit u, Vector2 centre, float blockedRadius)
        {
            var dir = MathUtil.SafeNormalize(u.Position - centre, Vector2.UnitX);
            u.ApproachGoal = Grid.NearestWalkable(centre + dir * (blockedRadius + u.Radius + 0.25f), 8);
            u.Path.Clear();
        }

        private static int NodeKey(Unit target) => target.Id;
        private static int TreeKey(int tree) => -1 - tree;

        private void UpdateMining(Unit u, float dt)
        {
            var mine = GetUnit(u.HarvestNodeId);
            if (mine == null || mine.Dead || mine.ResourceAmount <= 0)
            {
                StopWorking(u);
                mine = NearestMine(u.Position, 25f);
                if (mine == null) { u.Harvesting = HarvestKind.None; CompleteOrder(u); return; }
                u.HarvestNodeId = mine.Id;
                var o = u.CurrentOrder; o.TargetId = mine.Id; u.CurrentOrder = o;
            }
            if (u.IsGathering)
            {
                u.GatherTimer -= dt;
                if (u.GatherTimer > 0f) return;
                u.GatherTimer = 0f;
                int take = Math.Min(u.UnitDef.GatherGold, mine.ResourceAmount);
                mine.ResourceAmount -= take;
                u.CarryGold = take;
                u.CarryLumber = 0;
                u.Returning = true;
                SetAction(u, ActionState.Idle);
                if (mine.ResourceAmount <= 0) DepleteMine(mine);
                return;
            }
            float reach = mine.Radius + u.Radius + TouchMargin;
            if (Vector2.Distance(u.Position, mine.Position) > reach)
            {
                if (!ApproachObject(u, NodeKey(mine), mine.Position, FootprintRadius(mine.UnitDef), reach, dt))
                { EmitError(u, "Can't reach that vein."); u.Harvesting = HarvestKind.None; CompleteOrder(u); }
                return;
            }
            StopMoving(u);
            if (MinersAt(mine) >= Math.Max(1, Rules.RtsMineSlots)) { SetAction(u, ActionState.Idle); return; } // wait for a slot
            u.GatherTimer = Math.Max(0.1f, u.UnitDef.MineTime);
            u.Facing = MathUtil.AngleOf(mine.Position - u.Position);
            SetAction(u, ActionState.Working);
        }

        private void DepleteMine(Unit mine)
        {
            Emit(new SimEvent { Type = SimEventType.MineDepleted, UnitId = mine.Id, Point = mine.Position, PlayerId = -1 });
            LogLine($"Gold mine {mine.StructureId ?? mine.Id.ToString()} is depleted");
            KillUnit(mine, null);
        }

        private void UpdateChopping(Unit u, float dt)
        {
            if (u.IsGathering)
            {
                u.GatherTimer -= dt;
                if (u.GatherTimer > 0f) return;
                u.GatherTimer = 0f;
                SetAction(u, ActionState.Idle);
                // Another worker may have felled the tree meanwhile; then this swing was wasted.
                int take = TakeLumber(u.HarvestTree, u.UnitDef.GatherLumber);
                if (take > 0) { u.CarryLumber = take; u.CarryGold = 0; u.Returning = true; }
                return;
            }
            if (!TreeStanding(u.HarvestTree))
            {
                var anchor = u.HarvestTree >= 0 ? TreePosition(u.HarvestTree) : u.HarvestAnchor;
                u.HarvestTree = ChooseTree(u, anchor, 12f, -1);
                if (u.HarvestTree < 0) { EmitError(u, "No reachable trees nearby."); u.Harvesting = HarvestKind.None; CompleteOrder(u); return; }
            }
            var trunk = TreePosition(u.HarvestTree);
            if (Vector2.Distance(u.Position, trunk) > ChopReach)
            {
                if (!ApproachObject(u, TreeKey(u.HarvestTree), trunk, NavGrid.TreeCellRadius, ChopReach, dt))
                {
                    // Walled in by other trees after all: try another one.
                    int failed = u.HarvestTree;
                    u.HarvestTree = ChooseTree(u, trunk, 12f, failed);
                    if (u.HarvestTree < 0) { EmitError(u, "No reachable trees nearby."); u.Harvesting = HarvestKind.None; CompleteOrder(u); }
                }
                return;
            }
            StopMoving(u);
            u.GatherTimer = Math.Max(0.1f, u.UnitDef.ChopTime);
            u.Facing = MathUtil.AngleOf(trunk - u.Position);
            SetAction(u, ActionState.Working);
        }

        /// <summary>
        /// Picks the standing tree nearest the anchor that the worker can actually stand next to. Forests are dense,
        /// so inner trees are often walled in; a path check weeds those out (a few A* runs per trip at most).
        /// </summary>
        private int ChooseTree(Unit u, Vector2 anchor, float radius, int exclude)
        {
            var candidates = new List<(int index, float score)>();
            ForEachTreeNear(anchor, radius, i =>
            {
                if (i == exclude) return;
                var t = TreePosition(i);
                candidates.Add((i, Vector2.Distance(t, anchor) + 0.25f * Vector2.Distance(t, u.Position)));
            });
            candidates.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.index.CompareTo(b.index));
            int tries = 0;
            foreach (var (i, _) in candidates)
            {
                if (++tries > 6) break;
                var t = TreePosition(i);
                if (Vector2.Distance(u.Position, t) <= ChopReach) return i;
                Grid.FindPath(u.Position, t, _rtsPathScratch);
                if (_rtsPathScratch.Count > 0 && Vector2.Distance(_rtsPathScratch[_rtsPathScratch.Count - 1], t) <= ChopReach - 0.1f) return i;
            }
            return -1;
        }

        private void UpdateReturn(Unit u, float dt)
        {
            StopWorking(u);
            var kind = u.CarryGold > 0 ? HarvestKind.Gold : HarvestKind.Lumber;
            if (u.CarryGold <= 0 && u.CarryLumber <= 0) { u.Returning = false; return; }
            var drop = NearestDropOff(u.Owner, u.Position, kind);
            if (drop == null) { EmitError(u, "There is nowhere to return resources to."); u.Returning = false; CompleteOrder(u); return; }
            float reach = drop.Radius + u.Radius + TouchMargin;
            if (Vector2.Distance(u.Position, drop.Position) > reach)
            {
                if (!ApproachObject(u, NodeKey(drop), drop.Position, FootprintRadius(drop.UnitDef), reach, dt))
                { EmitError(u, "Can't reach a drop-off."); CompleteOrder(u); }
                return;
            }
            StopMoving(u);
            Deliver(u);
            u.Returning = false;
            if (u.CurrentOrder.Type == OrderType.ReturnResources)
            {
                // "Return cargo" goes back to work afterwards, like classic RTS workers.
                if (u.Harvesting == HarvestKind.Gold && GetUnit(u.HarvestNodeId) != null)
                    u.CurrentOrder = new Order { Type = OrderType.Harvest, UnitId = u.Id, TargetId = u.HarvestNodeId };
                else if (u.Harvesting == HarvestKind.Lumber)
                    u.CurrentOrder = new Order { Type = OrderType.Harvest, UnitId = u.Id, Point = u.HarvestAnchor };
                else CompleteOrder(u);
            }
        }

        private void Deliver(Unit u)
        {
            var p = u.Owner;
            int g = u.CarryGold, l = u.CarryLumber;
            u.CarryGold = u.CarryLumber = 0;
            if (p == null || (g <= 0 && l <= 0)) return;
            p.Gold += g;
            p.GoldEarned += g;
            p.GoldMined += g;
            p.Lumber += l;
            p.LumberHarvested += l;
            EmitPrivate(new SimEvent { Type = SimEventType.ResourcesDelivered, UnitId = u.Id, Value = g, Value2 = l, Point = u.Position, Team = u.Team }, p.Id);
        }

        private void UpdateReturnOrder(Unit u, float dt)
        {
            if (u.CarryGold <= 0 && u.CarryLumber <= 0) { CompleteOrder(u); return; }
            u.Returning = true;
            UpdateReturn(u, dt);
        }

        private void EmitPrivate(SimEvent e, int playerId)
        {
            e.Tick = Tick;
            e.PlayerId = playerId;
            Events.Add(e);
        }

        // =================================================================== deaths and victory

        private void OnRtsDeath(Unit victim, Player killerPlayer)
        {
            ReleaseFootprint(victim);
            if (!victim.IsStructure && victim.Kind != UnitKind.Resource)
            {
                TryRaiseDead(victim, killerPlayer);
                PayBloodPrice(victim, killerPlayer);
            }
            var owner = victim.Owner;
            if (owner == null || victim.IsIllusion) return;
            bool enemyKill = killerPlayer != null && killerPlayer.Team != owner.Team;
            if (victim.Kind == UnitKind.Building)
            {
                owner.BuildingsLost++;
                if (enemyKill) killerPlayer.BuildingsRazed++;
            }
            else
            {
                owner.UnitsLost++;
                if (enemyKill) killerPlayer.UnitsKilled++;
            }
        }

        private void CheckRtsVictory()
        {
            foreach (var p in Players)
            {
                if (p.Eliminated) continue;
                bool standing = false;
                foreach (var u in Units)
                    if (u.Owner == p && u.Kind == UnitKind.Building && !u.Dead && !u.Removed) { standing = true; break; }
                if (standing) continue;
                p.Eliminated = true;
                Emit(new SimEvent { Type = SimEventType.PlayerEliminated, OtherId = p.Id, Team = p.Team, PlayerId = -1 });
                LogLine($"{p.Name} has been eliminated");
            }
            for (int t = 0; t < 2; t++)
            {
                var team = Players.Where(p => (int)p.Team == t).ToList();
                if (team.Count > 0 && team.All(p => p.Eliminated)) { EndMatch(t == 0 ? Team.Dusk : Team.Dawn); return; }
            }
        }
    
        // =================================================================== research

        public bool TryResearch(Unit b, string upgradeId)
        {
            var p = b?.Owner;
            if (p == null || !IsRts || b.Kind != UnitKind.Building || b.Dead) return false;
            string err = null;
            if (b.UnderConstruction) err = "The building is not finished.";
            else if (b.UnitDef.Research == null || !b.UnitDef.Research.Contains(upgradeId ?? "") || !Data.Upgrades.TryGetValue(upgradeId, out _)) err = "Can't research that here.";
            else if (p.Upgrades.Contains(upgradeId)) err = "Already researched.";
            else if (Units.Any(u => u.Owner == p && u.IsAlive && u.TrainQueue != null && u.TrainQueue.Contains(upgradeId))) err = "Already being researched.";
            else if ((b.TrainQueue?.Count ?? 0) >= Rules.RtsTrainQueueMax) err = "The queue is full.";
            if (err == null)
            {
                var up = Data.Upgrades[upgradeId];
                foreach (var req in up.RequiresUpgrades ?? new List<string>())
                    if (!p.Upgrades.Contains(req)) { err = "Requires " + (Data.Upgrades.TryGetValue(req, out var ru) ? ru.Name : req) + "."; break; }
                if (err == null)
                    foreach (var req in up.Requires ?? new List<string>())
                        if (!HasCompletedBuilding(p, req)) { err = "Requires " + (Data.Units.TryGetValue(req, out var rd) ? rd.Name : req) + "."; break; }
                if (err == null && p.Gold < up.GoldCost) err = "Not enough blood-iron.";
                if (err == null && p.Lumber < up.LumberCost) err = "Not enough lumber.";
                if (err == null)
                {
                    p.Gold -= up.GoldCost;
                    p.Lumber -= up.LumberCost;
                    p.GoldSpent += up.GoldCost;
                    p.LumberSpent += up.LumberCost;
                    (b.TrainQueue ??= new List<string>()).Add(upgradeId);
                    return true;
                }
            }
            EmitError(b, err);
            return false;
        }

        private void CompleteUpgrade(Player p, UpgradeDef up, Unit at)
        {
            if (p == null || !p.Upgrades.Add(up.Id)) return;
            foreach (var u in Units)
                if (u.Owner == p && u.IsAlive && u.UnitDef != null && UpgradeApplies(up, u.UnitDef))
                    ApplyStatus(u, up.Status, null, 1, -1f);
            EmitPrivate(new SimEvent { Type = SimEventType.ResearchComplete, UnitId = at.Id, Key = up.Id, Point = at.Position, Team = at.Team }, p.Id);
            LogLine($"{p.Name} researched {up.Name}");
        }

        /// <summary>Selectors: a unit id, a unit tag, or soldier / melee / ranged / siege / worker / building.</summary>
        public static bool UpgradeApplies(UpgradeDef up, UnitDef d)
        {
            foreach (var sel in up.AppliesTo ?? new List<string>())
            {
                bool siege = d.Tags != null && d.Tags.Contains("siege");
                switch (sel)
                {
                    case "soldier": if (d.Kind == UnitKind.Soldier) return true; break;
                    case "melee": if (d.Kind == UnitKind.Soldier && d.AttackType == AttackType.Melee) return true; break;
                    case "ranged": if (d.Kind == UnitKind.Soldier && d.AttackType == AttackType.Ranged && !siege) return true; break;
                    case "siege": if (siege) return true; break;
                    case "worker": if (d.Kind == UnitKind.Worker) return true; break;
                    case "building": if (d.Kind == UnitKind.Building) return true; break;
                    default:
                        if (d.Id == sel || (d.Tags != null && d.Tags.Contains(sel))) return true;
                        break;
                }
            }
            return false;
        }

        /// <summary>New units join with their owner's research (and, for the Covenant, the night's strength).</summary>
        private void ApplyOwnedRtsStatuses(Unit u)
        {
            var p = u.Owner;
            if (p == null || u.UnitDef == null) return;
            foreach (var id in p.Upgrades)
                if (Data.Upgrades.TryGetValue(id, out var up) && UpgradeApplies(up, u.UnitDef)) ApplyStatus(u, up.Status, null, 1, -1f);
            if (IsNight && p.RtsFaction?.NightStatus != null && u.Kind == UnitKind.Soldier) ApplyStatus(u, p.RtsFaction.NightStatus, null, 1, -1f);
            if (IsNight && u.UnitDef.NightForm != null) ApplyStatus(u, u.UnitDef.NightForm, null, 1, -1f);
        }

        // =================================================================== faction mechanics

        /// <summary>
        /// Wild Covenant: when night falls its soldiers gain the faction's night status and units with a night form
        /// (Moonfang Shifters) change shape; both end at dawn.
        /// </summary>
        private void ApplyNightStatuses()
        {
            foreach (var u in Units)
            {
                if (u.Owner == null || !u.IsAlive || u.UnitDef == null) continue;
                var faction = u.Owner.RtsFaction?.NightStatus;
                if (faction != null && u.Kind == UnitKind.Soldier && Data.Statuses.TryGetValue(faction, out var def)) SetNightStatus(u, def);
                if (u.UnitDef.NightForm != null && Data.Statuses.TryGetValue(u.UnitDef.NightForm, out var form)) SetNightStatus(u, form);
            }
        }

        private void SetNightStatus(Unit u, StatusDef def)
        {
            if (IsNight) { ApplyStatus(u, def, null, 1, -1f, 1, false, null, true); return; }
            for (int i = u.Statuses.Count - 1; i >= 0; i--)
                if (u.Statuses[i].Def == def) RemoveStatus(u, u.Statuses[i], false);
        }

        /// <summary>
        /// Ashen Legion: a living (not undead, not siege) unit that falls near a Legion soldier rises as a temporary
        /// skeleton (RtsFactionDef.RaiseUnit) for that Legion player, at most once per RaiseCooldown and if supply allows.
        /// </summary>
        private void TryRaiseDead(Unit victim, Player killer)
        {
            var vd = victim.UnitDef;
            if (vd == null || victim.IsIllusion || victim.Kind == UnitKind.Resource || victim.IsNeutral && victim.Kind == UnitKind.Boss) return;
            if (vd.Tags != null && (vd.Tags.Contains("undead") || vd.Tags.Contains("siege"))) return;
            // The side that made the kill raises the body; failing that, whoever has a soldier standing closest to it.
            Player raiser = null;
            UnitDef raiserDef = null;
            float best = float.MaxValue;
            foreach (var p in Players)
            {
                var f = p.RtsFaction;
                if (f?.RaiseUnit == null || p.Eliminated || Time < p.NextRaiseAt || !Data.Units.TryGetValue(f.RaiseUnit, out var raise)) continue;
                if (p.SupplyUsed + raise.SupplyCost > p.SupplyCap) continue;
                float near = float.MaxValue;
                foreach (var u in Units)
                    if (u.Owner == p && u.IsAlive && u.Kind == UnitKind.Soldier) near = Math.Min(near, Vector2.Distance(u.Position, victim.Position));
                if (near > f.RaiseRadius) continue;
                if (p == killer) near = -1f;
                if (near < best) { best = near; raiser = p; raiserDef = raise; }
            }
            if (raiser == null) return;
            var faction = raiser.RtsFaction;
            var risen = CreateUnit(raiserDef, raiser.Team, Grid.NearestWalkable(victim.Position), victim.Facing, raiser);
            if (faction.RaiseLifetime > 0f) risen.Lifetime = faction.RaiseLifetime;
            raiser.NextRaiseAt = Time + faction.RaiseCooldown;
            raiser.SupplyUsed += raiserDef.SupplyCost;
            raiser.UnitsRaised++;
            Emit(new SimEvent { Type = SimEventType.EffectVisual, Key = "raise_dead", UnitId = risen.Id, Point = victim.Position, PlayerId = -1 });
        }

        /// <summary>Crimson Court: every enemy unit its forces slay pays part of its cost in blood-iron.</summary>
        private void PayBloodPrice(Unit victim, Player killerPlayer)
        {
            if (killerPlayer?.RtsFaction == null || killerPlayer.RtsFaction.BloodPrice <= 0f || victim.UnitDef == null) return;
            if (victim.Owner != null && victim.Owner.Team == killerPlayer.Team) return;
            int cost = victim.Owner != null ? victim.UnitDef.GoldCost : victim.UnitDef.BountyGoldMax * 2;
            int pay = (int)(cost * killerPlayer.RtsFaction.BloodPrice);
            if (pay <= 0) return;
            killerPlayer.Gold += pay;
            killerPlayer.GoldEarned += pay;
            killerPlayer.BloodPriceEarned += pay;
            EmitPrivate(new SimEvent { Type = SimEventType.ResourcesDelivered, UnitId = victim.Id, Value = pay, Point = victim.Position, Key = "blood_price" }, killerPlayer.Id);
        }
    }
}
