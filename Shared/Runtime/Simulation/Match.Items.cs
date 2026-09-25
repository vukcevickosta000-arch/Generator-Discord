using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bloodfall.Data;

namespace Bloodfall.Simulation
{
    /// <summary>
    /// Inventory, shop and recipe logic. Slot numbering (shared with the network protocol and HUD):
    ///   0..5   main inventory (active items + passives)
    ///   6..8   backpack (no passives, no actives)
    ///   10..15 stash (at base)
    /// </summary>
    public sealed partial class Match
    {
        public const int BackpackSlotBase = 6;
        public const int StashSlotBase = 10;

        public bool InShopRange(Unit hero, ItemShop shop)
        {
            if (hero == null) return false;
            switch (shop)
            {
                case ItemShop.Main:
                {
                    var b = Map.Bases.FirstOrDefault(x => x.Team == hero.Team);
                    if (b == null) return true;
                    return Vector2.Distance(hero.Position, b.ShopPosition) <= Rules.ShopRange || Vector2.Distance(hero.Position, b.Fountain) <= b.FountainRadius + 2f;
                }
                case ItemShop.Secret:
                    foreach (var s in Map.SecretShops) if (Vector2.Distance(hero.Position, s) <= Rules.ShopRange * 0.6f) return true;
                    return false;
                case ItemShop.Side:
                    foreach (var s in Map.SideShops) if (Vector2.Distance(hero.Position, s) <= Rules.ShopRange * 0.6f) return true;
                    return InShopRange(hero, ItemShop.Main);
                default: return false;
            }
        }

        public bool AtBase(Unit hero) => InShopRange(hero, ItemShop.Main);

        public ItemInstance GetItemAt(Unit u, int slot)
        {
            if (u.Inventory == null) return null;
            if (slot >= 0 && slot < u.Inventory.Length) return u.Inventory[slot];
            if (slot >= BackpackSlotBase && slot < BackpackSlotBase + u.Backpack.Length) return u.Backpack[slot - BackpackSlotBase];
            if (slot >= StashSlotBase && slot < StashSlotBase + u.Stash.Length) return u.Stash[slot - StashSlotBase];
            return null;
        }

        private void SetItemAt(Unit u, int slot, ItemInstance it)
        {
            if (slot >= 0 && slot < u.Inventory.Length) u.Inventory[slot] = it;
            else if (slot >= BackpackSlotBase && slot < BackpackSlotBase + u.Backpack.Length) u.Backpack[slot - BackpackSlotBase] = it;
            else if (slot >= StashSlotBase && slot < StashSlotBase + u.Stash.Length) u.Stash[slot - StashSlotBase] = it;
            u.StatsDirty = true;
        }

        private IEnumerable<int> AllSlots(Unit u)
        {
            for (int i = 0; i < u.Inventory.Length; i++) yield return i;
            for (int i = 0; i < u.Backpack.Length; i++) yield return BackpackSlotBase + i;
            for (int i = 0; i < u.Stash.Length; i++) yield return StashSlotBase + i;
        }

        public int FindItemSlot(Unit u, ItemInstance it)
        {
            foreach (var s in AllSlots(u)) if (GetItemAt(u, s) == it) return s;
            return -1;
        }

        public void RemoveItem(Unit u, ItemInstance it)
        {
            int s = FindItemSlot(u, it);
            if (s >= 0) SetItemAt(u, s, null);
        }

        /// <summary>Gold required to buy 'item' given the components the hero already owns (in any container).</summary>
        public int PurchaseCost(Unit hero, ItemDef item, out List<ItemInstance> consumed, out bool needsSecretShop)
        {
            consumed = new List<ItemInstance>();
            needsSecretShop = false;
            var owned = hero.Inventory == null ? new List<ItemInstance>() : AllSlots(hero).Select(s => GetItemAt(hero, s)).Where(i => i != null).ToList();
            int cost = CostRecursive(item, owned, consumed, top: true, ref needsSecretShop);
            return cost;
        }

        private int CostRecursive(ItemDef item, List<ItemInstance> owned, List<ItemInstance> consumed, bool top, ref bool secret)
        {
            if (!top)
            {
                var have = owned.FirstOrDefault(i => i.Def == item && !consumed.Contains(i));
                if (have != null) { consumed.Add(have); return 0; }
            }
            if (item.Components == null || item.Components.Count == 0)
            {
                if (item.Shop == ItemShop.Secret) secret = true;
                return item.Cost;
            }
            int total = item.Cost;
            foreach (var cid in item.Components)
                if (Data.Items.TryGetValue(cid, out var comp)) total += CostRecursive(comp, owned, consumed, false, ref secret);
            return total;
        }

