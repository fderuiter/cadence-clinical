const { execSync } = require("child_process");
const fs = require("fs");
const path = require("path");
const { buildVitePressEnv } = require("./build-docs.js");

const repoRoot = path.resolve(__dirname, "..");

function normalizeRootBase(rawBase) {
  const trimmed = String(rawBase || "/cadence-clinical/").trim();
  const withLeading = trimmed.startsWith("/") ? trimmed : `/${trimmed}`;
  return withLeading.endsWith("/") ? withLeading : `${withLeading}/`;
}

function stagePortalDemos({
  rootDir = repoRoot,
  portalBase = process.env.VITEPRESS_BASE || "/cadence-clinical/",
  baseEnv = process.env,
} = {}) {
  const normalizedBase = normalizeRootBase(portalBase);
  const docsDistDir = path.join(rootDir, "docs", ".vitepress", "dist");

  if (!fs.existsSync(path.join(docsDistDir, "index.html"))) {
    throw new Error(
      `VitePress output not found at ${docsDistDir}. Run 'pnpm docs:build' before staging subpath demos.`
    );
  }

  const demoTargets = [
    {
      label: "Staff Demo (apps/web)",
      filter: "web",
      subpath: "demo",
      basePath: `${normalizedBase}demo/`,
      distDir: path.join(rootDir, "apps", "web", "dist"),
      requiredFiles: ["index.html", "mockServiceWorker.js"],
    },
    {
      label: "Participant Demo (apps/subject-portal)",
      filter: "subject-portal",
      subpath: "subject-portal",
      basePath: `${normalizedBase}subject-portal/`,
      distDir: path.join(rootDir, "apps", "subject-portal", "dist"),
      requiredFiles: [
        "index.html",
        "manifest.json",
        "mockServiceWorker.js",
        "sw.js",
      ],
    },
  ];

  for (const target of demoTargets) {
    console.log(
      `--- Building ${target.label} with VITE_BASE_PATH=${target.basePath} ---`
    );
    execSync(`pnpm --filter ${target.filter} build:demo`, {
      cwd: rootDir,
      stdio: "inherit",
      env: {
        ...buildVitePressEnv(baseEnv),
        VITE_BASE_PATH: target.basePath,
      },
    });

    const destDir = path.join(docsDistDir, target.subpath);
    fs.rmSync(destDir, { recursive: true, force: true });
    fs.cpSync(target.distDir, destDir, { recursive: true });

    for (const requiredFile of target.requiredFiles) {
      const stagedFile = path.join(destDir, requiredFile);
      if (!fs.existsSync(stagedFile)) {
        throw new Error(
          `Expected staged demo artifact '${requiredFile}' is missing from ${destDir}`
        );
      }
    }

    const assetsDir = path.join(destDir, "assets");
    const assetEntries = fs.existsSync(assetsDir)
      ? fs.readdirSync(assetsDir)
      : [];
    for (const requiredFontPrefix of [
      "InterVariable",
      "JetBrainsMonoVariable",
    ]) {
      const hasBundledFont = assetEntries.some(
        (entry) =>
          entry.startsWith(requiredFontPrefix) && entry.endsWith(".woff2")
      );
      if (!hasBundledFont) {
        throw new Error(
          `Expected staged demo WOFF2 font '${requiredFontPrefix}*.woff2' is missing from ${assetsDir}`
        );
      }
    }

    console.log(
      `Staged ${target.label} into ${path.relative(rootDir, destDir)}`
    );
  }
}

if (require.main === module) {
  stagePortalDemos();
}

module.exports = { normalizeRootBase, stagePortalDemos };
