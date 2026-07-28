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
`git log --oneline upstream/master..HEAD -- src/` should be **empty** through brief 001;
brief 002 is the first one allowed to touch `src/`, and it establishes the rebase procedure.

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
