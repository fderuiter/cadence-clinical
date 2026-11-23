const { execSync, spawn } = require("child_process");
const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const repoRoot = path.resolve(__dirname, "..");
const docsRoot = path.join(repoRoot, "docs");
const rootDocsModulePath = path.join(docsRoot, ".vitepress", "rootDocs.cjs");
const { STAGED_ROOT_DOCS } = fs.existsSync(rootDocsModulePath)
  ? require(rootDocsModulePath)
  : {
      STAGED_ROOT_DOCS: Object.freeze({
        "README.md": "README.md",
        "ARCHITECTURE.md": "ARCHITECTURE.md",
        "AGENTS.md": "AGENTS.md",
        "CONTEXT.md": "CONTEXT.md",
        "CONTRIBUTING.md": "CONTRIBUTING.md",
        "SECURITY.md": "SECURITY.md",
        "SUPPORT.md": "SUPPORT.md",
        LICENSE: "LICENSE.md",
      }),
    };

const VITEPRESS_ENV_ALLOWLIST = [
  "PATH",
  "PATHEXT",
  "SystemRoot",
  "ComSpec",
  "HOME",
  "USERPROFILE",
  "APPDATA",
  "LOCALAPPDATA",
  "XDG_CACHE_HOME",
  "XDG_CONFIG_HOME",
  "TMPDIR",
  "TEMP",
  "TMP",
  "NODE_ENV",
  "NODE_OPTIONS",
  "VITEPRESS_BASE",
  "CI",
  "TERM",
  "COLORTERM",
  "FORCE_COLOR",
  "NO_COLOR",
  "LANG",
  "LC_ALL",
  "npm_config_user_agent",
  "PNPM_HOME",
  "COREPACK_HOME",
];

function ensureHeapNodeOptions(nodeOptions) {
  if (!nodeOptions || !nodeOptions.includes("--max-old-space-size")) {
    return `${nodeOptions || ""} --max-old-space-size=16384`.trim();
  }
  return nodeOptions;
}

/**
 * Builds the isolated environment passed strictly to the pre-build CLI verification steps
 * (`check openapi`, `check schema-doc`, `check markdown`, `check rtm`), generating ephemeral
 * per-invocation signing secrets when none were supplied by the caller without mutating
 * `process.env` or setting `RTM_DRAFT`.
 * @param {NodeJS.ProcessEnv} [baseEnv=process.env] - Base environment variables.
 * @returns {NodeJS.ProcessEnv} Isolated environment for CLI verification steps.
 */
function buildRtmEnv(baseEnv = process.env) {
  return {
    ...baseEnv,
    NODE_OPTIONS: ensureHeapNodeOptions(baseEnv.NODE_OPTIONS),
    AUDIT_LOG_SECRET_KEY:
      baseEnv.AUDIT_LOG_SECRET_KEY || crypto.randomBytes(32).toString("hex"),
    INBOUND_EMAIL_HMAC_SECRET:
      baseEnv.INBOUND_EMAIL_HMAC_SECRET ||
      crypto.randomBytes(32).toString("hex"),
    GATEWAY_SECRET_KEY:
      baseEnv.GATEWAY_SECRET_KEY || crypto.randomBytes(32).toString("hex"),
  };
}

/**
 * Builds a clean, minimal environment allowlist for VitePress static compilation so compliance
 * signing keys, cloud credentials, and CI tokens are never exposed to `import.meta.env` or SSR plugins.
 * @param {NodeJS.ProcessEnv} [baseEnv=process.env] - Base environment variables.
 * @returns {NodeJS.ProcessEnv} Sanitized environment for `vitepress build docs`.
 */
