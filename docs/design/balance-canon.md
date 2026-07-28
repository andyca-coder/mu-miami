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

## The drop budget

Hard constraint from engine behavior: when per-kill drop-group chances sum past 1.0 the
roulette normalizes and something ALWAYS drops — loot becomes slot-machine noise. All
tuning respects a **total budget ≤ 0.85** summed chance in any monster context, leaving
headroom for future event groups.

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

- **Low (≈30–120):** Dungeon cluster
- **Mid (≈150–280):** Tarkan cluster — the flagship spot
- **High (≈300+):** Kanturu cluster

Implemented as spawn-area quantity/rect edits. Coordinates chosen in 002 via the admin
panel's graphical map editor, exported as JSON, frozen into the update plugin.

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
