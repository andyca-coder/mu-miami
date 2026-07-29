# Drop worksheet — the Mu Miami loot budget

**Brief:** 002 · **Contract:** [`balance-canon.md`](balance-canon.md) § The drop budget
**Instrument:** `scripts/mm verify-balance` (check 3 is the worst-context measurement)
**Status:** shipped, measured against the live server

---

## The engine behaviour everything here is defending against

`DefaultDropGenerator` merges four sources of `DropItemGroup` per kill
(`DefaultDropGenerator.cs:77-87`):

```csharp
this.PartitionDropGroups(monster.DropItemGroups ?? []);              // monster-owned, unfiltered
this.PartitionDropGroups(character.DropItemGroups ?? [], monster);   // character / quest
this.PartitionDropGroups(map.DropItemGroups ?? [], monster);         // map-wide
this.PartitionDropGroups(await GetQuestItemGroupsAsync(player) ?? [], monster);
```

Two facts decide the whole budget:

1. **Groups with `Chance >= 1.0` do not compete.** `PartitionDropGroups` (`:336-354`) sends
   them to a *guaranteed* list that drops in order. They are excluded from every sum in this
   document. This is why Box of Kundun (`Chance = 1`) is not a budget problem.
2. **Below 1.0, each group fires at exactly its own chance and "nothing dropped" is a real
   outcome. At or above 1.0, `SelectRandomGroup` (`:555-576`) normalises the draw and
   something drops from every single kill.** That is the cliff. Loot stops being an event.

`IsGroupRelevant` (`:240-258`) filters map and character groups by `MinimumMonsterLevel` /
`MaximumMonsterLevel` / `Monster`. Monster-owned groups are passed without a monster
argument and are never filtered. The measurement below reproduces all of this.

## The finding: stock Season 6 is already over canon's original budget

Canon originally set the budget at **≤ 0.85**. Measured on the live database *before* any
Mu Miami change:

| | |
|---|---|
| Worst merged context, stock S6E3 | **0.8681** |
| Where | **Icarus / Dark Phoenix (level 108)** |
| Upstream data version at measurement | 97 (the state brief 001's image seeded) |
| Measured with | check 3 of `scripts/verify-balance.sh` |

Composition of that 0.8681:

| Group | Chance | Note |
|---|---|---|
| Money | 0.5 | global default, `SetGuid(1)` |
| Random item | 0.3 | global default, `SetGuid(2)` |
| Jewels | 0.001 | global default, `SetGuid(4)` |
| Excellent | 0.0001 | global default, `SetGuid(3)` |
| ~6 quest-item windows overlapping level 108 | ~0.06 | Blood Bone, Devil's Key, Devil's Eye, Old Scroll, Scroll of Archangel, Illusion Sorcerer Covenant — upstream attaches all of these to **every** map with level windows |
| Symbol of Kundun (level 7 window) | 0.003 | |
| Dark Raven / Dark Horse Spirit, Feather of Dark Phoenix, Crest of Monarch | ~0.004 | Icarus-specific |

0.85 was therefore unreachable without cutting groups canon explicitly protects
("Money/common base groups — vanilla — texture preserved"). **Canon was amended to ≤ 0.95**
rather than silently violated or silently funded from the money group. See
`balance-canon.md § The drop budget`.

## The Mu Miami allocation

| Lever | Stock | Mu Miami | Δ | Quoted from |
|---|---|---|---|---|
| Money (`SetGuid(1)`) | 0.5 | **0.5** | 0 | `GameConfigurationInitializerBase.cs:178` |
| Random item (`SetGuid(2)`) | 0.3 | **0.3** | 0 | `GameConfigurationInitializerBase.cs:187` |
| Excellent (`SetGuid(3)`) | 0.0001 | **0.03** | +0.0299 | `GameConfigurationInitializerBase.cs:197` |
| Jewels (`SetGuid(4)`) | 0.001 | **0.003** | +0.002 | `GameConfigurationInitializerBase.cs:206` |
| Ancient (new, `SetGuid(900)`) | — | **0.002** | +0.002 on 9 maps | new in `MuMiamiAncientDropsUpdatePlugIn` |
| Chaos Machine +13/+15 | — | **untouched** | 0 | not a drop group at all — see below |

Jewels: 0.003 / 0.001 = **exactly 3.0×**, inside the brief's 3.0 ± 0.1.
Excellent: 0.03 flat, per the brief.

### Merged budget arithmetic

Global additions apply to every map (the four default groups are the *same object instances*
on every map — `BaseMapInitializer.InitializeDropItemGroups` adds the shared list):

```
worst stock context (data version 97)        0.8681   Icarus / Dark Phoenix
  + upstream update 99, "Rena" group, 0.01,
    level window 30-255, added to maps       +0.0100  <- not ours; see note below
  + excellent 0.0001 -> 0.03                 +0.0299
  + jewels    0.001  -> 0.003                +0.0020
  ------------------------------------------------
  worst Mu Miami context (measured)           0.9080
  budget                                      0.95     margin 0.042
  engine cliff                                1.00     margin 0.092
```

**Two upstream updates rode along.** Brief 001's stack ran the published image, whose data
was at update version 97. The source-built image contains 98 (`AddItemRegistrationAttributes`)
and 99 (`AddRenaItem`), both `IsMandatory = true` — the panel applies mandatory updates
whether or not you tick them. `AddRenaItem` attaches a 0.01 drop group to every map, which is
where +0.0100 above comes from. Mu Miami's own contribution is +0.0319, exactly as designed.

**Measured worst contexts after applying** (several tie at the maximum, all Blood Castle 5/6
and Devil Square 7 monsters in the level 102–135 band):

```
Blood Castle 6 / Magic Skeleton 6 (level 135)      0.9080
Blood Castle 6 / Giant Ogre 6 (level 120)          0.9080
Devil Square 7 / Dreadfear (level 119)             0.9080
```

Note the worst context is *not* one of the maps the ancient group touches — those
(Kalima/Aida/Icarus) carry +0.002 more than a plain map but have fewer overlapping quest
windows in their level bands. Reasoning about the budget from group totals rather than from
the merged per-monster maximum would have gotten this wrong twice over.

### The three merged contexts, enumerated

| Context | What it contributes | Mu Miami worst case |
|---|---|---|
| **Global / map** | the 4 shared defaults + upstream's per-map quest windows + per-map extras + (on 9 maps) the ancient group | 0.9020 |
| **Monster** | monster-owned groups, unfiltered. Every one seeded in S6E3 that matters is `Chance = 1` (Box of Kundun, Golden monster loot, Red Dragon) and therefore *guaranteed*, not competing. The sub-1.0 ones are the Wizard's Ring drops at 0.8 on two Crywolf monsters. | +0.8 on Destructive Ogre Soldier / Archer only |
| **Character / quest** | groups on the player's own `Character` while a quest is active, level-window filtered | +0.01 to +0.05 |

The per-monster maximum, not the group totals, is what check 3 reports — it evaluates every
`(map, monster)` pair that actually has a spawn area, applies the level windows for that
monster's `Stats.Level`, and takes the maximum. Group-level sums would have hidden the
Icarus result completely.

**The one context that needs watching:** Crywolf's Destructive Ogre Soldier and Archer carry
a 0.8 monster-owned Wizard's Ring group *on top of* the map's ~0.87. Those monsters are
event spawns (`SpawnTrigger.OnceAtEventStart`), so they have no `MonsterSpawnArea` row with
an automatic trigger and do not appear in check 3's spawned set. They are recorded here so
that a future change which makes them ordinary spawns knows what it is walking into.

## Excellent at 3 % — what it actually means

`ExcellentItemDropLevelDelta` stays at its seeded 25, so a monster still has to be at least
level 25 for an excellent to be possible at all, and the item pool is drawn from
`monsterLevel − 25` (`DefaultDropGenerator.cs:180-199`). Every excellent item gets a skill
and at least one excellent option.

At the plan's mid-game pace (15 kills/min in Tarkan) 3 % is roughly **27 excellent items an
hour**. That is enormous by MU standards and entirely deliberate: canon's thesis is "fast to
gear, slow to perfect", with scarcity concentrated in ancient sets and the Chaos Machine.

## Ancient scoping

New group, `SetGuid<DropItemGroup>(900)` → `00000200-0384-0000-0000-000000000000`,
`ItemType = Ancient`, `Chance = 0.002`, no level window, no monster.

Attached to exactly nine `GameMapDefinition.DropItemGroups` collections:

| Map | `Number` |
|---|---|
| Icarus | 10 |
| Kalima 1–6 | 24, 25, 26, 27, 28, 29 |
| Aida | 33 |
| Kalima 7 | 36 |

Scoping is by *attachment*, not by filter — the group exists on nine maps and nowhere else.
Check 4 of `verify-balance.sh` asserts the count is 9 and that no ancient-type group is
attached to any other map, querying the applied configuration rather than reading the
plug-in.

No `PossibleItems` needed: `DefaultDropGenerator.GenerateRandomAncient` (`:205-218`) picks
from items carrying an `AncientOption` set group.

**Restart required.** That ancient item pool is derived in `DefaultDropGenerator`'s
constructor (`:55-60`) from `ItemDefinition.DropsFromMonsters`, and the generator is built
once per `GameServer` (`GameServer.cs:68`). The group row appears live; ancients do not drop
until one restart.

## Chaos Machine: the grep evidence

Canon's scarcity anchor, and the brief's explicit "do not touch". The +13/+15 odds are not
drop groups at all — they live in `ItemCrafting` / `SimpleCraftingSettings` rows
(`SuccessPercent`, `AdditionalSuccessPerItemLevel`, `SuccessPercentageAdditionForLuck`),
seeded by `VersionSeasonSix/Items/ChaosMixes.cs`. Nothing in this brief can reach them.

```bash
$ grep -rl "ItemCrafting\|SimpleCraftingSettings\|SuccessPercent" \
    src/Persistence/Initialization/Updates/MuMiami/
# (no output — zero files)

$ git diff upstream/master --stat -- src/Persistence/Initialization/VersionSeasonSix/Items/ChaosMixes.cs
# (no output — file unmodified)
```

Check 5 of `verify-balance.sh` runs the grep as an assertion and prints the live crafting
rows, so the evidence is regenerated on every verification run rather than trusted from this
document.

## The standing rule

Any future change that adds or raises a drop group — brief 003 hot zones, seasonal events,
new monster loot — **must re-run check 3**:

```bash
scripts/mm verify-balance
```

Every monster context stays under **1.00** with explicit margin; the Mu Miami working budget
is **0.95**. If a new group would push a context past it, scope the group away from the hot
contexts (fewer maps, or a `MinimumMonsterLevel` / `MaximumMonsterLevel` window) rather than
raising the budget. The budget number moves only in `balance-canon.md`, and only with a
recorded measurement next to it.
