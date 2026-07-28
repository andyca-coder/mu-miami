# Mu Miami runbook

Everything needed to go from a clean clone to a running, seeded, persistent server —
and to get out of trouble when it breaks. Verified end-to-end on 2026-07-28, Apple
Silicon + OrbStack.

Companion docs: [`UPSTREAM.md`](UPSTREAM.md) (what we're forked from and pinned to),
[`../CLAUDE.md`](../CLAUDE.md) (working agreement + gotchas).

---

## 0. Prerequisites

- **OrbStack** running, and it must be the active Docker context:
  ```bash
  docker context show      # must print: orbstack
  ```
  If it prints something else, `docker context use orbstack`. Images pull either way,
  but behavior diverges (networking, file-sharing performance, container IPs).
- `htpasswd` — ships with macOS at `/usr/sbin/htpasswd`. No install needed.
- Repo cloned to **`~/code/mu-miami`**. Not `~/Documents` — iCloud evicts files out from
  under git and corrupts operations. This was diagnosed on this machine; it is not
  theoretical.

## 1. Clean clone → running server

```bash
git clone https://github.com/andyca-coder/mu-miami.git ~/code/mu-miami
cd ~/code/mu-miami
git remote add upstream https://github.com/MUnique/OpenMU.git

cp .env.example .env
$EDITOR .env                       # set POSTGRES_PASSWORD to something random
scripts/mm htpasswd                # prompts for the admin panel password
                                   # record it in .env as MM_ADMIN_PASSWORD

scripts/mm up
scripts/mm logs                    # watch for "...initialization finished."
```

First boot takes roughly **20 seconds**: Postgres initialises, the OpenMU startup
container finds no `openmu` database, creates it, and seeds stock **Season 6 Episode 3**.
Subsequent boots take about 9 seconds and do not re-seed.

Then open **<http://localhost:8380>** and log in with `MM_ADMIN_USER` /
`MM_ADMIN_PASSWORD` from `.env` (nginx basic auth prompt).

Because the image's entrypoint contains `-autostart`, the connect servers and all three
game servers are **already started** when you get there. `Servers` in the sidebar should
show:

| Server Name | Current State |
|---|---|
| Server 0 / 1 / 2 | Started |
| Connect Server (Season 6 Episode 3 GMO Client) | Started |
| Connect Server (Season 6 Episode 3 Open Source Client) | Started |

### The compose invocation, unwrapped

`scripts/mm` exists so nobody has to type this, but this is what it runs:

```bash
docker compose --env-file .env \
  -f deploy/all-in-one/docker-compose.yml \
  -f compose.mumiami.yml \
  up -d
```

Both `-f` flags, always, in that order. `compose.mumiami.yml` alone is not a stack;
`deploy/all-in-one/docker-compose.yml` alone is upstream's config with upstream's ports,
upstream's hardcoded `admin` password, and an unpinned Postgres.

## 2. Port map (table of record)

| Service | Host port | Notes |
|---|---|---|
| Admin panel (nginx) | **8380** | remapped from upstream's 80. Change via `MM_ADMIN_PORT` in `.env` |
| Game servers | 55901–55906 | stock |
| Connect server — original/GMO client | 44405 | stock |
| Connect server — MuMain open-source client | **44406** | stock — **this is the one brief 002 uses** |
| Chat server | 55980 | stock |
| Postgres | *(none)* | internal to the compose network. No host mapping, by design |
| Admin panel direct (`openmu-startup:8080`) | *(none)* | **deliberately unpublished** — see Gotchas |

All ten host ports were confirmed free on this machine before first bind. Nothing else
on this Mac was touched. Supabase local uses 543xx, which does not overlap.

If 8380 is ever taken, walk up (8381, 8382, …), set `MM_ADMIN_PORT` in `.env`, and update
this table plus the one in `CLAUDE.md`.

**macOS firewall:** the first bind may raise "Do you want the application to accept
incoming network connections?". **Allow it.** Silently dismissing it leaves the server
invisible to the LAN, which will not show up until brief 002 tries to connect a client.

## 3. Daily commands

```bash
scripts/mm up          # start (detached)
scripts/mm down        # stop, keep all data
scripts/mm restart     # down + up
scripts/mm ps          # status
scripts/mm logs        # follow logs (all services)
scripts/mm logs openmu-startup
scripts/mm psql        # psql shell on the openmu database
scripts/mm help        # everything
```

## 4. Seeding, reinit, and exactly what it destroys

### How seeding happens

Two paths, both automatic:

1. **First boot** — `PrepareRepositoryProviderAsync` sees no `openmu` database, creates it,
   and seeds stock Season 6 Episode 3. Idempotent in the sense that it only fires when the
   database is genuinely absent; a second `up` does nothing.
2. **`-reinit`** — always drops and recreates, regardless of what is there.

The seed creates **20 test accounts** (`test0`–`test9`, `test300`, `test400`, `testgm`,
`testgm2`, `testunlock`, `quest1`–`quest3`, `socket`, …) and **76 characters**. Those are
upstream's fixtures, not ours.

### `-reinit` — measured behavior, not assumed

```bash
scripts/mm reinit      # asks you to type REINIT
```

Measured on 2026-07-28. Before → after:

| | before | after |
|---|---|---|
| `pg_database.oid` for `openmu` | 16385 | **19248** |
| Accounts | 21 (20 seeded + `miamitest`) | 20 |
| `miamitest` present | yes | **no** |
| Characters | 76 | 76 (freshly re-seeded fixtures, not the same rows) |
| A hand-edited config value (`GameConfiguration.ExperienceRate` set to 99) | 99 | **1** |

**The `openmu` database is dropped and recreated.** The changed OID proves it. There is no
partial or config-only reinit: accounts, characters, guilds, *and* every configuration edit
go together. The character count looking unchanged is a coincidence of the fixtures — those
are new rows.

Consequence for brief 002's tuning loop: **there is no scoped "reseed config only" path.**
The workable loop is *back up → reinit → restore only what you need*, or accept that the
tuning loop runs on throwaway accounts. This is the load-bearing fact 002 needs.

`scripts/mm reinit` stops the live server first, runs a throwaway container with
`dotnet MUnique.OpenMU.Startup.dll -reinit` (no `-autostart`; it only needs to touch the
database, and `compose run` publishes no ports so it cannot contend with the real server),
waits for `...initialization finished.`, removes it, and brings the stack back up.

## 5. Backup and restore

```bash
scripts/mm backup                                   # -> ~/mu-miami-backup-YYYY-MM-DD.sql
scripts/mm backup /path/to/somewhere.sql
scripts/mm restore ~/mu-miami-backup-2026-07-28.sql # asks you to type RESTORE
```

Unwrapped:

```bash
# dump
docker compose --env-file .env -f deploy/all-in-one/docker-compose.yml -f compose.mumiami.yml \
  exec -T database pg_dump -U postgres --clean --if-exists openmu > ~/mu-miami-backup-$(date +%F).sql

# restore (stop the server first so it is not writing during the swap)
docker compose --env-file .env -f deploy/all-in-one/docker-compose.yml -f compose.mumiami.yml \
  stop openmu-startup
docker compose --env-file .env -f deploy/all-in-one/docker-compose.yml -f compose.mumiami.yml \
  exec -T database psql -U postgres -d openmu < ~/mu-miami-backup-2026-07-28.sql
docker compose --env-file .env -f deploy/all-in-one/docker-compose.yml -f compose.mumiami.yml \
  start openmu-startup
```

Verified 2026-07-28: a 15 MB dump taken before a `-reinit`, restored after it, brought back
all 21 accounts including `miamitest`. `--clean --if-exists` means the restore drops and
recreates objects, so `psql` prints a wall of `NOTICE`/`ERROR: does not exist` lines on a
freshly reinitialised database. That is expected noise, not failure — check the row counts,
not the log.

Dumps are gitignored (`mu-miami-backup-*.sql`). They contain account password hashes; keep
them off shared drives.

## 6. Nuke and rebuild

The only supported path to destroying the data volume:

```bash
scripts/mm backup          # do this first, seriously
scripts/mm nuke            # asks you to type: NUKE MU MIAMI
scripts/mm up              # fresh stack, fresh stock S6E3 seed
```

`nuke` is the *only* thing in the repo that runs `docker compose down -v`. `scripts/mm down`
refuses `-v` outright and tells you to use `nuke` if you mean it. See the warning in
`CLAUDE.md`.

## 7. Advertised server address (`MM_RESOLVE_IP`)

MU's connect server does not proxy gameplay. A client connects to the connect server
(44405/44406), asks for a server, and is handed **an address to go connect to**. That
address is resolved server-side and has nothing to do with what the client dialled.

Get it wrong and the failure is misleading: the server list loads fine, then entering a
world hangs or drops. It looks like the game server is down. It isn't — the client is
dialling an address that doesn't route.

The default resolver is `Auto`, which inside Docker means "ask an external service for our
public IP". Out of the box this stack advertised `134.56.250.177` — the WAN address, which
routes from neither this Mac nor the LAN.

`MM_RESOLVE_IP` in `.env` sets it. Current value: **this Mac's LAN IP.**

### loopback vs LAN — why LAN

| Option | This Mac | LAN (friends) | Stability |
|---|---|---|---|
| `loopback` (127.127.127.127) | ✅ | ❌ never | permanent |
| **LAN IP** (e.g. `192.168.5.166`) | ✅ | ✅ | DHCP lease — can move |
| `public` | ❌ | ❌ | — |
| `local` | ❌ (resolves the container hostname) | ❌ | — |

**Chosen: the LAN IP.** `loopback` fails the LAN half outright, and LAN access is a stated
goal for later briefs — an address that works from both places today is worth more than one
that can never move. The LAN IP is reachable from this Mac too; there is no local-access
cost to choosing it.

The cost is drift: it's a DHCP lease (8 hours on this network, /22 subnet, gateway
`192.168.4.1`), so it is not guaranteed across a reboot or a long absence. Two mitigations:

