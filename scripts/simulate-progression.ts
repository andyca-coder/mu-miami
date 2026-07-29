#!/usr/bin/env node
/**
 * simulate-progression.ts — Mu Miami's acceptance instrument for the XP curve.
 *
 * Answers one question: given a level -> required-XP expression, how many hours does a
 * solo character need to reach each phase boundary in docs/design/balance-canon.md?
 *
 * Run from the repo root:
 *
 *     node scripts/simulate-progression.ts                 # the shipped Mu Miami curve
 *     node scripts/simulate-progression.ts --vanilla       # stock S6E3, for comparison
 *     node scripts/simulate-progression.ts --from-db       # the curve the live server is running
 *     node scripts/simulate-progression.ts --expression "if(level == 0, 0, ...)"
 *     node scripts/simulate-progression.ts --detail        # per-farming-band breakdown
 *     node scripts/simulate-progression.ts --spot-check 320  # xp/kill + time-per-level at one level
 *
 * Nothing here is guessed:
 *
 *  - The XP-per-kill maths is a transcription of the engine
 *    (src/GameLogic/AttackableExtensions.cs CalculateBaseExperience + Player.CalculateExpAfterKill).
 *  - Monster levels are parsed out of the actual initializer sources at run time
 *    (src/Persistence/Initialization/**\/Maps/*.cs), never typed in here.
 *  - The curve expression is read out of the shipped update plugin (or the live DB),
 *    and evaluated by a small mXparser-subset evaluator, so this validates the exact
 *    string the server will run.
 *
 * The one thing that IS an assumption is the farming plan below: which monster a
 * character of a given level is standing in front of, and how fast they kill it.
 * Those numbers are stated inline, with reasoning, and are the knob to turn if
 * real play disagrees with the model.
 */

import { execFileSync } from 'node:child_process';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';

// ---------------------------------------------------------------------------
// Engine constants — all read from, or verified against, upstream source.
// ---------------------------------------------------------------------------

/** GameConfiguration.MaximumLevel, GameConfigurationInitializerBase.cs:42. */
const MAXIMUM_LEVEL = 400;

/**
 * The multipliers applied to base XP in Player.CalculateExpAfterKill (Player.cs:1238-1274).
 *
 * GameConfiguration.ExperienceRate = 1.0 (GameConfigurationInitializerBase.cs:40)
 * GameServerDefinition.ExperienceRate = 1.0 (DataInitializationBase.cs:253)
 * Stats.ExperienceRate = 1, Stats.BonusExperienceRate = 0 (CharacterClassInitialization.cs:164)
 * GameMapDefinition.ExpMultiplier = 1 for every map (BaseMapInitializer.cs:119)
 *
 * Mu Miami leaves every one of these at 1.0 on purpose — balance-canon.md says the curve
 * is the mechanism, not the rate multipliers. Hot-zone x1.5 (brief 003) is deliberately
 * NOT modelled: it is a bonus on top of the pace this simulator reports.
 */
const GAME_RATE = 1.0;
const PLAYER_RATE = 1.0;
const MAP_EXP_MULTIPLIER = 1.0;

/**
 * Stats.RandomExperienceMinMultiplier / MaxMultiplier are seeded 0.8 / 1.2
 * (GameConfigurationInitializerBase.cs:278+). Rand.NextInt(min, max) is uniform, so over
 * a level's worth of kills the expected multiplier is exactly the midpoint, 1.0.
 * Modelling the variance would change the noise, not the hours.
 */
const RANDOM_EXP_MEAN = 1.0;

/** The stock Season 6 curve, GameConfigurationInitializerBase.cs:65. */
const VANILLA_EXPERIENCE_FORMULA =
  'if(level == 0, 0, if(level < 256, 10 * (level + 8) * (level - 1) * (level - 1), (10 * (level + 8) * (level - 1) * (level - 1)) + (1000 * (level - 247) * (level - 256) * (level - 256))))';

/** Where the shipped Mu Miami curve lives. Single source of truth with the server. */
const CURVE_PLUGIN_PATH = 'src/Persistence/Initialization/Updates/MuMiami/MuMiamiExperienceCurveUpdatePlugIn.cs';

// ---------------------------------------------------------------------------
// The phases from docs/design/balance-canon.md, with the brief's tolerances.
// ---------------------------------------------------------------------------

interface Phase {
  name: string;
  from: number;
  to: number;
  targetHours: number;
  min: number;
  max: number;
}

