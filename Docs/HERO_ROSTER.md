# Hero Roster (96 heroes)

Four factions with 24 heroes each. All names, lore and kits are original.

| Status | Meaning |
|---|---|
| ✅ **Implemented** | Playable: data in `Shared/Runtime/Resources/GameData/heroes/`, used by bots, covered by tests |

**All 96 are implemented.**

- **The eight concept heroes** are hand-written. Their kits are described below.
- **The other 88 are generated** by `Tools/heroes/generate_heroes.py` from compact kits in `Tools/heroes/roster_*.py`.
  - Each kit follows the "Kit direction" column.
  - Kits are built from about 40 ability archetypes (skillshots, dashes, leaps, disables, zones, summons, auras,
    shields, executes, transformations and so on), with each hero's own names, numbers and effects.
  - The full kits are in **HERO_KITS.md**, which is written from the data.
  - Generated heroes have Blender models and portraits from the same pipeline. Their ability icons come from
    `Tools/art/generate_icons.py`.
  - `RosterTests` casts every ability of every hero in a real match and checks that bots use their kits.
  - Balance is first-pass; see BALANCE_NOTES.md.

**Where the engine forced a kit direction to change:**

| Hero | Kit direction | Implemented as |
|---|---|---|
| Lysandre | Mirror images | Mirror Swap, which swaps places with a unit (illusions are not implemented, B-008) |
| Sable Vix | Illusions and feints | Blinks, marks and invisibility |
| Castia & Pollan | Two linked bodies | One hero whose abilities fire twice (Twin Arrows) |
| Mother Moth | Sleep | A stun (Sleeping is not implemented in the engine) |
| Mercy Halloway | Resurrection prayer | Mass Absolution, a large area heal (Resurrect is not implemented, B-008) |

**Attribute:** STR, AGI or INT. **Attack:** M = melee, R = ranged.

The eight *concept heroes* named in the original brief are Vorak, Nyxara, Malgrave, Ardyn, Fenrax, Morwen, Thael and
Ilyra. Where the brief gave only a name, the kits below are this project's design, aligned with each faction's
identity. All eight are implemented, used by bots, and covered by `Server/tests/Bloodfall.Tests/HeroKitTests.cs`.

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

### ✅ Nyxara, the Night Countess — Crimson Court · AGI · M · Assassin / Escape
- **Velvet Dark** (innate): after 3 s unseen by the enemy team (invisible or outside their vision), her next attack
  within 4 s is a guaranteed 180% critical strike.
- **Batwing Step** (Q): blinks up to 9 m as a swarm of bats. Tracking projectiles aimed at her miss if she moves more
  than 6 m.
- **Kiss of Thorns** (W): 60–180 physical damage and *Crimson Mark* for 5 s. The mark removes 2–5 armor, and every
  attack an enemy hero lands on the marked target heals Nyxara for 12–30.
- **Countess's Veil** (E): 2.5–4 s of invisibility and 20–35% movement speed. Attacking or casting ends it.
- **Midnight Sentence** (R): steps behind an enemy hero and strikes for 150/250/350 pure damage. A target with Crimson
  Mark that is left below 22/28/34% health is executed.

### ✅ Malgrave, the Bone Tyrant — Ashen Legion · INT · R · Summoner / Pusher
- **Ossuary** (innate): every unit that dies within 9 m gives a *Bone Shard* (+4% spell amplification each, up to 6,
  for 40 s). His next ability consumes all shards.
- **Raise the Fallen** (Q): raises two skeletal legionnaires, plus one per corpse within 7 m (up to two more), for 30 s.
  They grow tougher with the ability's level.
- **Bone Cage** (W): a ring of ribs (3.2 m radius) for 3–4.5 s that blocks movement in and out. Enemies inside are
  slowed by 20% and take 20–50 magical damage per second.
- **Grave Chill** (E): a 60° cone, 7 m long, dealing 75–225 magical damage. Chilled for 3 s: 20–35% slower movement
  and 20–50 slower attacks.
- **Legion of the Pale** (R): opens a crypt at a point. Enemies within 4 m take 100–200 magical damage and are feared
  for 1–1.5 s. Six armoured revenants rise over 4 s and guard that ground for about 20 s.

### ✅ Ardyn, the Dawnbringer — Dawnguard · STR · M · Durable / Support
- **Oathkeeper** (innate): allied heroes within 9 m, Ardyn included, gain 2 armor. The bonus doubles while Ardyn is
  below 40% health. The same aura from two Ardyns does not stack.
