# The tuning loop

How a balance idea becomes a number on the live server, and how it survives.

```
   play  ──▶  edit in the admin panel  ──▶  play some more  ──▶  freeze into a
                (live, seconds)              (does it hold?)      MuMiami* update plug-in
                                                                        │
                                                                  scripts/mm build
                                                                  MM_IMAGE=... ; mm restart
                                                                  /config-updates → apply
                                                                        │
                                                                  scripts/mm verify-balance
```

**Nothing is real until it is in a plug-in.** Panel edits live only in Postgres. Nothing
writes back to the C# initializers, and a reseed drops the database. An evening of tuning
that never got frozen is an evening you will do again.

---

## 1. Tune live in the panel

<http://localhost:8380> → Game configuration. Edits propagate to the running server with no
restart, through `SaveChangesAsync → IConfigurationChangeListener → ConfigurationChangeMediator`.

| What you want to change | Where |
|---|---|
| The XP curve | General → `ExperienceFormula` (both experience tables are rebuilt on save) |
| Drop chances | Drop Item Groups |
| Monster stats | Monsters |
| Spawn clusters | Game Maps → map editor (`/map-editor/{id}`) |
| Per-map XP multiplier | Game Maps → `ExpMultiplier` (brief 003's hot zone lives here) |
| Plug-in on/off and settings | Plugins |

## 2. The restart-required exceptions

Everything above is live **except** what `DefaultDropGenerator` captures in its constructor
(`DefaultDropGenerator.cs:48-61`). The generator is built once per `GameServer`
(`GameServer.cs:68`) and never rebuilt on configuration change. Editing these persists the
value but the running server keeps the old one:

| Setting | Where it is captured | Symptom if you forget |
|---|---|---|
| `ItemDefinition.DropsFromMonsters` | `_droppableItems` | the item still does or doesn't drop, contrary to the panel |
| the derived ancient item pool | `_ancientItems` | an ancient drop group exists and never drops anything |
| `GameConfiguration.ExcellentItemDropLevelDelta` | `_excellentItemDropLevelDelta` | excellent eligibility uses the old level window |
| `GameConfiguration.MaximumItemOptionLevelDrop` | `_maxItemOptionLevelDrop` | option levels keep the old cap |

`scripts/mm restart` — about nine seconds, no reseed, accounts untouched.

Practical consequence for brief 002: `MuMiamiAncientDropsUpdatePlugIn` needs one restart
after applying. `MuMiamiDropRatesUpdatePlugIn` and `MuMiamiFarmingClustersUpdatePlugIn` do
not. Restart anyway — it costs nine seconds and removes a class of confusion.

## 3. Freeze it into a plug-in

Read the tuned values back out of the database (the worksheets in this folder each carry the
exact query), then either edit an existing `MuMiami*UpdatePlugIn` or add a new one:

- New file only, under `src/Persistence/Initialization/Updates/MuMiami/`.
- Version number from `MuMiamiUpdateVersions` — **append**, never renumber. Applied numbers
  are permanent; that number is what tells the panel an update is already installed.
- New `[Guid]`, `CreatedAt` set to now.
- **Idempotent.** The panel can be revisited and the update re-offered. Look the object up by
  its GUID, create it only if missing, then assign every field unconditionally. Applying
  twice must produce the same configuration, not two clusters. Verified for all four
  brief 002 plug-ins by applying them twice on a scratch database.
- Refuse cleanly rather than guess: if the object the update expects is missing, throw with a
  message that says what was expected. Half an update applied is worse than none.

Editing an existing plug-in only affects databases that have not installed it yet. To
re-apply an edited plug-in to *this* server, see §5.

## 4. Rebuild, redeploy, verify

The plug-ins are compiled into the server, so the image has to be rebuilt:

```bash
scripts/mm build                 # -> mumiami/openmu:<git-sha>
$EDITOR .env                     # MM_IMAGE=mumiami/openmu:<git-sha>
scripts/mm restart
# admin panel -> /config-updates -> tick the new update -> Apply
scripts/mm restart               # for the constructor-captured settings
scripts/mm verify-balance
```

`scripts/mm build` tags with the git SHA, and appends `-dirty` if `src/` has uncommitted
changes. A `-dirty` tag on the running server means the image cannot be rebuilt from any
commit — fine while iterating, not fine as a resting state. Commit and rebuild.

## 5. Re-applying an update that is already installed

The panel only offers updates that have no installed `ConfigurationUpdate` row. To re-apply
an edited Mu Miami plug-in:

```bash
scripts/mm balance-reoffer       # deletes ConfigurationUpdate rows with Version >= 9000
# panel -> /config-updates -> apply
```

This touches nothing but those rows. Because the plug-ins are idempotent, re-applying snaps
the configuration back to whatever the source says — which is also how you discard a panel
experiment you decided against.

## 6. After a reseed — the trap

**A fresh seed marks every Mu Miami update as installed without applying any of them.**
`DataInitializationBase.AddAllUpdateEntries` writes a `ConfigurationUpdate` row for every
plug-in it discovers, including ours, because upstream assumes a fresh install already
contains every change in the initializer itself. Ours are not in upstream's initializer — and
we do not edit upstream's initializer, because that is the fork discipline.

Measured on a scratch database seeded from the Mu Miami image:

```
ConfigurationUpdate rows 9001-9004 ... all present, all InstalledAt set
ExperienceFormula ................... stock "if(level == 0, 0, if(level < 256, 10 * ..."
excellent chance .................... 0.0001
ancient group ....................... absent
cluster spawn areas ................. 0
```

The server would have claimed to be balanced and played exactly like vanilla.

**Always run this after any reseed** (`scripts/mm reinit`, admin panel → Setup → Install, or
a first boot on an empty volume):

```bash
scripts/mm balance-reoffer
# panel -> /config-updates -> apply the Mu Miami updates
scripts/mm restart
scripts/mm verify-balance
```

Verified: fresh-seed-then-reoffer-then-apply produces configuration **byte-identical** to the
live database that received the same updates — same curve, same chances, same nine ancient
map attachments, same seven cluster spawn areas.

## 7. `-reinit` is never part of tuning

`-reinit` and admin panel → Setup → Install both run
`PersistenceContextProvider.ReCreateDatabaseAsync`, which calls `EnsureDeletedAsync()`. That
drops the entire `openmu` database: **accounts, characters, guilds, items, everything**. There
is no config-only reinit switch anywhere in OpenMU.

Nothing in a tuning loop needs it. Configuration updates edit `GameConfiguration` and never
touch `data.*` — the brief 002 apply left 22 accounts and 77 characters exactly as they were.

If you genuinely need a clean seed, back up first (`scripts/mm backup`), and expect to spend
the next ten minutes on §6.

---

## The one-page version

| Situation | Command |
|---|---|
| Try an idea | edit in the panel, play |
| Idea survived | write/edit a `MuMiami*UpdatePlugIn`, `scripts/mm build`, bump `MM_IMAGE`, restart, apply |
| Changed a plug-in already installed here | `scripts/mm balance-reoffer`, then apply |
| Just reseeded | `scripts/mm balance-reoffer`, then apply, then restart |
| Did I break the budget? | `scripts/mm verify-balance` |
| Did I break the curve? | `node scripts/simulate-progression.ts --from-db` |
| About to do something destructive | `scripts/mm backup` |
