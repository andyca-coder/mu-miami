# Cluster worksheet — the three farming spots

**Brief:** 002 · **Contract:** [`balance-canon.md`](balance-canon.md) § Farming geography
**Shipped in:** `MuMiamiFarmingClustersUpdatePlugIn`
**Status:** first pass, live on the server, awaiting a visual tuning pass in the map editor

---

## What a cluster is, mechanically

A `MonsterSpawnArea` with `X1 != X2`: an inclusive byte rectangle plus a `Quantity`.
`MapInitializer` (`:92-103`) materialises the monster `Quantity` times at map creation, each
instance on a random *walkable, non-safezone* cell inside the rectangle. Respawn timing comes
from `MonsterDefinition.RespawnDelay`, not from the area.

New areas take effect **live** — `MapInitializer` (`:105-118`) registers for new
`MonsterSpawnArea` rows and spawns immediately. No restart.

Stock Dungeon, Tarkan and Kanturu Ruins contain **no rectangle spawns at all**: 570, 217 and
210 spawns respectively, every one of them a point spawn of quantity 1. These three areas are
the first rectangles on those maps, which is also what makes them easy to find and audit —
check 6 of `verify-balance.sh` simply selects `WHERE X1 <> X2`.

## Density budget

The brief caps first-pass quantities at "~2× the densest vanilla spawn area on that map".
Taken literally that reads as 2 × 1, because every stock area on these maps has quantity 1.
The measurement that carries the intended meaning is **spawns per 20×20 cell window**, so
that is what was measured — sliding a 20×20 window over every stock spawn point on the map
and taking the maximum:

| Map | Stock spawns | Densest 20×20 window | Map-wide average per 20×20 | Cap (2×) |
|---|---|---|---|---|
| Dungeon (1) | 570 | **18** | 3.5 | 36 per 400 cells |
| Tarkan (8) | 217 | **8** | 1.3 | 16 per 400 cells |
| Kanturu Ruins (37) | 210 | **9** | 1.3 | 18 per 400 cells |

Every cluster below sits under its cap. All three are 4–10× the map-wide average density,
which is the part a player actually feels.

## The three clusters

Rectangles were chosen from map data, not by eye. Each one:

1. contains stock spawn points of the monsters it adds — proof the cells are reachable and
   that the monster belongs there;
2. was checked against the map's real `GameMapDefinition.TerrainData` (walkable and outside
   the safezone) pulled from the live database, not against a guess.

### Dungeon — "Brickell After Dark" (low band)

