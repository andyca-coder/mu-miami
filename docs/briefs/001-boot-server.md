# Brief 001: Boot Mu Miami — server live, seeded, persistent

| | |
|---|---|
| **Status** | approved |
| **Depends on** | none |
| **Parallel-safe with** | 000 (Andy's client spike), recon pass — disjoint files |
| **Owns files** | `CLAUDE.md`, `docs/**` (except `docs/recon/**`), `compose.mumiami.yml`, `.env.example`, `.gitignore` |
| **Risk level** | low (all state lives in one named Docker volume; destroy-and-reseed is a supported path) |
| **Executor** | Claude Code |

## Objective

A forked OpenMU server runs natively on Apple Silicon via OrbStack, seeded with stock Season 6 Episode 3 configuration. Admin panel reachable, connect + game servers started, and a created account survives a full stack restart. Reproducible from clean clone via a written runbook. **No client steps in this brief** — the client is Andy's parallel spike (000); the merge happens at the top of 002.

## Verified context (researched 2026-07-27 — do not re-litigate, do verify at execution)

- **`munique/openmu` on Docker Hub publishes native `linux/arm64`** — latest tag and `v0.9.10` both, pushed 2026-07-26. The project is actively maintained. **Pin `0.9.10` by digest** — never `latest` — and record the digest in `docs/UPSTREAM.md`. Re-run the arch check at execution time (`docker manifest inspect`) to confirm; if reality disagrees with this brief, that's a stop-and-escalate, not an improvise.
- **Upstream compose of record: `deploy/all-in-one/docker-compose.yml`.** Shape: `nginx:alpine` fronting the admin panel on **host port 80**; `openmu-startup` (all-in-one) exposing game ports **55901–55906**, connect **44405** (original client) + **44406** (MuMain open-source client), chat **55980**; `postgres` with **no host port mapping** (internal-only — zero collision risk with Supabase local). Named volume `dbdata` exists upstream but gets a Mu Miami-named override.
- Upstream compose has **hardcoded Postgres credentials (`admin`)** and mounts an `.htpasswd` for the admin panel. Both get overridden — creds to `.env`, and generate a fresh `.htpasswd`.
- `DB_HOST` / `DB_ADMIN_USER` / `DB_ADMIN_PW` env vars are the supported way to override connection settings in-container. Use them; don't edit `ConnectionSettings.xml` in the image.
- **`-reinit` startup parameter reinitializes the database.** This is the seed-reset primitive brief 002's tuning loop will be built on — confirm it works during this brief (it's cheap to test now, load-bearing later).
- OpenMU ships a **ClientLauncher** (`MUnique.OpenMU.ClientLauncher`, .NET 10) that points a client at a server IP/port — no config-file hex-editing. Client-side detail lives in 000/002, but the runbook should link it.
- Fork base: record the exact upstream `master` SHA at fork time in `docs/UPSTream.md`.

## Fork discipline (hard constraint)

- **Zero modifications under `src/` in this brief.** All customization is additive: compose override, `mumiami/` dir, docs. Brief 002 will touch `src/Persistence/Initialization/**` in isolated, marked files with a rebase procedure it establishes. 001 staying clean makes the first upstream rebase a non-event.
- `git remote add upstream https://github.com/MUnique/OpenMU.git` at clone time.
- Repo lives at **`~/code/mu-miami`** — never `~/Documents` (iCloud eviction corrupts git operations; previously diagnosed on this machine).

## Out of Scope

- **Any balance change.** Stock Season 6 rates when this brief closes. This is the #1 scope-creep temptation — resist.
- Client steps of any kind (000 owns them)
- Miami theming/branding; Tailscale friend-access (002+); public exposure of any port; CI/CD; backup automation beyond one manual pg_dump pair

## Implementation Plan

1. **Fork → clone → remotes.** Fork `MUnique/OpenMU` → `andyca-coder/mu-miami`, clone to `~/code/mu-miami`, add `upstream` remote. Write `docs/UPSTREAM.md`: fork-base SHA, image tag + digest, date.
2. **Verify arch + pin.** `docker manifest inspect munique/openmu:0.9.10` → confirm `linux/arm64` present → record digest. OrbStack as runtime (confirm `docker context` points at it).
3. **`compose.mumiami.yml`** — an override layered on `deploy/all-in-one/docker-compose.yml`, never an edit of it:
   - Volume: replace upstream `dbdata` mapping with named volume **`mumiami-pgdata`**.
   - Ports: admin panel **80 → 8380** (freeing 80 — this Mac runs dev servers; a root-privileged port for a hobby admin panel is silly), game/connect/chat ports kept stock (55901–55906, 44405–44406, 55980 — no known collisions; Supabase local uses 543xx/54321).
   - Postgres: creds from `.env` via `POSTGRES_PASSWORD` + matching `DB_ADMIN_PW` on the startup service. Restart policy `unless-stopped`.
   - Generate `.htpasswd` (`htpasswd -B`) for the admin panel; file is gitignored, procedure documented.