- **Aegis of Dawn** (Q): shields an ally for 120–360 damage for 6 s. When it expires it bursts for 60–180 magical
  damage within 3.5 m.
- **Consecrate** (W): blesses the ground around Ardyn (4.5 m) for 5 s. Allies in it heal 20–50 health per second.
  Enemies in it take 20–50 magical damage per second, doubled against non-hero units.
- **Judgment Strike** (E): his next attack within 6 s deals 20–80 bonus physical damage plus 5–8% of the target's
  missing health, and stuns for 0.4 s.
- **Radiant Crusade** (R): Ardyn and allied heroes within 6 m are cleansed of basic debuffs and gain 60% status
  resistance and 20% movement speed for 3–4 s. Then Ardyn charges 10–12 m. Every enemy he passes takes 100–200
  magical damage, is knocked aside and is dazzled for 2–3 s (half its attacks miss).

### ✅ Fenrax, the Moonfang — Wild Covenant · AGI · M · Carry / Jungler
- **Lunar Hunger** (innate): at night he gains 10% movement speed and 20 attack speed.
- **Pounce** (Q): leaps onto an enemy 6–9 m away, dealing 70–190 physical damage and rooting it for 1–1.6 s.
- **Rending Claws** (W): each attack removes 0.75–1.5 armor for 5 s, stacking up to 6 times.
- **Howl of the Pack** (E): two spirit wolves join him for 20 s. Allied heroes and summons within 9 m gain 10–34
  attack damage for 10 s.
- **Night Unleashed** (R): becomes the Moonfang for 18 s: +30–50% maximum health, +20–50 attack damage, +10% movement
  speed, and his attacks cleave 35–65% damage within 2.5 m. It is also night for everyone for 8 s. Cannot be
  dispelled.

### ✅ Morwen, the Hedge Witch — Wild Covenant · INT · R · Disabler / Support
- **Old Pact** (innate): her hexes and curses strip 15% magic resistance. The effect lives on the Toad Hex and
  Crooked Curse statuses.
- **Toad Hex** (Q): turns an enemy into a toad for 1.5–3 s. It cannot attack, cast or use items, and moves 40%
  slower.
- **Bubbling Cauldron** (W): a destructible cauldron for 12 s. Allies within 5 m regenerate 8–26 health and 1–2.5
  mana per second. Enemies within 5 m are slowed by 15–30%.
- **Crooked Curse** (E): 60–150 magical damage and an 8 s curse. Every ability or item the target uses repeats that
  damage, and Morwen gets the kill credit.
- **Witching Hour** (R): for 6 s, every basic ability she casts happens a second time at 50/60/70% power. This covers
  damage, healing and mana; durations are unchanged, and a second cauldron appears. Enemies within 5 m when she casts
  it are hexed for 1 s.

### ✅ Thael, the Wildwood Warden — Wild Covenant · STR · M · Initiator / Durable
- **Barkskin** (innate): with at least 3 trees within 5 m he gains 5 armor and 4 health regeneration.
- **Grasping Roots** (Q): roots burrow along a 10 m line and erupt under every enemy they pass: 70–190 magical damage
  and rooted for 1.2–2.1 s.
- **Summon Treant** (W): wakes 1/1/2/2 treants at a point for 25 s. They are sturdy and deal extra damage to
  structures.
- **Grove Call** (E): after a 2 s channel he steps out of the ground up to 30–60 m away. Enemies within 3 m of where
  he emerges are slowed by 50% for 1.5 s.
- **Wrath of the Old Forest** (R): for 5 s, lashing roots strike enemies within 7–9 m of Thael (the area follows
  him): 50–110 magical damage per second and a 20% slow. Thael takes 30% less damage while it lasts.

**Differences from the first concepts.** Each change keeps the concept's role and is noted so design can revisit it:

| Hero | Concept | Implemented |
|---|---|---|
| Nyxara | Midnight Sentence only on marked targets | Any enemy hero; only marked ones can be executed |
| Malgrave | Curved 8 m bone wall | Ring cage (the engine builds ring walls) |
| Malgrave | Grave Chill kills leave hero corpses | Not implemented: corpses come only from creeps and neutrals |
| Ardyn | Aegis bursts when it expires *or breaks* | Bursts on expiry only |
| Ardyn | "Unstoppable" | Basic dispel + 60% status resistance |
| Thael | +1 armor per 2 trees (max +8) | Flat bonus with 3+ trees nearby |
| Thael | Roots erupt after 0.6 s | A fast burrowing line |
| Thael | Treant made from a tree; teleport only to trees | Summoned at a point; teleport to any point |
| Morwen | Echo at 50% | 50/60/70% by level, plus a 1 s hex burst |

