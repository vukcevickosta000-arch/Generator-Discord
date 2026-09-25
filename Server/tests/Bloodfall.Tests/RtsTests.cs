using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bloodfall.Data;
using Bloodfall.Simulation;
using Xunit;

namespace Bloodfall.Tests
{
    /// <summary>War of the Ancients (RTS) rules on the Ashfields map: economy, construction, training, victory.</summary>
    public class RtsTests
    {
        internal static Match NewRts(string dawn = "dawnguard", string dusk = "ashen_legion", bool neutrals = false, ulong seed = 3)
        {
            var cfg = new MatchConfig { ModeId = "rts_1v1", MapId = "map_rts_ashfields", Seed = seed, SkipHeroSelect = true, PreGameTimeOverride = 0.1f, DisableNeutrals = !neutrals };
            cfg.Players.Add(new PlayerSetup { Name = "Dawn", Team = Team.Dawn, RtsFaction = dawn });
            cfg.Players.Add(new PlayerSetup { Name = "Dusk", Team = Team.Dusk, RtsFaction = dusk });
            var m = new Match(TestUtil.Data, cfg);
            TestUtil.Run(m, 0.2f);
            return m;
        }

        internal static List<Unit> Owned(Match m, Player p, UnitKind kind) => m.Units.Where(u => u.Owner == p && u.Kind == kind && u.IsAlive).ToList();
        internal static Unit Hall(Match m, Player p) => Owned(m, p, UnitKind.Building).First(b => b.DefId == p.RtsFaction.Hall);
        private static Unit MainVein(Match m, Player p) => m.NearestMine(Hall(m, p).Position, 20f);
        internal static UnitDef Def(string id) => TestUtil.Data.Units[id];

