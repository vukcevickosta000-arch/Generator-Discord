using System.Linq;
using System.Numerics;
using Bloodfall.Data;
using Bloodfall.Simulation;
using Xunit;
using static Bloodfall.Tests.RtsTests;

namespace Bloodfall.Tests
{
    /// <summary>RTS R2: research, faction mechanics (Legion raising, Dawnguard shrines) and neutral camps that guard.</summary>
    public class RtsFactionTests
    {
        private static Order Research(Unit b, string id) => new Order { Type = OrderType.Research, UnitId = b.Id, ItemId = id };

        [Fact]
        public void ResearchIsPaidQueuedAndStrengthensOldAndNewUnits()
        {
            var m = NewRts();
            var p = m.Players[0];
            var barracks = BuildAndFinish(m, p, "rts_dg_barracks", FindSpot(m, p, "rts_dg_barracks"));
            p.Gold = 1000;
            p.Lumber = 1000;
            var spot = m.Grid.NearestWalkable(Hall(m, p).Position + new Vector2(-6, 6));
            var footman = m.CreateUnit(Def("rts_dg_footman"), p.Team, spot, 0f, p);
            var arbalist = m.CreateUnit(Def("rts_dg_arbalist"), p.Team, spot, 0f, p);
            TestUtil.Run(m, 0.1f);
            float footBefore = footman.Stats.BonusDamage, arbBefore = arbalist.Stats.BonusDamage;
            int supply = p.SupplyUsed;

            m.IssueOrder(barracks, Research(barracks, "rts_dg_up_blades_1"));
            Assert.Equal(900, p.Gold);
            Assert.Equal(950, p.Lumber);
            Assert.Equal(new[] { "rts_dg_up_blades_1" }, barracks.TrainQueue);
            Assert.Equal(supply, p.SupplyUsed); // research costs no supply
            m.Events.Clear();
            m.IssueOrder(barracks, Research(barracks, "rts_dg_up_blades_1"));
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.Key == "Already being researched.");
            Assert.Equal(900, p.Gold);

            m.Events.Clear();
            TestUtil.Run(m, TestUtil.Data.Upgrades["rts_dg_up_blades_1"].ResearchTime + 1f);
            Assert.Contains("rts_dg_up_blades_1", p.Upgrades);
            Assert.Contains(m.Events, e => e.Type == SimEventType.ResearchComplete && e.Key == "rts_dg_up_blades_1");
            Assert.Empty(barracks.TrainQueue);
            Assert.Equal(footBefore + 2f, footman.Stats.BonusDamage, 3);
            Assert.Equal(arbBefore, arbalist.Stats.BonusDamage, 3); // melee only

            var recruit = m.CreateUnit(Def("rts_dg_footman"), p.Team, spot, 0f, p);
            TestUtil.Run(m, 0.1f);
            Assert.Equal(footman.Stats.BonusDamage, recruit.Stats.BonusDamage, 3);
            Assert.Equal(0f, m.Players[1].Upgrades.Count); // the enemy is unaffected

