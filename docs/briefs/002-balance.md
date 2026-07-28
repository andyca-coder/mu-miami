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

- [ ] Simulator runs with one documented command; assumptions (kills/min per band, source
      of monster XP values) stated inline
- [ ] Simulated hours: 1–150 ∈ [3,5] · 151–300 ∈ [8,12] · 301–380 ∈ [11,15] ·
      381–400 ∈ [7,10] · total ∈ [30,40]
- [ ] Drop worksheet shows per-context summed chance ≤ 0.85, with the merged contexts
      enumerated (global / map / monster)
- [ ] Excellent chance 0.03; jewel groups 3.0×±0.1 their seeded vanilla values (vanilla
      values quoted from source in the worksheet); Chaos Machine +13/+15 values byte-
      identical to upstream (grep-verified, path cited)
- [ ] Ancient drop group exists ONLY in Kalima/Aida/Icarus map contexts — verified by
      querying the applied config, not by reading the plugin
- [ ] Three clusters exist as spawn areas; counts + rects match the worksheet; visible in
      the admin panel map editor
- [ ] All changes applied to the 001 stack via `/config-updates`; `miamitest` and all 21
      accounts intact afterward (count verified before/after)
- [ ] Update plugins are new `MuMiami*` files only; `git diff upstream/master --stat`
      shows zero modified upstream files under `src/`
- [ ] `docs/UPSTREAM.md` rebase procedure written; solution builds clean
      (`dotnet build` green — install the SDK in whatever way the runbook documents)
- [ ] `docs/design/tuning-loop.md` exists with the restart-exception list

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

- **Result:** {{...}}
- **Escalations raised & how resolved:** {{...}}
- **Deviations from plan and why:** {{...}}
- **QA reviewer verdict:** {{...}}
- **Adjacent issues noticed (not fixed):** {{...}}
- **Suggested follow-up briefs:** {{...}}