const PHASES: Phase[] = [
  { name: 'Ignition', from: 1, to: 150, targetHours: 4, min: 3, max: 5 },
  { name: 'The climb', from: 151, to: 300, targetHours: 10, min: 8, max: 12 },
  { name: 'The grind', from: 301, to: 380, targetHours: 13, min: 11, max: 15 },
  { name: 'The summit', from: 381, to: 400, targetHours: 8, min: 7, max: 10 },
];

const TOTAL_TOLERANCE = { min: 30, max: 40 };

// ---------------------------------------------------------------------------
// The farming plan — THE assumption set. Everything else is derived.
// ---------------------------------------------------------------------------

/**
 * One row per stretch of character levels: which monster the character is farming and how
 * fast they kill it. `monster` and `map` are looked up in the parsed initializer sources,
 * so the monster's LEVEL (the only input to the XP formula) is never typed in here.
 *
 * How the monsters were chosen: the map a character of that level would actually be on in
 * Season 6 progression, and the top monster on it they can hold a rotation against. Past
 * character level ~150 monster levels stop growing (the game's highest normal-map monster
 * is Dark Iron Knight, level 148), so the plan saturates there and the engine's over-level
 * penalty — CalculateBaseExperience multiplies by (targetLevel + 10) / killerLevel once
 * killerLevel > targetLevel + 10 — does the rest. That penalty is the reason the late
 * phases are slow, and it is modelled exactly.
 *
 * killsPerMinute: a solo character at ordinary pace, including walking between packs and
 * respawn waits. It falls as monster HP outgrows the character's damage curve, then
 * recovers slightly at high level when AoE skills come online but monsters are 200k+ HP.
 * These are the numbers to argue with after a real play session; nothing else in this
 * file is opinion.
 */
interface FarmingBand {
  upToLevel: number;
  map: string;
  monster: string;
  killsPerMinute: number;
  note: string;
}

const FARMING_PLAN: FarmingBand[] = [
  { upToLevel: 10, map: 'Lorencia', monster: 'Spider', killsPerMinute: 30, note: 'first minutes; one-shot territory' },
  { upToLevel: 20, map: 'Lorencia', monster: 'Bull Fighter', killsPerMinute: 28, note: 'Lorencia field' },
  { upToLevel: 35, map: 'Noria', monster: 'Stone Golem', killsPerMinute: 24, note: 'Noria / Elvenland' },
  { upToLevel: 55, map: 'Dungeon', monster: 'Dark Knight', killsPerMinute: 20, note: 'THE LOW CLUSTER — Brickell After Dark' },
  { upToLevel: 75, map: 'LostTower', monster: 'Balrog', killsPerMinute: 18, note: 'Lost Tower' },
  { upToLevel: 110, map: 'Atlans', monster: 'Hydra', killsPerMinute: 18, note: 'Atlans' },
  { upToLevel: 280, map: 'Tarkan', monster: 'Death Beam Knight', killsPerMinute: 15, note: 'THE MID CLUSTER — Calle Ocho' },
  { upToLevel: 400, map: 'KanturuRuins', monster: 'Genocider Warrior', killsPerMinute: 10, note: 'THE HIGH CLUSTER — Wynwood; 218k HP, hence the slower pace' },
];

// ---------------------------------------------------------------------------
// mXparser-subset expression evaluator.
// ---------------------------------------------------------------------------

/**
 * Evaluates the subset of mXparser that OpenMU's experience formulas use: numeric
 * literals, the `level` argument, + - * / ^, parentheses, the comparison operators, and
 * `if(condition, then, else)` (condition is true when non-zero, matching mXparser).
 *
 * Written out rather than pulled from npm on purpose: this script must run from a clean
 * clone with nothing installed.
 */
