# Curve worksheet — deriving Mu Miami's ~35 hours to 400

**Brief:** 002 · **Contract:** [`balance-canon.md`](balance-canon.md) § The curve
**Instrument:** `node scripts/simulate-progression.ts`
**Status:** derived, shipped in `MuMiamiExperienceCurveUpdatePlugIn`, verified against the live server

---

## The mechanism, and why it is the only one used

Canon: the curve is the level→required-XP expression, not the rate multipliers. Every
multiplier stays at 1.0:

| Lever | Value | Seeded at |
|---|---|---|
| `GameConfiguration.ExperienceRate` | 1.0 | `GameConfigurationInitializerBase.cs:40` |
| `GameServerDefinition.ExperienceRate` | 1.0 | `DataInitializationBase.cs:253` |
| `Stats.ExperienceRate` / `BonusExperienceRate` | 1 / 0 | `CharacterClassInitialization.cs:164` |
| `GameMapDefinition.ExpMultiplier` | 1, every map | `BaseMapInitializer.cs:119` |

That is a deliberate cost: it means the whole curve has to come out of one string. The
payoff is that the string is per-band tunable, live-editable from the admin panel, and
leaves all four multipliers free for the brief 003 hot zone (`ExpMultiplier = 1.5`) without
double-counting.

## Shape

Stock Season 6 is `10 · V(level)` below 256, where

```
V(level) = (level + 8) · (level - 1)²
```

plus a second cubic above 255. Mu Miami keeps `V` and varies the multiplier per phase,
adding a constant per branch so the function is continuous at the seams:

| Phase | Levels | Branch condition | Multiplier | vs. stock's 10 |
|---|---|---|---|---|
| Ignition | 1–150 | `level <= 151` | 3.20 | 0.32× |
| The climb | 151–300 | `level <= 301` | 1.17 | 0.117× |
| The grind | 301–380 | `level <= 381` | 1.64 | 0.164× |
| The summit | 381–400 | else | 3.04 | 0.304× |

**Why the boundaries are 151 / 301 / 381 and not 150 / 300 / 380.** The cost of the step
"level 150 → 151" is `F(151) − F(150)`, and that step is the last level of Ignition. Putting
the branch at `level <= 150` would charge it at the *climb* multiplier and leak one level's
cost across the phase boundary. `Player.cs:2003` reads `expTable[level + 1]`, which is what
fixes the convention.

**Why the summit multiplier jumps back up.** 1.64 → 3.04 at level 381 makes each of the
last twenty levels ~1.85× more expensive than the ones before it. That is canon's "each
level is an event worth announcing", expressed as a number.

### Seam constants

```
V(151) = 159 · 150² =  3,577,500
V(301) = 309 · 300² = 27,810,000
V(381) = 389 · 380² = 56,171,600
V(400) = 408 · 399² = 64,954,008

F(151) = 3.20 · 3,577,500                     = 11,448,000
F(301) = F(151) + 1.17 · (27,810,000 −  3,577,500) = 39,800,025
F(381) = F(301) + 1.64 · (56,171,600 − 27,810,000) = 86,313,049
F(400) = F(381) + 3.04 · (64,954,008 − 56,171,600) = 113,011,569
```

Multipliers are written as integer fractions (`16 · X / 5`, not `3.2 · X`) so no decimal
separator appears in the string at all.

### The expression

```
if(level == 0, 0,
 if(level <= 151, 16 * (level + 8) * (level - 1) * (level - 1) / 5,
  if(level <= 301, 11448000 + 117 * ((level + 8) * (level - 1) * (level - 1) - 3577500) / 100,
   if(level <= 381, 39800025 + 41 * ((level + 8) * (level - 1) * (level - 1) - 27810000) / 25,
    86313049 + 76 * ((level + 8) * (level - 1) * (level - 1) - 56171600) / 25))))
```

Shipped as `MuMiamiExperienceCurveUpdatePlugIn.ExperienceFormula` (one line, no whitespace
between branches). The simulator parses it out of that file, so the thing scored below is
literally the thing that ships.

Total experience to level 400: **113,011,569** (stock: 3,822,148,080 — 33.8× more).

## How the hours were calibrated

Hours are linear in the multiplier within a phase — the seam constants are additive offsets
that cancel out of every `F(L+1) − F(L)`. So one run at multiplier 1 gives the whole
calibration:

```bash
node scripts/simulate-progression.ts --expression "(level + 8) * (level - 1) * (level - 1)"
```

| Phase | Hours at multiplier 1 | Canon target | Required multiplier | Chosen | Resulting hours |
|---|---|---|---|---|---|
| Ignition | 1.255 | 4 | 3.186 | 3.20 | 4.02 |
| The climb | 8.556 | 10 | 1.169 | 1.17 | 10.01 |
| The grind | 7.910 | 13 | 1.644 | 1.64 | 12.97 |
| The summit | 2.634 | 8 | 3.038 | 3.04 | 7.99 |

## The simulator table (the acceptance instrument)

