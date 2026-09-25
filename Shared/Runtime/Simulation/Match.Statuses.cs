using System;
using System.Collections.Generic;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    public sealed partial class Match
    {
        private int _statusSerial;

        /// <summary>
        /// Applies a status effect honouring immunity, status resistance and the definition's stacking mode.
        /// Returns the resulting instance (or null if blocked).
        /// </summary>
        public StatusInstance ApplyStatus(Unit target, StatusDef def, Unit source, int level, float duration, int stacks = 1,
            bool piercesMagicImmunity = false, object origin = null, bool permanent = false)
        {
            if (target == null || def == null || target.Dead) return null;
            bool hostile = def.IsDebuff && source != null && source.Team != target.Team;
            if (hostile)
            {
                if (target.IsMagicImmune && !piercesMagicImmunity) return null;
                if (target.Invulnerable && (def.Flags & StatusFlags.Revealed) == 0) return null;
            }
            if (def.IsDebuff && def.AffectedByStatusResist && duration > 0 && !permanent)
                duration *= 1f - MathUtil.Clamp(target.Stats.StatusResist, -1f, Rules.StatusResistCap);

            StatusInstance existing = null;
            if (def.Stacking != StackingMode.Independent)
            {
                foreach (var s in target.Statuses)
                {
                    if (s.Def != def) continue;
                    // Passive sources (auras/items) are tracked per origin so two auras do not fight.
                    if (origin != null && s.Origin != null && s.Origin != origin && def.Stacking != StackingMode.Intensity) continue;
                    existing = s; break;
                }
            }

            if (existing != null)
            {
                switch (def.Stacking)
                {
                    case StackingMode.Refresh:
                        existing.Remaining = Math.Max(existing.Remaining, duration);
                        existing.Duration = Math.Max(existing.Duration, duration);
                        existing.Level = Math.Max(existing.Level, level);
                        existing.Source = source ?? existing.Source;
                        break;
                    case StackingMode.Longest:
                        if (duration > existing.Remaining) { existing.Remaining = duration; existing.Duration = duration; existing.Source = source; existing.Level = level; }
                        break;
                    case StackingMode.Intensity:
                        existing.Stacks = Math.Min(def.MaxStacks > 0 ? def.MaxStacks : 999, existing.Stacks + stacks);
                        existing.Remaining = Math.Max(existing.Remaining, duration);
                        existing.Duration = Math.Max(existing.Duration, duration);
                        existing.Level = Math.Max(existing.Level, level);
                        target.StatsDirty = true;
                        break;
                }
                if (def.Shield != null) existing.ShieldRemaining = Math.Max(existing.ShieldRemaining, def.Shield.Get(level));
                RecomputeFlags(target);
                return existing;
            }

            if (def.Stacking == StackingMode.Independent && def.MaxStacks > 0)
            {
                // Evict the oldest when at the independent stack cap.
                int count = 0; StatusInstance oldest = null;
                foreach (var s in target.Statuses)
                    if (s.Def == def) { count++; if (oldest == null || s.Remaining < oldest.Remaining) oldest = s; }
                if (count >= def.MaxStacks && oldest != null) RemoveStatus(target, oldest, expired: false);
            }

            var inst = new StatusInstance
            {
                Def = def,
                Source = source,
                Level = level,
                Remaining = duration,
                Duration = duration,
                Stacks = def.Stacking == StackingMode.Intensity ? Math.Min(def.MaxStacks > 0 ? def.MaxStacks : 999, stacks) : 1,
                IntervalTimer = def.Interval,
                PiercesMagicImmunity = piercesMagicImmunity,
                Permanent = permanent || duration < 0,
                Serial = ++_statusSerial,
                Origin = origin,
                ShieldRemaining = def.Shield?.Get(level) ?? 0f,
            };
            target.Statuses.Add(inst);
            target.StatsDirty = true;
            RecomputeFlags(target);

            if ((inst.Def.Flags & StatusFlags.HardDisable) != 0) InterruptUnit(target);
            if ((inst.Def.Flags & StatusFlags.Silenced) != 0 && (target.Action == ActionState.CastWindup || target.Action == ActionState.Channeling)) InterruptUnit(target);

            if (!def.Hidden)
                Emit(new SimEvent { Type = SimEventType.StatusApplied, UnitId = target.Id, OtherId = source?.Id ?? 0, Key = def.Id, Value = duration, PlayerId = -1 });

            if (def.OnApply != null)
                ExecuteEffects(def.OnApply, new EffectContext { Caster = source ?? target, Target = target, Level = level, Point = target.Position, StatusInstance = inst });
            return inst;
        }

        public StatusInstance ApplyStatus(Unit target, string statusId, Unit source, int level, float duration, int stacks = 1, bool pierces = false)
        {
            return Data.Statuses.TryGetValue(statusId, out var def) ? ApplyStatus(target, def, source, level, duration, stacks, pierces) : null;
        }

        public void RemoveStatus(Unit target, StatusInstance s, bool expired)
        {
            if (!target.Statuses.Remove(s)) return;
            target.StatsDirty = true;
            RecomputeFlags(target);
            if (!s.Def.Hidden)
                Emit(new SimEvent { Type = SimEventType.StatusRemoved, UnitId = target.Id, Key = s.Def.Id, PlayerId = -1 });
            if (expired && s.Def.OnExpire != null && !target.Dead)
                ExecuteEffects(s.Def.OnExpire, new EffectContext { Caster = s.Source ?? target, Target = target, Level = s.Level, Point = target.Position, StatusInstance = s });
            // A toggle status being removed turns its ability off.
            foreach (var ab in target.Abilities)
                if (ab.ToggledOn && ab.Def.ToggleStatus == s.Def.Id) ab.ToggledOn = false;
        }

        public void RemoveStatusById(Unit target, string id)
        {
            for (int i = target.Statuses.Count - 1; i >= 0; i--)
                if (target.Statuses[i].Def.Id == id) RemoveStatus(target, target.Statuses[i], false);
        }

        public void Dispel(Unit target, DispelType strength, bool debuffs, bool buffs)
        {
            for (int i = target.Statuses.Count - 1; i >= 0; i--)
            {
                var s = target.Statuses[i];
                if (s.Def.Dispel == DispelType.None || s.Permanent) continue;
                if (s.Def.Dispel == DispelType.Strong && strength != DispelType.Strong) continue;
                if ((s.Def.IsDebuff && debuffs) || (!s.Def.IsDebuff && buffs)) RemoveStatus(target, s, false);
            }
        }

        public void RecomputeFlags(Unit u)
        {
            var f = u.InnateFlags;
            foreach (var s in u.Statuses) f |= s.Def.Flags;
            if (u.Motion != null)
            {
                if (u.Motion.Kind == MotionKind.Leap) f |= StatusFlags.Airborne;
                if (u.Motion.Invulnerable) f |= StatusFlags.Invulnerable;
            }
            if (u.IsStructure && u.ProtectedBy != null)
            {
                foreach (var p in u.ProtectedBy) if (!p.Dead) { f |= StatusFlags.Invulnerable; break; }
            }
            u.Flags = f;
        }

        private void UpdateStatuses(float dt)
        {
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (u.Removed || u.Statuses.Count == 0) continue;
                for (int k = u.Statuses.Count - 1; k >= 0; k--)
                {
                    if (k >= u.Statuses.Count) continue;
                    var s = u.Statuses[k];
                    if (s.Def.Interval > 0 && s.Def.OnInterval != null && !u.Dead)
                    {
                        s.IntervalTimer -= dt;
                        while (s.IntervalTimer <= 0f && u.Statuses.Contains(s))
                        {
                            s.IntervalTimer += s.Def.Interval;
                            ExecuteEffects(s.Def.OnInterval, new EffectContext
                            {
                                Caster = s.Source ?? u, Target = u, Level = s.Level, Point = u.Position, StatusInstance = s,
                                PiercesMagicImmunity = s.PiercesMagicImmunity, Stacks = s.Stacks,
                            });
                            if (u.Dead) break;
                        }
                    }
                    if (s.Permanent) continue;
                    s.Remaining -= dt;
                    if (s.Remaining <= 0f && u.Statuses.Contains(s)) RemoveStatus(u, s, expired: true);
                }
            }
        }

        /// <summary>Refreshes aura statuses (every 0.5 s) from abilities and items to nearby units.</summary>
        private void UpdateAuras()
        {
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (!u.IsAlive) continue;
                foreach (var ab in u.Abilities)
                {
                    if (ab.Def.Aura == null || ab.Level <= 0 || u.HasFlag(StatusFlags.BreakPassives)) continue;
                    PulseAura(u, ab.Def.Aura, ab.Level, ab);
                }
                if (u.Inventory != null)
                    foreach (var it in u.Inventory)
                        if (it?.Def.Aura != null) PulseAura(u, it.Def.Aura, 1, it.Def);
            }
        }

        private readonly List<Unit> _auraScratch = new List<Unit>(32);

        private void PulseAura(Unit owner, AuraDef aura, int level, object origin)
        {
            if (!Data.Statuses.TryGetValue(aura.Status ?? "", out var def)) return;
            _auraScratch.Clear();
            UnitsInRadius(owner.Position, aura.Radius.Get(level), _auraScratch);
            foreach (var t in _auraScratch)
            {
                if (!MatchesTeam(owner, t, aura.Team)) continue;
                if (aura.HeroesOnly && !t.IsHero) continue;
                if ((t.TargetTypeOf() & aura.Types) == 0) continue;
                ApplyStatus(t, def, owner, level, 0.75f, 1, true, origin);
            }
        }

        // ------------------------------------------------------------------ triggers (passives)

        /// <summary>Fires passive triggers of the given type on the unit (abilities, items, statuses).</summary>
        public void FireTriggers(Unit unit, TriggerType type, Unit other, float amount, bool isAttack = false)
        {
            if (unit == null || unit.Removed) return;
            bool broken = unit.HasFlag(StatusFlags.BreakPassives);
            // Abilities.
            for (int i = 0; i < unit.Abilities.Count; i++)
            {
                var ab = unit.Abilities[i];
                if (ab.Level <= 0 || ab.Def.Triggers == null || broken) continue;
                foreach (var t in ab.Def.Triggers)
                    if (t.On == type) TryFireTrigger(unit, t, ab.Level, ab.Def, null, other, amount, isAttack);
            }
            // Items (inventory only).
            if (unit.Inventory != null)
            {
                for (int i = 0; i < unit.Inventory.Length; i++)
                {
                    var it = unit.Inventory[i];
                    if (it?.Def.Triggers == null) continue;
                    foreach (var t in it.Def.Triggers)
                        if (t.On == type) TryFireTrigger(unit, t, 1, it.Def.Active, it, other, amount, isAttack);
                }
            }
            // Statuses.
            for (int i = unit.Statuses.Count - 1; i >= 0; i--)
            {
                if (i >= unit.Statuses.Count) continue;
                var s = unit.Statuses[i];
                if (s.Def.Triggers == null) continue;
                foreach (var t in s.Def.Triggers)
                    if (t.On == type) TryFireTrigger(unit, t, s.Level, null, null, other, amount, isAttack, s);
            }
        }

        private void TryFireTrigger(Unit unit, TriggerDef t, int level, AbilityDef ability, ItemInstance item, Unit other, float amount, bool isAttack, StatusInstance status = null)
        {
            if (other != null)
            {
                if ((other.TargetTypeOf() & t.Types) == 0) return;
                if (t.IgnoreIllusions && unit.IsIllusion) return;
                if (t.Team != TargetTeam.Any && t.Team != TargetTeam.None && !MatchesTeam(unit, other, t.Team)) return;
            }
            if (t.MeleeOnly && unit.AttackType != AttackType.Melee) return;
            if (t.Threshold > 0 && amount < t.Threshold) return;
            var st = unit.GetTriggerState(t);
            if (st.CooldownUntil > Time) return;
            if (t.EveryN > 0)
            {
                int targetId = other?.Id ?? 0;
                if (t.SameTarget && st.LastTargetId != targetId) { st.Counter = 0; st.LastTargetId = targetId; }
                st.Counter++;
                if (st.Counter < t.EveryN) return;
                st.Counter = 0;
            }
            if (t.Chance < 1f)
            {
                bool proc = t.PseudoRandom ? st.Prd.Roll(Rng, t.Chance) : Rng.Chance(t.Chance);
                if (!proc) return;
            }
            if (t.InternalCooldown != null) st.CooldownUntil = Time + t.InternalCooldown.Get(level);
            ExecuteEffects(t.Effects, new EffectContext
            {
                Caster = unit,
                Target = other ?? unit,
                TriggerSource = other,
                TriggerAmount = amount,
                Level = level,
                Ability = ability,
                Item = item,
                Point = other?.Position ?? unit.Position,
                IsAttack = isAttack,
                StatusInstance = status,
            });
        }

        /// <summary>Relationship check used by areas, auras and triggers.</summary>
        public bool MatchesTeam(Unit source, Unit target, TargetTeam filter)
        {
            if (target == source) return (filter & TargetTeam.Self) != 0;
            bool enemy = target.Team != source.Team;
            if (enemy) return (filter & TargetTeam.Enemy) != 0;
            return (filter & TargetTeam.Ally) != 0;
        }
    }
}