---

## Crimson Court — vampire nobility, blood knights, gargoyles, cursed assassins

| # | Hero | Title | Attr | Atk | Roles | Kit direction | Status |
|---|---|---|---|---|---|---|---|
| 1 | Vorak | the Blood Tyrant | STR | M | Initiator, Durable | Charge, bleed, bash, stunning leap | ✅ |
| 2 | Ilyra | the Blood Witch | INT | R | Nuker, Support | Health-cost spells, blood pool, drain | ✅ |
| 3 | Nyxara | the Night Countess | AGI | M | Assassin, Escape | Bat blink, marks, execute | ✅ |
| 4 | Sereth Vane | the Crimson Duelist | AGI | M | Carry | Parry stance, riposte counters | ✅ |
| 5 | Isolde Marrow | the Chalice Queen | INT | R | Support | Chalice heals, charm | ✅ |
| 6 | Grimbane | the Gargoyle Sentinel | STR | M | Durable, Disabler | Stone form, dive from perch | ✅ |
| 7 | Castor Dusk | the Lamplighter of Graves | INT | R | Nuker | Soul lantern, will-o'-wisps | ✅ |
| 8 | Veyla | the Whispering Blade | AGI | M | Assassin | Invisibility, poisoned daggers | ✅ |
| 9 | Kaelthorne | the Iron Baron | STR | M | Durable | Blood armour, tithe aura | ✅ |
| 10 | Morthaine | the Swarm | AGI | R | Carry, Escape | Splits into bats | ✅ |
| 11 | Ashvere | the Renegade Inquisitor | INT | R | Disabler | Blood chains, silence | ✅ |
| 12 | Dravenne | the Moonless Huntress | AGI | R | Carry | Crossbow, night bonuses | ✅ |
| 13 | Seraphel | the Fallen | STR | M | Initiator | Blood-wing flight, dive | ✅ |
| 14 | Lysandre | the Mirror Bride | INT | R | Nuker, Escape | Mirror images, swaps | ✅ |
| 15 | Hollowmere | the Cathedral Horror | STR | M | Durable | Bell tolls, fear | ✅ |
| 16 | Rhaegor | the Blood-Iron Champion | STR | M | Carry | Weapon grows with kills | ✅ |
| 17 | Vesper | the Silent Chorister | INT | R | Support, Disabler | Hymn silences | ✅ |
| 18 | Othniel | the Flesh Tailor | INT | M | Support | Stitches allies, abominations | ✅ |
| 19 | Talon | the Spire Gargoyle | AGI | R | Ganker | Diving strikes, stone skin | ✅ |
| 20 | Corvina | the Raven Courtesan | INT | R | Disabler | Ravens blind, whispers | ✅ |
| 21 | Malachar | the Thornblood Knight | STR | M | Initiator | Thorn reflection | ✅ |
| 22 | Elowen Sable | the Sanguine Oracle | INT | R | Support | Foresight, delayed damage | ✅ |
| 23 | Varric Ashmoor | the Oathless | STR | M | Carry, Durable | Cursed blade, lifesteal | ✅ |
| 24 | Nocturne | the Last Heir | AGI | M | Carry | Blood stacks from kills | ✅ |

## Ashen Legion — necromancers, skeletal hosts, plague cults, corpse engines

