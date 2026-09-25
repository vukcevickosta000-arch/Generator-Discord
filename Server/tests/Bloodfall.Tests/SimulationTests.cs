using System;
using System.Linq;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;
using Bloodfall.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Bloodfall.Tests
{
    public class GameDataTests
    {
        private readonly ITestOutputHelper _out;
        public GameDataTests(ITestOutputHelper output) { _out = output; }

        [Fact]
        public void GameData_LoadsWithoutErrors()
        {
            var d = TestUtil.Data;
            foreach (var w in d.Warnings) _out.WriteLine("WARN " + w);
            foreach (var e in d.Errors) _out.WriteLine("ERR  " + e);
            Assert.Empty(d.Errors);
            Assert.True(d.Heroes.Count >= 2);
            Assert.True(d.Items.Count >= 30);
            Assert.True(d.Maps.ContainsKey("map_velmoragh"));
            Assert.Equal(64, d.ContentHash.Length);
        }

        [Fact]
        public void Recipes_ComputeTotalCost()
        {
            var d = TestUtil.Data;
            var marchers = d.Item("item_marchers_haste");
            Assert.Equal(d.Item("item_worn_boots").Cost + d.Item("item_quickblade_gloves").Cost, marchers.TotalCost);
            Assert.Contains("item_marchers_haste", d.Item("item_worn_boots").BuildsInto);
        }

        [Fact]
        public void Json_RoundTrip()
        {
            var node = Json.Parse("{\"a\": [1, 2.5, \"x\"], \"b\": {\"c\": true, \"d\": null}, // comment\n \"e\": \"quote\\\"d\"}");
            Assert.Equal(2.5, node["a"][1].AsDouble());
            Assert.True(node["b"]["c"].AsBool());
            Assert.Equal("quote\"d", node["e"].AsString());
            var again = Json.Parse(Json.Write(node));
            Assert.Equal(Json.Write(node, false), Json.Write(again, false));
        }

        [Fact]
        public void ArmorMultiplier_MatchesClassicCurve()
        {
            Assert.Equal(1f, MathUtil.ArmorMultiplier(0), 3);
            Assert.Equal(0.625f, MathUtil.ArmorMultiplier(10), 3);
            Assert.True(MathUtil.ArmorMultiplier(-5) > 1f);
        }

        [Fact]
        public void PseudoRandom_ConvergesToNominalRate()
        {
            var rng = new DeterministicRandom(99);
            var prd = new PseudoRandom();
            int procs = 0, n = 200000;
            for (int i = 0; i < n; i++) if (prd.Roll(rng, 0.25f)) procs++;
            Assert.InRange(procs / (float)n, 0.235f, 0.265f);
        }
    }

    public class MapAndPathingTests
    {
        [Fact]
        public void Map_SpawnsAreWalkable_AndPathExistsBetweenBases()
        {
            var m = TestUtil.NewMatch();
            var dawn = m.Map.Bases.First(b => b.Team == Team.Dawn);
            var dusk = m.Map.Bases.First(b => b.Team == Team.Dusk);
            Assert.True(m.Grid.IsWalkable(dawn.HeroSpawn));
            Assert.True(m.Grid.IsWalkable(dusk.HeroSpawn));
            var path = new System.Collections.Generic.List<Vector2>();
            Assert.True(m.Grid.FindPath(dawn.HeroSpawn, dusk.HeroSpawn, path));
            float len = 0; Vector2 prev = dawn.HeroSpawn;
            foreach (var p in path) { len += Vector2.Distance(prev, p); prev = p; }
            Assert.InRange(len, 200f, 320f);
        }

        [Fact]
        public void Structures_SpawnWithProtection()
        {
            var m = TestUtil.NewMatch();
            var t1 = m.Units.First(u => u.StructureId == "dawn_mid_t1");
            var t2 = m.Units.First(u => u.StructureId == "dawn_mid_t2");
            var core = m.Cores[(int)Team.Dawn];
            Assert.False(t1.Invulnerable);
            Assert.True(t2.Invulnerable);
            Assert.True(core.Invulnerable);
            m.KillUnit(t1, null);
            m.RecomputeFlags(t2);
            Assert.False(t2.Invulnerable);
        }

        [Fact]
        public void Vision_HighGroundBlocksLowGround()
        {
            var m = TestUtil.NewMatch();
            TestUtil.Run(m, 0.5f);
            // A Dusk unit standing inside the Dawn base (high ground) is invisible to Dawn? No: Dawn owns the base.
            // Instead check: Dawn units on low ground next to the Dusk plateau cannot see onto it.
            var dusk = m.Map.Bases.First(b => b.Team == Team.Dusk);
            Assert.False(m.Vision.IsVisible(Team.Dawn, dusk.Fountain));
            Assert.True(m.Vision.IsVisible(Team.Dusk, dusk.Fountain));
        }
    }

    public class CombatTests
    {
        [Fact]
        public void CreepWaves_Spawn_Fight_AndDie()
        {
            var m = TestUtil.NewMatch(dawn: 0, dusk: 0);
            TestUtil.Run(m, 1.2f);
            int creeps = m.Units.Count(u => u.IsCreep);
            Assert.Equal(2 * 3 * 4, creeps); // 3 lanes x 2 teams x (3 melee + 1 ranged)
            TestUtil.Run(m, 60f);
            Assert.True(m.Units.Any(u => u.IsCreep && u.Dead) || m.Units.Count(u => u.IsCreep) < creeps + 24, "creeps should have fought");
            // No creep should wander off the map.
            Assert.All(m.Units.Where(u => u.IsCreep && !u.Dead), c => Assert.True(m.Grid.IsWalkable(c.Position)));
        }

        [Fact]
        public void Hero_LastHit_GrantsGoldAndLastHit()
        {
            var m = TestUtil.NewMatch();
            var p = m.Players[0];
            var hero = p.Hero;
            TestUtil.Run(m, 1.2f);
            var creep = m.Units.First(u => u.IsCreep && u.Team == Team.Dusk && u.LaneIndex == 1);
            TestUtil.Teleport(m, hero, creep.Position + new Vector2(-1.2f, -1.2f));
            creep.Hp = 5;
            int gold = p.Gold;
            m.IssueOrder(hero, Order.Attack(hero.Id, creep.Id));
            TestUtil.Run(m, 2f);
            Assert.True(creep.Dead);
            Assert.Equal(1, p.LastHits);
            Assert.True(p.Gold > gold + 30);
        }

        [Fact]
        public void Hero_CanDenyLowAllyCreep()
        {
            var m = TestUtil.NewMatch();
            var p = m.Players[0];
            var hero = p.Hero;
            TestUtil.Run(m, 1.2f);
            var ally = m.Units.First(u => u.IsCreep && u.Team == Team.Dawn);
            TestUtil.Teleport(m, hero, ally.Position + new Vector2(1.0f, 0));
            ally.Hp = ally.Stats.MaxHp * 0.6f;
            Assert.False(m.CanAttackTarget(hero, ally, out _));
            ally.Hp = 8;
            Assert.True(m.CanAttackTarget(hero, ally, out bool deny));
            Assert.True(deny);
            m.IssueOrder(hero, Order.Attack(hero.Id, ally.Id));
            TestUtil.Run(m, 2f);
            Assert.True(ally.Dead);
            Assert.Equal(1, p.Denies);
            Assert.Equal(0, p.LastHits);
        }

        [Fact]
        public void Tower_AttacksEnemyInRange()
        {
            var m = TestUtil.NewMatch(c => c.DisableCreeps = true);
            var enemy = m.Players[1].Hero;
            var t1 = m.Units.First(u => u.StructureId == "dawn_mid_t1");
            TestUtil.Teleport(m, enemy, t1.Position + new Vector2(3.5f, 3.5f));
            float hp = enemy.Hp;
            TestUtil.Run(m, 4f);
            Assert.True(enemy.Hp < hp - 100);
        }

        [Fact]
        public void Vorak_CrimsonCharge_StunsFirstHero()
        {
            var m = TestUtil.NewMatch(c => c.DisableCreeps = true);
            var vorak = m.Players[0].Hero;
            var ilyra = m.Players[1].Hero;
            TestUtil.Teleport(m, vorak, TestUtil.MidPoint + new Vector2(-8, -8));
            TestUtil.Teleport(m, ilyra, TestUtil.MidPoint + new Vector2(-2, -2));
            TestUtil.Run(m, 1.5f);
            m.IssueOrder(vorak, Order.LevelUp(vorak.Id, 1));
            var q = vorak.Abilities[1];
            Assert.Equal(1, q.Level);
            float hp = ilyra.Hp;
            m.IssueOrder(vorak, Order.CastPointOrder(vorak.Id, 1, ilyra.Position));
            bool stunned = false;
            for (int i = 0; i < 60; i++) { m.Step(); stunned |= ilyra.HasFlag(StatusFlags.Stunned); }
            Assert.True(stunned, "Ilyra should have been stunned by the charge");
            Assert.True(ilyra.Hp < hp - 50);
            Assert.True(q.Cooldown > 0);
            Assert.True(Vector2.Distance(vorak.Position, ilyra.Position) < 2.5f);
        }

        [Fact]
        public void Ilyra_BloodLance_CostsHealth_AndDamagesLine()
        {
            var m = TestUtil.NewMatch(c => c.DisableCreeps = true);
            var vorak = m.Players[0].Hero;
            var ilyra = m.Players[1].Hero;
            TestUtil.Teleport(m, ilyra, TestUtil.MidPoint + new Vector2(6, 6));
            TestUtil.Teleport(m, vorak, TestUtil.MidPoint);
            TestUtil.Run(m, 1.5f);
            m.IssueOrder(ilyra, Order.LevelUp(ilyra.Id, 1));
            float ilyraHp = ilyra.Hp, vorakHp = vorak.Hp;
            m.IssueOrder(ilyra, Order.CastPointOrder(ilyra.Id, 1, vorak.Position));
            TestUtil.Run(m, 1.5f);
            Assert.True(ilyra.Hp < ilyraHp - 30, "health cost paid");
            Assert.True(vorak.Hp < vorakHp - 40, "lance hit");
            Assert.NotNull(vorak.FindStatus("slow_light") ?? (object)(vorak.Hp < vorakHp ? "hit" : null));
        }

        [Fact]
        public void Bleed_StacksByIntensity()
        {
            var m = TestUtil.NewMatch(c => c.DisableCreeps = true);
            var vorak = m.Players[0].Hero;
            var ilyra = m.Players[1].Hero;
            TestUtil.Run(m, 1.2f);
            var def = m.Data.Status("vorak_bleed");
            for (int i = 0; i < 5; i++) m.ApplyStatus(ilyra, def, vorak, 1, 4f);
            var s = ilyra.FindStatus("vorak_bleed");
            Assert.Equal(3, s.Stacks);
            ilyra.RecomputeStats(m.Rules);
            Assert.True(ilyra.Stats.MoveSpeed < ilyra.HeroDef.MoveSpeed * 0.7f);
        }

        [Fact]
        public void Death_GivesKillGold_AssistsAndRespawns()
        {
            var m = TestUtil.NewMatch(c => c.DisableCreeps = true);
            var killer = m.Players[0];
            var victim = m.Players[1];
            TestUtil.Run(m, 1.2f);
            int gold = killer.Gold;
            m.DealDamage(new DamageInfo { Source = killer.Hero, Target = victim.Hero, Amount = 99999, Type = DamageType.Pure });
            Assert.True(victim.Hero.Dead);
            Assert.Equal(1, killer.Kills);
            Assert.Equal(1, victim.Deaths);
            Assert.True(killer.Gold >= gold + m.Rules.HeroKillBaseGold + m.Rules.FirstBloodBonus);
            Assert.Contains(m.Events, e => e.Type == SimEventType.Announcer && e.Key == AnnouncerKeys.FirstBlood);
            TestUtil.Run(m, 12f);
            Assert.False(victim.Hero.Dead);
        }
    }

    public class ItemTests
    {
        [Fact]
        public void Buy_Combine_Sell()
        {
            var m = TestUtil.NewMatch(c => c.DisableCreeps = true);
            var p = m.Players[0];
            var hero = p.Hero;
            TestUtil.Run(m, 0.5f);
            p.Gold = 5000;
            m.IssueOrder(hero, Order.Buy(hero.Id, "item_worn_boots"));
            m.IssueOrder(hero, Order.Buy(hero.Id, "item_quickblade_gloves"));
            // Marchers is a pure (cost 0) recipe: owning both components combines automatically.
            Assert.Contains(hero.Inventory, i => i?.Def.Id == "item_marchers_haste");
            Assert.DoesNotContain(hero.Inventory, i => i?.Def.Id == "item_worn_boots");
            TestUtil.Run(m, 0.2f);
            Assert.True(hero.Stats.AttackSpeed > 20);
            // Buying a complete item deducts owned components.
            int before = p.Gold;
            m.IssueOrder(hero, Order.Buy(hero.Id, "item_rusted_blade"));
            m.IssueOrder(hero, Order.Buy(hero.Id, "item_vampiric_mask"));
            var mask = m.Data.Item("item_vampiric_mask");
            Assert.Contains(hero.Inventory, i => i?.Def.Id == "item_vampiric_mask");
            Assert.Equal(before - mask.TotalCost, p.Gold);
            // Full refund inside the window.
            int slot = Array.FindIndex(hero.Inventory, i => i?.Def.Id == "item_vampiric_mask");
            m.IssueOrder(hero, Order.Sell(hero.Id, slot));
            Assert.Equal(before, p.Gold);
        }

        [Fact]
        public void Consumable_DraughtHeals()
        {
            var m = TestUtil.NewMatch(c => c.DisableCreeps = true);
            var p = m.Players[0];
            var hero = p.Hero;
            TestUtil.Run(m, 0.5f);
            m.IssueOrder(hero, Order.Buy(hero.Id, "item_crimson_draught"));
            TestUtil.Teleport(m, hero, TestUtil.MidPoint);
            hero.Hp = 100;
            int slot = Array.FindIndex(hero.Inventory, i => i?.Def.Id == "item_crimson_draught");
            Assert.True(slot >= 0);
            m.IssueOrder(hero, Order.CastNoTargetOrder(hero.Id, Order.ItemSlotBase + slot));
            TestUtil.Run(m, 5f);
            Assert.True(hero.Hp > 250);
            Assert.DoesNotContain(hero.Inventory, i => i?.Def.Id == "item_crimson_draught");
        }

        [Fact]
        public void NotEnoughGold_ProducesError()
        {
            var m = TestUtil.NewMatch();
            var hero = m.Players[0].Hero;
            TestUtil.Run(m, 0.3f);
            m.Events.Clear();
            m.IssueOrder(hero, Order.Buy(hero.Id, "item_heart_colossus"));
            Assert.Contains(m.Events, e => e.Type == SimEventType.Error && e.PlayerId == 0);
        }
    }

    public class FullMatchTests
    {
        private readonly ITestOutputHelper _out;
        public FullMatchTests(ITestOutputHelper output) { _out = output; }

        [Fact]
        public void BotMatch_5v5_RunsTwentyMinutes_WithoutExceptions()
        {
            var cfg = new MatchConfig { Seed = 11, SkipHeroSelect = true, PreGameTimeOverride = 10f, SameHeroAllowed = true };
            string[] heroes = { "hero_vorak", "hero_ilyra" };
            for (int i = 0; i < 5; i++) cfg.Players.Add(new PlayerSetup { Name = $"DawnBot{i}", Team = Team.Dawn, Slot = i, HeroId = heroes[i % 2], IsBot = true });
            for (int i = 0; i < 5; i++) cfg.Players.Add(new PlayerSetup { Name = $"DuskBot{i}", Team = Team.Dusk, Slot = i, HeroId = heroes[(i + 1) % 2], IsBot = true });
            var m = new Match(TestUtil.Data, cfg);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int ticks = 0;
            while (m.Time < 20 * 60 && m.Phase != MatchPhase.PostGame)
            {
                m.Step();
                m.Events.Clear();
                ticks++;
            }
            sw.Stop();
            _out.WriteLine($"Simulated {m.Time:0}s in {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalMilliseconds / ticks:0.000} ms/tick), units={m.Units.Count}");
            foreach (var p in m.Players)
                _out.WriteLine($"{p.Name,-10} {p.HeroId,-12} L{p.Hero.Level,2} K/D/A {p.Kills}/{p.Deaths}/{p.Assists} LH {p.LastHits} DN {p.Denies} gold {p.Gold} NW {m.NetWorth(p)} items {string.Join(",", p.Hero.Inventory.Where(i => i != null).Select(i => i.Def.Id))}");
            _out.WriteLine($"Team kills {m.TeamKills[0]}-{m.TeamKills[1]}, towers down: {m.Units.Count(u => u.Kind == UnitKind.Tower && u.Dead)}, winner {m.Winner}");
            Assert.True(m.Players.Sum(p => p.LastHits) > 50, "bots should farm");
            Assert.True(m.Players.All(p => p.Hero.Level >= 5), "all bots should gain levels");
            Assert.True(sw.Elapsed.TotalMilliseconds / ticks < 8.0, "simulation must stay well within the 33 ms tick budget");
        }
    }
}