function makeEvaluator(expression: string): (level: number) => number {
  let pos = 0;
  const src = expression;

  const skipWs = (): void => {
    while (pos < src.length && /\s/.test(src[pos]!)) pos++;
  };

  const expect = (token: string): void => {
    skipWs();
    if (!src.startsWith(token, pos)) {
      throw new Error(`expected "${token}" at offset ${pos} of: ${src}`);
    }
    pos += token.length;
  };

  type Node = (level: number) => number;

  const parseComparison = (): Node => {
    const left = parseAdditive();
    skipWs();
    for (const op of ['<=', '>=', '==', '!=', '<', '>']) {
      if (src.startsWith(op, pos)) {
        pos += op.length;
        const right = parseAdditive();
        return (level) => {
          const a = left(level);
          const b = right(level);
          switch (op) {
            case '<=': return a <= b ? 1 : 0;
            case '>=': return a >= b ? 1 : 0;
            case '==': return a === b ? 1 : 0;
            case '!=': return a !== b ? 1 : 0;
            case '<': return a < b ? 1 : 0;
            default: return a > b ? 1 : 0;
          }
        };
      }
    }
    return left;
  };

  function parseAdditive(): Node {
    let node = parseMultiplicative();
    for (;;) {
      skipWs();
      const op = src[pos];
      if (op !== '+' && op !== '-') return node;
      pos++;
      const right = parseMultiplicative();
      const left = node;
      node = op === '+' ? (l) => left(l) + right(l) : (l) => left(l) - right(l);
    }
  }

  function parseMultiplicative(): Node {
    let node = parsePower();
    for (;;) {
      skipWs();
      const op = src[pos];
      if (op !== '*' && op !== '/') return node;
      pos++;
      const right = parsePower();
      const left = node;
      node = op === '*' ? (l) => left(l) * right(l) : (l) => left(l) / right(l);
    }
  }

  function parsePower(): Node {
    const base = parseUnary();
    skipWs();
    if (src[pos] === '^') {
      pos++;
      const exponent = parsePower(); // right-associative
      return (l) => Math.pow(base(l), exponent(l));
    }
    return base;
  }

  function parseUnary(): Node {
    skipWs();
    if (src[pos] === '-') {
      pos++;
      const operand = parseUnary();
      return (l) => -operand(l);
    }
    if (src[pos] === '+') {
      pos++;
      return parseUnary();
    }
    return parsePrimary();
  }

  function parsePrimary(): Node {
    skipWs();
    if (src[pos] === '(') {
      pos++;
      const inner = parseComparison();
      expect(')');
      return inner;
    }

    const identifier = /^[A-Za-z_][A-Za-z0-9_]*/.exec(src.slice(pos));
    if (identifier) {
      const name = identifier[0];
      pos += name.length;
      skipWs();
      if (src[pos] === '(') {
        pos++;
        const args: Node[] = [parseComparison()];
        for (;;) {
          skipWs();
          if (src[pos] !== ',') break;
          pos++;
          args.push(parseComparison());
        }
        expect(')');
        switch (name) {
          case 'if':
            if (args.length !== 3) throw new Error(`if() takes 3 arguments, got ${args.length}`);
            return (l) => (args[0]!(l) !== 0 ? args[1]!(l) : args[2]!(l));
          case 'min':
            return (l) => Math.min(...args.map((a) => a(l)));
          case 'max':
            return (l) => Math.max(...args.map((a) => a(l)));
          default:
            throw new Error(`unsupported function "${name}" — extend the evaluator if the curve needs it`);
        }
      }
      if (name === 'level') return (l) => l;
      throw new Error(`unknown identifier "${name}" — the only argument OpenMU binds is "level"`);
    }

    const number = /^\d+(\.\d+)?([eE][+-]?\d+)?/.exec(src.slice(pos));
    if (!number) throw new Error(`cannot parse at offset ${pos} of: ${src}`);
    pos += number[0].length;
    const value = Number(number[0]);
    return () => value;
  }

  const root = parseComparison();
  skipWs();
  if (pos !== src.length) throw new Error(`trailing input at offset ${pos} of: ${src}`);
  return root;
}

/**
 * The table the server builds at start-up: GameContext.CreateExpTable (GameContext.cs:462).
 * Index = character level, value = total accumulated experience required to BE that level.
 * Values are truncated to long, exactly as the engine's `(long)expression.calculate()` does.
 */
function buildExperienceTable(expression: string, maximumLevel: number): number[] {
  const evaluate = makeEvaluator(expression);
  const table: number[] = [];
  for (let level = 0; level <= maximumLevel + 1; level++) {
    table.push(Math.trunc(evaluate(level)));
  }
  return table;
}

// ---------------------------------------------------------------------------
// Monster roster, parsed from the initializer sources.
// ---------------------------------------------------------------------------

interface Monster {
  number: number;
  designation: string;
  level: number;
  maximumHealth: number | null;
  sourceFile: string;
}

