using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    public struct DamageInfo
    {
        public Unit Source;
        public Unit Target;
        public float Amount;
        public DamageType Type;
        public bool IsSpell;
        public bool IsAttack;
        public bool IsCrit;
        public AbilityDef Ability;
        public float HealSourcePct;
        public bool PiercesMagicImmunity;
        public bool IgnoreShields;
        public bool NoTriggers;
        public string Vfx;
    }

    public sealed partial class Match
    {
        // ================================================================== damage

        /// <summary>Central damage pipeline. Returns the damage actually dealt after mitigation.</summary>
        public float DealDamage(DamageInfo d)
        {
            var t = d.Target;
            var src = d.Source;
            if (t == null || t.Dead || d.Amount <= 0f) return 0f;
            if (t.Invulnerable) return 0f;
            if (d.Type == DamageType.Magical && t.IsMagicImmune && !d.PiercesMagicImmunity) return 0f;

            float amount = d.Amount;
            if (src != null)
            {
                amount *= 1f + src.Stats.OutgoingDamagePct;
                if (d.IsSpell) amount *= 1f + src.Stats.SpellAmp;
            }
            amount *= 1f + t.Stats.DamageTakenPct;

            switch (d.Type)
            {
                case DamageType.Physical: amount *= MathUtil.ArmorMultiplier(t.Stats.Armor); break;
                case DamageType.Magical: amount *= 1f - MathUtil.Clamp(t.Stats.MagicResist, -1f, 0.95f); break;
            }

            if (t.IsStructure && src != null)
            {
                if (src.UnitDef != null) amount *= src.UnitDef.SiegeMultiplier;
                if (t.UnitDef != null && t.UnitDef.BackdoorProtection > 0 && !EnemyCreepsNear(t, 14f)) amount *= 1f - t.UnitDef.BackdoorProtection;
                if (d.IsSpell) amount *= 0.5f; // spells deal reduced damage to structures
            }

            // Shields.
            if (!d.IgnoreShields)
            {
                for (int i = t.Statuses.Count - 1; i >= 0 && amount > 0; i--)
                {
                    var s = t.Statuses[i];
                    if (s.ShieldRemaining <= 0f) continue;
                    if (s.Def.ShieldType.HasValue && s.Def.ShieldType.Value != d.Type) continue;
                    float absorbed = Math.Min(s.ShieldRemaining, amount);
                    s.ShieldRemaining -= absorbed;
                    amount -= absorbed;
                    if (s.ShieldRemaining <= 0.01f) RemoveStatus(t, s, expired: false);
                }
            }
            if (amount <= 0f) return 0f;

            bool lethal = amount >= t.Hp && !t.HasFlag(StatusFlags.CannotDie);
            float dealt = lethal ? t.Hp : Math.Min(amount, t.HasFlag(StatusFlags.CannotDie) ? Math.Max(0f, t.Hp - 1f) : amount);
            t.Hp -= dealt;
            t.TotalDamageTaken += dealt;
            t.LastAttackedTime = Time;
            if (src != null) t.LastAttackerId = src.Id;

            // Statistics.
            var srcPlayer = src?.Owner;
            if (srcPlayer != null && src.Team != t.Team)
            {
                if (t.IsHero && !t.IsIllusion) srcPlayer.HeroDamage += dealt;
                else if (t.IsStructure) srcPlayer.BuildingDamage += dealt;
            }
            if (t.IsHero && t.Owner != null) t.Owner.DamageTaken += dealt;
            if (t.IsHero && srcPlayer != null && src.Team != t.Team) RecordAssist(t, srcPlayer.Id);

            byte flags = 0;
            if (d.IsCrit) flags |= SimEvent.FlagCrit;
            if (d.Type == DamageType.Magical) flags |= SimEvent.FlagMagical;
            if (d.Type == DamageType.Pure) flags |= SimEvent.FlagPure;
            if (d.Type == DamageType.Physical) flags |= SimEvent.FlagPhysical;
            if (d.IsSpell) flags |= SimEvent.FlagSpell;
            Emit(new SimEvent { Type = SimEventType.Damage, UnitId = t.Id, OtherId = src?.Id ?? 0, Value = dealt, Flags = flags, Key = d.Vfx, PlayerId = -1 });

            // Sustain.
            if (src != null && !src.Dead && dealt > 0)
            {
                float heal = 0f;
                if (d.IsAttack && src.Stats.Lifesteal > 0 && !t.IsStructure) heal += dealt * src.Stats.Lifesteal;
                if (d.IsSpell && src.Stats.SpellVamp > 0 && !t.IsStructure) heal += dealt * src.Stats.SpellVamp * (t.IsHero ? 1f : 0.2f);
                if (d.HealSourcePct > 0) heal += dealt * d.HealSourcePct;
                if (heal > 0) Heal(src, heal, src);
            }

            // Channels break on damage only for specific abilities; disables break them via statuses.
            if (!d.NoTriggers)
            {
                FireTriggers(t, TriggerType.DamageTaken, src, dealt);
                if (src != null) FireTriggers(src, TriggerType.DamageDealt, t, dealt);
                if (t.IsHero && t.HpFraction < 0.25f) FireTriggers(t, TriggerType.LowHealth, src, dealt);
            }

            if (t.Hp <= 0f && !t.Dead) KillUnit(t, src);
            return dealt;
        }

        private bool EnemyCreepsNear(Unit structure, float radius)
        {
            var list = RentList();
            UnitsInRadius(structure.Position, radius, list);
            bool any = false;
            foreach (var u in list) if (u.Team != structure.Team && (u.IsCreep || u.Kind == UnitKind.Summon)) { any = true; break; }
            ReturnList(list);
            return any;
        }

        private void RecordAssist(Unit victim, int playerId)
        {
            for (int i = 0; i < victim.RecentHeroDamage.Count; i++)
            {
                if (victim.RecentHeroDamage[i].PlayerId == playerId)
                {
                    victim.RecentHeroDamage[i] = new DamageRecord { PlayerId = playerId, Time = Time };
                    return;
                }
            }
            victim.RecentHeroDamage.Add(new DamageRecord { PlayerId = playerId, Time = Time });
        }

        // ================================================================== attacks

        /// <summary>True if 'attacker' may right-click 'target' (enemy, or a deniable ally).</summary>
        public bool CanAttackTarget(Unit attacker, Unit target, out bool isDeny)
        {
            isDeny = false;
            if (target == null || !target.IsAlive || target == attacker) return false;
            if (target.HasFlag(StatusFlags.Untargetable) || target.HasFlag(StatusFlags.Airborne) && target.Motion != null && target.Motion.Invulnerable) return false;
            if (attacker.Team != Team.Neutral && target.Team != Team.Neutral && !IsVisibleTo(target, attacker.Team)) return false;
            if (target.Team != attacker.Team) return true;
            // Denies.
            if (target.IsCreep && target.HpFraction < Rules.CreepDenyThreshold) { isDeny = true; return true; }
            if (target.IsStructure && target.UnitDef != null && target.UnitDef.DenyThreshold > 0 && target.HpFraction < target.UnitDef.DenyThreshold) { isDeny = true; return true; }
            if (target.Kind == UnitKind.Ward) { isDeny = true; return true; }
            if (target.IsHero && target.Statuses.Any(s => s.Def.Id == "deniable")) { isDeny = true; return true; }
            return false;
        }

        public bool IsVisibleTo(Unit u, Team team)
        {
            if (team == Team.None || team == Team.Neutral || u.Team == team) return true;
            return u.VisibleTo[(int)team];
        }

        public float AttackReach(Unit attacker, Unit target) => attacker.Stats.AttackRange + attacker.Radius + target.Radius;

        /// <summary>Runs the attack state machine for a unit that wants to attack 'target'. Returns false if impossible.</summary>
        private bool ProcessAttack(Unit u, Unit target, float dt, bool allowMove)
        {
            if (!CanAttackTarget(u, target, out _)) return false;
            if (!u.CanAttack) return true;
            float reach = AttackReach(u, target);
            float dist = Vector2.Distance(u.Position, target.Position);

            if (u.Action == ActionState.AttackWindup)
            {
                // Cancel if the target escaped well beyond range during the windup.
                if (dist > reach + 3f) { SetAction(u, ActionState.Idle); return true; }
                FaceTowards(u, target.Position, dt);
                u.ActionTimer -= dt;
                if (u.ActionTimer <= 0f) LaunchAttack(u, target);
                return true;
            }
            if (u.Action == ActionState.AttackBackswing)
            {
                u.ActionTimer -= dt;
                if (u.ActionTimer <= 0f) SetAction(u, ActionState.Idle);
                if (dist <= reach) return true;
                SetAction(u, ActionState.Idle);
            }

            if (dist > reach)
            {
                if (!allowMove) return true;
                MoveTowardsUnit(u, target, dt, reach * 0.92f);
                return true;
            }
            StopMoving(u);
            bool faced = FaceTowards(u, target.Position, dt);
            if (!faced) return true;
            if (u.AttackCooldown > 0f) { if (u.Action != ActionState.Idle) SetAction(u, ActionState.Idle); return true; }

            // Begin a new attack.
            u.AttackTargetId = target.Id;
            u.AttackCooldown = u.Stats.AttackTime;
            u.AttackIsCrit = false;
            SetAction(u, ActionState.AttackWindup);
            u.ActionTimer = u.Stats.AttackPoint;
            u.ActionTargetId = target.Id;
            FireTriggers(u, TriggerType.AttackStart, target, 0f, true);
            if (u.IsHero && target.IsHero && u.Team != target.Team) DrawAggro(u, target);
            Emit(new SimEvent { Type = SimEventType.AttackStart, UnitId = u.Id, OtherId = target.Id, Value = u.Stats.AttackPoint, Value2 = u.Stats.AttackTime, PlayerId = -1 });
            if (u.ActionTimer <= 0f) LaunchAttack(u, target);
            return true;
        }

        private void LaunchAttack(Unit u, Unit target)
        {
            SetAction(u, ActionState.AttackBackswing);
            u.ActionTimer = u.Stats.AttackBackswing;
            var info = RollAttack(u, target);
            if (u.AttackType == AttackType.Ranged && u.ProjectileSpeed > 0f)
            {
                var p = new Projectile
                {
                    Id = _nextProjectileId++,
                    Kind = ProjectileKind.Tracking,
                    Source = u,
                    Target = target,
                    Position = u.Position + MathUtil.FromAngle(u.Facing) * (u.Radius + 0.2f),
                    Speed = u.ProjectileSpeed,
                    IsAttack = true,
                    Attack = info,
                    Dodgeable = true,
                    Visual = u.HeroDef?.ProjectileVisual ?? u.UnitDef?.ProjectileVisual ?? "attack_default",
                    Team = u.Team,
                };
                Projectiles.Add(p);
                Emit(new SimEvent { Type = SimEventType.ProjectileLaunch, UnitId = u.Id, OtherId = target.Id, Key = p.Visual, Point = p.Position, Value = p.Speed, Value2 = p.Id, PlayerId = -1 });
            }
            else
            {
                ApplyAttackHit(u, target, info);
            }
        }

        /// <summary>Classic aggro rule: attacking an enemy hero near its creeps/towers makes them target you.</summary>
        private void DrawAggro(Unit attacker, Unit victim)
        {
            var list = RentList();
            UnitsInRadius(attacker.Position, 12f, list);
            foreach (var d in list)
            {
                if (d.Team != victim.Team || d.Dead) continue;
                if (d.IsCreep && Vector2.Distance(d.Position, attacker.Position) <= d.AcquisitionRange)
                { d.AggroTargetId = attacker.Id; d.AggroUntil = Time + 2.3f; }
                else if (d.Kind == UnitKind.Tower && Vector2.Distance(d.Position, attacker.Position) <= AttackReach(d, attacker))
                { d.AggroTargetId = attacker.Id; d.AggroUntil = Time + 2.5f; }
            }
            ReturnList(list);
        }

        private DamageInfo RollAttack(Unit u, Unit target)
        {
            var s = u.Stats;
            float dmg = Rng.Range(s.DamageMin, s.DamageMax) + s.BonusDamage;
            bool crit = false;
            if (s.CritChance > 0 && !target.IsStructure)
            {
                var st = u.GetTriggerState(CritTriggerKey);
                if (st.Prd.Roll(Rng, Math.Min(1f, s.CritChance))) { crit = true; dmg *= s.CritMultiplier; }
            }
            return new DamageInfo { Source = u, Target = target, Amount = dmg, Type = DamageType.Physical, IsAttack = true, IsCrit = crit };
        }

        private static readonly TriggerDef CritTriggerKey = new TriggerDef();
        private static readonly TriggerDef EvasionTriggerKey = new TriggerDef();

        internal void ApplyAttackHit(Unit attacker, Unit target, DamageInfo info)
        {
            if (target == null || target.Dead || attacker == null) return;
            CanAttackTarget(attacker, target, out bool deny);
            // Evasion / blind.
            float missChance = 0f;
            if (!target.IsStructure) missChance = target.Stats.Evasion;
            if (attacker.HasFlag(StatusFlags.Blinded)) missChance = 1f - (1f - missChance) * 0.5f;
            if (attacker.Team != target.Team && missChance > 0f && !attacker.HasFlag(StatusFlags.TrueSight))
            {
                var st = target.GetTriggerState(EvasionTriggerKey);
                if (st.Prd.Roll(Rng, missChance))
                {
                    Emit(new SimEvent { Type = SimEventType.Miss, UnitId = target.Id, OtherId = attacker.Id, PlayerId = -1 });
                    return;
                }
            }
            info.Target = target;
            if (info.IsCrit) Emit(new SimEvent { Type = SimEventType.Crit, UnitId = target.Id, OtherId = attacker.Id, Value = info.Amount, PlayerId = -1 });
            Emit(new SimEvent { Type = SimEventType.AttackLanded, UnitId = attacker.Id, OtherId = target.Id, Flags = (byte)(info.IsCrit ? SimEvent.FlagCrit : 0), PlayerId = -1 });

            if (!deny) FireTriggers(attacker, TriggerType.AttackLanded, target, info.Amount, true);
            if (target.Dead) return;
            DealDamage(info);
            if (!target.Dead && !deny) FireTriggers(target, TriggerType.Attacked, attacker, info.Amount, true);
            if (attacker.Owner != null && attacker.IsHero) foreach (var it in attacker.EquippedItems()) it.UsedSincePurchase = true;
        }

        // ================================================================== death

        public void KillUnit(Unit victim, Unit killer)
        {
            if (victim.Dead) return;
            victim.Dead = true;
            victim.Hp = 0;
            victim.DeathTime = Time;
            victim.Motion = null;
            victim.Path.Clear();
            victim.OrderQueue.Clear();
            victim.CurrentOrder = default;
            if (victim.Action == ActionState.Channeling) EndChannel(victim, true);
            SetAction(victim, ActionState.Dead);
            for (int i = victim.Statuses.Count - 1; i >= 0; i--)
                if (victim.Statuses[i].Def.RemoveOnDeath) victim.Statuses.RemoveAt(i);
            victim.StatsDirty = true;
            RecomputeFlags(victim);

            bool deny = killer != null && killer.Team == victim.Team && killer != victim;
            var killerPlayer = killer?.Owner ?? killer?.Summoner?.Owner;

            Emit(new SimEvent
            {
                Type = SimEventType.Death,
                UnitId = victim.Id,
                OtherId = killer?.Id ?? 0,
                Point = victim.Position,
                Flags = (byte)((deny ? SimEvent.FlagDeny : 0) | (victim.IsHero ? SimEvent.FlagHero : 0)),
                Team = victim.Team,
                PlayerId = -1,
            });

            // Nearby-death passives (e.g. Vorak's Lord of the Feast).
            var near = RentList();
            UnitsInRadius(victim.Position, 12f, near);
            var copy = near.ToArray();
            ReturnList(near);
            foreach (var n in copy) if (n != victim) FireTriggers(n, TriggerType.NearbyDeath, victim, victim.IsHero ? 1f : 0f);
            if (killer != null) FireTriggers(killer, TriggerType.Kill, victim, 0f);
            FireTriggers(victim, TriggerType.Death, killer, 0f);

            if (victim.IsHero && !victim.IsIllusion) OnHeroDeath(victim, killer, killerPlayer, deny);
            else if (victim.IsStructure) OnStructureDeath(victim, killer, killerPlayer, deny);
            else OnUnitDeath(victim, killer, killerPlayer, deny);

            if ((victim.IsCreep || victim.IsNeutral) && !victim.Flying)
                Corpses.Add(new Corpse { Position = victim.Position, UnitId = victim.DefId, Team = victim.Team, Expires = Time + 30f });
            if (victim.Kind == UnitKind.Ward && killerPlayer != null && deny == false) killerPlayer.WardsDestroyed++;
        }

        private void OnUnitDeath(Unit victim, Unit killer, Player killerPlayer, bool deny)
        {
            var def = victim.UnitDef;
            if (def == null) return;
            int xp = def.BountyXp;
            if (deny)
            {
                if (killerPlayer != null && killer.IsHero) { killerPlayer.Denies++; Emit(new SimEvent { Type = SimEventType.Deny, UnitId = victim.Id, OtherId = killer.Id, Point = victim.Position, PlayerId = -1 }); }
                ShareXp(victim, (int)(xp * Rules.DenyXpFactor), OtherTeam(victim.Team));
                return;
            }
            if (killerPlayer != null && killer.Team != victim.Team)
            {
                int gold = Rng.Range(def.BountyGoldMin, def.BountyGoldMax + 1) + def.GoldPerUpgrade * victim.CreepUpgradeLevel;
                if (killer.IsHero || killer.Summoner != null || killer.Kind == UnitKind.Summon)
                {
                    GiveGold(killerPlayer, gold, victim.Position, true);
                    if (victim.IsCreep || victim.IsNeutral) killerPlayer.LastHits++;
                    if (victim.IsNeutral) killerPlayer.NeutralKills++;
                    Emit(new SimEvent { Type = SimEventType.LastHitGold, UnitId = victim.Id, OtherId = killer.Id, Value = gold, Point = victim.Position, PlayerId = killerPlayer.Id });
                }
            }
            Team xpTeam = killer != null && killer.Team != Team.Neutral ? killer.Team : OtherTeam(victim.Team);
            if (victim.Team == Team.Neutral && killer != null) xpTeam = killer.Team;
            ShareXp(victim, xp, xpTeam);
        }

        private void OnStructureDeath(Unit victim, Unit killer, Player killerPlayer, bool deny)
        {
            var def = victim.UnitDef;
            Team enemy = OtherTeam(victim.Team);
            if (!deny && def != null)
            {
                foreach (var p in Players)
                    if (p.Team == enemy) GiveGold(p, def.TeamBountyGold, victim.Position, false);
                if (killerPlayer != null && killer.Team == enemy)
                {
                    int bonus = Rng.Range(def.BountyGoldMin, def.BountyGoldMax + 1);
                    GiveGold(killerPlayer, bonus, victim.Position, true);
                    if (victim.Kind == UnitKind.Tower) killerPlayer.TowersDestroyed++;
                }
                ShareXpToTeam(enemy, def.BountyXp);
            }
            else if (deny && def != null)
            {
                foreach (var p in Players) if (p.Team == enemy) GiveGold(p, def.TeamBountyGold / 2, victim.Position, false);
            }
            Emit(new SimEvent { Type = SimEventType.StructureDestroyed, UnitId = victim.Id, OtherId = killer?.Id ?? 0, Key = victim.DefId, Team = victim.Team, Point = victim.Position, Flags = (byte)(deny ? SimEvent.FlagDeny : 0), PlayerId = -1 });
            if (victim.Kind == UnitKind.Tower)
            {
                Announce(AnnouncerKeys.TowerDestroyedAlly, victim.Team);
                Announce(AnnouncerKeys.TowerDestroyedEnemy, enemy);
            }
            else if (victim.Kind == UnitKind.Barracks)
            {
                Announce(AnnouncerKeys.BarracksDestroyedAlly, victim.Team);
                Announce(AnnouncerKeys.BarracksDestroyedEnemy, enemy);
                OnBarracksDestroyed(victim);
            }
            // Unlock structures that this one was protecting.
            foreach (var u in Units) if (u.ProtectedBy != null && u.ProtectedBy.Contains(victim)) RecomputeFlags(u);
        }

        private void OnHeroDeath(Unit victim, Unit killer, Player killerPlayer, bool deny)
        {
            var vp = victim.Owner;
            if (vp != null)
            {
                vp.Deaths++;
                float respawn = Rules.RespawnBase + Rules.RespawnPerLevel * victim.Level;
                victim.RespawnAt = Time + respawn;
                int loss = (int)Math.Min(vp.Gold, Rules.DeathGoldLossPerLevel * victim.Level);
                if (!deny)
                {
                    vp.Gold -= loss;
                    if (loss > 0) Emit(new SimEvent { Type = SimEventType.GoldChange, Value = -loss, UnitId = victim.Id, PlayerId = vp.Id });
                }
            }
            int victimStreak = vp?.KillStreak ?? 0;
            if (vp != null) vp.KillStreak = 0;

            Team enemyTeam = OtherTeam(victim.Team);
            // Assisting players: damaged the victim recently or are nearby enemy heroes.
            var assisters = new HashSet<Player>();
            foreach (var rec in victim.RecentHeroDamage)
                if (Time - rec.Time <= Rules.AssistWindow) { var p = GetPlayer(rec.PlayerId); if (p != null && p.Team == enemyTeam) assisters.Add(p); }
            var near = RentList();
            UnitsInRadius(victim.Position, Rules.AssistRadius, near);
            foreach (var n in near) if (n.IsHero && !n.IsIllusion && n.Team == enemyTeam && n.Owner != null) assisters.Add(n.Owner);
            ReturnList(near);
            victim.RecentHeroDamage.Clear();

            bool killedByEnemyHeroSide = killerPlayer != null && killerPlayer.Team == enemyTeam && !deny;
            if (killedByEnemyHeroSide) assisters.Remove(killerPlayer);

            if (killedByEnemyHeroSide)
            {
                killerPlayer.Kills++;
                killerPlayer.KillStreak++;
                killerPlayer.BestKillStreak = Math.Max(killerPlayer.BestKillStreak, killerPlayer.KillStreak);
                TeamKills[(int)killerPlayer.Team]++;
                int streakBonus = Rules.StreakBonusGold[Math.Min(victimStreak, Rules.StreakBonusGold.Length - 1)];
                int gold = Rules.HeroKillBaseGold + Rules.HeroKillGoldPerLevel * victim.Level + streakBonus;
                if (!_firstBloodTaken) { gold += Rules.FirstBloodBonus; _firstBloodTaken = true; Announce(AnnouncerKeys.FirstBlood, Team.None, killerPlayer.Id, killer.Id); }
                GiveGold(killerPlayer, gold, victim.Position, true);
                Emit(new SimEvent { Type = SimEventType.LastHitGold, UnitId = victim.Id, OtherId = killer?.Id ?? 0, Value = gold, Point = victim.Position, PlayerId = killerPlayer.Id });
                if (victimStreak >= 3) Announce(AnnouncerKeys.Shutdown, Team.None, killerPlayer.Id, killer?.Id ?? 0);
                AnnounceStreaks(killerPlayer, killer);
            }
            else if (!deny && killer != null && killer.Team == enemyTeam)
            {
                TeamKills[(int)enemyTeam]++; // creep / tower kill still counts for the team score
            }

            if (assisters.Count > 0 && !deny)
            {
                int pool = Rules.AssistGoldBase + Rules.AssistGoldPerLevel * victim.Level;
                int each = pool / assisters.Count + 20;
                foreach (var a in assisters) { a.Assists++; GiveGold(a, each, victim.Position, true); }
            }

            // Hero kill XP shared among nearby enemy heroes.
            var table = Rules.Experience.HeroKillXpByLevel;
            int xp = table != null && table.Length > 0 ? table[Math.Min(table.Length - 1, victim.Level - 1)] : 100 + victim.Level * 20;
            ShareXp(victim, deny ? xp / 2 : xp, enemyTeam);

            Emit(new SimEvent
            {
                Type = SimEventType.KillFeed,
                UnitId = victim.Id,
                OtherId = killer?.Id ?? 0,
                Key = string.Join(",", assisters.Select(a => a.Id)),
                Team = victim.Team,
                Value = killerPlayer?.Id ?? -1,
                PlayerId = -1,
            });

            // Team wipe: all heroes of the victim's team are dead.
            var teamHeroes = Players.Where(p => p.Team == victim.Team && p.Hero != null).Select(p => p.Hero).ToList();
            if (teamHeroes.Count >= 3 && teamHeroes.All(h => h.Dead)) Announce(AnnouncerKeys.TeamWipe, enemyTeam);
        }

        private void AnnounceStreaks(Player p, Unit killer)
        {
            // Multi-kills (window 14 s between kills).
            if (Time - p.LastKillTime <= 14f) p.MultiKillCount++;
            else p.MultiKillCount = 1;
            p.LastKillTime = Time;
            switch (p.MultiKillCount)
            {
                case 2: Announce(AnnouncerKeys.DoubleKill, Team.None, p.Id, killer?.Id ?? 0); break;
                case 3: Announce(AnnouncerKeys.TripleKill, Team.None, p.Id, killer?.Id ?? 0); break;
                case 4: Announce(AnnouncerKeys.QuadKill, Team.None, p.Id, killer?.Id ?? 0); break;
                case 5: Announce(AnnouncerKeys.Annihilation, Team.None, p.Id, killer?.Id ?? 0); break;
            }
            if (p.MultiKillCount <= 1 && p.KillStreak >= 3)
                Announce(AnnouncerKeys.StreakPrefix + Math.Min(10, p.KillStreak), Team.None, p.Id, killer?.Id ?? 0);
        }

        public static Team OtherTeam(Team t) => t == Team.Dawn ? Team.Dusk : t == Team.Dusk ? Team.Dawn : Team.None;

        // ================================================================== economy

        public void GiveGold(Player p, int amount, Vector2 at, bool earned)
        {
            if (p == null || amount == 0) return;
            p.Gold = Math.Max(0, p.Gold + amount);
            if (amount > 0) p.GoldEarned += amount;
            Events.Add(new SimEvent { Type = SimEventType.GoldChange, Tick = Tick, Value = amount, Point = at, UnitId = p.Hero?.Id ?? 0, PlayerId = p.Id });
        }

        private float _passiveGoldAccum;

        private void UpdatePassiveIncome(float dt)
        {
            _passiveGoldAccum += Rules.PassiveGoldPerSecond * dt;
            if (_passiveGoldAccum < 1f) return;
            int whole = (int)_passiveGoldAccum;
            _passiveGoldAccum -= whole;
            foreach (var p in Players)
            {
                if (p.Connection == PlayerConnection.Abandoned) continue;
                p.Gold += whole;
                p.GoldEarned += whole;
            }
        }

        public int NetWorth(Player p)
        {
            int nw = p.Gold;
            var h = p.Hero;
            if (h?.Inventory != null)
            {
                foreach (var it in h.Inventory) if (it != null) nw += it.Def.TotalCost;
                foreach (var it in h.Stash) if (it != null) nw += it.Def.TotalCost;
                foreach (var it in h.Backpack) if (it != null) nw += it.Def.TotalCost;
            }
            return nw;
        }

        public int BuybackCost(Player p)
        {
            int lvl = p.Hero?.Level ?? 1;
            return (int)(Rules.BuybackBaseCost + Rules.BuybackCostPerLevel * lvl * lvl / 2f + Rules.BuybackCostPerMinute * MatchSeconds / 60f);
        }

        private void TryBuyback(Player p)
        {
            var h = p.Hero;
            if (h == null || !h.Dead) return;
            if (Time < p.BuybackCooldownUntil) { EmitError(h, "Buyback is on cooldown."); return; }
            int cost = BuybackCost(p);
            if (p.Gold < cost) { EmitError(h, "Not enough gold to buy back."); return; }
            p.Gold -= cost;
            p.GoldSpent += cost;
            p.Buybacks++;
            p.BuybackCooldownUntil = Time + Rules.BuybackCooldown;
            RespawnHero(h);
            Emit(new SimEvent { Type = SimEventType.Buyback, UnitId = h.Id, Team = h.Team, PlayerId = -1 });
        }

        // ================================================================== experience

        private void ShareXp(Unit victim, int xp, Team team)
        {
            if (xp <= 0 || team == Team.None || team == Team.Neutral) return;
            var near = RentList();
            UnitsInRadius(victim.Position, Rules.XpShareRadius, near);
            var heroes = near.Where(u => u.IsHero && !u.IsIllusion && u.Team == team).ToList();
            ReturnList(near);
            if (heroes.Count == 0) return;
            int each = (int)Math.Ceiling(xp / (float)heroes.Count);
            foreach (var h in heroes) AddXp(h, each);
        }

        private void ShareXpToTeam(Team team, int xp)
        {
            if (xp <= 0) return;
            foreach (var p in Players) if (p.Team == team && p.Hero != null) AddXp(p.Hero, xp);
        }

        public void AddXp(Unit hero, int amount)
        {
            if (hero?.HeroDef == null || amount <= 0) return;
            var table = Rules.Experience.Cumulative;
            if (hero.Level >= Rules.MaxHeroLevel) return;
            hero.Xp += amount;
            if (hero.Owner != null) hero.Owner.XpEarned += amount;
            bool leveled = false;
            while (hero.Level < Rules.MaxHeroLevel && hero.Level < table.Length && hero.Xp >= table[hero.Level])
            {
                hero.Level++;
                hero.AbilityPoints++;
                leveled = true;
                Emit(new SimEvent { Type = SimEventType.LevelUp, UnitId = hero.Id, Value = hero.Level, Point = hero.Position, PlayerId = -1 });
            }
            if (leveled) hero.StatsDirty = true;
        }

        public int XpForNextLevel(Unit hero)
        {
            var table = Rules.Experience.Cumulative;
            return hero.Level < table.Length ? table[hero.Level] : table[table.Length - 1];
        }

        // ================================================================== death & respawn upkeep

        private void UpdateDeathsAndRespawns(float dt)
        {
            for (int i = Units.Count - 1; i >= 0; i--)
            {
                var u = Units[i];
                if (u.Removed) { Units.RemoveAt(i); UnitById.Remove(u.Id); continue; }
                if (!u.Dead && u.Lifetime > 0f)
                {
                    u.Lifetime -= dt;
                    if (u.Lifetime <= 0f) { KillUnit(u, null); }
                }
                if (!u.Dead) continue;
                if (u.IsHero && !u.IsIllusion)
                {
                    if (Time >= u.RespawnAt && Phase == MatchPhase.Playing) RespawnHero(u);
                    continue;
                }
                if (u.IsStructure) continue; // structure ruins stay (client shows rubble)
                if (Time - u.DeathTime > 2.5f) u.Removed = true;
            }
            for (int i = Corpses.Count - 1; i >= 0; i--) if (Corpses[i].Consumed || Time > Corpses[i].Expires) Corpses.RemoveAt(i);
        }

        public void RespawnHero(Unit h)
        {
            var baseDef = Map.Bases.FirstOrDefault(b => b.Team == h.Team);
            var pos = Grid.NearestWalkable(baseDef != null ? (Vector2)baseDef.HeroSpawn : h.HomePosition);
            h.Dead = false;
            h.Position = pos;
            h.LastPosition = pos;
            h.Motion = null;
            SetAction(h, ActionState.Idle);
            h.CurrentOrder = default;
            h.OrderQueue.Clear();
            h.StatsDirty = true;
            h.RecomputeStats(Rules);
            h.Hp = h.Stats.MaxHp;
            h.Mana = h.Stats.MaxMana;
            RecomputeFlags(h);
            Emit(new SimEvent { Type = SimEventType.Respawn, UnitId = h.Id, Point = pos, Team = h.Team, PlayerId = -1 });
        }
    }
}
