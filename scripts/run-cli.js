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

function readGlobalJsonSdkPolicy() {
  const defaultPolicy = {
    major: 10,
    minor: 0,
    patch: 100,
    rollForward: "latestFeature",
    allowPrerelease: false,
  };
  try {
    const raw = fs.readFileSync(path.join(rootDir, "global.json"), "utf8");
    const parsed = JSON.parse(raw);
    const sdk = parsed && typeof parsed === "object" ? parsed.sdk : null;
    if (!sdk || typeof sdk !== "object") return defaultPolicy;

    const match =
      typeof sdk.version === "string"
        ? /^(\d+)\.(\d+)\.(\d+)/.exec(sdk.version.trim())
        : null;
    return {
      major: match ? Number(match[1]) : defaultPolicy.major,
      minor: match ? Number(match[2]) : defaultPolicy.minor,
      patch: match ? Number(match[3]) : defaultPolicy.patch,
      rollForward:
        typeof sdk.rollForward === "string"
          ? sdk.rollForward
          : defaultPolicy.rollForward,
      allowPrerelease: Boolean(sdk.allowPrerelease),
    };
  } catch {
    return defaultPolicy;
  }
}

function sdkVersionSatisfiesPolicy(versionText, policy) {
  if (!versionText || typeof versionText !== "string") return false;
  const trimmed = versionText.trim();
  if (!policy.allowPrerelease && trimmed.includes("-")) {
    return false;
  }

  const core = trimmed.split("-")[0];
  const match = /^(\d+)\.(\d+)\.(\d+)$/.exec(core);
  if (!match) return false;

  const major = Number(match[1]);
  const minor = Number(match[2]);
  const patch = Number(match[3]);

  switch (policy.rollForward) {
    case "latestMajor":
    case "major":
      if (major !== policy.major) return major > policy.major;
      if (minor !== policy.minor) return minor > policy.minor;
      return patch >= policy.patch;
    case "latestMinor":
    case "minor":
      if (major !== policy.major) return false;
      if (minor !== policy.minor) return minor > policy.minor;
      return patch >= policy.patch;
    case "latestPatch":
      return (
        major === policy.major &&
        minor === policy.minor &&
        Math.trunc(patch / 100) === Math.trunc(policy.patch / 100) &&
        patch >= policy.patch
      );
    case "disable":
      return (
        major === policy.major &&
        minor === policy.minor &&
        patch === policy.patch
      );
    default:
      return (
        major === policy.major &&
        minor === policy.minor &&
        patch >= policy.patch
      );
  }
}

function getPathEnvKey(env = process.env) {
  if (!isWin) return "PATH";
  const found = Object.keys(env).find((key) => key.toUpperCase() === "PATH");
  return found || "PATH";
}

function prependPathEntry(targetDir) {
  const pathSep = isWin ? ";" : ":";
  const pathKey = getPathEnvKey();
  const entries = (process.env[pathKey] || "")
    .split(pathSep)
    .filter((entry) => entry && entry !== targetDir);
  entries.unshift(targetDir);
  process.env[pathKey] = entries.join(pathSep);
}

function appendMissingToolsPath(toolsDir) {
  if (!fs.existsSync(toolsDir)) return;
  const pathSep = isWin ? ";" : ":";
  const pathKey = getPathEnvKey();
  const currentValue = process.env[pathKey] || "";
  const currentEntries = currentValue.split(pathSep);
  if (!currentEntries.includes(toolsDir)) {
    process.env[pathKey] = currentValue
      ? `${toolsDir}${pathSep}${currentValue}`
      : toolsDir;
  }
}

function isValidDotnetSdkRoot(candidateDir, policy) {
  if (!candidateDir) return false;
  const dotnetExeName = isWin ? "dotnet.exe" : "dotnet";
  const dotnetExe = path.join(candidateDir, dotnetExeName);
  const hostFxrDir = path.join(candidateDir, "host", "fxr");
  const sdkDir = path.join(candidateDir, "sdk");

  try {
    if (!fs.statSync(dotnetExe).isFile()) return false;
    if (!isWin) {
      fs.accessSync(dotnetExe, fs.constants.X_OK);
    }
    if (!fs.statSync(hostFxrDir).isDirectory()) return false;
    if (!fs.statSync(sdkDir).isDirectory()) return false;

    const entries = fs.readdirSync(sdkDir);
    return entries.some((name) => {
      if (!name.startsWith(`${policy.major}.`)) return false;
      if (!sdkVersionSatisfiesPolicy(name, policy)) return false;
      try {
        return fs.statSync(path.join(sdkDir, name)).isDirectory();
      } catch {
        return false;
      }
    });
  } catch {
    return false;
  }
}

function activeDotnetSatisfiesGlobalJson(policy) {
  const dotnetExeName = isWin ? "dotnet.exe" : "dotnet";
  const probe = spawnSync(dotnetExeName, ["--version"], {
    cwd: rootDir,
    encoding: "utf8",
    timeout: 5000,
    stdio: ["ignore", "pipe", "pipe"],
    env: {
      ...process.env,
      DOTNET_CLI_TELEMETRY_OPTOUT: "1",
      DOTNET_SKIP_FIRST_TIME_EXPERIENCE: "1",
      DOTNET_NOLOGO: "1",
    },
  });
  if (probe.error || probe.status !== 0) {
    return false;
  }
  return sdkVersionSatisfiesPolicy(probe.stdout, policy);
}

function configureDotnetEnvironment() {
  const sdkPolicy = readGlobalJsonSdkPolicy();
  const homeDir = process.env.HOME || process.env.USERPROFILE;
  if (homeDir) {
    appendMissingToolsPath(path.join(homeDir, ".dotnet", "tools"));
  }

  if (activeDotnetSatisfiesGlobalJson(sdkPolicy)) {
    return;
  }

  const candidateRoots = [];
  if (process.env.DOTNET_ROOT) {
    candidateRoots.push(process.env.DOTNET_ROOT);
  }
  if (homeDir) {
    candidateRoots.push(path.join(homeDir, ".dotnet"));
  }
  if (process.env.LOCALAPPDATA) {
    candidateRoots.push(
      path.join(process.env.LOCALAPPDATA, "Microsoft", "dotnet")
    );
  }

  for (const candidate of candidateRoots) {
    if (isValidDotnetSdkRoot(candidate, sdkPolicy)) {
      process.env.DOTNET_ROOT = candidate;
      prependPathEntry(candidate);
      return;
    }
  }
}

configureDotnetEnvironment();

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

function resolveDotnetExecutable() {
  const dotnetExeName = isWin ? "dotnet.exe" : "dotnet";
  if (process.env.DOTNET_ROOT) {
    const candidate = path.join(process.env.DOTNET_ROOT, dotnetExeName);
    if (fs.existsSync(candidate)) {
      return candidate;
    }
  }
  return dotnetExeName;
}

/**
 * Builds the CLI binary using dotnet publish.
 */
function buildBinary() {
  const dotnetCmd = resolveDotnetExecutable();
  // Check if dotnet executable is available
  const dotnetCheck = spawnSync(dotnetCmd, ["--version"], {
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
    dotnetCmd,
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