```
$ node scripts/simulate-progression.ts

| Phase      | Levels    | Hours  | Cumulative | Canon target | Tolerance   | Verdict |
|------------|-----------|--------|------------|--------------|-------------|---------|
| Ignition   | 1-150     |   4.02 |       4.02 |         ~4 h |      [3, 5] |   PASS  |
| The climb  | 151-300   |  10.01 |      14.03 |        ~10 h |     [8, 12] |   PASS  |
| The grind  | 301-380   |  12.97 |      27.00 |        ~13 h |    [11, 15] |   PASS  |
| The summit | 381-400   |   7.99 |      34.99 |         ~8 h |     [7, 10] |   PASS  |
| TOTAL      | 1-400     |  34.99 |      34.99 |        ~35 h |    [30, 40] |   PASS  |
```

Stock Season 6 under the identical farming plan, for scale:

```
$ node scripts/simulate-progression.ts --vanilla
| Ignition   | 1-150     |  12.56 |   | The climb  | 151-300   | 117.18 |
| The grind  | 301-380   | 640.09 |   | The summit | 381-400   | 349.25 |
| TOTAL      | 1-400     | 1119.07 h                                     |
```

1119 hours → 35. The compression is not uniform: Ignition drops 3.1×, the summit 43.7×.
That is the over-level penalty (below) doing most of the work in the late game, and it is
why a flat `ExperienceRate` could never have produced this shape.

## What is measured and what is assumed

**Measured — transcribed from the engine, not approximated:**

- `AttackableExtensions.CalculateBaseExperience` (`:593-615`) in full, including the
  over-level penalty `× (targetLevel + 10) / killerLevel` once `killerLevel > targetLevel + 10`,
  and the `+ (targetLevel − 64) · (targetLevel / 4)` bonus for monsters at level 65+.
- `Player.CalculateExpAfterKill` (`:1238-1274`): the four multipliers, all 1.0.
- `GameContext.CreateExpTable` (`:462-475`): the table is `(long)` truncated per level, and
  the simulator truncates identically.
- Monster levels: parsed from the initializer sources at run time. Not one monster level is
  typed into the simulator — `Death Beam Knight` resolves to level 93 because
  `Version095d/Maps/Tarkan.cs` says so.

**Assumed — the farming plan, stated inline in `scripts/simulate-progression.ts`:**

| Levels | Map | Monster | Monster level | Kills/min | Hours |
|---|---|---|---|---|---|
| 1–10 | Lorencia | Spider | 2 | 30 | 0.2 |
| 11–20 | Lorencia | Bull Fighter | 6 | 28 | 0.3 |
| 21–35 | Noria | Stone Golem | 18 | 24 | 0.3 |
| 36–55 | **Dungeon cluster** | Dark Knight | 48 | 20 | 0.3 |
| 56–75 | Lost Tower | Balrog | 66 | 18 | 0.3 |
| 76–110 | Atlans | Hydra | 74 | 18 | 1.0 |
| 111–280 | **Tarkan cluster** | Death Beam Knight | 93 | 15 | 10.2 |
| 281–400 | **Kanturu cluster** | Genocider Warrior | 129 | 10 | 22.5 |

The three farming clusters carry 33.0 of the 35.0 hours. That is intentional — a cluster
nobody levels in is decoration.

Two honest caveats on the plan:

1. **Canon's cluster bands are looser than the plan.** Canon calls the Dungeon cluster
   "low (≈30–120)". A level-110 character would not actually be there: at level 110, a
   level-48 Dark Knight yields ~700 XP against ~3,000 from an Atlans Hydra, because of the
   over-level penalty. The plan models where the XP actually is; the canon band describes
   who the cluster is *for*. Both are true, they are answering different questions.
2. **Kills/minute is the only number in this document with no source.** It is a judgement
   call per band, falling as monster HP outgrows a character's damage and dropping to 10/min
   for the 218k-HP Kanturu Warriors. If a real play session disagrees, change those numbers
   and re-run — the multipliers will move, and that is the loop working as designed.

The hot zone (+50 % XP on one map every two hours, brief 003) is deliberately **not**
modelled. It is a bonus on top of this pace, not part of it.

## Spot-checking against the live server

```bash
node scripts/simulate-progression.ts --from-db        # score the curve the server is running
node scripts/simulate-progression.ts --spot-check 320 # XP/kill and minutes for one level
```

`--spot-check` prints the expected XP per kill. In game: `/level` yourself to that level,
kill ten of the named monster, and compare. Expect ±20 % scatter — `Stats.RandomExperience
Min/MaxMultiplier` are seeded 0.8 and 1.2, and the simulator uses the 1.0 mean.

## Engine checks this curve had to survive

- **Expression evaluation cost.** The table is built once at start-up and once per
  configuration change (`GameContext.OnGameConfigurationChangeAsync`), 402 evaluations
  total, not per kill. Four nested `if()`s cost nothing measurable. Verified in practice:
  applying the update on the live server rebuilt both experience tables with no error in the
  log and no interruption to the running game servers.
- **Truncation.** Every branch produces a value well inside `long`; the largest is
  113,011,569 at level 400. Stock reaches 3.8 billion, so this curve is strictly less
  demanding of the type.
- **`level` values outside 1–400.** `CreateExpTable` evaluates 0 through `MaximumLevel + 1`
  = 401. `level == 0` short-circuits to 0; 401 falls in the final branch and is finite.
