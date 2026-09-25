using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    public sealed partial class Match
    {
        private int _wavesSpawned;
        private float _nextWaveTime = float.MinValue;
        private float _nextNeutralTime = float.MinValue;
        private float _stashTimer;
        /// <summary>Per team per lane: which barracks types of the ENEMY were destroyed (grants super creeps).</summary>
        private readonly HashSet<string> _destroyedBarracks = new HashSet<string>();
        public readonly Dictionary<string, List<Unit>> CampUnits = new Dictionary<string, List<Unit>>();
        public int WavesSpawned => _wavesSpawned;

        private void SpawnStructures()
        {
            var byId = new Dictionary<string, Unit>();
            foreach (var s in Map.Structures)
            {
                if (!Data.Units.TryGetValue(s.UnitId, out var def)) continue;
                var u = CreateUnit(def, s.Team, s.Position, s.Facing * MathUtil.Deg2Rad);
                u.StructureId = s.Id;
                u.Tier = s.Tier;
                u.Lane = s.Lane;
                u.BarracksType = s.BarracksType;
                if (def.Kind == UnitKind.Tower || def.Kind == UnitKind.Fountain) u.Brain = new TowerBrain();
                if (def.Kind == UnitKind.Core) Cores[(int)s.Team] = u;
                byId[s.Id] = u;
            }
            foreach (var s in Map.Structures)
            {
                if (s.ProtectedBy == null || !byId.TryGetValue(s.Id, out var u)) continue;
                u.ProtectedBy = s.ProtectedBy.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
            }
            foreach (var s in Map.Structures)
            {
                if (s.UnlockedByAny == null || !byId.TryGetValue(s.Id, out var u)) continue;
                u.UnlockedByAny = s.UnlockedByAny.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
            }
            // Structures are created before the world runs: move them from the spawn queue now.
            foreach (var u in _spawnQueue) { Units.Add(u); UnitById[u.Id] = u; }
            _spawnQueue.Clear();
            foreach (var u in Units) RecomputeFlags(u);
        }

        private bool _rtsCampsSpawned;

        /// <summary>RTS maps: neutral camps guard the expansions and do not respawn.</summary>
        private void UpdateRtsSpawners()
        {
            if (_rtsCampsSpawned || Config.DisableNeutrals) return;
            _rtsCampsSpawned = true;
            SpawnNeutrals();
        }

        private void UpdateSpawners(float dt)
        {
            if (_nextWaveTime == float.MinValue) _nextWaveTime = Rules.FirstWaveTime;
            if (_nextNeutralTime == float.MinValue) _nextNeutralTime = Rules.NeutralSpawnTime;

            if (!Config.DisableCreeps && Time >= _nextWaveTime)
            {
                SpawnWave();
                _nextWaveTime += Rules.CreepWaveInterval;
            }
            if (!Config.DisableNeutrals && Time >= _nextNeutralTime)
            {
                SpawnNeutrals();
                _nextNeutralTime += Rules.NeutralRespawnInterval;
            }
            _stashTimer -= dt;
            if (_stashTimer <= 0f)
            {
                _stashTimer = Rules.StashDeliveryInterval;
                foreach (var p in Players) if (p.Hero != null) DeliverStash(p.Hero);
            }
        }

        private string CreepUnitId(Team team, string kind)
        {
            Team enemy = OtherTeam(team);
            return Rules.CreepUnits.TryGetValue($"{team}.{kind}", out var id) ? id : null;
        }

        private void SpawnWave()
        {
            _wavesSpawned++;
            int upgrade = Rules.CreepUpgradeInterval > 0 ? (int)(Math.Max(0f, Time) / Rules.CreepUpgradeInterval) : 0;
            bool siege = Rules.SiegeEveryNWaves > 0 && _wavesSpawned % Rules.SiegeEveryNWaves == 0;
            for (int lane = 0; lane < Map.Lanes.Count; lane++)
            {
                var wps = Map.Lanes[lane].Waypoints;
                if (wps.Count < 2) continue;
                foreach (var team in new[] { Team.Dawn, Team.Dusk })
                {
                    Vector2 start = team == Team.Dawn ? wps[0] : wps[wps.Count - 1];
                    Vector2 next = team == Team.Dawn ? wps[1] : wps[wps.Count - 2];
                    var dir = MathUtil.SafeNormalize(next - start, Vector2.UnitX);
                    var side = new Vector2(-dir.Y, dir.X);
                    string laneName = Map.Lanes[lane].Name;
                    bool allBarracksDown = AllEnemyBarracksDestroyed(team);
                    string Pick(string kind)
                    {
                        string tier = allBarracksDown ? ".mega" : _destroyedBarracks.Contains($"{team}:{laneName}:{kind}") ? ".super" : "";
                        return CreepUnitId(team, kind + tier) ?? CreepUnitId(team, kind);
                    }
                    var composition = new List<string>();
                    for (int i = 0; i < Rules.WaveMelee; i++) composition.Add(Pick("melee"));
                    for (int i = 0; i < Rules.WaveRanged; i++) composition.Add(Pick("ranged"));
                    if (siege) composition.Add(Pick("siege"));
                    for (int i = 0; i < composition.Count; i++)
                    {
                        if (composition[i] == null || !Data.Units.TryGetValue(composition[i], out var def)) continue;
                        int row = i / 3, col = i % 3;
                        var pos = start - dir * (row * 1.4f) + side * ((col - 1) * 0.9f);
                        pos = Grid.NearestWalkable(pos);
                        var c = CreateUnit(def, team, pos, MathUtil.AngleOf(dir));
                        c.LaneIndex = lane;
                        c.WaypointIndex = team == Team.Dawn ? 1 : wps.Count - 2;
                        c.CreepUpgradeLevel = upgrade;
                        c.IsMegaCreep = allBarracksDown;
                        c.StatsDirty = true;
                        c.Brain = new CreepBrain();
                    }
                }
            }
            Emit(new SimEvent { Type = SimEventType.WaveSpawned, Value = _wavesSpawned, PlayerId = -1 });
            if (_wavesSpawned == 1) Announce(AnnouncerKeys.CreepsSpawned, Team.None);
        }

        private bool AllEnemyBarracksDestroyed(Team team)
        {
            bool any = false;
            foreach (var u in Units)
            {
                if (u.Kind != UnitKind.Barracks || u.Team == team) continue;
                any = true;
                if (!u.Dead) return false;
            }
            return any;
        }

        private void OnBarracksDestroyed(Unit barracks)
        {
            Team beneficiary = OtherTeam(barracks.Team);
            _destroyedBarracks.Add($"{beneficiary}:{barracks.Lane}:{barracks.BarracksType ?? "melee"}");
            if (AllEnemyBarracksDestroyed(beneficiary)) Announce(AnnouncerKeys.MegaCreeps, beneficiary);
        }

        private void SpawnNeutrals()
        {
            foreach (var camp in Map.Camps)
            {
                if (!Data.CampTypes.TryGetValue(camp.CampType, out var ct) || ct.Variants.Count == 0) continue;
                // Blocking rule: the spawn box must be empty of all units.
                var list = RentList();
                UnitsInRadius(camp.Position, camp.SpawnBoxRadius, list);
                bool blocked = list.Any(u => u.Kind != UnitKind.Ward || !u.IsInvisible);
                ReturnList(list);
                if (blocked) continue;
                var variant = ct.Variants[IsRts ? SymmetricVariant(camp.Position, ct.Variants.Count) : Rng.Range(0, ct.Variants.Count)];
                if (!CampUnits.TryGetValue(camp.Id, out var members)) { members = new List<Unit>(); CampUnits[camp.Id] = members; }
                members.RemoveAll(m => m.Dead || m.Removed);
                int upgrade = (int)(Math.Max(0f, Time) / 600f);
                for (int i = 0; i < variant.Count; i++)
                {
                    if (!Data.Units.TryGetValue(variant[i], out var def)) continue;
                    var pos = Grid.NearestWalkable(camp.Position + MathUtil.FromAngle(i * 2.1f) * (i == 0 ? 0f : 1.3f));
                    var n = CreateUnit(def, Team.Neutral, pos, Rng.Range(0f, MathUtil.TwoPi));
                    n.CampId = camp.Id;
                    n.HomePosition = pos;
                    n.LeashRange = 9f;
                    n.CreepUpgradeLevel = upgrade;
                    n.StatsDirty = true;
                    n.Brain = new NeutralBrain();
                    members.Add(n);
                }
            }
        }

        /// <summary>
        /// RTS maps are point-symmetric; a camp and its mirror image must hold the same creeps or one player's
        /// expansion is easier to take. The variant is derived from the camp's position folded onto one half.
        /// </summary>
        private int SymmetricVariant(Vector2 p, int count)
        {
            var mirror = new Vector2(Grid.WorldWidth, Grid.WorldHeight) - p;
            var key = p.X + p.Y < mirror.X + mirror.Y ? p : mirror;
            int h = (int)Math.Round(key.X * 10f) * 7919 + (int)Math.Round(key.Y * 10f) * 104729;
            return (int)((uint)h % (uint)count);
        }

        /// <summary>Called by neutral brains: the whole camp retaliates together.</summary>
        public void AlertCamp(Unit member, Unit attacker)
        {
            if (member.CampId == null || !CampUnits.TryGetValue(member.CampId, out var members)) return;
            foreach (var m in members)
            {
                if (m.Dead || m.Removed || m == member) continue;
                if (m.AggroTargetId == 0) { m.AggroTargetId = attacker.Id; m.AggroUntil = Time + 4f; }
            }
        }
    }
}
