# Hero Roster (96 planned)

Four factions with 24 heroes each. All names, lore and kits are original.

| Status | Meaning |
|---|---|
| ✅ **Implemented** | Playable: data in `Shared/Runtime/Resources/GameData/heroes/`, used by bots, covered by tests |
| 🟡 **Concept** | Full kit designed (below) and next in line for implementation |
| ⚪ **Planned** | Identity and kit direction fixed; numbers not yet designed |

**Attribute:** STR, AGI or INT. **Attack:** M = melee, R = ranged.

The eight *concept heroes* named in the original brief are Vorak, Nyxara, Malgrave, Ardyn, Fenrax, Morwen, Thael and
Ilyra. Where the brief gave only a name, the kits below are this project's design, aligned with each faction's
identity.

---

## Concept kits

### ✅ Vorak, the Blood Tyrant — Crimson Court · STR · M · Initiator / Durable
- **Lord of the Feast** (innate): heals when enemy units die within 7 m.
- **Crimson Charge** (Q): dashes forward; the first enemy hero hit is stunned.
- **Blood Rend** (W): a sweeping slash that deals AoE damage and applies a stacking bleed.
- **Sovereign's Wrath** (E): every 4th attack bashes for bonus damage.
- **Bloodfall** (R): an invulnerable leap; landing stuns within 4 m and marks enemies with *Blood Tithe* (they take
  amplified damage).

### ✅ Ilyra, the Blood Witch — Crimson Court · INT · R · Nuker / Support
- **Sanguine Covenant** (innate): spending health grants *Blood Surge* stacks (spell amplification).
- **Blood Lance** (Q): a linear skillshot paid in health.
- **Hemorrhage Field** (W): a delayed eruption at a point that leaves a draining pool.
- **Sanguine Offering** (E): sacrifices HP to shield and heal an ally.
- **Exsanguinate** (R): a channelled drain on one target; ends in a burst and stun.

### 🟡 Nyxara, the Night Countess — Crimson Court · AGI · M · Assassin / Escape
- **Velvet Dark** (innate): out of vision of enemy heroes for 3 s, her next attack crits for 180%.
- **Batwing Step** (Q): blinks up to 9 m as a swarm of bats; breaks target lock on her.
- **Kiss of Thorns** (W): strikes one target, applying *Crimson Mark* (5 s). Attacks on marked targets heal her.
- **Countess's Veil** (E): 2.5 s of invisibility; 20% faster while invisible.
- **Midnight Sentence** (R): teleports behind a marked enemy hero and executes them if below 22/28/34% HP. Otherwise
  deals pure damage.

### 🟡 Malgrave, the Bone Tyrant — Ashen Legion · INT · R · Summoner / Pusher
- **Ossuary** (innate): nearby corpses become *bone shards*; each shard empowers his next spell by 4%.
- **Raise the Fallen** (Q): consumes up to 3 corpses to raise skeletal legionnaires for 30 s.
- **Bone Wall** (W): a curved wall 8 m long for 5 s. Enemies touching it are slowed.
- **Grave Chill** (E): a cone that slows and deals magic damage; kills leave corpses even from heroes.
- **Legion of the Pale** (R): opens a crypt for 8 s, spawning 6 armoured revenants that march toward the target
  point.

### 🟡 Ardyn, the Dawnbringer — Dawnguard · STR · M · Durable / Support
- **Oathkeeper** (innate): allied heroes within 9 m gain +2 armor. The bonus doubles while Ardyn is below 40% HP.
- **Aegis of Dawn** (Q): absorbs 120–360 damage on an ally for 6 s; explodes for holy damage when it expires or breaks.
- **Consecrate** (W): blesses the ground (6 m) for 5 s, healing allies and burning undead/Dusk summons.
- **Judgment Strike** (E): next attack mini-stuns and deals bonus damage equal to 8% of the target's missing health.
- **Radiant Crusade** (R): Ardyn and nearby allies become unstoppable for 2 s. Ardyn also charges to a point, knocking
  enemies aside and blinding them.

### 🟡 Fenrax, the Moonfang — Wild Covenant · AGI · M · Carry / Jungler
- **Lunar Hunger** (innate): at night +10% move speed and +20 attack speed.
- **Pounce** (Q): leaps onto an enemy, rooting for 1 s.
- **Rending Claws** (W): attacks shred 2 armor per hit (stacks 6).
- **Howl of the Pack** (E): summons two spirit wolves for 20 s; allies nearby gain attack damage.
- **Night Unleashed** (R): transforms for 18 s into a werewolf (+40% HP, cleaves). Forces night for everyone for
  8 s. Cannot be dispelled.

