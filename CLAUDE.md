# Mu Miami

A private, self-hosted **MU Online Season 6 Episode 3** server, forked from
[MUnique/OpenMU](https://github.com/MUnique/OpenMU) and run natively on Apple Silicon via
OrbStack. Personal/friends scale. Not public, not commercial.

- **Runbook** (clean clone → running, backup/restore, reinit, troubleshooting):
  [`docs/runbook.md`](docs/runbook.md)
- **Upstream record** (fork base SHA, pinned digests, local image, **rebase procedure**):
  [`docs/UPSTREAM.md`](docs/UPSTREAM.md)
- **Balance canon** (the *why* — curve, drop budget, farming geography):
  [`docs/design/balance-canon.md`](docs/design/balance-canon.md)
- **Tuning loop** (how a balance idea becomes a number that survives a reseed):
  [`docs/design/tuning-loop.md`](docs/design/tuning-loop.md)
- **Briefs** (the unit of work in this repo): [`docs/briefs/`](docs/briefs/)

## ⚠️ The most dangerous command in this repo

```bash
docker compose ... down -v     # DESTROYS mumiami-pgdata = the entire server
```

`-v` deletes the `mumiami-pgdata` volume: every account, character, guild, and
configuration edit, gone, with no confirmation prompt. It is one keystroke away from the
`down` you type ten times a day, and muscle memory from other projects will try to type it.

**Use `scripts/mm down`**, which refuses `-v` and points you at `scripts/mm nuke` — the only
sanctioned path, and it makes you type `NUKE MU MIAMI` in full.

Back up before anything destructive: `scripts/mm backup`.

## Running it

```bash
scripts/mm up          # start        scripts/mm logs      # follow logs
scripts/mm down        # stop, keep data   scripts/mm ps    # status
scripts/mm restart     # down + up    scripts/mm psql      # psql on the openmu DB
scripts/mm backup      # pg_dump      scripts/mm restore <file>
scripts/mm reinit      # DESTRUCTIVE: drop + re-seed stock S6E3
scripts/mm nuke        # DESTRUCTIVE: delete the data volume
scripts/mm htpasswd    # (re)generate admin panel credentials

scripts/mm build       # build the server image from src/  (no host .NET needed)
scripts/mm dotnet ...  # run the .NET SDK against the repo, in a container
scripts/mm verify-balance    # balance acceptance checks against the live config
scripts/mm balance-reoffer   # re-offer the Mu Miami config updates after a reseed
```

The stack runs a **locally built image** from brief 002 onward (`MM_IMAGE` in `.env`), because
the balance changes are configuration update plug-ins compiled into the server. Unset
`MM_IMAGE` and you are back on upstream's stock image, playing vanilla.

The `mm` commands other than `build` / `dotnet` wrap exactly one invocation:

```bash
docker compose --env-file .env \
  -f deploy/all-in-one/docker-compose.yml \
  -f compose.mumiami.yml <cmd>
```

Both `-f` flags, in that order, always.

Admin panel: **<http://localhost:8380>** (user/password from `MM_ADMIN_USER` /
`MM_ADMIN_PASSWORD` in `.env`).

## Port map

| Service | Host port | Notes |
|---|---|---|
| Admin panel (nginx) | **8380** | remapped from upstream's 80; `MM_ADMIN_PORT` in `.env` |
| Game servers | 55901–55906 | stock |
| Connect — original/GMO client | 44405 | stock |
| Connect — MuMain open-source client | **44406** | stock; this is the one the client work uses |
| Chat | 55980 | stock |
| Postgres | *(none)* | internal to the compose network, by design |

