# Upstream record

What Mu Miami is forked from and pinned to. Update this file whenever you rebase on
upstream or bump a pinned image — it is the thing that makes "why did it break?"
answerable six months from now.

## Fork

| | |
|---|---|
| Upstream | `https://github.com/MUnique/OpenMU.git` (remote name: `upstream`) |
| Origin | `https://github.com/andyca-coder/mu-miami.git` |
| Fork-base SHA | `1b2994e02c7154b491738abdc86382eb759d6e12` |
| Fork-base commit | `Merge pull request #855 from bulgarashi/docs/progress-golden-archer` (2026-07-27) |
| Local clone | `~/code/mu-miami` — **never** `~/Documents` (iCloud eviction corrupts git operations; diagnosed on this machine) |

`git log --oneline upstream/master..HEAD` should only ever show Mu Miami commits.

From brief 002 onward this fork **adds** files under `src/`. It still modifies none. The
invariant that replaces "src/ is empty" is:

```bash
git diff --name-only upstream/master -- src/ | grep -v MuMiami    # must print nothing
```

Everything Mu Miami adds under `src/` lives in
`src/Persistence/Initialization/Updates/MuMiami/` and is named `MuMiami*`. That is checked
automatically by `scripts/mm verify-balance` (check 5).

## Pinned images

Verified on 2026-07-28 on Apple Silicon (OrbStack, `docker context show` → `orbstack`).

| Image | Tag | Manifest-list digest |
|---|---|---|
| OpenMU all-in-one | `munique/openmu:0.9.10` | `sha256:681c52b304a44e70d26003e85193e167353d28eeee75cf1a49e8eae2add41ded` |
| Postgres | `postgres:18` | `sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a` |
| nginx | `nginx:alpine` | `sha256:4a73073bd557c65b759505da037898b61f1be6cbcc3c2c3aeac22d2a470c1752` |

All three are pinned by digest in `compose.mumiami.yml`. Never `latest`.

### arm64 confirmation (re-run this after any bump)

```bash
docker manifest inspect munique/openmu:0.9.10 | grep -A2 arm64
docker image inspect munique/openmu:0.9.10 --format '{{.Architecture}}'   # -> arm64
```

`munique/openmu:0.9.10` publishes `linux/amd64`, `linux/arm/v7` and **`linux/arm64`**.
The arm64 child manifest is `sha256:d881b5ab80dbe8e1d3e773bd777cf75bbccd63cbb9cfbd7da1a1f2ea62763a5d`.
It runs natively — no Rosetta, no `platform:` pin needed, no emulation warnings.

### Why Postgres is pinned even though upstream leaves it floating

Upstream's `deploy/all-in-one/docker-compose.yml` uses bare `image: postgres` (= `latest`)
and mounts the data volume at **`/var/lib/postgresql`**, not `/var/lib/postgresql/data`.
That works because Postgres 18 moved `PGDATA` to `/var/lib/postgresql/<major>/docker`.
A silent bump to Postgres 19 would change that path and present as total data loss.
`postgres:18`'s digest is identical to `postgres:latest` as of 2026-07-28 — pinning
costs nothing today and prevents that failure mode later. Runtime version verified:
PostgreSQL 18.4 (Debian).

## Local image (from brief 002)

The stack no longer runs upstream's published image by default. Everything Mu Miami adds to
the game — the `MuMiami*` configuration update plug-ins — is **compiled into the server**, so
`/config-updates` cannot offer the balance changes unless the server was built from this
repo's `src/`.

```bash
scripts/mm build                    # -> mumiami/openmu:<git-sha>   (appends -dirty if src/ is dirty)
$EDITOR .env                        # MM_IMAGE=mumiami/openmu:<git-sha>
scripts/mm restart
```

