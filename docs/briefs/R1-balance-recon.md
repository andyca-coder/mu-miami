# Brief R1: Balance Recon — map OpenMU's config surface for briefs 002–004

| | |
|---|---|
| **Status** | approved |
| **Depends on** | 001 step 1 only (repo cloned) — otherwise fully independent |
| **Parallel-safe with** | 000, 001 — **read-only against `src/`**, writes only `docs/recon/**` |
| **Owns files** | `docs/recon/**` |
| **Risk level** | low (zero code changes) |
| **Executor** | Claude Code (separate session from 001 — clean context, different job) |

## Objective

A written map of exactly where and how OpenMU defines the five things Mu Miami will customize: experience rates, drop tables, monster spawns, monster stats, and server events. Briefs 002–004 get written against this map — exact file paths, class names, and mechanisms — instead of "locate the pattern and mirror it." This document is the difference between a builder session that executes and one that explores.

## Context

OpenMU seeds its entire game configuration **from C# code** under `src/Persistence/Initialization/` into Postgres. The admin panel can also edit config live in the DB. The recon must resolve the tension between those two paths (see Q6) — the answer determines 002's whole tuning-loop design. Version of record: the fork-base SHA in `docs/UPSTREAM.md` (or upstream `master` if R1 starts before 001 lands it).

## Out of Scope

- **No code changes, no proposals, no opinions on what the rates should be.** Facts and mechanisms only. Balance design happens in 002 with Andy.
- No exhaustive catalog of every map/monster — representative examples + the pattern.

## Questions to Answer (the deliverable is the answers)

Each answer = file path(s) + class/method names + a minimal code excerpt + a one-paragraph "how you'd change it."

1. **Experience rate.** Where is the XP formula/multiplier? Is there a global rate? Per-server? Is the level→required-XP table code or formula? Can rates differ by level range natively, or does tiering require custom code?
2. **Drop system.** How are `DropItemGroup`s structured? Chance representation (per-kill %? weighted?)? Monster-specific vs map-wide vs global drops? Where do jewels (Bless/Soul/Chaos) get their drop chances? Where does excellent-item chance live? Ancient sets?
3. **Monster definitions.** Where are monster stats (HP/dmg/XP-value) defined? Is XP granted derived from monster level or explicit? Could an "elite variant" be a new monster definition reusing an existing model/appearance — what's the minimal set of fields?
4. **Spawn system.** Where are per-map spawn areas/counts? Format (coordinates? rects? quantity?)? How hard is "denser cluster at known coordinates in Tarkan"?
5. **Events/scheduling.** Does a periodic-event mechanism exist (invasions, happy hour)? Is there any XP-multiplier-by-map hook that a rotating hot zone could ride on, or is that custom plugin work? What's the plugin architecture's shape (`src/GameLogic/Plugins*` or wherever it actually lives)?
6. **Config lifecycle — the critical one.** When code under `Persistence/Initialization` changes, what re-seeds the DB? Does `-reinit` wipe accounts/characters or only `GameConfiguration`? Can a targeted slice (just drop groups, just one monster) be re-seeded? Does the admin panel expose rate/drop editing live, and do panel edits survive a reinit? **002's sub-minute tuning loop gets designed directly from this answer.**
7. **Prior art.** Do upstream docs/tests/plugins already demonstrate custom rates, custom monsters, or seasonal events? List anything reusable.

## Deliverables

- `docs/recon/balance-map.md` — the seven answers, structured as above
- `docs/recon/tuning-loop-options.md` — ≤1 page, mechanisms only (no recommendation): the 2–3 viable paths from "change a rate" → "live on server," with honest cycle-time estimates for each

## Acceptance Criteria

- [ ] All seven questions answered with concrete paths + names + excerpts — zero "presumably"/"likely" language; anything unverifiable is marked **UNRESOLVED** with what was tried
- [ ] Q6 answered definitively enough to design 002's tuning loop from the document alone
- [ ] Both deliverables exist under `docs/recon/`
- [ ] `git status` shows changes under `docs/recon/**` only

## Stop-and-Escalate

- Initialization code doesn't live under `src/Persistence/Initialization` in this version → report actual location, continue there
- Any question requires *running* modified code to answer → mark UNRESOLVED with the experiment 002 should run; do not modify code to find out