### 🟡 Morwen, the Hedge Witch — Wild Covenant · INT · R · Disabler / Support
- **Old Pact** (innate): hexed enemies take 15% more magic damage.
- **Toad Hex** (Q): hexes a target (1.5–3 s).
- **Bubbling Cauldron** (W): places a cauldron (12 s) that heals allies in range and slows enemies.
- **Crooked Curse** (E): a curse whose damage is dealt again when the target casts a spell.
- **Witching Hour** (R): for 6 s every ability she casts is echoed a second time at 50% power.

### 🟡 Thael, the Wildwood Warden — Wild Covenant · STR · M · Initiator / Durable
- **Barkskin** (innate): +1 armor per 2 nearby trees (max +8).
- **Grasping Roots** (Q): roots all enemies in a line after 0.6 s.
- **Summon Treant** (W): turns a tree into a treant ally for 25 s.
- **Grove Call** (E): teleports to any tree within 60 m after a 2 s channel.
- **Wrath of the Old Forest** (R): the forest awakens in a 10 m radius. Trees lash nearby enemies every 0.5 s for 5 s.
  Thael gains 30% damage reduction.

---

## Crimson Court — vampire nobility, blood knights, gargoyles, cursed assassins

| # | Hero | Title | Attr | Atk | Roles | Kit direction | Status |
|---|---|---|---|---|---|---|---|
| 1 | Vorak | the Blood Tyrant | STR | M | Initiator, Durable | Charge, bleed, bash, stunning leap | ✅ |
| 2 | Ilyra | the Blood Witch | INT | R | Nuker, Support | Health-cost spells, blood pool, drain | ✅ |
| 3 | Nyxara | the Night Countess | AGI | M | Assassin, Escape | Bat blink, marks, execute | 🟡 |
| 4 | Sereth Vane | the Crimson Duelist | AGI | M | Carry | Parry stance, riposte counters | ⚪ |
| 5 | Isolde Marrow | the Chalice Queen | INT | R | Support | Chalice heals, charm | ⚪ |
| 6 | Grimbane | the Gargoyle Sentinel | STR | M | Durable, Disabler | Stone form, dive from perch | ⚪ |
| 7 | Castor Dusk | the Lamplighter of Graves | INT | R | Nuker | Soul lantern, will-o'-wisps | ⚪ |
| 8 | Veyla | the Whispering Blade | AGI | M | Assassin | Invisibility, poisoned daggers | ⚪ |
| 9 | Kaelthorne | the Iron Baron | STR | M | Durable | Blood armour, tithe aura | ⚪ |
| 10 | Morthaine | the Swarm | AGI | R | Carry, Escape | Splits into bats | ⚪ |
| 11 | Ashvere | the Renegade Inquisitor | INT | R | Disabler | Blood chains, silence | ⚪ |
| 12 | Dravenne | the Moonless Huntress | AGI | R | Carry | Crossbow, night bonuses | ⚪ |
| 13 | Seraphel | the Fallen | STR | M | Initiator | Blood-wing flight, dive | ⚪ |
| 14 | Lysandre | the Mirror Bride | INT | R | Nuker, Escape | Mirror images, swaps | ⚪ |
| 15 | Hollowmere | the Cathedral Horror | STR | M | Durable | Bell tolls, fear | ⚪ |
| 16 | Rhaegor | the Blood-Iron Champion | STR | M | Carry | Weapon grows with kills | ⚪ |
| 17 | Vesper | the Silent Chorister | INT | R | Support, Disabler | Hymn silences | ⚪ |
| 18 | Othniel | the Flesh Tailor | INT | M | Support | Stitches allies, abominations | ⚪ |
| 19 | Talon | the Spire Gargoyle | AGI | R | Ganker | Diving strikes, stone skin | ⚪ |
| 20 | Corvina | the Raven Courtesan | INT | R | Disabler | Ravens blind, whispers | ⚪ |
| 21 | Malachar | the Thornblood Knight | STR | M | Initiator | Thorn reflection | ⚪ |
| 22 | Elowen Sable | the Sanguine Oracle | INT | R | Support | Foresight, delayed damage | ⚪ |
| 23 | Varric Ashmoor | the Oathless | STR | M | Carry, Durable | Cursed blade, lifesteal | ⚪ |
| 24 | Nocturne | the Last Heir | AGI | M | Carry | Blood stacks from kills | ⚪ |

## Ashen Legion — necromancers, skeletal hosts, plague cults, corpse engines