        public void TryBuyItem(Unit hero, string itemId)
        {
            var p = hero?.Owner;
            if (p == null || hero.Inventory == null) return;
            if (!Data.Items.TryGetValue(itemId ?? "", out var def) || !def.Purchasable) { EmitError(hero, "That item cannot be purchased."); return; }
            if (Phase != MatchPhase.PreGame && Phase != MatchPhase.Playing) return;

            int cost = PurchaseCost(hero, def, out var consumed, out bool secret);
            if (secret && !InShopRange(hero, ItemShop.Secret) && !hero.Dead)
            {
                EmitError(hero, "Must be purchased at a Secret Shop.");
                return;
            }
            if (p.Gold < cost) { EmitError(hero, $"Not enough gold ({cost - p.Gold} more needed)."); return; }

            // Stack consumables onto an existing stack.
            if (def.MaxStack > 1)
            {
                foreach (var s in AllSlots(hero))
                {
                    var it = GetItemAt(hero, s);
                    if (it != null && it.Def == def && it.Charges < def.MaxStack)
                    {
                        if (s >= StashSlotBase && !AtBase(hero) && !hero.Dead) continue;
                        p.Gold -= cost; p.GoldSpent += cost;
                        it.Charges = Math.Min(def.MaxStack, it.Charges + Math.Max(1, def.InitialCharges));
                        hero.StatsDirty = true;
                        OnPurchased(hero, def, cost);
                        return;
                    }
                }
            }

            int slot = FindFreeSlot(hero, preferStash: !AtBase(hero) && !hero.Dead, consumed);
            if (slot < 0) { EmitError(hero, "Inventory and stash are full."); return; }

            foreach (var c in consumed) RemoveItem(hero, c);
            var inst = CreateItemInstance(def, p);
            SetItemAt(hero, slot, inst);
            p.Gold -= cost;
            p.GoldSpent += cost;
            p.ItemPurchaseLog.Add($"{(int)MatchSeconds}:{def.Id}");
            OnPurchased(hero, def, cost);
        }

        private void OnPurchased(Unit hero, ItemDef def, int cost)
        {
            Emit(new SimEvent { Type = SimEventType.ItemPurchased, UnitId = hero.Id, Key = def.Id, Value = cost, PlayerId = -1 });
            TryAutoCombine(hero);
        }

        public ItemInstance CreateItemInstance(ItemDef def, Player p)
        {
            var inst = new ItemInstance
            {
                Def = def,
                Charges = def.InitialCharges > 0 ? def.InitialCharges : (def.MaxStack > 1 ? 1 : 0),
                PurchaseTime = Time,
                PurchaserPlayer = p?.Id ?? -1,
            };
            if (def.Active != null)
                inst.Active = new AbilityInstance { Def = def.Active, Level = 1, Item = inst, Charges = def.Active.MaxCharges, Index = -1 };
            return inst;
        }

        private int FindFreeSlot(Unit hero, bool preferStash, List<ItemInstance> consumed)
        {
            // A slot freed by consumed components can be reused.
            if (!preferStash)
            {
                for (int i = 0; i < hero.Inventory.Length; i++) if (hero.Inventory[i] == null || consumed.Contains(hero.Inventory[i])) return i;
                for (int i = 0; i < hero.Backpack.Length; i++) if (hero.Backpack[i] == null || consumed.Contains(hero.Backpack[i])) return BackpackSlotBase + i;
            }
            else
            {
                // Components carried on the hero combine in place.
                for (int i = 0; i < hero.Inventory.Length; i++) if (hero.Inventory[i] != null && consumed.Contains(hero.Inventory[i])) return i;
            }
            for (int i = 0; i < hero.Stash.Length; i++) if (hero.Stash[i] == null || consumed.Contains(hero.Stash[i])) return StashSlotBase + i;
            return -1;
        }