function collectSourceFiles(directory: string, out: string[] = []): string[] {
  for (const entry of readdirSync(directory)) {
    const full = join(directory, entry);
    if (statSync(full).isDirectory()) collectSourceFiles(full, out);
    else if (entry.endsWith('.cs')) out.push(full);
  }
  return out;
}

/**
 * Parses `MonsterDefinition` seed blocks out of the initializer sources. The shape is
 * rigidly conventional across every map file (see docs/recon/balance-map.md Q3), so a
 * split on `CreateNew<MonsterDefinition>` plus three field regexes is reliable — and it
 * fails loudly (empty roster) rather than silently if upstream ever changes the shape.
 *
 * Keyed by the source file's base name, which is the map name for everything under Maps/.
 */
function loadMonsterRoster(repoRoot: string): Map<string, Monster[]> {
  const root = join(repoRoot, 'src/Persistence/Initialization');
  const roster = new Map<string, Monster[]>();

  for (const file of collectSourceFiles(root)) {
    const source = readFileSync(file, 'utf8').replace(/^﻿/, '');
    const blocks = source.split('CreateNew<MonsterDefinition>');
    if (blocks.length < 2) continue;

    const mapName = file.split('/').pop()!.replace(/\.cs$/, '');
    for (const block of blocks.slice(1)) {
      const chunk = block.slice(0, 4000);
      const number = /\.Number\s*=\s*(\d+);/.exec(chunk);
      const designation = /\.Designation\s*=\s*"([^"]+)"/.exec(chunk);
      const level = /\{\s*Stats\.Level,\s*([0-9.]+)f?\s*\}/.exec(chunk);
      const health = /\{\s*Stats\.MaximumHealth,\s*([0-9.]+)f?\s*\}/.exec(chunk);
      if (!number || !designation || !level) continue;

      const list = roster.get(mapName) ?? [];
      list.push({
        number: Number(number[1]),
        designation: designation[1]!,
        level: Number(level[1]),
        maximumHealth: health ? Number(health[1]) : null,
        sourceFile: relative(repoRoot, file),
      });
      roster.set(mapName, list);
    }
  }

  return roster;
}

function findMonster(roster: Map<string, Monster[]>, map: string, designation: string): Monster {
  const monsters = roster.get(map);
  if (!monsters) {
    throw new Error(`no monsters parsed for map "${map}" — did the initializer sources move?`);
  }
  const monster = monsters.find((m) => m.designation === designation);
  if (!monster) {
    throw new Error(
      `"${designation}" is not defined in ${map}.cs. Available: ${monsters.map((m) => m.designation).join(', ')}`,
    );
  }
  return monster;
}

// ---------------------------------------------------------------------------
// The XP-per-kill maths, transcribed from the engine.
// ---------------------------------------------------------------------------

/** AttackableExtensions.CalculateBaseExperience (AttackableExtensions.cs:593-615). */
function calculateBaseExperience(targetLevel: number, killerLevel: number): number {
  let experience = ((targetLevel + 25) * targetLevel) / 3.0;
  if (killerLevel > targetLevel + 10) {
    experience *= (targetLevel + 10) / killerLevel;
  }
  if (targetLevel >= 65) {
    experience += (targetLevel - 64) * (targetLevel / 4);
  }
  return Math.max(experience, 0) * 1.25;
}

/** Player.CalculateExpAfterKill (Player.cs:1238-1274), expectation over the random roll. */
function experiencePerKill(targetLevel: number, killerLevel: number): number {
  return (
    calculateBaseExperience(targetLevel, killerLevel) *
    GAME_RATE *
    PLAYER_RATE *
    MAP_EXP_MULTIPLIER *
    RANDOM_EXP_MEAN
  );
}

// ---------------------------------------------------------------------------
// Simulation.
// ---------------------------------------------------------------------------

interface LevelResult {
  level: number;
  requiredExperience: number;
  monster: Monster;
  killsPerMinute: number;
  experiencePerKill: number;
  kills: number;
  minutes: number;
}

function bandFor(level: number): FarmingBand {
  const band = FARMING_PLAN.find((b) => level <= b.upToLevel);
  if (!band) throw new Error(`farming plan does not cover level ${level}`);
  return band;
}

