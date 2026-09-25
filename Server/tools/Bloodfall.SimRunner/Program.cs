using System;
using System.Collections.Generic;
using System.Linq;
using Bloodfall.Data;
using Bloodfall.Simulation;

// Headless balance / AI analysis tool:
//   dotnet run -- [minutes] [seed] [--deaths] [--trace <playerIndex>] [--mirror | --heroes id1,id2,...]
// By default the ten bots cycle through every playable hero (Dawn from the start of the roster, Dusk from the middle).
namespace Bloodfall.SimRunner
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            float minutes = args.Length > 0 && float.TryParse(args[0], out var mm) ? mm : 20f;
            ulong seed = args.Length > 1 && ulong.TryParse(args[1], out var ss) ? ss : 11UL;
            bool deaths = args.Contains("--deaths");
            int trace = -1;
            int ti = Array.IndexOf(args, "--trace");
            if (ti >= 0 && ti + 1 < args.Length) trace = int.Parse(args[ti + 1]);
            var root = GameDataLoader.FindDefaultRoot(AppContext.BaseDirectory) ?? GameDataLoader.FindDefaultRoot(Environment.CurrentDirectory);
            var data = GameDataLoader.FromDirectory(root);
            if (data.Errors.Count > 0) { foreach (var e in data.Errors) Console.WriteLine("DATA ERROR: " + e); return 1; }

            string[] heroes = data.PlayableHeroes().Select(h => h.Id).ToArray();
            if (args.Contains("--mirror")) heroes = new[] { "hero_vorak" };
            int hi = Array.IndexOf(args, "--heroes");
            if (hi >= 0 && hi + 1 < args.Length) heroes = args[hi + 1].Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var h in heroes)
                if (!data.Heroes.ContainsKey(h)) { Console.WriteLine("Unknown hero: " + h); return 1; }
            int duskOffset = heroes.Length > 1 ? heroes.Length / 2 : 0;

            var cfg = new MatchConfig { Seed = seed, SkipHeroSelect = true, PreGameTimeOverride = 30f, SameHeroAllowed = true };
            for (int i = 0; i < 5; i++) cfg.Players.Add(new PlayerSetup { Name = $"Dawn{i}", Team = Team.Dawn, Slot = i, HeroId = heroes[i % heroes.Length], IsBot = true });
            for (int i = 0; i < 5; i++) cfg.Players.Add(new PlayerSetup { Name = $"Dusk{i}", Team = Team.Dusk, Slot = i, HeroId = heroes[(i + duskOffset) % heroes.Length], IsBot = true });
            var m = new Match(data, cfg);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var killers = new Dictionary<string, int>();
            float nextTrace = 0;
            while (m.Time < minutes * 60 && m.Phase != MatchPhase.PostGame)
            {
                m.Step();
                foreach (var e in m.Events)
                {
                    if (e.Type == SimEventType.Death && (e.Flags & SimEvent.FlagHero) != 0)
                    {
                        var victim = m.GetUnit(e.UnitId);
                        var killer = m.GetUnit(e.OtherId);
                        string k = killer == null ? "none" : killer.IsHero ? "hero" : killer.Kind.ToString();
                        killers[k] = killers.TryGetValue(k, out var c) ? c + 1 : 1;
                        if (deaths) Console.WriteLine($"[{m.Time,6:0}] {victim?.Owner?.Name} ({victim?.Name}) killed by {killer?.Name ?? "?"} ({k}) at {e.Point}");
                    }
                    if (e.Type == SimEventType.StructureDestroyed && deaths) Console.WriteLine($"[{m.Time,6:0}] STRUCTURE {m.GetUnit(e.UnitId)?.StructureId} destroyed");
                }
                m.Events.Clear();
                if (trace >= 0 && m.Time >= nextTrace)
                {
                    nextTrace = m.Time + 5f;
                    var p = m.Players[trace];
                    var h = p.Hero;
                    var brain = h.Brain as BotBrain;
                    Console.WriteLine($"[{m.Time,6:0}] {p.Name} {brain?.DebugState,-8} pos=({h.Position.X:0},{h.Position.Y:0}) hp={h.HpFraction:P0} lvl={h.Level} order={h.CurrentOrder.Type} action={h.Action} dead={h.Dead}");
                }
            }
            sw.Stop();
            Console.WriteLine($"Simulated {m.Time:0}s in {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalMilliseconds / Math.Max(1, m.Tick):0.000} ms/tick)");
            foreach (var p in m.Players)
                Console.WriteLine($"{p.Name,-7} {p.HeroId,-11} L{p.Hero.Level,2} K/D/A {p.Kills,2}/{p.Deaths,2}/{p.Assists,2} LH {p.LastHits,3} DN {p.Denies,2} NW {m.NetWorth(p),5} GPM {p.Gpm(m.MatchSeconds),4:0} XPM {p.Xpm(m.MatchSeconds),4:0}");
            Console.WriteLine($"Kills {m.TeamKills[0]}-{m.TeamKills[1]}  towers down Dawn {m.Units.Count(u => u.Kind == UnitKind.Tower && u.Dead && u.Team == Team.Dawn)} Dusk {m.Units.Count(u => u.Kind == UnitKind.Tower && u.Dead && u.Team == Team.Dusk)}  winner {m.Winner}");
            Console.WriteLine("Hero deaths by killer type: " + string.Join(", ", killers.Select(kv => $"{kv.Key}={kv.Value}")));
            return 0;
        }
    }
}
