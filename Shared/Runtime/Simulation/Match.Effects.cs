using System;
using System.Collections.Generic;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    /// <summary>Everything an effect node needs to know about why and where it executes.</summary>
    public struct EffectContext
    {
        public Unit Caster;
        public Unit Target;
        public Vector2 Point;
        public Vector2 Direction;
        public int Level;
        public AbilityDef Ability;
        public ItemInstance Item;
        public Unit TriggerSource;
        public float TriggerAmount;
        public bool IsAttack;
        public bool PiercesMagicImmunity;
        public StatusInstance StatusInstance;
        /// <summary>Unit that applied the status whose trigger/interval is running (kill credit for DoTs and curses).</summary>
        public Unit StatusSource;
        public int Stacks;
        public int Depth;
        public Projectile Projectile;
        /// <summary>Multiplier on damage/heal/mana amounts (echoed casts). 0 means full power.</summary>
        public float PowerScale;
    }

    public sealed partial class Match
    {
        private readonly Stack<List<Unit>> _listPool = new Stack<List<Unit>>();
        private List<Unit> RentList() => _listPool.Count > 0 ? _listPool.Pop() : new List<Unit>(32);
        private void ReturnList(List<Unit> l) { l.Clear(); _listPool.Push(l); }

        public void ExecuteEffects(List<EffectDef> effects, EffectContext ctx)
        {
            if (effects == null || effects.Count == 0) return;
            if (ctx.Depth > 12) { LogLine("Effect recursion limit hit"); return; }
            if (ctx.Level <= 0) ctx.Level = 1;
            for (int i = 0; i < effects.Count; i++)
            {
                if (ctx.Caster != null && ctx.Caster.Removed) return;
                ExecuteEffect(effects[i], ctx);
            }
        }

        private Unit ResolveUnit(EffectDef e, in EffectContext ctx)
        {
            switch (e.Target)
            {
                case EffectTarget.Caster: return ctx.Caster;
                case EffectTarget.TriggerSource: return ctx.TriggerSource;
                case EffectTarget.StatusSource: return ctx.StatusSource;
                default: return ctx.Target;
            }
        }

        public float StatValue(Unit u, StatType stat)
        {
            var s = u.Stats;
            switch (stat)
            {
                case StatType.Str: return s.Str;
                case StatType.Agi: return s.Agi;
                case StatType.Int: return s.Int;
                case StatType.AllAttributes: return s.Str + s.Agi + s.Int;
                case StatType.MaxHp: return s.MaxHp;
                case StatType.MaxMana: return s.MaxMana;
                case StatType.Armor: return s.Armor;
                case StatType.BaseDamage: return (s.DamageMin + s.DamageMax) * 0.5f;
                case StatType.BonusDamage: return s.BonusDamage;
                case StatType.MoveSpeed: return s.MoveSpeed;
                case StatType.AttackSpeed: return s.AttackSpeed;
                case StatType.AttackRange: return s.AttackRange;
                case StatType.HpRegen: return s.HpRegen;
                default: return 0f;
            }
        }

        private float ComputeAmount(EffectDef e, in EffectContext ctx, Unit target)
        {
            int L = ctx.Level;
            float a = e.Amount != null ? e.Amount.Get(L) : 0f;
            if (e.Scaling != null && ctx.Caster != null)
                foreach (var sc in e.Scaling) a += StatValue(ctx.Caster, sc.Stat) * sc.Ratio.Get(L);
            if (target != null)
            {
                if (e.PctMaxHp != null) a += target.Stats.MaxHp * e.PctMaxHp.Get(L);
                if (e.PctCurrentHp != null) a += target.Hp * e.PctCurrentHp.Get(L);
                if (e.PctMissingHp != null) a += (target.Stats.MaxHp - target.Hp) * e.PctMissingHp.Get(L);
            }
            if (ctx.StatusInstance != null && ctx.StatusInstance.Def.Stacking == StackingMode.Intensity)
                a *= Math.Max(1, ctx.StatusInstance.Stacks);
            if (e.Condition == "triggerAmount") a *= ctx.TriggerAmount;
            if (ctx.PowerScale > 0f) a *= ctx.PowerScale;
            return a;
        }

        /// <summary>
        /// Optional gates on an effect (<c>condition</c> in data). Special-purpose conditions consumed by specific effect
        /// types ("triggerAmount", "alliedStructure", "itemMin") are ignored here.
        /// Supported: targetHasStatus:&lt;id&gt;, targetLacksStatus:&lt;id&gt;, casterHasStatus:&lt;id&gt;, casterLacksStatus:&lt;id&gt;,
        /// night, day, targetIsHero, targetNotHero, targetHpBelow:&lt;fraction&gt;, casterHpBelow:&lt;fraction&gt;,
        /// casterUnseen (no enemy team can see the caster), casterStacksAtLeast:&lt;status&gt;:&lt;n&gt;,
        /// casterNearTrees:&lt;count&gt;:&lt;radius&gt;.
        /// </summary>
        public bool CheckCondition(string condition, in EffectContext ctx, Unit unit)
        {
            if (string.IsNullOrEmpty(condition)) return true;
            int colon = condition.IndexOf(':');
            string key = colon >= 0 ? condition.Substring(0, colon) : condition;
            string arg = colon >= 0 ? condition.Substring(colon + 1) : null;
            switch (key)
            {
                case "targetHasStatus": return unit != null && unit.FindStatus(arg) != null;
                case "targetLacksStatus": return unit == null || unit.FindStatus(arg) == null;
                case "casterHasStatus": return ctx.Caster != null && ctx.Caster.FindStatus(arg) != null;
                case "casterLacksStatus": return ctx.Caster == null || ctx.Caster.FindStatus(arg) == null;
                case "night": return IsNight;
                case "day": return !IsNight;
                case "targetIsHero": return unit != null && unit.IsHero;
                case "targetNotHero": return unit != null && !unit.IsHero;
                case "targetHpBelow": return unit != null && float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tf) && unit.HpFraction < tf;
                case "casterHpBelow": return ctx.Caster != null && float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cf) && ctx.Caster.HpFraction < cf;
                case "casterUnseen":
                {
                    if (ctx.Caster == null) return false;
                    for (int t = 0; t < 2; t++)
                        if (t != (int)ctx.Caster.Team && ctx.Caster.VisibleTo[t]) return false;
                    return true;
                }
                case "casterStacksAtLeast":
                {
                    if (ctx.Caster == null || !SplitArg(arg, out var id, out var n)) return false;
                    var s = ctx.Caster.FindStatus(id);
                    return s != null && s.Stacks >= n;
                }
                case "casterNearTrees":
                {
                    if (ctx.Caster == null || !SplitArg(arg, out var countText, out var radius)) return false;
                    return int.TryParse(countText, out int need) && CountTreesNear(ctx.Caster.Position, radius) >= need;
                }
                default: return true;
            }
        }

        /// <summary>Parses "&lt;text&gt;:&lt;number&gt;" condition arguments.</summary>
        private static bool SplitArg(string arg, out string head, out float value)
        {
            head = null; value = 0f;
            if (arg == null) return false;
            int c = arg.LastIndexOf(':');
            if (c <= 0) return false;
            head = arg.Substring(0, c);
            return float.TryParse(arg.Substring(c + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        // The ability cast currently resolving (for EchoCast) and a guard against echoing an echo.
        private EffectContext _lastCastContext;
        private bool _lastCastValid;
        private bool _echoing;

        private void ExecuteEffect(EffectDef e, EffectContext ctx)
        {
            int L = ctx.Level;
            var unit = ResolveUnit(e, ctx);
            if (!CheckCondition(e.Condition, ctx, unit)) return;
            switch (e.Type)
            {
                case EffectType.Damage:
                {
                    if (unit == null || unit.Dead) return;
                    float amount = ComputeAmount(e, ctx, unit);
                    DealDamage(new DamageInfo
                    {
                        Source = e.CreditStatusSource && ctx.StatusSource != null ? ctx.StatusSource : ctx.Caster,
                        Target = unit,
                        Amount = amount,
                        Type = e.DamageType,
                        IsSpell = !ctx.IsAttack && !e.IsAttackDamage,
                        IsAttack = e.IsAttackDamage,
                        Ability = ctx.Ability,
                        HealSourcePct = e.HealCasterPct?.Get(L) ?? 0f,
                        PiercesMagicImmunity = ctx.PiercesMagicImmunity || e.PiercesMagicImmunity,
                        Vfx = e.Vfx,
                    });
                    break;
                }
                case EffectType.Heal:
                    if (unit == null || unit.Dead) return;
                    Heal(unit, ComputeAmount(e, ctx, unit), ctx.Caster);
                    break;
                case EffectType.RestoreMana:
                    if (unit == null || unit.Dead) return;
                    unit.Mana = Math.Min(unit.Stats.MaxMana, unit.Mana + ComputeAmount(e, ctx, unit));
                    break;
                case EffectType.ReduceMana:
                {
                    if (unit == null || unit.Dead) return;
                    float burned = Math.Min(unit.Mana, ComputeAmount(e, ctx, unit));
                    unit.Mana -= burned;
                    if (e.Effects != null) { var c = ctx; c.Target = unit; c.TriggerAmount = burned; c.Depth++; ExecuteEffects(e.Effects, c); }
                    break;
                }
                case EffectType.ApplyStatus:
                {
                    if (unit == null || unit.Dead || !Data.Statuses.TryGetValue(e.Status ?? "", out var sd)) return;
                    float dur = e.Duration != null ? e.Duration.Get(L) : -1f;
                    ApplyStatus(unit, sd, ctx.Caster, L, dur, Math.Max(1, e.Stacks), ctx.PiercesMagicImmunity || e.PiercesMagicImmunity, null, dur < 0);
                    break;
                }
                case EffectType.RemoveStatus:
                    if (unit != null) RemoveStatusById(unit, e.Status);
                    break;
                case EffectType.Dispel:
                    if (unit != null) Dispel(unit, e.DispelStrength, e.DispelDebuffs, e.DispelBuffs);
                    break;
                case EffectType.Area:
                    ExecuteArea(e, ctx);
                    break;
                case EffectType.Projectile:
                    LaunchEffectProjectile(e, ctx);
                    break;
                case EffectType.Dash:
                case EffectType.Leap:
                    StartCasterMotion(e, ctx);
                    break;
                case EffectType.Blink:
                case EffectType.Teleport:
                {
                    var mover = e.Target == EffectTarget.Target && ctx.Target != null && e.Type == EffectType.Teleport ? ctx.Target : ctx.Caster;
                    if (mover == null) return;
                    Vector2 dest = ctx.Point;
                    if (e.Condition == "alliedStructure")
                    {
                        // Teleport scrolls: land next to the allied structure closest to the chosen point.
                        Unit best = null; float bd = float.MaxValue;
                        foreach (var s in Units)
                        {
                            if (!s.IsStructure || s.Dead || s.Team != mover.Team || s.Kind == UnitKind.Shop) continue;
                            float dd = Vector2.DistanceSquared(s.Position, ctx.Point);
                            if (dd < bd) { bd = dd; best = s; }
                        }
                        if (best == null) return;
                        var towardFountain = MathUtil.SafeNormalize(ctx.Point - best.Position, Vector2.UnitX);
                        dest = best.Position + towardFountain * (best.Radius + 1.5f);
                    }
                    else if (e.BehindTarget && ctx.Target != null)
                    {
                        var dir = MathUtil.SafeNormalize(ctx.Target.Position - mover.Position, MathUtil.FromAngle(mover.Facing));
                        dest = ctx.Target.Position + dir * (ctx.Target.Radius + mover.Radius + 0.3f);
                    }
                    if (e.MaxDistance != null)
                    {
                        float max = e.MaxDistance.Get(L);
                        var delta = dest - mover.Position;
                        if (delta.Length() > max) dest = mover.Position + MathUtil.SafeNormalize(delta, Vector2.UnitX) * (max * (e.Type == EffectType.Blink ? 0.8f : 1f));
                    }
                    TeleportUnit(mover, dest, e.Vfx);
                    break;
                }
                case EffectType.Knockback:
                case EffectType.Pull:
                    StartTargetMotion(e, ctx, unit);
                    break;
                case EffectType.Delayed:
                case EffectType.Zone:
                case EffectType.CreateWall:
                    CreateZoneFromEffect(e, ctx);
                    break;
                case EffectType.Reveal:
                    CreateZoneFromEffect(e, ctx);
                    break;
                case EffectType.SpawnUnit:
                case EffectType.SummonAtTarget:
                    SpawnSummons(e, ctx);
                    break;
                case EffectType.Chance:
                    if (Rng.Chance(e.Chance)) { ctx.Depth++; ExecuteEffects(e.Effects, ctx); }
                    break;
                case EffectType.Sequence:
                    ctx.Depth++;
                    ExecuteEffects(e.Effects, ctx);
                    break;
                case EffectType.Execute:
                    if (unit == null || unit.Dead || unit.IsStructure) return;
                    if (unit.IsMagicImmune && !(ctx.PiercesMagicImmunity || e.PiercesMagicImmunity)) return;
                    if (unit.HpFraction <= (e.Threshold?.Get(L) ?? 0f))
                        DealDamage(new DamageInfo { Source = ctx.Caster, Target = unit, Amount = unit.Hp + 1f, Type = DamageType.Pure, IsSpell = true, Ability = ctx.Ability, Vfx = e.Vfx, IgnoreShields = true });
                    break;
                case EffectType.Kill:
                    if (unit != null && !unit.Dead && !unit.IsStructure) KillUnit(unit, ctx.Caster);
                    break;
                case EffectType.Swap:
                    if (unit != null && ctx.Caster != null && unit != ctx.Caster)
                    {
                        var a = ctx.Caster.Position; var b = unit.Position;
                        TeleportUnit(ctx.Caster, b, e.Vfx);
                        TeleportUnit(unit, a, e.Vfx);
                    }
                    break;
                case EffectType.ModifyCooldowns:
                    if (e.Condition == "itemMin" && ctx.Item?.Active != null)
                    {
                        // e.g. blink items are disabled for a few seconds after taking hero damage.
                        ctx.Item.Active.Cooldown = Math.Max(ctx.Item.Active.Cooldown, e.CooldownDelta?.Get(L) ?? 0f);
                        ctx.Item.Active.CooldownTotal = Math.Max(ctx.Item.Active.CooldownTotal, ctx.Item.Active.Cooldown);
                    }
                    else if (unit != null)
                        foreach (var ab in unit.Abilities)
                            if (ab.Level > 0) ab.Cooldown = Math.Max(0f, ab.Cooldown + (e.CooldownDelta?.Get(L) ?? 0f));
                    break;
                case EffectType.SpendResource:
                    if (unit != null)
                    {
                        float amt = ComputeAmount(e, ctx, unit);
                        unit.Hp = Math.Max(1f, unit.Hp - amt);
                    }
                    break;
                case EffectType.GrantGold:
                    if (ctx.Caster?.Owner != null) GiveGold(ctx.Caster.Owner, (int)(e.Gold?.Get(L) ?? 0f), ctx.Caster.Position, true);
                    break;
                case EffectType.AddCharges:
                    if (ctx.Ability != null && ctx.Caster != null)
                    {
                        var ab = ctx.Caster.FindAbility(ctx.Ability.Id);
                        if (ab != null && ab.Def.MaxCharges > 0) ab.Charges = Math.Min(ab.Def.MaxCharges, ab.Charges + Math.Max(1, (int)(e.Amount?.Get(L) ?? 1)));
                    }
                    break;
                case EffectType.Transform:
                    if (unit != null && Data.Statuses.TryGetValue(e.Status ?? "", out var td))
                        ApplyStatus(unit, td, ctx.Caster, L, e.Duration?.Get(L) ?? -1f, 1, true);
                    break;
                case EffectType.ConsumeCorpse:
                {
                    float r = e.Radius?.Get(L) ?? 6f;
                    Corpse best = null; float bestD = r * r;
                    foreach (var c in Corpses)
                    {
                        if (c.Consumed) continue;
                        float d = Vector2.DistanceSquared(c.Position, ctx.Point);
                        if (d <= bestD) { best = c; bestD = d; }
                    }
                    if (best == null) { if (ctx.Caster != null && ctx.Depth == 0) EmitError(ctx.Caster, "No corpse nearby."); return; }
                    best.Consumed = true;
                    var cc = ctx; cc.Point = best.Position; cc.Depth++;
                    Emit(new SimEvent { Type = SimEventType.EffectVisual, Key = "corpse_consumed", Point = best.Position, PlayerId = -1 });
                    ExecuteEffects(e.Effects, cc);
                    break;
                }
                case EffectType.EchoCast:
                {
                    // Re-runs the ability the caster just cast (fired from an AbilityCast trigger) at reduced power.
                    if (_echoing || !_lastCastValid || ctx.Caster == null || _lastCastContext.Caster != ctx.Caster) return;
                    var cast = _lastCastContext;
                    if (cast.Ability == null || cast.Item != null || cast.Ability.IsUltimate) return;
                    if (cast.Target != null && cast.Target != ctx.Caster && cast.Target.Dead) return;
                    cast.PowerScale = e.Amount?.Get(L) ?? 0.5f;
                    _echoing = true;
                    try
                    {
                        Emit(new SimEvent { Type = SimEventType.EffectVisual, Key = e.Vfx ?? "echo_cast", UnitId = ctx.Caster.Id, Point = ctx.Caster.Position, PlayerId = -1 });
                        ExecuteEffects(cast.Ability.OnCast, cast);
                    }
                    finally { _echoing = false; }
                    return;
                }
                case EffectType.ForceNight:
                    ForcedNightUntil = Math.Max(ForcedNightUntil, Time + (e.Duration?.Get(L) ?? 0f));
                    break;
                case EffectType.Illusion:
                case EffectType.Resurrect:
                    // Planned: illusions and resurrection share the summon pipeline (see TODO.md).
                    break;
            }

            if (!string.IsNullOrEmpty(e.Vfx) && e.Type != EffectType.Projectile && e.Type != EffectType.Delayed && e.Type != EffectType.Zone
                && e.Type != EffectType.Damage && e.Type != EffectType.Blink && e.Type != EffectType.Teleport && e.Type != EffectType.Dash && e.Type != EffectType.Leap)
            {
                Emit(new SimEvent
                {
                    Type = SimEventType.EffectVisual,
                    Key = e.Vfx,
                    UnitId = unit?.Id ?? 0,
                    OtherId = ctx.Caster?.Id ?? 0,
                    Point = unit?.Position ?? ctx.Point,
                    Value = e.Radius?.Get(L) ?? 0f,
                    PlayerId = -1,
                });
            }
            if (e.Shake > 0)
                Emit(new SimEvent { Type = SimEventType.Shake, Point = unit?.Position ?? ctx.Point, Value = e.Shake, PlayerId = -1 });
        }

        private void ExecuteArea(EffectDef e, EffectContext ctx)
        {
            int L = ctx.Level;
            Vector2 center;
            switch (e.Center)
            {
                case AreaCenter.Target: center = ctx.Target?.Position ?? ctx.Point; break;
                case AreaCenter.Point: center = ctx.Point; break;
                default: center = ctx.Caster?.Position ?? ctx.Point; break;
            }
            float radius = e.Radius?.Get(L) ?? 3f;
            var list = RentList();
            UnitsInRadius(center, radius, list);
            Vector2 dir = ctx.Direction != Vector2.Zero ? Vector2.Normalize(ctx.Direction)
                : ctx.Caster != null ? MathUtil.FromAngle(ctx.Caster.Facing) : Vector2.UnitX;
            float cosLimit = e.ConeAngle > 0 ? (float)Math.Cos(e.ConeAngle * MathUtil.Deg2Rad) : -2f;
            bool pierce = ctx.PiercesMagicImmunity || e.PiercesMagicImmunity;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var t = list[i];
                bool keep = ctx.Caster == null || MatchesTeam(ctx.Caster, t, e.Team);
                keep &= (t.TargetTypeOf() & e.Types) != 0;
                if (e.ExcludePrimary && t == ctx.Target) keep = false;
                if (ctx.Caster != null && t.Team != ctx.Caster.Team)
                {
                    if (t.Invulnerable) keep = false;
                    if (t.IsMagicImmune && !pierce) keep = false;
                    if (t.HasFlag(StatusFlags.Untargetable)) keep = false;
                }
                if (cosLimit > -1.5f)
                {
                    var to = t.Position - center;
                    if (to.LengthSquared() > 0.01f && Vector2.Dot(Vector2.Normalize(to), dir) < cosLimit) keep = false;
                }
                if (!keep) list.RemoveAt(i);
            }
            list.Sort((a, b) => Vector2.DistanceSquared(a.Position, center).CompareTo(Vector2.DistanceSquared(b.Position, center)));
            int max = e.MaxTargets > 0 ? Math.Min(e.MaxTargets, list.Count) : list.Count;
            // Copy before executing children: nested areas reuse the pool.
            var targets = new Unit[max];
            for (int i = 0; i < max; i++) targets[i] = list[i];
            ReturnList(list);
            var child = ctx;
            child.Depth++;
            child.Point = center;
            foreach (var t in targets)
            {
                if (t.Dead) continue;
                child.Target = t;
                ExecuteEffects(e.Effects, child);
            }
        }

        // ------------------------------------------------------------------ heal

        public void Heal(Unit target, float amount, Unit source)
        {
            if (target == null || target.Dead || amount <= 0f) return;
            amount *= 1f + target.Stats.HealAmp;
            float before = target.Hp;
            target.Hp = Math.Min(target.Stats.MaxHp, target.Hp + amount);
            float healed = target.Hp - before;
            if (healed <= 0.5f) return;
            if (source?.Owner != null && source.Owner.Hero == source) source.Owner.Healing += healed;
            Emit(new SimEvent { Type = SimEventType.Heal, UnitId = target.Id, OtherId = source?.Id ?? 0, Value = healed, PlayerId = -1 });
        }

        // ------------------------------------------------------------------ teleport / summons

        public void TeleportUnit(Unit u, Vector2 dest, string vfx = null)
        {
            var from = u.Position;
            dest = Grid.NearestWalkable(new Vector2(MathUtil.Clamp(dest.X, 1, Grid.WorldWidth - 1), MathUtil.Clamp(dest.Y, 1, Grid.WorldHeight - 1)));
            u.Position = dest;
            u.LastPosition = dest;
            u.Path.Clear();
            Emit(new SimEvent { Type = SimEventType.Blink, UnitId = u.Id, Point = from, Point2 = dest, Key = vfx, PlayerId = -1 });
        }

        private void SpawnSummons(EffectDef e, EffectContext ctx)
        {
            if (!Data.Units.TryGetValue(e.UnitId ?? "", out var ud) || ctx.Caster == null) return;
            int L = ctx.Level;
            int count = Math.Max(1, (int)(e.Count?.Get(L) ?? 1f));
            Vector2 basePos = e.Type == EffectType.SummonAtTarget && ctx.Target != null ? ctx.Target.Position
                : e.Center == AreaCenter.Point ? ctx.Point : ctx.Caster.Position + MathUtil.FromAngle(ctx.Caster.Facing) * 1.2f;
            for (int i = 0; i < count; i++)
            {
                float ang = ctx.Caster.Facing + (i - (count - 1) * 0.5f) * 0.9f;
                var pos = Grid.NearestWalkable(basePos + MathUtil.FromAngle(ang) * (count > 1 ? 0.9f : 0f));
                var s = CreateUnit(ud, ctx.Caster.Team, pos, ctx.Caster.Facing, ctx.Caster.Owner);
                s.Summoner = ctx.Caster;
                float life = e.SummonDuration?.Get(L) ?? -1f;
                s.Lifetime = life;
                s.Level = L;
                // Summon abilities (auras, scaling passives) follow the level of the ability that raised them.
                if (s.Abilities.Count > 0)
                {
                    foreach (var ab in s.Abilities) ab.Level = Math.Max(1, Math.Min(ab.Def.MaxLevel, L));
                    s.RecomputeStats(Rules);
                    s.Hp = s.Stats.MaxHp;
                    s.Mana = s.Stats.MaxMana;
                }
                if (ud.Kind == UnitKind.Ward) { s.Brain = null; if (ctx.Caster.Owner != null) ctx.Caster.Owner.WardsPlaced++; }
                else s.Brain = new SummonBrain(e.ControlledByOwner);
                Emit(new SimEvent { Type = SimEventType.EffectVisual, Key = e.Vfx ?? "summon", UnitId = s.Id, Point = pos, PlayerId = -1 });
            }
        }
    }
}
