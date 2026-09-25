using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bloodfall.Data;
using Bloodfall.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Bloodfall.Tests
{
    /// <summary>Mechanics of the concept heroes (Nyxara, Malgrave, Ardyn, Fenrax, Morwen, Thael) and the engine features they use.</summary>
    public class HeroKitTests
    {
        private readonly ITestOutputHelper _out;
        public HeroKitTests(ITestOutputHelper output) { _out = output; }

        private const int Q = 1, W = 2, E = 3, R = 4;

        /// <summary>A 1v1 (or NvM) match in the Playing phase with both heroes near the map centre and no creeps.</summary>
        private static Match Duel(string dawnHero, string duskHero, out Unit a, out Unit b, float distance = 3f, int dawn = 1, int dusk = 1)
        {
            var m = TestUtil.NewMatch(c => { c.DisableCreeps = true; c.DisableNeutrals = true; }, dawn, dusk, dawnHero, duskHero);
            TestUtil.Run(m, 1.2f);
            a = m.Players[0].Hero;
            b = m.Players[dawn].Hero;
            TestUtil.Teleport(m, a, TestUtil.MidPoint);
            TestUtil.Teleport(m, b, TestUtil.MidPoint + new Vector2(distance, 0));
            m.Vision.Update(force: true);   // unit-target casts need the target to be visible after the teleport
            return m;
        }

        private static void Learn(Match m, Unit u, int slot, int level)
        {
            u.Abilities[slot].Level = level;
            u.RecomputeStats(m.Rules);
            u.Mana = u.Stats.MaxMana;
        }

        private static void Pacify(Match m, params Unit[] units)
        {
            foreach (var u in units) m.ApplyStatus(u, "disarm", null, 1, 60f);
        }

        private static IEnumerable<Unit> Alive(Match m, string defId) => m.Units.Where(u => u.DefId == defId && u.IsAlive);

        private static List<SimEvent> RunCollect(Match m, float seconds)
        {
            var events = new List<SimEvent>();
            int ticks = (int)(seconds * m.Rules.TickRate);
            for (int i = 0; i < ticks; i++) { m.Events.Clear(); m.Step(); events.AddRange(m.Events); }
            return events;
        }

        // ============================================================== Nyxara

        [Fact]
        public void Nyxara_VelvetDark_ChargesOnlyWhileUnseen_AndCrits()
        {
            var m = Duel("hero_nyxara", "hero_ilyra", out var nyx, out var ilyra, distance: 2f);
            TestUtil.Run(m, 4f);
            Assert.Null(nyx.FindStatus("nyxara_velvet"));   // Ilyra stands next to her: seen the whole time

            // Hidden in her own base, far from any enemy vision.
            TestUtil.Teleport(m, nyx, m.Map.Bases.First(b => b.Team == Team.Dawn).HeroSpawn);
            TestUtil.Run(m, 3.6f);
            Assert.NotNull(nyx.FindStatus("nyxara_velvet"));

            // Velvet Dark outlasts being seen for a few seconds.
            TestUtil.Teleport(m, nyx, ilyra.Position - new Vector2(1.2f, 0));
            Pacify(m, ilyra);
            m.IssueOrder(nyx, Order.Attack(nyx.Id, ilyra.Id));
            var events = RunCollect(m, 1.5f);
            Assert.Contains(events, e => e.Type == SimEventType.Crit && e.OtherId == nyx.Id);
            Assert.Null(nyx.FindStatus("nyxara_velvet"));
        }

        [Fact]
        public void Nyxara_CrimsonMark_HealsHerWhenAlliesAttack()
        {
            var m = Duel("hero_nyxara", "hero_ilyra", out var nyx, out var ilyra, dawn: 2);
            var ally = m.Players[1].Hero;
            TestUtil.Teleport(m, ally, ilyra.Position + new Vector2(1.2f, 0));
            Learn(m, nyx, W, 4);
            Pacify(m, nyx, ilyra);
            m.IssueOrder(nyx, Order.CastUnitOrder(nyx.Id, W, ilyra.Id));
            TestUtil.Run(m, 0.6f);
            Assert.NotNull(ilyra.FindStatus("nyxara_crimson_mark"));

            nyx.Hp = nyx.Stats.MaxHp * 0.5f;
            float before = nyx.Hp;
            m.IssueOrder(ally, Order.Attack(ally.Id, ilyra.Id));
            TestUtil.Run(m, 3f);
            Assert.True(nyx.Hp > before + 50, $"marked target should heal Nyxara when her ally attacks it ({before} -> {nyx.Hp})");
        }

        [Fact]
        public void Nyxara_MidnightSentence_ExecutesOnlyMarkedTargets()
        {
            foreach (bool marked in new[] { true, false })
            {
                var m = Duel("hero_nyxara", "hero_ilyra", out var nyx, out var ilyra, distance: 5f);
                TestUtil.LevelTo(m, nyx, 6);
                Learn(m, nyx, R, 1);
                Pacify(m, nyx, ilyra);
                if (marked) m.ApplyStatus(ilyra, "nyxara_crimson_mark", nyx, 1, 10f);
                ilyra.Hp = ilyra.Stats.MaxHp * 0.45f;   // 150 pure damage leaves her near 17%
                m.IssueOrder(nyx, Order.CastUnitOrder(nyx.Id, R, ilyra.Id));
                TestUtil.Run(m, 0.8f);
                Assert.True(Vector2.Distance(nyx.Position, ilyra.Position) < 1.6f, "teleports behind the target");
                Assert.Equal(marked, ilyra.Dead);
            }
        }

        [Fact]
        public void Nyxara_Veil_IsInvisible_AndBreaksOnAttack()
        {
            var m = Duel("hero_nyxara", "hero_ilyra", out var nyx, out var ilyra, distance: 6f);
            Learn(m, nyx, E, 1);
            Pacify(m, ilyra);
            m.IssueOrder(nyx, Order.CastNoTargetOrder(nyx.Id, E));
            TestUtil.Run(m, 0.3f);
            Assert.True(nyx.IsInvisible);
            TestUtil.Run(m, 0.2f);
            Assert.False(m.IsVisibleTo(nyx, Team.Dusk));
            m.IssueOrder(nyx, Order.Attack(nyx.Id, ilyra.Id));
            TestUtil.Run(m, 2.5f);
            Assert.False(nyx.IsInvisible);
            Assert.Null(nyx.FindStatus("nyxara_veil"));
        }

        // ============================================================== Malgrave

        [Fact]
        public void Malgrave_RaiseTheFallen_RaisesTwoPlusOnePerCorpse_ScaledByLevel()
        {
            var m = Duel("hero_malgrave", "hero_ilyra", out var mal, out _, distance: 30f);
            Learn(m, mal, Q, 3);
            m.Corpses.Add(new Corpse { Position = mal.Position + new Vector2(3, 0), UnitId = "x", Expires = m.Time + 30 });
            m.Corpses.Add(new Corpse { Position = mal.Position + new Vector2(0, 3), UnitId = "x", Expires = m.Time + 30 });
            m.Corpses.Add(new Corpse { Position = mal.Position + new Vector2(-3, 0), UnitId = "x", Expires = m.Time + 30 });
            m.IssueOrder(mal, Order.CastNoTargetOrder(mal.Id, Q));
            TestUtil.Run(m, 1f);
            var skeletons = Alive(m, "malgrave_legionnaire").ToList();
            Assert.Equal(4, skeletons.Count);
            Assert.Equal(1, m.Corpses.Count(c => !c.Consumed));
            Assert.All(skeletons, s => Assert.Equal(320 + 160, s.Stats.MaxHp, 1));
            Assert.All(skeletons, s => Assert.Equal(s.Stats.MaxHp, s.Hp, 1));
        }

        [Fact]
        public void Malgrave_RaiseTheFallen_WithoutCorpses_StillRaisesTwo()
        {
            var m = Duel("hero_malgrave", "hero_ilyra", out var mal, out _, distance: 30f);
            Learn(m, mal, Q, 1);
            m.IssueOrder(mal, Order.CastNoTargetOrder(mal.Id, Q));
            var events = RunCollect(m, 1f);
            Assert.Equal(2, Alive(m, "malgrave_legionnaire").Count());
            Assert.DoesNotContain(events, e => e.Type == SimEventType.Error);
        }

        [Fact]
        public void Malgrave_BoneCage_BlocksARing_ThenCrumbles()
        {
            var m = Duel("hero_malgrave", "hero_ilyra", out var mal, out var ilyra, distance: 6f);
            Learn(m, mal, W, 1);
            Pacify(m, ilyra);
            var center = ilyra.Position;
            m.IssueOrder(mal, Order.CastPointOrder(mal.Id, W, center));
            TestUtil.Run(m, 0.6f);
            int blocked = 0;
            for (int i = 0; i < 16; i++)
                if (!m.Grid.IsWalkable(center + new Vector2((float)System.Math.Cos(i * 0.3927), (float)System.Math.Sin(i * 0.3927)) * 3.2f)) blocked++;
            Assert.True(blocked >= 12, $"ring should block movement ({blocked}/16 samples blocked)");
            Assert.NotNull(ilyra.FindStatus("slow_light"));
            TestUtil.Run(m, 3.5f);
            blocked = 0;
            for (int i = 0; i < 16; i++)
                if (!m.Grid.IsWalkable(center + new Vector2((float)System.Math.Cos(i * 0.3927), (float)System.Math.Sin(i * 0.3927)) * 3.2f)) blocked++;
            Assert.True(blocked <= 2, "ring cells are released when the cage ends");
        }

        [Fact]
        public void Malgrave_GraveChill_OnlyHitsTheCone()
        {
            var m = Duel("hero_malgrave", "hero_ilyra", out var mal, out var front, distance: 4f, dusk: 2);
            var behind = m.Players[2].Hero;
            TestUtil.Teleport(m, behind, mal.Position - new Vector2(4, 0));
            Learn(m, mal, E, 1);
            Pacify(m, mal, front, behind);
            float f = front.Hp, b = behind.Hp;
            m.IssueOrder(mal, Order.CastPointOrder(mal.Id, E, front.Position));
            TestUtil.Run(m, 0.8f);
            Assert.True(front.Hp < f - 40);
            Assert.NotNull(front.FindStatus("malgrave_grave_chill"));
            Assert.Equal(b, behind.Hp, 1);
        }

        [Fact]
        public void Malgrave_LegionOfThePale_RaisesSixGuards()
        {
            var m = Duel("hero_malgrave", "hero_ilyra", out var mal, out var ilyra, distance: 40f);
            TestUtil.LevelTo(m, mal, 6);
            Learn(m, mal, R, 1);
            var point = mal.Position + new Vector2(8, 0);
            m.IssueOrder(mal, Order.CastPointOrder(mal.Id, R, point));
            TestUtil.Run(m, 5.5f);
            var guards = Alive(m, "malgrave_pale_revenant").ToList();
            Assert.Equal(6, guards.Count);
            // Guards hold their ground: they do not wander off after Malgrave.
            m.IssueOrder(mal, Order.MoveTo(mal.Id, mal.Position - new Vector2(20, 0)));
            TestUtil.Run(m, 6f);
            Assert.All(guards.Where(g => g.IsAlive), g => Assert.True(Vector2.Distance(g.Position, point) < 10f));
        }

        [Fact]
        public void Malgrave_Ossuary_GathersShards_AndSpendsThemOnCast()
        {
            var m = Duel("hero_malgrave", "hero_ilyra", out var mal, out _, distance: 40f);
            Learn(m, mal, E, 1);
            var neutral = m.Data.Units.Values.First(u => u.Kind == UnitKind.Neutral);
            for (int i = 0; i < 3; i++)
            {
                var victim = m.CreateUnit(neutral, Team.Neutral, mal.Position + new Vector2(3, i));
                TestUtil.Run(m, 0.1f);
                m.KillUnit(victim, null);
            }
            TestUtil.Run(m, 0.2f);
            Assert.Equal(3, mal.FindStatus("malgrave_bone_shard")?.Stacks ?? 0);
            mal.RecomputeStats(m.Rules);
            float withShards = mal.Stats.SpellAmp;
            m.RemoveStatusById(mal, "malgrave_bone_shard");
            mal.RecomputeStats(m.Rules);
            Assert.Equal(0.12f, withShards - mal.Stats.SpellAmp, 3);
            m.ApplyStatus(mal, "malgrave_bone_shard", mal, 1, 40f, 3);
            m.IssueOrder(mal, Order.CastPointOrder(mal.Id, E, mal.Position + new Vector2(5, 0)));
            TestUtil.Run(m, 0.8f);
            Assert.Null(mal.FindStatus("malgrave_bone_shard"));
        }

        // ============================================================== Ardyn

        [Fact]
        public void Ardyn_Oathkeeper_GrantsArmor_DoubledWhenWounded()
        {
            var m = Duel("hero_ardyn", "hero_ilyra", out var ardyn, out _, distance: 30f);
            TestUtil.Run(m, 1f);
            Assert.NotNull(ardyn.FindStatus("ardyn_oathkeeper"));
            Assert.Null(ardyn.FindStatus("ardyn_oath_fervor"));
            ardyn.RecomputeStats(m.Rules);
            float armor = ardyn.Stats.Armor;
            ardyn.Hp = ardyn.Stats.MaxHp * 0.3f;
            TestUtil.Run(m, 1f);
            Assert.NotNull(ardyn.FindStatus("ardyn_oath_fervor"));
            ardyn.RecomputeStats(m.Rules);
            Assert.Equal(armor + 2f, ardyn.Stats.Armor, 2);
        }

        [Fact]
        public void Ardyn_SameAuraFromTwoArdyns_DoesNotStack()
        {
            var m = Duel("hero_ardyn", "hero_ilyra", out var a, out _, distance: 30f, dawn: 2);
            var b = m.Players[1].Hero;
            TestUtil.Teleport(m, b, a.Position + new Vector2(2, 0));
            TestUtil.Run(m, 1f);
            Assert.Single(a.Statuses, s => s.Def.Id == "ardyn_oathkeeper");
        }

        [Fact]
        public void Ardyn_AegisOfDawn_AbsorbsDamage_ThenBursts()
        {
            var m = Duel("hero_ardyn", "hero_ilyra", out var ardyn, out var ilyra, distance: 3f);
            Learn(m, ardyn, Q, 1);
            Pacify(m, ardyn, ilyra);
            m.IssueOrder(ardyn, Order.CastUnitOrder(ardyn.Id, Q, ardyn.Id));
            TestUtil.Run(m, 0.5f);
            Assert.NotNull(ardyn.FindStatus("ardyn_aegis"));
            float hp = ardyn.Hp;
            m.DealDamage(new DamageInfo { Source = ilyra, Target = ardyn, Amount = 80, Type = DamageType.Pure });
            Assert.Equal(hp, ardyn.Hp, 1);
            Assert.Equal(40f, ardyn.FindStatus("ardyn_aegis").ShieldRemaining, 1);
            float ilyraHp = ilyra.Hp;
            TestUtil.Run(m, 6.2f);
            Assert.Null(ardyn.FindStatus("ardyn_aegis"));
            Assert.True(ilyra.Hp < ilyraHp - 30, "shield bursts on expiry");
        }

        [Fact]
        public void Ardyn_JudgmentStrike_NextAttackStuns()
        {
            var m = Duel("hero_ardyn", "hero_ilyra", out var ardyn, out var ilyra, distance: 1.5f);
            Learn(m, ardyn, E, 1);
            Pacify(m, ilyra);
            m.IssueOrder(ardyn, Order.CastNoTargetOrder(ardyn.Id, E));
            TestUtil.Run(m, 0.2f);
            Assert.NotNull(ardyn.FindStatus("ardyn_judgment"));
            m.IssueOrder(ardyn, Order.Attack(ardyn.Id, ilyra.Id));
            bool stunned = false;
            for (int i = 0; i < 60; i++) { m.Step(); stunned |= ilyra.HasFlag(StatusFlags.Stunned); }
            Assert.True(stunned);
            Assert.Null(ardyn.FindStatus("ardyn_judgment"));
        }

        [Fact]
        public void Ardyn_Consecrate_HealsAllies_AndBurnsEnemies()
        {
            var m = Duel("hero_ardyn", "hero_ilyra", out var ardyn, out var ilyra, distance: 2.5f);
            Learn(m, ardyn, W, 1);
            Pacify(m, ardyn, ilyra);
            ardyn.Hp = ardyn.Stats.MaxHp * 0.5f;
            float a = ardyn.Hp, i = ilyra.Hp;
            m.IssueOrder(ardyn, Order.CastNoTargetOrder(ardyn.Id, W));
            TestUtil.Run(m, 3f);
            Assert.True(ardyn.Hp > a + 40, "allies heal in the consecrated ground");
            Assert.True(ilyra.Hp < i - 30, "enemies burn in it");
        }

        [Fact]
        public void Ardyn_RadiantCrusade_ChargesThroughEnemies_AndBlindsThem()
        {
            var m = Duel("hero_ardyn", "hero_ilyra", out var ardyn, out var ilyra, distance: 5f);
            TestUtil.LevelTo(m, ardyn, 6);
            Learn(m, ardyn, R, 1);
            Pacify(m, ilyra);
            m.ApplyStatus(ardyn, "slow_heavy", ilyra, 1, 10f);
            m.IssueOrder(ardyn, Order.CastPointOrder(ardyn.Id, R, ardyn.Position + new Vector2(10, 0)));
            TestUtil.Run(m, 1.2f);
            Assert.Null(ardyn.FindStatus("slow_heavy"));           // cleansed
            Assert.NotNull(ardyn.FindStatus("ardyn_crusade"));
            Assert.NotNull(ilyra.FindStatus("ardyn_blinded"));
            Assert.True(ardyn.Position.X > TestUtil.MidPoint.X + 7f, "charged past Ilyra");
        }

        // ============================================================== Fenrax

        [Fact]
        public void Fenrax_NightUnleashed_Transforms_ForcesNight_AndCleaves()
        {
            var m = Duel("hero_fenrax", "hero_ilyra", out var fen, out var target, distance: 1.5f, dusk: 2);
            var bystander = m.Players[2].Hero;
            TestUtil.Teleport(m, bystander, target.Position + new Vector2(0.9f, 0.6f));
            TestUtil.LevelTo(m, fen, 6);
            Learn(m, fen, R, 1);
            Pacify(m, target, bystander);
            Assert.False(m.IsNight);
            float maxHp = fen.Stats.MaxHp;
            m.IssueOrder(fen, Order.CastNoTargetOrder(fen.Id, R));
            TestUtil.Run(m, 0.7f);
            Assert.True(m.IsNight);
            Assert.Equal("hero_fenrax_moonfang", fen.ModelKey);
            fen.RecomputeStats(m.Rules);
            Assert.InRange(fen.Stats.MaxHp, maxHp * 1.3f - 1f, maxHp * 1.3f + 1f);
            TestUtil.Run(m, 0.6f);
            Assert.NotNull(fen.FindStatus("fenrax_lunar_hunger"));

            float by = bystander.Hp;
            m.IssueOrder(fen, Order.Attack(fen.Id, target.Id));
            TestUtil.Run(m, 2f);
            Assert.True(bystander.Hp < by - 10, "cleave hits units next to the target");

            TestUtil.Run(m, 6f);
            Assert.False(m.IsNight);                                   // forced night lasts 8 s
            TestUtil.Run(m, 1f);
            Assert.Null(fen.FindStatus("fenrax_lunar_hunger"));
            m.Dispel(fen, DispelType.Strong, false, true);
            Assert.NotNull(fen.FindStatus("fenrax_moonfang"));        // cannot be dispelled
        }

        [Fact]
        public void Fenrax_RendingClaws_StacksArmorReduction()
        {
            var m = Duel("hero_fenrax", "hero_ilyra", out var fen, out var ilyra, distance: 1.5f);
            Learn(m, fen, W, 2);
            Pacify(m, ilyra);
            ilyra.RecomputeStats(m.Rules);
            float armor = ilyra.Stats.Armor;
            m.IssueOrder(fen, Order.Attack(fen.Id, ilyra.Id));
            TestUtil.Run(m, 6f);
            var rend = ilyra.FindStatus("fenrax_rend");
            Assert.NotNull(rend);
            Assert.True(rend.Stacks >= 3);
            ilyra.RecomputeStats(m.Rules);
            Assert.Equal(armor - rend.Stacks * 1f, ilyra.Stats.Armor, 2);
        }

        [Fact]
        public void Fenrax_Pounce_LeapsAndRoots()
        {
            var m = Duel("hero_fenrax", "hero_ilyra", out var fen, out var ilyra, distance: 5.5f);
            Learn(m, fen, Q, 1);
            Pacify(m, ilyra);
            m.IssueOrder(fen, Order.CastUnitOrder(fen.Id, Q, ilyra.Id));
            bool rooted = false;
            for (int i = 0; i < 40; i++) { m.Step(); rooted |= ilyra.HasFlag(StatusFlags.Rooted); }
            Assert.True(rooted);
            Assert.True(Vector2.Distance(fen.Position, ilyra.Position) < 1.5f);
        }

        // ============================================================== Morwen

        [Fact]
        public void Morwen_ToadHex_SilencesButLetsTheToadHop()
        {
            var m = Duel("hero_morwen", "hero_ilyra", out var mor, out var ilyra, distance: 5f);
            Learn(m, mor, Q, 4);
            m.IssueOrder(mor, Order.CastUnitOrder(mor.Id, Q, ilyra.Id));
            TestUtil.Run(m, 0.5f);
            Assert.True(ilyra.HasFlag(StatusFlags.Hexed));
            Assert.Equal("hex_toad", ilyra.ModelKey);
            Assert.False(ilyra.CanCast);
            var start = ilyra.Position;
            m.IssueOrder(ilyra, Order.MoveTo(ilyra.Id, start + new Vector2(0, 10)));
            TestUtil.Run(m, 1f);
            float moved = Vector2.Distance(start, ilyra.Position);
            Assert.True(moved > 1f && moved < ilyra.HeroDef.MoveSpeed * 0.8f, $"hexed units walk slowly (moved {moved:0.00} m)");
            TestUtil.Run(m, 2.5f);
            Assert.False(ilyra.HasFlag(StatusFlags.Hexed));
        }

        [Fact]
        public void Morwen_CrookedCurse_PunishesCasting_AndCreditsMorwen()
        {
            var m = Duel("hero_morwen", "hero_ilyra", out var mor, out var ilyra, distance: 5f);
            Learn(m, mor, E, 1);
            Learn(m, ilyra, Q, 1);
            Pacify(m, mor, ilyra);
            m.IssueOrder(mor, Order.CastUnitOrder(mor.Id, E, ilyra.Id));
            TestUtil.Run(m, 0.6f);
            Assert.NotNull(ilyra.FindStatus("morwen_crooked_curse"));
            ilyra.Hp = 60f;   // Blood Lance costs 40 health; the curse's 54 damage finishes her
            m.IssueOrder(ilyra, Order.CastPointOrder(ilyra.Id, Q, mor.Position));
            TestUtil.Run(m, 1f);
            Assert.True(ilyra.Dead);
            Assert.Equal(1, m.Players[0].Kills);
        }

        [Fact]
        public void Morwen_WitchingHour_EchoesAbilitiesAtReducedPower()
        {
            float Drop(bool withUltimate)
            {
                // Outside the ultimate's 5 m hex, somewhere in Morwen's line of sight.
                var m = Duel("hero_morwen", "hero_ilyra", out var mor, out var ilyra, distance: 6.2f);
                foreach (var dir in new[] { new Vector2(1, 0), new Vector2(0, 1), new Vector2(-1, 0), new Vector2(0, -1) })
                {
                    TestUtil.Teleport(m, ilyra, mor.Position + dir * 6.2f);
                    m.Vision.Update(force: true);
                    if (m.IsVisibleTo(ilyra, Team.Dawn)) break;
                }
                Assert.True(m.IsVisibleTo(ilyra, Team.Dawn));
                TestUtil.LevelTo(m, mor, 6);
                Learn(m, mor, E, 1);
                Learn(m, mor, R, 1);
                Pacify(m, mor, ilyra);
                if (withUltimate)
                {
                    m.IssueOrder(mor, Order.CastNoTargetOrder(mor.Id, R));
                    TestUtil.Run(m, 0.6f);
                    Assert.NotNull(mor.FindStatus("morwen_witching_hour"));
                }
                float hp = ilyra.Hp;
                m.IssueOrder(mor, Order.CastUnitOrder(mor.Id, E, ilyra.Id));
                TestUtil.Run(m, 0.6f);
                return hp - ilyra.Hp;
            }
            float plain = Drop(false), echoed = Drop(true);
            _out.WriteLine($"Crooked Curse damage: plain {plain:0.0}, echoed {echoed:0.0}");
            // The echo runs at 50% power after the curse's -15% magic resistance landed, so slightly above +50%.
            Assert.True(plain > 30);
            Assert.InRange(echoed, plain * 1.45f, plain * 1.7f);
        }

        [Fact]
        public void Morwen_Cauldron_HealsAlliesAndSlowsEnemies()
        {
            var m = Duel("hero_morwen", "hero_ilyra", out var mor, out var ilyra, distance: 4f);
            Learn(m, mor, W, 2);
            Pacify(m, mor, ilyra);
            m.IssueOrder(mor, Order.CastPointOrder(mor.Id, W, TestUtil.MidPoint + new Vector2(2, 0)));
            TestUtil.Run(m, 1.5f);
            var cauldron = Alive(m, "morwen_cauldron").Single();
            Assert.Equal(0f, cauldron.Stats.MoveSpeed);
            Assert.NotNull(mor.FindStatus("morwen_brew"));
            Assert.NotNull(ilyra.FindStatus("morwen_fumes"));
            Assert.Null(ilyra.FindStatus("morwen_brew"));
            Assert.Equal(2, mor.FindStatus("morwen_brew").Level);
            TestUtil.Run(m, 12f);
            Assert.Empty(Alive(m, "morwen_cauldron"));
        }

        // ============================================================== Thael

        [Fact]
        public void Thael_Barkskin_OnlyAmongTrees()
        {
            var m = Duel("hero_thael", "hero_ilyra", out var thael, out _, distance: 40f);
            Assert.True(m.CountTreesNear(thael.Position, 5f) < 3, "map centre is open ground");
            TestUtil.Run(m, 1f);
            Assert.Null(thael.FindStatus("thael_barkskin"));

            Vector2? forest = null;
            for (float x = 20; x < 170 && forest == null; x += 2)
                for (float y = 20; y < 170 && forest == null; y += 2)
                {
                    var p = new Vector2(x, y);
                    if (m.Grid.IsWalkable(p) && m.CountTreesNear(p, 4f) >= 5) forest = p;
                }
            Assert.NotNull(forest);
            TestUtil.Teleport(m, thael, forest.Value);
            TestUtil.Run(m, 1f);
            Assert.NotNull(thael.FindStatus("thael_barkskin"));
        }

        [Fact]
        public void Thael_GroveCall_TeleportsAfterChannel()
        {
            var m = Duel("hero_thael", "hero_ilyra", out var thael, out _, distance: 60f);
            Learn(m, thael, E, 1);
            var dest = m.Grid.NearestWalkable(thael.Position + new Vector2(0, 25));
            m.IssueOrder(thael, Order.CastPointOrder(thael.Id, E, dest));
            TestUtil.Run(m, 1f);
            Assert.True(Vector2.Distance(thael.Position, TestUtil.MidPoint) < 1f, "still channelling");
            TestUtil.Run(m, 1.6f);
            Assert.True(Vector2.Distance(thael.Position, dest) < 1.5f);
        }

        [Fact]
        public void Thael_GraspingRoots_RootsAlongTheLine()
        {
            var m = Duel("hero_thael", "hero_ilyra", out var thael, out var ilyra, distance: 7f);
            Learn(m, thael, Q, 1);
            Pacify(m, ilyra);
            float hp = ilyra.Hp;
            m.IssueOrder(thael, Order.CastPointOrder(thael.Id, Q, ilyra.Position));
            bool rooted = false;
            for (int i = 0; i < 45; i++) { m.Step(); rooted |= ilyra.HasFlag(StatusFlags.Rooted); }
            Assert.True(rooted);
            Assert.True(ilyra.Hp < hp - 40);
        }

        [Fact]
        public void Thael_Treants_ScaleWithLevel_AndExpire()
        {
            var m = Duel("hero_thael", "hero_ilyra", out var thael, out _, distance: 40f);
            Learn(m, thael, W, 4);
            m.IssueOrder(thael, Order.CastPointOrder(thael.Id, W, thael.Position + new Vector2(5, 0)));
            TestUtil.Run(m, 1f);
            var treants = Alive(m, "thael_treant").ToList();
            Assert.Equal(2, treants.Count);
            Assert.All(treants, t => Assert.Equal(700 + 450, t.Stats.MaxHp, 1));
            TestUtil.Run(m, 25f);
            Assert.Empty(Alive(m, "thael_treant"));
        }

        [Fact]
        public void Thael_WrathOfTheOldForest_FollowsHim_AndHardensHim()
        {
            var m = Duel("hero_thael", "hero_ilyra", out var thael, out var ilyra, distance: 20f);
            TestUtil.LevelTo(m, thael, 6);
            Learn(m, thael, R, 1);
            Pacify(m, ilyra);
            m.IssueOrder(thael, Order.CastNoTargetOrder(thael.Id, R));
            TestUtil.Run(m, 0.6f);
            Assert.NotNull(thael.FindStatus("thael_ancient_bark"));
            float hp = ilyra.Hp;
            TestUtil.Run(m, 1f);
            Assert.Equal(hp, ilyra.Hp, 1);                             // out of range
            TestUtil.Teleport(m, thael, ilyra.Position - new Vector2(3, 0));
            TestUtil.Run(m, 1.5f);
            Assert.True(ilyra.Hp < hp - 40, "the zone follows Thael");
        }

        // ============================================================== engine

        [Fact]
        public void CritMultiplier_TakesTheStrongestSource()
        {
            var acc = new StatAccumulator();
            acc.Reset();
            acc.Apply(StatType.CritMultiplier, 1.8f);
            acc.Apply(StatType.CritMultiplier, 2.0f);
            Assert.Equal(2.0f, acc.Add[(int)StatType.CritMultiplier], 3);
        }

        [Fact]
        public void TreeIndex_CountsTrunksFromTheMap()
        {
            var m = TestUtil.NewMatch(c => c.DisableCreeps = true);
            int total = 0;
            for (float x = 4; x < 192; x += 8)
                for (float y = 4; y < 192; y += 8)
                    total += m.CountTreesNear(new Vector2(x, y), 4f);
            Assert.True(total > 1000, $"expected thousands of trees, sampled {total}");
        }

        [Fact]
        public void BotMatch_AllHeroes_UseTheirAbilities()
        {
            var cfg = new MatchConfig { Seed = 5, SkipHeroSelect = true, PreGameTimeOverride = 10f, SameHeroAllowed = true };
            string[] dawn = { "hero_vorak", "hero_nyxara", "hero_malgrave", "hero_ardyn", "hero_fenrax" };
            string[] dusk = { "hero_ilyra", "hero_morwen", "hero_thael", "hero_nyxara", "hero_malgrave" };
            for (int i = 0; i < 5; i++) cfg.Players.Add(new PlayerSetup { Name = $"DawnBot{i}", Team = Team.Dawn, Slot = i, HeroId = dawn[i], IsBot = true });
            for (int i = 0; i < 5; i++) cfg.Players.Add(new PlayerSetup { Name = $"DuskBot{i}", Team = Team.Dusk, Slot = i, HeroId = dusk[i], IsBot = true });
            var m = new Match(TestUtil.Data, cfg);
            var casts = new Dictionary<string, HashSet<string>>();
            while (m.Time < 12 * 60 && m.Phase != MatchPhase.PostGame)
            {
                m.Step();
                foreach (var e in m.Events)
                {
                    if (e.Type != SimEventType.CastComplete || e.Key == null || e.Key.StartsWith("item_")) continue;
                    var u = m.GetUnit(e.UnitId);
                    if (u?.HeroDef == null) continue;
                    if (!casts.TryGetValue(u.HeroDef.Id, out var set)) casts[u.HeroDef.Id] = set = new HashSet<string>();
                    set.Add(e.Key);
                }
                m.Events.Clear();
            }
            foreach (var p in m.Players)
                _out.WriteLine($"{p.HeroId,-14} L{p.Hero.Level,2} K/D/A {p.Kills}/{p.Deaths}/{p.Assists} LH {p.LastHits} casts: {string.Join(", ", casts.TryGetValue(p.HeroId, out var s) ? s : new HashSet<string>())}");
            foreach (var h in dawn.Concat(dusk).Distinct())
                Assert.True(casts.ContainsKey(h) && casts[h].Count >= 2, $"{h} bots should cast at least two different abilities");
            // Levelling pace is covered by the 20-minute bot test; here every bot only has to be alive in the game.
            Assert.True(m.Players.All(p => p.Hero.Level >= 3));
        }
    }
}
