const { execSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const repoRoot = path.resolve(__dirname, "..");

if (!process.env.AUDIT_LOG_SECRET_KEY) {
  process.env.AUDIT_LOG_SECRET_KEY =
    "development_secret_key_for_rtm_generation_32bytes"; // pragma: allowlist secret
}
if (!process.env.INBOUND_EMAIL_HMAC_SECRET) {
  process.env.INBOUND_EMAIL_HMAC_SECRET =
    "development_secret_key_for_rtm_generation_32bytes"; // pragma: allowlist secret
}
if (!process.env.GATEWAY_SECRET_KEY) {
  process.env.GATEWAY_SECRET_KEY =
    "development_secret_key_for_rtm_generation_32bytes"; // pragma: allowlist secret
}

// Ensure adequate heap allocation for large documentation bundles under VitePress
if (
  !process.env.NODE_OPTIONS ||
  !process.env.NODE_OPTIONS.includes("--max-old-space-size")
) {
  process.env.NODE_OPTIONS =
    `${process.env.NODE_OPTIONS || ""} --max-old-space-size=16384`.trim();
}

let pnpmCmd = "pnpm";

/**
 * Preflight checks to verify required executables are on PATH.
 */
function runPreflightChecks() {
  console.log("--- Preflight Tool-Availability Check ---");

  try {
    execSync("pnpm -v", { stdio: "ignore", cwd: repoRoot });
    pnpmCmd = "pnpm";
  } catch (e) {
    try {
      execSync("npx pnpm -v", { stdio: "ignore", cwd: repoRoot });
      pnpmCmd = "npx pnpm";
    } catch (err) {
      console.error('\n[ERROR] Missing required tool: "pnpm" (or npx pnpm).');
      console.error("Please install pnpm or Node/npm and try again.");
      process.exit(1);
    }
  }

  const tools = [
    {
      name: "node",
      cmd: "node -v",
      desc: "Node.js runtime environment",
      expected: "expected to be provided by Node.js installer or nvm",
    },
    {
      name: "pnpm",
      cmd: `${pnpmCmd} -v`,
      desc: "pnpm package manager",
      expected: "expected to be provided via pnpm or npx pnpm",
    },
  ];

  for (const tool of tools) {
    try {
      execSync(tool.cmd, { stdio: "ignore", cwd: repoRoot });
    } catch (err) {
      console.error(
        `\n[ERROR] Missing required tool: "${tool.name}" (${tool.desc}).`
      );
      console.error(
        `How it's expected to be provided: ${tool.expected}. Please install it and try again.`
      );
      process.exit(1);
    }
  }
  console.log(
    `Preflight checks successful. Using package manager command: "${pnpmCmd}".\n`
  );
}

/**
 * Executes a shell command from the application directory.
 * @param {string} command - The shell command to execute.
 */
function runCommand(command) {
  console.log(`Running: ${command}`);
  try {
    // Use repoRoot for cwd to keep the execution portable and environment-agnostic
    execSync(command, { stdio: "inherit", cwd: repoRoot, env: process.env });
  } catch (error) {
    console.error(`Command failed: ${command}`);
    throw error;
  }
}

/**
 * Rewrites relative links inside staged markdown documents.
 * @param {string} content - The markdown file content.
 * @returns {string} The updated markdown content with corrected links.
 */
function rewriteLinks(content) {
  // Inline links [text](link), ignoring fenced and inline code blocks
  let rewritten = content.replace(
    /```[\s\S]*?```|`[^`\n]+`|(\[(?:[^\]]|\\\])*\]\()([^)]+)(\))/g,
    (match, prefix, url, suffix) => {
      if (match.startsWith("```") || match.startsWith("`")) {
        return match;
      }
      let cleanUrl = url.trim();
      // Only modify relative local links. If it starts with http, https, file, mailto, or #, keep it as-is.
      if (
        cleanUrl.startsWith("http://") ||
        cleanUrl.startsWith("https://") ||
        cleanUrl.startsWith("file://") ||
        cleanUrl.startsWith("mailto:") ||
        cleanUrl.startsWith("#")
      ) {
        return match;
      }

      // If the relative link starts with docs/, strip 'docs/' prefix
      if (cleanUrl.startsWith("docs/")) {
        cleanUrl = cleanUrl.substring(5); // strip 'docs/'
      } else if (cleanUrl === "LICENSE") {
        // If it points to LICENSE, change it to LICENSE.md
        cleanUrl = "LICENSE.md";
      } else if (
        !cleanUrl.startsWith("../") &&
        fs.existsSync(path.join(repoRoot, withoutAnchor(cleanUrl))) &&
        !fs.existsSync(path.join(repoRoot, "docs", withoutAnchor(cleanUrl)))
      ) {
        cleanUrl = "../" + cleanUrl;
      }

      return `${prefix}${cleanUrl}${suffix}`;
    }
  );

  // Reference links [id]: link
  rewritten = rewritten.replace(
    /^(\[[^\]]+\]:\s*)(\S+)/gm,
    (match, prefix, url) => {
      let cleanUrl = url.trim();
      if (
        cleanUrl.startsWith("http://") ||
        cleanUrl.startsWith("https://") ||
        cleanUrl.startsWith("file://") ||
        cleanUrl.startsWith("mailto:") ||
        cleanUrl.startsWith("#")
      ) {
        return match;
      }

      if (cleanUrl.startsWith("docs/")) {
        cleanUrl = cleanUrl.substring(5);
      } else if (cleanUrl === "LICENSE") {
        cleanUrl = "LICENSE.md";
      } else if (
        !cleanUrl.startsWith("../") &&
        fs.existsSync(path.join(repoRoot, withoutAnchor(cleanUrl))) &&
        !fs.existsSync(path.join(repoRoot, "docs", withoutAnchor(cleanUrl)))
      ) {
        cleanUrl = "../" + cleanUrl;
      }

      return `${prefix}${cleanUrl}`;
    }
  );

  return rewritten;
}

/**
 * Drops a link's `#anchor`, so `CONTRIBUTING.md#3-local-checks` is checked as the file `CONTRIBUTING.md`.
 * @param {string} url - The link target.
 * @returns {string} The target without its anchor.
 */
function withoutAnchor(url) {
  return url.split("#")[0];
}

/**
 * Copies a source markdown file to docs directory with link preprocessing.
 * @param {string} srcName - The source file name in repo root.
 * @param {string} destName - The destination file name in docs/ folder.
 */
function copyAndPreprocess(srcName, destName) {
  const srcPath = path.join(repoRoot, srcName);
  const destPath = path.join(repoRoot, "docs", destName);
  if (fs.existsSync(destPath) && !originalFilesMap.has(destPath)) {
    originalFilesMap.set(destPath, fs.readFileSync(destPath, "utf8"));
  }
  let content = fs.readFileSync(srcPath, "utf8");
  content = rewriteLinks(content);
  fs.writeFileSync(destPath, content, "utf8");
  console.log(
    `Successfully prepared and preprocessed ${srcName} -> docs/${destName}`
  );
}

const originalFilesMap = new Map();

function sweepAndPreprocessDocs(docsDir) {
  function walkDir(dir) {
    if (!fs.existsSync(dir)) return;
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    entries.sort((a, b) => a.name.localeCompare(b.name));
    for (const entry of entries) {
      const fullPath = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (entry.name !== ".vitepress" && entry.name !== "node_modules") {
          walkDir(fullPath);
        }
      } else if (entry.isFile() && entry.name.endsWith(".md")) {
        preprocessDocFile(fullPath, docsDir);
      }
    }
  }

  function preprocessDocFile(fullPath, docsDir) {
    const content = fs.readFileSync(fullPath, "utf8");
    const relToDocs = path.relative(docsDir, fullPath).replace(/\\/g, "/");
    const docSubDir = path.dirname(relToDocs);

    const rewritten = content.replace(
      /```[\s\S]*?```|`[^`\n]+`|(\[(?:[^\]]|\\\])*\]\()([^)]+)(\))/g,
      (match, prefix, url, suffix) => {
        if (match.startsWith("```") || match.startsWith("`")) {
          return match;
        }
        let cleanUrl = url.trim();
        if (
          cleanUrl.startsWith("http://") ||
          cleanUrl.startsWith("https://") ||
          cleanUrl.startsWith("file://") ||
          cleanUrl.startsWith("mailto:") ||
          cleanUrl.startsWith("#") ||
          /^\/(demo|subject-portal)(\/|$)/.test(cleanUrl)
        ) {
          return match;
        }

        let newUrl = cleanUrl;
        if (cleanUrl.startsWith("docs/")) {
          const targetPathInDocs = cleanUrl.substring(5);
          newUrl = path
            .relative(docSubDir, targetPathInDocs)
            .replace(/\\/g, "/");
        } else if (cleanUrl === "LICENSE") {
          newUrl = path.relative(docSubDir, "LICENSE.md").replace(/\\/g, "/");
        }

        const rawTargetClean = cleanUrl.split("#")[0].split("?")[0];
        if (
          rawTargetClean &&
          !cleanUrl.startsWith("../") &&
          !cleanUrl.startsWith("/") &&
          fs.existsSync(path.join(repoRoot, rawTargetClean)) &&
          !fs.existsSync(path.join(path.dirname(fullPath), rawTargetClean))
        ) {
          const anchor = cleanUrl.includes("#")
            ? "#" + cleanUrl.split("#")[1]
            : "";
          const relPath = path
            .relative(
              path.dirname(fullPath),
              path.join(repoRoot, rawTargetClean)
            )
            .replace(/\\/g, "/");
          newUrl = relPath + anchor;
        }

        const targetClean = newUrl.split("#")[0].split("?")[0];
        if (targetClean) {
          const resolvedPath = path.resolve(
            path.dirname(fullPath),
            targetClean
          );
          if (!fs.existsSync(resolvedPath)) {
            console.error(
              `[ERROR] Broken relative link in ${relToDocs}: '${cleanUrl}' resolves to non-existent file '${resolvedPath}'`
            );
            throw new Error(
              `Broken relative link in ${relToDocs}: ${cleanUrl}`
            );
          }
        }

        return `${prefix}${newUrl}${suffix}`;
      }
    );

    if (rewritten !== content) {
      if (!originalFilesMap.has(fullPath)) {
        originalFilesMap.set(fullPath, content);
      }
      fs.writeFileSync(fullPath, rewritten, "utf8");
      console.log(`Preprocessed cross-document links in docs/${relToDocs}`);
    }
  }

  walkDir(docsDir);
}

/**
 * Stages the root README.md, ARCHITECTURE.md, AGENTS.md and LICENSE into docs/ for VitePress.
 * These copies are generated (gitignored). ARCHITECTURE.md and AGENTS.md are staged before
 * README.md so the README's links to them resolve inside the docs site.
 */
function prepareRootDocs() {
  copyAndPreprocess("ARCHITECTURE.md", "ARCHITECTURE.md");
  copyAndPreprocess("AGENTS.md", "AGENTS.md");

  // Convert plain-text LICENSE to docs/LICENSE.md
  const licenseSrcPath = path.join(repoRoot, "LICENSE");
  const licenseDestPath = path.join(repoRoot, "docs", "LICENSE.md");
  if (fs.existsSync(licenseSrcPath)) {
    const licenseContent = fs.readFileSync(licenseSrcPath, "utf8");
    const licenseMarkdown = `# Project License\n\n\`\`\`text\n${licenseContent}\n\`\`\`\n`;
    fs.writeFileSync(licenseDestPath, licenseMarkdown, "utf8");
    console.log(
      `Successfully converted plain-text LICENSE to markdown at docs/LICENSE.md`
    );
  } else {
    console.warn(`Plain-text LICENSE file not found at ${licenseSrcPath}`);
  }

  copyAndPreprocess("README.md", "README.md");
}

// `node scripts/build-docs.js --prepare-only` stages the root documents for `vitepress dev` and exits.
if (process.argv.includes("--prepare-only")) {
  prepareRootDocs();
  process.exit(0);
}

// Run preflight checks before Step 1
runPreflightChecks();

try {
  // 1. Run validation scripts
  console.log("--- Step 1: Pre-Build Validation ---");
  // `cadence ci` and `deploy-docs.yml` set CADENCE_DOCS_SKIP_CHECKS=1 when quality sentinels already passed on
  // this tree: that step runs openapi, schema-doc, and markdown, so running them again here only costs minutes.
  if (process.env.CADENCE_DOCS_SKIP_CHECKS === "1") {
    console.log(
      "Skipping 'openapi', 'schema-doc', and 'markdown' checks (CADENCE_DOCS_SKIP_CHECKS=1: they already passed in this run)."
    );
  } else {
    runCommand("node scripts/run-cli.js check openapi");
    runCommand("node scripts/run-cli.js check schema-doc");
    runCommand("node scripts/run-cli.js check markdown");
  }

  // 2. Validate committed GxP snapshot without mutation
  console.log("--- Step 2: Validating GxP Compliance Snapshot ---");
  runCommand("node scripts/run-cli.js check rtm");

  // 3. Prepare files for VitePress
  console.log("--- Step 3: Preparing Documentation Files ---");
  prepareRootDocs();

  // Sweep and preprocess all docs files and subdirectories
  sweepAndPreprocessDocs(path.join(repoRoot, "docs"));

  // 4. Build VitePress static portal
  console.log("--- Step 4: Compiling VitePress Static Portal ---");
  runCommand(`${pnpmCmd} exec vitepress build docs`);

  console.log("--- Docs Build Completed Successfully! ---");
} catch (error) {
  console.error("Docs build pipeline failed.");
  // exitCode, not process.exit(): exiting here would skip the cleanup in `finally`
  // and leave the staged root docs behind to fail the next run's index check.
  process.exitCode = 1;
} finally {
  console.log("--- Cleanup Temporary Docs ---");
  const tempGeneratedDocs = new Set([
    path.join(repoRoot, "docs", "README.md"),
    path.join(repoRoot, "docs", "ARCHITECTURE.md"),
    path.join(repoRoot, "docs", "AGENTS.md"),
    path.join(repoRoot, "docs", "LICENSE.md"),
  ]);

  for (const [fullPath, originalContent] of originalFilesMap.entries()) {
    if (tempGeneratedDocs.has(fullPath)) {
      continue;
    }
    try {
      fs.writeFileSync(fullPath, originalContent, "utf8");
      console.log(
        `Restored original content for ${path.relative(repoRoot, fullPath)}`
      );
    } catch (err) {
      console.warn(`Could not restore ${fullPath}:`, err);
    }
  }
  // Clean up temporary staged docs files relative to repoRoot
  for (const filePath of tempGeneratedDocs) {
    if (fs.existsSync(filePath)) {
      try {
        fs.unlinkSync(filePath);
        console.log(`Cleaned up ${filePath}`);
      } catch (err) {
        console.warn(`Could not clean up ${filePath}:`, err);
      }
    }
  }
}
