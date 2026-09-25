using System;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    public sealed partial class Match
    {
        public float EffectiveCastRange(Unit u, AbilityInstance ab) => ab.Def.CastRange.Get(Math.Max(1, ab.Level)) + u.Stats.CastRangeBonus;

        public float ManaCostOf(Unit u, AbilityInstance ab) => ab.Def.ManaCost.Get(Math.Max(1, ab.Level)) * (1f - u.Stats.ManaCostReduction);
        public float HealthCostOf(Unit u, AbilityInstance ab) => ab.Def.HealthCost.Get(Math.Max(1, ab.Level)) * (1f - u.Stats.HealthCostReduction);

        /// <summary>Early validation when the order is issued, producing a human-readable error.</summary>
        public bool ValidateCast(Unit u, AbilityInstance ab, Order o, out string error)
        {
            error = null;
            var d = ab.Def;
            if (ab.Level <= 0) { error = "Ability not learned."; return false; }
            if (d.Targeting == TargetingMode.Passive) { error = "That ability is passive."; return false; }
            if (u.Dead) { error = "You are dead."; return false; }
            if (ab.Item != null ? !u.CanUseItems : (!u.CanCast && !d.IgnoreSilence)) { error = ab.Item != null ? "Items are muted." : "Cannot cast right now."; return false; }
            if (!ab.IsReady) { error = d.MaxCharges > 0 && ab.Charges <= 0 ? "No charges." : "Ability is on cooldown."; return false; }
            if (u.Mana < ManaCostOf(u, ab)) { error = "Not enough mana."; return false; }
            if (HealthCostOf(u, ab) > 0 && u.Hp <= HealthCostOf(u, ab)) { error = "Not enough health."; return false; }
            if (d.Targeting == TargetingMode.Unit || (d.Targeting == TargetingMode.UnitOrPoint && o.TargetId != 0))
            {
                var t = GetUnit(o.TargetId);
                if (!ValidTarget(u, ab, t, out error)) return false;
            }
            return true;
        }

        public bool ValidTarget(Unit caster, AbilityInstance ab, Unit t, out string error)
        {
            error = null;
            var d = ab.Def;
            if (t == null || !t.IsAlive) { error = "Invalid target."; return false; }
            if (!IsVisibleTo(t, caster.Team)) { error = "Target is not visible."; return false; }
            if (!MatchesTeam(caster, t, d.TargetTeam)) { error = t.Team == caster.Team ? "Must target an enemy." : "Must target an ally."; return false; }
            if ((t.TargetTypeOf() & d.TargetTypes) == 0) { error = "Invalid target type."; return false; }
            if (t.Team != caster.Team && t.IsMagicImmune && !d.PiercesMagicImmunity && !d.CanTargetMagicImmune) { error = "Target is magic immune."; return false; }
            if (t.Team != caster.Team && (t.Invulnerable || t.HasFlag(StatusFlags.Untargetable))) { error = "Target is invulnerable."; return false; }
            return true;
        }

        /// <summary>Handles the cast order each tick: approach, turn, windup, fire, channel, backswing.</summary>
        private void ProcessCastOrder(Unit u, float dt)
        {
            var o = u.CurrentOrder;
            var ab = u.GetAbility(o.Slot);
            if (ab == null || ab.Level <= 0) { CompleteOrder(u); return; }

            if (u.Action == ActionState.Channeling) { UpdateChannel(u, ab, dt); return; }
            if (u.Action == ActionState.CastBackswing)
            {
                u.ActionTimer -= dt;
                if (u.ActionTimer <= 0f) { SetAction(u, ActionState.Idle); CompleteOrder(u); }
                return;
            }

            Unit target = o.Type == OrderType.CastUnit ? GetUnit(o.TargetId) : null;
            Vector2 point = target != null ? target.Position : o.Point;
            if (o.Type == OrderType.CastUnit && !ValidTarget(u, ab, target, out var err))
            {
                if (u.Action == ActionState.CastWindup) CancelCast(u, false);
                EmitError(u, err);
                CompleteOrder(u);
                return;
            }

            if (u.Action == ActionState.CastWindup)
            {
                if (!u.CanCast && ab.Item == null && !ab.Def.IgnoreSilence) { CancelCast(u, false); return; }
                if (target != null) FaceTowards(u, target.Position, dt);
                u.ActionTimer -= dt;
                if (u.ActionTimer <= 0f)
                {
                    if (!ValidateCast(u, ab, o, out var e2)) { EmitError(u, e2); CancelCast(u, false); CompleteOrder(u); return; }
                    ExecuteCast(u, ab, target?.Id ?? 0, point, o.Point2);
                    if (u.Motion != null || u.Dead) return; // dash/leap took over; the order completes when it lands
                    float channel = ab.Def.ChannelTime?.Get(ab.Level) ?? 0f;
                    if (channel > 0f)
                    {
                        SetAction(u, ActionState.Channeling);
                        u.ActionAbility = ab.Index;
                        u.ChannelRemaining = channel;
                        u.ChannelTickTimer = ab.Def.ChannelInterval;
                        u.ActionTargetId = target?.Id ?? 0;
                        u.ActionPoint = point;
                        Emit(new SimEvent { Type = SimEventType.ChannelStart, UnitId = u.Id, Key = ab.Def.Id, Value = channel, Point = point, OtherId = target?.Id ?? 0, PlayerId = -1 });
                    }
                    else
                    {
                        SetAction(u, ActionState.CastBackswing);
                        u.ActionTimer = ab.Def.Backswing;
                        if (u.ActionTimer <= 0f) { SetAction(u, ActionState.Idle); CompleteOrder(u); }
                    }
                }
                return;
            }

            // Approach.
            if (o.Type != OrderType.CastNoTarget)
            {
                float range = EffectiveCastRange(u, ab);
                float dist = Vector2.Distance(u.Position, point) - (target != null ? target.Radius + u.Radius : 0f);
                if (range > 0 && dist > range)
                {
                    if (!u.CanMove) return;
                    if (target != null) MoveTowardsUnit(u, target, dt, range * 0.95f);
                    else MoveTowardsPoint(u, point, dt, range * 0.95f);
                    return;
                }
                StopMoving(u);
                if (!ab.Def.IgnoreFacing && !FaceTowards(u, point, dt)) return;
            }
            else StopMoving(u);

            if (!u.CanCast && ab.Item == null && !ab.Def.IgnoreSilence) return;
            if (ab.Item != null && !u.CanUseItems) return;
            // Windup.
            SetAction(u, ActionState.CastWindup);
            u.ActionAbility = ab.Index;
            u.ActionTimer = ab.Def.CastPoint;
            u.ActionTargetId = target?.Id ?? 0;
            u.ActionPoint = point;
            Emit(new SimEvent
            {
                Type = SimEventType.CastStart,
                UnitId = u.Id,
                OtherId = target?.Id ?? 0,
                Key = ab.Def.Id,
                Point = point,
                Value = ab.Def.CastPoint,
                Value2 = ab.Def.AoeRadius?.Get(ab.Level) ?? 0f,
                PlayerId = -1,
            });
            if (u.ActionTimer <= 0f) ProcessCastOrder(u, 0f);
        }

        /// <summary>Pays costs, starts cooldown and runs the ability's OnCast effects.</summary>
        public void ExecuteCast(Unit u, AbilityInstance ab, int targetId, Vector2 point, Vector2 point2)
        {
            var d = ab.Def;
            int L = Math.Max(1, ab.Level);
            u.Mana = Math.Max(0f, u.Mana - ManaCostOf(u, ab));
            float hpCost = HealthCostOf(u, ab);
            if (hpCost > 0) u.Hp = Math.Max(1f, u.Hp - hpCost);
            float cd = d.Cooldown.Get(L) * (1f - u.Stats.CooldownReduction);
            if (d.MaxCharges > 0)
            {
                ab.Charges = Math.Max(0, ab.Charges - 1);
                if (ab.ChargeTimer <= 0) ab.ChargeTimer = d.ChargeRestoreTime?.Get(L) ?? cd;
                ab.Cooldown = Math.Min(0.25f, cd);
            }
            else ab.Cooldown = cd;
            ab.CooldownTotal = Math.Max(ab.Cooldown, 0.01f);
            if (ab.Item != null)
            {
                ab.Item.UsedSincePurchase = true;
                if (ab.Item.Def.Consumable || ab.Item.Def.InitialCharges > 0)
                {
                    ab.Item.Charges--;
                    if (ab.Item.Charges <= 0 && ab.Item.Def.Consumable) RemoveItem(u, ab.Item);
                }
            }

            var target = GetUnit(targetId);
            var dir = MathUtil.SafeNormalize((target?.Position ?? point) - u.Position, MathUtil.FromAngle(u.Facing));
            if (d.Targeting == TargetingMode.Vector) dir = MathUtil.SafeNormalize(point2 - point, dir);
            var ctx = new EffectContext
            {
                Caster = u,
                Target = target ?? (d.Targeting == TargetingMode.NoTarget ? u : null),
                Point = d.Targeting == TargetingMode.NoTarget ? u.Position : point,
                Direction = dir,
                Level = L,
                Ability = d,
                Item = ab.Item,
                PiercesMagicImmunity = d.PiercesMagicImmunity,
            };
            Emit(new SimEvent { Type = SimEventType.CastComplete, UnitId = u.Id, OtherId = targetId, Key = d.Id, Point = ctx.Point, Point2 = point2, Value = L, PlayerId = -1 });
            if (ab.Item != null) Emit(new SimEvent { Type = SimEventType.ItemUsed, UnitId = u.Id, Key = ab.Item.Def.Id, PlayerId = -1 });

            // Linkens-style spell block could intercept here (reserved).
            if (target != null && target.Team != u.Team && d.Targeting == TargetingMode.Unit)
                FireTriggers(target, TriggerType.SpellHit, u, 0f);
            ExecuteEffects(d.OnCast, ctx);
            FireTriggers(u, TriggerType.AbilityCast, target, 0f);
            if (d.Targeting != TargetingMode.NoTarget) u.Facing = MathUtil.AngleOf(dir);
        }

        private void UpdateChannel(Unit u, AbilityInstance ab, float dt)
        {
            if (!u.CanCast && ab.Item == null) { EndChannel(u, true); CompleteOrder(u); return; }
            var target = GetUnit(u.ActionTargetId);
            if (u.ActionTargetId != 0 && (target == null || target.Dead)) { EndChannel(u, true); CompleteOrder(u); return; }
            u.ChannelRemaining -= dt;
            u.ChannelTickTimer -= dt;
            if (ab.Def.OnChannelTick != null && u.ChannelTickTimer <= 0f)
            {
                u.ChannelTickTimer += Math.Max(0.05f, ab.Def.ChannelInterval);
                ExecuteEffects(ab.Def.OnChannelTick, new EffectContext
                {
                    Caster = u, Target = target ?? u, Point = target?.Position ?? u.ActionPoint, Level = ab.Level, Ability = ab.Def,
                    Direction = MathUtil.FromAngle(u.Facing), PiercesMagicImmunity = ab.Def.PiercesMagicImmunity,
                });
            }
            if (u.ChannelRemaining <= 0f) { EndChannel(u, false); CompleteOrder(u); }
        }

        public void EndChannel(Unit u, bool interrupted)
        {
            if (u.Action != ActionState.Channeling) return;
            var ab = u.GetAbility(u.ActionAbility);
            SetAction(u, ActionState.Idle);
            Emit(new SimEvent { Type = SimEventType.ChannelEnd, UnitId = u.Id, Key = ab?.Def.Id, Flags = (byte)(interrupted ? 1 : 0), PlayerId = -1 });
            if (ab == null) return;
            var target = GetUnit(u.ActionTargetId);
            var ctx = new EffectContext { Caster = u, Target = target ?? u, Point = target?.Position ?? u.ActionPoint, Level = ab.Level, Ability = ab.Def, Direction = MathUtil.FromAngle(u.Facing) };
            if (interrupted) ExecuteEffects(ab.Def.OnChannelInterrupted, ctx);
            else ExecuteEffects(ab.Def.OnChannelEnd, ctx);
        }

        public void CancelCast(Unit u, bool refund)
        {
            if (u.Action != ActionState.CastWindup) return;
            var ab = u.GetAbility(u.ActionAbility);
            SetAction(u, ActionState.Idle);
            Emit(new SimEvent { Type = SimEventType.CastCancelled, UnitId = u.Id, Key = ab?.Def.Id, PlayerId = -1 });
        }

        /// <summary>Called when a hard disable lands: cancels windups and channels.</summary>
        public void InterruptUnit(Unit u)
        {
            if (u.Action == ActionState.CastWindup) CancelCast(u, false);
            else if (u.Action == ActionState.Channeling) EndChannel(u, true);
            else if (u.Action == ActionState.AttackWindup) SetAction(u, ActionState.Idle);
        }

        private void ToggleAbility(Unit u, int slot)
        {
            var ab = u.GetAbility(slot);
            if (ab == null || ab.Level <= 0 || ab.Def.Targeting != TargetingMode.Toggle || u.Dead) return;
            if (!ab.ToggledOn)
            {
                if (!ab.IsReady) { EmitError(u, "Ability is on cooldown."); return; }
                if (u.Mana < ManaCostOf(u, ab)) { EmitError(u, "Not enough mana."); return; }
                if (!u.CanCast) { EmitError(u, "Cannot cast right now."); return; }
                u.Mana -= ManaCostOf(u, ab);
                ab.ToggledOn = true;
                if (ab.Def.ToggleStatus != null) ApplyStatus(u, ab.Def.ToggleStatus, u, ab.Level, -1f);
                ExecuteEffects(ab.Def.OnCast, new EffectContext { Caster = u, Target = u, Point = u.Position, Level = ab.Level, Ability = ab.Def });
            }
            else
            {
                ab.ToggledOn = false;
                if (ab.Def.ToggleStatus != null) RemoveStatusById(u, ab.Def.ToggleStatus);
                ab.Cooldown = ab.Def.Cooldown.Get(ab.Level) * (1f - u.Stats.CooldownReduction);
                ab.CooldownTotal = Math.Max(0.01f, ab.Cooldown);
            }
            Emit(new SimEvent { Type = SimEventType.CastComplete, UnitId = u.Id, Key = ab.Def.Id, Value = ab.ToggledOn ? 1 : 0, Point = u.Position, PlayerId = -1 });
        }

        public bool CanLevelAbility(Unit u, AbilityInstance ab)
        {
            if (u.AbilityPoints <= 0 || ab.Def.Slot == AbilitySlot.Innate || ab.Def.Hidden) return false;
            if (ab.Level >= ab.Def.MaxLevel) return false;
            return u.Level >= ab.Def.RequiredHeroLevel(ab.Level + 1);
        }

        private void TryLevelAbility(Unit u, int slot)
        {
            var ab = u.GetAbility(slot);
            if (ab == null || !CanLevelAbility(u, ab)) return;
            ab.Level++;
            u.AbilityPoints--;
            if (ab.Def.MaxCharges > 0 && ab.Level == 1) ab.Charges = ab.Def.MaxCharges;
            u.StatsDirty = true;
            Emit(new SimEvent { Type = SimEventType.AbilityLeveled, UnitId = u.Id, Key = ab.Def.Id, Value = ab.Level, PlayerId = -1 });
        }

        private void UpdateCooldowns(Unit u, float dt)
        {
            for (int i = 0; i < u.Abilities.Count; i++) TickAbility(u.Abilities[i], dt);
            if (u.Inventory != null)
            {
                foreach (var it in u.Inventory) if (it?.Active != null) TickAbility(it.Active, dt);
                foreach (var it in u.Backpack) if (it?.Active != null) TickAbility(it.Active, dt);
                foreach (var it in u.Stash) if (it?.Active != null) TickAbility(it.Active, dt);
            }
        }

        private static void TickAbility(AbilityInstance ab, float dt)
        {
            if (ab.Cooldown > 0f) ab.Cooldown = Math.Max(0f, ab.Cooldown - dt);
            if (ab.Def.MaxCharges > 0 && ab.Charges < ab.Def.MaxCharges && ab.Level > 0)
            {
                ab.ChargeTimer -= dt;
                if (ab.ChargeTimer <= 0f)
                {
                    ab.Charges++;
                    ab.ChargeTimer = ab.Charges < ab.Def.MaxCharges ? (ab.Def.ChargeRestoreTime?.Get(ab.Level) ?? ab.Def.Cooldown.Get(ab.Level)) : 0f;
                }
            }
        }
    }
}
