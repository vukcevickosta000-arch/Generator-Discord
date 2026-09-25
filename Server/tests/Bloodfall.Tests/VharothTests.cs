using System.Linq;
using System.Numerics;
using Bloodfall.Data;
using Bloodfall.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Bloodfall.Tests
{
    /// <summary>The Vharoth event (milestone 7): seals, awakening, the boss, the Blood Moon and the Heart of Vharoth.</summary>
    public class VharothTests
    {
        private readonly ITestOutputHelper _out;
        public VharothTests(ITestOutputHelper output) { _out = output; }

        private static Match NewMatch(int dawn = 1, int dusk = 1, string dawnHero = "hero_vorak", string duskHero = "hero_ilyra")
        {
            var m = TestUtil.NewMatch(c => { c.DisableCreeps = true; c.DisableNeutrals = true; }, dawn, dusk, dawnHero, duskHero);
            TestUtil.Run(m, 1.2f);
            return m;
        }

        /// <summary>Jumps the clock to just before the event and steps into Tremors.</summary>
        private static void StartTremors(Match m)
        {
            m.Time = m.Rules.VharothMinTime - 0.05f;
            TestUtil.Run(m, 0.2f);
            Assert.Equal(VharothPhase.Tremors, m.VharothState);
        }

        private static Unit[] Seals(Match m) => m.Units.Where(u => u.DefId == Match.SealUnitId && u.IsAlive).ToArray();

        private static void Awaken(Match m)
        {
            StartTremors(m);
            var hero = m.Players[0].Hero;
            foreach (var s in Seals(m)) m.KillUnit(s, hero);
            TestUtil.Run(m, 0.1f);
            Assert.Equal(VharothPhase.Awakened, m.VharothState);
        }

        private static void Pacify(Match m, params Unit[] units)
        {
            foreach (var u in units) m.ApplyStatus(u, "disarm", null, 1, 600f);
        }

        [Fact]
        public void Seals_AppearAtTheMinimumTime()
        {
            var m = NewMatch();
            m.Time = m.Rules.VharothMinTime - 5f;
            TestUtil.Run(m, 2f);
            Assert.Equal(VharothPhase.Dormant, m.VharothState);
            Assert.Empty(Seals(m));
            TestUtil.Run(m, 3.5f);
            Assert.Equal(VharothPhase.Tremors, m.VharothState);
            var seals = Seals(m);
            Assert.Equal(m.Map.VharothSeals.Count, seals.Length);
            Assert.All(seals, s => Assert.True(s.Invulnerable));
        }

        [Fact]
        public void DisableVharoth_KeepsTheEventDormant()
        {
            var m = TestUtil.NewMatch(c => { c.DisableCreeps = true; c.DisableVharoth = true; });
            TestUtil.Run(m, 1.2f);
            m.Time = m.Rules.VharothMinTime + 1f;
            TestUtil.Run(m, 1f);
            Assert.Equal(VharothPhase.Dormant, m.VharothState);
        }

        [Fact]
        public void Seal_CannotBeAttacked_ButBreaksAfterAChannel()
        {
            var m = NewMatch();
            StartTremors(m);
            var hero = m.Players[0].Hero;
            var seal = Seals(m)[0];
            TestUtil.Teleport(m, hero, seal.Position + new Vector2(1.8f, 0));
            m.Vision.Update(force: true);
            Assert.False(m.CanAttackTarget(hero, seal, out _) && !seal.Invulnerable);

            int slot = hero.Abilities.FindIndex(a => a.Def.Id == "vharoth_break_seal");
            Assert.True(slot >= 5, "the seal channel is appended after the hero's own abilities");
            int goldBefore = m.Players[0].Gold;
            m.IssueOrder(hero, Order.CastUnitOrder(hero.Id, slot, seal.Id));
            TestUtil.Run(m, 1.5f);
            Assert.Equal(ActionState.Channeling, hero.Action);
            Assert.True(hero.HasFlag(StatusFlags.Revealed));
            Assert.True(seal.IsAlive);
            TestUtil.Run(m, 2.2f);
            Assert.True(seal.Dead);
            Assert.Equal(1, m.VharothSealsBroken);
            Assert.True(m.Players[0].Gold >= goldBefore + m.Rules.VharothSealGold);
        }

        [Fact]
        public void SealChannel_IsInterruptedByEnemyDamage()
        {
            var m = NewMatch();
            StartTremors(m);
            var hero = m.Players[0].Hero;
            var enemy = m.Players[1].Hero;
            var seal = Seals(m)[0];
            TestUtil.Teleport(m, hero, seal.Position + new Vector2(1.8f, 0));
            m.Vision.Update(force: true);
            int slot = hero.Abilities.FindIndex(a => a.Def.Id == "vharoth_break_seal");
            m.IssueOrder(hero, Order.CastUnitOrder(hero.Id, slot, seal.Id));
            TestUtil.Run(m, 1f);
            Assert.Equal(ActionState.Channeling, hero.Action);
            m.DealDamage(new DamageInfo { Source = enemy, Target = hero, Amount = 20, Type = DamageType.Pure });
            Assert.NotEqual(ActionState.Channeling, hero.Action);
            TestUtil.Run(m, 3f);
            Assert.True(seal.IsAlive);
            Assert.Equal(0, m.VharothSealsBroken);
        }

        [Fact]
        public void BreakingEverySeal_AwakensVharothInHisPit()
        {
            var m = NewMatch();
            Awaken(m);
            Assert.Equal(m.Map.VharothSeals.Count, m.VharothSealsBroken);
            var boss = m.Vharoth;
            Assert.NotNull(boss);
            Assert.Equal(UnitKind.Boss, boss.Kind);
            Assert.Equal(Team.Neutral, boss.Team);
            Assert.True(Vector2.Distance(boss.Position, m.Map.BossPit) < 2f);
            Assert.Equal(m.Data.Units[Match.VharothUnitId].MaxHp, boss.Stats.MaxHp, 0);
        }

        [Fact]
        public void Vharoth_FightsInThePit_LeashesAndResets()
        {
            var m = NewMatch();
            Awaken(m);
            var boss = m.Vharoth;
            var hero = m.Players[0].Hero;
            TestUtil.LevelTo(m, hero, 15);
            hero.RecomputeStats(m.Rules);
            hero.Hp = hero.Stats.MaxHp;
            TestUtil.Teleport(m, hero, m.Map.BossPit + new Vector2(3, 0));
            m.Vision.Update(force: true);
            Pacify(m, hero);
            float hp = hero.Hp;
            TestUtil.Run(m, 3f);
            Assert.True(hero.Hp < hp - 100, "Vharoth attacks heroes in his pit");

            boss.Hp = boss.Stats.MaxHp * 0.8f;
            TestUtil.Teleport(m, hero, m.Map.BossPit + new Vector2(25, 0));
            TestUtil.Run(m, 16f);
            Assert.True(Vector2.Distance(boss.Position, m.Map.BossPit) < 2.5f, "he returns to his pit");
            Assert.True(boss.HpFraction > 0.99f, "and heals to full when left alone");
        }

        [Fact]
        public void Vharoth_Crush_And_BloodRain_And_Wrath_FollowHisHealth()
        {
            var m = NewMatch();
            Awaken(m);
            var boss = m.Vharoth;
            var hero = m.Players[0].Hero;
            TestUtil.LevelTo(m, hero, 25);
            hero.RecomputeStats(m.Rules);
            TestUtil.Teleport(m, hero, m.Map.BossPit + new Vector2(2.5f, 0));
            m.Vision.Update(force: true);
            Pacify(m, hero);

            // Full health: Crush stuns, but no Blood Rain yet.
            int rain = 0; bool stunned = false;
            for (int i = 0; i < 30 * 10; i++)
            {
                hero.Hp = hero.Stats.MaxHp;
                m.Events.Clear(); m.Step();
                stunned |= hero.HasFlag(StatusFlags.Stunned);
                rain += m.Events.Count(e => e.Type == SimEventType.ZoneCreated && e.Key == "blood_rain_telegraph");
            }
            Assert.True(stunned, "every third attack crushes");
            Assert.Equal(0, rain);

            // Below 70%: five telegraphs per volley.
            boss.Hp = boss.Stats.MaxHp * 0.6f;
            rain = 0;
            for (int i = 0; i < 30 * 8; i++)
            {
                hero.Hp = hero.Stats.MaxHp;
                m.Events.Clear(); m.Step();
                rain += m.Events.Count(e => e.Type == SimEventType.ZoneCreated && e.Key == "blood_rain_telegraph");
            }
            Assert.True(rain >= 5 && rain % 5 == 0, $"blood rain volleys of five (got {rain})");
            Assert.Null(boss.FindStatus("vharoth_enrage"));

            // Below 30%: enraged, pulses knock heroes around.
            boss.Hp = boss.Stats.MaxHp * 0.25f;
            int pulses = 0;
            for (int i = 0; i < 30 * 4; i++)
            {
                hero.Hp = hero.Stats.MaxHp;
                m.Events.Clear(); m.Step();
                pulses += m.Events.Count(e => e.Type == SimEventType.EffectVisual && e.Key == "titan_wrath_pulse");
            }
            Assert.NotNull(boss.FindStatus("vharoth_enrage"));
            Assert.True(pulses >= 1);
        }

        [Fact]
        public void BloodMoon_RisesIfVharothLives_AndEndsWhenHeDies()
        {
            var m = NewMatch();
            Awaken(m);
            Assert.False(m.IsNight);
            m.Time = m.VharothAwakenedAt + m.Rules.VharothBloodMoonDelay + 0.1f;
            TestUtil.Run(m, 0.2f);
            Assert.Equal(VharothPhase.BloodMoon, m.VharothState);
            Assert.True(m.IsNight);
            Assert.Equal(m.Rules.VharothBloodMoonVision, m.VisionScale, 3);

            m.KillUnit(m.Vharoth, m.Players[0].Hero);
            TestUtil.Run(m, 0.2f);
            Assert.Equal(VharothPhase.Slain, m.VharothState);
            Assert.Equal(1f, m.VisionScale, 3);
            Assert.Equal(m.NaturalNight, m.IsNight);
        }

        [Fact]
        public void SlayingVharoth_RewardsTheKillersTeam_WithTheHeart()
        {
            var m = NewMatch(dawn: 2);
            Awaken(m);
            var killer = m.Players[1];
            var ally = m.Players[0];
            var enemy = m.Players[2];
            int kg = killer.Gold, ag = ally.Gold, eg = enemy.Gold;
            m.KillUnit(m.Vharoth, killer.Hero);
            Assert.Equal(Team.Dawn, m.VharothSlainBy);
            var boss = m.Data.Units[Match.VharothUnitId];
            Assert.True(killer.Gold >= kg + boss.TeamBountyGold + boss.BountyGoldMin);
            Assert.True(ally.Gold >= ag + boss.TeamBountyGold);
            Assert.Equal(eg, enemy.Gold);
            Assert.Contains(killer.Hero.Inventory, i => i?.Def.Id == Match.HeartItemId);
        }

        [Fact]
        public void HeartOfVharoth_BringsItsBearerBackWhereTheyFell()
        {
            var m = NewMatch();
            Awaken(m);
            var bearer = m.Players[0].Hero;
            m.KillUnit(m.Vharoth, bearer);
            Assert.Contains(bearer.Inventory, i => i?.Def.Id == Match.HeartItemId);
            bearer.RecomputeStats(m.Rules);
            TestUtil.Teleport(m, bearer, TestUtil.MidPoint);
            var diedAt = bearer.Position;
            m.KillUnit(bearer, m.Players[1].Hero);
            Assert.DoesNotContain(bearer.Inventory, i => i?.Def.Id == Match.HeartItemId);
            TestUtil.Run(m, m.Rules.ReincarnationDelay + 0.3f);
            Assert.False(bearer.Dead);
            Assert.True(Vector2.Distance(bearer.Position, diedAt) < 1.5f, "revived on the spot, not at the fountain");
            // Without the Heart the next death sends the hero to the fountain on the normal timer.
            m.KillUnit(bearer, m.Players[1].Hero);
            TestUtil.Run(m, m.Rules.ReincarnationDelay + 0.3f);
            Assert.True(bearer.Dead);
        }

        [Fact]
        public void HeartOfVharoth_Use_GrantsBloodthirstToNearbyAllies()
        {
            var m = NewMatch(dawn: 2);
            Awaken(m);
            var bearer = m.Players[0].Hero;
            var ally = m.Players[1].Hero;
            m.KillUnit(m.Vharoth, bearer);
            TestUtil.Teleport(m, bearer, TestUtil.MidPoint);
            TestUtil.Teleport(m, ally, TestUtil.MidPoint + new Vector2(3, 0));
            int slot = System.Array.FindIndex(bearer.Inventory, i => i?.Def.Id == Match.HeartItemId);
            TestUtil.Run(m, 0.1f);   // let the spatial index see the teleported heroes
            m.IssueOrder(bearer, Order.CastNoTargetOrder(bearer.Id, Order.ItemSlotBase + slot));
            TestUtil.Run(m, 0.3f);
            Assert.NotNull(bearer.FindStatus("vharoth_bloodthirst"));
            Assert.NotNull(ally.FindStatus("vharoth_bloodthirst"));
            Assert.DoesNotContain(bearer.Inventory, i => i?.Def.Id == Match.HeartItemId);
        }

        [Fact]
        public void Snapshot_CarriesTheEventState()
        {
            var m = NewMatch();
            StartTremors(m);
            m.KillUnit(Seals(m)[0], m.Players[0].Hero);
            var index = new Bloodfall.Protocol.ContentIndex(m.Data);
            var bytes = Bloodfall.Protocol.Codec.Snapshot(m, Team.Dawn, m.Players[0], index);
            var r = new Bloodfall.Protocol.NetReader(bytes, 0, bytes.Length);
            Assert.Equal((byte)Bloodfall.Protocol.MsgType.Snapshot, r.ReadByte());
            var frame = Bloodfall.Protocol.Codec.ReadSnapshot(r, index);
            Assert.Equal((byte)VharothPhase.Tremors, frame.VharothPhase);
            Assert.Equal(1, frame.VharothSeals);
        }

        [Fact]
        public void Bots_BreakTheSeals()
        {
            var cfg = new MatchConfig { Seed = 3, SkipHeroSelect = true, PreGameTimeOverride = 1f, SameHeroAllowed = true, DisableCreeps = true, DisableNeutrals = true };
            string[] heroes = { "hero_vorak", "hero_ardyn", "hero_thael" };
            for (int i = 0; i < 3; i++) cfg.Players.Add(new PlayerSetup { Name = $"D{i}", Team = Team.Dawn, Slot = i, HeroId = heroes[i], IsBot = true });
            for (int i = 0; i < 3; i++) cfg.Players.Add(new PlayerSetup { Name = $"N{i}", Team = Team.Dusk, Slot = i, HeroId = heroes[i], IsBot = true });
            var m = new Match(TestUtil.Data, cfg);
            TestUtil.Run(m, 1.5f);
            m.Time = m.Rules.VharothMinTime - 0.05f;
            TestUtil.Run(m, 150f);
            _out.WriteLine($"phase {m.VharothState}, seals broken {m.VharothSealsBroken}");
            Assert.True(m.VharothSealsBroken >= 2, "bots go and break seals");
        }
    }
}
