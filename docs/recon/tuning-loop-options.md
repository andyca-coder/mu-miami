# Tuning-loop options — "change a rate" → "live on server"

Companion to `balance-map.md` (brief R1). **Mechanisms only — no recommendation.** Cycle times are
given in steps, not seconds: the .NET SDK is absent from the recon environment, so nothing was built or
timed (see UNRESOLVED in `balance-map.md`).

## A. Admin panel edit against the running server

**Steps:** open `http://<host>/edit-config/…` → change the field → Save. Done.
**Cycle:** one page save. No build, no restart, no reconnect; the next monster kill uses the new value.

**Covers** (propagation chain and evidence in `balance-map.md` Q6): `GameConfiguration.ExperienceRate`
and `MasterExperienceRate`; `ExperienceFormula` / `MasterExperienceFormula` / `MaximumLevel` (exp tables
rebuilt on change); per-server `GameServerDefinition.ExperienceRate`; `GameMapDefinition.ExpMultiplier`;
every `DropItemGroup` field (`Chance`, `PossibleItems`, `Monster`, level bounds); `MonsterDefinition`
attributes (spawned monsters call `ReloadAttributes`); `MonsterSpawnArea` coordinates / `Quantity` /
monster (respawn logic runs, new areas spawn immediately); plugin `IsActive` and custom configuration
(e.g. Happy Hour multiplier and timetable, `MonsterAttributeScaler` percentages). Map spawn sets can
also be replaced wholesale by JSON import in the map editor.

**Does not cover:** `ItemDefinition.DropsFromMonsters`, `ExcellentItemDropLevelDelta` and
`MaximumItemOptionLevelDrop` — captured in `DefaultDropGenerator`'s constructor, which runs once per
`GameServer`. Saving them persists the value but the running server keeps the old one until restart.

**Durability:** the value lives only in Postgres. It is **lost** on the next `-reinit` / Setup→Install,
and it does not exist in git. Anything kept must be transcribed to option B or C.

## B. Edit the initializers + full reinit

**Steps:** edit `src/Persistence/Initialization/**` → `dotnet build` → restart with `-reinit`.
**Cycle:** build + full re-seed + server start.

`-reinit` calls `EnsureDeletedAsync()` — it **drops the entire database**: accounts, characters, guilds,
inventories, everything, not just `GameConfiguration`. There is no config-only reinit flag. Same wipe
happens via the admin panel's Setup → Install/Re-install button.

**Covers:** everything, including the constructor-captured drop-generator inputs that option A cannot
reach, and the seeded defaults for a fresh install.
**Durability:** authoritative — this is the state a new environment gets. Lives in git.

## C. Edit the initializers + a targeted `IConfigurationUpdatePlugIn`

**Steps:** write the change into the initializer (so fresh installs get it) **and** into a new
`UpdatePlugInBase` subclass under `src/Persistence/Initialization/Updates/` with a new `UpdateVersion`
enum member → `dotnet build` → restart the server (so the plugin type is discovered) → admin panel
`/config-updates` → tick the update → Apply.
**Cycle:** build + restart + one click.

The update runs against the live database inside a normal persistence context, is recorded as a
`ConfigurationUpdate` row so it never runs twice, and touches only `GameConfiguration`. **Accounts and
characters survive.** Roughly 115 upstream examples exist covering drop groups, new monsters, drop
tuning, new attributes and monster stat fixes.

Caveat: a fresh seed marks *all* update plugins as already installed
(`DataInitializationBase.AddAllUpdateEntries`), so the change must also be present in the initializer
itself or new environments will not have it.

**Durability:** in git, and applies to existing databases. The most code per change of the three.

## Cross-cutting notes

* **A and B/C are not exclusive.** A is the only path that avoids a build; B/C are the only paths that
  reach git. A common shape is: iterate in A, then transcribe the settled numbers into B or C.
* **In-game GM commands** (`/createmonster`, `/startevent`-family, `/setlevel`, `/item`, `/teleport`)
  shorten the *observation* half of any loop without touching configuration at all.
* **The admin panel is in-process by default** (`-adminpanel:` absent ⇒ enabled), which is what makes
  option A's propagation immediate. A split/Dapr deployment routes changes differently and was not
  verified.
* **One-way coupling to watch while tuning:** monster money drop = granted experience + 7, so an XP-rate
  change silently rescales Zen income; and monster XP is derived from `Stats.Level`, so making a monster
  worth more XP also changes its drop eligibility and master-XP eligibility.