            m.Events.Clear();
            m.IssueOrder(barracks, Research(barracks, "rts_dg_up_blades_1"));
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.Key == "Already researched.");
        }

        [Fact]
        public void ResearchNeedsTheRightBuildingAndPrerequisitesAndCancelRefunds()
        {
            var m = NewRts();
            var p = m.Players[0];
            var barracks = BuildAndFinish(m, p, "rts_dg_barracks", FindSpot(m, p, "rts_dg_barracks"));
            p.Gold = 1000;
            p.Lumber = 1000;

            m.Events.Clear();
            m.IssueOrder(Hall(m, p), Research(Hall(m, p), "rts_dg_up_blades_1"));
            m.IssueOrder(barracks, Research(barracks, "rts_al_up_claws_1"));
            m.IssueOrder(barracks, Research(barracks, "not_an_upgrade"));
            Assert.Equal(3, m.Events.Count(e => e.Type == SimEventType.Error && e.Key == "Can't research that here."));
            m.Events.Clear();
            m.IssueOrder(barracks, Research(barracks, "rts_dg_up_blades_2"));
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.Key == "Requires Forged Blades.");
            Assert.Equal(1000, p.Gold);

            // Queue two, cancel the last: exact refund, the first keeps going.
            m.IssueOrder(barracks, Research(barracks, "rts_dg_up_blades_1"));
            m.IssueOrder(barracks, Research(barracks, "rts_dg_up_plate"));
            Assert.Equal(1000 - 100 - 100, p.Gold);
            Assert.Equal(1000 - 50 - 75, p.Lumber);
            m.IssueOrder(barracks, new Order { Type = OrderType.CancelQueue, UnitId = barracks.Id, Slot = -1 });
            Assert.Equal(900, p.Gold);
            Assert.Equal(950, p.Lumber);
            Assert.Equal(new[] { "rts_dg_up_blades_1" }, barracks.TrainQueue);

            TestUtil.Run(m, 41f);
            m.Events.Clear();
            m.IssueOrder(barracks, Research(barracks, "rts_dg_up_blades_2"));
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.Key == "Requires Sanctum of Dawn.");
            Assert.Equal(900, p.Gold);

            // Poor players are told why.
            p.Gold = 10;
            m.Events.Clear();
            m.IssueOrder(barracks, Research(barracks, "rts_dg_up_plate"));
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.Key == "Not enough blood-iron.");
        }

        [Fact]
        public void AshenLegionRaisesTheLivingFallenAsTemporarySkeletons()
        {
            var m = NewRts();
            var dawn = m.Players[0];
            var legion = m.Players[1];
            var at = m.Grid.NearestWalkable(new Vector2(60, 60));
            var thrall = m.CreateUnit(Def("rts_al_thrall"), legion.Team, at, 0f, legion);
            // New units join the world on the next tick.
            Unit Spawn(string id, Player owner, float dx)
            {
                var u = m.CreateUnit(Def(id), owner.Team, m.Grid.NearestWalkable(at + new Vector2(dx, 2f)), 0f, owner);
                m.Step();
                return u;
            }
            void Kill(Unit victim, Unit by)
            {
                m.DealDamage(new DamageInfo { Source = by, Target = victim, Amount = 99999f, Type = DamageType.Pure });
                m.Step();
            }
            int Risen() => m.Units.Count(u => u.DefId == "rts_al_risen" && u.IsAlive && u.Owner == legion);
            TestUtil.Run(m, 0.1f);
            int supply = legion.SupplyUsed;

            Kill(Spawn("rts_dg_footman", dawn, 1f), thrall);
            Assert.Equal(1, Risen());
            Assert.Equal(1, legion.UnitsRaised);
            Assert.Equal(supply, legion.SupplyUsed); // skeletons cost no supply
            var skeleton = m.Units.Single(u => u.DefId == "rts_al_risen");
            Assert.Equal(30f, skeleton.Lifetime, 1);

            // Once per cooldown.
            Kill(Spawn("rts_dg_footman", dawn, 2f), thrall);
            Assert.Equal(1, legion.UnitsRaised);

            TestUtil.Run(m, legion.RtsFaction.RaiseCooldown + 0.5f);
            var footman = Spawn("rts_dg_footman", dawn, 3f);
            // The undead and siege engines do not rise.
            Kill(Spawn("rts_al_thrall", legion, 4f), footman);
            Kill(Spawn("rts_dg_ballista", dawn, 5f), thrall);
            Assert.Equal(1, legion.UnitsRaised);
            Kill(footman, thrall);
            Assert.Equal(2, legion.UnitsRaised);

            TestUtil.Run(m, 6f);
            Assert.False(skeleton.IsAlive, "the first skeleton crumbles after 30 s");
            Assert.Equal(0, dawn.UnitsRaised);
        }

        [Fact]
        public void TheKillersSideRaisesFirstInALegionMirror()
        {
            // It used to be player order, which always favoured the first player.
            var m = NewRts("ashen_legion", "ashen_legion", neutrals: true);
            TestUtil.Run(m, 1f);
            var creep = m.Units.First(u => u.IsNeutral && u.IsAlive && !(u.UnitDef.Tags?.Contains("undead") ?? false));
            var near = m.CreateUnit(Def("rts_al_thrall"), Team.Dawn, m.Grid.NearestWalkable(creep.Position + new Vector2(1, 0)), 0f, m.Players[0]);
            var killer = m.CreateUnit(Def("rts_al_thrall"), Team.Dusk, m.Grid.NearestWalkable(creep.Position + new Vector2(-5, 0)), 0f, m.Players[1]);
            m.Step();
            m.DealDamage(new DamageInfo { Source = killer, Target = creep, Amount = 99999f, Type = DamageType.Pure });
            Assert.Equal(0, m.Players[0].UnitsRaised);
            Assert.Equal(1, m.Players[1].UnitsRaised);
            Assert.NotNull(near);
        }

        [Fact]
        public void SunShrinesHealNearbyUnitsOnceBuilt()
        {
            var m = NewRts();
            var p = m.Players[0];
            var shrine = BuildAndFinish(m, p, "rts_dg_sun_shrine", FindSpot(m, p, "rts_dg_sun_shrine"));
            var near = m.CreateUnit(Def("rts_dg_footman"), p.Team, m.Grid.NearestWalkable(shrine.Position + new Vector2(2.5f, 0)), 0f, p);
            var far = m.CreateUnit(Def("rts_dg_footman"), p.Team, m.Grid.NearestWalkable(new Vector2(60, 60)), 0f, p);
            TestUtil.Run(m, 0.5f);
            near.Hp = far.Hp = 100f;
            TestUtil.Run(m, 5f);
            Assert.True(near.Hp - 100f > far.Hp - 100f + 10f, $"near healed {near.Hp - 100f:0.0}, far {far.Hp - 100f:0.0}");
            Assert.Contains(near.Statuses, s => s.Def.Id == "rts_dg_sunlit");
            Assert.DoesNotContain(far.Statuses, s => s.Def.Id == "rts_dg_sunlit");
        }

        [Fact]
        public void ExpansionCampsAttackIntrudersButTheCentreCampOnlyRetaliates()
        {
            var m = NewRts(neutrals: true);
            TestUtil.Run(m, 1f);
            var p = m.Players[0];
            var guard = m.Units.First(u => u.IsNeutral && u.IsAlive && u.CampGuards);
            var centre = m.Units.First(u => u.IsNeutral && u.IsAlive && !u.CampGuards);
            Assert.Equal("camp_neutral_ancient_6", centre.CampId);

            var intruder = m.CreateUnit(Def("rts_dg_footman"), p.Team, m.Grid.NearestWalkable(guard.HomePosition + new Vector2(3, 0)), 0f, p);
            var visitor = m.CreateUnit(Def("rts_dg_footman"), p.Team, m.Grid.NearestWalkable(centre.HomePosition + new Vector2(3.5f, 0)), 0f, p);
            // An army attack-moving past the centre camp leaves it alone too.
            var from = m.Grid.NearestWalkable(centre.HomePosition + new Vector2(-9, 3));
            var to = m.Grid.NearestWalkable(centre.HomePosition + new Vector2(9, 3));
            var marcher = m.CreateUnit(Def("rts_dg_footman"), p.Team, from, 0f, p);
            m.IssueOrder(marcher, Order.AttackMoveTo(marcher.Id, to));
            TestUtil.Run(m, 6f);

            Assert.True(intruder.Hp < intruder.Stats.MaxHp, "the expansion camp attacks a soldier on its ground");
            Assert.Equal(visitor.Stats.MaxHp, visitor.Hp);
            Assert.Equal(marcher.Stats.MaxHp, marcher.Hp);
            Assert.All(m.Units.Where(u => u.CampId == centre.CampId), c => Assert.Equal(c.Stats.MaxHp, c.Hp));
            Assert.True(Vector2.Distance(marcher.Position, to) < 1.5f, $"marcher at {marcher.Position}, goal {to}");

            // Attacked, the centre camp fights back.
            m.IssueOrder(visitor, Order.Attack(visitor.Id, centre.Id));
            TestUtil.Run(m, 4f);
            Assert.True(visitor.Hp < visitor.Stats.MaxHp);
        }
    }
}