| # | Hero | Title | Attr | Atk | Roles | Kit direction | Status |
|---|---|---|---|---|---|---|---|
| 1 | Malgrave | the Bone Tyrant | INT | R | Summoner, Pusher | Raise dead, bone wall | ✅ |
| 2 | Morrowmaw | the Corpse Engine | STR | M | Durable, Pusher | Siege engine of corpses | ✅ |
| 3 | Sister Pallor | the Plague Mother | INT | R | Nuker | Contagious damage over time | ✅ |
| 4 | Kessrith | the Bone Archer King | AGI | R | Carry | Bone arrows, volley | ✅ |
| 5 | Vulgrim | the Gravedigger | STR | M | Disabler | Buries enemies, shovel | ✅ |
| 6 | Dren | the Ossuary Knight | STR | M | Durable | Bone armour plating | ✅ |
| 7 | The Hollow Choir | — | INT | R | Support | Ghost choir, sustain | ✅ |
| 8 | Rotfang | the Carrion Hound | AGI | M | Jungler | Feast on corpses | ✅ |
| 9 | Varuun | the Lich-Magister | INT | R | Carry (caster) | Phylactery, frost | ✅ |
| 10 | Yselde | the Ashcaller | INT | R | Nuker | Ash storms, blind | ✅ |
| 11 | Grimsoul | the Reaper of Tithes | STR | M | Carry | Scythe executes | ✅ |
| 12 | Ulgar | the Bonewright | INT | R | Pusher | Bone turrets | ✅ |
| 13 | Hekra | the Scourgeborn | AGI | M | Assassin | Plague blades | ✅ |
| 14 | The Pale Emissary | — | INT | R | Disabler | Fear, terror auras | ✅ |
| 15 | Cindermaw | the Pyre Colossus | STR | M | Initiator | Corpse fire, burn | ✅ |
| 16 | Mother Moth | — | INT | R | Support | Moth swarms, sleep | ✅ |
| 17 | Solace | the Deathwarden | STR | M | Support | Brief "cannot die" aura | ✅ |
| 18 | Vezmorah | the Crypt Lord | STR | M | Durable, Initiator | Burrow, impale | ✅ |
| 19 | Ilveth | the Skullspeaker | INT | R | Nuker | Talking skull familiar | ✅ |
| 20 | Wraith of Eldermoor | — | AGI | M | Escape | Phase through units | ✅ |
| 21 | Ossian | the Grand Embalmer | INT | R | Disabler | Embalming wraps | ✅ |
| 22 | The Chained Colossus | — | STR | M | Durable | Chains, drag | ✅ |
| 23 | Blightrunner | — | AGI | M | Escape, Ganker | Plague trail | ✅ |
| 24 | Sepulchra | the Tomb Queen | INT | R | Nuker | Crypt prison ultimate | ✅ |

## Wild Covenant — werewolves, witches, druids, forest spirits under lunar pacts

| # | Hero | Title | Attr | Atk | Roles | Kit direction | Status |
|---|---|---|---|---|---|---|---|
| 1 | Fenrax | the Moonfang | AGI | M | Carry, Jungler | Werewolf form, pounce | ✅ |
| 2 | Morwen | the Hedge Witch | INT | R | Disabler, Support | Hex, cauldron, echo | ✅ |
| 3 | Thael | the Wildwood Warden | STR | M | Initiator, Durable | Roots, treants, forest | ✅ |
| 4 | Ursolmar | the Barrow Bear | STR | M | Durable | Maul, hibernate | ✅ |
| 5 | Kithra | the Owl Seer | INT | R | Support | Owl scouts, vision | ✅ |
| 6 | Briarheart | — | STR | M | Durable | Thorn body | ✅ |
| 7 | Luneth | the Silver Stag | AGI | M | Initiator | Charging antlers | ✅ |
| 8 | Grimtooth | the Alpha | AGI | M | Carry | Pack bonuses | ✅ |
| 9 | Old Mother Yew | — | INT | R | Support | Tree healing | ✅ |
| 10 | Sable Vix | the Fox Trickster | AGI | M | Escape | Illusions, feints | ✅ |
| 11 | Mossbeard | — | STR | M | Support | Moss armour, regrowth | ✅ |
| 12 | Ravenna Blackfeather | — | INT | R | Nuker | Crow shapeshift | ✅ |
| 13 | Nerissa | the Marsh Spirit | INT | R | Disabler | Bog, drowning | ✅ |
| 14 | The Hollow Stag | — | STR | M | Initiator | Spirit charge | ✅ |
| 15 | Agathe | the Bramblewitch | INT | R | Nuker | Thorn damage over time | ✅ |
| 16 | Varkul | the Blood Moon Berserker | STR | M | Carry | Rage, frenzy | ✅ |
| 17 | Isra Silverclaw | — | AGI | M | Assassin | Silver claws (anti-regen) | ✅ |
| 18 | The Green Knight of Harrowmere | — | STR | M | Durable | Beheading pact | ✅ |
| 19 | Sprigg | the Mushroom Shaman | INT | R | Pusher | Spores, fungus traps | ✅ |
| 20 | Ansa | the Wolfmother | STR | M | Summoner | Wolf pack | ✅ |
| 21 | Stormhorn | the Thunder Elk | STR | M | Initiator | Thunderclap | ✅ |
| 22 | Mira Thornveil | — | AGI | R | Assassin | Poison darts | ✅ |
| 23 | Aldric | the Grovekeeper | INT | R | Support | Grove sanctuary | ✅ |
| 24 | Lunara | the Eclipse Priestess | INT | R | Nuker | Lunar beams, eclipse | ✅ |

