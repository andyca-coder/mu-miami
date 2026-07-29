# Brief 002: Mu Miami Balance — curve, drops, farming clusters

| | |
|---|---|
| **Status** | approved |
| **Depends on** | 001 (done), R1 (done), `docs/design/balance-canon.md` (the contract) |
| **Parallel-safe with** | none — first brief to touch `src/`; establishes the pattern others follow |
| **Owns files** | `src/**/MuMiami*` (new files only), `docs/design/**`, `docs/briefs/002*`, `scripts/**`, `docs/UPSTREAM.md` (rebase section) |
| **Risk level** | medium — first `src/` modifications; all changes ship as additive update plugins; accounts must survive |
| **Executor** | Claude Code |

## Objective

The server plays like the balance canon says: ~35 hours to level 400 across four phases,
jewels at ~3x vanilla, excellent items at ~3%, ancients only in Kalima/Aida/Icarus, dense
farming clusters in Dungeon/Tarkan/Kanturu — with +13/+15 Chaos Machine odds untouched.
All of it lands as durable, account-preserving configuration migrations, verified by a
simulator that computes hours-to-level from the actual config, and tunable live afterward.

## Context the Builder Needs

**The contract is `docs/design/balance-canon.md`.** Read it first. The mechanism map is
`docs/recon/balance-map.md` (file paths, class names, excerpts — trust it; it was verified
against a running server). Key mechanisms, confirmed:

- Level→required-XP curve: mXparser expression string in `GameConfiguration`, already
  piecewise-capable via `if()`. The curve work is a config-value rewrite, not engine code.
- Drops: `DropItemGroup.Chance` (0–1) at monster/character/map scope, merged per kill;
  chances summing past 1.0 normalize → **budget ≤ 0.85 total in any monster context** is a
  hard acceptance constraint.
- Durable changes ship as `IConfigurationUpdatePlugIn` implementations (the repo has ~115
  as prior art) applied via the admin panel's `/config-updates` page. Accounts survive.
  `-reinit` is forbidden in this brief.
- Restart-required exceptions: excellent-drop deltas + `DropsFromMonsters` (constructor-
  captured in `DefaultDropGenerator`). The verification flow must account for this.
- Spawn clusters: inclusive byte rects + quantity on `MonsterSpawnArea`; the admin panel
  map editor exports per-map JSON.

**First `src/` brief — establish the fork discipline (this outlives the brief):**

1. New files only, named `MuMiami*` (e.g. `MuMiamiBalanceUpdatePlugIn.cs`), living beside
   the upstream patterns they mirror. Zero edits to existing upstream files. If a change
   genuinely cannot be additive, that's a stop-and-escalate, not a workaround.
2. Write `docs/UPSTREAM.md § Rebase procedure`: fetch upstream → rebase → the only
   conflict surface is `MuMiami*` files + docs → build → apply pending config updates on a
   scratch DB → smoke test. Document it as an operator checklist.

## Out of Scope

- Hot zone / periodic events (003). Elites / new monster definitions (003, blocked on
  client-ID evidence). Miami theming beyond what canon already names (005).
- **Chaos Machine +13/+15 odds — do not touch, do not "improve."**
- PvP balance, class balance, skill tuning — not this server's fight.
- Client work of any kind.

## Implementation Plan

1. **Simulator first** (`scripts/simulate-progression.ts` or `.csx` — builder's call,
   must run from repo root with one command). Input: an XP-curve expression + assumed
   kills/minute per band (document assumptions inline; source monster XP from the actual
   initializer values, not guesses). Output: cumulative hours to each phase boundary +
   total to 400, as a table. This is the acceptance instrument for the curve — build it
   before touching config.
2. **Derive the curve.** Iterate the piecewise expression against the simulator until
   phase hours land within canon tolerances (below). Record the final expression AND the
   simulator table in `docs/design/curve-worksheet.md`.
3. **Drop budget worksheet.** Read the five seeded default groups' actual chances from the
   initializer source; compute the Mu Miami allocation (jewels ×3, excellent →0.03,
   ancients group ~0.002 scoped to Kalima/Aida/Icarus maps, commons untouched); verify
   the ≤0.85 sum in every monster context (per-map + per-monster + global merged). Table
   goes in `docs/design/drop-worksheet.md`.
