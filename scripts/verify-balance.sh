#!/usr/bin/env bash
#
# verify-balance.sh — the Mu Miami balance acceptance toolkit.
#
#   scripts/mm verify-balance          # or: scripts/verify-balance.sh
#
# Every check queries the LIVE database, never the source. That is the point: the plug-in
# source says what was intended, this says what the server is actually running.
#
# The worst-context drop measurement (check 3) is not a one-off. Any future change that adds
# or raises a drop group — brief 003 hot zones, seasonal events, anything — must re-run it.
# The rule, from docs/design/balance-canon.md:
#
#   Every monster context must stay under 1.00 with explicit margin. At 1.00 the engine
#   normalises the roulette and something drops from EVERY kill, which turns loot into noise.
#   The Mu Miami working budget is 0.95. If a new group would push a context past it, scope
#   the group away from the hot contexts (a map or monster-level window) instead of raising
#   the budget.
#
# Exit code is 0 only if every check passes.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

BUDGET=0.95
FAILURES=0

bold()  { printf '\n\033[1m%s\033[0m\n' "$*"; }
pass()  { printf '  \033[32mPASS\033[0m  %s\n' "$*"; }
fail()  { printf '  \033[31mFAIL\033[0m  %s\n' "$*"; FAILURES=$((FAILURES + 1)); }
note()  { printf '        %s\n' "$*"; }

q() { scripts/mm psql -tAc "$1" 2>/dev/null | tr -d '\r'; }

# --- 1. The experience curve ----------------------------------------------------------
bold "1. Experience curve"
live_curve="$(q 'SELECT "ExperienceFormula" FROM config."GameConfiguration" LIMIT 1;')"
shipped_curve="$(sed -n 's/.*const string ExperienceFormula = "\(.*\)";/\1/p' \
  src/Persistence/Initialization/Updates/MuMiami/MuMiamiExperienceCurveUpdatePlugIn.cs)"

if [ "$live_curve" = "$shipped_curve" ]; then
  pass "live curve is byte-identical to MuMiamiExperienceCurveUpdatePlugIn"
else
  fail "live curve differs from the shipped plug-in"
  note "live:    $live_curve"
  note "shipped: $shipped_curve"
fi

# Rates must stay at 1.0 — canon says the curve is the mechanism, not the multipliers.
rates="$(q "SELECT \"ExperienceRate\" || ' / ' || COALESCE(\"MasterExperienceRate\"::text, 'null') FROM config.\"GameConfiguration\" LIMIT 1;")"
maps_not_one="$(q 'SELECT count(*) FROM config."GameMapDefinition" WHERE "ExpMultiplier" <> 1;')"
if [ "$maps_not_one" = "0" ]; then
  pass "every map ExpMultiplier is still 1 (rate multipliers untouched; global rate: $rates)"
else
  fail "$maps_not_one map(s) have ExpMultiplier <> 1 — brief 003 territory, not 002"
fi

# --- 2. Drop chances ------------------------------------------------------------------
bold "2. Drop chances"
chance_of() { q "SELECT \"Chance\" FROM config.\"DropItemGroup\" WHERE \"Id\" = '$1';"; }
excellent="$(chance_of 00000200-0003-0000-0000-000000000000)"
jewel="$(chance_of 00000200-0004-0000-0000-000000000000)"
money="$(chance_of 00000200-0001-0000-0000-000000000000)"
random_item="$(chance_of 00000200-0002-0000-0000-000000000000)"

[ "$excellent" = "0.03" ]  && pass "excellent group  = 0.03  (stock 0.0001)" || fail "excellent group is $excellent, expected 0.03"
[ "$jewel" = "0.003" ]     && pass "jewel group      = 0.003 (stock 0.001, exactly 3.0x)" || fail "jewel group is $jewel, expected 0.003"
[ "$money" = "0.5" ]       && pass "money group      = 0.5   (stock, untouched)" || fail "money group is $money, expected the stock 0.5"
[ "$random_item" = "0.3" ] && pass "random-item group = 0.3   (stock, untouched)" || fail "random-item group is $random_item, expected the stock 0.3"

