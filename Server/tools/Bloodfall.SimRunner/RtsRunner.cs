using System;
using System.Collections.Generic;
using System.Linq;
using Bloodfall.Data;
using Bloodfall.Simulation;

namespace Bloodfall.SimRunner
{
    /// <summary>
    /// RTS bot-vs-bot games on Ashfields: `--rts [--factions dawnguard,ashen_legion] [--games N] [--difficulty Normal] [--trace]`.
    /// With several games, seeds count up from the given seed and sides alternate so both factions play both starts.
    /// </summary>
    public static class RtsRunner
    {
        public static int Run(GameData data, string[] args, float minutes, ulong seed)
        {
            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            var factions = (Arg("--factions") ?? string.Join(",", data.RtsFactionOrder.Take(2))).Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (factions.Length == 1) factions = new[] { factions[0], factions[0] };
            foreach (var f in factions) if (!data.RtsFactions.ContainsKey(f)) { Console.WriteLine("Unknown faction: " + f); return 1; }
            int games = int.TryParse(Arg("--games"), out var g) ? g : 1;
            // "--difficulty Normal" for both, or "--difficulty Normal,Beginner" (first faction's bot, second faction's bot).
            var diffs = (Arg("--difficulty") ?? "Normal").Split(',').Select(x => Enum.TryParse<BotDifficulty>(x, true, out var d) ? d : BotDifficulty.Normal).ToArray();
            if (diffs.Length == 1) diffs = new[] { diffs[0], diffs[0] };
            bool trace = args.Contains("--trace");
            var wins = new Dictionary<string, int>();
            var sideWins = new Dictionary<Team, int>();
            var lengths = new List<float>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long ticks = 0;
            for (int game = 0; game < games; game++)
            {
                bool swap = game % 2 == 1;
                var cfg = new MatchConfig { ModeId = "rts_1v1", MapId = "map_rts_ashfields", Seed = seed + (ulong)game, SkipHeroSelect = true };
                cfg.Players.Add(new PlayerSetup { Name = "Dawn", Team = Team.Dawn, IsBot = true, BotDifficulty = swap ? diffs[1] : diffs[0], RtsFaction = swap ? factions[1] : factions[0] });
                cfg.Players.Add(new PlayerSetup { Name = "Dusk", Team = Team.Dusk, IsBot = true, BotDifficulty = swap ? diffs[0] : diffs[1], RtsFaction = swap ? factions[0] : factions[1] });
                var m = new Match(data, cfg);
                float nextReport = 0f;
                while (m.Time < minutes * 60 && m.Phase != MatchPhase.PostGame)
                {
                    m.Step();
                    if (args.Contains("--log"))
                        foreach (var e in m.Events.Where(e => e.Type == SimEventType.Error))
                            Console.WriteLine($"   [{m.Time:0.0}] ERROR {m.GetPlayer(e.PlayerId)?.Name} unit {e.UnitId}: {e.Key}");
                    m.Events.Clear();
                    if (games == 1 && m.Time >= nextReport)
                    {
                        nextReport += trace ? 30f : 120f;
                        foreach (var p in m.Players) Console.WriteLine($"[{m.Time / 60f,5:0.0}m] {Line(m, p)}");
                    }
                }
                ticks += m.Tick;
                if (args.Contains("--log")) foreach (var l in m.Log) Console.WriteLine("   " + l);
                var wp = m.Players.FirstOrDefault(p => p.Team == m.Winner);
                string winner = m.Winner == Team.None ? "none" : diffs[0] == diffs[1] ? wp.RtsFaction.Id : $"{wp.RtsFaction.Id}/{wp.BotDifficulty}";
                wins[winner] = wins.TryGetValue(winner, out var w) ? w + 1 : 1;
                sideWins[m.Winner] = sideWins.TryGetValue(m.Winner, out var sw2) ? sw2 + 1 : 1;
                if (m.Winner != Team.None) lengths.Add(m.EndTime / 60f);
                Console.WriteLine($"Game {game + 1}: seed {cfg.Seed}, {string.Join(" vs ", m.Players.Select(p => $"{p.Team}={p.RtsFaction.Id}/{p.BotDifficulty}"))}, winner {winner} at {m.MatchSeconds / 60f:0.0} min");
                foreach (var p in m.Players)
                    Console.WriteLine($"   {p.RtsFaction.Id,-13} mined {p.GoldMined,6} lumber {p.LumberHarvested,5} trained {p.UnitsTrained,3} lost {p.UnitsLost,3} killed {p.UnitsKilled,3} built {p.BuildingsBuilt,2} lostB {p.BuildingsLost,2} razed {p.BuildingsRazed,2}");
            }
            sw.Stop();
            Console.WriteLine($"Simulated {games} game(s), {ticks} ticks in {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalMilliseconds / Math.Max(1, ticks):0.000} ms/tick)");
            Console.WriteLine("Wins: " + string.Join(", ", wins.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))
                + (lengths.Count > 0 ? $"; decided games last {lengths.Average():0.0} min on average" : ""));
            Console.WriteLine("By start: " + string.Join(", ", sideWins.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}")));
            return 0;
        }

        private static string Line(Match m, Player p)
        {
            var own = m.Units.Where(u => u.Owner == p && u.IsAlive).ToList();
            int workers = own.Count(u => u.Kind == UnitKind.Worker);
            int soldiers = own.Count(u => u.Kind == UnitKind.Soldier);
            var buildings = own.Where(u => u.Kind == UnitKind.Building).GroupBy(u => u.Name).Select(gr => $"{gr.Key}×{gr.Count()}");
            var army = own.Where(u => u.Kind == UnitKind.Soldier).ToList();
            string armyInfo = "";
            if (army.Count > 0)
            {
                var c = army.Aggregate(System.Numerics.Vector2.Zero, (s, u) => s + u.Position) / army.Count;
                var orders = army.GroupBy(u => u.CurrentOrder.Type + "/" + u.Action).Select(g => $"{g.Key}×{g.Count()}");
                var targets = army.Select(u => m.GetUnit(u.CurrentOrder.Type == OrderType.AttackUnit ? u.CurrentOrder.TargetId : u.AttackTargetId)).Where(t => t != null)
                                  .GroupBy(t => t.Name + (t.Team == p.Team ? "(own)" : "")).Select(g => $"{g.Key}×{g.Count()}");
                armyInfo = $" army@({c.X:0},{c.Y:0}) {string.Join(" ", orders)} targets: {string.Join(" ", targets)}";
            }
            if (m.Time > 0 && System.Environment.GetEnvironmentVariable("RTS_ARMY") == "1") return $"{p.Team} {armyInfo}";
            return $"{p.Team,-4} {p.RtsFaction.Id,-13} gold {p.Gold,5} lumber {p.Lumber,4} supply {p.SupplyUsed,3}/{p.SupplyCap,-3} workers {workers,2} army {soldiers,2} [{m.RtsAiOf(p)?.DebugState}] {string.Join(", ", buildings)}";
        }
    }
}
