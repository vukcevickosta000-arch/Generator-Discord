using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Bloodfall.Data;
using Bloodfall.Simulation;

namespace Bloodfall.Tests
{
    public static class TestUtil
    {
        private static GameData _data;
        private static readonly object Lock = new object();

        public static GameData Data
        {
            get
            {
                lock (Lock)
                {
                    if (_data != null) return _data;
                    var root = GameDataLoader.FindDefaultRoot(AppContext.BaseDirectory) ?? throw new DirectoryNotFoundException("GameData folder not found");
                    _data = GameDataLoader.FromDirectory(root);
                    return _data;
                }
            }
        }

        public static Match NewMatch(Action<MatchConfig> configure = null, int dawn = 1, int dusk = 1, string dawnHero = "hero_vorak", string duskHero = "hero_ilyra", bool bots = false)
        {
            var cfg = new MatchConfig { Seed = 7, SkipHeroSelect = true, PreGameTimeOverride = 1f, SameHeroAllowed = true };
            for (int i = 0; i < dawn; i++) cfg.Players.Add(new PlayerSetup { Name = $"Dawn{i}", Team = Team.Dawn, Slot = i, HeroId = dawnHero, IsBot = bots });
            for (int i = 0; i < dusk; i++) cfg.Players.Add(new PlayerSetup { Name = $"Dusk{i}", Team = Team.Dusk, Slot = i, HeroId = duskHero, IsBot = bots });
            configure?.Invoke(cfg);
            return new Match(Data, cfg);
        }

        public static void Run(Match m, float seconds)
        {
            int ticks = (int)(seconds * m.Rules.TickRate);
            for (int i = 0; i < ticks && m.Phase != MatchPhase.PostGame; i++) m.Step();
        }

        /// <summary>Places a hero at a position (tests only).</summary>
        public static void Teleport(Match m, Unit u, Vector2 p)
        {
            u.Position = m.Grid.NearestWalkable(p);
            u.LastPosition = u.Position;
            u.Path.Clear();
        }

        public static Vector2 MidPoint => new Vector2(96, 96);

        public static void LevelTo(Match m, Unit hero, int level)
        {
            int need = m.Rules.Experience.Cumulative[level - 1] - hero.Xp;
            if (need > 0) m.AddXp(hero, need);
        }
    }
}