function buildVitePressEnv(baseEnv = process.env) {
  const cleanEnv = {};
  for (const key of VITEPRESS_ENV_ALLOWLIST) {
    if (baseEnv[key] !== undefined) {
      cleanEnv[key] = baseEnv[key];
    }
  }
  cleanEnv.NODE_OPTIONS = ensureHeapNodeOptions(cleanEnv.NODE_OPTIONS);
  return cleanEnv;
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
 * Executes a shell command from the repository root directory.
 * @param {string} command - The shell command to execute.
 * @param {NodeJS.ProcessEnv} [envOverride] - Optional explicit environment map for the child process.
 */
function runCommand(command, envOverride) {
  console.log(`Running: ${command}`);
  try {
    // Use repoRoot for cwd to keep the execution portable and environment-agnostic
    execSync(command, {
      stdio: "inherit",
      cwd: repoRoot,
      env: envOverride || {
        ...process.env,
        NODE_OPTIONS: ensureHeapNodeOptions(process.env.NODE_OPTIONS),
      },
    });
  } catch (error) {
    console.error(`Command failed: ${command}`);
    throw error;
  }
}

function isExternalOrSamePageHref(url) {
  return (
    url.startsWith("http://") ||
    url.startsWith("https://") ||
    url.startsWith("file://") ||
    url.startsWith("mailto:") ||
    url.startsWith("tel:") ||
    url.startsWith("#") ||
    url.startsWith("/")
  );
}

function splitUrlParts(url) {
  const hashIdx = url.indexOf("#");
  const withoutHash = hashIdx === -1 ? url : url.slice(0, hashIdx);
  const hashPart = hashIdx === -1 ? "" : url.slice(hashIdx);
  const queryIdx = withoutHash.indexOf("?");
  const targetPath =
    queryIdx === -1 ? withoutHash : withoutHash.slice(0, queryIdx);
  const queryPart = queryIdx === -1 ? "" : withoutHash.slice(queryIdx);
  return { targetPath, suffix: `${queryPart}${hashPart}` };
}

function rewriteRootDocHref(cleanUrl) {
  if (isExternalOrSamePageHref(cleanUrl)) {
    return cleanUrl;
  }

  const { targetPath, suffix } = splitUrlParts(cleanUrl);
  if (!targetPath) {
    return cleanUrl;
  }

  const absTarget = path.resolve(repoRoot, decodeURI(targetPath));
  const relToRepo = path.relative(repoRoot, absTarget).replace(/\\/g, "/");

  if (STAGED_ROOT_DOCS[relToRepo] && fs.existsSync(absTarget)) {
    return `${STAGED_ROOT_DOCS[relToRepo]}${suffix}`;
  }

  const relToDocs = path.relative(docsRoot, absTarget).replace(/\\/g, "/");
  if (!relToDocs.startsWith("..") && !path.isAbsolute(relToDocs)) {
    return `${relToDocs}${suffix}`;
  }

  if (fs.existsSync(absTarget)) {
    return `${relToDocs}${suffix}`;
  }

  return cleanUrl;
}

function updateFenceState(line, state) {
  const m = line.trimStart().match(/^(`{3,}|~{3,})(.*)$/);
  if (!m) return false;
  const marker = m[1];
  const rest = m[2].trim();
  if (!state.char) {
    state.char = marker[0];
    state.len = marker.length;
    return true;
  }
  if (marker[0] === state.char && marker.length >= state.len && rest === "") {
    state.char = "";
    state.len = 0;
    return true;
  }
  return false;
}

/**
 * Rewrites relative links inside staged root Markdown documents.
 * @param {string} content - The Markdown file content.
 * @returns {string} The updated Markdown content with corrected links.
 */
function rewriteLinks(content) {
  // Inline links [text](link), ignoring fenced and inline code blocks
  let rewritten = content.replace(
    /(`{3,}|~{3,})[\s\S]*?\1|`[^`\n]+`|(\[(?:[^\]]|\\\])*\]\()([^)]+)(\))/g,
    (match, _fence, prefix, url, suffix) => {
      if (!prefix) {
        return match;
      }
      const cleanUrl = url.trim();
      return `${prefix}${rewriteRootDocHref(cleanUrl)}${suffix}`;
    }
  );

  // Reference links [id]: link (outside fenced code blocks)
  const fenceState = { char: "", len: 0 };
  rewritten = rewritten
    .split("\n")
    .map((line) => {
      if (updateFenceState(line, fenceState) || fenceState.len > 0) {
        return line;
      }
      return line.replace(/^(\[[^\]]+\]:\s*)(\S+)/, (m, prefix, url) => {
        return `${prefix}${rewriteRootDocHref(url.trim())}`;
      });
    })
    .join("\n");

  return rewritten;
}

const originalFilesMap = new Map();

/**
 * Copies a source Markdown file from repo root to docs/ with link preprocessing.
 * @param {string} srcName - The source file name in repo root.
 * @param {string} destName - The destination file name in docs/ folder.
 */
function copyAndPreprocess(srcName, destName) {
  const srcPath = path.join(repoRoot, srcName);
  if (!fs.existsSync(srcPath)) {
    console.warn(`Root document not found at ${srcPath}, skipping staging.`);
    return;
  }
  const destPath = path.join(docsRoot, destName);
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

function rewriteDocsHref(cleanUrl, fullPath, docsDir, relToDocs) {
  if (isExternalOrSamePageHref(cleanUrl)) {
    return cleanUrl;
  }

  const { targetPath, suffix } = splitUrlParts(cleanUrl);
  if (!targetPath) {
    return cleanUrl;
  }

  const fileDir = path.dirname(fullPath);
  const decodedTarget = decodeURI(targetPath);

  let absTarget;
  let needsRewrite = false;

  if (decodedTarget.startsWith("docs/")) {
    absTarget = path.resolve(repoRoot, decodedTarget);
    needsRewrite = true;
  } else if (decodedTarget === "LICENSE") {
    absTarget = path.join(repoRoot, "LICENSE");
    needsRewrite = true;
  } else {
    const fromFileDir = path.resolve(fileDir, decodedTarget);
    const relFromRepo = path
      .relative(repoRoot, fromFileDir)
      .replace(/\\/g, "/");

    if (
      STAGED_ROOT_DOCS[relFromRepo] ||
      fs.existsSync(fromFileDir) ||
      fs.existsSync(`${fromFileDir}.md`)
    ) {
      absTarget = fromFileDir;
    } else if (
      !decodedTarget.startsWith("../") &&
      (fs.existsSync(path.resolve(repoRoot, decodedTarget)) ||
        Boolean(STAGED_ROOT_DOCS[decodedTarget]))
    ) {
      absTarget = path.resolve(repoRoot, decodedTarget);
      needsRewrite = true;
    } else {
      absTarget = fromFileDir;
    }
  }

  const relToRepo = path.relative(repoRoot, absTarget).replace(/\\/g, "/");
  if (STAGED_ROOT_DOCS[relToRepo] && fs.existsSync(absTarget)) {
    const stagedAbs = path.join(docsDir, STAGED_ROOT_DOCS[relToRepo]);
    const relToStaged =
      path.relative(fileDir, stagedAbs).replace(/\\/g, "/") ||
      path.basename(stagedAbs);
    return `${relToStaged}${suffix}`;
  }

  const resolvedExists =
    fs.existsSync(absTarget) || fs.existsSync(`${absTarget}.md`);
  if (!resolvedExists) {
    console.error(
      `[ERROR] Broken relative link in ${relToDocs}: '${cleanUrl}' resolves to non-existent file '${absTarget}'`
    );
    throw new Error(`Broken relative link in ${relToDocs}: ${cleanUrl}`);
  }

  if (needsRewrite) {
    const relPath =
      path.relative(fileDir, absTarget).replace(/\\/g, "/") ||
      path.basename(absTarget);
    return `${relPath}${suffix}`;
  }

  return cleanUrl;
}

function sweepAndPreprocessDocs(docsDir) {
  function walkDir(dir) {
    if (!fs.existsSync(dir)) return;
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    entries.sort((a, b) => a.name.localeCompare(b.name));
    for (const entry of entries) {
      const fullPath = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (
          entry.name !== ".vitepress" &&
          entry.name !== "node_modules" &&
          entry.name !== "runs"
        ) {
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

    let rewritten = content.replace(
      /(`{3,}|~{3,})[\s\S]*?\1|`[^`\n]+`|(\[(?:[^\]]|\\\])*\]\()([^)]+)(\))/g,
      (match, _fence, prefix, url, suffix) => {
        if (!prefix) {
          return match;
        }
        const cleanUrl = url.trim();
        const newUrl = rewriteDocsHref(cleanUrl, fullPath, docsDir, relToDocs);
        return `${prefix}${newUrl}${suffix}`;
      }
    );

    const fenceState = { char: "", len: 0 };
    rewritten = rewritten
      .split("\n")
      .map((line) => {
        if (updateFenceState(line, fenceState) || fenceState.len > 0) {
          return line;
        }
        return line.replace(/^(\[[^\]]+\]:\s*)(\S+)/, (m, prefix, url) => {
          const newUrl = rewriteDocsHref(
            url.trim(),
            fullPath,
            docsDir,
            relToDocs
          );
          return `${prefix}${newUrl}`;
        });
      })
      .join("\n");

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
 * Stages all repository-root guides, policies, and LICENSE into docs/ for VitePress.
 * These copies are generated (gitignored) and removed after `pnpm docs:build`.
 */
function prepareRootDocs() {
  const entries = Object.entries(STAGED_ROOT_DOCS);
  const nonReadmeEntries = entries.filter(([src]) => src !== "README.md");
  const readmeEntry = entries.find(([src]) => src === "README.md");

  for (const [srcName, destName] of nonReadmeEntries) {
    if (srcName === "LICENSE") {
      const licenseSrcPath = path.join(repoRoot, srcName);
      const licenseDestPath = path.join(docsRoot, destName);
      if (fs.existsSync(licenseSrcPath)) {
        const licenseContent = fs.readFileSync(licenseSrcPath, "utf8");
        const licenseMarkdown = `# Project License\n\n\`\`\`text\n${licenseContent}\n\`\`\`\n`;
        fs.writeFileSync(licenseDestPath, licenseMarkdown, "utf8");
        console.log(
          `Successfully converted plain-text LICENSE to markdown at docs/${destName}`
        );
      } else {
        console.warn(`Plain-text LICENSE file not found at ${licenseSrcPath}`);
      }
      continue;
    }

    copyAndPreprocess(srcName, destName);
  }

  if (readmeEntry) {
    copyAndPreprocess(readmeEntry[0], readmeEntry[1]);
  }
}