4. **Boot + seed.** `docker compose -f deploy/all-in-one/docker-compose.yml -f compose.mumiami.yml up -d`. Complete the setup/initialization flow. Admin panel (http://localhost:8380): start connect servers + at least one game server, confirm running state.
5. **Persistence drill.** Create account `miamitest` via admin panel → `down` (NO `-v`) → `up -d` → account still exists. Verified, not assumed.
6. **`-reinit` drill.** Run the documented reinit path once; confirm it rebuilds config and what it destroys (accounts? characters? — record the answer precisely; 002's tuning loop depends on knowing exactly this).
7. **Backup pair.** One `pg_dump` command + its restore counterpart in the runbook; run the dump once, confirm non-empty.
8. **`CLAUDE.md`** at root: what this is, fork/rebase discipline, the compose invocation (alias it: `scripts/mm` wrapper for up/down/logs/reinit), port table, `-v`-flag warning, Gotchas seeded from real findings.
9. **`docs/runbook.md`**: clean-clone-to-running, port table, arch/digest record, seed + reinit + backup/restore procedures, nuke-and-rebuild path.

## Port map (runbook table of record)

| Service | Host port | Notes |
|---|---|---|
| Admin panel (nginx) | **8380** | remapped from 80 |
| Game servers | 55901–55906 | stock |
| Connect (original client) | 44405 | stock |
| Connect (MuMain) | **44406** | stock — this is the one 002 uses |
| Chat | 55980 | stock |
| Postgres | — | internal-only, no host mapping |

## Acceptance Criteria

- [ ] `~/code/mu-miami` forked, `upstream` remote set, `git log -- src/` shows zero commits from this brief
- [ ] `docs/UPSTREAM.md` records fork-base SHA + `munique/openmu:0.9.10` digest, arm64 confirmed at execution time
- [ ] Single documented compose invocation brings the stack up clean; all services healthy; no restart loops in logs
- [ ] Admin panel loads on **:8380**; connect servers + ≥1 game server show started
- [ ] `docker volume ls` shows `mumiami-pgdata`
- [ ] `miamitest` account survives full `down`/`up` cycle
- [ ] `-reinit` behavior tested once; exactly what it destroys is documented
- [ ] Backup dump produced non-empty; restore procedure written
- [ ] `CLAUDE.md` + `docs/runbook.md` exist; Gotchas populated with real findings, not placeholders
- [ ] `.env.example` complete with comments; no credential or `.htpasswd` in any commit
- [ ] No other project's containers/ports/services touched

## Edge Cases

- **`down -v` muscle memory** destroys `mumiami-pgdata`. Runbook + CLAUDE.md flag it as the most dangerous command in the repo; the `scripts/mm` wrapper must not expose a casual path to it.
- **Port 80 already bound** on the host by something else → we remapped to 8380 preemptively; if *8380* collides, walk up (8381…), update the table.
- **Seed run twice** — determine idempotent vs destructive during step 6, don't guess.
- **macOS firewall prompt** on first port bind: allow it, note it — silently dismissed = server invisible to the LAN (bites in 002).
- **OrbStack not the active docker context** → images pull but behavior diverges; check `docker context show` first.
- **Compose file drift** — upstream may reorganize `deploy/`; if the all-in-one file isn't where this brief says, stop and report, don't hunt-and-adapt silently.

## Verification Script

```bash
cd ~/code/mu-miami
git remote -v                                  # origin + upstream
git log --oneline -- src/ | head               # zero commits from this brief
docker context show                            # orbstack
docker manifest inspect munique/openmu:0.9.10 | grep -A2 arm64   # arm64 present

docker compose -f deploy/all-in-one/docker-compose.yml -f compose.mumiami.yml up -d
docker compose -f deploy/all-in-one/docker-compose.yml -f compose.mumiami.yml ps
docker volume ls | grep mumiami-pgdata

# browser: http://localhost:8380 → login via .htpasswd → start servers
# create account miamitest
docker compose -f deploy/all-in-one/docker-compose.yml -f compose.mumiami.yml down
docker compose -f deploy/all-in-one/docker-compose.yml -f compose.mumiami.yml up -d
# confirm miamitest persists

docker compose -f deploy/all-in-one/docker-compose.yml -f compose.mumiami.yml exec -T database \
  pg_dump -U postgres openmu > ~/mu-miami-backup-$(date +%F).sql
ls -lh ~/mu-miami-backup-*.sql                 # non-empty
```

## Stop-and-Escalate

- Any `src/` file would need modification
- arm64 absent from the pinned tag at execution time (contradicts research — investigate, don't emulate silently)
- Compose layout differs from `deploy/all-in-one/docker-compose.yml`
- Seeding fails or produces non-S6E3 config
- Any port remap would require touching another project's services
- `-reinit` destroys more than expected and no scoped alternative exists → report findings; 002's design needs the truth

## Handback

- **Result:** {{done | done-with-deviations | blocked}}
- **Escalations raised & how resolved:** {{...}}
- **Deviations from plan and why:** {{...}}
- **QA reviewer verdict:** {{...}}
- **Adjacent issues noticed (not fixed):** {{...}}
- **Suggested follow-up briefs:** {{...}}
