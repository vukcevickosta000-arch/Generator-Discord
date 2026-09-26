using System.Linq;
using System.Numerics;
using Bloodfall.Data;
using Bloodfall.Simulation;
using Xunit;

namespace Bloodfall.Tests
{
    /// <summary>Lane creep behaviour at the end of a lane.</summary>
    public class CreepTests
    {
        [Fact]
        public void CreepsPastTheLaneEnd_MarchOnTheCore()
        {
            // Regression: every lane ends short of the core, and a wave that reached the last waypoint used to stand
            // there idle out of the core's reach, so 17 of 22 hour-long bot games never ended.
            var m = TestUtil.NewMatch(c => { c.DisableCreeps = true; c.DisableNeutrals = true; });
            TestUtil.Run(m, 1.2f);
            foreach (var s in m.Units.Where(u => u.Team == Team.Dusk && (u.Kind == UnitKind.Tower || u.Kind == UnitKind.Barracks)).ToList())
                m.KillUnit(s, null);
            var core = m.Units.Single(u => u.Team == Team.Dusk && u.Kind == UnitKind.Core);
            Assert.False(core.Invulnerable);

            int mid = m.Map.Lanes.FindIndex(l => l.Name == "mid");
            var wps = m.Map.Lanes[mid].Waypoints;
            Vector2 end = wps[wps.Count - 1];
            Assert.True(Vector2.Distance(end, core.Position) > 12f, "the lane ends out of the core's reach");
            var def = m.Data.Units[m.Rules.CreepUnits["Dawn.melee"]];
            var creep = m.CreateUnit(def, Team.Dawn, m.Grid.NearestWalkable(end));
            creep.LaneIndex = mid;
            creep.WaypointIndex = wps.Count - 1;
            creep.Brain = new CreepBrain();

            TestUtil.Run(m, 25f);
            Assert.False(creep.Dead);
            Assert.True(core.Hp < core.Stats.MaxHp, "the creep reached and attacked the core");
        }
    }
}