# --- 3. Worst merged drop context -----------------------------------------------------
#
# This reproduces DefaultDropGenerator's per-kill roulette exactly:
#   * map groups and character/quest groups are filtered by IsGroupRelevant
#     (MinimumMonsterLevel / MaximumMonsterLevel / Monster), monster-owned groups are not;
#   * groups with Chance >= 1.0 go to the GUARANTEED list and do not compete in the
#     roulette, so they are excluded from the sum;
#   * everything else is summed. Past 1.0 the draw normalises and something always drops.
bold "3. Worst merged drop context (budget $BUDGET, engine cliff 1.00)"
read -r worst_sum worst_where <<EOF
$(scripts/mm psql -tAc "
WITH lvl AS (
  SELECT ma.\"MonsterDefinitionId\" AS mid, ma.\"Value\"::numeric AS level
  FROM config.\"MonsterAttribute\" ma
  JOIN config.\"AttributeDefinition\" ad ON ad.\"Id\" = ma.\"AttributeDefinitionId\"
  WHERE ad.\"Designation\" = 'Level'
),
spawned AS (
  SELECT DISTINCT sa.\"GameMapId\" AS map_id, sa.\"MonsterDefinitionId\" AS mid
  FROM config.\"MonsterSpawnArea\" sa
),
map_part AS (
  SELECT s.map_id, s.mid, SUM(g.\"Chance\") AS chance
  FROM spawned s
  JOIN lvl l ON l.mid = s.mid
  JOIN config.\"GameMapDefinitionDropItemGroup\" mg ON mg.\"GameMapDefinitionId\" = s.map_id
  JOIN config.\"DropItemGroup\" g ON g.\"Id\" = mg.\"DropItemGroupId\"
  WHERE g.\"Chance\" < 1.0
    AND (g.\"MinimumMonsterLevel\" IS NULL OR l.level >= g.\"MinimumMonsterLevel\")
    AND (g.\"MaximumMonsterLevel\" IS NULL OR l.level <= g.\"MaximumMonsterLevel\")
    AND (g.\"MonsterId\" IS NULL OR g.\"MonsterId\" = s.mid)
  GROUP BY s.map_id, s.mid
),
mon_part AS (
  SELECT mdg.\"MonsterDefinitionId\" AS mid, SUM(g.\"Chance\") AS chance
  FROM config.\"MonsterDefinitionDropItemGroup\" mdg
  JOIN config.\"DropItemGroup\" g ON g.\"Id\" = mdg.\"DropItemGroupId\"
  WHERE g.\"Chance\" < 1.0
  GROUP BY mdg.\"MonsterDefinitionId\"
)
SELECT round((COALESCE(p.chance,0) + COALESCE(mo.chance,0))::numeric, 4) || ' '
       || m.\"Name\" || ' / ' || md.\"Designation\" || ' (level ' || l.level::int || ')'
FROM spawned s
JOIN config.\"GameMapDefinition\" m ON m.\"Id\" = s.map_id
JOIN config.\"MonsterDefinition\" md ON md.\"Id\" = s.mid
JOIN lvl l ON l.mid = s.mid
LEFT JOIN map_part p ON p.map_id = s.map_id AND p.mid = s.mid
LEFT JOIN mon_part mo ON mo.mid = s.mid
ORDER BY (COALESCE(p.chance,0) + COALESCE(mo.chance,0)) DESC
LIMIT 1;" 2>/dev/null)
EOF

if [ -z "${worst_sum:-}" ]; then
  fail "could not measure the worst context (is the stack up?)"
else
  over_cliff="$(awk -v v="$worst_sum" 'BEGIN{print (v >= 1.0) ? 1 : 0}')"
  over_budget="$(awk -v v="$worst_sum" -v b="$BUDGET" 'BEGIN{print (v > b) ? 1 : 0}')"
  if [ "$over_cliff" = "1" ]; then
    fail "worst context $worst_sum — AT OR PAST THE ENGINE CLIFF. Something drops from every kill."
    note "$worst_where"
  elif [ "$over_budget" = "1" ]; then
    fail "worst context $worst_sum — over the $BUDGET budget"
    note "$worst_where"
    note "Scope the offending group to fewer maps or a monster-level window; do not raise the budget."
  else
    pass "worst context $worst_sum <= $BUDGET"
    note "$worst_where"
  fi
fi

# --- 4. Ancient group scoping ---------------------------------------------------------
bold "4. Ancient drop group scoping"
ancient_id='00000200-0384-0000-0000-000000000000'
ancient_maps="$(q "SELECT count(*) FROM config.\"GameMapDefinitionDropItemGroup\" WHERE \"DropItemGroupId\" = '$ancient_id';")"
ancient_names="$(q "SELECT string_agg(m.\"Name\", ', ' ORDER BY m.\"Name\") FROM config.\"GameMapDefinitionDropItemGroup\" mg JOIN config.\"GameMapDefinition\" m ON m.\"Id\" = mg.\"GameMapDefinitionId\" WHERE mg.\"DropItemGroupId\" = '$ancient_id';")"
ancient_chance="$(q "SELECT \"Chance\" FROM config.\"DropItemGroup\" WHERE \"Id\" = '$ancient_id';")"
ancient_monsters="$(q "SELECT count(*) FROM config.\"MonsterDefinitionDropItemGroup\" WHERE \"DropItemGroupId\" = '$ancient_id';")"

if [ "$ancient_maps" = "9" ] && [ "$ancient_monsters" = "0" ]; then
  pass "ancient group (chance $ancient_chance) on exactly 9 map contexts, 0 monster contexts"
  note "$ancient_names"
else
  fail "ancient group is on $ancient_maps map contexts and $ancient_monsters monster contexts, expected 9 and 0"
  note "${ancient_names:-<none>}"
fi

# Nothing outside Kalima / Aida / Icarus may carry an Ancient-type group on a field map.
stray="$(q "SELECT count(*) FROM config.\"GameMapDefinitionDropItemGroup\" mg JOIN config.\"DropItemGroup\" g ON g.\"Id\" = mg.\"DropItemGroupId\" JOIN config.\"GameMapDefinition\" m ON m.\"Id\" = mg.\"GameMapDefinitionId\" WHERE g.\"ItemType\" = 1 AND m.\"Name\" NOT LIKE 'Kalima%' AND m.\"Name\" NOT IN ('Aida','Icarus');")"
[ "$stray" = "0" ] && pass "no ancient-type drop group on any other map" || fail "$stray ancient-type group attachment(s) on maps outside Kalima/Aida/Icarus"

# --- 5. Chaos Machine ------------------------------------------------------------------
#
# Canon's scarcity anchor. These are ItemCrafting rows, NOT drop groups, which is why
# nothing in brief 002 could have touched them by accident — but "should not have" is not
# evidence, so we check.
bold "5. Chaos Machine +13/+15 odds (must be byte-identical to upstream)"
chaos="$(scripts/mm psql -tAc "
SELECT rpad(c.\"Name\", 22) || ' base ' || lpad(s.\"SuccessPercent\"::text, 3)
       || '%, max ' || lpad(s.\"MaximumSuccessPercent\"::text, 3)
       || '%, luck +' || s.\"SuccessPercentageAdditionForLuck\"::text || '%'
FROM config.\"ItemCrafting\" c
JOIN config.\"SimpleCraftingSettings\" s ON s.\"Id\" = c.\"SimpleCraftingSettingsId\"
WHERE c.\"Name\" LIKE '+1_ Item Combination'
ORDER BY c.\"Number\";" 2>/dev/null)"

chaos_rows="$(printf '%s' "$chaos" | grep -c . || true)"
if [ "$chaos_rows" = "6" ]; then
  pass "all six +10..+15 Item Combination craftings present"
  printf '%s\n' "$chaos" | sed 's/^/        /'
else
  fail "expected 6 (+10..+15) Item Combination craftings, found $chaos_rows"
  printf '%s\n' "$chaos" | sed 's/^/        /'
fi

# The real assertion: nothing Mu Miami ships can reach the crafting tables at all.
hits="$(grep -rl "ItemCrafting\|SimpleCraftingSettings\|SuccessPercent" src/Persistence/Initialization/Updates/MuMiami/ 2>/dev/null | wc -l | tr -d ' ')"
if [ "$hits" = "0" ]; then
  pass "no MuMiami* file references ItemCrafting / SimpleCraftingSettings / SuccessPercent"
else
  fail "$hits MuMiami* file(s) reference crafting settings — canon forbids touching Chaos Machine odds"
  grep -rl "ItemCrafting\|SimpleCraftingSettings\|SuccessPercent" src/Persistence/Initialization/Updates/MuMiami/ | sed 's/^/        /'
fi

# And that the file that seeds those odds is untouched in this fork. The +10..+15 success
# percentages are seeded in VersionSeasonSix/ChaosMixes.cs (ItemLevelUpgrade), nowhere else.
chaos_src="src/Persistence/Initialization/VersionSeasonSix/ChaosMixes.cs"
if git diff --quiet upstream/master -- "$chaos_src" 2>/dev/null; then
  pass "$chaos_src is byte-identical to upstream/master"
else
  fail "$chaos_src differs from upstream/master (or upstream/master is not fetched)"
fi

# --- 5b. Fork invariant ----------------------------------------------------------------
#
# Mu Miami ADDS files under src/ and modifies none. `git diff` only sees tracked changes,
# so the additions are listed separately (tracked + untracked) — otherwise an empty result
# here could mean "clean" or "nothing committed yet", and those are very different.
bold "5b. Fork invariant under src/"
foreign="$(git diff --name-only upstream/master -- src/ 2>/dev/null | grep -v 'MuMiami' || true)"
if [ -z "$foreign" ]; then
  pass "no upstream file under src/ is modified by this fork"
else
  fail "this fork modifies upstream files under src/:"
  printf '%s\n' "$foreign" | sed 's/^/        /'
fi

added_tracked="$(git diff --name-only --diff-filter=A upstream/master -- src/ 2>/dev/null || true)"
added_untracked="$(git ls-files --others --exclude-standard -- src/ 2>/dev/null || true)"
added="$(printf '%s\n%s\n' "$added_tracked" "$added_untracked" | grep . || true)"
non_mumiami="$(printf '%s\n' "$added" | grep . | grep -v 'MuMiami' || true)"
added_count="$(printf '%s\n' "$added" | grep -c . || true)"

if [ -z "$non_mumiami" ] && [ "$added_count" -gt 0 ]; then
  pass "$added_count added file(s) under src/, all named MuMiami*"
  printf '%s\n' "$added" | sed 's/^/        /'
elif [ -n "$non_mumiami" ]; then
  fail "files added under src/ that are not named MuMiami*:"
  printf '%s\n' "$non_mumiami" | sed 's/^/        /'
else
  fail "no MuMiami files found under src/ — is this the right working tree?"
fi

uncommitted="$(git ls-files --others --exclude-standard -- src/ 2>/dev/null | grep -c . || true)"
if [ "$uncommitted" != "0" ]; then
  note "$uncommitted of them are UNCOMMITTED. The running image tag will say -dirty and"
  note "will not be reproducible from any commit until you commit and rebuild."
fi

# --- 6. Farming clusters ---------------------------------------------------------------
bold "6. Farming clusters"
clusters="$(scripts/mm psql -tAc "
SELECT m.\"Name\" || ' | ' || md.\"Designation\" || ' (lvl ' || (SELECT ma.\"Value\"::int FROM config.\"MonsterAttribute\" ma JOIN config.\"AttributeDefinition\" ad ON ad.\"Id\" = ma.\"AttributeDefinitionId\" WHERE ma.\"MonsterDefinitionId\" = md.\"Id\" AND ad.\"Designation\" = 'Level') || ') | x' || sa.\"X1\" || '-' || sa.\"X2\" || ' y' || sa.\"Y1\" || '-' || sa.\"Y2\" || ' | qty ' || sa.\"Quantity\"
FROM config.\"MonsterSpawnArea\" sa
JOIN config.\"GameMapDefinition\" m ON m.\"Id\" = sa.\"GameMapId\"
JOIN config.\"MonsterDefinition\" md ON md.\"Id\" = sa.\"MonsterDefinitionId\"
WHERE sa.\"X1\" <> sa.\"X2\" AND m.\"Number\" IN (1, 8, 37)
ORDER BY m.\"Number\", md.\"Designation\";" 2>/dev/null)"
cluster_count="$(printf '%s' "$clusters" | grep -c . || true)"
cluster_total="$(q 'SELECT COALESCE(SUM("Quantity"),0) FROM config."MonsterSpawnArea" sa JOIN config."GameMapDefinition" m ON m."Id" = sa."GameMapId" WHERE sa."X1" <> sa."X2" AND m."Number" IN (1, 8, 37);')"
if [ "$cluster_count" = "7" ] && [ "$cluster_total" = "100" ]; then
  pass "7 cluster spawn areas across 3 maps, 100 monsters total"
else
  fail "found $cluster_count cluster spawn areas totalling $cluster_total monsters, expected 7 and 100"
fi
printf '%s\n' "$clusters" | sed 's/^/        /'

# --- 7. Accounts survived ---------------------------------------------------------------
bold "7. Accounts"
accounts="$(q 'SELECT count(*) FROM data."Account";')"
characters="$(q 'SELECT count(*) FROM data."Character";')"
note "$accounts accounts, $characters characters"
note "Compare against the count you recorded before applying. Configuration updates never"
note "touch data.* — if this dropped, something else did it and you want the 001 backup."

# --- 8. Which Mu Miami updates are installed -------------------------------------------
bold "8. Installed Mu Miami configuration updates"
scripts/mm psql -c 'SELECT "Version", "Name", "InstalledAt" FROM config."ConfigurationUpdate" WHERE "Version" >= 9000 ORDER BY "Version";' 2>/dev/null | sed 's/^/  /'
installed="$(q 'SELECT count(*) FROM config."ConfigurationUpdate" WHERE "Version" >= 9000 AND "InstalledAt" IS NOT NULL;')"
[ "$installed" = "4" ] && pass "all 4 Mu Miami updates installed" || fail "$installed of 4 Mu Miami updates installed"

# --- Result -----------------------------------------------------------------------------
if [ "$FAILURES" -eq 0 ]; then
  printf '\n\033[32mAll balance checks passed.\033[0m\n'
  exit 0
fi
printf '\n\033[31m%s check(s) failed.\033[0m\n' "$FAILURES"
exit 1
