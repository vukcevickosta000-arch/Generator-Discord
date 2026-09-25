using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    public sealed class PlayerSetup
    {
        public string AccountId;
        public string Name;
        public Team Team;
        public int Slot;
        public string HeroId;
        public bool IsBot;
        public BotDifficulty BotDifficulty = BotDifficulty.Normal;
        /// <summary>RTS: faction id (see GameData.RtsFactions); null or unknown = random.</summary>
        public string RtsFaction;
    }

    public sealed class MatchConfig
    {
        public string MatchId = Guid.NewGuid().ToString("N");
        public string ModeId = "moba_5v5";
        public string MapId = "map_velmoragh";
        public ulong Seed = 1;
        public List<PlayerSetup> Players = new List<PlayerSetup>();
        public HeroPickMode PickMode = HeroPickMode.AllPick;
        /// <summary>Skip hero select + loading (tests, quick practice).</summary>
        public bool SkipHeroSelect;
        public float? PreGameTimeOverride;
        public float? HeroSelectTimeOverride;
        public bool AllowCheats;
        public bool Ranked;
        public bool DisableCreeps;
        public bool DisableNeutrals;
        public bool DisableVharoth;
        public bool SameHeroAllowed;
    }

    /// <summary>
    /// The authoritative MOBA simulation. Runs at a fixed tick on the dedicated server (and in-process for offline
    /// bot games). Pure C#: no engine types, no wall-clock access, deterministic RNG.
    /// Split across partial files by system: Combat, Casting, Effects, Statuses, Movement, Projectiles, Items,
    /// Spawning, Economy, Vision, Vharoth.
    /// </summary>
    public sealed partial class Match
    {
        public readonly GameData Data;
        public readonly RulesDef Rules;
        public readonly MapDef Map;
        public readonly NavGrid Grid;
        public readonly MatchConfig Config;
        public readonly GameModeDef Mode;
        public readonly float Dt;

        public int Tick;
        /// <summary>Match clock in seconds. Negative during pre-game (horn at 0).</summary>
        public float Time;
        public MatchPhase Phase;
        public float PhaseTimer;
        /// <summary>Effective night: the natural cycle or a forced night (Fenrax's Night Unleashed).</summary>
        public bool IsNight;
        /// <summary>Night by the natural day/night cycle.</summary>
        public bool NaturalNight;
        /// <summary>Match time until which it is night regardless of the cycle.</summary>
        public float ForcedNightUntil = float.MinValue;
        public float DayNightTimer;
        public Team Winner = Team.None;
        public float EndTime;

        public readonly List<Unit> Units = new List<Unit>(512);
        public readonly Dictionary<int, Unit> UnitById = new Dictionary<int, Unit>(512);
        public readonly List<Player> Players = new List<Player>();
        public readonly List<Projectile> Projectiles = new List<Projectile>(128);
        public readonly List<Zone> Zones = new List<Zone>(64);
        public readonly List<Corpse> Corpses = new List<Corpse>(64);
        public readonly SpatialHash Spatial;
        public readonly VisionSystem Vision;
        public readonly DeterministicRandom Rng;
        public readonly List<SimEvent> Events = new List<SimEvent>(256);
        public readonly int[] TeamKills = new int[2];
        public readonly Unit[] Cores = new Unit[2];
        public readonly List<string> Log = new List<string>();

        private readonly List<(Player player, Order order)> _pendingOrders = new List<(Player, Order)>();
        private readonly List<Unit> _spawnQueue = new List<Unit>();
        private readonly List<Unit> _scratch = new List<Unit>(64);
        private readonly List<Unit> _scratch2 = new List<Unit>(64);
        private int _nextUnitId = 1;
        private int _nextProjectileId = 1;
        private int _nextZoneId = 1;
        private bool _firstBloodTaken;

        public Match(GameData data, MatchConfig config)
        {
            Data = data;
            Config = config;
            Rules = data.Rules;
            Mode = data.Modes.TryGetValue(config.ModeId, out var m) ? m : new GameModeDef { Id = config.ModeId, Map = config.MapId };
            Map = data.Maps.TryGetValue(config.MapId, out var map) ? map : throw new ArgumentException($"Unknown map '{config.MapId}'");
            Dt = 1f / Rules.TickRate;
            Rng = new DeterministicRandom(config.Seed);

            byte[] cells = string.IsNullOrEmpty(Map.Grid)
                ? null
                : NavGrid.DecodeRle(Map.Grid, Map.GridWidth * Map.GridHeight);
            Grid = cells != null
                ? new NavGrid(Map.GridWidth, Map.GridHeight, Map.CellSize, cells)
                : NavGrid.CreateOpen((int)(Map.Width / Map.CellSize), (int)(Map.Height / Map.CellSize), Map.CellSize);
            Spatial = new SpatialHash(Grid.WorldWidth, Grid.WorldHeight, 4f);
            Vision = new VisionSystem(this);

            int pid = 0;
            foreach (var ps in config.Players)
            {
                Players.Add(new Player
                {
                    Id = pid++,
                    AccountId = ps.AccountId,
                    Name = ps.Name,
                    Team = ps.Team,
                    Slot = ps.Slot,
                    HeroId = ps.HeroId,
                    IsBot = ps.IsBot,
                    BotDifficulty = ps.BotDifficulty,
                    Connection = ps.IsBot ? PlayerConnection.Bot : PlayerConnection.Connected,
                    Gold = Rules.StartingGold,
                    HeroLocked = !string.IsNullOrEmpty(ps.HeroId),
                    Loaded = ps.IsBot,
                });
            }

            if (config.SkipHeroSelect)
            {
                if (!IsRts) AutoPickRemaining();
                BeginPreGame();
            }
            else if (IsRts)
            {
                // No hero draft in the RTS: factions are chosen in the lobby (heroes are recruited in game).
                Phase = MatchPhase.Loading;
                PhaseTimer = 90f;
            }
            else
            {
                Phase = MatchPhase.HeroSelect;
                PhaseTimer = config.HeroSelectTimeOverride ?? Rules.HeroSelectTime;
                // Bots pick after the humans (AutoPickRemaining) so they never take a hero a player wanted.
            }
        }

        public float MatchSeconds => Math.Max(0f, Time);
        public Player GetPlayer(int id) => id >= 0 && id < Players.Count ? Players[id] : null;
        public Unit GetUnit(int id) => id != 0 && UnitById.TryGetValue(id, out var u) && !u.Removed ? u : null;

        // =================================================================== phases

        public void Step()
        {
            Tick++;
            switch (Phase)
            {
                case MatchPhase.HeroSelect:
                    PhaseTimer -= Dt;
                    if (PhaseTimer <= 0 || Players.All(p => p.HeroLocked || p.IsBot))
                    {
                        AutoPickRemaining();
                        Phase = MatchPhase.Loading;
                        PhaseTimer = 90f;
                        Emit(new SimEvent { Type = SimEventType.MatchPhase, Value = (float)Phase, PlayerId = -1 });
                    }
                    break;
                case MatchPhase.Loading:
                    PhaseTimer -= Dt;
                    if (PhaseTimer <= 0 || Players.All(p => p.Loaded || p.Connection != PlayerConnection.Connected))
                        BeginPreGame();
                    break;
                case MatchPhase.PreGame:
                case MatchPhase.Playing:
                    SimulateWorld();
                    break;
            }
        }

        public void SetPlayerLoaded(Player p, float progress)
        {
            p.LoadProgress = progress;
            if (progress >= 1f) p.Loaded = true;
        }

        private void BeginPreGame()
        {
            Phase = MatchPhase.PreGame;
            Time = -(Config.PreGameTimeOverride ?? (IsRts ? Rules.RtsPreGameTime : Rules.PreGameTime));
            DayNightTimer = Rules.DayLength;
            IsNight = NaturalNight = false;
            if (IsRts) SpawnRtsStart();
            else
            {
                SpawnStructures();
                foreach (var p in Players) SpawnHero(p);
            }
            Emit(new SimEvent { Type = SimEventType.MatchPhase, Value = (float)Phase, PlayerId = -1 });
            Vision.Update(force: true);
        }

        // =================================================================== hero select

        public bool TryPickHero(Player p, string heroId, out string error)
        {
            error = null;
            if (Phase != MatchPhase.HeroSelect) { error = "Hero selection is closed."; return false; }
            if (p.HeroLocked) { error = "You already locked in a hero."; return false; }
            if (heroId == "random")
            {
                var pool = AvailableHeroes(p.Team).ToList();
                if (pool.Count == 0) { error = "No heroes available."; return false; }
                heroId = pool[Rng.Range(0, pool.Count)];
            }
            if (!Data.Heroes.TryGetValue(heroId, out var hero) || !hero.Playable) { error = "That hero is not available."; return false; }
            if (UniquePicks && Players.Any(o => o != p && o.HeroId == heroId && o.HeroLocked)) { error = "That hero has already been picked."; return false; }
            p.HeroId = heroId;
            p.HeroLocked = true;
            Emit(new SimEvent { Type = SimEventType.MatchPhase, Key = "pick", Value = (float)Phase, UnitId = p.Id, PlayerId = -1 });
            return true;
        }

        /// <summary>
        /// Each hero may be picked once per match, unless the mode allows duplicates or the playable roster is smaller
        /// than the number of players (early development roster).
        /// </summary>
        public bool UniquePicks => !Config.SameHeroAllowed && Data.PlayableHeroes().Count() >= Players.Count;

        public IEnumerable<string> AvailableHeroes(Team team)
        {
            foreach (var h in Data.PlayableHeroes())
                if (!UniquePicks || !Players.Any(o => o.HeroLocked && o.HeroId == h.Id)) yield return h.Id;
        }

        private void BotPickHero(Player p)
        {
            var pool = AvailableHeroes(p.Team).ToList();
            if (pool.Count == 0) pool = Data.PlayableHeroes().Select(h => h.Id).ToList();
            if (pool.Count == 0) return;
            p.HeroId = pool[Rng.Range(0, pool.Count)];
            p.HeroLocked = true;
        }

        private void AutoPickRemaining()
        {
            foreach (var p in Players)
            {
                if (p.HeroLocked && !string.IsNullOrEmpty(p.HeroId) && Data.Heroes.ContainsKey(p.HeroId)) continue;
                p.HeroLocked = false;
                BotPickHero(p);
            }
        }

        // =================================================================== orders

        /// <summary>Queue an order from a player. Validated and applied at the start of the next tick.</summary>
        public void SubmitOrder(Player p, Order o)
        {
            if (p == null) return;
            o.IssuedTick = Tick;
            lock (_pendingOrders) _pendingOrders.Add((p, o));
        }

        private void ProcessPendingOrders()
        {
            List<(Player, Order)> batch;
            lock (_pendingOrders)
            {
                if (_pendingOrders.Count == 0) return;
                batch = new List<(Player, Order)>(_pendingOrders);
                _pendingOrders.Clear();
            }
            foreach (var (p, o) in batch) ApplyOrder(p, o);
        }

        public bool PlayerControls(Player p, Unit u) => u != null && (u.Owner == p || (u.Summoner != null && u.Summoner.Owner == p));

        private void ApplyOrder(Player p, Order o)
        {
            if (o.Type == OrderType.Ping) { Emit(new SimEvent { Type = SimEventType.Ping, Point = o.Point, Value = o.Slot, Team = p.Team, UnitId = p.Hero?.Id ?? 0, OtherId = p.Id, PlayerId = -1 }); return; }
            if (o.Type == OrderType.Buyback) { TryBuyback(p); return; }
            if (o.Group != null && o.Group.Length > 0) { ApplyGroupOrder(p, o); return; }
            var unit = GetUnit(o.UnitId) ?? p.Hero;
            if (!PlayerControls(p, unit)) return;
            IssueOrder(unit, o);
        }

        /// <summary>Applies an order to a unit (used by players and AI).</summary>
        private void ApplyGroupOrder(Player p, Order o)
        {
            var units = new List<Unit>();
            foreach (int id in new[] { o.UnitId }.Concat(o.Group))
            {
                if (units.Count >= Order.MaxGroup) break;
                var u = GetUnit(id);
                if (u != null && PlayerControls(p, u) && !units.Contains(u)) units.Add(u);
            }
            if (units.Count == 0) return;
            o.Group = null;
            switch (o.Type)
            {
                case OrderType.Train:
                {
                    // Like classic RTS: the selected building with the shortest queue takes the order.
                    var b = units.Where(u => u.Kind == UnitKind.Building && !u.Dead && !u.UnderConstruction && u.UnitDef.Trains != null && u.UnitDef.Trains.Contains(o.ItemId ?? ""))
                                 .OrderBy(u => u.TrainQueue?.Count ?? 0).ThenBy(u => u.Id).FirstOrDefault() ?? units[0];
                    o.UnitId = b.Id;
                    IssueOrder(b, o);
                    return;
                }
                case OrderType.Build:
                case OrderType.CastNoTarget:
                case OrderType.CastUnit:
                case OrderType.CastPoint:
                case OrderType.ToggleAbility:
                case OrderType.LevelAbility:
                case OrderType.BuyItem:
                case OrderType.SellItem:
                case OrderType.SwapItems:
                    // One unit acts; the rest of the selection keeps what it was doing.
                    o.UnitId = units[0].Id;
                    IssueOrder(units[0], o);
                    return;
            }
            foreach (var u in units)
            {
                var copy = o;
                copy.UnitId = u.Id;
                IssueOrder(u, copy);
            }
        }

        public void IssueOrder(Unit unit, Order o)
        {
            if (unit == null || unit.Removed) return;
            switch (o.Type)
            {
                case OrderType.LevelAbility: TryLevelAbility(unit, o.Slot); return;
                case OrderType.BuyItem: TryBuyItem(unit, o.ItemId); return;
                case OrderType.SellItem: TrySellItem(unit, o.Slot); return;
                case OrderType.SwapItems: SwapItems(unit, o.Slot, o.Slot2); return;
                case OrderType.ToggleAbility: ToggleAbility(unit, o.Slot); return;
            }
            if (TryImmediateRtsOrder(unit, o)) return;
            if (unit.Dead) return;
            if (o.Type == OrderType.Harvest || o.Type == OrderType.Build || o.Type == OrderType.ReturnResources)
            {
                if (!ValidateWorkerOrder(unit, o, out var werr)) { EmitError(unit, werr); return; }
            }
            if (o.Type == OrderType.CastNoTarget || o.Type == OrderType.CastUnit || o.Type == OrderType.CastPoint)
            {
                var ab = unit.GetAbility(o.Slot);
                if (ab == null) return;
                if (ab.Def.Targeting == TargetingMode.Toggle) { ToggleAbility(unit, o.Slot); return; }
                if (!ValidateCast(unit, ab, o, out var err)) { EmitError(unit, err); return; }
                // Instant no-target abilities with zero cast point do not interrupt the current order (like items).
                if (ab.Def.CastPoint <= 0f && ab.Def.Targeting == TargetingMode.NoTarget && (ab.Def.ChannelTime == null || ab.Def.ChannelTime.IsZero))
                {
                    ExecuteCast(unit, ab, o.TargetId, o.Point, o.Point2);
                    return;
                }
            }
            if (o.Queue && (unit.CurrentOrder.Type != OrderType.None || unit.OrderQueue.Count > 0))
            {
                if (unit.OrderQueue.Count < 16) unit.OrderQueue.Add(o);
                return;
            }
            unit.OrderQueue.Clear();
            BeginOrder(unit, o);
        }

        private void BeginOrder(Unit unit, Order o)
        {
            // Interrupt: cancelling attack windup / cast windup / backswing / channel.
            if (unit.Action == ActionState.CastWindup) CancelCast(unit, false);
            if (unit.Action == ActionState.Channeling) EndChannel(unit, interrupted: true);
            if (unit.Action == ActionState.AttackWindup || unit.Action == ActionState.AttackBackswing || unit.Action == ActionState.CastBackswing)
                SetAction(unit, ActionState.Idle);
            if (unit.GatherTimer > 0f || unit.Action == ActionState.Working) StopWorking(unit);
            unit.CurrentOrder = o;
            unit.Path.Clear();
            unit.PathIndex = 0;
            unit.RepathTimer = 0;
            if (o.Type == OrderType.Stop) { unit.CurrentOrder = default; SetAction(unit, ActionState.Idle); }
            if (o.Type == OrderType.Hold) unit.HoldPosition = unit.Position;
        }

        internal void CompleteOrder(Unit unit)
        {
            unit.Path.Clear();
            if (unit.OrderQueue.Count > 0)
            {
                var next = unit.OrderQueue[0];
                unit.OrderQueue.RemoveAt(0);
                BeginOrder(unit, next);
            }
            else unit.CurrentOrder = default;
        }

        // =================================================================== world tick

        private void SimulateWorld()
        {
            float dt = Dt;
            Time += dt;
            if (Phase == MatchPhase.PreGame && Time >= 0f)
            {
                Phase = MatchPhase.Playing;
                Emit(new SimEvent { Type = SimEventType.MatchPhase, Value = (float)Phase, PlayerId = -1 });
                Announce(AnnouncerKeys.BattleBegins, Team.None);
            }

            // New units spawned during the previous tick join the world now.
            if (_spawnQueue.Count > 0)
            {
                foreach (var u in _spawnQueue) { Units.Add(u); UnitById[u.Id] = u; }
                _spawnQueue.Clear();
            }

            RebuildSpatial();
            UpdateDayNight(dt);
            if (Phase == MatchPhase.Playing)
            {
                if (IsRts) { UpdateRtsSpawners(); UpdateRtsAi(); }
                else
                {
                    UpdateSpawners(dt);
                    UpdatePassiveIncome(dt);
                    UpdateVharoth(dt);
                }
            }
            ProcessPendingOrders();

            // AI: staggered by unit id so the per-tick cost stays flat.
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (u.Brain == null || u.Removed) continue;
                if (u.Dead && !u.IsHero) continue;
                int period = u.IsStructure ? 2 : u.IsHero ? 3 : 4;
                if ((Tick + u.Id) % period == 0) u.Brain.Think(this, u, dt * period);
            }

            UpdateStatuses(dt);
            if (Tick % 15 == 0) UpdateAuras();
            if (Tick % 3 == 0) UpdateIntervalTriggers();

            // RTS: alternate the update direction every tick. Units update in creation order, so a fixed order let
            // the first player's units win every simultaneous exchange of blows (a 2:1 edge in bot mirrors).
            bool reverse = IsRts && (Tick & 1) == 1;
            for (int n = 0; n < Units.Count; n++)
            {
                var u = Units[reverse ? Units.Count - 1 - n : n];
                if (u.Removed) continue;
                if (u.StatsDirty) u.RecomputeStats(Rules);
                UpdateUnit(u, dt);
            }
            if (IsRts) UpdateRts(dt);

            UpdateProjectiles(dt);
            UpdateZones(dt);
            ResolveUnitOverlaps(dt);
            UpdateDeathsAndRespawns(dt);

            if (Tick % 3 == 0) Vision.Update(false);
            if (Tick % (Rules.TickRate * 60) == 0) SampleTimelines();
            CheckVictory();
        }

        private void RebuildSpatial()
        {
            Spatial.Clear();
            foreach (var u in Units) if (u.IsAlive) Spatial.Insert(u);
        }

        /// <summary>Radius query helper returning a reused list (do not hold on to it).</summary>
        public List<Unit> UnitsInRadius(Vector2 center, float radius, bool includeRadius = true)
        {
            _scratch.Clear();
            Spatial.Query(center, radius, _scratch, includeRadius);
            for (int i = _scratch.Count - 1; i >= 0; i--) if (!_scratch[i].IsAlive) _scratch.RemoveAt(i);
            return _scratch;
        }

        /// <summary>Radius query into a caller-owned list (safe for nested use).</summary>
        public void UnitsInRadius(Vector2 center, float radius, List<Unit> results, bool includeRadius = true)
        {
            int start = results.Count;
            Spatial.Query(center, radius, results, includeRadius);
            for (int i = results.Count - 1; i >= start; i--) if (!results[i].IsAlive) results.RemoveAt(i);
        }

        private void UpdateDayNight(float dt)
        {
            DayNightTimer -= dt;
            if (DayNightTimer <= 0)
            {
                NaturalNight = !NaturalNight;
                DayNightTimer = NaturalNight ? Rules.NightLength : Rules.DayLength;
            }
            bool night = NaturalNight || Time < ForcedNightUntil;
            if (night != IsNight)
            {
                IsNight = night;
                Emit(new SimEvent { Type = SimEventType.DayNight, Value = IsNight ? 1 : 0, PlayerId = -1 });
                Announce(IsNight ? AnnouncerKeys.Nightfall : AnnouncerKeys.Daybreak, Team.None);
            }
        }

        private void SampleTimelines()
        {
            foreach (var p in Players)
            {
                p.NetWorthTimeline.Add(NetWorth(p));
                p.XpTimeline.Add(p.Hero?.Xp ?? 0);
            }
        }

        private void CheckVictory()
        {
            if (Winner != Team.None || Phase != MatchPhase.Playing) return;
            if (IsRts) { CheckRtsVictory(); if (Winner != Team.None) return; }
            for (int t = 0; t < 2; t++)
            {
                if (Cores[t] != null && Cores[t].Dead) { EndMatch(t == 0 ? Team.Dusk : Team.Dawn); return; }
            }
            // Abandon rule: a team with every human abandoned loses (if the other team has humans).
            for (int t = 0; t < 2; t++)
            {
                var humans = Players.Where(p => (int)p.Team == t && !p.IsBot).ToList();
                if (humans.Count > 0 && humans.All(p => p.Connection == PlayerConnection.Abandoned)
                    && Players.Any(p => (int)p.Team != t && !p.IsBot && p.Connection != PlayerConnection.Abandoned))
                { EndMatch(t == 0 ? Team.Dusk : Team.Dawn); return; }
            }
        }

        public void EndMatch(Team winner)
        {
            if (Phase == MatchPhase.PostGame) return;
            Winner = winner;
            EndTime = Time;
            Phase = MatchPhase.PostGame;
            SampleTimelines();
            foreach (var p in Players)
                p.FinalItems = p.Hero?.Inventory?.Where(i => i != null).Select(i => i.Def.Id).ToArray() ?? new string[0];
            Emit(new SimEvent { Type = SimEventType.MatchPhase, Value = (float)Phase, Team = winner, PlayerId = -1 });
            Announce(AnnouncerKeys.Victory, winner);
            Announce(AnnouncerKeys.Defeat, winner == Team.Dawn ? Team.Dusk : Team.Dawn);
        }

        // =================================================================== spawning units

        public Unit CreateUnit(UnitDef def, Team team, Vector2 position, float facing = 0f, Player owner = null)
        {
            var u = new Unit
            {
                Id = _nextUnitId++,
                Kind = def.Kind,
                Team = team,
                DefId = def.Id,
                UnitDef = def,
                Name = def.Name,
                Position = position,
                Facing = facing,
                Radius = def.CollisionRadius,
                Flying = def.Flying,
                InnateFlags = def.InnateFlags,
                Owner = owner,
                SpawnTick = Tick,
                HomePosition = position,
            };
            if (def.MaxMana > 0 || def.Abilities.Count > 0) { }
            foreach (var abId in def.Abilities)
            {
                if (!Data.Abilities.TryGetValue(abId, out var ad)) continue;
                u.Abilities.Add(new AbilityInstance { Def = ad, Level = ad.MaxLevel > 0 ? Math.Min(ad.MaxLevel, 1) : 1, Index = u.Abilities.Count, Charges = ad.MaxCharges });
            }
            u.Flags = u.InnateFlags;
            u.RecomputeStats(Rules);
            u.LastPosition = position;
            RegisterUnit(u);
            if (IsRts && owner != null) ApplyOwnedRtsStatuses(u);
            return u;
        }

        private void RegisterUnit(Unit u)
        {
            if (Phase == MatchPhase.PreGame || Phase == MatchPhase.Playing) _spawnQueue.Add(u);
            else { Units.Add(u); UnitById[u.Id] = u; }
            UnitById[u.Id] = u;
            Emit(new SimEvent { Type = SimEventType.Spawn, UnitId = u.Id, Point = u.Position, Team = u.Team, Key = u.DefId, PlayerId = -1 });
        }

        private void SpawnHero(Player p)
        {
            if (string.IsNullOrEmpty(p.HeroId) || !Data.Heroes.TryGetValue(p.HeroId, out var hd)) return;
            var baseDef = Map.Bases.FirstOrDefault(b => b.Team == p.Team);
            Vector2 spawn = baseDef != null ? (Vector2)baseDef.HeroSpawn : new Vector2(10, 10);
            float angle = p.Slot * (MathUtil.TwoPi / 5f);
            spawn += MathUtil.FromAngle(angle) * 1.5f;
            spawn = Grid.NearestWalkable(spawn);
            var u = CreateHero(hd, p, spawn, p.Team == Team.Dawn ? MathUtil.Pi * 0.25f : -MathUtil.Pi * 0.75f);
            p.Hero = u;
            if (p.IsBot) u.Brain = new BotBrain(p.BotDifficulty, Rng.NextULong());
            RegisterUnit(u);
        }

        /// <summary>
        /// A level-1 hero for a player: abilities unlearned (innates at level 1), one ability point, empty inventory.
        /// The caller registers it (MOBA: the player's one hero; RTS: recruited at an altar).
        /// </summary>
        private Unit CreateHero(HeroDef hd, Player p, Vector2 spawn, float facing)
        {
            var u = new Unit
            {
                Id = _nextUnitId++,
                Kind = UnitKind.Hero,
                Team = p.Team,
                DefId = hd.Id,
                HeroDef = hd,
                Name = hd.Name,
                Position = spawn,
                Facing = facing,
                Radius = hd.CollisionRadius,
                Owner = p,
                Level = 1,
                AbilityPoints = 1,
                Inventory = new ItemInstance[Rules.InventorySlots],
                Stash = new ItemInstance[Rules.StashSlots],
                Backpack = new ItemInstance[Rules.BackpackSlots],
                SpawnTick = Tick,
                HomePosition = spawn,
            };
            foreach (var abId in hd.Abilities)
            {
                if (!Data.Abilities.TryGetValue(abId, out var ad)) continue;
                var inst = new AbilityInstance { Def = ad, Level = ad.Slot == AbilitySlot.Innate ? 1 : 0, Index = u.Abilities.Count, Charges = ad.MaxCharges };
                u.Abilities.Add(inst);
            }
            // Abilities every hero has (hidden from the bar), after the hero's own so their slots stay 0..4.
            foreach (var ad in Data.Abilities.Values.Where(a => a.CommonHeroAbility).OrderBy(a => a.Id, StringComparer.Ordinal))
                if (!hd.Abilities.Contains(ad.Id))
                    u.Abilities.Add(new AbilityInstance { Def = ad, Level = 1, Index = u.Abilities.Count, Charges = ad.MaxCharges });
            u.RecomputeStats(Rules);
            u.LastPosition = spawn;
            return u;
        }

        // =================================================================== events

        public void Emit(SimEvent e)
        {
            e.Tick = Tick;
            if (e.PlayerId == 0 && e.Type != SimEventType.Error && e.Type != SimEventType.GoldChange) e.PlayerId = -1;
            Events.Add(e);
        }

        public void EmitError(Unit unit, string message)
        {
            if (unit?.Owner == null || string.IsNullOrEmpty(message)) return;
            Events.Add(new SimEvent { Type = SimEventType.Error, Tick = Tick, UnitId = unit.Id, Key = message, PlayerId = unit.Owner.Id });
        }

        public void Announce(string key, Team team, int playerId = -1, int unitId = 0)
        {
            Events.Add(new SimEvent { Type = SimEventType.Announcer, Tick = Tick, Key = key, Team = team, OtherId = playerId, UnitId = unitId, PlayerId = -1 });
        }

        public void LogLine(string s) { if (Log.Count < 5000) Log.Add($"[{Time:0.0}] {s}"); }
    }

    public sealed class Corpse
    {
        public Vector2 Position;
        public string UnitId;
        public Team Team;
        public float Expires;
        public bool Consumed;
    }
}
