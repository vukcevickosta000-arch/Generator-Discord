using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    /// <summary>
    /// RTS hero altars: recruit up to <see cref="RulesDef.RtsMaxHeroes"/> of the playable MOBA heroes, each hero once,
    /// with the price rising for every hero; revive fallen heroes (they never come back on their own). Heroes level
    /// from kills near them, like in the MOBA.
    /// </summary>
    public partial class Match
    {
        /// <summary>A training-queue entry that names a hero (a recruitment or a revival).</summary>
        public bool IsHeroEntry(string id) => id != null && Data.Heroes.ContainsKey(id) && !Data.Units.ContainsKey(id);

        /// <summary>Heroes a player has or will have: recruited (alive or dead) plus recruitments in a queue.</summary>
        public int HeroCount(Player p)
        {
            int n = p.RtsHeroes.Count;
            foreach (var b in Units)
                if (b.Owner == p && !b.Dead && b.TrainQueue != null)
                    foreach (var q in b.TrainQueue)
                        if (IsHeroEntry(q) && !p.RtsHeroes.Any(h => h.DefId == q)) n++;
            return n;
        }

        /// <summary>Price of the player's next recruitment: the first hero is cheapest, the third dearest.</summary>
        public (int Gold, int Lumber) NextHeroPrice(Player p)
        {
            int n = Math.Min(HeroCount(p), Math.Min(Rules.RtsHeroGold.Length, Rules.RtsHeroLumber.Length) - 1);
            return (Rules.RtsHeroGold[Math.Max(0, n)], Rules.RtsHeroLumber[Math.Max(0, n)]);
        }

        public int ReviveGold(Unit hero) => Rules.RtsReviveGold + Rules.RtsReviveGoldPerLevel * hero.Level;
        public float ReviveTime(Unit hero) => Rules.RtsReviveTime + Rules.RtsReviveTimePerLevel * hero.Level;

        private bool TryRecruitHero(Unit altar, string heroId)
        {
            var p = altar.Owner;
            string err = null;
            var existing = p.RtsHeroes.FirstOrDefault(h => h.DefId == heroId);
            bool queued = Units.Any(b => b.Owner == p && !b.Dead && b.TrainQueue != null && b.TrainQueue.Contains(heroId ?? ""));
            if (altar.UnderConstruction) err = "The building is not finished.";
            else if (heroId == null || !Data.Heroes.TryGetValue(heroId, out var hd) || !hd.Playable) err = "That hero can't be recruited.";
            else if (queued) err = existing != null ? "Already being revived." : "Already being recruited.";
            else if (existing != null && !existing.Dead) err = existing.Name + " already fights for you.";
            else if (existing == null && HeroCount(p) >= Rules.RtsMaxHeroes) err = $"You can lead at most {Rules.RtsMaxHeroes} heroes.";
            else if ((altar.TrainQueue?.Count ?? 0) >= Rules.RtsTrainQueueMax) err = "The training queue is full.";
            if (err == null)
            {
                var (gold, lumber) = existing != null ? (ReviveGold(existing), 0) : NextHeroPrice(p);
                if (p.Gold < gold) err = "Not enough blood-iron.";
                else if (p.Lumber < lumber) err = "Not enough lumber.";
                else if (p.SupplyUsed + Rules.RtsHeroSupply > p.SupplyCap)
                    err = p.SupplyCap >= Rules.RtsMaxSupply ? "Supply limit reached." : "Not enough supply. Build more supply structures.";
                else
                {
                    p.Gold -= gold;
                    p.Lumber -= lumber;
                    p.GoldSpent += gold;
                    p.LumberSpent += lumber;
                    p.SupplyUsed += Rules.RtsHeroSupply;
                    p.HeroPaid[heroId] = (gold, lumber);
                    (altar.TrainQueue ??= new List<string>()).Add(heroId);
                    return true;
                }
            }
            EmitError(altar, err);
            return false;
        }

        private void UpdateHeroTraining(Unit altar, float dt)
        {
            var p = altar.Owner;
            string id = altar.TrainQueue[0];
            var existing = p.RtsHeroes.FirstOrDefault(h => h.DefId == id);
            float time = existing != null ? ReviveTime(existing) : Rules.RtsHeroTrainTime;
            altar.TrainProgress += dt / Math.Max(0.1f, time);
            if (altar.TrainProgress < 1f) return;
            altar.TrainProgress = 0f;
            altar.TrainQueue.RemoveAt(0);
            p.HeroPaid.Remove(id);
            var hd = Data.Heroes[id];
            var toward = altar.HasRally ? altar.RallyPoint : new Vector2(Grid.WorldWidth, Grid.WorldHeight) * 0.5f;
            var dir = MathUtil.SafeNormalize(toward - altar.Position, MathUtil.FromAngle(altar.Facing));
            var pos = Grid.NearestWalkable(altar.Position + dir * (altar.Radius + hd.CollisionRadius + 0.4f));
            Unit hero;
            if (existing != null)
            {
                if (!existing.Dead) return;
                existing.ReviveAt = pos;
                RespawnHero(existing);
                hero = existing;
            }
            else
            {
                hero = CreateHero(hd, p, pos, MathUtil.AngleOf(dir));
                p.RtsHeroes.Add(hero);
                if (RtsAiOf(p) != null) hero.Brain = new RtsHeroBrain(p.BotDifficulty, Rng.NextULong());
                RegisterUnit(hero);
                p.UnitsTrained++;
                LogLine($"{p.Name} recruited {hd.Name}");
            }
            EmitPrivate(new SimEvent { Type = SimEventType.UnitTrained, UnitId = hero.Id, OtherId = altar.Id, Key = id, Point = pos, Team = altar.Team }, p.Id);
            if (existing == null) SendToRally(altar, hero);
            else if (altar.HasRally) IssueOrder(hero, Order.MoveTo(hero.Id, altar.RallyPoint));
        }

        /// <summary>
        /// RTS hero death: no gold changes and no respawn timer (a fallen hero waits for a revival at an altar).
        /// The killers' heroes nearby share the hero-kill experience.
        /// </summary>
        private void OnRtsHeroDeath(Unit victim, Unit killer, Player killerPlayer)
        {
            var vp = victim.Owner;
            if (vp != null) vp.Deaths++;
            victim.RespawnAt = float.MaxValue;
            victim.ReviveAt = null;
            victim.RecentHeroDamage.Clear();
            Team enemyTeam = OtherTeam(victim.Team);
            if (killerPlayer != null && killerPlayer.Team == enemyTeam) killerPlayer.Kills++;
            var table = Rules.Experience.HeroKillXpByLevel;
            int xp = table != null && table.Length > 0 ? table[Math.Min(table.Length - 1, victim.Level - 1)] : 100 + victim.Level * 20;
            ShareXp(victim, xp, enemyTeam);
            Emit(new SimEvent { Type = SimEventType.KillFeed, UnitId = victim.Id, OtherId = killer?.Id ?? 0, Key = "", Team = victim.Team, Value = killerPlayer?.Id ?? -1, PlayerId = -1 });
            LogLine($"{vp?.Name}'s {victim.Name} has fallen");
        }

        /// <summary>
        /// Experience for killing a player's RTS unit, shared by the killers' heroes near it. Units without a bounty in
        /// their data give <see cref="RulesDef.RtsXpPerSupply"/> per supply (at least one); buildings a flat amount, halls more.
        /// </summary>
        private int RtsKillXp(UnitDef d)
        {
            if (d.BountyXp > 0) return d.BountyXp;
            if (d.Kind == UnitKind.Building) return d.DropOffGold ? Rules.RtsHallXp : Rules.RtsBuildingXp;
            if (d.Kind == UnitKind.Resource) return 0;
            return Rules.RtsXpPerSupply * Math.Max(1, d.SupplyCost);
        }
    }
}
