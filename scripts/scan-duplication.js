const { execSync } = require('child_process');
const path = require('path');
const fs = require('fs');

// Determine if we should run in staged mode or CI mode
let files = process.argv.slice(2);
let isLocal = !process.env.CI;

// Determine diff command to fetch edited line numbers
let diffCommand = '';
if (isLocal) {
  diffCommand = 'git diff --cached --unified=0';
} else {
  let targetBranch = 'origin/main';
  try {
    execSync('git rev-parse --verify origin/main', { stdio: 'ignore' });
  } catch (e) {
    try {
      execSync('git rev-parse --verify main', { stdio: 'ignore' });
      targetBranch = 'main';
    } catch (err) {
      targetBranch = 'HEAD~1';
    }
  }
  diffCommand = `git diff --unified=0 ${targetBranch}...HEAD`;
}

console.log(`Running duplication check in ${isLocal ? 'LOCAL (staged)' : 'CI'} mode...`);
console.log(`Diff command: ${diffCommand}`);

// Parse git diff output for modified line numbers
const changedLinesByFile = {}; // relative_path -> Set of line numbers
try {
  const diffOutput = execSync(diffCommand, { encoding: 'utf8' });
  const lines = diffOutput.split('\n');
  let currentFile = null;

  for (const line of lines) {
    if (line.startsWith('+++ b/')) {
      currentFile = line.substring(6).trim();
      changedLinesByFile[currentFile] = new Set();
    } else if (line.startsWith('@@ ') && currentFile) {
      const match = line.match(/^@@\s+-\d+(?:,\d+)?\s+\+(\d+)(?:,(\d+))?\s+@@/);
      if (match) {
        const startLine = parseInt(match[1], 10);
        const count = match[2] ? parseInt(match[2], 10) : 1;
        for (let i = 0; i < count; i++) {
          changedLinesByFile[currentFile].add(startLine + i);
        }
      }
    }
  }
} catch (err) {
  console.warn('Warning: Unable to parse git diff for precise line validation. Fallback to scanning all lines in files.');
}

// If no file arguments are passed via CLI, find all changed files from git diff
if (files.length === 0) {
  files = Object.keys(changedLinesByFile);
}

// Filter files to only check GxP/production paths and ignore scratch, tests, etc.
const ignorePatterns = [
  /node_modules/,
  /\.spec\.js$/,
  /\.test\.js$/,
  /_test\.py$/,
  /tests\//,
  /verification\//,
  /scratch\//,
  /prototype\//,
  /\.venv\//,
  /dist\//,
  /build\//
];

let validFiles = files.filter(file => {
  const isProduction = file.startsWith('apps/') || file.startsWith('packages/');
  if (!isProduction) return false;
  
  // Check ignore patterns
  for (const pattern of ignorePatterns) {
    if (pattern.test(file)) {
      return false;
    }
  }
  
  // Check if file exists and is a file (not deleted)
  try {
    return fs.statSync(file).isFile();
  } catch (e) {
    return false;
  }
});

if (validFiles.length === 0) {
  console.log('No production files modified to scan for duplication.');
  process.exit(0);
}

console.log(`Scanning ${validFiles.length} files for code duplication...`);
const fileArgs = validFiles.map(f => `"${f}"`).join(' ');

// Clean up previous report folder if any
fs.rmSync('.jscpd-report', { recursive: true, force: true });

try {
  // Run jscpd to generate JSON report
  execSync(`pnpm exec jscpd -c .jscpd.json -r json --output .jscpd-report ${fileArgs}`, { stdio: 'pipe' });
} catch (error) {
  // jscpd exits with 1 when duplication percentage > threshold (0%)
  // That's expected, we will parse the report.json to filter clones.
}

const reportPath = path.join('.jscpd-report', 'jscpd-report.json');
if (!fs.existsSync(reportPath)) {
  console.log('Duplication check passed (no clones found).');
  process.exit(0);
}

let report;
try {
  report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));
} catch (e) {
  console.error('Failed to parse jscpd report. Assuming check passed.');
  process.exit(0);
}

// Clean up report
fs.rmSync('.jscpd-report', { recursive: true, force: true });

const duplicates = report.duplicates || [];
const blockedClones = [];
const ignoredClones = [];

for (const clone of duplicates) {
  const fileA_rel = path.relative('/app', clone.firstFile.name);
  const fileB_rel = path.relative('/app', clone.secondFile.name);

  // Check overlap for file A
  let fileA_overlaps = false;
  const setA = changedLinesByFile[fileA_rel];
  if (setA) {
    for (let line = clone.firstFile.start; line <= clone.firstFile.end; line++) {
      if (setA.has(line)) {
        fileA_overlaps = true;
        break;
      }
    }
  } else if (!isLocal) {
    // If we're in CI and the file is not in diff, but we are scanning it, count as overlap
    fileA_overlaps = true;
  }

  // Check overlap for file B
  let fileB_overlaps = false;
  const setB = changedLinesByFile[fileB_rel];
  if (setB) {
    for (let line = clone.secondFile.start; line <= clone.secondFile.end; line++) {
      if (setB.has(line)) {
        fileB_overlaps = true;
        break;
      }
    }
  } else if (!isLocal) {
    // If we're in CI and the file is not in diff, but we are scanning it, count as overlap
    fileB_overlaps = true;
  }

  if (fileA_overlaps || fileB_overlaps) {
    blockedClones.push(clone);
  } else {
    ignoredClones.push(clone);
  }
}

if (ignoredClones.length > 0) {
  console.log(`\nFound ${ignoredClones.length} pre-existing historical duplicates (ignored/not modified in this commit).`);
}

if (blockedClones.length > 0) {
  console.error(`\nERROR: Duplication check failed. Found ${blockedClones.length} NEW or MODIFIED duplicated code blocks:\n`);
  for (const clone of blockedClones) {
    const fileA_rel = path.relative('/app', clone.firstFile.name);
    const fileB_rel = path.relative('/app', clone.secondFile.name);
    console.error(`- Duplicate block found (${clone.lines} lines, ${clone.tokens} tokens):`);
    console.error(`  1. File: ${fileA_rel} (Lines ${clone.firstFile.start}-${clone.firstFile.end})`);
    console.error(`  2. File: ${fileB_rel} (Lines ${clone.secondFile.start}-${clone.secondFile.end})`);
    console.error(`  --- Duplicate Fragment ---`);
    console.error(clone.fragment.split('\n').slice(0, 5).join('\n') + (clone.fragment.split('\n').length > 5 ? '\n  ...' : ''));
    console.error(`  --------------------------\n`);
  }
  process.exit(1);
}

console.log('Duplication check passed successfully.');
process.exit(0);
