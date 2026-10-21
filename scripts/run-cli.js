#!/usr/bin/env node

/**
 * Cadence Clinical Platform - CLI Cross-Platform Runner Script
 * Executes compiled Native AOT binary (./bin/cadence) with automatic
 * compilation fallback when missing or stale.
 */

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const rootDir = path.resolve(__dirname, '..');
const isWin = process.platform === 'win32';
const binaryName = isWin ? 'cadence.exe' : 'cadence';
const binaryPath = path.join(rootDir, 'bin', binaryName);
const csprojPath = path.join(rootDir, 'packages', 'cli', 'src', 'Cadence.Cli.csproj');
const cliSrcDir = path.join(rootDir, 'packages', 'cli', 'src');

// Ensure local .NET installation is available in PATH
const homeDir = process.env.HOME || process.env.USERPROFILE;
if (homeDir) {
  const dotnetHome = path.join(homeDir, '.dotnet');
  if (fs.existsSync(dotnetHome)) {
    process.env.DOTNET_ROOT = process.env.DOTNET_ROOT || dotnetHome;
    const dotnetTools = path.join(dotnetHome, 'tools');
    const pathSep = isWin ? ';' : ':';
    if (!process.env.PATH || !process.env.PATH.includes(dotnetHome)) {
      process.env.PATH = `${dotnetHome}${pathSep}${dotnetTools}${pathSep}${process.env.PATH || ''}`;
    }
  }
}

/**
 * Gets the latest file modification time (mtimeMs) for source files in dir,
 * ignoring bin, obj, and hidden files/directories.
 */
function getLatestSourceMtime(dir) {
  if (!fs.existsSync(dir)) return 0;
  let maxMtime = 0;
  try {
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    for (const entry of entries) {
      if (entry.name === 'bin' || entry.name === 'obj' || entry.name.startsWith('.')) {
        continue;
      }
      const fullPath = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        const subMax = getLatestSourceMtime(fullPath);
        if (subMax > maxMtime) maxMtime = subMax;
      } else if (entry.isFile()) {
        const stat = fs.statSync(fullPath);
        if (stat.mtimeMs > maxMtime) maxMtime = stat.mtimeMs;
      }
    }
  } catch (err) {
    // If directory reading fails, ignore and return current maxMtime
  }
  return maxMtime;
}

/**
 * Checks whether the binary needs to be built (missing or stale).
 */
function needsBuild() {
  if (!fs.existsSync(binaryPath)) {
    return true;
  }

  const binaryMtime = fs.statSync(binaryPath).mtimeMs;
  const latestSourceMtime = getLatestSourceMtime(cliSrcDir);

  return latestSourceMtime > binaryMtime;
}

/**
 * Builds the CLI binary using dotnet publish.
 */
function buildBinary() {
  // Check if dotnet executable is available
  const dotnetCheck = spawnSync('dotnet', ['--version'], {
    stdio: 'ignore',
    env: process.env,
  });

  if (dotnetCheck.error && dotnetCheck.error.code === 'ENOENT') {
    console.error('::error:: .NET SDK not found. Please install .NET 10 SDK to run Cadence CLI or quality gates.');
    process.exit(1);
  }

  console.log('[run-cli] Compiling Cadence CLI binary...');
  const publishResult = spawnSync(
    'dotnet',
    [
      'publish',
      csprojPath,
      '-c',
      'Release',
      '--self-contained',
      '-o',
      path.join(rootDir, 'bin'),
    ],
    {
      stdio: 'inherit',
      cwd: rootDir,
      env: process.env,
    }
  );

  if (publishResult.status !== 0) {
    console.error('::error:: Failed to compile Cadence CLI binary.');
    process.exit(publishResult.status ?? 1);
  }
}

function main() {
  if (needsBuild()) {
    buildBinary();
  }

  const args = process.argv.slice(2);
  const runResult = spawnSync(binaryPath, args, {
    stdio: 'inherit',
    cwd: rootDir,
    env: process.env,
  });

  if (runResult.error) {
    console.error('::error:: Failed to execute Cadence CLI binary:', runResult.error.message);
    process.exit(1);
  }

  process.exit(runResult.status ?? (runResult.signal ? 1 : 0));
}

main();
