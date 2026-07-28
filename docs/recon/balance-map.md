# Balance Map — where OpenMU defines the things Mu Miami will customize

**Brief:** R1 (`docs/briefs/R1-balance-recon.md`) · **Status:** complete
**Version of record:** working tree at commit `d49b6ee00` (`master`). `docs/UPSTREAM.md` does not exist yet
(brief 001 has not landed it); the tree contains no fork-local game-logic changes — the most recent
non-docs commits are upstream merges (`1b2994e02`, `af74575be`).
**Scope:** facts and mechanisms only. No proposals, no recommended values.

All paths are relative to the repo root. Line numbers are from the commit above.

---

## Q1 — Experience rate

### Where the numbers live

| Lever | Type | Defined / seeded at | Read at run time |
|---|---|---|---|
| `GameConfiguration.ExperienceRate` | `float` | `src/Persistence/Initialization/GameConfigurationInitializerBase.cs:40` (`= 1.0f`) | `src/GameLogic/GameContext.cs:114` |
| `GameConfiguration.MasterExperienceRate` | `float` | `src/DataModel/Configuration/GameConfiguration.cs:39` (default `1.0f`) | `src/GameLogic/GameContext.cs:117` |
| `GameServerDefinition.ExperienceRate` | `float` | `src/Persistence/Initialization/DataInitializationBase.cs:253` (`= 1.0f` per server) | `src/GameServer/GameServerContext.cs:106,109` |
| `GameMapDefinition.ExpMultiplier` | `double` | `src/Persistence/Initialization/BaseMapInitializer.cs:119` (`= 1` for **every** map) | `src/GameLogic/Player.cs:1259`, `src/GameLogic/Party.cs:346` |
| `Stats.ExperienceRate`, `Stats.MasterExperienceRate`, `Stats.BonusExperienceRate` | character attributes | `src/Persistence/Initialization/CharacterClasses/CharacterClassInitialization.cs:164,179` (const `1`) | `src/GameLogic/Player.cs:1258` |
| `GameConfiguration.ExperienceFormula` (level → required XP) | `string` (mXparser expression) | `src/Persistence/Initialization/GameConfigurationInitializerBase.cs:65` | `src/GameLogic/GameContext.cs:89` |
| `GameConfiguration.MasterExperienceFormula` | `string` | same file `:66` | `src/GameLogic/GameContext.cs:90` |
| `GameConfiguration.MinimumMonsterLevelForMasterExperience` | `short` | same file `:41` (`= 95`) | master-XP gate, `src/GameLogic/Player.cs:1960` path |

### The per-kill formula (solo)

`src/GameLogic/Player.cs:1238-1274` — `Player.CalculateExpAfterKill`:

```csharp
var experience = killedObject.CalculateBaseExperience(attributes[Stats.TotalLevel]);
experience *= gameRate;                                                       // GameConfiguration.ExperienceRate * GameServerDefinition.ExperienceRate
experience *= attributes[expRateAttribute] + attributes[Stats.BonusExperienceRate];
experience *= this.CurrentMap?.Definition.ExpMultiplier ?? 1;                 // per-map multiplier

var minMultiplier = attributes[Stats.RandomExperienceMinMultiplier];          // seeded 0.8
var maxMultiplier = attributes[Stats.RandomExperienceMaxMultiplier];          // seeded 1.2 (see GameConfigurationInitializerBase:278+)
if (minMultiplier > 0 && maxMultiplier > 0) { ... return Rand.NextInt(minimumExperience, maximumExperience); }
```

Base XP is a pure function of the **killed monster's level** and the killer's total level —
`src/GameLogic/AttackableExtensions.cs:593-615`:

```csharp
var targetLevel = killedObject.Attributes[Stats.Level];
var tempExperience = (targetLevel + 25) * targetLevel / 3.0;
if (killerLevel > targetLevel + 10) { tempExperience *= (targetLevel + 10) / killerLevel; }   // over-level penalty
if (killedObject.Attributes[Stats.Level] >= 65) { tempExperience += (targetLevel - 64) * (targetLevel / 4); }
return Math.Max(tempExperience, 0) * 1.25;
```

Party kills use a separate path (`src/GameLogic/Party.cs:338-399`): base XP is computed once from the
party's **average** total level, multiplied by `memberCount * 1.05^(memberCount-1) * map.ExpMultiplier`,
then split proportionally to each member's level, with the game rate and the member's rate attributes
applied per member.

**Coupling worth knowing:** money dropped by a monster is derived from the granted experience.
`src/GameLogic/NPC/AttackableNpcBase.cs:415-423` sums the experience shares and passes them to the drop
generator; `src/GameLogic/DefaultDropGenerator.cs:517` computes `droppedMoney = gainedExperience + 7`.
Raising the XP rate raises Zen income by the same factor.

### Level → required XP: formula, not a table

`src/GameLogic/GameContext.cs:462-475` builds the level table at start-up by evaluating the configured
expression string once per level with mXparser (`MathParser.org-mXparser` 4.4.2, `src/Directory.Packages.props:70`):

```csharp
private static long[] CreateExpTable(string experienceFormula, short maximumLevel)
{
    var argument = new Argument("level");
    var expression = new Expression(experienceFormula);
    expression.addArguments(argument);
    return Enumerable.Range(0, maximumLevel + 2).Select(level => { argument.setArgumentValue(level); return (long)expression.calculate(); }).ToArray();
}
```

The seeded default is already **piecewise** — it uses mXparser's `if()`:

```
if(level == 0, 0,
   if(level < 256, 10 * (level + 8) * (level - 1) * (level - 1),
      (10 * (level + 8) * (level - 1) * (level - 1)) + (1000 * (level - 247) * (level - 256) * (level - 256))))
```

The table is rebuilt when the configuration changes at run time —
`src/GameLogic/GameContext.cs:478-484` (`OnGameConfigurationChangeAsync`), registered at `:87`.

### Can rates differ by level range natively?

**Not as a rate.** `ExperienceRate` is a single flat float multiplied into every kill; there is no
level-banded rate table anywhere in the codebase (verified by reading every read site of
`ExperienceRate` / `MasterExperienceRate` listed above). The only native level-conditional behaviour in
the XP path is the over-level penalty in `CalculateBaseExperience` and the master-XP switch at
`MaximumLevel`.

**But the curve can be banded without code**, because the required-XP side is an arbitrary mXparser
expression supporting nested `if(level < N, …, …)`. Making levels 1–150 cheap and 300+ expensive is a
config-string edit to `GameConfiguration.ExperienceFormula`, applied live.

Two further native levers that are not level-banded but are *zone*-banded and *time*-banded:
`GameMapDefinition.ExpMultiplier` (per map, seeded to 1 everywhere) and `HappyHourPlugIn` (see Q5).

**How you'd change it.** Global rate: edit `GameConfiguration.ExperienceRate` (admin panel → Config →
General, or `GameConfigurationInitializerBase.cs:40` for the seed). Per-server rate: edit that server's
`GameServerDefinition.ExperienceRate` — it multiplies the global one. Per-zone: set
`GameMapDefinition.ExpMultiplier` on the map. Level-banded progression: rewrite `ExperienceFormula`
with `if()` branches. True level-banded *rate* multipliers would require custom code — the natural
insertion point is an attribute-based plugin adding a `SimpleElement` to `Stats.ExperienceRate`
(exactly what `HappyHourPlugIn` does, `src/GameLogic/PlugIns/PeriodicTasks/HappyHourPlugIn.cs:46-47`),
keyed on the player's level.

---

## Q2 — Drop system

### Structure

`src/DataModel/Configuration/DropItemGroup.cs` is the whole model:

```csharp
public partial class DropItemGroup
{
    public LocalizedString Description { get; set; }
    public double Chance { get; set; }                       // 0.0 .. 1.0
    public byte? MinimumMonsterLevel { get; set; }           // null = no bound
    public byte? MaximumMonsterLevel { get; set; }
    public virtual MonsterDefinition? Monster { get; set; }  // null = any monster
    public byte? ItemLevel { get; set; }
    public SpecialItemType ItemType { get; set; }            // None|Ancient|Excellent|RandomItem|SocketItem|Money|Jewel
    public virtual ICollection<ItemDefinition> PossibleItems { get; protected set; }
}
```

`ItemDropItemGroup` (`src/DataModel/Configuration/ItemDropItemGroup.cs`) extends it for item-triggered
drops (boxes) with `MinimumLevel`/`MaximumLevel`/`MoneyAmount`/`DropEffect`.

### Attachment points — three of them, all merged per kill

`src/GameLogic/DefaultDropGenerator.cs:77-87`:

```csharp
this.PartitionDropGroups(monster.DropItemGroups ?? []);                       // monster-specific
this.PartitionDropGroups(character.DropItemGroups ?? [], monster);            // character (quest) groups
this.PartitionDropGroups(map.DropItemGroups ?? [], monster);                  // map-wide
this.PartitionDropGroups(await GetQuestItemGroupsAsync(player) ?? [], monster);
```

`MinimumMonsterLevel` / `MaximumMonsterLevel` / `Monster` are applied as filters at
`DefaultDropGenerator.cs:240-258` (`IsGroupRelevant`) for the character/map/quest groups. Monster-owned
groups are not filtered (they are already monster-scoped).

### Chance semantics

`Chance` is a probability in `[0,1]`, **not** a weight — but the roll is a roulette over the group list,
so multiple groups compete. `DefaultDropGenerator.cs:336-354` splits them:

* `Chance >= 1.0` → **guaranteed** list, each one produces a drop, in order, until
  `MonsterDefinition.NumberOfMaximumItemDrops` is exhausted (`:279-300`).
* everything else → **chance** list, rolled `remainingDrops` times (`:303-331`):

```csharp
double totalChance = 0;
foreach (var group in this._chanceDropGroups) { totalChance += group.Chance; }
for (int i = 0; i < remainingDrops; i++) { var group = this.SelectRandomGroup(this._chanceDropGroups, totalChance); ... }
```

`SelectRandomGroup` (`:555-576`) draws `r = random()` in `[0,1)`, scales it by `totalChance` **only if
`totalChance > 1.0`**, then walks the list subtracting each `Chance`. Consequence:

* while the group chances sum to ≤ 1, each group fires with exactly its own `Chance` per roll and there
  is a real "nothing dropped" outcome;
* once they sum to > 1, the draw is normalised and **something always drops** — chances become relative
  weights. This is the behavioural cliff to watch when raising drop rates.

`MonsterDefinition.NumberOfMaximumItemDrops` (seeded `1` for ordinary monsters, e.g.
`src/Persistence/Initialization/Version095d/Maps/Tarkan.cs:60`) caps rolls per kill.

### The five default groups (apply to every map)

`src/Persistence/Initialization/GameConfigurationInitializerBase.cs:174-212`. Each is added to
`GameConfiguration.DropItemGroups` **and** registered as a default via
`BaseMapInitializer.RegisterDefaultDropItemGroup`, which `BaseMapInitializer.InitializeDropItemGroups`
(`:171-179`) copies onto every map:

| GUID seed | `ItemType` | `Chance` | Meaning |
|---|---|---|---|
| 1 | `Money` | `0.5` | money drop (amount = granted XP + 7) |
| 2 | `RandomItem` | `0.3` | random droppable item for the monster's level |
| 3 | `Excellent` | `0.0001` | random excellent item (only if the version has excellent options) |
| 4 | `Jewel` | `0.001` | jewels |

There is **no default Ancient group.** `SpecialItemType.Ancient` is used in exactly one seeded place:
`src/Persistence/Initialization/VersionSeasonSix/Events/ChaosCastleInitializer.cs:190`. Ancient items
otherwise reach players through the excellent/random paths only if an ancient group is added; the
generator itself supports them (`DefaultDropGenerator.cs:205-218`, `439-460`, using
`ItemOptionTypes.AncientOption` set groups).

### Jewels

The global jewel group is GUID-seed `4`, `Chance = 0.001`, `ItemType = Jewel`. Its `PossibleItems` are
populated by each jewel's own definition calling `src/Persistence/Initialization/InitializerBase.cs:203`:

```csharp
protected void AddItemToJewelItemDrop(ItemDefinition item)
{
    var id = GuidHelper.CreateGuid<DropItemGroup>(4);
    var jewelsItemDrop = this.GameConfiguration.DropItemGroups.First(x => x.GetId() == id);
    jewelsItemDrop.PossibleItems.Add(item);
}
```

called from e.g. `src/Persistence/Initialization/Version075/Items/Jewels.cs:50,71,92` (Bless, Soul,
Chaos) and `Version095d/Items/Jewels.cs:48` (Life). The jewel items themselves carry
`DropsFromMonsters = false`, so they *only* drop through this group. Jewel groups bypass the
`DropLevelMaxGap` window but still respect each item's `DropLevel` / `MaximumDropLevel`
(`DefaultDropGenerator.cs:531-539`).

A map-scoped example (Jewel of Guardian, Land of Trials only) is
`src/Persistence/Initialization/VersionSeasonSix/Maps/LandOfTrials.cs:452-468` — an override of
`InitializeDropItemGroups` that calls `base` and then adds one more group to `MapDefinition.DropItemGroups`.

### Excellent items

Chance = the `0.0001` group above. The *eligibility* rule is a separate global:
`GameConfiguration.ExcellentItemDropLevelDelta`, seeded `25` at
`GameConfigurationInitializerBase.cs:50`, captured by the generator at
`DefaultDropGenerator.cs:50` and used at `:180-199`:

```csharp
if (monsterLevel < this._excellentItemDropLevelDelta && possibleItems is null) { return null; }
var possible = possibleItems ?? this.GetPossibleList(monsterLevel - this._excellentItemDropLevelDelta);
item.HasSkill = item.CanHaveSkill();     // every excellent item gets a skill
this.AddRandomExcOptions(item);
```

Excellent option count/chance comes from the item's own `ItemOptionDefinition` with
`ItemOptionTypes.Excellent` (`:462-503`): first option is free, each further one rolls `AddChance`.

### Monster-specific drops

`src/Persistence/Initialization/Version095d/InvasionMobsInitialization.cs:150-161` is the canonical
pattern (guaranteed Box of Kundun on a golden monster):

```csharp
var itemDrop = this.Context.CreateNew<DropItemGroup>();
itemDrop.Chance = 1;                       // guaranteed
itemDrop.ItemLevel = (byte)(7 + lvl);
itemDrop.Description = $"Box of Kundun +{lvl}";
itemDrop.Monster = monster;
itemDrop.PossibleItems.Add(this.GameConfiguration.Items.First(item => item.Group == 14 && item.Number == 11));
monster.DropItemGroups.Add(itemDrop);
this.GameConfiguration.DropItemGroups.Add(itemDrop);
```

**How you'd change it.** Rates: edit `DropItemGroup.Chance` (admin panel → Config → Drop Item Groups,
or the seed sites above). New monster-specific loot: create a `DropItemGroup`, set `Monster` (or add it
to `monster.DropItemGroups`), add `PossibleItems`. New zone loot: add the group to the map's
`DropItemGroups` (either via a `InitializeDropItemGroups` override or in the panel). Global level-banded
loot: one group per band using `MinimumMonsterLevel`/`MaximumMonsterLevel` attached to the map or
character. Remember the `totalChance > 1.0` normalisation cliff above.

---

## Q3 — Monster definitions

### Where stats live

`src/DataModel/Configuration/MonsterDefinition.cs` — scalar fields at `:225-293`
(`Number`, `Designation`, `MoveRange`, `AttackRange`, `ViewRange`, `MoveDelay`, `AttackDelay`,
`RespawnDelay`, `Attribute`, `NumberOfMaximumItemDrops`, `NpcWindow`, `ObjectKind`,
`IntelligenceTypeName`) and collections at `:299-335` (`AttackSkill`, `MerchantStore`,
`ItemCraftings`, `DropItemGroups`, `Attributes`, `Quests`, `Buffs`).

Combat numbers are **not** fields — they are `MonsterAttribute` rows keyed by `AttributeDefinition`,
seeded through `MonsterDefinitionExtensions.AddAttributes`. Canonical seed block, one per monster,
`src/Persistence/Initialization/Version095d/Maps/Tarkan.cs:36-67`:

