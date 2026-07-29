# Mu Miami — Balance Canon

Locked 2026-07-28 in design session. This document is the *why*; brief 002 is the *how*.
Changes to these numbers happen here first, then flow to briefs.

## Design thesis

Fast to gear, slow to perfect. Generous excellent drops (~3%) mean build variety arrives
quickly and friends can compete on builds within the first week. Scarcity lives in exactly
two places: the +13/+15 Chaos Machine gamble (vanilla-brutal, untouched) and ancient sets
(geographically locked). Jewels drop ~3x vanilla because upgrade roulette is the endgame
and it needs fuel. The server has a heartbeat: every two hours a Miami-named hot zone
lights up one map.

## The curve — ~35 hours to 400

Mechanism: rewrite the level→required-XP piecewise expression (mXparser `if()` config
string). We do NOT touch ExperienceRate multipliers — the expression is live-editable,
per-band tunable, and survives as one config value.

| Phase | Levels | Target hours | Feel |
|---|---|---|---|
| Ignition | 1–150 | ~4 | Fresh character to real build in one evening |
| The climb | 151–300 | ~10 | Steady; map progression drives it |
| The grind | 301–380 | ~13 | The comparison zone between friends |
| The summit | 381–400 | ~8 | Each level is an event worth announcing |
| Master | 400+ | slow burn | Retirement plan, near-vanilla pace |

Hours are the contract; XP values are derived. Calibration assumes a solo character on
band-appropriate maps at ordinary kill pace — measured, not guessed (002 builds the
simulator).

**Delivered in 002** (`node scripts/simulate-progression.ts`, derivation in
`curve-worksheet.md`): 4.02 / 10.01 / 12.97 / 7.99 hours, **34.99 total**. Mechanism is the
expression only — every rate multiplier is still 1.0. Stock, under the identical farming
plan, is 1119 hours.

## The drop budget

Hard constraint from engine behavior: when per-kill drop-group chances sum past 1.0 the
roulette normalizes and something ALWAYS drops — loot becomes slot-machine noise.

**Budget: ≤ 0.95** summed chance in any monster context.

> **Amended 2026-07-28 during brief 002, from ≤ 0.85.** The original number was set before
> anyone measured stock. Stock Season 6 Episode 3 is *already* at **0.8681** in its worst
> context — **Icarus / Dark Phoenix (level 108)**, at upstream data version 97 — mostly the
> 0.5 money group, the 0.3 random-item group, and ~0.06 of overlapping quest-item windows
> (Blood Bone, Devil's Key, Devil's Eye, Old Scroll, Scroll of Archangel, Illusion Sorcerer
> Covenant) that upstream attaches to every map. 0.85 was therefore unreachable without
> cutting groups this same table protects as "vanilla — texture preserved". 0.95 keeps a
> real margin below the 1.0 cliff and is honest about where stock actually sits. Full
> measurement and composition: `drop-worksheet.md`.
>
> **Standing rule.** Any future change that adds or raises a drop group — hot zones, events,
> seasonal content, new monster loot — must re-run the worst-context measurement:
>
> ```bash
> scripts/mm verify-balance      # check 3
> ```
>
> It reproduces `DefaultDropGenerator`'s roulette exactly (level-window filtering, guaranteed
> `Chance >= 1.0` groups excluded) across every map/monster pair that actually spawns, and
> reports the maximum. Every context stays **under 1.00 with explicit margin**. If a new
> group would push a context past 0.95, **scope the group away from the hot contexts** — fewer
> maps, or a `MinimumMonsterLevel` / `MaximumMonsterLevel` window — rather than raising the
> budget. The budget moves only here, and only with a fresh measurement recorded next to it.
>
> Measured after brief 002: worst context **0.9080** (several Blood Castle 5/6 and Devil
> Square 7 monsters tie), margin 0.092 below the cliff.

| Lever | Setting | Why |
|---|---|---|
| Jewels (Bless/Soul/Chaos et al.) | ~3x vanilla chance | A jewel per session, not per week; fuels the Chaos Machine endgame |
| Excellent items | ~3% | Generous by design — build variety fast, gear is content |
| +10/+11/+12 via jewels | vanilla | — |
| +13/+15 Chaos Machine odds | **vanilla, untouched** | The scarcity anchor. With 3% excellent, this is the only wall left — it stays brutal |
| Ancient sets | new drop group, low chance, **Kalima / Aida / Icarus only** | Geography must mean something; pilgrimage loot |
| Money/common base groups | vanilla | Texture preserved |

Engine caveats (from R1, verified): excellent-drop deltas and `ItemDefinition.DropsFromMonsters`
are captured in `DefaultDropGenerator`'s constructor — panel edits to those persist but need a
stack restart to take effect. Everything else propagates live.

## Farming geography

Dense clusters of existing top-of-band monsters at fixed, learnable coordinates — the
classic "everyone knows the spot" tradition:

- **Low (≈30–120):** Dungeon cluster — "Brickell After Dark"
- **Mid (≈150–280):** Tarkan cluster — "Calle Ocho", the flagship spot
- **High (≈300+):** Kanturu cluster — "Wynwood"

Implemented as spawn-area quantity/rect edits, shipped in `MuMiamiFarmingClustersUpdatePlugIn`.
Delivered in 002 as a first pass chosen from map data (stock spawn anchors + real terrain
walkability) rather than from the map editor; sizes, coordinates and quantities are in
`cluster-worksheet.md`. Andy tunes them visually in the panel afterwards and freezes the
result back into the plug-in — `tuning-loop.md`.

**Elites are deferred to 003** — honest engine reason: `MaximumHealthOverride` only retunes
HP; bonus XP and custom loot require new monster definitions, which is blocked on the
unknown-client-model-ID question until the client is running and can answer it empirically.

## The heartbeat — rotating hot zone (built in 003, canon here)

Every **2 hours**, one map gets **+50% XP** (`GameMapDefinition.ExpMultiplier = 1.5`) with
a server-wide announcement. Rotation covers all level bands so it's always relevant to
someone. Zone names, first set:

| Map | Zone name |
|---|---|
| Lorencia | Ocean Drive |
| Dungeon | Brickell After Dark |
| Tarkan | Calle Ocho |
| Kanturu | Wynwood |

Announcement voice: warm, a little neon. "Ocean Drive is heating up — +50% XP in Lorencia."
Schedule is time-of-day based (engine limit: no weekday dimension) — fine for this rotation.

## Tuning philosophy

Live admin-panel edits propagate to the running server without restart (exceptions above).
The loop: tune in the panel while playing → what survives a play session gets frozen into
an `IConfigurationUpdatePlugIn` migration → committed. `-reinit` is never part of tuning —
it drops the entire database including accounts.