## Dawnguard — paladins, monster hunters, battle priests of a zealous crusade

| # | Hero | Title | Attr | Atk | Roles | Kit direction | Status |
|---|---|---|---|---|---|---|---|
| 1 | Ardyn | the Dawnbringer | STR | M | Durable, Support | Aegis, consecration, crusade | ✅ |
| 2 | Sister Cassia | the Flame Confessor | INT | R | Nuker | Purifying fire | ✅ |
| 3 | Brennock Vail | the Stake Hunter | AGI | R | Carry | Silver bolts, traps | ✅ |
| 4 | Oswin | the High Templar | STR | M | Durable | Aegis, taunt | ✅ |
| 5 | Mercy Halloway | the Battle Priest | INT | R | Support | Heals, resurrection prayer | ✅ |
| 6 | Gideon | the Witchfinder | AGI | R | Anti-mage | Mana burn, purge | ✅ |
| 7 | Aurelia | the Seraph Commander | STR | M | Initiator | Winged descent | ✅ |
| 8 | Brother Tobias | the Bellringer | STR | M | Disabler | Bell stuns | ✅ |
| 9 | Lucan Hale | the Monster Slayer | AGI | M | Carry | Bonus versus summons/heroes | ✅ |
| 10 | Maerith | the Inquisitor | INT | R | Disabler | Chains of faith | ✅ |
| 11 | Roderic | the Lionheart | STR | M | Initiator | Cavalry charge | ✅ |
| 12 | The Pilgrim | — | STR | M | Support | Escort, sanctuary | ✅ |
| 13 | Deacon Vale | the Lamplight | INT | R | Support | True-sight lamps | ✅ |
| 14 | Isabeau | the Sunlancer | AGI | R | Carry | Thrown sun lances | ✅ |
| 15 | Thorne | the Wall Warden | STR | M | Durable | Fortifications | ✅ |
| 16 | Emmerich | the Brightforge Artificer | INT | R | Pusher | Holy turrets | ✅ |
| 17 | Solenne | the Radiant Archer | AGI | R | Carry | Light arrows, blind | ✅ |
| 18 | Halbrecht | the Justicar | STR | M | Carry | Execution strikes | ✅ |
| 19 | Ignatius | the Canon | INT | R | Disabler | Sermon silence | ✅ |
| 20 | Brynja | the Shieldmaiden | STR | M | Durable | Shield bash, wall | ✅ |
| 21 | Father Ashgrove | the Exorcist | INT | R | Nuker | Banish, dispel | ✅ |
| 22 | Castia & Pollan | the Blessed Twins | AGI | R | Carry | Two linked bodies | ✅ |
| 23 | Evangeline | the Martyr | STR | M | Support | Sacrifice, shields | ✅ |
| 24 | Lysander | the Dawnbreaker | AGI | M | Carry | Sunrise combo strikes | ✅ |

---

## Adding a hero

**Generated (the usual way).**

1. Add an `H(...)` entry and a kit function to the faction's `Tools/heroes/roster_*.py`. That means identity, lore,
   an optional look, and five `kitlib` calls: innate, Q, W, E, R. Add an archetype to `kitlib.py` if nothing fits.
2. Run `python3 Tools/heroes/generate_heroes.py`. It writes the hero JSON, the Blender spec, HERO_KITS.md and the data
   index.
3. Run `python3 Blender/scripts/build_models.py hero_<stem> --preview-idle`, then
   `python3 Blender/scripts/render_portraits.py hero_<stem>`, then `python3 Tools/art/generate_icons.py` for the
   ability icons.
4. Run `dotnet test Server/tests/Bloodfall.Tests`. `RosterTests` casts every ability and checks bot usage.
5. For balance, run `SimRunner -- 60 <seed> --random` over many seeds, then `python3 Tools/heroes/soak_report.py <logs>`.

**Hand-written (bespoke mechanics).**

1. Create `Shared/Runtime/Resources/GameData/heroes/<name>.json`. Use `vorak.json` and `ilyra.json` as references,
   and do not add it to a roster module.
2. Tag abilities with `botUsage` so bots can use them: `engage`, `stun`, `nuke`, `aoe`, `buff`, `escape`, `farm` or
   `ultimate`, plus `ally` or `self` for friendly targets.
3. Add a Blender spec to `bf_characters.py`, then continue with steps 3–5 above.