| | |
|---|---|
| Built from | `src/Startup/Dockerfile` (upstream's, unmodified), context `src/` |
| Build host requirement | **none** — the SDK runs in a container. There is no .NET on this Mac. |
| Runtime base | `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` |
| Build base | `mcr.microsoft.com/dotnet/sdk:10.0-alpine` |
| Pin | the git SHA in the tag. A `-dirty` suffix means the image is not reproducible from any commit. |
| Size | ~397 MB |
| First build | ~5 minutes on M-series; incremental rebuilds reuse the restore layer |

`MM_IMAGE` unset still boots upstream's digest-pinned image — useful for A/B-ing against
stock, and it keeps a clean clone working before the first build. That image is stock OpenMU:
the balance updates will not appear.

`scripts/mm dotnet <args>` runs the same SDK container against the repo for compiling without
building an image, e.g. `scripts/mm dotnet build MUnique.OpenMU.sln -p:ci=true`. **`-p:ci=true`
is not optional** — without it, pre-build targets invoke `npx` and a source generator that are
not present in the SDK image, and the build fails for reasons unrelated to your code. The
Dockerfile passes the same flag.

Verified on 2026-07-28: full solution build **0 errors** (353 pre-existing upstream warnings),
image builds and runs on arm64, server boots and serves the live database.

## Rebase procedure

Operator checklist. The whole design of this fork is to make this boring: the only files that
can conflict are ones upstream does not have.

```bash
cd ~/code/mu-miami
scripts/mm backup                              # 1. always. ~15 MB, one file.
git fetch upstream
git log --oneline HEAD..upstream/master | head # 2. what am I taking?
```

**3. Rebase.**

```bash
git rebase upstream/master
```

Expected conflict surface — and nothing else:

| Path | Why it can conflict |
|---|---|
| `src/Persistence/Initialization/Updates/MuMiami/**` | ours; only if upstream adds a file at the same path, which it will not |
| `compose.mumiami.yml`, `scripts/**`, `mumiami/**` | ours; upstream has no such files |
| `CLAUDE.md`, `docs/**` (excluding upstream's own docs) | ours |
| `deploy/all-in-one/docker-compose.yml` | **not ours** — if this changed upstream, stop and read `docs/runbook.md § Compose drift` before continuing |

If a conflict appears in any *other* file under `src/`, something has gone wrong with the
fork discipline. Do not resolve it — find out how an upstream file came to be modified.

**4. Check the invariant.**

```bash
git diff --name-only upstream/master -- src/ | grep -v MuMiami    # must print nothing
```

**5. Check the update-version block** hasn't collided. Upstream tracks its own numbers in
`UpdateVersion.cs`; Mu Miami uses 9000+ in `MuMiamiUpdateVersions.cs` precisely so it never
has to touch that enum. If upstream's counter ever approaches 9000, move the Mu Miami block
up — never renumber an update that has already been applied to a database.

```bash
tail -5 src/Persistence/Initialization/Updates/UpdateVersion.cs   # upstream's highest
```

**6. Build.**

```bash
scripts/mm dotnet build MUnique.OpenMU.sln -c Release -p:ci=true   # must be 0 errors
```

**7. Dry-run the config updates on a scratch database** — never on the real one. A throwaway
pair on its own network, no volume, no published ports:

```bash
scripts/mm build
docker network create mumiami-scratch
docker run -d --name mumiami-scratch-db --network mumiami-scratch \
  -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=scratchonly -e POSTGRES_DB=openmu postgres:18
docker exec mumiami-scratch-db pg_isready -U postgres          # wait for this before the app
docker run -d --name mumiami-scratch-app --network mumiami-scratch \
  -e DB_HOST=mumiami-scratch-db -e DB_ADMIN_USER=postgres -e DB_ADMIN_PW=scratchonly \
  -e RESOLVE_IP=loopback mumiami/openmu:<tag>

# the app races initdb if you start it too early; it has no restart policy here.
# `docker start mumiami-scratch-app` again if it exits.

docker exec mumiami-scratch-db psql -U postgres -d openmu \
  -c 'DELETE FROM config."ConfigurationUpdate" WHERE "Version" >= 9000;'
# browse http://<scratch-app-container-ip>:8080/config-updates and apply
```

Then compare the scratch configuration against the live one (the query set is in
`docs/design/tuning-loop.md § 6`). They must match.

```bash
docker rm -f mumiami-scratch-app mumiami-scratch-db && docker network rm mumiami-scratch
```

**8. Deploy and smoke test.**

```bash
$EDITOR .env                       # MM_IMAGE=mumiami/openmu:<new tag>
scripts/mm restart
scripts/mm verify-balance          # all checks PASS
node scripts/simulate-progression.ts --from-db   # curve still in tolerance
```

Then log in and walk to Calle Ocho.

**9. Record it here** — fork-base SHA, date, and any new pinned digests.

## Upstream files we depend on but never edit

| Path | SHA-256 at fork base | Why we care |
|---|---|---|
| `deploy/all-in-one/docker-compose.yml` | `4a005488ec8ec4d678ab1cb545c89ce1ecfd33aaeb9cbaa657d80514b8201d85` | `compose.mumiami.yml` is layered on top of it; the merge assumes its service names (`nginx-80`, `openmu-startup`, `database`) and its `nginx/nginx.dev.conf` mount |
| `deploy/all-in-one/nginx/nginx.dev.conf` | — | proxies `/` to `http://openmu-startup:8080` and enforces basic auth from `/etc/nginx/.htpasswd` |

If a rebase changes either, re-read `docs/runbook.md § Compose drift` before running anything.

## Documented override points (used instead of editing the image)

- `DB_HOST`, `DB_ADMIN_USER`, `DB_ADMIN_PW` —
  `src/Persistence/EntityFramework/ConfigFileDatabaseConnectionStringProvider.cs`.
  These rewrite `ConnectionSettings.xml` in memory at startup. We never edit that file in
  the image.
- `-reinit` startup argument — `src/Startup/Program.cs`, drops and re-seeds the database.
- `RESOLVE_IP` env var, equivalently the `-resolveIP:{public|local|loopback|<ip>}` argument —
  `src/Network/IpAddressResolverFactory.cs`. Sets the address the connect server advertises
  to clients. We use the env var (set from `MM_RESOLVE_IP` in `.env`); it takes precedence
  over database configuration and disables runtime reconfiguration from the admin panel.
  See `docs/runbook.md § 7`.
- `-version:<gameversion>` — defaults to `season6`, which is what we want.

## Related

- Client-side tooling: OpenMU ships `MUnique.OpenMU.ClientLauncher` (.NET 10) which points a
  client at a server IP/port without hex-editing config files. Client work is briefs 000/002.
