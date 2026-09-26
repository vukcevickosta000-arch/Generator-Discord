using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bloodfall.Data;
using Bloodfall.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Bloodfall.Tests
{
    /// <summary>
    /// Every playable hero (the hand-written concept heroes and the generated roster, Tools/heroes): each active ability
    /// casts in a real match, and bots use their kits.
    /// </summary>
    public class RosterTests
    {
        private readonly ITestOutputHelper _out;
        public RosterTests(ITestOutputHelper output) => _out = output;

        private static readonly HashSet<string> Concept = new HashSet<string>
            { "hero_vorak", "hero_ilyra", "hero_nyxara", "hero_malgrave", "hero_ardyn", "hero_fenrax", "hero_morwen", "hero_thael" };

        [Fact]
        public void EveryHeroCastsEveryAbility()
        {
            var failures = new List<string>();
            int casts = 0;
            foreach (var hd in TestUtil.Data.PlayableHeroes().ToList())
            {
                var m = TestUtil.NewMatch(c => { c.DisableCreeps = true; c.DisableNeutrals = true; }, 1, 1, hd.Id, "hero_thael");
                TestUtil.Run(m, 1.2f);
                var a = m.Players[0].Hero;
                var b = m.Players[1].Hero;
                TestUtil.LevelTo(m, a, 18);
                foreach (var ab in a.Abilities) ab.Level = ab.Def.MaxLevel;
                a.RecomputeStats(m.Rules);
                for (int i = 0; i < a.Abilities.Count; i++)
                {
                    var ab = a.Abilities[i];
                    if (ab.Def.Targeting == TargetingMode.Passive || ab.Def.Hidden) continue;
                    // Fresh positions and resources for every cast (dashes and swaps move the heroes).
                    TestUtil.Teleport(m, a, TestUtil.MidPoint);
                    TestUtil.Teleport(m, b, TestUtil.MidPoint + new Vector2(2.5f, 0));
                    foreach (var s in a.Statuses.Where(s => s.Def.IsDebuff).ToList()) m.RemoveStatus(a, s, expired: false);
                    a.CurrentOrder = default;
                    b.CurrentOrder = default;
                    a.Mana = a.Stats.MaxMana; a.Hp = a.Stats.MaxHp;
                    b.Hp = b.Stats.MaxHp;
                    m.Vision.Update(force: true);
                    m.Events.Clear();
                    var d = ab.Def;
                    Order o = d.Targeting switch
                    {
                        TargetingMode.NoTarget or TargetingMode.Toggle => Order.CastNoTargetOrder(a.Id, i),
                        TargetingMode.Unit => Order.CastUnitOrder(a.Id, i, (d.TargetTeam & TargetTeam.Enemy) != 0 ? b.Id : a.Id),
                        _ => Order.CastPointOrder(a.Id, i, b.Position),
                    };
                    m.IssueOrder(a, o);
                    bool cast = false;
                    for (int t = 0; t < 3 * m.Rules.TickRate && !cast; t++)
                    {
                        m.Step();
                        cast = ab.Cooldown > 0f;
                    }
                    if (!cast)
                    {
                        var err = m.Events.FirstOrDefault(e => e.Type == SimEventType.Error);
                        failures.Add($"{hd.Id} {d.Id}: no cast{(err.Key != null ? " (" + err.Key + ")" : "")}");
                    }
                    else casts++;
                    TestUtil.Run(m, 1.5f);
                    if (b.Dead) failures.Add($"{hd.Id} {d.Id}: the target died from one cast at full health");
                    if (b.Dead || a.Dead) break;
                }
            }
            _out.WriteLine($"{casts} casts");
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void RosterBots_UseTheirAbilities()
        {
            var roster = TestUtil.Data.PlayableHeroes().Select(h => h.Id).Where(id => !Concept.Contains(id)).OrderBy(id => id).ToList();
            if (roster.Count == 0) return;
            var casts = new Dictionary<string, HashSet<string>>();
            for (int g = 0; g * 10 < roster.Count; g++)
            {
                var group = roster.Skip(g * 10).Take(10).ToList();
                while (group.Count < 10) group.Add(roster[group.Count % roster.Count]);
                var cfg = new MatchConfig { Seed = (ulong)(31 + g), SkipHeroSelect = true, PreGameTimeOverride = 10f, SameHeroAllowed = true };
                for (int i = 0; i < 10; i++)
                    cfg.Players.Add(new PlayerSetup { Name = $"Bot{i}", Team = i < 5 ? Team.Dawn : Team.Dusk, Slot = i % 5, HeroId = group[i], IsBot = true });
                var m = new Match(TestUtil.Data, cfg);
                while (m.Time < 8 * 60 && m.Phase != MatchPhase.PostGame)
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
            }
            foreach (var h in roster) _out.WriteLine($"{h,-20} {(casts.TryGetValue(h, out var s) ? string.Join(", ", s) : "-")}");
            // One 8-minute game per hero: which abilities a bot levels first and whether it meets enemies varies, so a
            // single hero may show only one ability (every ability's cast is proven by EveryHeroCastsEveryAbility).
            var silent = roster.Where(h => !casts.ContainsKey(h)).ToList();
            var single = roster.Where(h => casts.TryGetValue(h, out var s) && s.Count < 2).ToList();
            Assert.True(silent.Count == 0, "bots should cast their abilities: " + string.Join(", ", silent));
            Assert.True(single.Count <= roster.Count / 10, "most bots should cast at least two different abilities; only one: " + string.Join(", ", single));
        }
    }
}