        /// <summary>Combines pure recipes (cost 0) whose components are all owned.</summary>
        private void TryAutoCombine(Unit hero)
        {
            bool changed = true;
            int guard = 8;
            while (changed && guard-- > 0)
            {
                changed = false;
                foreach (var def in Data.Items.Values)
                {
                    if (def.Components == null || def.Components.Count == 0 || def.Cost != 0) continue;
                    var owned = AllSlots(hero).Select(s => GetItemAt(hero, s)).Where(i => i != null).ToList();
                    var used = new List<ItemInstance>();
                    bool all = true;
                    foreach (var c in def.Components)
                    {
                        var have = owned.FirstOrDefault(i => i.Def.Id == c && !used.Contains(i));
                        if (have == null) { all = false; break; }
                        used.Add(have);
                    }
                    if (!all) continue;
                    int slot = FindItemSlot(hero, used[0]);
                    foreach (var u in used) RemoveItem(hero, u);
                    SetItemAt(hero, slot, CreateItemInstance(def, hero.Owner));
                    changed = true;
                }
            }
        }

        public int SellValue(ItemInstance it)
        {
            bool fullRefund = Time - it.PurchaseTime <= Rules.SellFullRefundWindow && !it.UsedSincePurchase;
            int value = fullRefund ? it.Def.TotalCost : (int)(it.Def.TotalCost * Rules.SellFraction);
            if (it.Def.MaxStack > 1 && it.Def.InitialCharges > 0) value = value * Math.Max(1, it.Charges) / Math.Max(1, it.Def.InitialCharges);
            return value;
        }

        public void TrySellItem(Unit hero, int slot)
        {
            var p = hero?.Owner;
            if (p == null || hero.Inventory == null) return;
            var it = GetItemAt(hero, slot);
            if (it == null) return;
            bool nearShop = hero.Dead || InShopRange(hero, ItemShop.Main) || InShopRange(hero, ItemShop.Secret) || InShopRange(hero, ItemShop.Side) || slot >= StashSlotBase;
            if (!nearShop) { EmitError(hero, "You must be near a shop to sell items."); return; }
            int value = SellValue(it);
            SetItemAt(hero, slot, null);
            GiveGold(p, value, hero.Position, false);
            p.GoldEarned -= value; // selling is not income
            Emit(new SimEvent { Type = SimEventType.ItemSold, UnitId = hero.Id, Key = it.Def.Id, Value = value, PlayerId = -1 });
        }

        public void SwapItems(Unit hero, int a, int b)
        {
            if (hero?.Inventory == null || a == b) return;
            bool stashInvolved = a >= StashSlotBase || b >= StashSlotBase;
            if (stashInvolved && !AtBase(hero) && !hero.Dead) { EmitError(hero, "The stash can only be accessed at base."); return; }
            var ia = GetItemAt(hero, a);
            var ib = GetItemAt(hero, b);
            if (!ValidSlot(hero, a) || !ValidSlot(hero, b)) return;
            SetItemAt(hero, a, ib);
            SetItemAt(hero, b, ia);
            // Moving an item into the backpack puts its active on a short cooldown (anti-abuse, classic rule).
            if (ia?.Active != null && b >= BackpackSlotBase && b < StashSlotBase) ia.Active.Cooldown = Math.Max(ia.Active.Cooldown, 6f);
            if (ib?.Active != null && a >= BackpackSlotBase && a < StashSlotBase) ib.Active.Cooldown = Math.Max(ib.Active.Cooldown, 6f);
        }

        private bool ValidSlot(Unit u, int s) =>
            (s >= 0 && s < u.Inventory.Length) || (s >= BackpackSlotBase && s < BackpackSlotBase + u.Backpack.Length) || (s >= StashSlotBase && s < StashSlotBase + u.Stash.Length);

        /// <summary>Moves stash items onto the hero while standing at base.</summary>
        private void DeliverStash(Unit hero)
        {
            if (hero.Stash == null || hero.Dead || !AtBase(hero)) return;
            for (int i = 0; i < hero.Stash.Length; i++)
            {
                var it = hero.Stash[i];
                if (it == null) continue;
                int free = Array.IndexOf(hero.Inventory, null);
                if (free >= 0) { hero.Inventory[free] = it; hero.Stash[i] = null; hero.StatsDirty = true; continue; }
                int bp = Array.IndexOf(hero.Backpack, null);
                if (bp >= 0) { hero.Backpack[bp] = it; hero.Stash[i] = null; }
            }
            TryAutoCombine(hero);
        }
    }
}