        private static int NearestTree(Match m, Vector2 p)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < m.TreeCount; i++)
            {
                if (!m.TreeStanding(i)) continue;
                float d = Vector2.Distance(m.TreePosition(i), p);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>A valid spot for a building, searched outward from the hall toward the map centre.</summary>
        internal static Vector2 FindSpot(Match m, Player p, string buildingId, float startDist = 8f)
        {
            var hall = Hall(m, p);
            var centre = new Vector2(m.Grid.WorldWidth, m.Grid.WorldHeight) * 0.5f;
            var dir = Vector2.Normalize(centre - hall.Position);
            var side = new Vector2(-dir.Y, dir.X);
            for (float d = startDist; d < 22f; d += 1f)
                for (float s = 0f; s < 12f; s += 1f)
                    foreach (var sign in new[] { 1f, -1f })
                    {
                        var pos = hall.Position + dir * d + side * s * sign;
                        if (m.CanPlaceBuilding(p, Def(buildingId), pos, null, out _)) return pos;
                    }
            throw new InvalidOperationException("No spot for " + buildingId);
        }

        internal static Unit BuildAndFinish(Match m, Player p, string buildingId, Vector2 spot, int builders = 1)
        {
            var workers = Owned(m, p, UnitKind.Worker).Take(builders).ToList();
            m.IssueOrder(workers[0], new Order { Type = OrderType.Build, UnitId = workers[0].Id, ItemId = buildingId, Point = spot });
            Unit site = null;
            for (int i = 0; i < 30 * 30 && site == null; i++)
            {
                m.Step();
                site = m.Units.FirstOrDefault(u => u.Owner == p && u.DefId == buildingId && u.IsAlive && Vector2.Distance(u.Position, spot) < 0.1f);
            }
            Assert.NotNull(site);
            foreach (var w in workers.Skip(1)) m.IssueOrder(w, new Order { Type = OrderType.Build, UnitId = w.Id, TargetId = site.Id });
            TestUtil.Run(m, Def(buildingId).BuildTime + 5f);
            Assert.False(site.UnderConstruction, buildingId + " did not finish");
            return site;
        }

        [Fact]
        public void EachPlayerStartsWithAHallAndFiveWorkers()
        {
            var m = NewRts();
            Assert.Equal(MatchPhase.Playing, m.Phase);
            foreach (var p in m.Players)
            {
                Assert.NotNull(p.RtsFaction);
                Assert.Null(p.Hero);
                Assert.Equal(m.Rules.RtsStartingGold, p.Gold);
                Assert.Equal(m.Rules.RtsStartingLumber, p.Lumber);
                var hall = Hall(m, p);
                Assert.False(hall.UnderConstruction);
                Assert.Equal(5, Owned(m, p, UnitKind.Worker).Count);
                Assert.Equal(5, p.SupplyUsed);
                Assert.Equal(Def(p.RtsFaction.Hall).SupplyProvided, p.SupplyCap);
                Assert.False(m.Grid.IsWalkable(hall.Position), "hall footprint must block the nav grid");
                Assert.NotNull(MainVein(m, p));
            }
            Assert.Equal("dawnguard", m.Players[0].RtsFaction.Id);
            Assert.Equal("ashen_legion", m.Players[1].RtsFaction.Id);
            // Point-symmetric map: both halls equally far from their main vein.
            float d0 = Vector2.Distance(Hall(m, m.Players[0]).Position, MainVein(m, m.Players[0]).Position);
            float d1 = Vector2.Distance(Hall(m, m.Players[1]).Position, MainVein(m, m.Players[1]).Position);
            Assert.Equal(d0, d1, 2);
            Assert.DoesNotContain(m.Units, u => u.IsHero || u.IsCreep);
        }

        [Fact]
        public void WorkersMineBloodIronIntoTheHall()
        {
            var m = NewRts();
            var p = m.Players[0];
            var vein = MainVein(m, p);
            int before = vein.ResourceAmount;
            foreach (var w in Owned(m, p, UnitKind.Worker)) m.IssueOrder(w, new Order { Type = OrderType.Harvest, UnitId = w.Id, TargetId = vein.Id });
            TestUtil.Run(m, 60f);
            int gained = p.Gold - m.Rules.RtsStartingGold;
            // One worker at a time inside a vein (1 s each) caps a vein at 10 per second.
            Assert.InRange(gained, 250, 600);
            Assert.Equal(gained, p.GoldMined);
            int carried = Owned(m, p, UnitKind.Worker).Sum(w => w.CarryGold);
            Assert.Equal(before - vein.ResourceAmount, gained + carried);
            Assert.Contains(m.Events, e => e.Type == SimEventType.ResourcesDelivered && e.PlayerId == p.Id);
        }

        [Fact]
        public void WorkersCutLumberAndTreesFall()
        {
            var m = NewRts();
            var p = m.Players[1];
            var hall = Hall(m, p);
            int tree = NearestTree(m, hall.Position);
            var trunk = m.TreePosition(tree);
            var workers = Owned(m, p, UnitKind.Worker).Take(3).ToList();
            foreach (var w in workers) m.IssueOrder(w, new Order { Type = OrderType.Harvest, UnitId = w.Id, Point = trunk });
            bool felled = false;
            for (int i = 0; i < 150 * 30; i++)
            {
                m.Step();
                if (m.Events.Any(e => e.Type == SimEventType.TreeDestroyed)) felled = true;
                m.Events.Clear();
            }
            int gained = p.Lumber - m.Rules.RtsStartingLumber;
            Assert.True(gained >= 100, $"only {gained} lumber in 150 s");
            Assert.Equal(gained, p.LumberHarvested);
            Assert.True(felled, "no tree fell");
            Assert.False(m.TreeStanding(tree), "the first tree should have been cut down");
            Assert.True(m.Grid.IsWalkable(trunk), "a felled tree opens the ground");
            Assert.All(workers, w => Assert.Equal(OrderType.Harvest, w.CurrentOrder.Type));
        }

        [Fact]
        public void ConstructionNeedsABuilderAndGrantsSupplyWhenDone()
        {
            var m = NewRts();
            var p = m.Players[0];
            var spot = FindSpot(m, p, "rts_dg_sun_shrine");
            var w = Owned(m, p, UnitKind.Worker)[0];
            m.IssueOrder(w, new Order { Type = OrderType.Build, UnitId = w.Id, ItemId = "rts_dg_sun_shrine", Point = spot });
            Assert.Equal(500, p.Gold); // paid only when the building is placed
            TestUtil.Run(m, 6f);
            var shrine = m.Units.Single(u => u.DefId == "rts_dg_sun_shrine");
            Assert.True(shrine.UnderConstruction);
            Assert.Equal(500 - 80, p.Gold);
            Assert.Equal(150 - 20, p.Lumber);
            Assert.Equal(10, p.SupplyCap);
            Assert.True(shrine.Hp < shrine.Stats.MaxHp * 0.5f);
            // Pull the squire away: construction pauses.
            m.IssueOrder(w, Order.MoveTo(w.Id, Hall(m, p).Position + new Vector2(0, -5)));
            TestUtil.Run(m, 1f);
            float paused = shrine.BuildProgress;
            TestUtil.Run(m, 5f);
            Assert.Equal(paused, shrine.BuildProgress, 3);
            // Resume and finish.
            m.IssueOrder(w, new Order { Type = OrderType.Build, UnitId = w.Id, TargetId = shrine.Id });
            TestUtil.Run(m, Def("rts_dg_sun_shrine").BuildTime + 3f);
            Assert.False(shrine.UnderConstruction);
            Assert.Equal(shrine.Stats.MaxHp, shrine.Hp, 0);
            Assert.Equal(18, p.SupplyCap);
            Assert.Equal(1, p.BuildingsBuilt);
            Assert.Equal(OrderType.None, w.CurrentOrder.Type);
        }

        [Fact]
        public void ExtraBuildersSpeedUpConstruction()
        {
            float TimeWith(int builders)
            {
                var m = NewRts();
                var p = m.Players[0];
                var spot = FindSpot(m, p, "rts_dg_barracks");
                var ws = Owned(m, p, UnitKind.Worker).Take(builders).ToList();
                m.IssueOrder(ws[0], new Order { Type = OrderType.Build, UnitId = ws[0].Id, ItemId = "rts_dg_barracks", Point = spot });
                Unit site = null;
                while (site == null) { m.Step(); site = m.Units.FirstOrDefault(u => u.DefId == "rts_dg_barracks"); }
                float start = m.Time;
                foreach (var w in ws.Skip(1)) m.IssueOrder(w, new Order { Type = OrderType.Build, UnitId = w.Id, TargetId = site.Id });
                while (site.UnderConstruction && m.Time - start < 120f) m.Step();
                return m.Time - start;
            }
            float one = TimeWith(1), three = TimeWith(3);
            Assert.InRange(one, Def("rts_dg_barracks").BuildTime - 0.1f, Def("rts_dg_barracks").BuildTime + 1f);
            Assert.True(three < one * 0.6f, $"3 builders took {three:0.0}s vs {one:0.0}s");
        }

        [Fact]
        public void AshenLegionBuildingsRiseOnTheirOwn()
        {
            var m = NewRts();
            var p = m.Players[1];
            var spot = FindSpot(m, p, "rts_al_obelisk");
            var w = Owned(m, p, UnitKind.Worker)[0];
            m.IssueOrder(w, new Order { Type = OrderType.Build, UnitId = w.Id, ItemId = "rts_al_obelisk", Point = spot });
            Unit site = null;
            for (int i = 0; i < 30 * 20 && site == null; i++) { m.Step(); site = m.Units.FirstOrDefault(u => u.DefId == "rts_al_obelisk"); }
            Assert.NotNull(site);
            TestUtil.Run(m, 0.5f);
            Assert.Equal(OrderType.None, w.CurrentOrder.Type); // the acolyte is free at once
            m.IssueOrder(w, new Order { Type = OrderType.Harvest, UnitId = w.Id, TargetId = MainVein(m, p).Id });
            TestUtil.Run(m, Def("rts_al_obelisk").BuildTime + 1f);
            Assert.False(site.UnderConstruction);
            Assert.Equal(18, p.SupplyCap);
        }

        [Fact]
        public void InvalidBuildOrdersAreRejectedWithoutCharging()
        {
            var m = NewRts();
            var p = m.Players[0];
            var w = Owned(m, p, UnitKind.Worker)[0];
            var hall = Hall(m, p);
            string Try(Order o)
            {
                m.Events.Clear();
                m.IssueOrder(w, o);
                return m.Events.Where(e => e.Type == SimEventType.Error).Select(e => e.Key).FirstOrDefault();
            }
            Order Build(string id, Vector2 at) => new Order { Type = OrderType.Build, UnitId = w.Id, ItemId = id, Point = at };

            Assert.Equal("Can't build there.", Try(Build("rts_dg_sun_shrine", hall.Position)));
            Assert.Equal("Can't build there.", Try(Build("rts_dg_sun_shrine", m.TreePosition(NearestTree(m, hall.Position)))));
            var vein = MainVein(m, p);
            Assert.Equal("Too close to a blood-iron vein.", Try(Build("rts_dg_sun_shrine", vein.Position + Vector2.Normalize(hall.Position - vein.Position) * 3f)));
            Assert.Equal("Requires Barracks.", Try(Build("rts_dg_workshop", FindSpot(m, p, "rts_dg_workshop"))));
            Assert.Equal("This worker can't build that.", Try(Build("rts_al_obelisk", FindSpot(m, p, "rts_dg_sun_shrine"))));
            Assert.Equal("This worker can't build that.", Try(Build("no_such_building", FindSpot(m, p, "rts_dg_sun_shrine"))));
            p.Gold = 50;
            Assert.Equal("Not enough blood-iron.", Try(Build("rts_dg_sun_shrine", FindSpot(m, p, "rts_dg_sun_shrine"))));
            p.Gold = 500; p.Lumber = 0;
            Assert.Equal("Not enough lumber.", Try(Build("rts_dg_sun_shrine", FindSpot(m, p, "rts_dg_sun_shrine"))));
            Assert.Equal(OrderType.None, w.CurrentOrder.Type);
            Assert.Equal(500, p.Gold);
            // Soldiers and buildings cannot harvest or build.
            m.Events.Clear();
            m.IssueOrder(hall, new Order { Type = OrderType.Harvest, UnitId = hall.Id, TargetId = vein.Id });
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.Key == "Only workers can do that.");
        }