function simulate(table: number[], roster: Map<string, Monster[]>): LevelResult[] {
  const results: LevelResult[] = [];
  for (let level = 1; level < MAXIMUM_LEVEL; level++) {
    const band = bandFor(level);
    const monster = findMonster(roster, band.map, band.monster);
    // Player.cs:2003 — the character levels up when total experience reaches table[level + 1].
    const required = table[level + 1]! - table[level]!;
    const perKill = experiencePerKill(monster.level, level);
    const kills = required / perKill;
    results.push({
      level,
      requiredExperience: required,
      monster,
      killsPerMinute: band.killsPerMinute,
      experiencePerKill: perKill,
      kills,
      minutes: kills / band.killsPerMinute,
    });
  }
  return results;
}

// ---------------------------------------------------------------------------
// Reporting.
// ---------------------------------------------------------------------------

const hours = (minutes: number): number => minutes / 60;
const fmt = (value: number, digits = 2): string => value.toFixed(digits);
const compact = (value: number): string => {
  if (value >= 1e9) return `${(value / 1e9).toFixed(2)}B`;
  if (value >= 1e6) return `${(value / 1e6).toFixed(2)}M`;
  if (value >= 1e3) return `${(value / 1e3).toFixed(1)}k`;
  return value.toFixed(0);
};

function reportPhases(results: LevelResult[], label: string): boolean {
  const totalMinutes = results.reduce((sum, r) => sum + r.minutes, 0);
  let allWithinTolerance = true;
  let cumulative = 0;

  console.log(`\nCurve: ${label}`);
  console.log('');
  console.log('| Phase      | Levels    | Hours  | Cumulative | Canon target | Tolerance   | Verdict |');
  console.log('|------------|-----------|--------|------------|--------------|-------------|---------|');

  for (const phase of PHASES) {
    const phaseMinutes = results
      .filter((r) => r.level >= phase.from && r.level < phase.to + (phase.to === MAXIMUM_LEVEL ? 0 : 1))
      .filter((r) => r.level >= phase.from && r.level <= phase.to)
      .reduce((sum, r) => sum + r.minutes, 0);
    cumulative += phaseMinutes;
    const h = hours(phaseMinutes);
    const ok = h >= phase.min && h <= phase.max;
    allWithinTolerance &&= ok;
    console.log(
      `| ${phase.name.padEnd(10)} | ${`${phase.from}-${phase.to}`.padEnd(9)} | ${fmt(h).padStart(6)} | ${fmt(hours(cumulative)).padStart(10)} | ${`~${phase.targetHours} h`.padStart(12)} | ${`[${phase.min}, ${phase.max}]`.padStart(11)} | ${ok ? '  PASS ' : '  FAIL '} |`,
    );
  }

  const total = hours(totalMinutes);
  const totalOk = total >= TOTAL_TOLERANCE.min && total <= TOTAL_TOLERANCE.max;
  allWithinTolerance &&= totalOk;
  console.log(
    `| ${'TOTAL'.padEnd(10)} | ${'1-400'.padEnd(9)} | ${fmt(total).padStart(6)} | ${fmt(total).padStart(10)} | ${'~35 h'.padStart(12)} | ${`[${TOTAL_TOLERANCE.min}, ${TOTAL_TOLERANCE.max}]`.padStart(11)} | ${totalOk ? '  PASS ' : '  FAIL '} |`,
  );

  return allWithinTolerance;
}

function reportBands(results: LevelResult[]): void {
  console.log('\nFarming plan (the assumptions):\n');
  console.log('| Levels    | Map           | Monster              | Mon. lvl | Kills/min | XP/kill | Kills    | Hours |');
  console.log('|-----------|---------------|----------------------|----------|-----------|---------|----------|-------|');
  let from = 1;
  for (const band of FARMING_PLAN) {
    const slice = results.filter((r) => r.level >= from && r.level <= band.upToLevel);
    if (slice.length === 0) {
      from = band.upToLevel + 1;
      continue;
    }
    const monster = slice[0]!.monster;
    const minutes = slice.reduce((sum, r) => sum + r.minutes, 0);
    const kills = slice.reduce((sum, r) => sum + r.kills, 0);
    const avgPerKill = slice.reduce((sum, r) => sum + r.experiencePerKill, 0) / slice.length;
    console.log(
      `| ${`${from}-${band.upToLevel}`.padEnd(9)} | ${band.map.padEnd(13)} | ${monster.designation.padEnd(20)} | ${String(monster.level).padStart(8)} | ${String(band.killsPerMinute).padStart(9)} | ${compact(avgPerKill).padStart(7)} | ${compact(kills).padStart(8)} | ${fmt(hours(minutes), 1).padStart(5)} |`,
    );
    from = band.upToLevel + 1;
  }
  console.log('\nMonster levels above are parsed from the initializer sources, not typed in.');
  console.log(`Source of record: ${results[0]!.monster.sourceFile} and siblings.`);
}