```csharp
var monster = this.Context.CreateNew<MonsterDefinition>();
this.GameConfiguration.Monsters.Add(monster);
monster.Number = 57;
monster.Designation = "Iron Wheel";
monster.MoveRange = 3;  monster.AttackRange = 4;  monster.ViewRange = 7;
monster.MoveDelay = new TimeSpan(400 * TimeSpan.TicksPerMillisecond);
monster.AttackDelay = new TimeSpan(1400 * TimeSpan.TicksPerMillisecond);
monster.RespawnDelay = new TimeSpan(10 * TimeSpan.TicksPerSecond);
monster.Attribute = 2;
monster.NumberOfMaximumItemDrops = 1;
var attributes = new Dictionary<AttributeDefinition, float>
{
    { Stats.Level, 80 }, { Stats.MaximumHealth, 17000 },
    { Stats.MinimumPhysBaseDmg, 280 }, { Stats.MaximumPhysBaseDmg, 330 },
    { Stats.DefenseBase, 215 }, { Stats.AttackRatePvm, 446 }, { Stats.DefenseRatePvm, 150 },
    { Stats.PoisonResistance, 9f / 255 }, { Stats.IceResistance, 9f / 255 },
    { Stats.WaterResistance, 9f / 255 }, { Stats.FireResistance, 9f / 255 },
};
monster.AddAttributes(attributes, this.Context, this.GameConfiguration);
monster.SetGuid(monster.Number);
```

Monsters are defined inside the map initializer that first uses them (`BaseMapInitializer.CreateMonsters`,
`src/Persistence/Initialization/BaseMapInitializer.cs:219-222`), plus
`VersionSeasonSix/NpcInitialization.cs` and `.../InvasionMobsInitialization.cs` for NPCs and event mobs.
The doc comment at `BaseMapInitializer.cs:216-217` carries the regex used to bulk-convert the original
`Monsters.txt` into this C# form.

### XP granted is derived, never explicit

There is no XP field on `MonsterDefinition`. XP is computed from `Stats.Level` of the killed object only
(`AttackableExtensions.cs:593-615`, quoted in Q1). **To make a monster worth more XP you must raise its
`Stats.Level`** — which simultaneously changes the over-level penalty, drop-level eligibility
(`DropLevel` windows), master-XP eligibility (`MinimumMonsterLevelForMasterExperience = 95`) and the
money drop. There is no lever that raises XP alone.

### "Elite variant" — minimal field set

`MonsterDefinition.Number` is both the config key and **the model id sent to the client** —
`src/GameServer/RemoteView/World/NewNpcsInScopePlugIn075.cs:76` (`npcBlock.TypeNumber = (byte)npc.Definition.Number`),
same in the 095/S6 variants. `Number` must also be unique: `BaseMapInitializer.NpcDictionary`
(`:66`) is `GameConfiguration.Monsters.ToDictionary(npc => npc.Number, …)`, which throws on duplicates.
So **two definitions cannot share one `Number`** — an "elite Iron Wheel" needs a `Number` the client
already has artwork for; it cannot alias 57.

Prior art for exactly this: the Golden monsters
(`src/Persistence/Initialization/VersionSeasonSix/InvasionMobsInitialization.cs:38-103` — Golden Goblin 78,
Golden Dragon 79, Golden Vepar 81) are ordinary `MonsterDefinition`s on client-known numbers with
inflated stats and a guaranteed drop group.

Minimum viable new definition, from the seed blocks above: `Number`, `Designation`, `MoveRange`,
`AttackRange`, `ViewRange`, `MoveDelay`, `AttackDelay`, `RespawnDelay`, `Attribute`,
`NumberOfMaximumItemDrops`, `AddAttributes({Level, MaximumHealth, Min/MaxPhysBaseDmg, DefenseBase,
AttackRatePvm, DefenseRatePvm, resistances})`, `SetGuid(Number)`, plus a `MonsterSpawnArea` referencing it.
`ObjectKind` defaults to `Monster`; `AttackSkill` is optional.

**Cheaper alternative that reuses the appearance exactly:** `MonsterSpawnArea.MaximumHealthOverride`
(`src/DataModel/Configuration/MonsterSpawnArea.cs:129`) overrides HP **per spawn area** with no new
definition — a "tanky pack at these coordinates" needs nothing but a spawn-area edit. It overrides HP
only; damage/defense/XP stay at the definition's values.

**How you'd change it.** Tune an existing monster: edit its `MonsterAttribute` rows (admin panel →
Config → Monsters, live — see Q6) or the seed block. New elite: copy a seed block, pick an unused
client-known `Number`, raise the attributes, attach a `DropItemGroup` with `Monster` set, add spawns.
Blanket difficulty: `MonsterAttributeScaler` already exists (Q7).

---

## Q4 — Spawn system

### Model

`src/DataModel/Configuration/MonsterSpawnArea.cs`: `MonsterDefinition`, `GameMap`, `X1`,`Y1`,`X2`,`Y2`
(byte coordinates, an inclusive rectangle — `IsPoint()` at `:153` is `X1==X2 && Y1==Y2`), `Direction`,
`Quantity` (`short`), `SpawnTrigger`, `WaveNumber`, `MaximumHealthOverride`.

`SpawnTrigger` (`:13-65`): `Automatic`, `AutomaticDuringEvent`, `OnceAtEventStart`,
`AutomaticDuringWave`, `OnceAtWaveStart`, `ManuallyForEvent`, `Wandering`.

### Where the numbers are

