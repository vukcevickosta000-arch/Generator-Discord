using System.Linq;
using System.Numerics;
using Bloodfall.Data;
using Bloodfall.Simulation;
using Xunit;

namespace Bloodfall.Tests
{
    /// <summary>Blood War couriers: bought items in the stash are flown to the hero on the ` order.</summary>
    public class CourierTests
    {
        private static Order Deliver(Unit courier) => new Order { Type = OrderType.CourierDeliver, UnitId = courier.Id };

        private static (Match m, Player p, Unit hero, Unit courier) FieldMatch()
        {
            var m = TestUtil.NewMatch();
            TestUtil.Run(m, 1.2f);
            var p = m.Players[0];
            var hero = p.Hero;
            // Away from the shop: purchases go to the stash.
            TestUtil.Teleport(m, hero, m.Grid.NearestWalkable(new Vector2(m.Grid.WorldWidth * 0.42f, m.Grid.WorldHeight * 0.42f)));
            hero.CurrentOrder = default;
            p.Gold = 2000;
            return (m, p, hero, p.Courier);
        }

        [Fact]
        public void EveryPlayerHasACourierAtTheFountain()
        {
            var m = TestUtil.NewMatch(dawn: 2, dusk: 2);
            TestUtil.Run(m, 0.5f);
            foreach (var p in m.Players)
            {
                Assert.NotNull(p.Courier);
                Assert.Equal(UnitKind.Courier, p.Courier.Kind);
                Assert.True(p.Courier.Flying);
                Assert.Equal(p, p.Courier.Owner);
                Assert.True(Vector2.Distance(p.Courier.Position, (Vector2)m.Map.Bases.First(b => b.Team == p.Team).Fountain) < 5f);
            }
        }

        [Fact]
        public void TheCourierBringsBoughtItemsToTheHeroAndFliesHome()
        {
            var (m, p, hero, courier) = FieldMatch();
            m.IssueOrder(hero, new Order { Type = OrderType.BuyItem, UnitId = hero.Id, ItemId = "item_gauntlets_might" });
            m.IssueOrder(hero, new Order { Type = OrderType.BuyItem, UnitId = hero.Id, ItemId = "item_iron_circlet" });
            Assert.Equal(2, hero.Stash.Count(i => i != null));
            Assert.DoesNotContain(hero.Inventory, i => i != null && i.Def.Id == "item_gauntlets_might");

            m.Events.Clear();
            m.IssueOrder(courier, Deliver(courier));
            bool delivered = false;
            for (int i = 0; i < 60 * 30 && !delivered; i++)
            {
                m.Step();
                delivered = m.Events.Any(e => e.Type == SimEventType.CourierDelivered && e.PlayerId == p.Id);
            }
            Assert.True(delivered, "the courier reached the hero");
            Assert.Contains(hero.Inventory, i => i != null && i.Def.Id == "item_gauntlets_might");
            Assert.Contains(hero.Inventory, i => i != null && i.Def.Id == "item_iron_circlet");
            Assert.All(hero.Stash, i => Assert.Null(i));
            Assert.Empty(courier.Carried);
            TestUtil.Run(m, 40f);
            Assert.Equal(CourierState.Idle, courier.CourierState);
            Assert.True(Vector2.Distance(courier.Position, courier.HomePosition) < 1.5f, "the courier flew home");
        }

        [Fact]
        public void NothingToDeliverIsReported()
        {
            var (m, p, hero, courier) = FieldMatch();
            m.Events.Clear();
            Assert.False(m.TryCourierDeliver(courier));
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.Key.StartsWith("Nothing to deliver"));
            Assert.Equal(CourierState.Idle, courier.CourierState);
        }

        [Fact]
        public void AKilledCourierKeepsItsCargoRespawnsAndReturnsItToTheStash()
        {
            var (m, p, hero, courier) = FieldMatch();
            m.IssueOrder(hero, new Order { Type = OrderType.BuyItem, UnitId = hero.Id, ItemId = "item_gauntlets_might" });
            m.IssueOrder(courier, Deliver(courier));
            TestUtil.Run(m, 1f);
            Assert.Single(courier.Carried);
            Assert.Equal(CourierState.Delivering, courier.CourierState);
            int enemyGold = m.Players[1].Gold;
            m.DealDamage(new DamageInfo { Source = m.Players[1].Hero, Target = courier, Amount = 99999f, Type = DamageType.Pure });
            Assert.True(courier.Dead);
            Assert.True(m.Players[1].Gold >= enemyGold + 50, "the courier's bounty goes to its killer");
            m.Events.Clear();
            Assert.False(m.TryCourierDeliver(courier));
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.Key.StartsWith("Your courier is dead"));

            TestUtil.Run(m, m.Rules.CourierRespawnTime + 1f);
            Assert.True(courier.IsAlive);
            Assert.True(Vector2.Distance(courier.Position, courier.HomePosition) < 1.5f);
            Assert.Empty(courier.Carried);
            Assert.Contains(hero.Stash, i => i != null && i.Def.Id == "item_gauntlets_might"); // nothing bought is lost
        }

        [Fact]
        public void BotsHaveTheirStashFlownToThem()
        {
            var m = TestUtil.NewMatch(bots: true);
            TestUtil.Run(m, 1.2f);
            var p = m.Players[0];
            var hero = p.Hero;
            TestUtil.Teleport(m, hero, m.Grid.NearestWalkable(new Vector2(m.Grid.WorldWidth * 0.42f, m.Grid.WorldHeight * 0.42f)));
            p.Gold = 2000;
            m.IssueOrder(hero, new Order { Type = OrderType.BuyItem, UnitId = hero.Id, ItemId = "item_iron_circlet" });
            Assert.Contains(hero.Stash, i => i != null);
            TestUtil.Run(m, 3f);
            Assert.NotEqual(CourierState.Idle, p.Courier.CourierState);
        }
    }
}