4. **Cluster definitions.** Three spawn-area additions (Dungeon / Tarkan / Kanturu) as
   data: coordinates + quantity, top-of-band monsters per canon. Reasonable first-pass
   coordinates from map data are fine — Andy tunes visually later via the panel editor.
5. **Ship as update plugins.** One or more `MuMiami*UpdatePlugIn` classes (mirror the
   newest upstream update plugin's pattern exactly): curve expression, drop groups,
   ancient group + map scoping, spawn areas. Version/date them per upstream convention.
6. **Apply + verify on the live stack** (the one from 001, real accounts intact): apply
   via `/config-updates`, restart once (constructor-captured values), then run the
   verification script.
7. **Tuning loop doc** (`docs/design/tuning-loop.md`, ≤1 page): the panel-edit → freeze →
   plugin cycle, the restart-required exception list, and the "never `-reinit`" warning.

## Acceptance Criteria

- [x] Simulator runs with one documented command; assumptions (kills/min per band, source
      of monster XP values) stated inline
      — `node scripts/simulate-progression.ts`. Monster levels are parsed from the
      initializer sources at run time; the farming plan and kills/min are stated inline with
      reasoning.
- [x] Simulated hours: 1–150 ∈ [3,5] · 151–300 ∈ [8,12] · 301–380 ∈ [11,15] ·
      381–400 ∈ [7,10] · total ∈ [30,40]
      — **4.02 / 10.01 / 12.97 / 7.99, total 34.99.** All PASS.
- [x] Drop worksheet shows per-context summed chance ≤ ~~0.85~~ **0.95**, with the merged
      contexts enumerated (global / map / monster)
      — **AMENDED, see Escalations.** Stock S6E3 is already at 0.8681 in its worst context,
      so 0.85 was unreachable without cutting groups canon protects. Canon amended to 0.95
      with the measurement recorded. Delivered worst context: **0.9080**.
- [x] Excellent chance 0.03; jewel groups 3.0×±0.1 their seeded vanilla values (vanilla
      values quoted from source in the worksheet); Chaos Machine +13/+15 values byte-
      identical to upstream (grep-verified, path cited)
      — excellent 0.03, jewels 0.003 = exactly 3.0× the seeded 0.001. Chaos Machine verified
      three ways: live rows printed, `grep` over `Updates/MuMiami/` returns zero files, and
      `git diff upstream/master -- VersionSeasonSix/ChaosMixes.cs` is empty.
- [x] Ancient drop group exists ONLY in Kalima/Aida/Icarus map contexts — verified by
      querying the applied config, not by reading the plugin
      — 9 map attachments (Kalima 1–7, Aida, Icarus), 0 monster attachments, and no
      ancient-type group anywhere else. Queried, not read.
- [x] Three clusters exist as spawn areas; counts + rects match the worksheet; visible in
      the admin panel map editor
      — 7 spawn areas, 100 monsters, matching `docs/design/cluster-worksheet.md`. They are
      the only rectangle spawns on those three maps.
- [x] All changes applied to the 001 stack via `/config-updates`; `miamitest` and all 21
      accounts intact afterward (count verified before/after)
      — applied through the panel. **22 accounts / 77 characters before and after**
      (recounted at execution, as the brief instructed).
- [x] Update plugins are new `MuMiami*` files only; `git diff upstream/master --stat`
      shows zero modified upstream files under `src/`
      — zero. Asserted by `scripts/mm verify-balance` check 5, not just by inspection.
- [x] `docs/UPSTREAM.md` rebase procedure written; solution builds clean
      (`dotnet build` green — install the SDK in whatever way the runbook documents)
      — full solution build **0 errors** (353 pre-existing upstream warnings). No SDK was
      installed on the Mac: `scripts/mm dotnet` runs it in a container.
- [x] `docs/design/tuning-loop.md` exists with the restart-exception list

## Edge Cases to Handle

- Expression evaluation cost: the curve string is evaluated on level-up/exp events —
  confirm the piecewise form doesn't regress the exp-table rebuild the change mediator
  triggers (R1 traced it; verify it completes without error after apply).
- A drop-group multiplication that pushes one specific monster context over budget while
  the global sum looks fine — the worksheet must check the *merged* per-monster maximum,
  not just group totals.
- Update plugin applied twice (panel allows re-visits): must be idempotent or refuse
  cleanly, matching upstream plugin convention.
- Applying to a fresh DB (future reseed) vs. the live 001 DB: both paths must produce
  identical config — test both (scratch DB via a second compose project name, never via
  `-reinit` on the real one).
- Spawn quantity high enough to lag the map thread: cap first-pass cluster quantities at
  ~2× the densest vanilla spawn area on that map; note the observed value.

## Verification Script

```bash
cd ~/code/mu-miami
git diff upstream/master --stat -- src/ | grep -v MuMiami   # expect: empty (docs/scripts aside)
<simulator command>                                          # table within tolerances
dotnet build                                                 # green
./scripts/mm up
# admin panel → /config-updates → apply pending → restart stack once
./scripts/mm restart
# panel: account count unchanged (22 incl. miamitest baseline — recount at execution)
# panel: map editor shows the three clusters
# psql (via docker exec): ancient group present in exactly 3 map contexts; excellent=0.03;
#   jewel groups at 3x quoted vanilla; chaos machine rows identical to upstream values
# 10-minute manual play (or the simulator's spot-check mode) sanity-checks Ignition pace
```

## Stop-and-Escalate

- Any change that cannot be made through a new `MuMiami*` file
- The piecewise expression cannot hit the hour tolerances without touching
  `ExperienceRate` multipliers (canon says expression-only — escalate for a canon
  amendment rather than silently switching mechanism)
- The update-plugin path cannot express something (e.g. spawn areas) — report which
  mechanism upstream uses for that class of change instead
- Any acceptance query requires schema access the read-only tools can't provide
- Applying updates on the live DB errors mid-apply — stop immediately, report state,
  restore path is the 001 backup pair

## Handback

**Result:** Done and live. The server at `MM_IMAGE=mumiami/openmu:739447193-dirty` runs the
Mu Miami curve, drop rates, ancient scoping and three farming clusters, on the brief 001
database with all 22 accounts and 77 characters intact. `scripts/mm verify-balance` passes
every check.

Shipped:

| | |
|---|---|
| `scripts/simulate-progression.ts` | the acceptance instrument; parses monster levels and the shipped curve from source, evaluates the mXparser expression itself |
| `scripts/verify-balance.sh` (+ `mm verify-balance`) | 8 acceptance checks against the live config, including the standing worst-context drop measurement |
| `scripts/mm build` / `mm dotnet` / `mm balance-reoffer` | containerised SDK + image build; reseed recovery |
| `src/.../Updates/MuMiami/` (5 files) | `MuMiamiUpdateVersions`, ExperienceCurve, DropRates, AncientDrops, FarmingClusters |
| `docs/design/` | `curve-worksheet.md`, `drop-worksheet.md`, `cluster-worksheet.md`, `tuning-loop.md`, amended `balance-canon.md` |
| `docs/UPSTREAM.md` | rebase procedure + local image record |

**Escalations raised & how resolved:**

1. **The stack ran upstream's published image, so no `src/` code could ever execute.** The
   brief assumed `/config-updates` would offer `MuMiami*` plug-ins; it cannot, because those
   are compiled into the server. Raised before starting. Resolved by Andy: build a Mu Miami
   image from source (`scripts/mm build`, `MM_IMAGE` in `.env`), keeping upstream's
   digest-pinned image as the default for a clean clone. No host SDK — the build runs in
   `mcr.microsoft.com/dotnet/sdk:10.0-alpine`.
2. **Canon's ≤ 0.85 drop budget was below stock's own floor.** Measured stock worst context:
   **0.8681**, Icarus / Dark Phoenix (level 108) — 0.5 money + 0.3 random-item + ~0.06 of
   overlapping quest-item windows upstream attaches to every map. Hitting 0.85 would have
   required cutting groups canon lists as "vanilla — texture preserved". Raised with three
   options; Andy chose to amend canon to **≤ 0.95**, record the stock baseline and the query,
   and make the worst-context measurement a standing rule for all future drop changes rather
   than a one-off. Done: `balance-canon.md` amended, query shipped as check 3.

**Deviations from plan and why:**

- **`UpdateVersion.cs` was not edited.** The plan implied adding an enum member; that is an
  upstream file. `MuMiamiUpdateVersions.cs` declares the 9000 block and casts at the single
  point the interface requires the enum type. Legal C#, zero upstream diff.
- **Cluster coordinates came from map data, not the panel's map editor.** The brief allowed
  "reasonable first-pass coordinates from map data". Each rectangle was placed on stock spawn
  anchors and validated against the map's real `TerrainData` pulled from the live database
  (95 % / 89 % / 57 % walkable). Visual tuning is still Andy's, and `cluster-worksheet.md § 
  Tuning them visually` says exactly how to freeze the result back.
- **The density cap was reinterpreted.** "~2× the densest vanilla spawn area on that map"
  reads as 2 × 1 on these maps — every stock area there is a point spawn of quantity 1. Used
  spawns-per-20×20-window instead: observed maxima 18 / 8 / 9, clusters at 1.18× / 1.49× /
  1.40×. Observed values recorded, as the brief asked.
- **Two upstream updates rode along.** The source-built image carries updates 98 and 99,
  both `IsMandatory = true`, which the panel applies whether or not you tick them. Effect on
  balance: `AddRenaItem` adds a 0.01 drop group to every map (+0.0100 of the 0.9080). Mu
  Miami's own contribution is +0.0319 exactly as designed. Flagged rather than fought — they
  are upstream data fixes the fork wanted anyway.

**QA reviewer verdict:** no separate reviewer; verification is mechanical and repeatable.
`scripts/mm verify-balance` — all checks PASS. Both edge cases the brief called out were
tested on a throwaway scratch stack, not reasoned about:

- **Fresh DB vs. live DB produce identical config** — verified field by field (curve md5,
  every chance, the 9 ancient attachments, an md5 over the cluster geometry). Identical.
- **Applied twice** — idempotent: still 7 spawn areas / 100 monsters / 1 ancient group / 9
  attachments, no duplicates.
- **The reseed trap is real and was measured.** A fresh seed marks all four updates installed
  and applies none of them: stock curve, 0.0001 excellent, no ancient group, no clusters — a
  server that claims to be balanced and plays like vanilla. `scripts/mm balance-reoffer` is
  the fix; documented in the runbook, `tuning-loop.md` and `CLAUDE.md`.

**Adjacent issues noticed (not fixed):**

- **The running image is tagged `-dirty`.** `src/` had uncommitted changes at build time.
  Commit, `scripts/mm build`, update `MM_IMAGE`, restart — the tag then pins a real commit.
  Until then the running server is not reproducible from any SHA.
- **Two monsters exceed the drop budget in a context check 3 cannot see.** Crywolf's
  Destructive Ogre Soldier and Archer carry a 0.8 monster-owned Wizard's Ring group on top of
  their map's ~0.87. They are event spawns with no automatic spawn area, so they never enter
  the measurement. Recorded in `drop-worksheet.md`. Harmless today; a trap for whoever makes
  them ordinary spawns.
- **`ExcellentItemDropLevelDelta` is still 25.** With excellent at 3 % this is now a much
  more load-bearing number than it was at 0.01 % — it gates both eligibility and which item
  pool an excellent is drawn from. Left alone deliberately (canon says nothing about it), but
  it is the first knob to reach for if 3 % feels wrong in play.
- **Two characters have items with no definition** (`TheCreator`, slot 38), logged as
  warnings on every boot. Pre-existing, unrelated to balance.
- **The panel's Updates page needs a scroll to reach the Apply button** on a 737 px viewport;
  a click on the button while it is off-screen silently does nothing. Cosmetic, upstream.

**Suggested follow-up briefs:**

- **003 as planned** — hot zones (`ExpMultiplier = 1.5` on a rotating map, the mechanism is
  free and untouched), elites, new monster definitions. Note it must re-run check 3 if it
  adds any drop group.
- **A 10-minute play validation.** Every number here is measured against config; none of it
  has been felt. `node scripts/simulate-progression.ts --spot-check <level>` prints the
  expected XP per kill for exactly this purpose. If kills/min is wrong, the curve multipliers
  move and everything downstream re-derives cleanly.
- **CI for the fork invariant.** `git diff --name-only upstream/master -- src/ | grep -v
  MuMiami` and `scripts/mm dotnet build` are two commands that would catch a discipline
  break at commit time instead of at rebase time.
