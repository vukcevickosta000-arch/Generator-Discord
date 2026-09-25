# Item Database

Items are data: `Shared/Runtime/Resources/GameData/items/*.json`.

- **Stats** come from `modifiers`: flat values; percentage stats are stored as fractions.
- **Passives** are `triggers` and `aura`.
- **Actives** are an `active` ability (the same ability format as heroes).
- **Recipes** are `components` plus a recipe `cost`.

Prices are computed recursively. Items with `cost: 0` and components combine automatically.

## Implemented items

<!-- GENERATED:BEGIN (Tools/dev/gen_item_docs.py) -->
**38 items implemented.**

| Item | Category | Total cost | Recipe | Stats | Notes |
|---|---|---|---|---|---|
| Ring of the Wellspring | Arcane | 175 |  | +1.5 HpRegen |  |
| Sapphire Band | Arcane | 175 |  | +0.75 ManaRegen |  |
| Void Cloak | Arcane | 450 |  | +15% MagicResist |  |
| Arcane Crystal | Arcane | 500 |  | +150 MaxMana |  |
| Vitality Charm | Arcane | 900 |  | +250 MaxHp |  |
| Ring of Warding | Armaments | 175 |  | +2 Armor |  |
| Rusted Blade | Armaments | 300 |  | +8 BonusDamage |  |
| Quickblade Gloves | Armaments | 450 |  | +15 AttackSpeed |  |
| Blackened Chainmail | Armaments | 550 |  | +5 Armor |  |
| Bloodstone Shard | Armaments | 900 |  | +10% Lifesteal |  |
| Warforged Broadsword | Armaments | 1000 |  | +18 BonusDamage |  |
| Headsman's Axe | Armaments | 1350 |  | +24 BonusDamage |  |
| Gauntlets of Might | Attributes | 150 |  | +3 Str |  |
| Nightrunner Slippers | Attributes | 150 |  | +3 Agi |  |
| Mantle of Lore | Attributes | 150 |  | +3 Int |  |
| Iron Circlet | Attributes | 165 |  | +2 AllAttributes |  |
| Brute's Belt | Attributes | 450 |  | +6 Str |  |
| Serpent Bands | Attributes | 450 |  | +6 Agi |  |
| Scholar's Cowl | Attributes | 450 |  | +6 Int |  |
| Worn Boots | Boots | 500 |  | +0.55 MoveSpeed |  |
| Pilgrim's Greaves | Boots | 925 | Worn Boots + Sapphire Band + 250 | +0.7 MoveSpeed, +1.5 ManaRegen, +1 HpRegen |  |
| Marchers of Haste | Boots | 950 | Worn Boots + Quickblade Gloves + 0 | +0.6 MoveSpeed, +25 AttackSpeed |  |
| Ember of Clarity | Consumables | 50 |  |  | Active: yes |
| Crimson Draught | Consumables | 90 |  |  | Active: yes |
| Waystone Shard | Consumables | 90 |  |  | Active: yes |
| Grave-Ward Amulet | Defense | 1200 | Void Cloak + Ring of the Wellspring + Ring of the Wellspring + 400 | +20% MagicResist, +4 HpRegen | Active: yes |
| Bulwark of the Last Keep | Defense | 1850 | Blackened Chainmail + Vitality Charm + 400 | +8 Armor, +300 MaxHp | Passive |
| Heart of the Colossus | Defense | 3100 | Vitality Charm + Brute's Belt + Brute's Belt + 1300 | +40 Str, +400 MaxHp, +1.6% HpRegenPct |  |
| Stormcaller's Rod | Magic | 1450 | Scholar's Cowl + Arcane Crystal + 500 | +10 Int, +200 MaxMana | Active: yes |
| Sanguine Scepter | Magic | 1800 | Scholar's Cowl + Bloodstone Shard + 450 | +12 Int, +8% SpellAmp, +15% SpellVamp |  |
| Shadowstep Talisman | Magic | 2250 |  |  | Active: yes; Passive |
| Mask of the Thirsting | Offense | 1400 | Bloodstone Shard + Rusted Blade + 200 | +15 BonusDamage, +16% Lifesteal | Active: yes |
| Voidpiercer | Offense | 1950 | Warforged Broadsword + Quickblade Gloves + 500 | +25 BonusDamage, +20 AttackSpeed | Passive |
| Cleaver of the Headsman | Offense | 2400 | Headsman's Axe + Brute's Belt + 600 | +30 BonusDamage, +8 Str | Passive |
| Nightfang Greatsword | Offense | 3350 | Warforged Broadsword + Headsman's Axe + 1000 | +50 BonusDamage, +30% CritChance, +2 CritMultiplier |  |
| Crown of Mercy | Support | 815 | Iron Circlet + Ring of Warding + Ring of the Wellspring + 300 | +2 AllAttributes | Active: yes; Passive |
| Truesight Lantern | Wards | 50 |  |  | Active: yes |
| Watcher's Eye | Wards | 75 |  |  | Active: yes |
<!-- GENERATED:END -->

## Shops

| Shop | Location | Current behaviour |
|---|---|---|
| Base shop | At each fountain | Purchases go straight into the inventory. All 38 current items are base-shop items. |
| Side shops | Two, near the outer lanes | Map locations and `ItemShop.Side` range checks exist. **Not yet used:** buying there still goes to the stash (TODO T-021). |
| Secret shops | Two, deep in the jungle | Range check exists and is enforced for items flagged `shop: Secret`. No secret items exist yet. |

Purchases made away from the base go to the 6-slot stash, which delivers periodically.

## Planned catalogue (target 150–200)

| Category | Now | Target | Planned additions (names are working titles) |
|---|---|---|---|
| Consumables | 3 | 10 | Smoke of Veils, Tome of Old Blood, Moonwater Flask, Dust of Revealing, Grave Salt |
| Wards | 2 | 4 | Watcher's Totem (upgraded observer), Lantern Post (area true sight) |
| Attributes | 7 | 14 | Bloodstone Diadem, Warlord's Girdle, Nightshade Band, … |
| Armaments | 7 | 16 | Gravecleaver, Moonsilver Rapier, Thornwhip, … |
| Arcane | 5 | 12 | Ashen Codex, Witchlight Orb, Pactbinder Staff, … |
| Boots | 3 | 8 | Treads of the Tyrant, Pilgrim's Sandals, Nightstalker Boots, Sunstrider Greaves |
| Offense | 4 | 30 | Crimson Maw (lifesteal carry), Executioner's Writ, Titan Breaker (armor reduction), … |
| Defense | 3 | 25 | Gargoyle Hide, Warden's Bulwark, Veil of Thorns (reflect), … |
| Magic | 3 | 25 | Sceptre of Covenants (ultimate upgrade), Eclipse Lens, Blood Pact Grimoire, … |
| Support | 1 | 20 | Chalice of Mercy, Banner of the Dawn, Lamplighter's Crook, … |
| Relics (Vharoth) | 0 | 3 | Heart of Vharoth (event reward), Titan's Tooth, Moon-Blood Vial |

**Design rules:**

- Every item has a clear role.
- Active items use the shared ability framework, so their behaviour is authoritative on the server.
- No item may duplicate a HoN or Dota item's exact identity. Mechanics may be familiar genre staples (lifesteal,
  blink, magic-immunity-style effects) but names, numbers and combinations are original.