function reportSpotCheck(results: LevelResult[], level: number): void {
  const row = results.find((r) => r.level === level);
  if (!row) {
    console.error(`level ${level} is outside 1..${MAXIMUM_LEVEL - 1}`);
    process.exitCode = 1;
    return;
  }
  console.log(`\nSpot check — character level ${level} -> ${level + 1}:`);
  console.log(`  farming        ${row.monster.designation} (level ${row.monster.level}, ${row.monster.sourceFile})`);
  console.log(`  XP required    ${row.requiredExperience.toLocaleString('en-US')}`);
  console.log(`  XP per kill    ${Math.round(row.experiencePerKill).toLocaleString('en-US')}`);
  console.log(`  kills          ${Math.round(row.kills).toLocaleString('en-US')}`);
  console.log(`  at ${row.killsPerMinute} kills/min  ${fmt(row.minutes, 1)} minutes`);
  console.log('\nIn game: /level yourself to this level, kill 10 of that monster, and compare');
  console.log('the XP gained against "XP per kill" above (expect +/- 20 % from the random roll).');
}

// ---------------------------------------------------------------------------
// Curve sources.
// ---------------------------------------------------------------------------

function readShippedCurve(repoRoot: string): { expression: string; label: string } {
  const path = join(repoRoot, CURVE_PLUGIN_PATH);
  const source = readFileSync(path, 'utf8');
  const match = /const string ExperienceFormula\s*=\s*"([^"]+)"/.exec(source);
  if (!match) {
    throw new Error(`could not find "const string ExperienceFormula" in ${CURVE_PLUGIN_PATH}`);
  }
  return { expression: match[1]!, label: `Mu Miami, from ${CURVE_PLUGIN_PATH}` };
}

function readLiveCurve(repoRoot: string): { expression: string; label: string } {
  const output = execFileSync(
    join(repoRoot, 'scripts/mm'),
    ['psql', '-tAc', 'SELECT "ExperienceFormula" FROM config."GameConfiguration" LIMIT 1;'],
    { cwd: repoRoot, encoding: 'utf8' },
  );
  const expression = output.trim();
  if (!expression) throw new Error('the live GameConfiguration has no ExperienceFormula');
  return { expression, label: 'live server (config.GameConfiguration.ExperienceFormula)' };
}

// ---------------------------------------------------------------------------
// Entry point.
// ---------------------------------------------------------------------------

function main(): void {
  const argv = process.argv.slice(2);
  const repoRoot = process.cwd();
  const flag = (name: string): boolean => argv.includes(name);
  const option = (name: string): string | undefined => {
    const index = argv.indexOf(name);
    return index >= 0 ? argv[index + 1] : undefined;
  };

  let curve: { expression: string; label: string };
  const explicit = option('--expression');
  if (explicit) curve = { expression: explicit, label: 'command line --expression' };
  else if (flag('--vanilla')) curve = { expression: VANILLA_EXPERIENCE_FORMULA, label: 'stock Season 6 Episode 3' };
  else if (flag('--from-db')) curve = readLiveCurve(repoRoot);
  else curve = readShippedCurve(repoRoot);

  const roster = loadMonsterRoster(repoRoot);
  const table = buildExperienceTable(curve.expression, MAXIMUM_LEVEL);
  const results = simulate(table, roster);

  console.log('Mu Miami progression simulator');
  console.log(`  expression   ${curve.expression}`);
  console.log(`  total XP to 400   ${table[MAXIMUM_LEVEL]!.toLocaleString('en-US')}`);

  const spotCheck = option('--spot-check');
  if (spotCheck) {
    reportSpotCheck(results, Number(spotCheck));
    return;
  }

  const withinTolerance = reportPhases(results, curve.label);
  if (flag('--detail')) reportBands(results);

  if (flag('--json')) {
    console.log(JSON.stringify(results.map((r) => ({ ...r, monster: r.monster.designation })), null, 2));
  }

  console.log('');
  if (withinTolerance) {
    console.log('All phases within the brief 002 tolerances.');
  } else {
    console.log('OUT OF TOLERANCE — see the FAIL rows above.');
    process.exitCode = 1;
  }
}

main();
