#!/usr/bin/env node

/**
 * Cadence Clinical Platform - CLI Cross-Platform Runner Script
 * Executes compiled Native AOT binary (./bin/cadence) with automatic
 * compilation fallback when missing or stale.
 */

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

const rootDir = path.resolve(__dirname, "..");
const isWin = process.platform === "win32";
const binaryName = isWin ? "cadence.exe" : "cadence";
const binaryPath = path.join(rootDir, "bin", binaryName);
const csprojPath = path.join(
  rootDir,
  "packages",
  "cli",
  "src",
  "Cadence.Cli.csproj"
);
const cliSourceDirs = [
  path.join(rootDir, "packages", "cli", "src"),
  path.join(rootDir, "packages", "engine_core", "src"),
  path.join(rootDir, "packages", "database", "src"),
  path.join(rootDir, "packages", "compliance", "src"),
  path.join(rootDir, "packages", "security", "src"),
  path.join(rootDir, "packages", "tooling_core", "src"),
  path.join(
    rootDir,
    "packages",
    "kernel",
    "src",
    "Cadence.Kernel.Abstractions"
  ),
  path.join(rootDir, "packages", "core-models", "src"),
  path.join(rootDir, "packages", "deid", "src"),
  path.join(rootDir, "packages", "topology", "src"),
];
const buildConfigFiles = [
  "Directory.Build.props",
  "Directory.Build.targets",
  "Directory.Packages.props",
  "NuGet.Config",
  "global.json",
  "BannedSymbols.txt",
];

function isExecutableOnPath(exeName, pathEnv) {
  if (!pathEnv) return false;
  const pathSep = isWin ? ";" : ":";
  for (const rawEntry of pathEnv.split(pathSep)) {
    const entry = rawEntry.trim();
    if (!entry) continue;
    const candidate = path.join(entry, exeName);
    try {
      if (fs.statSync(candidate).isFile()) {
        if (!isWin) {
          fs.accessSync(candidate, fs.constants.X_OK);
        }
        return true;
      }
    } catch {
      // Continue scanning PATH entries
    }
  }
  return false;
}

// Preserve ~/.dotnet/tools on PATH and only set DOTNET_ROOT when PATH lacks dotnet
// and ~/.dotnet contains an actual .NET host runtime (dotnet + host/fxr).
const homeDir = process.env.HOME || process.env.USERPROFILE;
if (homeDir) {
  const dotnetHome = path.join(homeDir, ".dotnet");
  const dotnetTools = path.join(dotnetHome, "tools");
  const pathSep = isWin ? ";" : ":";

  if (fs.existsSync(dotnetTools)) {
    const currentEntries = (process.env.PATH || "").split(pathSep);
    if (!currentEntries.includes(dotnetTools)) {
      process.env.PATH = process.env.PATH
        ? `${dotnetTools}${pathSep}${process.env.PATH}`
        : dotnetTools;
    }
  }

  const dotnetExeName = isWin ? "dotnet.exe" : "dotnet";
  const localDotnetExe = path.join(dotnetHome, dotnetExeName);
  const localHostFxr = path.join(dotnetHome, "host", "fxr");
  if (
    !isExecutableOnPath(dotnetExeName, process.env.PATH) &&
    fs.existsSync(localDotnetExe) &&
    fs.existsSync(localHostFxr)
  ) {
    process.env.DOTNET_ROOT = process.env.DOTNET_ROOT || dotnetHome;
    const currentEntries = (process.env.PATH || "").split(pathSep);
    if (!currentEntries.includes(dotnetHome)) {
      process.env.PATH = process.env.PATH
        ? `${dotnetHome}${pathSep}${process.env.PATH}`
        : dotnetHome;
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
      if (
        entry.name === "bin" ||
        entry.name === "obj" ||
        entry.name.startsWith(".")
      ) {
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

  if (
    process.env.CADENCE_CLI_CACHE_HIT === "true" ||
    process.env.GITHUB_ACTIONS === "true"
  ) {
    if (process.env.CADENCE_CLI_CACHE_HIT === "true") {
      const now = new Date();
      try {
        fs.utimesSync(binaryPath, now, now);
      } catch {
        // Ignore utime update failures on read-only filesystems
      }
    }
    return false;
  }

  const binaryMtime = fs.statSync(binaryPath).mtimeMs;
  const latestSourceMtime = Math.max(
    ...cliSourceDirs.map((dir) => getLatestSourceMtime(dir)),
    ...buildConfigFiles.map(
      (file) =>
        fs.statSync(path.join(rootDir, file), { throwIfNoEntry: false })
          ?.mtimeMs ?? 0
    )
  );

  return latestSourceMtime > binaryMtime;
}

/**
 * Builds the CLI binary using dotnet publish.
 */
function buildBinary() {
  // Check if dotnet executable is available
  const dotnetCheck = spawnSync("dotnet", ["--version"], {
    stdio: "ignore",
    env: process.env,
  });

  if (dotnetCheck.error && dotnetCheck.error.code === "ENOENT") {
    console.error(
      "::error:: .NET SDK not found. Please install .NET 10 SDK to run Cadence CLI or quality gates."
    );
    process.exit(1);
  }

  console.log("[run-cli] Compiling Cadence CLI binary...");
  const cliArtifactsDir = path.join(rootDir, ".cadence", "cli-artifacts");
  const publishResult = spawnSync(
    "dotnet",
    [
      "publish",
      csprojPath,
      "-c",
      "Release",
      "--self-contained",
      "-p:UseArtifactsOutput=true",
      `-p:ArtifactsPath=${cliArtifactsDir}`,
      "-o",
      path.join(rootDir, "bin"),
    ],
    {
      stdio: "inherit",
      cwd: rootDir,
      env: process.env,
    }
  );

  if (publishResult.status !== 0) {
    console.error("::error:: Failed to compile Cadence CLI binary.");
    process.exit(publishResult.status ?? 1);
  }
}

function main() {
  if (needsBuild()) {
    buildBinary();
  }

  if (!isWin) {
    try {
      fs.accessSync(binaryPath, fs.constants.X_OK);
    } catch {
      fs.chmodSync(binaryPath, 0o755);
    }
  }

  const args = process.argv.slice(2);
  const runResult = spawnSync(binaryPath, args, {
    stdio: "inherit",
    cwd: rootDir,
    env: process.env,
  });

  if (runResult.error) {
    console.error(
      "::error:: Failed to execute Cadence CLI binary:",
      runResult.error.message
    );
    process.exit(1);
  }

  process.exit(runResult.status ?? (runResult.signal ? 1 : 0));
}

main();