The connect server advertises `MM_RESOLVE_IP` (this Mac's LAN IP) as the address clients
should dial for game servers. See Gotchas and `docs/runbook.md § 7`.

## Fork discipline

This is a **fork we intend to keep rebasing**, so upstream rebases must stay boring.

1. **Customization is additive.** Everything Mu Miami adds lives in files upstream does not
   have: `compose.mumiami.yml`, `scripts/`, `mumiami/`, `CLAUDE.md`, `docs/` (excluding
   upstream's own docs). We do not edit `deploy/all-in-one/docker-compose.yml` — we layer
   an override on top of it.
2. **`src/` is upstream's — we only add to it.** Brief 002 added
   `src/Persistence/Initialization/Updates/MuMiami/`, and modified nothing. Not one upstream
   file under `src/` differs from upstream/master, including `UpdateVersion.cs`: Mu Miami's
   update versions live in `MuMiamiUpdateVersions.cs` in the 9000 block and are cast to the
   enum at the one point the interface demands that type.
3. **Configure through documented override points, never by editing the image.**
   `DB_HOST` / `DB_ADMIN_USER` / `DB_ADMIN_PW`, the `-reinit` / `-resolveIP:` / `-version:`
   startup arguments. See `docs/UPSTREAM.md`.
4. **Pin by digest.** Never `latest`, for any image. Record the digest in `docs/UPSTREAM.md`
   when you bump.
5. Repo lives at **`~/code/mu-miami`**. Never `~/Documents` — iCloud eviction corrupts git
   operations; this was diagnosed on this machine.

Check yourself before opening a PR:

```bash
git diff --name-only upstream/master -- src/ | grep -v MuMiami    # must print nothing
```

`scripts/mm verify-balance` asserts this too (check 5). Rebase procedure:
[`docs/UPSTREAM.md § Rebase procedure`](docs/UPSTREAM.md).

## Secrets

- `.env` and `mumiami/.htpasswd` are gitignored and must stay that way.
  `.env.example` is the committed template.
- Upstream ships hardcoded credentials (Postgres `admin`, admin panel `admin:openmu`).
  Both are overridden here. If you ever see `openmu` accepted as the admin panel password,
  the override is not being applied.
- Database dumps (`mu-miami-backup-*.sql`) contain account password hashes. Gitignored;
  keep them off shared drives.

## Gotchas

Real findings from brief 001. Each one cost time to discover.

- **Compose resolves relative paths against the *first* `-f` file's directory**, not against
  the file the path is written in and not against your cwd. That is why the `.htpasswd`
  mount in `compose.mumiami.yml` is `../../mumiami/.htpasswd` — the project directory is
  `deploy/all-in-one/`. It also means the repo-root `.env` is **not** auto-loaded, hence
  `--env-file .env` in every invocation.
- **Compose concatenates `ports` lists across files; it does not replace them.** Overriding
  a published port needs the `!override` tag. Without it, upstream's `80:80` survives
  alongside our `8380:80` and the admin panel is on both. `volumes` are different — they
  merge by container-side target path, so those do replace cleanly.
- **Upstream published `"8080"` on `openmu-startup`**, which Docker maps to a random host
  port that reaches the admin panel **directly, bypassing nginx basic auth entirely**. Our
  override re-declares the port list without it. If you ever see `->8080/tcp` in
  `docker port mumiami-openmu`, the admin panel is unauthenticated on that port.
- **`-reinit` drops the whole `openmu` database.** Not just config — accounts, characters,
  and every configuration edit. Verified by watching the database OID change. There is no
  config-only reseed. Measured before/after table in `docs/runbook.md § 4`.
- **The connect server advertises an address, and by default it's the wrong one.** The
  default resolver is `Auto`, which inside Docker resolves to the machine's *public* IP
  (observed: `GameServer ... has registered with endpoint "134.56.250.177:55901"`) — an
  address that routes from neither this Mac nor the LAN. Fixed via `MM_RESOLVE_IP` in
  `.env`, set to this Mac's LAN IP. **This is a DHCP lease and can move**; `scripts/mm up`
  warns when it drifts. Symptom if it's wrong: the server list loads, then entering a world
  hangs — looks like the game server is down, isn't. Tradeoff table: `docs/runbook.md § 7`.
- **Only the `postgres` role's password is env-overridable.** OpenMU's
  `ConnectionSettings.xml` also defines `config`/`account`/`friend`/`guild` roles with
  hardcoded matching passwords, and `DB_ADMIN_PW` only rewrites connection strings
  containing `User Id=postgres;`. Contained today because Postgres has no host port mapping,
  but do not expose 5432 without dealing with this first. Same reason `POSTGRES_USER` must
  stay `postgres` — a different name silently no-ops the override.
- **Postgres data lives at `/var/lib/postgresql`, not `/var/lib/postgresql/data`.** That is
  correct for Postgres 18, which moved `PGDATA` to `/var/lib/postgresql/<major>/docker`.
  Upstream's unpinned `image: postgres` would break this on the next major; we pin `18`.
- **`Cannot load library libgssapi_krb5.so.2`** on every boot is harmless — Npgsql probing
  for Kerberos support that the .NET runtime image does not ship.
- **`FATAL: database "openmu" does not exist`** in the Postgres log on first boot is also
  harmless — the app races `initdb`, then creates the database itself.
- **The image entrypoint already contains `-autostart`.** Connect servers and all three game
  servers are running by the time you reach the admin panel; you do not need to start them
  by hand.
- **macOS firewall prompt on first port bind: allow it.** Silently dismissed means the
  server is invisible to the LAN, and you will not notice until a friend cannot connect.
- **OrbStack gives containers routable IPs from macOS.** `http://$(docker inspect
  mumiami-openmu --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}'):8080`
  reaches the admin panel without the nginx basic-auth prompt — handy for scripted or
  automated access. Do not publish that port; see above.

## Balance (brief 002, done)

Balance is **no longer stock**. The contract is
[`docs/design/balance-canon.md`](docs/design/balance-canon.md); the how is
`src/Persistence/Initialization/Updates/MuMiami/`.

- **~35 hours to level 400** via a four-phase piecewise `ExperienceFormula`. Every rate
  multiplier (`ExperienceRate`, per-server, per-map `ExpMultiplier`, character attributes) is
  still 1.0 — the curve is the mechanism. Do not "help" by adding a rate multiplier; you will
  double-count and you will break brief 003's hot zone.
- **Excellent 3 %, jewels 3× stock, ancients only in Kalima / Aida / Icarus.**
- **Chaos Machine +13/+15 odds are untouched and stay untouched.** That is the whole scarcity
  design. `scripts/mm verify-balance` fails if any `MuMiami*` file so much as mentions
  `ItemCrafting`.
- **Drop budget ≤ 0.95** summed chance in any monster context (amended from 0.85 — stock
  itself is at 0.8681). **Every change that adds or raises a drop group must re-run
  `scripts/mm verify-balance`.**
- Three farming clusters: Dungeon, Tarkan, Kanturu Ruins.

Two traps worth knowing before you touch any of it:

- **A reseed silently reverts the balance while claiming it is installed.** Run
  `scripts/mm balance-reoffer` after any reseed. See `docs/design/tuning-loop.md § 6`.
- **Panel edits are not real until frozen into a `MuMiami*UpdatePlugIn` and rebuilt.**

Still stock, and still tempting: PvP and class balance, skill tuning, monster stats.

## Scope discipline

Hot zones, elites and new monster definitions are **brief 003**. Miami theming beyond the
names canon already fixes is **005**. If you are reading this while doing something else,
that is exactly when the temptation lands.