1. `scripts/mm up` compares `MM_RESOLVE_IP` against `ipconfig getifaddr en0` and warns
   loudly when they disagree, with the exact fix. It's a warning, not a hard failure —
   plugging into Ethernet or using a VPN legitimately changes the answer.
2. **Set a DHCP reservation for this Mac on the router.** That removes the problem entirely
   and is worth doing before inviting anyone else onto the server.

When it does drift: edit `MM_RESOLVE_IP` in `.env`, `scripts/mm restart`, done.

### Verifying it

The connect server logs the address it advertises for every game server:

```bash
docker logs mumiami-openmu 2>&1 | grep "has registered with endpoint"
# has registered with endpoint "192.168.5.166:55901"   ... through :55906
```

Confirmed 2026-07-28: all six game servers advertise the LAN IP, and both
`192.168.5.166:44406` and `192.168.5.166:55901` accept connections.

Because the value arrives via the environment, OpenMU also locks out runtime
reconfiguration of the resolver from the admin panel — the compose file is the single
source of truth, which is what we want.

## 8. Verification script

Run this after any change to the compose layer, or after any upstream rebase:

```bash
cd ~/code/mu-miami
git remote -v                                   # origin + upstream
git log --oneline upstream/master..HEAD -- src/ # must be empty through brief 001
docker context show                             # orbstack
docker manifest inspect munique/openmu:0.9.10 | grep -A2 arm64

scripts/mm up
scripts/mm ps                                   # all three Up, no restart loops
docker volume ls | grep mumiami-pgdata
docker port mumiami-openmu | grep 8080          # must print NOTHING (auth bypass check)

curl -s -o /dev/null -w '%{http_code}\n' http://localhost:8380/            # 401
curl -s -o /dev/null -w '%{http_code}\n' -u admin:PASS http://localhost:8380/  # 200

# persistence drill
scripts/mm psql -tAc 'select count(*) from data."Account";'
scripts/mm down                                 # no -v
scripts/mm up
scripts/mm psql -tAc 'select count(*) from data."Account";'   # same number

scripts/mm backup && ls -lh ~/mu-miami-backup-*.sql           # non-empty

# advertised address matches MM_RESOLVE_IP, not a WAN address
docker logs mumiami-openmu 2>&1 | grep "has registered with endpoint" | sort -u
```