| # | Hero | Title | Attr | Atk | Roles | Kit direction | Status |
|---|---|---|---|---|---|---|---|
| 1 | Malgrave | the Bone Tyrant | INT | R | Summoner, Pusher | Raise dead, bone wall | 🟡 |
| 2 | Morrowmaw | the Corpse Engine | STR | M | Durable, Pusher | Siege engine of corpses | ⚪ |
| 3 | Sister Pallor | the Plague Mother | INT | R | Nuker | Contagious damage over time | ⚪ |
| 4 | Kessrith | the Bone Archer King | AGI | R | Carry | Bone arrows, volley | ⚪ |
| 5 | Vulgrim | the Gravedigger | STR | M | Disabler | Buries enemies, shovel | ⚪ |
| 6 | Dren | the Ossuary Knight | STR | M | Durable | Bone armour plating | ⚪ |
| 7 | The Hollow Choir | — | INT | R | Support | Ghost choir, sustain | ⚪ |
| 8 | Rotfang | the Carrion Hound | AGI | M | Jungler | Feast on corpses | ⚪ |
| 9 | Varuun | the Lich-Magister | INT | R | Carry (caster) | Phylactery, frost | ⚪ |
| 10 | Yselde | the Ashcaller | INT | R | Nuker | Ash storms, blind | ⚪ |
| 11 | Grimsoul | the Reaper of Tithes | STR | M | Carry | Scythe executes | ⚪ |
| 12 | Ulgar | the Bonewright | INT | R | Pusher | Bone turrets | ⚪ |
| 13 | Hekra | the Scourgeborn | AGI | M | Assassin | Plague blades | ⚪ |
| 14 | The Pale Emissary | — | INT | R | Disabler | Fear, terror auras | ⚪ |
| 15 | Cindermaw | the Pyre Colossus | STR | M | Initiator | Corpse fire, burn | ⚪ |
| 16 | Mother Moth | — | INT | R | Support | Moth swarms, sleep | ⚪ |
| 17 | Solace | the Deathwarden | STR | M | Support | Brief "cannot die" aura | ⚪ |
| 18 | Vezmorah | the Crypt Lord | STR | M | Durable, Initiator | Burrow, impale | ⚪ |
| 19 | Ilveth | the Skullspeaker | INT | R | Nuker | Talking skull familiar | ⚪ |
| 20 | Wraith of Eldermoor | — | AGI | M | Escape | Phase through units | ⚪ |
| 21 | Ossian | the Grand Embalmer | INT | R | Disabler | Embalming wraps | ⚪ |
| 22 | The Chained Colossus | — | STR | M | Durable | Chains, drag | ⚪ |
| 23 | Blightrunner | — | AGI | M | Escape, Ganker | Plague trail | ⚪ |
| 24 | Sepulchra | the Tomb Queen | INT | R | Nuker | Crypt prison ultimate | ⚪ |

## Wild Covenant — werewolves, witches, druids, forest spirits under lunar pacts

| # | Hero | Title | Attr | Atk | Roles | Kit direction | Status |
|---|---|---|---|---|---|---|---|
| 1 | Fenrax | the Moonfang | AGI | M | Carry, Jungler | Werewolf form, pounce | 🟡 |
| 2 | Morwen | the Hedge Witch | INT | R | Disabler, Support | Hex, cauldron, echo | 🟡 |
| 3 | Thael | the Wildwood Warden | STR | M | Initiator, Durable | Roots, treants, forest | 🟡 |
| 4 | Ursolmar | the Barrow Bear | STR | M | Durable | Maul, hibernate | ⚪ |
| 5 | Kithra | the Owl Seer | INT | R | Support | Owl scouts, vision | ⚪ |
| 6 | Briarheart | — | STR | M | Durable | Thorn body | ⚪ |
| 7 | Luneth | the Silver Stag | AGI | M | Initiator | Charging antlers | ⚪ |
| 8 | Grimtooth | the Alpha | AGI | M | Carry | Pack bonuses | ⚪ |
| 9 | Old Mother Yew | — | INT | R | Support | Tree healing | ⚪ |
| 10 | Sable Vix | the Fox Trickster | AGI | M | Escape | Illusions, feints | ⚪ |
| 11 | Mossbeard | — | STR | M | Support | Moss armour, regrowth | ⚪ |
| 12 | Ravenna Blackfeather | — | INT | R | Nuker | Crow shapeshift | ⚪ |
| 13 | Nerissa | the Marsh Spirit | INT | R | Disabler | Bog, drowning | ⚪ |
| 14 | The Hollow Stag | — | STR | M | Initiator | Spirit charge | ⚪ |
| 15 | Agathe | the Bramblewitch | INT | R | Nuker | Thorn damage over time | ⚪ |
| 16 | Varkul | the Blood Moon Berserker | STR | M | Carry | Rage, frenzy | ⚪ |
| 17 | Isra Silverclaw | — | AGI | M | Assassin | Silver claws (anti-regen) | ⚪ |
| 18 | The Green Knight of Harrowmere | — | STR | M | Durable | Beheading pact | ⚪ |
| 19 | Sprigg | the Mushroom Shaman | INT | R | Pusher | Spores, fungus traps | ⚪ |
| 20 | Ansa | the Wolfmother | STR | M | Summoner | Wolf pack | ⚪ |
| 21 | Stormhorn | the Thunder Elk | STR | M | Initiator | Thunderclap | ⚪ |
| 22 | Mira Thornveil | — | AGI | R | Assassin | Poison darts | ⚪ |
| 23 | Aldric | the Grovekeeper | INT | R | Support | Grove sanctuary | ⚪ |
| 24 | Lunara | the Eclipse Priestess | INT | R | Nuker | Lunar beams, eclipse | ⚪ |