Per map, in that map's initializer, via
`BaseMapInitializer.CreateMonsterSpawn` (`src/Persistence/Initialization/BaseMapInitializer.cs:240-270`)
— two overloads: point spawn `(number, monster, x, y, direction, trigger)` and area spawn
`(number, monster, x1, x2, y1, y2, quantity, direction, trigger, waveNumber)`.

Tarkan (`src/Persistence/Initialization/Version095d/Maps/Tarkan.cs:34-...`, map number 8, reused by
Season 6 via `VersionSeasonSix/GameMapsInitializer.cs:38`) is **entirely point spawns**, ~200 of them:

```csharp
yield return this.CreateMonsterSpawn(106, this.NpcDictionary[62], 146, 53);
yield return this.CreateMonsterSpawn(110, this.NpcDictionary[62], 148, 43);
yield return this.CreateMonsterSpawn(111, this.NpcDictionary[62], 155, 40);
```

Rect-with-quantity spawns look like this (`src/Persistence/Initialization/Version075/Maps/Devias.cs:63-66`):

```csharp
yield return this.CreateMonsterSpawn(102, this.NpcDictionary[20], 210, 242, 210, 220, 15);   // x1,x2,y1,y2,quantity
yield return this.CreateMonsterSpawn(103, this.NpcDictionary[20], 0, 251, 128, 245, 200);
```

Each spawn area is materialised `Quantity` times at map creation
(`src/GameLogic/MapInitializer.cs:92-103`); each instance picks a random walkable cell inside the
rectangle. Respawn timing comes from `MonsterDefinition.RespawnDelay`, not from the spawn area.

### "Denser cluster at known coordinates in Tarkan" — difficulty: trivial

One added line in `Tarkan.CreateMonsterSpawns` (or one new row in the admin panel's map editor):

```csharp
yield return this.CreateMonsterSpawn(900, this.NpcDictionary[62], 140, 160, 30, 50, 40); // 40 Tantallos in a 20x20 box
```

The spawn `number` argument only feeds `area.SetGuid(this.MapId, number)` (`:243`), so it must be
unique within the map. Live-editing the same thing is supported: the admin panel has a graphical map
editor with spawn-area CRUD (`src/Web/AdminPanel/Pages/EditMap.cs`,
`src/Web/Shared/Components/MapEditor/MapEditor.razor`, `MapCrudOperationsService.CreateSpawnArea`) plus
**JSON export/import of a map's whole spawn set** (`MapExportImportService.BuildExport` /
`ApplyImportAsync`, DTO in `MapSpawnExport.cs`) — import replaces all spawns of the map and preserves gates.

New spawn areas take effect immediately on a running server:
`src/GameLogic/MapInitializer.cs:105-118` registers `RegisterForNew<MonsterSpawnArea, GameMap>` and spawns
`Quantity` instances as soon as the row is created. Quantity/coordinate/definition edits are handled at
`:271-332` (dispose extra monsters, respawn on definition change, add missing ones).

---

## Q5 — Events and scheduling

### The mechanism exists and is generic

`IPeriodicTaskPlugIn` (`src/GameLogic/PlugIns/IPeriodicTaskPlugIn.cs`) is a plugin point invoked **once
per second** by every `GameContext` — timer created at `src/GameLogic/GameContext.cs:85`, dispatch at
`:487-500`.

`PeriodicTaskBasePlugIn<TConfiguration, TState>`
(`src/GameLogic/PlugIns/PeriodicTasks/PeriodicTaskBasePlugIn.cs`) implements the whole lifecycle —
`NotStarted → Prepared → Started → NotStarted` with `OnPrepareEventAsync` / `OnPreparedAsync` /
`OnStartedAsync` / `OnFinishedAsync` hooks, per-`IGameContext` state, and a `ForceStart()` used by the
`/startevent`-style chat commands.

`PeriodicTaskConfiguration` (same folder) is the shared schedule model: `Timetable` (`IList<TimeOnly>`,
UTC), `TaskDuration`, `PreStartMessageDelay`, `StartMessage`, `EndMessage`, plus the helper
`GenerateTimeSequence(duration, startLimit, endLimit)` and `IsItTimeToStart()` (5-second window match).

### Existing periodic tasks

| Plugin | File |
|---|---|
| Happy Hour (global XP multiplier) | `src/GameLogic/PlugIns/PeriodicTasks/HappyHourPlugIn.cs` |
| Devil Square / Blood Castle / Chaos Castle starts | `.../DevilSquareStartPlugIn.cs`, `BloodCastleStartPlugIn.cs`, `ChaosCastleStartPlugIn.cs` |
| Golden / Red Dragon / White Wizard invasions, generic `SimpleInvasionPlugIn` | `src/GameLogic/PlugIns/InvasionEvents/` |
| Wandering merchants | `src/GameLogic/PlugIns/WanderingMerchants/WanderingMerchantsPlugIn.cs` |

### Is there an XP-multiplier hook a rotating hot zone could ride on?

**Yes, two — both already wired, neither currently scheduled per map.**

1. **Per-map multiplier.** `GameMapDefinition.ExpMultiplier` is read *per kill* from the live map
   definition (`Player.cs:1259`, `Party.cs:346`) and is seeded to `1` for every map
   (`BaseMapInitializer.cs:119` is its only write site in the whole tree — verified by grep). A rotating
   hot zone is a periodic-task plugin that sets `ExpMultiplier = N` on the chosen map's definition and
   back to `1` on finish. No changes to the XP pipeline are needed. Nothing upstream does this today.

