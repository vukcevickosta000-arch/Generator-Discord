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
    
        [Fact]
        public void TheCrimsonCourtIsPaidInBloodForItsKills()
        {
            var m = NewRts("crimson_court", "dawnguard");
            var court = m.Players[0];
            var dawn = m.Players[1];
            var at = m.Grid.NearestWalkable(new Vector2(60, 60));
            var duelist = m.CreateUnit(Def("rts_cc_duelist"), court.Team, at, 0f, court);
            var footman = m.CreateUnit(Def("rts_dg_footman"), dawn.Team, m.Grid.NearestWalkable(at + new Vector2(2, 0)), 0f, dawn);
            m.Step();
            int courtGold = court.Gold, dawnGold = dawn.Gold;
            m.DealDamage(new DamageInfo { Source = duelist, Target = footman, Amount = 99999f, Type = DamageType.Pure });
            int price = (int)(Def("rts_dg_footman").GoldCost * court.RtsFaction.BloodPrice);
            Assert.True(price > 0);
            Assert.Equal(courtGold + price, court.Gold);
            Assert.Equal(price, court.BloodPriceEarned);
            // Other factions are not paid for their kills.
            m.DealDamage(new DamageInfo { Source = m.CreateUnit(Def("rts_dg_footman"), dawn.Team, at, 0f, dawn), Target = duelist, Amount = 99999f, Type = DamageType.Pure });
            Assert.Equal(dawnGold, dawn.Gold);
            Assert.Equal(0, dawn.BloodPriceEarned);
        }

        [Fact]
        public void TheWildCovenantGrowsStrongerAtNightAndShiftersBecomeWolves()
        {
            var m = NewRts("wild_covenant", "dawnguard");
            var cov = m.Players[0];
            var dawn = m.Players[1];
            var at = m.Grid.NearestWalkable(new Vector2(40, 60));
            var shifter = m.CreateUnit(Def("rts_wc_shifter"), cov.Team, at, 0f, cov);
            var thornshot = m.CreateUnit(Def("rts_wc_thornshot"), cov.Team, m.Grid.NearestWalkable(at + new Vector2(2, 0)), 0f, cov);
            var footman = m.CreateUnit(Def("rts_dg_footman"), dawn.Team, m.Grid.NearestWalkable(new Vector2(100, 90)), 0f, dawn);
            TestUtil.Run(m, 0.2f);
            Assert.False(m.IsNight);
            float dayDamage = thornshot.Stats.BonusDamage;
            Assert.Equal("rts_wc_shifter", shifter.ModelKey);

            m.ForcedNightUntil = m.Time + 60f;
            TestUtil.Run(m, 0.2f);
            Assert.True(m.IsNight);
            Assert.Contains(thornshot.Statuses, st => st.Def.Id == "rts_wc_moonlit");
            Assert.True(thornshot.Stats.BonusDamage > dayDamage + 1f, $"night bonus {thornshot.Stats.BonusDamage - dayDamage:0.00}");
            Assert.Equal("rts_wc_shifter_wolf", shifter.ModelKey);
            Assert.DoesNotContain(thornshot.Statuses, st => st.Def.Id == "rts_wc_wolf_form"); // only shifters change shape
            Assert.DoesNotContain(footman.Statuses, st => st.Def.Id == "rts_wc_moonlit");
            var recruit = m.CreateUnit(Def("rts_wc_shifter"), cov.Team, at, 0f, cov);
            TestUtil.Run(m, 0.1f);
            Assert.Equal("rts_wc_shifter_wolf", recruit.ModelKey); // units raised at night join in their night form

            m.ForcedNightUntil = m.Time;
            TestUtil.Run(m, 0.2f);
            Assert.False(m.IsNight);
            Assert.DoesNotContain(thornshot.Statuses, st => st.Def.Id == "rts_wc_moonlit");
            Assert.Equal(dayDamage, thornshot.Stats.BonusDamage, 3);
            Assert.Equal("rts_wc_shifter", shifter.ModelKey);
            Assert.Equal("rts_wc_shifter", recruit.ModelKey);
        }

        [Fact]
        public void CourtAndCovenantBotsBuildTheirTechAndFight()
        {
            // Ten minutes of the two newer factions against each other: both run an economy, build their tech and
            // armies, and research, with only a handful of rejected orders.
            var cfg = new MatchConfig { ModeId = "rts_1v1", MapId = "map_rts_ashfields", Seed = 77, SkipHeroSelect = true };
            cfg.Players.Add(new PlayerSetup { Name = "Dawn", Team = Team.Dawn, IsBot = true, BotDifficulty = BotDifficulty.Normal, RtsFaction = "crimson_court" });
            cfg.Players.Add(new PlayerSetup { Name = "Dusk", Team = Team.Dusk, IsBot = true, BotDifficulty = BotDifficulty.Normal, RtsFaction = "wild_covenant" });
            var m = new Match(TestUtil.Data, cfg);
            int errors = 0;
            while (m.Phase != MatchPhase.PostGame && m.Time < 10 * 60)
            {
                m.Step();
                errors += m.Events.Count(e => e.Type == SimEventType.Error);
                m.Events.Clear();
            }
            foreach (var p in m.Players)
            {
                Assert.True(p.GoldMined > 2000, $"{p.Name} mined {p.GoldMined}");
                Assert.True(p.UnitsTrained >= 15, $"{p.Name} trained {p.UnitsTrained}");
                Assert.True(p.BuildingsBuilt >= 8, $"{p.Name} built {p.BuildingsBuilt}");
                var f = p.RtsFaction;
                Assert.True(m.Units.Any(u => u.Owner == p && u.UnitDef?.Trains != null && u.UnitDef.Trains.Any(t => TestUtil.Data.Units[t].Kind == UnitKind.Soldier)),
                    $"{p.Name} ({f.Id}) has a barracks");
            }
            Assert.True(m.Players[0].UnitsKilled + m.Players[1].UnitsKilled > 0, "the armies met");
            Assert.All(m.Players, p => Assert.NotEmpty(p.RtsHeroes));
            Assert.Contains(m.Players.SelectMany(p => p.RtsHeroes), h => h.Level >= 2 && h.Abilities.Any(a => a.Level > 0 && !a.Def.CommonHeroAbility));
            Assert.True(errors < 40, $"{errors} rejected bot orders");
        }
    
        /// <summary>A Dawnguard base with a finished altar, plenty of money and supply for three heroes.</summary>
        private static (Match m, Player p, Unit altar) AltarBase()
        {
            var m = NewRts();
            var p = m.Players[0];
            var altar = BuildAndFinish(m, p, "rts_dg_altar", FindSpot(m, p, "rts_dg_altar"));
            for (int i = 0; i < 3; i++)
                m.CreateUnit(Def("rts_dg_sun_shrine"), p.Team, m.Grid.NearestWalkable(Hall(m, p).Position + new Vector2(-8 + i * 3, -9)), 0f, p);
            p.Gold = 5000;
            p.Lumber = 5000;
            TestUtil.Run(m, 0.2f);
            return (m, p, altar);
        }

        [Fact]
        public void AltarsRecruitUpToThreeHeroesAtRisingPrices()
        {
            var (m, p, altar) = AltarBase();
            var rules = m.Rules;
            int supply = p.SupplyUsed;
            Assert.True(m.TryTrain(altar, "hero_vorak"));
            Assert.Equal(5000 - rules.RtsHeroGold[0], p.Gold);
            Assert.Equal(5000 - rules.RtsHeroLumber[0], p.Lumber);
            Assert.Equal(supply + rules.RtsHeroSupply, p.SupplyUsed);
            m.Events.Clear();
            Assert.False(m.TryTrain(altar, "hero_vorak"));
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.Key == "Already being recruited.");
            Assert.True(m.TryTrain(altar, "hero_ilyra"));
            Assert.True(m.TryTrain(altar, "hero_ardyn"));
            Assert.Equal(5000 - rules.RtsHeroGold.Take(3).Sum(), p.Gold);
            m.Events.Clear();
            Assert.False(m.TryTrain(altar, "hero_thael"));
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.Key.StartsWith("You can lead at most 3 heroes"));
            // Cancelling the last one refunds exactly what it cost.
            m.IssueOrder(altar, new Order { Type = OrderType.CancelQueue, UnitId = altar.Id, Slot = -1 });
            Assert.Equal(5000 - rules.RtsHeroGold[0] - rules.RtsHeroGold[1], p.Gold);
            Assert.Equal(5000 - rules.RtsHeroLumber[0] - rules.RtsHeroLumber[1], p.Lumber);
            m.Events.Clear();
            Assert.False(m.TryTrain(altar, "not_a_hero"));
            Assert.False(m.TryTrain(BuildAndFinish(m, p, "rts_dg_barracks", FindSpot(m, p, "rts_dg_barracks")), "hero_thael"));
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.Key == "That hero can't be recruited.");
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.Key == "Can't train that here.");

            TestUtil.Run(m, 2 * rules.RtsHeroTrainTime + 1f);
            // The queue trains them in order (building the barracks above took time too).
            Assert.Equal(new[] { "hero_vorak", "hero_ilyra" }, p.RtsHeroes.Select(h => h.DefId));
            var vorak = p.RtsHeroes[0];
            Assert.True(vorak.IsAlive && vorak.IsHero && vorak.Owner == p);
            Assert.Equal(1, vorak.Level);
            Assert.Null(p.Hero); // the RTS keeps its heroes in RtsHeroes
            Assert.True(Vector2.Distance(vorak.Position, altar.Position) < 6f);
        }

        [Fact]
        public void FallenHeroesStayDeadUntilRevivedAtAnAltar()
        {
            var (m, p, altar) = AltarBase();
            var dusk = m.Players[1];
            m.TryTrain(altar, "hero_vorak");
            TestUtil.Run(m, m.Rules.RtsHeroTrainTime + 1f);
            var vorak = p.RtsHeroes[0];
            m.AddXp(vorak, m.Rules.Experience.Cumulative[2]);
            Assert.Equal(3, vorak.Level);
            int gold = p.Gold, duskGold = dusk.Gold, supply = p.SupplyUsed;
            var killer = m.CreateUnit(Def("rts_al_thrall"), dusk.Team, m.Grid.NearestWalkable(vorak.Position + new Vector2(2, 0)), 0f, dusk);
            m.Step();
            m.DealDamage(new DamageInfo { Source = killer, Target = vorak, Amount = 99999f, Type = DamageType.Pure });
            Assert.True(vorak.Dead);
            Assert.Equal(gold, p.Gold);           // no MOBA death penalty
            Assert.Equal(duskGold, dusk.Gold);    // and no hero bounty
            m.KillUnit(killer, null);
            TestUtil.Run(m, 90f);
            Assert.True(vorak.Dead, "RTS heroes do not respawn on their own");
            Assert.Equal(supply - m.Rules.RtsHeroSupply, p.SupplyUsed);

            gold = p.Gold;
            Assert.True(m.TryTrain(altar, "hero_vorak"));
            Assert.Equal(gold - m.ReviveGold(vorak), p.Gold);
            Assert.Equal(m.Rules.RtsReviveGold + m.Rules.RtsReviveGoldPerLevel * 3, gold - p.Gold);
            Assert.Equal(1, m.HeroCount(p)); // a revival is not a new hero
            TestUtil.Run(m, m.ReviveTime(vorak) + 1f);
            Assert.True(vorak.IsAlive);
            Assert.Equal(3, vorak.Level);
            Assert.Equal(vorak.Stats.MaxHp, vorak.Hp, 1);
            Assert.True(Vector2.Distance(vorak.Position, altar.Position) < 6f);
        }

        [Fact]
        public void HeroesGrowFromKillsNearThem()
        {
            var (m, p, altar) = AltarBase();
            var dusk = m.Players[1];
            m.TryTrain(altar, "hero_vorak");
            m.TryTrain(altar, "hero_ilyra");
            TestUtil.Run(m, 2 * m.Rules.RtsHeroTrainTime + 2f);
            var near = p.RtsHeroes[0];
            var far = p.RtsHeroes[1];
            far.Position = m.Grid.NearestWalkable(new Vector2(20, 60));
            var at = m.Grid.NearestWalkable(near.Position + new Vector2(3, 0));
            var footman = m.CreateUnit(Def("rts_al_thrall"), dusk.Team, at, 0f, dusk);
            var obelisk = m.CreateUnit(Def("rts_al_obelisk"), dusk.Team, m.Grid.NearestWalkable(near.Position + new Vector2(0, 4)), 0f, dusk);
            m.Step();
            int xp = near.Xp;
            m.KillUnit(footman, null);
            Assert.Equal(xp + m.Rules.RtsXpPerSupply * Def("rts_al_thrall").SupplyCost, near.Xp);
            m.KillUnit(obelisk, null);
            Assert.Equal(xp + m.Rules.RtsXpPerSupply * 2 + m.Rules.RtsBuildingXp, near.Xp);
            Assert.Equal(0, far.Xp);
        }
}
}