function getStagedRootDocPaths() {
  return new Set(
    Object.values(STAGED_ROOT_DOCS).map((destName) =>
      path.join(docsRoot, destName)
    )
  );
}

function cleanupStagedRootDocs() {
  for (const filePath of getStagedRootDocPaths()) {
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

function runDevServer(extraArgs) {
  runPreflightChecks();
  prepareRootDocs();

  let cleanedUp = false;
  const cleanupOnce = () => {
    if (cleanedUp) {
      return;
    }
    cleanedUp = true;
    console.log("--- Cleanup Temporary Docs ---");
    cleanupStagedRootDocs();
  };

  process.on("exit", cleanupOnce);

  const isWindows = process.platform === "win32";
  const useNpx = pnpmCmd === "npx pnpm";
  const command = useNpx ? "npx" : "pnpm";
  const commandArgs = useNpx
    ? ["pnpm", "exec", "vitepress", "dev", "docs", ...extraArgs]
    : ["exec", "vitepress", "dev", "docs", ...extraArgs];

  const child = spawn(command, commandArgs, {
    cwd: repoRoot,
    stdio: "inherit",
    env: buildVitePressEnv(process.env),
    shell: isWindows,
    detached: !isWindows,
  });

  const terminateChildTree = (signal) => {
    if (child.exitCode !== null || child.signalCode !== null) {
      return false;
    }
    try {
      if (!isWindows && child.pid) {
        process.kill(-child.pid, signal);
      } else {
        child.kill(signal);
      }
      return true;
    } catch {
      try {
        child.kill(signal);
        return true;
      } catch {
        return false;
      }
    }
  };

  let receivedSignal = null;
  const handleSignal = (signal) => {
    receivedSignal = signal;
    if (terminateChildTree(signal)) {
      setTimeout(() => {
        terminateChildTree("SIGKILL");
        cleanupOnce();
        process.exit(0);
      }, 3000).unref();
    } else {
      cleanupOnce();
      process.exit(0);
    }
  };

  process.on("SIGINT", () => handleSignal("SIGINT"));
  process.on("SIGTERM", () => handleSignal("SIGTERM"));

  child.on("error", (err) => {
    console.error("Failed to start VitePress dev server:", err);
    cleanupOnce();
    process.exit(1);
  });

  child.on("close", (code, signal) => {
    cleanupOnce();
    if (receivedSignal || signal === "SIGINT" || signal === "SIGTERM") {
      process.exit(0);
    }
    process.exit(code ?? 0);
  });
}

if (require.main !== module) {
  module.exports = {
    rewriteLinks,
    buildRtmEnv,
    buildVitePressEnv,
    VITEPRESS_ENV_ALLOWLIST,
  };
} else {
  // `node scripts/build-docs.js --prepare-only` stages the root documents for `vitepress dev` and exits.
  if (process.argv.includes("--prepare-only")) {
    prepareRootDocs();
    process.exit(0);
  }

  if (process.argv.includes("--dev")) {
    const extraArgs = process.argv
      .slice(2)
      .filter((arg) => arg !== "--dev" && arg !== "--");
    runDevServer(extraArgs);
  } else {
    // Run preflight checks before Step 1
    runPreflightChecks();

    const cliEnv = buildRtmEnv(process.env);

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
        runCommand("node scripts/run-cli.js check openapi", cliEnv);
        runCommand("node scripts/run-cli.js check schema-doc", cliEnv);
        runCommand("node scripts/run-cli.js check markdown", cliEnv);
      }

      // 2. Validate committed GxP snapshot without mutation
      console.log("--- Step 2: Validating GxP Compliance Snapshot ---");
      runCommand("node scripts/run-cli.js check rtm", cliEnv);

      // 3. Prepare files for VitePress
      console.log("--- Step 3: Preparing Documentation Files ---");
      prepareRootDocs();

      // Sweep and preprocess all docs files and subdirectories
      sweepAndPreprocessDocs(docsRoot);

      // 4. Build VitePress static portal with a strict non-secret environment allowlist
      console.log("--- Step 4: Compiling VitePress Static Portal ---");
      runCommand(
        `${pnpmCmd} exec vitepress build docs`,
        buildVitePressEnv(process.env)
      );

      console.log("--- Docs Build Completed Successfully! ---");
    } catch (error) {
      console.error("Docs build pipeline failed.");
      // exitCode, not process.exit(): exiting here would skip the cleanup in `finally`
      // and leave the staged root docs behind to fail the next run's index check.
      process.exitCode = 1;
    } finally {
      console.log("--- Cleanup Temporary Docs ---");
      const tempGeneratedDocs = getStagedRootDocPaths();

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
      cleanupStagedRootDocs();
    }
  }
}
