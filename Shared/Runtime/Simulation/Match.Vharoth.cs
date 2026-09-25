using System;
using System.Collections.Generic;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    /// <summary>Progress of the Vharoth event. Sent in every snapshot header.</summary>
    public enum VharothPhase : byte
    {
        /// <summary>Before <c>vharothMinTime</c>: the seals are dormant dressing.</summary>
        Dormant,
        /// <summary>Phase 1: the seals glow and can be broken by channelling.</summary>
        Tremors,
        /// <summary>Phase 2: all seals are broken and Vharoth fights in his pit.</summary>
        Awakened,
        /// <summary>Phase 3: Vharoth survived long enough; night falls and vision shrinks until he dies.</summary>
        BloodMoon,
        /// <summary>Vharoth is dead; his corpse stays in the pit for the rest of the match.</summary>
        Slain,
    }

    public sealed partial class Match
    {
        public const string SealUnitId = "vharoth_seal";
        public const string VharothUnitId = "vharoth";
        public const string HeartItemId = "item_heart_of_vharoth";

        public VharothPhase VharothState = VharothPhase.Dormant;
        public int VharothSealsBroken;
        public float VharothAwakenedAt;
        public Team VharothSlainBy = Team.None;
        public Unit Vharoth;
        /// <summary>Vision multiplier applied to every unit (the Blood Moon shrinks it).</summary>
        public float VisionScale = 1f;

        private readonly List<Unit> _seals = new List<Unit>();

        private bool VharothAvailable =>
            !Config.DisableVharoth && Map.VharothSeals != null && Map.VharothSeals.Count > 0
            && Data.Units.ContainsKey(SealUnitId) && Data.Units.ContainsKey(VharothUnitId);

        /// <summary>
        /// Vharoth, the Blood Titan: the imprisoned world boss (GAME_DESIGN.md §8). Tremors wake the seals after
        /// <c>vharothMinTime</c>; breaking all four raises him; if he survives <c>vharothBloodMoonDelay</c> the Blood Moon
        /// rises; killing him grants the Heart of Vharoth.
        /// </summary>
        private void UpdateVharoth(float dt)
        {
            if (!VharothAvailable) return;
            switch (VharothState)
            {
                case VharothPhase.Dormant:
                    if (Time >= Rules.VharothMinTime) BeginTremors();
                    break;
                case VharothPhase.Awakened:
                    if (Vharoth != null && !Vharoth.Dead && Time - VharothAwakenedAt >= Rules.VharothBloodMoonDelay) BeginBloodMoon();
                    break;
            }
        }

        private void BeginTremors()
        {
            VharothState = VharothPhase.Tremors;
            var def = Data.Units[SealUnitId];
            _seals.Clear();
            foreach (var p in Map.VharothSeals)
            {
                var s = CreateUnit(def, Team.Neutral, p, 0f);
                s.Brain = null;
                _seals.Add(s);
            }
            Emit(new SimEvent { Type = SimEventType.VharothEvent, Key = "tremors", Point = Map.BossPit, Value = _seals.Count, PlayerId = -1 });
            Emit(new SimEvent { Type = SimEventType.Shake, Point = Map.BossPit, Value = 0.5f, PlayerId = -1 });
            Announce(AnnouncerKeys.VharothTremor, Team.None);
        }

        /// <summary>Handles deaths that belong to the event. Returns true when the normal bounty logic must be skipped.</summary>
        private bool OnVharothUnitDeath(Unit victim, Unit killer, Player killerPlayer)
        {
            if (victim.DefId == SealUnitId)
            {
                OnSealBroken(victim, killer, killerPlayer);
                return true;
            }
            if (victim == Vharoth)
            {
                OnVharothSlain(victim, killer, killerPlayer);
                return true;
            }
            return false;
        }

        private void OnSealBroken(Unit seal, Unit breaker, Player breakerPlayer)
        {
            VharothSealsBroken++;
            Team team = breakerPlayer?.Team ?? breaker?.Team ?? Team.None;
            if (team == Team.Dawn || team == Team.Dusk)
                foreach (var p in Players)
                    if (p.Team == team) GiveGold(p, Rules.VharothSealGold, seal.Position, false);
            Emit(new SimEvent { Type = SimEventType.VharothEvent, Key = "seal_broken", Point = seal.Position, UnitId = seal.Id, OtherId = breaker?.Id ?? 0, Team = team, Value = VharothSealsBroken, PlayerId = -1 });
            Emit(new SimEvent { Type = SimEventType.EffectVisual, Key = "seal_break", Point = seal.Position, PlayerId = -1 });
            Announce(AnnouncerKeys.VharothSealBroken, Team.None);
            if (VharothState == VharothPhase.Tremors && VharothSealsBroken >= _seals.Count) Awaken();
        }

        private void Awaken()
        {
            VharothState = VharothPhase.Awakened;
            VharothAwakenedAt = Time;
            Vharoth = CreateUnit(Data.Units[VharothUnitId], Team.Neutral, Grid.NearestWalkable(Map.BossPit), MathUtil.AngleOf(new Vector2(0, -1)));
            Vharoth.Brain = new BossBrain(Map.BossPit, Rules.VharothLeash);
            Emit(new SimEvent { Type = SimEventType.VharothEvent, Key = "awakened", Point = Map.BossPit, UnitId = Vharoth.Id, PlayerId = -1 });
            Emit(new SimEvent { Type = SimEventType.Shake, Point = Map.BossPit, Value = 1.2f, PlayerId = -1 });
            Announce(AnnouncerKeys.VharothAwakened, Team.None);
        }

        private void BeginBloodMoon()
        {
            VharothState = VharothPhase.BloodMoon;
            ForcedNightUntil = float.MaxValue;
            VisionScale = Rules.VharothBloodMoonVision;
            Emit(new SimEvent { Type = SimEventType.VharothEvent, Key = "blood_moon", Point = Map.BossPit, PlayerId = -1 });
            Announce(AnnouncerKeys.VharothBloodMoon, Team.None);
        }

        private void OnVharothSlain(Unit boss, Unit killer, Player killerPlayer)
        {
            VharothState = VharothPhase.Slain;
            if (ForcedNightUntil == float.MaxValue) ForcedNightUntil = Time;
            VisionScale = 1f;
            Team team = killerPlayer?.Team ?? killer?.Team ?? Team.None;
            if (team != Team.Dawn && team != Team.Dusk) team = Team.None;
            VharothSlainBy = team;
            var def = boss.UnitDef;
            if (team != Team.None && def != null)
            {
                foreach (var p in Players)
                    if (p.Team == team) GiveGold(p, def.TeamBountyGold, boss.Position, false);
                ShareXpToTeam(team, def.BountyXp);
                if (killerPlayer != null && killerPlayer.Team == team) GiveGold(killerPlayer, def.BountyGoldMin, boss.Position, true);
                GrantHeart(killerPlayer != null && killerPlayer.Team == team ? killerPlayer : null, team, boss.Position);
            }
            Emit(new SimEvent { Type = SimEventType.VharothEvent, Key = "slain", Point = boss.Position, UnitId = boss.Id, OtherId = killer?.Id ?? 0, Team = team, PlayerId = -1 });
            Announce(AnnouncerKeys.VharothSlain, Team.None);
        }

        /// <summary>Gives the Heart of Vharoth to the killer's hero, or to the first living hero of the team with room.</summary>
        private void GrantHeart(Player killer, Team team, Vector2 at)
        {
            if (!Data.Items.TryGetValue(HeartItemId, out var heart)) return;
            var candidates = new List<Player>();
            if (killer?.Hero != null) candidates.Add(killer);
            foreach (var p in Players) if (p.Team == team && p != killer && p.Hero != null) candidates.Add(p);
            foreach (var p in candidates)
            {
                var hero = p.Hero;
                int slot = -1;
                for (int i = 0; i < hero.Inventory.Length && slot < 0; i++) if (hero.Inventory[i] == null) slot = i;
                for (int i = 0; i < hero.Backpack.Length && slot < 0; i++) if (hero.Backpack[i] == null) slot = BackpackSlotBase + i;
                for (int i = 0; i < hero.Stash.Length && slot < 0; i++) if (hero.Stash[i] == null) slot = StashSlotBase + i;
                if (slot < 0) continue;
                SetItemAt(hero, slot, CreateItemInstance(heart, p));
                hero.StatsDirty = true;
                Emit(new SimEvent { Type = SimEventType.ItemPurchased, UnitId = hero.Id, Key = heart.Id, Value = 0, PlayerId = -1 });
                Emit(new SimEvent { Type = SimEventType.EffectVisual, Key = "heart_granted", UnitId = hero.Id, Point = at, PlayerId = -1 });
                return;
            }
        }
    }

    /// <summary>
    /// Vharoth's AI: fights whoever enters his pit (any team), prefers heroes, never follows past the leash, and heals to
    /// full when left alone (the classic boss reset).
    /// </summary>
    public sealed class BossBrain : IUnitBrain
    {
        private readonly Vector2 _home;
        private readonly float _leash;
        private float _calmSince = -1f;
        private readonly List<Unit> _near = new List<Unit>(32);

        public BossBrain(Vector2 home, float leash) { _home = home; _leash = leash; }

        public void Think(Match m, Unit u, float dt)
        {
            if (u.Dead) return;
            var current = u.CurrentOrder.Type == OrderType.AttackUnit ? m.GetUnit(u.CurrentOrder.TargetId) : null;
            if (current != null && (current.Dead || Vector2.Distance(current.Position, _home) > _leash || !m.IsVisibleTo(current, u.Team)))
                current = null;

            if (current == null)
            {
                Unit best = null;
                float bestScore = float.MaxValue;
                _near.Clear();
                m.UnitsInRadius(_home, _leash, _near);
                foreach (var t in _near)
                {
                    if (t.Team == u.Team || t.Team == Team.Neutral || t.Invulnerable || t.IsStructure || t.Kind == UnitKind.Ward) continue;
                    if (!m.CanAttackTarget(u, t, out bool deny) || deny) continue;
                    float score = Vector2.Distance(t.Position, u.Position) - (t.IsHero ? 6f : 0f);
                    if (score < bestScore) { bestScore = score; best = t; }
                }
                current = best;
                if (current != null) m.IssueOrder(u, Order.Attack(u.Id, current.Id));
            }

            if (current != null) { _calmSince = -1f; return; }

            // Nobody in the pit: walk home and regenerate once calm for a few seconds.
            if (Vector2.Distance(u.Position, _home) > 1.5f)
            {
                if (u.CurrentOrder.Type != OrderType.Move) m.IssueOrder(u, Order.MoveTo(u.Id, _home));
                return;
            }
            if (u.CurrentOrder.Type != OrderType.None) m.IssueOrder(u, Order.StopOrder(u.Id));
            if (_calmSince < 0f) _calmSince = m.Time;
            if (m.Time - _calmSince > 5f && u.Hp < u.Stats.MaxHp) u.Hp = Math.Min(u.Stats.MaxHp, u.Hp + u.Stats.MaxHp * 0.04f * dt);
        }
    }
}
