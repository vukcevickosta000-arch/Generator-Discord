using System;
using System.Linq;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    /// <summary>
    /// Blood War couriers. Every player has a flying courier at the fountain.
    /// - The deliver order (the ` key) sends it to pick up the player's stash (items bought away from a shop), fly to
    ///   the hero and hand them over, then fly home.
    /// - What does not fit in the hero's inventory and backpack stays on the courier and goes back into the stash.
    /// - A killed courier keeps what it carries and respawns at the fountain after <see cref="RulesDef.CourierRespawnTime"/>.
    /// </summary>
    public partial class Match
    {
        private void SpawnCouriers()
        {
            if (!Data.Units.TryGetValue(Rules.CourierUnit ?? "", out var def)) return;
            foreach (var p in Players)
            {
                if (p.Hero == null) continue;
                var home = CourierHome(p);
                var c = CreateUnit(def, p.Team, home, p.Hero.Facing, p);
                c.HomePosition = home;
                c.CourierState = CourierState.Idle;
                p.Courier = c;
            }
        }

        /// <summary>Where a player's courier waits: beside the team fountain, one spot per slot.</summary>
        private Vector2 CourierHome(Player p)
        {
            var b = Map.Bases.FirstOrDefault(x => x.Team == p.Team);
            var fountain = b != null ? (Vector2)b.Fountain : p.Hero?.HomePosition ?? Vector2.Zero;
            return fountain + MathUtil.FromAngle(p.Slot * (MathUtil.TwoPi / 5f) + 0.6f) * 3.2f;
        }

        private bool CourierAtHome(Unit c) => Vector2.Distance(c.Position, c.HomePosition) < 1.5f;

        /// <summary>The deliver order: bring the stash (and anything already carried) to the owner's hero.</summary>
        public bool TryCourierDeliver(Unit courier)
        {
            var p = courier?.Owner;
            if (p == null || courier.Kind != UnitKind.Courier) return false;
            string err = null;
            var hero = p.Hero;
            if (courier.Dead) err = $"Your courier is dead ({Math.Max(0, (int)Math.Ceiling(courier.RespawnAt - Time))} s).";
            else if (hero == null || hero.Dead) err = "Your hero is dead.";
            else if (courier.Carried.Count == 0 && (hero.Stash == null || hero.Stash.All(i => i == null))) err = "Nothing to deliver: buy items first.";
            if (err != null) { EmitError(courier, err); return false; }
            courier.CourierState = courier.Carried.Count > 0 && !CourierAtHome(courier) ? CourierState.Delivering : CourierState.Fetching;
            return true;
        }

        private void UpdateCouriers()
        {
            foreach (var p in Players)
            {
                var c = p.Courier;
                if (c == null || c.Dead || c.Removed) continue;
                var hero = p.Hero;
                switch (c.CourierState)
                {
                    case CourierState.Idle:
                        if (!CourierAtHome(c)) { c.CourierState = CourierState.Returning; break; }
                        StashCarried(c, hero);
                        // Bots (and players who left) have their items brought to them.
                        if ((p.IsBot || p.Connection == PlayerConnection.Abandoned) && hero != null && !hero.Dead && !AtBase(hero)
                            && hero.Stash != null && hero.Stash.Any(i => i != null) && (Tick + c.Id) % 30 == 0)
                            TryCourierDeliver(c);
                        break;
                    case CourierState.Fetching:
                        if (!CourierAtHome(c)) { FlyTo(c, c.HomePosition, 0.3f); break; }
                        if (hero?.Stash != null)
                            for (int i = 0; i < hero.Stash.Length; i++)
                                if (hero.Stash[i] != null) { c.Carried.Add(hero.Stash[i]); hero.Stash[i] = null; }
                        c.CourierState = c.Carried.Count > 0 ? CourierState.Delivering : CourierState.Idle;
                        break;
                    case CourierState.Delivering:
                        if (hero == null || hero.Dead || c.Carried.Count == 0) { c.CourierState = CourierState.Returning; break; }
                        if (Vector2.Distance(c.Position, hero.Position) > Rules.CourierReach) { FlyTo(c, hero.Position, Rules.CourierReach * 0.6f); break; }
                        HandOver(c, hero);
                        c.CourierState = CourierState.Returning;
                        break;
                    case CourierState.Returning:
                        if (!CourierAtHome(c)) { FlyTo(c, c.HomePosition, 0.3f); break; }
                        StopMoving(c);
                        c.CurrentOrder = default;
                        c.CourierState = CourierState.Idle;
                        break;
                }
            }
        }

        private void FlyTo(Unit c, Vector2 point, float stop)
        {
            if (c.CurrentOrder.Type == OrderType.Move && Vector2.DistanceSquared(c.CurrentOrder.Point, point) < 0.8f * 0.8f) return;
            IssueOrder(c, Order.MoveTo(c.Id, point));
        }

        /// <summary>Carried items go into the hero's free inventory slots, then the backpack.</summary>
        private void HandOver(Unit c, Unit hero)
        {
            int given = 0;
            for (int i = 0; i < c.Carried.Count; i++)
            {
                var it = c.Carried[i];
                int free = Array.IndexOf(hero.Inventory, null);
                if (free >= 0) hero.Inventory[free] = it;
                else
                {
                    int bp = Array.IndexOf(hero.Backpack, null);
                    if (bp < 0) continue;
                    hero.Backpack[bp] = it;
                }
                c.Carried.RemoveAt(i--);
                given++;
            }
            if (given == 0) { EmitError(c, "Your hero's inventory is full."); return; }
            hero.StatsDirty = true;
            EmitPrivate(new SimEvent { Type = SimEventType.CourierDelivered, UnitId = c.Id, OtherId = hero.Id, Value = given, Point = hero.Position, Team = hero.Team }, c.Owner.Id);
            if (c.Carried.Count > 0) EmitError(c, "Your hero's inventory is full: the rest goes back to the stash.");
        }

        /// <summary>Items still carried at the fountain go back into the stash as it has room.</summary>
        private void StashCarried(Unit c, Unit hero)
        {
            if (c.Carried.Count == 0 || hero?.Stash == null) return;
            for (int i = 0; i < hero.Stash.Length && c.Carried.Count > 0; i++)
                if (hero.Stash[i] == null) { hero.Stash[i] = c.Carried[0]; c.Carried.RemoveAt(0); }
        }

        private void OnCourierDeath(Unit c)
        {
            c.RespawnAt = Time + Rules.CourierRespawnTime;
            c.CourierState = CourierState.Idle;
        }

        private void RespawnCourier(Unit c)
        {
            c.Dead = false;
            c.Position = c.HomePosition;
            c.LastPosition = c.HomePosition;
            c.Motion = null;
            SetAction(c, ActionState.Idle);
            c.CurrentOrder = default;
            c.OrderQueue.Clear();
            c.StatsDirty = true;
            c.RecomputeStats(Rules);
            c.Hp = c.Stats.MaxHp;
            c.CourierState = CourierState.Idle;
            RecomputeFlags(c);
            Emit(new SimEvent { Type = SimEventType.Respawn, UnitId = c.Id, Point = c.Position, Team = c.Team, PlayerId = -1 });
        }
    }
}