        [Fact]
        public void PlacementChangesOnTheWayAreCaughtOnArrival()
        {
            var m = NewRts();
            var p = m.Players[0];
            var spot = FindSpot(m, p, "rts_dg_barracks", 14f);
            var w = Owned(m, p, UnitKind.Worker)[0];
            m.IssueOrder(w, new Order { Type = OrderType.Build, UnitId = w.Id, ItemId = "rts_dg_barracks", Point = spot });
            p.Gold = 10; // spent elsewhere before the squire arrives
            TestUtil.Run(m, 10f);
            Assert.DoesNotContain(m.Units, u => u.DefId == "rts_dg_barracks");
            Assert.Equal(10, p.Gold);
            Assert.Equal(OrderType.None, w.CurrentOrder.Type);
        }

        [Fact]
        public void TrainingUsesSupplyQueueAndRefunds()
        {
            var m = NewRts();
            var p = m.Players[0];
            var hall = Hall(m, p);
            for (int i = 0; i < 5; i++) Assert.True(m.TryTrain(hall, "rts_dg_squire"));
            Assert.Equal(500 - 5 * 75, p.Gold);
            Assert.Equal(10, p.SupplyUsed);
            m.Events.Clear();
            Assert.False(m.TryTrain(hall, "rts_dg_squire"));
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.Key == "The training queue is full.");
            // Cancel the last one: full refund, supply freed.
            m.IssueOrder(hall, new Order { Type = OrderType.CancelQueue, UnitId = hall.Id, Slot = -1 });
            Assert.Equal(500 - 4 * 75, p.Gold);
            Assert.Equal(4, hall.TrainQueue.Count);
            Assert.Equal(9, p.SupplyUsed);
            TestUtil.Run(m, 14f * 2 + 1f);
            Assert.Equal(7, Owned(m, p, UnitKind.Worker).Count);
            Assert.Equal(2, p.UnitsTrained);
            Assert.Equal(9, p.SupplyUsed); // 7 living + 2 still queued
            // Supply cap: 10 used after the queue drains; one more squire exceeds nothing, a sixth queued one would.
            TestUtil.Run(m, 14f * 2 + 1f);
            Assert.Equal(9, Owned(m, p, UnitKind.Worker).Count);
            Assert.True(m.TryTrain(hall, "rts_dg_squire"));
            m.Events.Clear();
            Assert.False(m.TryTrain(hall, "rts_dg_squire"));
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.Key.StartsWith("Not enough supply"));
            // Things a hall cannot train.
            m.Events.Clear();
            Assert.False(m.TryTrain(hall, "rts_dg_footman"));
            Assert.False(m.TryTrain(hall, "rts_al_acolyte"));
            Assert.False(m.TryTrain(hall, "definitely_not_a_unit"));
            Assert.Equal(3, m.Events.Count(e => e.Type == SimEventType.Error && e.Key == "Can't train that here."));
        }

        [Fact]
        public void NewWorkersGoToTheVeinAndRallyPointsAreObeyed()
        {
            var m = NewRts();
            var p = m.Players[0];
            var hall = Hall(m, p);
            Assert.True(m.TryTrain(hall, "rts_dg_squire"));
            TestUtil.Run(m, 15f);
            var fresh = Owned(m, p, UnitKind.Worker).OrderByDescending(u => u.Id).First();
            Assert.Equal(OrderType.Harvest, fresh.CurrentOrder.Type);
            Assert.Equal(MainVein(m, p).Id, fresh.CurrentOrder.TargetId);
            // A barracks with a rally point sends footmen there.
            p.Gold = 5000; p.Lumber = 5000;
            var barracks = BuildAndFinish(m, p, "rts_dg_barracks", FindSpot(m, p, "rts_dg_barracks"), builders: 3);
            var rally = barracks.Position + new Vector2(8, 8);
            m.IssueOrder(barracks, new Order { Type = OrderType.SetRally, UnitId = barracks.Id, Point = rally });
            Assert.True(m.TryTrain(barracks, "rts_dg_footman"));
            TestUtil.Run(m, Def("rts_dg_footman").BuildTime + 8f);
            var footman = Owned(m, p, UnitKind.Soldier).Single();
            Assert.True(Vector2.Distance(footman.Position, rally) < 1.5f, $"footman at {footman.Position}, rally {rally}");
            // The workshop needs the barracks, which now exists.
            var w = Owned(m, p, UnitKind.Worker)[0];
            m.Events.Clear();
            m.IssueOrder(w, new Order { Type = OrderType.Build, UnitId = w.Id, ItemId = "rts_dg_workshop", Point = FindSpot(m, p, "rts_dg_workshop") });
            Assert.DoesNotContain(m.Events, e => e.Type == SimEventType.Error);
        }

        [Fact]
        public void CancellingConstructionRefundsMostOfTheCost()
        {
            var m = NewRts();
            var p = m.Players[0];
            var spot = FindSpot(m, p, "rts_dg_barracks");
            var w = Owned(m, p, UnitKind.Worker)[0];
            m.IssueOrder(w, new Order { Type = OrderType.Build, UnitId = w.Id, ItemId = "rts_dg_barracks", Point = spot });
            Unit site = null;
            while (site == null) { m.Step(); site = m.Units.FirstOrDefault(u => u.DefId == "rts_dg_barracks"); }
            Assert.Equal(500 - 160, p.Gold);
            Assert.False(m.Grid.IsWalkable(spot));
            m.IssueOrder(site, new Order { Type = OrderType.CancelQueue, UnitId = site.Id });
            Assert.Equal(500 - 160 + 120, p.Gold);
            Assert.Equal(150 - 60 + 45, p.Lumber);
            TestUtil.Run(m, 0.5f);
            Assert.True(m.Grid.IsWalkable(spot), "the footprint is released");
            Assert.DoesNotContain(m.Units, u => u.DefId == "rts_dg_barracks");
            Assert.Equal(OrderType.None, w.CurrentOrder.Type);
        }

        [Fact]
        public void WatchtowersShootOnlyOnceFinished()
        {
            var m = NewRts();
            var p = m.Players[0];
            var enemy = m.Players[1];
            p.Gold = 5000; p.Lumber = 5000;
            var spot = FindSpot(m, p, "rts_dg_watchtower");
            var w = Owned(m, p, UnitKind.Worker)[0];
            m.IssueOrder(w, new Order { Type = OrderType.Build, UnitId = w.Id, ItemId = "rts_dg_watchtower", Point = spot });
            Unit tower = null;
            while (tower == null) { m.Step(); tower = m.Units.FirstOrDefault(u => u.DefId == "rts_dg_watchtower"); }
            var intruder = m.CreateUnit(Def("rts_al_thrall"), enemy.Team, m.Grid.NearestWalkable(spot + new Vector2(4, 0)), 0f, enemy);
            m.IssueOrder(intruder, Order.HoldOrder(intruder.Id));
            TestUtil.Run(m, 5f);
            Assert.Equal(intruder.Stats.MaxHp, intruder.Hp, 0);
            TestUtil.Run(m, Def("rts_dg_watchtower").BuildTime + 5f);
            Assert.False(tower.UnderConstruction);
            Assert.True(intruder.Dead || intruder.Hp < intruder.Stats.MaxHp, "the finished tower should shoot");
        }

        [Fact]
        public void RazingEveryBuildingWinsAndRuinsAreCleared()
        {
            var m = NewRts();
            var dawn = m.Players[0];
            var dusk = m.Players[1];
            var hall = Hall(m, dusk);
            var footman = m.CreateUnit(Def("rts_dg_footman"), dawn.Team, m.Grid.NearestWalkable(hall.Position + new Vector2(-4, -4)), 0f, dawn);
            TestUtil.Run(m, 0.1f);
            hall.Hp = 1f;
            m.IssueOrder(footman, Order.Attack(footman.Id, hall.Id));
            TestUtil.Run(m, 5f);
            Assert.True(hall.Dead);
            Assert.Equal(MatchPhase.PostGame, m.Phase);
            Assert.Equal(Team.Dawn, m.Winner);
            Assert.True(dusk.Eliminated);
            Assert.Equal(1, dusk.BuildingsLost);
            Assert.Equal(1, dawn.BuildingsRazed);
            Assert.Contains(m.Events, e => e.Type == SimEventType.PlayerEliminated && e.OtherId == dusk.Id);
        }

        [Fact]
        public void DestroyedBuildingsReleaseTheirGround()
        {
            var m = NewRts();
            var p = m.Players[0];
            var spot = FindSpot(m, p, "rts_dg_sun_shrine");
            var shrine = BuildAndFinish(m, p, "rts_dg_sun_shrine", spot);
            Assert.False(m.Grid.IsWalkable(spot));
            m.KillUnit(shrine, null);
            Assert.True(m.Grid.IsWalkable(spot));
            TestUtil.Run(m, m.Rules.RtsRuinRemoveDelay + 1f);
            Assert.True(shrine.Removed);
            Assert.Equal(10, p.SupplyCap);
            Assert.Equal(MatchPhase.Playing, m.Phase); // the citadel still stands
        }

        [Fact]
        public void PlayersCannotCommandOrTrainWithSomeoneElsesUnits()
        {
            var m = NewRts();
            var dawn = m.Players[0];
            var dusk = m.Players[1];
            var enemyHall = Hall(m, dusk);
            var enemyWorker = Owned(m, dusk, UnitKind.Worker)[0];
            int gold = dusk.Gold, dawnGold = dawn.Gold;
            m.SubmitOrder(dawn, new Order { Type = OrderType.Train, UnitId = enemyHall.Id, ItemId = "rts_al_acolyte" });
            m.SubmitOrder(dawn, new Order { Type = OrderType.CancelQueue, UnitId = enemyHall.Id });
            m.SubmitOrder(dawn, Order.MoveTo(enemyWorker.Id, new Vector2(70, 70)));
            m.SubmitOrder(dawn, new Order { Type = OrderType.Build, UnitId = enemyWorker.Id, ItemId = "rts_al_obelisk", Point = FindSpot(m, dusk, "rts_al_obelisk") });
            TestUtil.Run(m, 2f);
            Assert.True(enemyHall.TrainQueue == null || enemyHall.TrainQueue.Count == 0);
            Assert.Equal(gold, dusk.Gold);
            Assert.Equal(dawnGold, dawn.Gold);
            Assert.Equal(OrderType.None, enemyWorker.CurrentOrder.Type);
            // Mines cannot be attacked.
            var footman = m.CreateUnit(Def("rts_dg_footman"), dawn.Team, m.Grid.NearestWalkable(MainVein(m, dawn).Position + new Vector2(3, 0)), 0f, dawn);
            Assert.False(m.CanAttackTarget(footman, MainVein(m, dawn), out _));
        }

        [Fact]
        public void NeutralCampsGuardExpansionsOnceAndPayBounty()
        {
            var m = NewRts(neutrals: true);
            TestUtil.Run(m, 1f);
            var neutrals = m.Units.Where(u => u.IsNeutral && u.IsAlive).ToList();
            Assert.True(neutrals.Count >= 7, $"{neutrals.Count} neutrals");
            var p = m.Players[0];
            var victim = neutrals.First();
            var footman = m.CreateUnit(Def("rts_dg_footman"), p.Team, m.Grid.NearestWalkable(victim.Position + new Vector2(2, 0)), 0f, p);
            TestUtil.Run(m, 0.1f);
            int gold = p.Gold;
            m.DealDamage(new DamageInfo { Source = footman, Target = victim, Amount = 99999f, Type = DamageType.Pure });
            Assert.True(victim.Dead);
            Assert.True(p.Gold > gold, "the neutral bounty goes to the soldier's owner");
            foreach (var n in m.Units.Where(u => u.IsNeutral && u.IsAlive).ToList()) m.KillUnit(n, null);
            TestUtil.Run(m, 130f);
            Assert.DoesNotContain(m.Units, u => u.IsNeutral && u.IsAlive);
        }

        [Fact]
        public void RtsSimulationIsDeterministic()
        {
            string Run()
            {
                var m = NewRts(seed: 11);
                foreach (var p in m.Players)
                {
                    var ws = Owned(m, p, UnitKind.Worker);
                    var vein = MainVein(m, p);
                    int tree = NearestTree(m, Hall(m, p).Position);
                    for (int i = 0; i < ws.Count; i++)
                        m.IssueOrder(ws[i], i < 3 ? new Order { Type = OrderType.Harvest, UnitId = ws[i].Id, TargetId = vein.Id }
                                                  : new Order { Type = OrderType.Harvest, UnitId = ws[i].Id, Point = m.TreePosition(tree) });
                    m.TryTrain(Hall(m, p), p.RtsFaction.Worker);
                }
                TestUtil.Run(m, 90f);
                return string.Join("|", m.Players.Select(p => $"{p.Gold},{p.Lumber},{p.SupplyUsed}"))
                       + "|" + string.Join(";", m.Units.Where(u => u.Owner != null).Select(u => $"{u.Id}:{u.Position.X:0.000},{u.Position.Y:0.000}"));
            }
            Assert.Equal(Run(), Run());
        }

        [Fact]
        public void AttackMoveResumesWhenTheTargetDiesMidSwing()
        {
            // Regression: a unit whose target died during its wind-up stayed frozen in the wind-up forever.
            var m = NewRts();
            var dawn = m.Players[0];
            var dusk = m.Players[1];
            var start = m.Grid.NearestWalkable(new Vector2(60, 60));
            var goal = m.Grid.NearestWalkable(new Vector2(80, 80));
            var archer = m.CreateUnit(Def("rts_dg_arbalist"), dawn.Team, start, 0f, dawn);
            var victim = m.CreateUnit(Def("rts_al_thrall"), dusk.Team, m.Grid.NearestWalkable(start + new Vector2(3, 3)), 0f, dusk);
            m.IssueOrder(victim, Order.HoldOrder(victim.Id));
            TestUtil.Run(m, 0.1f);
            m.IssueOrder(archer, Order.AttackMoveTo(archer.Id, goal));
            for (int i = 0; i < 90 && archer.Action != ActionState.AttackWindup; i++) m.Step();
            Assert.Equal(ActionState.AttackWindup, archer.Action);
            m.KillUnit(victim, null);
            TestUtil.Run(m, 8f);
            Assert.NotEqual(ActionState.AttackWindup, archer.Action);
            Assert.True(Vector2.Distance(archer.Position, goal) < 1f, $"archer stuck at {archer.Position}");
        }

        [Fact]
        public void BotsPlayACompleteGame()
        {
            // A Normal bot against a Beginner bot: both run an economy, build a base and armies, and the stronger
            // one razes the other within 30 minutes. The AI plays through the same validated orders as a human.
            var cfg = new MatchConfig { ModeId = "rts_1v1", MapId = "map_rts_ashfields", Seed = 701, SkipHeroSelect = true };
            cfg.Players.Add(new PlayerSetup { Name = "Dawn", Team = Team.Dawn, IsBot = true, BotDifficulty = BotDifficulty.Normal, RtsFaction = "dawnguard" });
            cfg.Players.Add(new PlayerSetup { Name = "Dusk", Team = Team.Dusk, IsBot = true, BotDifficulty = BotDifficulty.Beginner, RtsFaction = "ashen_legion" });
            var m = new Match(TestUtil.Data, cfg);
            int errors = 0;
            while (m.Phase != MatchPhase.PostGame && m.Time < 30 * 60)
            {
                m.Step();
                errors += m.Events.Count(e => e.Type == SimEventType.Error);
                m.Events.Clear();
            }
            Assert.Equal(Team.Dawn, m.Winner);
            foreach (var p in m.Players)
            {
                Assert.NotNull(m.RtsAiOf(p));
                Assert.True(p.GoldMined > 3000, $"{p.Name} mined {p.GoldMined}");
                Assert.True(p.LumberHarvested > 500, $"{p.Name} cut {p.LumberHarvested}");
                Assert.True(p.UnitsTrained >= 15, $"{p.Name} trained {p.UnitsTrained}");
                Assert.True(p.BuildingsBuilt >= 5, $"{p.Name} built {p.BuildingsBuilt}");
            }
            Assert.True(m.Players[0].BuildingsRazed >= m.Players[1].BuildingsBuilt, "the winner razed the loser's base");
            // Bots issue orders like players; a few rejections (a site blocked on arrival) are normal, a flood is a bug.
            Assert.True(errors < 60, $"{errors} rejected bot orders");
        }

        [Fact]
        public void AshfieldsDataIsValid()
        {
            var d = TestUtil.Data;
            Assert.Empty(d.Errors);
            var map = d.Maps["map_rts_ashfields"];
            Assert.Equal(2, map.StartLocations.Count);
            Assert.True(map.ResourceNodes.Count >= 6);
            Assert.Equal("map_rts_ashfields", d.Modes["rts_1v1"].Map);
            Assert.Equal(GameModeKind.Rts, d.Modes["rts_1v1"].Kind);
            foreach (var f in d.RtsFactions.Values)
            {
                var worker = d.Units[f.Worker];
                // Every building a worker can raise is reachable through the tech tree from the hall.
                foreach (var b in worker.Builds)
                    foreach (var req in d.Units[b].Requires ?? new List<string>())
                        Assert.Contains(req, worker.Builds);
                Assert.Contains(f.Hall, worker.Builds);
                Assert.Contains(f.Worker, d.Units[f.Hall].Trains);
                Assert.Contains(worker.Builds, b => d.Units[b].SupplyProvided > 0);
            }
        }
    }
}
