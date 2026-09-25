using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Bloodfall.Core;

namespace Bloodfall.Data
{
    /// <summary>A named JSON text blob (file path relative to the GameData root, forward slashes).</summary>
    public readonly struct GameDataFile
    {
        public readonly string Path;
        public readonly string Text;
        public GameDataFile(string path, string text) { Path = path.Replace('\\', '/'); Text = text; }
    }

    /// <summary>
    /// Registry of all gameplay definitions. Loaded identically by the Unity client (from Resources) and the
    /// dedicated server (from disk). The <see cref="ContentHash"/> is exchanged during the network handshake so a
    /// client with mismatched balance data is rejected instead of desyncing.
    /// </summary>
    public sealed class GameData
    {
        public readonly Dictionary<string, HeroDef> Heroes = new Dictionary<string, HeroDef>();
        public readonly Dictionary<string, AbilityDef> Abilities = new Dictionary<string, AbilityDef>();
        public readonly Dictionary<string, StatusDef> Statuses = new Dictionary<string, StatusDef>();
        public readonly Dictionary<string, ItemDef> Items = new Dictionary<string, ItemDef>();
        public readonly Dictionary<string, UnitDef> Units = new Dictionary<string, UnitDef>();
        public readonly Dictionary<string, MapDef> Maps = new Dictionary<string, MapDef>();
        public readonly Dictionary<string, CampTypeDef> CampTypes = new Dictionary<string, CampTypeDef>();
        public readonly Dictionary<string, GameModeDef> Modes = new Dictionary<string, GameModeDef>();
        public readonly Dictionary<Faction, FactionDef> Factions = new Dictionary<Faction, FactionDef>();
        public readonly Dictionary<string, RtsFactionDef> RtsFactions = new Dictionary<string, RtsFactionDef>();
        public readonly List<string> RtsFactionOrder = new List<string>();
        public readonly List<string> ShopCategories = new List<string>();
        public RulesDef Rules = new RulesDef();
        public string ContentHash = "";
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        /// <summary>Hero ids in a stable order (declaration order of the data files, then id).</summary>
        public readonly List<string> HeroOrder = new List<string>();
        public readonly List<string> ItemOrder = new List<string>();

        public static GameData Load(IEnumerable<GameDataFile> files)
        {
            var data = new GameData();
            var sorted = files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
            data.ContentHash = ComputeHash(sorted);
            JsonMapper.Warnings = data.Warnings;
            try
            {
                foreach (var f in sorted)
                {
                    try { data.LoadFile(f); }
                    catch (Exception e) { data.Errors.Add($"{f.Path}: {e.Message}"); }
                }
            }
            finally { JsonMapper.Warnings = null; }
            data.Link();
            data.Validate();
            return data;
        }