| | |
|---|---|
| Rectangle | x **5–30**, y **101–126** (26 × 26 = 676 cells) |
| Spawnable cells | **645** (95 %) |
| Monsters | Dark Knight (#10, level 48) × 24 · Gorgon (#18, level 55) × 12 |
| Total | **36** |
| Density | 21.3 per 400 cells = **1.18×** the map's densest stock window (cap 2×) |
| Stock anchors inside the box | Dark Knight (14,107) and (19,120); Gorgon (8,123) |

Dark Knight and Gorgon are the two highest-level ordinary monsters in Dungeon — the level-80
entries on that map are traps, not farmable. The whole Dark Knight / Gorgon population is
already concentrated in the map's west side, so the cluster thickens a place players already
walk through rather than inventing one.

### Tarkan — "Calle Ocho" (mid band, the flagship)

| | |
|---|---|
| Rectangle | x **150–175**, y **195–225** (26 × 31 = 806 cells) |
| Spawnable cells | **722** (89 %) |
| Monsters | Death Beam Knight (#63, level 93) × 18 · Beam Knight (#61, level 84) × 6 |
| Total | **24** |
| Density | 11.9 per 400 cells = **1.49×** the map's densest stock window (cap 2×) |
| Stock anchors inside the box | the map's *only* Death Beam Knight, at (161,225); Beam Knights at (157,214), (161,218), (167,225) |

This is the change a player notices first. Stock Tarkan has **one** Death Beam Knight on the
entire 256×256 map; Calle Ocho has eighteen in one corner. It is also the cluster the
simulator leans on hardest — levels 111–280, 10.2 of the 35 hours.

### Kanturu Ruins — "Wynwood" (high band)

| | |
|---|---|
| Rectangle | x **120–150**, y **90–130** (31 × 41 = 1271 cells) |
| Spawnable cells | **736** (57 % — the map is heavily built up) |
| Monsters | Genocider Warrior (#556, lvl 129) × 18 · Gigantis Warrior (#555, lvl 128) × 12 · Kentauros Warrior (#554, lvl 126) × 10 |
| Total | **40** |
| Density | 12.6 per 400 cells = **1.40×** the map's densest stock window (cap 2×) |
| Stock anchors inside the box | Genocider Warrior (131,81)…(148,129); Gigantis Warrior (126,109), (129,114), (134,107); Kentauros Warrior (132,111), (133,127), (141,133) |

The Season 6 "Warrior" variants are the highest-level monsters on the map and carry
180k–220k HP, which is why the simulator assumes only 10 kills/min here. Levels 281–400,
22.5 of the 35 hours.

## Spawn numbering

`MonsterSpawnArea.SetGuid(mapNumber, number)` — the number only has to be unique within its
map. Highest stock number on each of the three maps: Dungeon 768, Tarkan 316, Kanturu Ruins
962. Mu Miami uses **9001, 9002, 9003**, far enough clear that a rebase adding stock spawns
cannot collide.

## Verified on the live server

```
$ scripts/mm verify-balance
6. Farming clusters
  PASS  7 cluster spawn areas across 3 maps, 100 monsters total
        Dungeon   | Dark Knight (lvl 48)        | x5-30    y101-126 | qty 24
        Dungeon   | Gorgon (lvl 55)             | x5-30    y101-126 | qty 12
        Tarkan    | Beam Knight (lvl 84)        | x150-175 y195-225 | qty 6
        Tarkan    | Death Beam Knight (lvl 93)  | x150-175 y195-225 | qty 18
        Kanturu_I | Genocider Warrior (lvl 129) | x120-150 y90-130  | qty 18
        Kanturu_I | Gigantis Warrior (lvl 128)  | x120-150 y90-130  | qty 12
        Kanturu_I | Kentauros Warrior (lvl 126) | x120-150 y90-130  | qty 10
```

No map-thread impact was observed after spawning: the three maps gained 36 / 24 / 40
monsters against stock populations of 570 / 217 / 210, and the server restarted and ran with
no warnings beyond the two known-harmless boot messages.

## Tuning them visually

The rectangles above are a first pass, deliberately conservative. To move them:

1. Admin panel → **Game configuration → Game Maps → the map → map editor**
   (`/map-editor/{id}`). The Mu Miami areas are the only rectangles on these maps, so they
   are easy to spot.
2. Drag / resize / change quantity. Changes apply to the running server immediately —
   `MapInitializer` disposes surplus monsters and spawns missing ones.
3. Play in the spot. Keep what survives a session.
4. **Freeze it back into the plug-in**, or the next reseed loses it: read the final numbers
   out of the database and update the `Clusters` array in
   `MuMiamiFarmingClustersUpdatePlugIn.cs`.

```sql
-- the numbers to paste back into the plug-in
SELECT m."Number" AS map, md."Number" AS monster, md."Designation",
       sa."X1", sa."X2", sa."Y1", sa."Y2", sa."Quantity"
FROM config."MonsterSpawnArea" sa
JOIN config."GameMapDefinition" m ON m."Id" = sa."GameMapId"
JOIN config."MonsterDefinition" md ON md."Id" = sa."MonsterDefinitionId"
WHERE sa."X1" <> sa."X2" AND m."Number" IN (1, 8, 37)
ORDER BY m."Number", md."Number";
```

The plug-in rewrites the same GUIDs every time it is applied, so a re-apply after editing the
array snaps the live areas back to whatever the file says — no duplicate clusters. See
[`tuning-loop.md`](tuning-loop.md).

If you move a rectangle by hand, re-check walkability: a box over water or wall spawns fewer
monsters than `Quantity` claims, silently.
