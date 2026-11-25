// Dedicated CSS custom-property definition resolver for root ESLint rules.
// Extracted from eslint.config.mjs so stylesheet token lookup and VitePress
// theme variable resolution evolve independently of the root ESLint rule table.

import fs from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";

const require = createRequire(import.meta.url);

export const CSS_VAR_DEF_REGEX = /(--[a-zA-Z0-9_-]+)\s*:/g;

const VITEPRESS_RUNTIME_SLOT_VARS = [
  "--vp-layout-top-height",
  "--vp-doc-top-height",
];

const definedVarsFileCache = new Map();

export function extractDefinedVarsFromFile(filePath) {
  if (!fs.existsSync(filePath)) {
    return new Set();
  }

  const { mtimeMs } = fs.statSync(filePath);
  const cached = definedVarsFileCache.get(filePath);
  if (cached && cached.mtimeMs === mtimeMs) {
    return new Set(cached.vars);
  }

  const vars = new Set();
  const raw = fs
    .readFileSync(filePath, "utf8")
    .replace(/\/\*[\s\S]*?\*\//g, "");
  for (const match of raw.matchAll(CSS_VAR_DEF_REGEX)) {
    vars.add(match[1]);
  }

  definedVarsFileCache.set(filePath, { mtimeMs, vars });
  return new Set(vars);
}

function resolveVitepressVarsCssPath(repoRoot) {
  try {
    return path.join(
      path.dirname(require.resolve("vitepress/package.json")),
      "dist/client/theme-default/styles/vars.css"
    );
  } catch {
    return path.join(
      repoRoot,
      "node_modules/vitepress/dist/client/theme-default/styles/vars.css"
    );
  }
}

export function createBaseDefinedVarsResolver(repoRoot) {
  const tokensCssPath = path.join(repoRoot, "packages/ui/tokens.css");
  const webStyleCssPath = path.join(repoRoot, "apps/web/src/style.css");
  const portalStyleCssPath = path.join(
    repoRoot,
    "apps/subject-portal/src/style.css"
  );
  const vitepressCustomCssPath = path.join(
    repoRoot,
    "docs/.vitepress/theme/custom.css"
  );
  const vitepressVarsCssPath = resolveVitepressVarsCssPath(repoRoot);

  return function getBaseDefinedVars(filename) {
    const normalized = String(filename || "")
      .split(path.sep)
      .join("/")
      .replace(/\\/g, "/");
    const vars = extractDefinedVarsFromFile(tokensCssPath);

    if (
      normalized.includes("/apps/web/") ||
      normalized.startsWith("apps/web/")
    ) {
      for (const v of extractDefinedVarsFromFile(webStyleCssPath)) {
        vars.add(v);
      }
    } else if (
      normalized.includes("/apps/subject-portal/") ||
      normalized.startsWith("apps/subject-portal/")
    ) {
      for (const v of extractDefinedVarsFromFile(portalStyleCssPath)) {
        vars.add(v);
      }
    } else if (
      normalized.includes("/docs/.vitepress/") ||
      normalized.startsWith("docs/.vitepress/")
    ) {
      for (const v of extractDefinedVarsFromFile(vitepressVarsCssPath)) {
        vars.add(v);
      }
      for (const v of extractDefinedVarsFromFile(vitepressCustomCssPath)) {
        vars.add(v);
      }
      for (const v of VITEPRESS_RUNTIME_SLOT_VARS) {
        vars.add(v);
      }
    }

    return vars;
  };
}
