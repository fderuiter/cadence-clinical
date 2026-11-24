const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

function setupWorkspaceCliBin(rootDir) {
  const binDir = path.join(rootDir, "node_modules", ".bin");
  if (!fs.existsSync(binDir)) {
    return;
  }

  const posixShim = path.join(binDir, "cadence");
  const posixContent =
    '#!/bin/sh\nbasedir=$(dirname "$(echo "$0" | sed -e \'s,\\\\,/,g\')")\nexec node "$basedir/../../scripts/run-cli.js" "$@"\n';
  fs.writeFileSync(posixShim, posixContent, { mode: 0o755 });
  if (process.platform !== "win32") {
    fs.chmodSync(posixShim, 0o755);
  }

  const cmdShim = path.join(binDir, "cadence.cmd");
  const cmdContent =
    '@ECHO off\r\nnode "%~dp0\\..\\..\\scripts\\run-cli.js" %*\r\n';
  fs.writeFileSync(cmdShim, cmdContent);
}

function setupGitHooks() {
  const rootDir = path.resolve(__dirname, "..");
  setupWorkspaceCliBin(rootDir);
  const gitPath = path.join(rootDir, ".git");

  if (!fs.existsSync(gitPath)) {
    console.log("Skipping Git hooks setup: .git directory not found.");
    return;
  }

  try {
    execSync("git config core.hooksPath .githooks", {
      cwd: rootDir,
      stdio: "ignore",
    });

    const preCommitPath = path.join(rootDir, ".githooks", "pre-commit");
    if (fs.existsSync(preCommitPath) && process.platform !== "win32") {
      fs.chmodSync(preCommitPath, 0o755);
    }

    console.log("✔ Git hooks configured (.githooks)");
  } catch {
    console.log(
      "Skipping Git hooks setup: Git command failed or Git is not installed."
    );
  }
}

setupGitHooks();
