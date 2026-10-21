const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

function setupGitHooks() {
  const rootDir = path.resolve(__dirname, "..");
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
  } catch (error) {
    console.log(
      "Skipping Git hooks setup: Git command failed or Git is not installed."
    );
  }
}

setupGitHooks();