        public static string ComputeHash(IEnumerable<GameDataFile> sortedFiles)
        {
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                foreach (var f in sortedFiles)
                {
                    sb.Append(f.Path.Replace('\\', '/')).Append('\n');
                    // Normalize line endings so git autocrlf does not change the hash.
                    sb.Append(f.Text.Replace("\r\n", "\n")).Append('\n');
                }
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(64);
                foreach (var b in bytes) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

        private void LoadFile(GameDataFile f)
        {
            var root = Json.Parse(f.Text);
            // A file may contain any of these top-level collections; lets designers group content freely.
            foreach (var kv in root.Properties())
            {
                switch (kv.Key)
                {
                    case "rules": Rules = JsonMapper.FromNode<RulesDef>(kv.Value); break;
                    case "heroes": foreach (var n in kv.Value.ArrayValue ?? new List<JsonNode>()) AddHero(JsonMapper.FromNode<HeroDef>(n), f.Path); break;
                    case "hero": AddHero(JsonMapper.FromNode<HeroDef>(kv.Value), f.Path); break;
                    case "abilities": foreach (var n in kv.Value.ArrayValue ?? new List<JsonNode>()) AddAbility(JsonMapper.FromNode<AbilityDef>(n), f.Path); break;
                    case "statuses": foreach (var n in kv.Value.ArrayValue ?? new List<JsonNode>()) AddStatus(JsonMapper.FromNode<StatusDef>(n), f.Path); break;
                    case "items": foreach (var n in kv.Value.ArrayValue ?? new List<JsonNode>()) AddItem(JsonMapper.FromNode<ItemDef>(n), f.Path); break;
                    case "units": foreach (var n in kv.Value.ArrayValue ?? new List<JsonNode>()) AddUnit(JsonMapper.FromNode<UnitDef>(n), f.Path); break;
                    case "map": { var m = JsonMapper.FromNode<MapDef>(kv.Value); Maps[m.Id] = m; break; }
                    case "campTypes": foreach (var n in kv.Value.ArrayValue ?? new List<JsonNode>()) { var c = JsonMapper.FromNode<CampTypeDef>(n); CampTypes[c.Id] = c; } break;
                    case "modes": foreach (var n in kv.Value.ArrayValue ?? new List<JsonNode>()) { var m = JsonMapper.FromNode<GameModeDef>(n); Modes[m.Id] = m; } break;
                    case "factions": foreach (var n in kv.Value.ArrayValue ?? new List<JsonNode>()) { var fd = JsonMapper.FromNode<FactionDef>(n); Factions[fd.Id] = fd; } break;
                    case "rtsFactions":
                        foreach (var n in kv.Value.ArrayValue ?? new List<JsonNode>())
                        {
                            var rf = JsonMapper.FromNode<RtsFactionDef>(n);
                            if (string.IsNullOrEmpty(rf.Id)) { Errors.Add($"{f.Path}: RTS faction without id"); continue; }
                            if (RtsFactions.ContainsKey(rf.Id)) Errors.Add($"{f.Path}: duplicate RTS faction id '{rf.Id}'");
                            else RtsFactionOrder.Add(rf.Id);
                            RtsFactions[rf.Id] = rf;
                        }
                        break;
                    case "shopCategories": foreach (var n in kv.Value.ArrayValue ?? new List<JsonNode>()) { var s = n.AsString(); if (!ShopCategories.Contains(s)) ShopCategories.Add(s); } break;
                    case "$schema": case "$comment": case "roster": break;
                    default: Warnings.Add($"{f.Path}: unknown top-level key '{kv.Key}'"); break;
                }
            }
        }

        private void AddHero(HeroDef h, string path)
        {
            if (string.IsNullOrEmpty(h.Id)) { Errors.Add($"{path}: hero without id"); return; }
            if (Heroes.ContainsKey(h.Id)) Errors.Add($"{path}: duplicate hero id '{h.Id}'");
            else HeroOrder.Add(h.Id);
            Heroes[h.Id] = h;
        }

        private void AddAbility(AbilityDef a, string path)
        {
            if (string.IsNullOrEmpty(a.Id)) { Errors.Add($"{path}: ability without id"); return; }
            if (Abilities.ContainsKey(a.Id)) Errors.Add($"{path}: duplicate ability id '{a.Id}'");
            Abilities[a.Id] = a;
            if (a.Statuses != null) foreach (var s in a.Statuses) AddStatus(s, path);
        }

        private void AddStatus(StatusDef s, string path)
        {
            if (string.IsNullOrEmpty(s.Id)) { Errors.Add($"{path}: status without id"); return; }
            if (Statuses.ContainsKey(s.Id)) Errors.Add($"{path}: duplicate status id '{s.Id}'");
            Statuses[s.Id] = s;
        }

        private void AddItem(ItemDef i, string path)
        {
            if (string.IsNullOrEmpty(i.Id)) { Errors.Add($"{path}: item without id"); return; }
            if (Items.ContainsKey(i.Id)) Errors.Add($"{path}: duplicate item id '{i.Id}'");
            else ItemOrder.Add(i.Id);
            Items[i.Id] = i;
            if (i.Active != null)
            {
                if (string.IsNullOrEmpty(i.Active.Id)) i.Active.Id = i.Id + "_active";
                if (string.IsNullOrEmpty(i.Active.Name)) i.Active.Name = i.Name;
                i.Active.Slot = AbilitySlot.Item;
                i.Active.MaxLevel = 1;
                AddAbility(i.Active, path);
            }
        }

        private void AddUnit(UnitDef u, string path)
        {
            if (string.IsNullOrEmpty(u.Id)) { Errors.Add($"{path}: unit without id"); return; }
            if (Units.ContainsKey(u.Id)) Errors.Add($"{path}: duplicate unit id '{u.Id}'");
            Units[u.Id] = u;
        }

        private void Link()
        {
            // Compute total item costs and reverse recipe links.
            var visiting = new HashSet<string>();
            foreach (var item in Items.Values) item.TotalCost = ComputeTotalCost(item, visiting);
            foreach (var item in Items.Values)
            {
                if (item.Components == null) continue;
                foreach (var c in item.Components)
                    if (Items.TryGetValue(c, out var comp) && !comp.BuildsInto.Contains(item.Id)) comp.BuildsInto.Add(item.Id);
                if (!string.IsNullOrEmpty(item.Category) && !ShopCategories.Contains(item.Category)) ShopCategories.Add(item.Category);
            }
        }

        private int ComputeTotalCost(ItemDef item, HashSet<string> visiting)
        {
            if (item.Components == null || item.Components.Count == 0) return item.Cost;
            if (!visiting.Add(item.Id)) { Errors.Add($"Item recipe cycle at '{item.Id}'"); return item.Cost; }
            int total = item.Cost;
            foreach (var c in item.Components)
            {
                if (Items.TryGetValue(c, out var comp)) total += ComputeTotalCost(comp, visiting);
                else Errors.Add($"Item '{item.Id}' references unknown component '{c}'");
            }
            visiting.Remove(item.Id);
            return total;
        }

        private void Validate()
        {
            foreach (var h in Heroes.Values)
            {
                if (!h.Playable) continue;
                foreach (var a in h.Abilities)
                    if (!Abilities.ContainsKey(a)) Errors.Add($"Hero '{h.Id}' references unknown ability '{a}'");
                if (h.RecommendedItems != null)
                    foreach (var kv in h.RecommendedItems)
                        foreach (var it in kv.Value)
                            if (!Items.ContainsKey(it)) Warnings.Add($"Hero '{h.Id}' recommends unknown item '{it}'");
            }
            foreach (var a in Abilities.Values) ValidateEffects(a.Id, a.OnCast);
            foreach (var a in Abilities.Values)
            {
                if (a.Triggers != null) foreach (var t in a.Triggers) ValidateEffects(a.Id, t.Effects);
                if (a.Aura != null && !Statuses.ContainsKey(a.Aura.Status ?? "")) Errors.Add($"Ability '{a.Id}' aura references unknown status '{a.Aura.Status}'");
                if (a.ToggleStatus != null && !Statuses.ContainsKey(a.ToggleStatus)) Errors.Add($"Ability '{a.Id}' toggles unknown status '{a.ToggleStatus}'");
                ValidateEffects(a.Id, a.OnChannelTick);
                ValidateEffects(a.Id, a.OnChannelEnd);
            }
            foreach (var s in Statuses.Values)
            {
                ValidateEffects(s.Id, s.OnInterval);
                ValidateEffects(s.Id, s.OnApply);
                ValidateEffects(s.Id, s.OnExpire);
                if (s.Triggers != null) foreach (var t in s.Triggers) ValidateEffects(s.Id, t.Effects);
            }
            foreach (var item in Items.Values)
            {
                if (item.Triggers != null) foreach (var t in item.Triggers) ValidateEffects(item.Id, t.Effects);
                if (item.Aura != null && !Statuses.ContainsKey(item.Aura.Status ?? "")) Errors.Add($"Item '{item.Id}' aura references unknown status '{item.Aura.Status}'");
            }
            foreach (var m in Maps.Values)
            {
                foreach (var s in m.Structures)
                    if (!Units.ContainsKey(s.UnitId)) Errors.Add($"Map '{m.Id}' structure '{s.Id}' uses unknown unit '{s.UnitId}'");
                foreach (var c in m.Camps)
                    if (!CampTypes.ContainsKey(c.CampType)) Errors.Add($"Map '{m.Id}' camp '{c.Id}' uses unknown camp type '{c.CampType}'");
            }
            foreach (var m in Maps.Values)
                foreach (var rn in m.ResourceNodes)
                    if (!Units.TryGetValue(rn.UnitId ?? "", out var rd) || rd.Kind != UnitKind.Resource) Errors.Add($"Map '{m.Id}' resource node '{rn.Id}' uses unknown resource unit '{rn.UnitId}'");
            ValidateRts();
            foreach (var c in CampTypes.Values)
                foreach (var v in c.Variants)
                    foreach (var u in v)
                        if (!Units.ContainsKey(u)) Errors.Add($"Camp type '{c.Id}' uses unknown unit '{u}'");
            if (Rules.Experience?.Cumulative == null || Rules.Experience.Cumulative.Length < Rules.MaxHeroLevel)
                Errors.Add("rules.experience.cumulative must define XP for every level");
        }

        private void ValidateRts()
        {
            void Refs(string owner, string what, List<string> ids, Func<UnitDef, bool> ok)
            {
                if (ids == null) return;
                foreach (var id in ids)
                    if (!Units.TryGetValue(id ?? "", out var d) || !ok(d)) Errors.Add($"Unit '{owner}' {what} '{id}' is not a valid unit");
            }
            foreach (var u in Units.Values)
            {
                Refs(u.Id, "trains", u.Trains, d => d.Kind != UnitKind.Building && d.Kind != UnitKind.Resource && d.BuildTime > 0);
                Refs(u.Id, "builds", u.Builds, d => d.Kind == UnitKind.Building && d.BuildTime > 0);
                Refs(u.Id, "requires", u.Requires, d => d.Kind == UnitKind.Building);
                if (u.Kind == UnitKind.Worker && u.GatherGold <= 0 && u.GatherLumber <= 0) Warnings.Add($"Worker '{u.Id}' gathers nothing");
            }
            foreach (var f in RtsFactions.Values)
            {
                if (!Units.TryGetValue(f.Hall ?? "", out var hall) || hall.Kind != UnitKind.Building) Errors.Add($"RTS faction '{f.Id}' hall '{f.Hall}' is not a building");
                else if (!hall.DropOffGold || !hall.DropOffLumber) Errors.Add($"RTS faction '{f.Id}' hall '{f.Hall}' must accept gold and lumber");
                if (!Units.TryGetValue(f.Worker ?? "", out var w) || w.Kind != UnitKind.Worker) Errors.Add($"RTS faction '{f.Id}' worker '{f.Worker}' is not a worker");
                foreach (var su in f.StartingUnits ?? new List<string>())
                    if (!Units.ContainsKey(su)) Errors.Add($"RTS faction '{f.Id}' starts with unknown unit '{su}'");
            }
        }

        private void ValidateEffects(string owner, List<EffectDef> effects)
        {
            if (effects == null) return;
            foreach (var e in effects)
            {
                if ((e.Type == EffectType.ApplyStatus || e.Type == EffectType.RemoveStatus) && !Statuses.ContainsKey(e.Status ?? ""))
                    Errors.Add($"'{owner}': effect {e.Type} references unknown status '{e.Status}'");
                if ((e.Type == EffectType.SpawnUnit || e.Type == EffectType.SummonAtTarget) && !Units.ContainsKey(e.UnitId ?? ""))
                    Errors.Add($"'{owner}': effect SpawnUnit references unknown unit '{e.UnitId}'");
                ValidateEffects(owner, e.Effects);
                ValidateEffects(owner, e.OnHit);
                ValidateEffects(owner, e.OnEnd);
                ValidateEffects(owner, e.OnCollide);
                ValidateEffects(owner, e.OnPass);
                ValidateEffects(owner, e.OnArrive);
                ValidateEffects(owner, e.OnTick);
                ValidateEffects(owner, e.OnExpire);
            }
        }

        public HeroDef Hero(string id) => Heroes.TryGetValue(id, out var h) ? h : throw new KeyNotFoundException($"Unknown hero '{id}'");
        public UnitDef Unit(string id) => Units.TryGetValue(id, out var u) ? u : throw new KeyNotFoundException($"Unknown unit '{id}'");
        public AbilityDef Ability(string id) => Abilities.TryGetValue(id, out var a) ? a : throw new KeyNotFoundException($"Unknown ability '{id}'");
        public StatusDef Status(string id) => Statuses.TryGetValue(id, out var s) ? s : throw new KeyNotFoundException($"Unknown status '{id}'");
        public ItemDef Item(string id) => Items.TryGetValue(id, out var i) ? i : throw new KeyNotFoundException($"Unknown item '{id}'");

        public IEnumerable<HeroDef> PlayableHeroes() => HeroOrder.Select(id => Heroes[id]).Where(h => h.Playable);
    }
}