2. **Per-player attribute multiplier.** `HappyHourPlugIn` shows the exact pattern — a shared
   `SimpleElement(1.0f, AggregateType.Multiplicate)` added to each player's `Stats.ExperienceRate` and
   `Stats.MasterExperienceRate` on world-entry, whose `.Value` is flipped on start/finish
   (`HappyHourPlugIn.cs:26, 46-47, 82-92`). Global, not map-scoped, but map-scoping is possible by
   adding/removing the element on `IObjectAddedToMapPlugIn` / `IObjectRemovedFromMapPlugIn` — which is
   precisely what `MonsterAttributeScaler` does for monsters
   (`src/GameLogic/PlugIns/MonsterAttributeScaler.cs:66-80`).

So a hot zone is **custom plugin work, but small** — a `PeriodicTaskBasePlugIn` subclass with a map list
in its configuration. No engine changes.

### Plugin architecture shape

The framework is `src/PlugIns/` (`MUnique.OpenMU.PlugIns`, standalone, documented in
`src/PlugIns/Readme.md`). Three plugin flavours: regular plugin points (proxy object fans out to all
active implementations, `PlugInManager.GetPlugInPoint<T>()`), custom containers (e.g. view plugins per
client version), and strategy plugins (`GetStrategy<T>(key)` — used for chat commands, data
initialization, and configuration updates).

Game plugins live in `src/GameLogic/PlugIns/**` (plus `src/GameServer/RemoteView/**` for view plugins and
`src/Persistence/Initialization/Updates/**` for config updates). A plugin is a class with `[PlugIn]`,
`[Guid("…")]`, `[Display(…)]`, implementing a plugin-point interface; optionally
`ISupportCustomConfiguration<TConfig>` + `ISupportDefaultCustomConfiguration`, and `IDisabledByDefault`
to ship inactive.

Activation and configuration are **data**, not code:
`DataInitializationBase.cs:130-176` discovers every plugin type at seed time and writes a
`PlugInConfiguration` row (`src/PlugIns/PlugInConfiguration.cs` — `TypeId`, `IsActive`,
`CustomConfiguration` JSON, `CustomPlugInSource`, `ExternalAssemblyName`) into
`GameConfiguration.PlugInConfigurations`. The admin panel's Plugins page edits those rows, and changes
apply to the running server without restart via
`src/PlugIns/PlugInConfigurationChangeApplier.cs:22-68` (activate / deactivate / re-configure).

---

## Q6 — Config lifecycle **(the critical one)**

### What re-seeds the DB when `Persistence/Initialization` changes

**Nothing automatic.** Editing initializer code has zero effect on an existing database. There are
exactly three entry points:

1. **`-reinit` command-line flag** — `src/Startup/Program.cs:468`:
   ```csharp
   contextProvider = await this.PrepareRepositoryProviderAsync(args.Contains("-reinit"), version, loggerFactory, changeListener);
   ```
   `PrepareRepositoryProviderAsync` (`:476-527`) then calls
   `ReCreateDatabaseAsync(dropExistingDatabase: !assumeExternallyProvisioned)` and, since
   `-reinit` forces `assumeExternallyProvisioned = false` (`:487-488`), always drops.
   `PersistenceContextProvider.ReCreateDatabaseAsync` (`src/Persistence/EntityFramework/PersistenceContextProvider.cs:162-192`)
   runs `installationContext.Database.EnsureDeletedAsync()` → **the entire Postgres database is dropped**,
   then migrations run and `InitializeDataAsync` re-seeds from code.
   **Answer to the sub-question: `-reinit` wipes everything — accounts, characters, guilds, items — not
   just `GameConfiguration`.** There is no config-only reinit switch.

2. **First run** — the same path fires automatically when the database does not exist (`:479`).

3. **Admin panel → Setup → Install / Re-install** — `src/Web/AdminPanel/Pages/Setup.razor.cs:60-66`
   (with a JS `confirm`) → `SetupService.CreateDatabaseAsync` → `ReCreateDatabaseAsync()` with the
   default `dropExistingDatabase: true`. Same total wipe.

The version seeded is chosen by `-version:` (`Program.cs:443-452`, default `season6`), resolved to an
`IDataInitializationPlugIn` strategy at `:550`.

### Targeted slice re-seed — yes, first-class

`IConfigurationUpdatePlugIn` (`src/Persistence/Initialization/Updates/IConfigurationUpdatePlugIn.cs`) is a
strategy plugin point for exactly this: apply a *slice* of configuration change to an **existing**
database, idempotently and once.

A concrete example that changes only drop groups —
`src/Persistence/Initialization/Updates/AddItemDropGroupForJewelsUpdate075.cs`:

```csharp
[PlugIn] [Display(Name = PlugInName, Description = PlugInDescription)] [Guid("DCF14924-…")]
public class AddItemDropGroupForJewelsUpdate075 : UpdatePlugInBase
{
    public override UpdateVersion Version => UpdateVersion.AddItemDropGroupForJewels075;
    public override string DataInitializationKey => Version075.DataInitialization.Id;
    public override bool IsMandatory => false;
    public override DateTime CreatedAt => new(2024, 08, 26, 20, 0, 0, DateTimeKind.Utc);

    protected override async ValueTask ApplyAsync(IContext context, GameConfiguration gameConfiguration)
    {
        this.CreateDropItemGroupForJewels(context, gameConfiguration, 4, null, "The jewels drop item group (0.1 % drop chance)");
        this.AddJewelToItemDrop(gameConfiguration, 4, null, "Jewel of Bless");
        ...
    }
}
```