## Dawnguard — paladins, monster hunters, battle priests of a zealous crusade

| # | Hero | Title | Attr | Atk | Roles | Kit direction | Status |
|---|---|---|---|---|---|---|---|
| 1 | Ardyn | the Dawnbringer | STR | M | Durable, Support | Aegis, consecration, crusade | 🟡 |
| 2 | Sister Cassia | the Flame Confessor | INT | R | Nuker | Purifying fire | ⚪ |
| 3 | Brennock Vail | the Stake Hunter | AGI | R | Carry | Silver bolts, traps | ⚪ |
| 4 | Oswin | the High Templar | STR | M | Durable | Aegis, taunt | ⚪ |
| 5 | Mercy Halloway | the Battle Priest | INT | R | Support | Heals, resurrection prayer | ⚪ |
| 6 | Gideon | the Witchfinder | AGI | R | Anti-mage | Mana burn, purge | ⚪ |
| 7 | Aurelia | the Seraph Commander | STR | M | Initiator | Winged descent | ⚪ |
| 8 | Brother Tobias | the Bellringer | STR | M | Disabler | Bell stuns | ⚪ |
| 9 | Lucan Hale | the Monster Slayer | AGI | M | Carry | Bonus versus summons/heroes | ⚪ |
| 10 | Maerith | the Inquisitor | INT | R | Disabler | Chains of faith | ⚪ |
| 11 | Roderic | the Lionheart | STR | M | Initiator | Cavalry charge | ⚪ |
| 12 | The Pilgrim | — | STR | M | Support | Escort, sanctuary | ⚪ |
| 13 | Deacon Vale | the Lamplight | INT | R | Support | True-sight lamps | ⚪ |
| 14 | Isabeau | the Sunlancer | AGI | R | Carry | Thrown sun lances | ⚪ |
| 15 | Thorne | the Wall Warden | STR | M | Durable | Fortifications | ⚪ |
| 16 | Emmerich | the Brightforge Artificer | INT | R | Pusher | Holy turrets | ⚪ |
| 17 | Solenne | the Radiant Archer | AGI | R | Carry | Light arrows, blind | ⚪ |
| 18 | Halbrecht | the Justicar | STR | M | Carry | Execution strikes | ⚪ |
| 19 | Ignatius | the Canon | INT | R | Disabler | Sermon silence | ⚪ |
| 20 | Brynja | the Shieldmaiden | STR | M | Durable | Shield bash, wall | ⚪ |
| 21 | Father Ashgrove | the Exorcist | INT | R | Nuker | Banish, dispel | ⚪ |
| 22 | Castia & Pollan | the Blessed Twins | AGI | R | Carry | Two linked bodies | ⚪ |
| 23 | Evangeline | the Martyr | STR | M | Support | Sacrifice, shields | ⚪ |
| 24 | Lysander | the Dawnbreaker | AGI | M | Carry | Sunrise combo strikes | ⚪ |

---

## Adding a hero

1. Create `Shared/Runtime/Resources/GameData/heroes/<name>.json` with the hero, its abilities and statuses. Use
   `vorak.json` and `ilyra.json` as references.
2. Run `python3 Tools/dev/gen_gamedata_index.py`, then `dotnet test Server/tests/Bloodfall.Tests`. Data validation
   errors fail the load.
3. Tag abilities with `botUsage` (`engage`, `stun`, `nuke`, `aoe`, `buff`, `escape`, `farm`, `ultimate`) so bots can
   use them. Add `recommendedItems`.
4. Add the model (`model` key), portrait and icons (see ART_DIRECTION.md). Until then the client uses stand-ins.
5. Run the SimRunner with the new hero and record balance notes.