## 9. Troubleshooting

### Compose drift

If `deploy/all-in-one/docker-compose.yml` is missing or its service names changed after an
upstream rebase, **stop and report** — do not hunt-and-adapt. `compose.mumiami.yml` merges
by service name (`nginx-80`, `openmu-startup`, `database`) and by container-side mount path;
a rename silently produces a half-configured stack rather than an error.
`scripts/mm` hard-fails if the upstream file is absent. `docs/UPSTREAM.md` records the
file's SHA-256 at fork base so drift is checkable:

```bash
shasum -a 256 deploy/all-in-one/docker-compose.yml
```

### `scripts/mm up` fails with `POSTGRES_PASSWORD missing`

No `.env`. `cp .env.example .env` and fill it in. Note that compose does **not** auto-load
the repo-root `.env` for this stack (the compose project directory is `deploy/all-in-one/`,
the directory of the first `-f` file), which is why `scripts/mm` passes `--env-file`
explicitly. If you run compose by hand, you must pass it too.

### `Cannot load library libgssapi_krb5.so.2` in the openmu logs

Harmless. Npgsql probes for Kerberos/GSSAPI support at startup and the .NET runtime image
does not ship it. It appears on every boot, before `The database is getting (re-)initialized`.
Not an error condition; nothing to fix.

### `FATAL: database "openmu" does not exist` in the Postgres logs on first boot

Also expected. The OpenMU startup container races Postgres's own `initdb` and gets a couple
of connection failures before creating the database itself. It resolves within a second.
Only worry if it repeats past `...initialization finished.`

### Admin panel returns 401 forever

The `.htpasswd` and your password disagree. Regenerate: `scripts/mm htpasswd` then
`scripts/mm restart`. Update `MM_ADMIN_PASSWORD` in `.env` to match while you're there.

### Port 8380 already bound

Set `MM_ADMIN_PORT=8381` (or higher) in `.env`, `scripts/mm restart`, and update the port
tables here and in `CLAUDE.md`.