Mechanics: `UpdatePlugInBase.ApplyUpdateAsync` runs `ApplyAsync` then writes a `ConfigurationUpdate` row;
`DataUpdateService.DetermineAvailableUpdatesAsync` lists un-installed updates for the DB's
initialization key; `DataUpdateService.ApplyUpdatesAsync` runs the selected ones against the live
`GameConfiguration` and saves. The UI is the admin panel's **`/config-updates`** page
(`src/Web/AdminPanel/Pages/Updates.razor.cs`), with per-update checkboxes and progress.
Accounts and characters are untouched — the update only edits `GameConfiguration`.

Adding one requires: a new enum member in `src/Persistence/Initialization/Updates/UpdateVersion.cs`
(currently up to `AddRenaItem = 99`), a new class, a rebuild, and a server restart so the plugin type is
discovered — then a click. Note `DataInitializationBase.AddAllUpdateEntries` (`:202-226`) marks **all**
updates as already installed on a fresh seed, so a fresh install must contain the change in the
initializer itself, not only in an update plugin.

### Does the admin panel expose rate/drop editing live? Yes — and edits apply without restart

Editable surfaces (`src/Web/AdminPanel/Components/Layout/ConfigNavMenu.razor:10-23`):
System, Game Clients, **General (`GameConfiguration` — `ExperienceRate`, `MasterExperienceRate`,
`ExperienceFormula`, `ExcellentItemDropLevelDelta`, …)**, **Monsters**, Character Classes, Skills, Items,
**Drop Item Groups**, **Game Maps** (incl. `ExpMultiplier`, plus the map editor at `/map-editor/{id}`),
Mini Games, Warps, Jewel Mixes. Per-server `GameServerDefinition.ExperienceRate` is reachable from the
Servers page (`src/Web/AdminPanel/Components/ServerItem.razor:28`). Routes are generic:
`/edit-config-grid/{fullTypeName}` and `/edit-config/{fullTypeName}/{guid}`.

The propagation chain — this is what makes the sub-minute loop possible:

```
AutoForm save → IContext.SaveChangesAsync
  → EntityFrameworkContextBase (src/Persistence/EntityFramework/EntityFrameworkContextBase.cs:333-369)
      → IConfigurationChangeListener.ConfigurationChangedAsync
          → CacheAwareRepositoryProvider.UpdateCachedInstanceAsync   // mutates the *same* in-memory
                                                                     // config graph the game server holds
          → IConfigurationChangePublisher = ConfigurationChangeHandler (src/Startup/ConfigurationChangeHandler.cs:34-54)
              → PlugInManager.ApplyChangedConfiguration              // plugin activate/deactivate/reconfigure
              → ConfigurationChangeMediator.HandleConfigurationChangedAsync
                  → GameContext.OnGameConfigurationChangeAsync       // rebuilds both experience tables
                  → MapInitializer registrations                     // monster ReloadAttributes / respawn / new spawn areas
```

The admin panel is hosted in the same process as the game servers by default —
`Program.cs:430-441`: an absent `-adminpanel:` argument returns `true`.

**Live (no restart), verified by reading the read sites:**

| Lever | Why it's live |
|---|---|
| `GameConfiguration.ExperienceRate` / `MasterExperienceRate` | computed properties, read per kill (`GameContext.cs:114,117`) |
| `GameServerDefinition.ExperienceRate` | read per kill (`GameServerContext.cs:106,109`) |
| `GameConfiguration.ExperienceFormula` / `MasterExperienceFormula` / `MaximumLevel` | tables rebuilt in `OnGameConfigurationChangeAsync` (`GameContext.cs:478-484`) |
| `GameMapDefinition.ExpMultiplier` | read per kill from the live definition (`Player.cs:1259`) |
| `DropItemGroup.Chance` / `PossibleItems` / `Monster` / level bounds | re-read per kill from the cached config graph (`DefaultDropGenerator.cs:77-87`) |
| `MonsterDefinition` attributes | `attackableNpc.ReloadAttributes()` on change (`MapInitializer.cs:271-287`) |
| `MonsterSpawnArea` quantity / coords / monster | dispose/respawn logic (`MapInitializer.cs:289-332`); brand-new areas spawn via `RegisterForNew` (`:105-118`) |
| `PlugInConfiguration.IsActive` / `CustomConfiguration` | `PlugInConfigurationChangeApplier` (`src/PlugIns/PlugInConfigurationChangeApplier.cs:54-68`) |

**Requires a server restart** — everything captured in `DefaultDropGenerator`'s constructor
(`src/GameLogic/DefaultDropGenerator.cs:48-61`), because the generator is built once per `GameServer`
(`src/GameServer/GameServer.cs:68`) and never rebuilt on config change:
`ItemDefinition.DropsFromMonsters` (the `_droppableItems` list), the derived `_ancientItems` list,
`ExcellentItemDropLevelDelta` (`_excellentItemDropLevelDelta`), and `MaximumItemOptionLevelDrop`
(`_maxItemOptionLevelDrop`). Editing those in the panel persists them but the running server keeps the
old values.

### Do panel edits survive a reinit?

**No.** Panel edits are written to Postgres only; nothing writes back to the C# initializers, and
`-reinit` / Setup→Install drop the database. Any tuning done in the panel must be transcribed into
`Persistence/Initialization/**` (or into an `IConfigurationUpdatePlugIn`) before the next reinit, or it
is lost. The one partial escape hatch is the map editor's JSON spawn export/import
(`MapExportImportService`), which round-trips a map's spawn set outside the DB.

---

## Q7 — Prior art

Reusable, in rough order of usefulness to 002–004:

| Thing | Where | Why it matters |
|---|---|---|
| `HappyHourPlugIn` | `src/GameLogic/PlugIns/PeriodicTasks/HappyHourPlugIn.cs` + `HappyHourConfiguration.cs` | Complete worked example of a scheduled, configurable XP multiplier: timetable, duration, golden-message broadcast, per-player attribute element. Default: ×1.5 for 1h every 6h. |
| `MonsterAttributeScaler` (+ config) | `src/GameLogic/PlugIns/MonsterAttributeScaler.cs`, `MonsterAttributeScalerConfiguration.cs` | Global monster difficulty knob (HP/damage/defense/rates as %), applied via shared multiplicative elements on map-add, live-reconfigurable, `IDisabledByDefault`. Template for any "scale everything" balance plugin. |
| Invasion framework | `src/GameLogic/PlugIns/InvasionEvents/` (`BaseInvasionPlugIn`, `SimpleInvasionPlugIn`, `GoldenInvasionPlugIn`, `PeriodicInvasionConfiguration`, `SpawnMapStrategy`, `InvasionMaps`, `InvasionMonsters`) | Scheduled spawning of arbitrary monsters on arbitrary maps at random walkable coordinates, with start/end broadcasts and death announcements. `BaseInvasionPlugIn.CreateMonstersAsync` builds `MonsterSpawnArea` objects in memory — no DB rows needed for ephemeral event spawns. |
| Golden monsters | `src/Persistence/Initialization/VersionSeasonSix/InvasionMobsInitialization.cs`, `Version095d/InvasionMobsInitialization.cs` | Custom monsters with inflated stats + guaranteed monster-specific drop group (`AddBoxOfKundunToMonster`, `:150-161`). The elite-variant recipe. |
| ~115 configuration update plugins | `src/Persistence/Initialization/Updates/` | Every kind of targeted live-DB config edit already has an example: new drop groups (`AddItemDropGroupForJewelsUpdate*`), new monsters (`AddWhiteWizardInvasionMobsUpdatePlugIn`), drop tuning (`LimitWhiteWizardDropsUpdatePlugIn`), new attributes (`AddRandomExperienceConfigAttributes*`), monster stat fixes (`FixBloodCastleMonsterAttributesUpdatePlugIn`). |
| Map editor + spawn JSON | `src/Web/AdminPanel/Pages/EditMap.cs`, `src/Web/Shared/Components/MapEditor/**` | Graphical spawn CRUD with undo history, filters, and full-map JSON export/import. |
| GM chat commands | `src/GameLogic/PlugIns/ChatCommands/` — `CreateMonsterChatCommand`, `RemoveNpcChatCommand`, `ShowNpcIdsChatCommand`, `WalkMonsterChatCommand`, `SetLevelChatCommandPlugIn`, `ItemChatCommandPlugIn`, `StartDevilSquareEventChatCommandPlugIn` (+ Blood/Chaos Castle) | In-game testing loop without restarts: spawn a monster next to you, force-start an event, set your level, spawn an item. |
| Tests | `tests/MUnique.OpenMU.Tests/DropGeneratorTest.cs`, `ExperienceRateSplitTest.cs`, `MonsterAttributeReloadTests.cs`, `DroppedMoneyTest.cs`; `tests/MUnique.OpenMU.Persistence.Initialization.Tests/TestInitializationWithEfCore.cs` | Existing coverage for drop selection, XP-rate splitting between normal/master, live monster-attribute reload, and a full initialization-into-EF-Core smoke test — the harness for regression-testing balance changes already exists. |
| `PlayerLosesExperienceAfterDeathPlugIn` | `src/GameLogic/PlugIns/PlayerLosesExperienceAfterDeathPlugIn.cs` (+ configuration) | Configurable XP-loss-on-death, another example of a balance rule expressed as a configurable plugin. |

**Not found (verified by grep across `src/`):** any seasonal/date-based event scheduling (the schedule
model is time-of-day only — `PeriodicTaskConfiguration.Timetable` is `IList<TimeOnly>`, matched against
`DateTime.UtcNow` time-of-day at `PeriodicTaskConfiguration.cs:66-78`; there is no date or weekday
dimension), any per-map or per-level-range experience *rate* table, and any map that sets
`ExpMultiplier` to anything other than `1`.

---

## UNRESOLVED

* **Wall-clock cycle times for anything requiring a rebuild.** The .NET SDK is not installed in this
  environment (`which dotnet` → not found), so `dotnet build` / start-up duration could not be measured.
  Every cycle-time figure in `tuning-loop-options.md` is expressed in mechanical steps, not seconds.
  *Experiment for 002:* time `dotnet build src/MUnique.OpenMU.sln` (cold and incremental) and time
  server start-up to "listening" both with and without `-reinit`.
* **Whether an admin-panel edit propagates to game servers running in a separate process** (the Dapr
  deployment under `src/Dapr/**`, which routes changes through `ConfigurationChangePublisher` /
  `ConfigurationChangeController` instead of the in-process `ConfigurationChangeHandler`). The
  in-process `Startup` host — the default and the one 001 boots — is confirmed live by code reading.
  Not verified by running either topology.
* **Whether the client renders correctly for a `MonsterDefinition.Number` the client has no model for.**
  This is client-side behaviour and cannot be determined from this repo; the code comment at
  `src/DataModel/Configuration/MonsterDefinition.cs:158-161` warns that an unknown *NPC dialog* number
  crashes the client, which suggests caution, but says nothing about monster models.
  *Experiment for 002/004:* spawn a definition on an unused number with `/createmonster` and observe.
