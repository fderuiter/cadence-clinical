import { describe, it, expect } from "vitest";
import fs from "fs";
import path from "path";
import * as rootUi from "../index.js";

const uiDir = path.resolve(__dirname, "..");
const repoRoot = path.resolve(uiDir, "../..");
const webSrcDir = path.join(repoRoot, "apps/web/src");
const portalSrcDir = path.join(repoRoot, "apps/subject-portal/src");
const docsComponentsDir = path.join(repoRoot, "docs/components");

const EXPECTED_KEYBOARD_HELPERS = [
  "useFocusTrap",
  "hasActiveModalFocusTrap",
  "getTopActiveModalContainer",
  "getFocusableElements",
  "useEscapeClose",
];

function walkFiles(dir, extensions, ignoreDirs = new Set()) {
  const results = [];
  if (!fs.existsSync(dir)) return results;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (ignoreDirs.has(entry.name)) continue;
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      results.push(...walkFiles(fullPath, extensions, ignoreDirs));
    } else if (extensions.some((ext) => entry.name.endsWith(ext))) {
      results.push(fullPath);
    }
  }
  return results;
}

function parseCssBlockVars(cssText, blockSelectorRegex) {
  const match = cssText.match(blockSelectorRegex);
  if (!match) return {};
  const startBrace = cssText.indexOf("{", match.index);
  let depth = 0;
  let endBrace = startBrace;
  for (let i = startBrace; i < cssText.length; i++) {
    if (cssText[i] === "{") depth++;
    else if (cssText[i] === "}") {
      depth--;
      if (depth === 0) {
        endBrace = i;
        break;
      }
    }
  }
  const body = cssText.slice(startBrace + 1, endBrace);
  const vars = {};
  const declRe = /(--[a-zA-Z0-9-]+)\s*:\s*([^;]+);/g;
  let m;
  while ((m = declRe.exec(body)) !== null) {
    vars[m[1]] = m[2].trim().replace(/\s+/g, " ");
  }
  return vars;
}

function resolveVar(name, themeMap, seen = new Set()) {
  if (seen.has(name)) {
    throw new Error(`Circular CSS variable reference for ${name}`);
  }
  const raw = themeMap[name];
  if (!raw) return null;
  const varMatch = raw.match(
    /^var\(\s*(--[a-zA-Z0-9-]+)(?:\s*,\s*([^)]+))?\)$/
  );
  if (varMatch) {
    const nextSeen = new Set(seen);
    nextSeen.add(name);
    return (
      resolveVar(varMatch[1], themeMap, nextSeen) ?? varMatch[2]?.trim() ?? null
    );
  }
  return raw;
}

function hexToRgb(hex) {
  const clean = hex.trim().replace(/^#/, "");
  const full =
    clean.length === 3
      ? clean
          .split("")
          .map((c) => c + c)
          .join("")
      : clean;
  if (!/^[0-9a-fA-F]{6}$/.test(full)) {
    throw new Error(`Unsupported hex color: ${hex}`);
  }
  return [
    parseInt(full.slice(0, 2), 16),
    parseInt(full.slice(2, 4), 16),
    parseInt(full.slice(4, 6), 16),
  ];
}

function relativeLuminance([r, g, b]) {
  const toLinear = (c) => {
    const s = c / 255;
    return s <= 0.04045 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  };
  const [R, G, B] = [toLinear(r), toLinear(g), toLinear(b)];
  return 0.2126 * R + 0.7152 * G + 0.0722 * B;
}

function contrastRatio(hexA, hexB) {
  const l1 = relativeLuminance(hexToRgb(hexA));
  const l2 = relativeLuminance(hexToRgb(hexB));
  const lighter = Math.max(l1, l2);
  const darker = Math.min(l1, l2);
  return (lighter + 0.05) / (darker + 0.05);
}

function extractVarUsagesWithoutFallback(text) {
  const usages = [];
  const marker = "var(--";
  let idx = 0;
  while ((idx = text.indexOf(marker, idx)) !== -1) {
    let pos = idx + 4; // starts at --
    let depth = 1;
    while (pos < text.length && depth > 0) {
      if (text[pos] === "(") depth++;
      else if (text[pos] === ")") depth--;
      pos++;
    }
    if (depth === 0) {
      const inner = text.slice(idx + 4, pos - 1).trim();
      if (!inner.includes(",")) {
        const varNameMatch = inner.match(/^(--[a-zA-Z0-9-]+)/);
        if (varNameMatch) {
          usages.push(varNameMatch[1]);
        }
      }
    }
    idx = pos;
  }
  return usages;
}

function parseRuleProps(cssText, selectorRegex) {
  const match = cssText.match(selectorRegex);
  if (!match) return null;
  const openBrace = cssText.indexOf("{", match.index);
  const closeBrace = cssText.indexOf("}", openBrace);
  const body = cssText.slice(openBrace + 1, closeBrace);
  const props = {};
  const declRe = /([a-zA-Z-]+)\s*:\s*([^;]+);/g;
  let m;
  while ((m = declRe.exec(body)) !== null) {
    props[m[1].trim()] = m[2].replace(/!important/g, "").trim();
  }
  return props;
}

function resolveCssColorExpr(expr, themeMap) {
  if (!expr) return null;
  if (expr.startsWith("#")) return expr;
  const varMatch = expr.match(
    /^var\(\s*(--[a-zA-Z0-9-]+)(?:\s*,\s*([^)]+))?\)$/
  );
  if (varMatch) {
    return resolveVar(varMatch[1], themeMap) ?? varMatch[2]?.trim() ?? null;
  }
  return expr;
}

describe("Design System Harmony, Exports, Token Resolution & WCAG AA Contrast", () => {
  describe("Requirement 1: Complete Root Package Exports & Type Definitions", () => {
    const discoveredVueNames = walkFiles(path.join(uiDir, "src/components"), [
      ".vue",
    ])
      .map((f) => path.basename(f, ".vue"))
      .sort();

    it("exposes every shared Vue component in src/components/ from root index.js with zero unexported SFCs", () => {
      expect(discoveredVueNames.length).toBeGreaterThanOrEqual(30);
      expect(new Set(discoveredVueNames).size).toBe(discoveredVueNames.length);
      expect(Object.keys(rootUi.UI_COMPONENTS)).toEqual(discoveredVueNames);
      for (const name of discoveredVueNames) {
        expect(rootUi[name], `root index.js missing ${name}`).toBeDefined();
      }
    });

    it("exposes all 5 keyboard interaction helpers, createSrOnlyActionButton, and useAsyncRetry from root index.js", () => {
      expect(EXPECTED_KEYBOARD_HELPERS).toHaveLength(5);
      for (const helperName of [
        ...EXPECTED_KEYBOARD_HELPERS,
        "createSrOnlyActionButton",
        "useAsyncRetry",
      ]) {
        expect(
          rootUi[helperName],
          `root index.js missing ${helperName}`
        ).toBeTypeOf("function");
      }
    });

    it("declares complete TypeScript definitions for all discovered components and helpers in generated dist/index.d.ts", () => {
      const dtsPath = path.join(uiDir, "dist/index.d.ts");
      expect(fs.existsSync(dtsPath)).toBe(true);
      expect(fs.existsSync(path.join(uiDir, "index.d.ts"))).toBe(false);
      const dts = fs.readFileSync(dtsPath, "utf-8");
      for (const componentName of discoveredVueNames) {
        expect(dts).toMatch(
          new RegExp(`export (?:declare )?const ${componentName}:`)
        );
      }
      for (const helperName of [
        ...EXPECTED_KEYBOARD_HELPERS,
        "handoffFocus",
        "useInlineFocusHandoff",
        "handoffRetryFocus",
        "useRetryFocusHandoff",
        "createSrOnlyActionButton",
        "useAsyncRetry",
        "registerEntityPeekHandler",
        "parseNodeRef",
        "requestStepUpSignature",
      ]) {
        expect(dts).toMatch(
          new RegExp(`export (?:declare )?(?:function|const) ${helperName}\\b`)
        );
      }
    });
  });

  describe("Requirement 2: Documentation Package Naming & Zero Duplicated Controls", () => {
    it('uses active package name "ui" with zero stale "@cadence/ui" imports across component docs and README', () => {
      const docFiles = [
        path.join(uiDir, "README.md"),
        ...walkFiles(docsComponentsDir, [".md"]),
      ];
      expect(docFiles.length).toBeGreaterThanOrEqual(6);

      for (const docPath of docFiles) {
        const content = fs.readFileSync(docPath, "utf-8");
        expect(
          content,
          `Found stale @cadence/ui in ${path.relative(repoRoot, docPath)}`
        ).not.toContain("@cadence/ui");
      }

      const indexDoc = fs.readFileSync(
        path.join(docsComponentsDir, "index.md"),
        "utf-8"
      );
      expect(indexDoc).toContain('@import "ui/tokens.css"');

      const modalDoc = fs.readFileSync(
        path.join(docsComponentsDir, "modal.md"),
        "utf-8"
      );
      expect(modalDoc).toContain('from "ui"');
    });

    it("retains zero duplicated EmptyState.vue, useFocusTrap.js, or useEscapeClose.js copies in apps/web", () => {
      const webFiles = walkFiles(webSrcDir, [".vue", ".js", ".ts"]);
      const forbiddenNames = new Set([
        "EmptyState.vue",
        "useFocusTrap.js",
        "useFocusTrap.ts",
        "useEscapeClose.js",
        "useEscapeClose.ts",
      ]);
      const duplicates = webFiles.filter((f) =>
        forbiddenNames.has(path.basename(f))
      );
      expect(duplicates).toEqual([]);
    });
  });

  describe("Requirement 3 & Guardrails: Standardized Semantic Aliases & ADR-2166 / ADR-2635", () => {
    const tokensCss = fs.readFileSync(path.join(uiDir, "tokens.css"), "utf-8");
    const responsiveCss = fs.readFileSync(
      path.join(uiDir, "responsive.css"),
      "utf-8"
    );
    const webStyleCss = fs.readFileSync(
      path.join(webSrcDir, "style.css"),
      "utf-8"
    );
    const portalStyleCss = fs.readFileSync(
      path.join(portalSrcDir, "style.css"),
      "utf-8"
    );

    it("centralizes shared :root semantic aliases, dark-mode mirrors, resets, focus outlines, and utilities in packages/ui", () => {
      const lightTokens = parseCssBlockVars(tokensCss, /:root\s*\{/);
      const darkTokens = parseCssBlockVars(
        tokensCss,
        /:root\[data-theme="dark"\][\s\S]*?\{/
      );
      const autoDarkMatch = tokensCss.match(
        /@media\s*\(prefers-color-scheme:\s*dark\)\s*\{[\s\S]*?:root\[data-theme="auto"\]\s*\{([\s\S]*?)\}/
      );
      expect(autoDarkMatch).not.toBeNull();
      const autoDarkBody = autoDarkMatch[1];

      const webAliases = parseCssBlockVars(webStyleCss, /:root\s*\{/);
      const portalAliases = parseCssBlockVars(portalStyleCss, /:root\s*\{/);

      const requiredSharedAliases = [
        "--primary",
        "--primary-dark",
        "--primary-light",
        "--accent",
        "--accent-light",
        "--accent-bg",
        "--text-main",
        "--text-muted",
        "--bg-main",
        "--bg-card",
        "--neutral-dark",
        "--neutral-light",
        "--border",
        "--border-control",
        "--success",
        "--success-bg",
        "--warning",
        "--warning-bg",
        "--error",
        "--error-bg",
        "--danger",
        "--danger-bg",
        "--font",
      ];

      for (const alias of requiredSharedAliases) {
        expect(
          lightTokens[alias],
          `tokens.css :root missing ${alias}`
        ).toBeDefined();
        expect(lightTokens[alias]).toMatch(/^var\(--semantic-/);
        if (alias !== "--font") {
          expect(
            darkTokens[alias],
            `tokens.css dark theme missing ${alias}`
          ).toBe(lightTokens[alias]);
          expect(autoDarkBody).toContain(`${alias}: ${lightTokens[alias]};`);
        }
        expect(
          webAliases[alias],
          `apps/web/src/style.css must not duplicate ${alias}`
        ).toBeUndefined();
        expect(
          portalAliases[alias],
          `apps/subject-portal/src/style.css must not duplicate ${alias}`
        ).toBeUndefined();
      }

      expect(parseRuleProps(tokensCss, /\*\s*\{/)).toMatchObject({
        "box-sizing": "border-box",
        margin: "0",
        padding: "0",
      });
      expect(parseRuleProps(tokensCss, /\.form-actions\s*\{/)).toMatchObject({
        display: "flex",
        "justify-content": "flex-end",
      });
      expect(tokensCss).toMatch(
        /:where\(\[role="button"\],\s*\[tabindex\]:not\(\[tabindex="-1"\]\)\):focus-visible\s*\{/
      );
      expect(tokensCss).toMatch(
        /:where\(\s*:is\(\s*button,\s*\[role="button"\],\s*summary,/
      );
      expect(responsiveCss).toMatch(
        /\.touch-target,\s*\.min-touch-target\s*\{/
      );
      expect(responsiveCss).toMatch(/\.touch-target-interactive\s*\{/);

      for (const [label, css] of [
        ["apps/web/src/style.css", webStyleCss],
        ["apps/subject-portal/src/style.css", portalStyleCss],
      ]) {
        expect(css, `${label} must import tokens.css`).toContain(
          '@import "../../../packages/ui/tokens.css";'
        );
        expect(css, `${label} must import responsive.css`).toContain(
          '@import "../../../packages/ui/responsive.css";'
        );
        expect(css, `${label} must not duplicate * reset`).not.toMatch(
          /(?:^|\n)\*\s*\{/
        );
        expect(css, `${label} must not duplicate .form-actions`).not.toMatch(
          /\.form-actions\s*\{/
        );
        expect(css, `${label} must not duplicate .btn-primary`).not.toMatch(
          /(?:^|\n)\.btn-primary\s*\{/
        );
        expect(css, `${label} must not redeclare .touch-target`).not.toMatch(
          /\.(?:min-)?touch-target(?:-interactive)?\b/
        );
      }
    });

    it("preserves ADR-2166 runtime custom properties and 48px minimum touch targets", () => {
      const lightVars = parseCssBlockVars(tokensCss, /:root\s*\{/);
      expect(lightVars["--touch-target-min"]).toBe("48px");
      expect(lightVars["--semantic-touch-target-min"]).toBe(
        "var(--touch-target-min)"
      );
    });

    it("reserves --semantic-color-ai* exclusively for AI content and never workflow/validation states", () => {
      for (const css of [tokensCss, webStyleCss, portalStyleCss]) {
        const statusBlocks =
          css.match(
            /\.(status-[a-z-]+|validation-[a-z-]+|sync-badge|has-error|lookup-[a-z-]+)[^{]*\{[^}]*\}/g
          ) || [];
        for (const block of statusBlocks) {
          expect(block).not.toContain("--semantic-color-ai");
        }
      }

      const sourceFiles = [
        ...walkFiles(path.join(uiDir, "src"), [".vue", ".css", ".js", ".ts"]),
        ...walkFiles(webSrcDir, [".vue", ".css", ".js", ".ts"]),
        ...walkFiles(portalSrcDir, [".vue", ".css", ".js", ".ts"]),
      ];
      const unauthorizedAiTokenFiles = [];
      for (const file of sourceFiles) {
        const rel = path.relative(repoRoot, file).split(path.sep).join("/");
        const isGovernedAi =
          rel.startsWith("apps/web/src/components/ai/") ||
          /^packages\/ui\/src\/components\/clinical\/Ai[^/]*\.vue$/.test(rel);
        if (isGovernedAi) continue;
        const text = fs
          .readFileSync(file, "utf-8")
          .replace(/\/\*[\s\S]*?\*\//g, "");
        if (/--(?:semantic-)?color-ai(?:-[a-zA-Z0-9_-]+)?\b/.test(text)) {
          unauthorizedAiTokenFiles.push(rel);
        }
      }
      expect(unauthorizedAiTokenFiles).toEqual([]);
    });
  });

  describe("Requirement 4: Zero Unresolved CSS Variable References Without Fallbacks", () => {
    const tokensCss = fs.readFileSync(path.join(uiDir, "tokens.css"), "utf-8");
    const responsiveCss = fs.readFileSync(
      path.join(uiDir, "responsive.css"),
      "utf-8"
    );
    const webStyleCss = fs.readFileSync(
      path.join(webSrcDir, "style.css"),
      "utf-8"
    );
    const portalStyleCss = fs.readFileSync(
      path.join(portalSrcDir, "style.css"),
      "utf-8"
    );

    function collectDefinedVars(texts) {
      const defined = new Set();
      const defRe = /(--[a-zA-Z0-9-]+)\s*:/g;
      for (const text of texts) {
        let m;
        while ((m = defRe.exec(text)) !== null) {
          defined.add(m[1]);
        }
      }
      return defined;
    }

    it("reports zero unresolved CSS variable references across packages/ui", () => {
      const uiDefined = collectDefinedVars([tokensCss, responsiveCss]);
      const uiFiles = [
        path.join(uiDir, "index.js"),
        path.join(uiDir, "tokens.css"),
        path.join(uiDir, "responsive.css"),
        ...walkFiles(path.join(uiDir, "src"), [".js", ".ts", ".vue", ".css"]),
      ];

      const unresolved = [];
      for (const file of uiFiles) {
        const content = fs.readFileSync(file, "utf-8");
        const localDefs = collectDefinedVars([content]);
        for (const varName of extractVarUsagesWithoutFallback(content)) {
          if (!uiDefined.has(varName) && !localDefs.has(varName)) {
            unresolved.push(`${path.relative(repoRoot, file)} -> ${varName}`);
          }
        }
      }
      expect(unresolved).toEqual([]);
    });

    it("reports zero unresolved CSS variable references across apps/subject-portal/src", () => {
      const portalDefined = collectDefinedVars([
        tokensCss,
        responsiveCss,
        portalStyleCss,
      ]);
      const portalFiles = walkFiles(portalSrcDir, [
        ".js",
        ".ts",
        ".vue",
        ".css",
      ]);

      const unresolved = [];
      for (const file of portalFiles) {
        const content = fs.readFileSync(file, "utf-8");
        const localDefs = collectDefinedVars([content]);
        for (const varName of extractVarUsagesWithoutFallback(content)) {
          if (!portalDefined.has(varName) && !localDefs.has(varName)) {
            unresolved.push(`${path.relative(repoRoot, file)} -> ${varName}`);
          }
        }
      }
      expect(unresolved).toEqual([]);
    });

    it("reports zero unresolved CSS variable references across apps/web/src", () => {
      const webDefined = collectDefinedVars([
        tokensCss,
        responsiveCss,
        webStyleCss,
      ]);
      const webFiles = walkFiles(webSrcDir, [".js", ".ts", ".vue", ".css"]);

      const unresolved = [];
      for (const file of webFiles) {
        const content = fs.readFileSync(file, "utf-8");
        const localDefs = collectDefinedVars([content]);
        for (const varName of extractVarUsagesWithoutFallback(content)) {
          if (!webDefined.has(varName) && !localDefs.has(varName)) {
            unresolved.push(`${path.relative(repoRoot, file)} -> ${varName}`);
          }
        }
      }
      expect(unresolved).toEqual([]);
    });
  });

  describe("Requirement 5 & 6: Paired Light/Dark Themes & >= 4.5:1 WCAG 2.1 AA Contrast", () => {
    const tokensCss = fs.readFileSync(path.join(uiDir, "tokens.css"), "utf-8");
    const webStyleCss = fs.readFileSync(
      path.join(webSrcDir, "style.css"),
      "utf-8"
    );
    const portalStyleCss = fs.readFileSync(
      path.join(portalSrcDir, "style.css"),
      "utf-8"
    );

    const lightTheme = parseCssBlockVars(tokensCss, /:root\s*\{/);
    const darkThemeOverrides = parseCssBlockVars(
      tokensCss,
      /:root\[data-theme="dark"\][\s\S]*?\{/
    );
    const darkTheme = { ...lightTheme, ...darkThemeOverrides };

    const semanticContrastPairs = [
      // Core text roles on surfaces
      ["--semantic-color-text", "--semantic-color-surface"],
      ["--semantic-color-text", "--semantic-color-surface-muted"],
      ["--semantic-color-text-muted", "--semantic-color-surface"],
      ["--semantic-color-text-muted", "--semantic-color-surface-muted"],
      ["--semantic-color-text-muted", "--semantic-color-surface-header"],
      ["--semantic-color-text-dark", "--semantic-color-surface"],
      ["--semantic-color-text-dark", "--semantic-color-surface-muted"],
      ["--semantic-color-text-body", "--semantic-color-surface"],
      ["--semantic-color-text-body", "--semantic-color-surface-muted"],
      ["--semantic-color-text-secondary", "--semantic-color-surface"],
      ["--semantic-color-text-secondary", "--semantic-color-surface-muted"],
      ["--semantic-color-text-subtle", "--semantic-color-surface"],
      ["--semantic-color-text-subtle", "--semantic-color-surface-muted"],

      // Brand & interactive roles
      ["--semantic-color-primary", "--semantic-color-surface"],
      ["--semantic-color-primary", "--semantic-color-primary-light"],
      ["--semantic-color-text-on-primary", "--semantic-color-primary"],
      ["--semantic-color-primary-dark", "--semantic-color-surface"],
      ["--semantic-color-primary-dark", "--semantic-color-primary-light"],
      ["--semantic-color-accent", "--semantic-color-surface"],
      ["--semantic-color-accent", "--semantic-color-accent-bg"],
      ["--semantic-color-interactive-primary", "--semantic-color-surface"],
      ["--semantic-color-interactive-brand", "--semantic-color-surface"],

      // Validation, info, warning, success, error & AI badge/banner pairs
      ["--semantic-color-success", "--semantic-color-surface"],
      ["--semantic-color-success", "--semantic-color-success-bg"],
      ["--semantic-color-success-dark", "--semantic-color-surface"],
      ["--semantic-color-success-dark", "--semantic-color-success-bg"],
      ["--semantic-color-warning", "--semantic-color-surface"],
      ["--semantic-color-warning", "--semantic-color-warning-bg"],
      ["--semantic-color-warning-text", "--semantic-color-warning-bg"],
      ["--semantic-color-warning-text", "--semantic-color-warning-bg-light"],
      ["--semantic-color-warning-text", "--semantic-color-warning-bg-badge"],
      ["--semantic-color-warning-text-dark", "--semantic-color-warning-bg"],
      [
        "--semantic-color-warning-text-dark",
        "--semantic-color-warning-bg-light",
      ],
      [
        "--semantic-color-warning-text-dark",
        "--semantic-color-warning-bg-badge",
      ],
      ["--semantic-color-error", "--semantic-color-surface"],
      ["--semantic-color-error", "--semantic-color-error-bg"],
      ["--semantic-color-error-dark", "--semantic-color-surface"],
      ["--semantic-color-error-dark", "--semantic-color-error-bg"],
      ["--semantic-color-info", "--semantic-color-surface"],
      ["--semantic-color-info", "--semantic-color-info-bg"],
      ["--semantic-color-info", "--semantic-color-info-light"],
      ["--semantic-color-ai", "--semantic-color-surface"],
      ["--semantic-color-ai", "--semantic-color-ai-bg"],

      // Solid action/badge pairs
      ["--semantic-color-text-on-solid", "--semantic-color-warning-dark"],
      ["--semantic-color-text-on-solid", "--semantic-color-warning-action"],
      [
        "--semantic-color-text-on-solid",
        "--semantic-color-success-interactive",
      ],
      ["--semantic-color-text-on-solid", "--semantic-color-success-accent"],
      ["--semantic-color-text-on-solid", "--semantic-color-wallet-start"],
      ["--semantic-color-text-on-solid", "--semantic-color-wallet-end"],

      // Categorical & GxP status / readiness badge pairs (ADR-2654)
      ["--semantic-color-teal-text", "--semantic-color-teal-bg"],
      ["--semantic-color-indigo-text", "--semantic-color-indigo-bg"],
      ["--semantic-color-purple-text", "--semantic-color-purple-bg"],
      ["--semantic-badge-draft-text", "--semantic-badge-draft-bg"],
      ["--semantic-badge-verified-text", "--semantic-badge-verified-bg"],
      ["--semantic-badge-queried-text", "--semantic-badge-queried-bg"],
      ["--semantic-badge-frozen-text", "--semantic-badge-frozen-bg"],
      ["--semantic-badge-signed-text", "--semantic-badge-signed-bg"],
      ["--semantic-badge-locked-text", "--semantic-badge-locked-bg"],
      ["--semantic-badge-warning-text", "--semantic-badge-warning-bg"],
      ["--semantic-badge-error-text", "--semantic-badge-error-bg"],
    ];

    it("achieves >= 4.5:1 WCAG AA contrast across all shared semantic pairs in light theme", () => {
      for (const [fgVar, bgVar] of semanticContrastPairs) {
        const fgHex = resolveVar(fgVar, lightTheme);
        const bgHex = resolveVar(bgVar, lightTheme);
        expect(fgHex, `Missing light ${fgVar}`).toMatch(/^#[0-9a-fA-F]{3,6}$/);
        expect(bgHex, `Missing light ${bgVar}`).toMatch(/^#[0-9a-fA-F]{3,6}$/);
        const ratio = contrastRatio(fgHex, bgHex);
        expect(
          ratio,
          `Light contrast ${fgVar} (${fgHex}) on ${bgVar} (${bgHex}) = ${ratio.toFixed(2)}:1`
        ).toBeGreaterThanOrEqual(4.5);
      }
    });

    it("defines paired dark-theme semantic tokens and achieves >= 4.5:1 WCAG AA contrast in dark theme", () => {
      expect(Object.keys(darkThemeOverrides).length).toBeGreaterThan(30);
      expect(tokensCss).toContain("@media (prefers-color-scheme: dark)");
      expect(tokensCss).toContain(':root[data-theme="auto"]');

      for (const [fgVar, bgVar] of semanticContrastPairs) {
        const fgHex = resolveVar(fgVar, darkTheme);
        const bgHex = resolveVar(bgVar, darkTheme);
        expect(fgHex, `Missing dark ${fgVar}`).toMatch(/^#[0-9a-fA-F]{3,6}$/);
        expect(bgHex, `Missing dark ${bgVar}`).toMatch(/^#[0-9a-fA-F]{3,6}$/);
        const ratio = contrastRatio(fgHex, bgHex);
        expect(
          ratio,
          `Dark contrast ${fgVar} (${fgHex}) on ${bgVar} (${bgHex}) = ${ratio.toFixed(2)}:1`
        ).toBeGreaterThanOrEqual(4.5);
      }
    });

    it("achieves >= 4.5:1 label contrast and >= 3:1 active indicator contrast across all ClinicalTabs variants in light and dark themes", () => {
      const clinicalTabsSfc = fs.readFileSync(
        path.join(uiDir, "src/components/clinical/ClinicalTabs.vue"),
        "utf-8"
      );
      const styleMatch = clinicalTabsSfc.match(
        /<style\s+scoped>([\s\S]*?)<\/style>/
      );
      expect(styleMatch).not.toBeNull();
      const tabsCss = styleMatch[1];

      const triggerBase = parseRuleProps(
        tabsCss,
        /\.clinical-tabs-trigger\s*\{/
      );
      expect(triggerBase).not.toBeNull();
      expect(triggerBase.appearance).toBe("none");
      expect(triggerBase["border-style"]).toBe("solid");
      expect(triggerBase["min-height"]).toBe("44px");
      expect(triggerBase["min-width"]).toBe("44px");

      const badgeBase = parseRuleProps(tabsCss, /\.clinical-tabs-badge\s*\{/);
      expect(badgeBase).not.toBeNull();
      expect(badgeBase["font-variant-numeric"]).toBe("tabular-nums");

      const underlineTrigger = parseRuleProps(
        tabsCss,
        /\.clinical-tabs-list\.variant-underline\s+\.clinical-tabs-trigger\s*\{/
      );
      const underlineActive = parseRuleProps(
        tabsCss,
        /\.clinical-tabs-list\.variant-underline\s+\.clinical-tabs-trigger\[aria-selected="true"\]\s*\{/
      );
      const underlineActiveBadge = parseRuleProps(
        tabsCss,
        /\.clinical-tabs-list\.variant-underline\s+\.clinical-tabs-trigger\[aria-selected="true"\]\s+\.clinical-tabs-badge\s*\{/
      );

      const pillsTrigger = parseRuleProps(
        tabsCss,
        /\.clinical-tabs-list\.variant-pills\s+\.clinical-tabs-trigger\s*\{/
      );
      const pillsActive = parseRuleProps(
        tabsCss,
        /\.clinical-tabs-list\.variant-pills\s+\.clinical-tabs-trigger\[aria-selected="true"\]\s*\{/
      );
      const solidActiveBadge = parseRuleProps(
        tabsCss,
        /\.clinical-tabs-list\.variant-pills\s+\.clinical-tabs-trigger\[aria-selected="true"\]\s+\.clinical-tabs-badge,\s*\.clinical-tabs-list\.variant-segmented\s+\.clinical-tabs-trigger\[aria-selected="true"\]\s+\.clinical-tabs-badge\s*\{/
      );

      const segmentedList = parseRuleProps(
        tabsCss,
        /\.clinical-tabs-list\.variant-segmented\s*\{/
      );
      const segmentedTrigger = parseRuleProps(
        tabsCss,
        /\.clinical-tabs-list\.variant-segmented\s+\.clinical-tabs-trigger\s*\{/
      );
      const segmentedActive = parseRuleProps(
        tabsCss,
        /\.clinical-tabs-list\.variant-segmented\s+\.clinical-tabs-trigger\[aria-selected="true"\]\s*\{/
      );

      for (const [mode, map] of [
        ["light", lightTheme],
        ["dark", darkTheme],
      ]) {
        const surfaceBg = resolveVar("--semantic-color-surface", map);
        const headerBg = resolveVar("--semantic-color-surface-header", map);

        // 1. Underline variant: >= 4.5:1 label & badge contrast, >= 3:1 active indicator contrast
        const underlineInactiveFg = resolveCssColorExpr(
          underlineTrigger.color,
          map
        );
        const underlineActiveFg = resolveCssColorExpr(
          underlineActive.color,
          map
        );
        const underlineIndicator = resolveCssColorExpr(
          underlineActive["border-bottom-color"],
          map
        );
        for (const hostBg of [surfaceBg, headerBg]) {
          expect(
            contrastRatio(underlineInactiveFg, hostBg),
            `underline inactive label (${mode})`
          ).toBeGreaterThanOrEqual(4.5);
          expect(
            contrastRatio(underlineActiveFg, hostBg),
            `underline active label (${mode})`
          ).toBeGreaterThanOrEqual(4.5);
          expect(
            contrastRatio(underlineIndicator, hostBg),
            `underline active indicator (${mode})`
          ).toBeGreaterThanOrEqual(3.0);
        }
        expect(
          contrastRatio(
            resolveCssColorExpr(underlineActiveBadge.color, map),
            resolveCssColorExpr(underlineActiveBadge["background-color"], map)
          ),
          `underline active badge (${mode})`
        ).toBeGreaterThanOrEqual(4.5);

        // 2. Pills variant: >= 4.5:1 label & badge contrast, >= 3:1 active pill contrast
        const pillsInactiveBg = resolveCssColorExpr(
          pillsTrigger["background-color"],
          map
        );
        const pillsInactiveFg = resolveCssColorExpr(pillsTrigger.color, map);
        const pillsActiveBg = resolveCssColorExpr(
          pillsActive["background-color"],
          map
        );
        const pillsActiveFg = resolveCssColorExpr(pillsActive.color, map);
        expect(
          contrastRatio(pillsInactiveFg, pillsInactiveBg),
          `pills inactive label (${mode})`
        ).toBeGreaterThanOrEqual(4.5);
        expect(
          contrastRatio(pillsActiveFg, pillsActiveBg),
          `pills active label (${mode})`
        ).toBeGreaterThanOrEqual(4.5);
        expect(
          contrastRatio(pillsActiveBg, pillsInactiveBg),
          `pills active vs inactive (${mode})`
        ).toBeGreaterThanOrEqual(3.0);
        for (const hostBg of [surfaceBg, headerBg]) {
          expect(
            contrastRatio(pillsActiveBg, hostBg),
            `pills active indicator vs surface (${mode})`
          ).toBeGreaterThanOrEqual(3.0);
        }
        expect(
          contrastRatio(
            resolveCssColorExpr(solidActiveBadge.color, map),
            resolveCssColorExpr(solidActiveBadge["background-color"], map)
          ),
          `pills/segmented active badge (${mode})`
        ).toBeGreaterThanOrEqual(4.5);

        // 3. Segmented variant: >= 4.5:1 label contrast, >= 3:1 active segment vs track contrast
        const trackBg = resolveCssColorExpr(
          segmentedList["background-color"],
          map
        );
        const segInactiveFg = resolveCssColorExpr(segmentedTrigger.color, map);
        const segActiveBg = resolveCssColorExpr(
          segmentedActive["background-color"],
          map
        );
        const segActiveFg = resolveCssColorExpr(segmentedActive.color, map);
        expect(
          contrastRatio(segInactiveFg, trackBg),
          `segmented inactive label (${mode})`
        ).toBeGreaterThanOrEqual(4.5);
        expect(
          contrastRatio(segActiveFg, segActiveBg),
          `segmented active label (${mode})`
        ).toBeGreaterThanOrEqual(4.5);
        expect(
          contrastRatio(segActiveBg, trackBg),
          `segmented active indicator vs track (${mode})`
        ).toBeGreaterThanOrEqual(3.0);
      }
    });

    it("achieves >= 4.5:1 WCAG AA contrast on centralized .btn-primary, portal .sync-badge, and clinician CRF designer badges/buttons", () => {
      const btnPrimary = parseRuleProps(
        tokensCss,
        /\.btn-primary,\s*\.btn\.btn-primary\s*\{/
      );
      expect(btnPrimary).not.toBeNull();
      const btnPrimaryHover = parseRuleProps(
        tokensCss,
        /\.can-hover\s+\.btn-primary:not\(:disabled\):not\(\[aria-disabled="true"\]\):hover,\s*\.can-hover\s+\.btn\.btn-primary:not\(:disabled\):not\(\[aria-disabled="true"\]\):hover\s*\{/
      );
      expect(btnPrimaryHover).not.toBeNull();
      const btnDisabled = parseRuleProps(
        tokensCss,
        /\.btn:disabled,\s*\.btn\[aria-disabled="true"\],\s*button\.btn:disabled,\s*button\.btn\[aria-disabled="true"\]\s*\{/
      );
      expect(btnDisabled).toMatchObject({
        cursor: "not-allowed",
      });

      for (const [mode, map] of [
        ["light", lightTheme],
        ["dark", darkTheme],
      ]) {
        const fg = resolveCssColorExpr(btnPrimary.color, map);
        const bg = resolveCssColorExpr(btnPrimary["background-color"], map);
        const ratio = contrastRatio(fg, bg);
        expect(
          ratio,
          `.btn-primary (${mode}): ${fg} on ${bg} = ${ratio.toFixed(2)}:1`
        ).toBeGreaterThanOrEqual(4.5);

        const hoverFg = resolveCssColorExpr(btnPrimaryHover.color, map);
        const hoverBg = resolveCssColorExpr(
          btnPrimaryHover["background-color"],
          map
        );
        const hoverRatio = contrastRatio(hoverFg, hoverBg);
        expect(
          hoverRatio,
          `.btn-primary:hover (${mode}): ${hoverFg} on ${hoverBg} = ${hoverRatio.toFixed(2)}:1`
        ).toBeGreaterThanOrEqual(4.5);
      }

      const portalMapLight = {
        ...lightTheme,
        ...parseCssBlockVars(portalStyleCss, /:root\s*\{/),
      };
      const portalMapDark = {
        ...darkTheme,
        ...parseCssBlockVars(portalStyleCss, /:root\s*\{/),
      };

      const syncBadge = parseRuleProps(portalStyleCss, /\.sync-badge\s*\{/);
      expect(syncBadge).not.toBeNull();
      for (const [mode, map] of [
        ["light", portalMapLight],
        ["dark", portalMapDark],
      ]) {
        const fg = resolveCssColorExpr(syncBadge.color, map);
        const bg = resolveCssColorExpr(syncBadge["background-color"], map);
        expect(
          contrastRatio(fg, bg),
          `sync-badge ${mode}`
        ).toBeGreaterThanOrEqual(4.5);
      }

      const webMapLight = {
        ...lightTheme,
        ...parseCssBlockVars(webStyleCss, /:root\s*\{/),
      };
      const webMapDark = {
        ...darkTheme,
        ...parseCssBlockVars(webStyleCss, /:root\s*\{/),
      };

      const centralizedMatrixRules = [
        { name: ".status-n-a", re: /\.status-n-a\s*\{/ },
        {
          name: ".clinical-soa-matrix th.arm-header",
          re: /\.clinical-soa-matrix\s+th\.arm-header\s*\{/,
        },
      ];
      for (const { name, re } of centralizedMatrixRules) {
        const props = parseRuleProps(tokensCss, re);
        expect(
          props,
          `Centralized rule not found in tokens.css: ${name}`
        ).not.toBeNull();
        for (const [mode, map] of [
          ["light", webMapLight],
          ["dark", webMapDark],
        ]) {
          const fg = resolveCssColorExpr(props.color, map);
          const bg = resolveCssColorExpr(
            props["background-color"] ||
              props.background ||
              "var(--semantic-color-surface)",
            map
          );
          const ratio = contrastRatio(fg, bg);
          expect(
            ratio,
            `${name} (${mode}): ${fg} on ${bg} = ${ratio.toFixed(2)}:1`
          ).toBeGreaterThanOrEqual(4.5);
        }
      }

      const formSectionVue = fs.readFileSync(
        path.join(webSrcDir, "components/crf/FormSectionContainer.vue"),
        "utf-8"
      );
      const canvasFieldVue = fs.readFileSync(
        path.join(webSrcDir, "components/crf/CanvasFieldWidget.vue"),
        "utf-8"
      );
      const scopedCrfRules = [
        {
          name: ".btn-add-field",
          source: formSectionVue,
          re: /\.btn-add-field\s*\{/,
        },
        {
          name: ".empty-dropzone-placeholder",
          source: formSectionVue,
          re: /\.empty-dropzone-placeholder\s*\{/,
        },
        {
          name: ".section-field-count",
          source: formSectionVue,
          re: /\.section-field-count\s*\{/,
        },
        {
          name: ".sdtm-tag-badge",
          source: canvasFieldVue,
          re: /\.sdtm-tag-badge\s*\{/,
        },
      ];

      for (const { name, source, re } of scopedCrfRules) {
        const props = parseRuleProps(source, re);
        expect(props, `Scoped CRF rule not found: ${name}`).not.toBeNull();
        for (const [mode, map] of [
          ["light", webMapLight],
          ["dark", webMapDark],
        ]) {
          const fg = resolveCssColorExpr(props.color, map);
          const bg = resolveCssColorExpr(
            props["background-color"] ||
              props.background ||
              "var(--semantic-color-surface)",
            map
          );
          const ratio = contrastRatio(fg, bg);
          expect(
            ratio,
            `${name} (${mode}): ${fg} on ${bg} = ${ratio.toFixed(2)}:1`
          ).toBeGreaterThanOrEqual(4.5);
        }
      }
    });
  });

  describe("Requirement 6: Tabular Numeral Alignment Across Tokens, Form Controls, Readouts, & AST Lint (ADR-2670)", () => {
    it("enforces --font-variant-numeric-tabular in tokens.css across tables, time, badges, and numeric/date controls while preserving coarse-pointer >=16px font-size and proportional textareas", () => {
      const tokensCss = fs.readFileSync(
        path.join(uiDir, "tokens.css"),
        "utf-8"
      );
      const rootVars = parseCssBlockVars(tokensCss, /:root\s*\{/);
      expect(rootVars["--font-variant-numeric-tabular"]).toBe("tabular-nums");
      expect(
        resolveVar("--semantic-font-variant-numeric-tabular", rootVars)
      ).toBe("tabular-nums");

      const globalTabularMatch = tokensCss.match(
        /table,\s*th,\s*td,\s*time,\s*\.tabular-nums,\s*\[data-tabular-nums\]:not\(\[data-tabular-nums="false"\]\),\s*\.clinical-table,\s*\.clinical-visit-matrix,\s*\.data-table\s*\{([\s\S]*?)\}/
      );
      expect(globalTabularMatch).not.toBeNull();
      expect(globalTabularMatch[1]).toContain(
        "font-variant-numeric: tabular-nums"
      );
      expect(globalTabularMatch[1]).toContain(
        "font-variant-numeric: var(--font-variant-numeric-tabular)"
      );

      // Verify UA font reset override for numeric/date inputs, table controls, and clinical-input-control
      for (const selectorFragment of [
        'input[type="number"]',
        'input[type="date"]',
        'input[type="datetime-local"]',
        'input[type="time"]',
        'input[type="tel"]',
        'input[type="range"]',
        'input[inputmode="numeric"]',
        'input[inputmode="decimal"]',
        "input.tabular-nums",
        "select.tabular-nums",
        'td input:not([type="checkbox"]):not([type="radio"])',
        "td select",
        ".clinical-input-control.tabular-nums",
        '.clinical-input-control[data-tabular-nums]:not([data-tabular-nums="false"])',
      ]) {
        expect(tokensCss).toContain(selectorFragment);
      }

      // Verify .clinical-badge applies tabular numerals
      const badgeMatch = tokensCss.match(/\.clinical-badge\s*\{([\s\S]*?)\}/);
      expect(badgeMatch).not.toBeNull();
      expect(badgeMatch[1]).toContain("var(--font-variant-numeric-tabular)");

      // Verify narrative <textarea> is never forced into tabular-nums
      const tokensCssNoComments = tokensCss.replace(/\/\*[\s\S]*?\*\//g, "");
      const tabularRuleBlocks =
        tokensCssNoComments.match(
          /[^{}]*\{[^{}]*font-variant-numeric:\s*tabular-nums[^{}]*\}/g
        ) || [];
      expect(tabularRuleBlocks.length).toBeGreaterThanOrEqual(3);
      for (const rule of tabularRuleBlocks) {
        const selectorPart = rule.slice(0, rule.indexOf("{"));
        expect(selectorPart).not.toMatch(/\btextarea\b/i);
      }

      // Verify touch viewports preserve >= 16px (1rem) font-size to prevent iOS auto-zoom
      const coarsePointerMatch = tokensCss.match(
        /@media\s*\(\s*pointer:\s*coarse\s*\)\s*\{([\s\S]*?\})\s*\}/
      );
      expect(coarsePointerMatch).not.toBeNull();
      expect(coarsePointerMatch[1]).toContain(
        "font-size: max(1rem, var(--font-size-md, 16px))"
      );

      const dataTableVue = fs.readFileSync(
        path.join(uiDir, "src/components/clinical/ClinicalDataTable.vue"),
        "utf-8"
      );
      expect(dataTableVue).toContain("font-variant-numeric: tabular-nums");

      const webStyleCss = fs.readFileSync(
        path.join(webSrcDir, "style.css"),
        "utf-8"
      );
      const portalStyleCss = fs.readFileSync(
        path.join(portalSrcDir, "style.css"),
        "utf-8"
      );
      expect(webStyleCss).toContain(
        '@import "../../../packages/ui/tokens.css"'
      );
      expect(portalStyleCss).toContain(
        '@import "../../../packages/ui/tokens.css"'
      );
    });

    it("scans all Vue, CSS, and JS files across packages/ui, apps/web, and apps/subject-portal for zero font-variant-numeric: normal overrides and full surface adoption", () => {
      const appVueFiles = [
        ...walkFiles(webSrcDir, [".vue"]),
        ...walkFiles(portalSrcDir, [".vue"]),
      ];
      const allFrontendFiles = [
        ...walkFiles(path.join(uiDir, "src"), [".vue", ".css", ".js"]),
        path.join(uiDir, "tokens.css"),
        path.join(uiDir, "responsive.css"),
        path.join(uiDir, "index.js"),
        ...walkFiles(webSrcDir, [".vue", ".css", ".js", ".ts"]),
        ...walkFiles(portalSrcDir, [".vue", ".css", ".js", ".ts"]),
      ];

      const allVueFiles = [
        ...walkFiles(path.join(uiDir, "src"), [".vue"]),
        ...appVueFiles,
      ];
      const tabularVueFiles = allVueFiles.filter((file) =>
        /<(?:table|ClinicalDataTable)[\s>]/.test(fs.readFileSync(file, "utf-8"))
      );
      expect(tabularVueFiles.length + 2).toBeGreaterThanOrEqual(52);

      // Scan ALL frontend files (not just <table> files) for forbidden normal overrides
      const overridingTabularNums = allFrontendFiles
        .filter((file) =>
          /font-variant-numeric\s*:\s*normal\b|fontVariantNumeric\s*:\s*["'`]normal["'`]/.test(
            fs.readFileSync(file, "utf-8")
          )
        )
        .map((file) => path.relative(repoRoot, file));
      expect(overridingTabularNums).toEqual([]);

      // Ensure app Vue components retain zero redundant <style scoped> table tabular-nums rules and zero empty <style> tags
      const redundantTableRuleFiles = [];
      const emptyStyleTagFiles = [];
      for (const file of appVueFiles) {
        const content = fs.readFileSync(file, "utf-8");
        if (/<style\b(?![^>]*\bsrc\s*=)[^>]*>\s*<\/style>/.test(content)) {
          emptyStyleTagFiles.push(path.relative(repoRoot, file));
        }
        for (const styleMatch of content.matchAll(
          /<style\b[^>]*>([\s\S]*?)<\/style>/g
        )) {
          const rules =
            styleMatch[1].match(/[^{}]*\{[^{}]*tabular-nums[^{}]*\}/g) || [];
          for (const rule of rules) {
            const selector = rule.slice(0, rule.indexOf("{")).trim();
            if (
              /(?:^|[\s,])(?:table|th|td|\.[a-z0-9-]*(?:table|matrix))\b/i.test(
                selector
              )
            ) {
              redundantTableRuleFiles.push(
                `${path.relative(repoRoot, file)} (${selector.replace(/\s+/g, " ")})`
              );
            }
          }
        }
      }
      expect(redundantTableRuleFiles).toEqual([]);
      expect(emptyStyleTagFiles).toEqual([]);

      // Verify key clinician web and subject portal surfaces adopt shared tabular tokens/classes
      const expectedAdoptions = [
        [
          path.join(uiDir, "src/components/clinical/ClinicalInput.vue"),
          ":data-tabular-nums=",
        ],
        [
          path.join(webSrcDir, "views/ObservationView.vue"),
          ":data-tabular-nums=",
        ],
        [
          path.join(webSrcDir, "components/ecrf/EcrfItemTable.vue"),
          ":data-tabular-nums=",
        ],
        [
          path.join(webSrcDir, "components/ecrf/EcrfSignaturesSection.vue"),
          "tabular-nums",
        ],
        [
          path.join(webSrcDir, "components/clinical/LabGovernanceConsole.vue"),
          "var(--font-variant-numeric-tabular)",
        ],
        [
          path.join(
            webSrcDir,
            "components/clinical/SensorTelemetryConsole.vue"
          ),
          "var(--font-variant-numeric-tabular)",
        ],
        [
          path.join(webSrcDir, "views/OverviewView.vue"),
          "var(--font-variant-numeric-tabular)",
        ],
        [
          path.join(webSrcDir, "views/TicketsView.vue"),
          "var(--font-variant-numeric-tabular)",
        ],
        [
          path.join(portalSrcDir, "style.css"),
          "var(--font-variant-numeric-tabular)",
        ],
        [
          path.join(portalSrcDir, "widgets/clinical-fields.js"),
          "var(--font-variant-numeric-tabular)",
        ],
        [path.join(portalSrcDir, "views/ComplianceView.vue"), "tabular-nums"],
        [path.join(portalSrcDir, "components/SyncDrawer.vue"), "tabular-nums"],
        [
          path.join(portalSrcDir, "components/PortalHeader.vue"),
          "tabular-nums",
        ],
        [path.join(portalSrcDir, "views/WalletView.vue"), "tabular-nums"],
      ];
      for (const [filePath, needle] of expectedAdoptions) {
        expect(
          fs.readFileSync(filePath, "utf-8"),
          `${path.relative(repoRoot, filePath)} must contain ${needle}`
        ).toContain(needle);
      }
    });

    it("enforces custom-vue/require-tabular-nums across numeric controls, countdowns, badges, KPIs, and CSS overrides via ESLint AST checks", async () => {
      const { ESLint } = await import("eslint");
      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });

      const violatingVue = await eslint.lintText(
        `<template>
  <div class="synthetic-panel">
    <label for="dose-input">Dose</label>
    <input id="dose-input" v-model.number="dose" type="text" />
    <label for="cohort-select">Cohort</label>
    <select id="cohort-select" v-model.number="cohort">
      <option :value="1">Cohort 1</option>
    </select>
    <ClinicalInput id="VS.SYSBP" v-model="sysbp" label="Systolic BP" type="numeric" :tabular-nums="false" />
    <span class="sla-timer">{{ remainingSeconds }}s</span>
    <span class="badge-unread">{{ unreadCount }}</span>
    <div id="wallet-balance-amount">\${{ balanceAmount.toFixed(2) }}</div>
  </div>
</template>
<script setup>
import { ref } from "vue";
import { ClinicalInput } from "ui";
const dose = ref(10);
const cohort = ref(1);
const sysbp = ref(120);
const remainingSeconds = ref(42);
const unreadCount = ref(5);
const balanceAmount = ref(125);
</script>
<style scoped>
.synthetic-panel {
  font-variant-numeric: normal;
}
</style>
`,
        {
          filePath: path.join(
            webSrcDir,
            "views/__SyntheticTabularViolations.vue"
          ),
        }
      );

      const violationMessages = violatingVue[0].messages.filter(
        (m) => m.ruleId === "custom-vue/require-tabular-nums"
      );
      expect(violationMessages.length).toBeGreaterThanOrEqual(7);

      const violatingJs = await eslint.lintText(
        `export function buildStyle() {
  return "font-variant-numeric: normal; color: inherit;";
}
`,
        {
          filePath: path.join(
            portalSrcDir,
            "widgets/__syntheticTabularViolation.js"
          ),
        }
      );
      expect(
        violatingJs[0].messages.some(
          (m) => m.ruleId === "custom-vue/require-tabular-nums"
        )
      ).toBe(true);

      const compliantVue = await eslint.lintText(
        `<template>
  <div class="synthetic-panel">
    <label for="dose-input">Dose</label>
    <input id="dose-input" v-model.number="dose" type="number" />
    <label for="cohort-select">Cohort</label>
    <select id="cohort-select" v-model.number="cohort" class="tabular-nums">
      <option :value="1">Cohort 1</option>
    </select>
    <ClinicalInput id="VS.SYSBP" v-model="sysbp" label="Systolic BP" type="numeric" />
    <time class="sla-timer">{{ remainingSeconds }}s</time>
    <span class="badge-unread tabular-nums">{{ unreadCount }}</span>
    <div id="wallet-balance-amount" class="tabular-nums">\${{ balanceAmount.toFixed(2) }}</div>
    <p class="prose-note">
      Participants whose cumulative stipends reach \${{ thresholdAmount.toFixed(2) }} within a calendar year receive IRS Form 1099-MISC documentation.
    </p>
  </div>
</template>
<script setup>
import { ref } from "vue";
import { ClinicalInput } from "ui";
const dose = ref(10);
const cohort = ref(1);
const sysbp = ref(120);
const remainingSeconds = ref(42);
const unreadCount = ref(5);
const balanceAmount = ref(125);
const thresholdAmount = ref(600);
</script>
`,
        {
          filePath: path.join(
            webSrcDir,
            "views/__SyntheticTabularCompliant.vue"
          ),
        }
      );

      const compliantErrors = compliantVue[0].messages.filter(
        (m) => m.ruleId === "custom-vue/require-tabular-nums"
      );
      expect(compliantErrors).toEqual([]);
    });

    it("defines platform-first --font-mono, --semantic-font-mono, 12px --font-size-2xs, .font-mono/.numeric utilities, and aligned body rules (ADR-2824)", () => {
      const tokensCss = fs.readFileSync(
        path.join(uiDir, "tokens.css"),
        "utf-8"
      );
      const rootVars = parseCssBlockVars(tokensCss, /:root\s*\{/);

      expect(rootVars["--font-mono"]).toBeDefined();
      const monoParts = rootVars["--font-mono"]
        .split(",")
        .map((part) => part.trim().replace(/^['"]|['"]$/g, ""));
      expect(monoParts[0]).toBe("JetBrains Mono");
      expect(monoParts.indexOf("ui-monospace")).toBeGreaterThan(0);
      expect(monoParts[monoParts.length - 1]).toBe("monospace");
      expect(monoParts.indexOf("monospace")).toBeGreaterThan(
        monoParts.indexOf("JetBrains Mono")
      );

      expect(rootVars["--semantic-font-mono"]).toBe("var(--font-mono)");
      expect(rootVars["--font-size-2xs"]).toBe("12px");
      expect(resolveVar("--semantic-font-size-2xs", rootVars)).toBe("12px");

      const fontMonoProps = parseRuleProps(tokensCss, /\.font-mono\s*\{/);
      expect(fontMonoProps).not.toBeNull();
      expect(fontMonoProps["font-family"]).toBe("var(--semantic-font-mono)");

      const numericProps = parseRuleProps(tokensCss, /\.numeric\s*\{/);
      expect(numericProps).not.toBeNull();
      expect(["tabular-nums", "var(--font-variant-numeric-tabular)"]).toContain(
        numericProps["font-variant-numeric"]
      );

      const webStyleCss = fs.readFileSync(
        path.join(webSrcDir, "style.css"),
        "utf-8"
      );
      const portalStyleCss = fs.readFileSync(
        path.join(portalSrcDir, "style.css"),
        "utf-8"
      );
      const webBody = parseRuleProps(webStyleCss, /(?:^|\n)body\s*\{/);
      const portalBody = parseRuleProps(portalStyleCss, /(?:^|\n)body\s*\{/);
      expect(webBody["font-family"]).toBe("var(--semantic-font-family)");
      expect(portalBody["font-family"]).toBe("var(--semantic-font-family)");
      expect(webBody["font-feature-settings"]).toContain('"cv05" 1');
      expect(webBody["font-feature-settings"]).toContain('"cv08" 1');
      expect(portalBody["font-feature-settings"]).toContain('"cv05" 1');
      expect(portalBody["font-feature-settings"]).toContain('"cv08" 1');
      expect(portalBody["font-size"]).toContain("--semantic-font-size-md");

      expect(rootVars["--touch-target-min"]).toBe("48px");
      expect(resolveVar("--semantic-touch-target-min", rootVars)).toBe("48px");
    });

    it("enforces zero sub-12px font-size rules and zero custom font-family stacks across ALL non-test Vue and CSS files", () => {
      const ignoreTestDirs = new Set([
        "tests",
        "__tests__",
        "node_modules",
        "dist",
        "playwright-report",
        "test-results",
      ]);
      const allVueFiles = [
        ...walkFiles(path.join(uiDir, "src"), [".vue"], ignoreTestDirs),
        ...walkFiles(webSrcDir, [".vue"], ignoreTestDirs),
        ...walkFiles(portalSrcDir, [".vue"], ignoreTestDirs),
      ].filter((f) => !f.includes(".test.") && !f.includes(".spec."));
      const allCssFiles = [
        path.join(uiDir, "tokens.css"),
        path.join(uiDir, "responsive.css"),
        ...walkFiles(path.join(uiDir, "src"), [".css"], ignoreTestDirs),
        ...walkFiles(webSrcDir, [".css"], ignoreTestDirs),
        ...walkFiles(portalSrcDir, [".css"], ignoreTestDirs),
      ];

      const nonTableVueFiles = allVueFiles.filter(
        (file) => !/<table[\s>]/.test(fs.readFileSync(file, "utf-8"))
      );
      expect(allVueFiles.length).toBeGreaterThanOrEqual(200);
      expect(nonTableVueFiles.length).toBeGreaterThanOrEqual(140);

      const allowedFontFamilies = new Set([
        "inherit",
        "var(--semantic-font-mono)",
        "var(--semantic-font-family)",
        "var(--font-sans)",
        "var(--font)",
        "var(--font-mono)",
      ]);

      const sub12pxViolations = [];
      const customFontStackViolations = [];

      for (const file of [...allVueFiles, ...allCssFiles]) {
        const rel = path.relative(repoRoot, file);
        const raw = fs.readFileSync(file, "utf-8");
        const clean = raw
          .replace(/\/\*[\s\S]*?\*\//g, "")
          .replace(/url\(\s*(?:"[^"]*"|'[^']*'|[^)]*)\s*\)/gi, "");

        for (const m of clean.matchAll(
          /(?:font-size|--(?:semantic-)?font-size-[a-zA-Z0-9_-]+)\s*:\s*([^;}"'`]+)/gi
        )) {
          const val = m[1].replace(/\s*!important\s*$/i, "").trim();
          for (const numMatch of val.matchAll(
            /([0-9]*\.?[0-9]+)\s*(px|rem|em|%)/gi
          )) {
            const n = parseFloat(numMatch[1]);
            const unit = numMatch[2].toLowerCase();
            if (
              (unit === "px" && n > 0 && n < 12) ||
              (unit === "rem" && n > 0 && n < 0.75) ||
              (unit === "em" && n > 0 && n < 0.75) ||
              (unit === "%" && n > 0 && n < 75)
            ) {
              sub12pxViolations.push(`${rel}: ${val}`);
            }
          }
        }

        const styleSources = file.endsWith(".css")
          ? [clean]
          : Array.from(
              clean.matchAll(/<style\b[^>]*>([\s\S]*?)<\/style>/gi),
              (m) => m[1]
            );
        for (const cssBlock of styleSources) {
          for (const m of cssBlock.matchAll(/font-family\s*:\s*([^;}]+)/gi)) {
            const normalized = m[1]
              .replace(/\s*!important\s*$/i, "")
              .replace(/\s+/g, " ")
              .trim();
            if (!allowedFontFamilies.has(normalized)) {
              customFontStackViolations.push(`${rel}: ${normalized}`);
            }
          }
        }
        if (file.endsWith(".vue")) {
          const tpl = clean
            .replace(/<style\b[^>]*>[\s\S]*?<\/style>/gi, "")
            .replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, "");
          for (const attr of tpl.matchAll(
            /:?style\s*=\s*(?:"([^"]*)"|'([^']*)')/gi
          )) {
            const inlineVal = attr[1] ?? attr[2] ?? "";
            for (const m of inlineVal.matchAll(/font-family\s*:\s*([^;]+)/gi)) {
              const normalized = m[1]
                .replace(/\s*!important\s*$/i, "")
                .replace(/\s+/g, " ")
                .trim();
              if (!allowedFontFamilies.has(normalized)) {
                customFontStackViolations.push(`${rel}: ${normalized}`);
              }
            }
          }
        }
      }

      expect(sub12pxViolations).toEqual([]);
      expect(customFontStackViolations).toEqual([]);
    });

    it("applies --semantic-font-mono and tabular-nums across non-table clinical & subject-portal views while preserving fixed DOM IDs", () => {
      const requiredTypographyTargets = [
        {
          file: path.join(webSrcDir, "views/StudyCanvasView.vue"),
          monoSelectors: ["#zoom-study", "#zoom-participant"],
          tabularSelectors: [],
        },
        {
          file: path.join(
            webSrcDir,
            "components/canvas/OpenStudyWorkspace.vue"
          ),
          monoSelectors: [
            "#open-study-code",
            "#create-study-code",
            "#create-study-sponsor-code",
            ".study-card-code",
          ],
          tabularSelectors: [".directory-status", ".study-card-sponsor"],
        },
        {
          file: path.join(
            webSrcDir,
            "components/canvas/ParticipantsSection.vue"
          ),
          monoSelectors: [".row-select", ".participant-site-code"],
          tabularSelectors: [".status-line"],
        },
        {
          file: path.join(webSrcDir, "components/canvas/CanvasInspector.vue"),
          monoSelectors: [".inspector-title--code", ".inspector-ref"],
          tabularSelectors: [],
        },
        {
          file: path.join(
            webSrcDir,
            "components/canvas/ParticipantInspectorPane.vue"
          ),
          monoSelectors: [".inspector-mono"],
          tabularSelectors: [".numeric"],
        },
        {
          file: path.join(
            webSrcDir,
            "components/canvas/ProtocolVersionInspectorPane.vue"
          ),
          monoSelectors: [],
          tabularSelectors: [
            "#inspector-version-forms",
            "#inspector-version-notes",
          ],
        },
        {
          file: path.join(
            webSrcDir,
            "components/canvas/GenericNodeInspectorPane.vue"
          ),
          monoSelectors: [".inspector-mono"],
          tabularSelectors: [],
        },
        {
          file: path.join(webSrcDir, "views/ObservationView.vue"),
          monoSelectors: [
            '[data-testid="observation-concept"]',
            '[data-testid="observation-value"]',
            ".observation-timestamp",
            "#observation-new-value",
          ],
          tabularSelectors: [
            '[data-testid="observation-value"]',
            "#observation-new-value",
          ],
        },
        {
          file: path.join(webSrcDir, "components/ecrf/EcrfFormPanel.vue"),
          monoSelectors: [
            '[data-testid="ecrf-form-id"]',
            "#ecrf-form-id",
            ".ecrf-item-id-label",
          ],
          tabularSelectors: [
            '[data-testid="ecrf-form-version"]',
            ".ecrf-item-value-control",
            ".ecrf-form-summary",
          ],
        },
        {
          file: path.join(webSrcDir, "components/ecrf/EcrfItemTable.vue"),
          monoSelectors: [
            ".ecrf-item-id-label",
            ".ecrf-item-concept",
            ".ecrf-item-value-control",
            ".ecrf-item-revision",
          ],
          tabularSelectors: [
            ".ecrf-item-value-control",
            ".ecrf-item-revision",
            ".ecrf-sdv-badge",
          ],
        },
        {
          file: path.join(portalSrcDir, "views/WalletView.vue"),
          monoSelectors: [
            "#wallet-ach-account-mask",
            "#wallet-ach-routing-mask",
            "#wallet-virtual-card-mask",
          ],
          tabularSelectors: [
            "#wallet-balance-amount",
            "#wallet-lifetime-stipends",
            "#wallet-lifetime-reimbursements",
            "#wallet-lifetime-disbursements",
          ],
          preservedIds: [
            "wallet-balance-amount",
            "wallet-ach-account-mask",
            "wallet-ach-routing-mask",
            "wallet-virtual-card-mask",
            "wallet-recent-transactions",
          ],
        },
        {
          file: path.join(portalSrcDir, "components/ParticipantCard.vue"),
          monoSelectors: ["#session-subject-id"],
          tabularSelectors: ["#session-wallet-balance"],
        },
        {
          file: path.join(portalSrcDir, "components/SyncDrawer.vue"),
          monoSelectors: [".answer-value"],
          tabularSelectors: ["#sync-drawer-retry-countdown", ".item-time"],
          preservedIds: [
            "global-sync-drawer",
            "sync-drawer-retry-countdown",
            "btn-drawer-sync-now",
          ],
        },
        {
          file: path.join(portalSrcDir, "views/ComplianceView.vue"),
          monoSelectors: [],
          tabularSelectors: [
            "#compliance-rate-pct",
            "#compliance-completed-count",
            "#compliance-pending-count",
            "#compliance-overdue-count",
          ],
          preservedIds: [
            "compliance-rate-pct",
            "compliance-completed-count",
            "compliance-pending-count",
            "compliance-overdue-count",
          ],
        },
        {
          file: path.join(webSrcDir, "views/CtmsView.vue"),
          monoSelectors: ["#ctms-study-input", ".ctms-scope-code"],
          tabularSelectors: [
            ".stat-value",
            "#kpi-total-subjects",
            "#kpi-enrollment-rate",
            "#kpi-sdv-percentage",
            "#kpi-open-queries",
          ],
        },
        {
          file: path.join(webSrcDir, "views/demo/CtmsDemoWorkspace.vue"),
          monoSelectors: ["#ctms-study-input", ".ctms-scope-code"],
          tabularSelectors: [
            ".stat-value",
            "#kpi-total-subjects",
            "#kpi-enrollment-rate",
            "#kpi-sdv-percentage",
            "#kpi-open-queries",
          ],
        },
        {
          file: path.join(
            webSrcDir,
            "components/canvas/KernelLineagePanel.vue"
          ),
          monoSelectors: [".amendment-change code"],
          tabularSelectors: [".lineage-total", ".numeric"],
        },
        {
          file: path.join(webSrcDir, "views/TicketsView.vue"),
          monoSelectors: [".scope-filter-input", ".ref-tag"],
          tabularSelectors: [".stat-value", ".column-count", ".sla-mini-badge"],
        },
        {
          file: path.join(
            webSrcDir,
            "components/tickets/TicketDetailDrawer.vue"
          ),
          monoSelectors: [".ref-badge", ".entity-id-value", ".sha-hash"],
          tabularSelectors: [".sla-banner", ".comment-time", ".audit-time"],
        },
        {
          file: path.join(webSrcDir, "views/KnowledgeView.vue"),
          monoSelectors: [
            ".knowledge-scope-input",
            ".crumb-slug",
            ".badge-version",
          ],
          tabularSelectors: [
            ".stat-value",
            ".category-count",
            ".badge-version",
          ],
        },
      ];

      for (const target of requiredTypographyTargets) {
        const rel = path.relative(repoRoot, target.file);
        const content = fs.readFileSync(target.file, "utf-8");
        const styleText = Array.from(
          content.matchAll(/<style\b[^>]*>([\s\S]*?)<\/style>/gi),
          (m) => m[1]
        ).join("\n");
        const rules = styleText.match(/[^{}]+\{[^{}]*\}/g) || [];

        for (const sel of target.monoSelectors) {
          const matched = rules.some(
            (r) =>
              r.slice(0, r.indexOf("{")).includes(sel) &&
              r.includes("var(--semantic-font-mono)")
          );
          expect(
            matched,
            `Expected ${rel} selector "${sel}" to set font-family: var(--semantic-font-mono)`
          ).toBe(true);
        }

        for (const sel of target.tabularSelectors) {
          const matched = rules.some(
            (r) =>
              r.slice(0, r.indexOf("{")).includes(sel) &&
              (r.includes("tabular-nums") ||
                r.includes("--font-variant-numeric-tabular"))
          );
          expect(
            matched,
            `Expected ${rel} selector "${sel}" to set tabular-nums`
          ).toBe(true);
        }

        for (const domId of target.preservedIds || []) {
          expect(
            content,
            `Expected ${rel} to preserve fixed DOM ID #${domId}`
          ).toContain(`id="${domId}"`);
        }
      }
    });

    it("enforces custom-vue typography lint rules against sub-12px sizes, custom font stacks, and normal numeral overrides across .vue and .css", async () => {
      const { ESLint } = await import("eslint");
      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });

      const vueViolations = await eslint.lintText(
        `<template>
  <div style="font-size: 11px; font-family: monospace; font-variant-numeric: normal">SUBJ-001</div>
</template>
<style scoped>
.bad-badge {
  font-size: 0.7rem;
  font-family: ui-monospace, monospace;
  font-variant-numeric: normal;
}
</style>
`,
        {
          filePath: path.join(webSrcDir, "views/__SyntheticTypographyBad.vue"),
        }
      );
      const vueMsgs = vueViolations[0].messages;
      expect(
        vueMsgs.filter((m) => m.ruleId === "custom-vue/no-sub12px-font-size")
      ).toHaveLength(2);
      expect(
        vueMsgs.filter((m) => m.ruleId === "custom-vue/no-custom-font-stack")
      ).toHaveLength(2);
      expect(
        vueMsgs.filter((m) => m.ruleId === "custom-vue/require-tabular-nums")
      ).toHaveLength(2);

      const cssViolations = await eslint.lintText(
        `:root {
  --font-mono: monospace, "JetBrains Mono";
  --font-size-2xs: 11px;
}
.raw-code {
  font-family: Courier, monospace;
  font-variant-numeric: normal;
}
`,
        { filePath: path.join(uiDir, "tokens.css") }
      );
      const cssRuleIds = cssViolations[0].messages.map((m) => m.ruleId);
      expect(cssRuleIds).toContain("custom-vue/no-custom-font-stack");
      expect(cssRuleIds).toContain("custom-vue/no-sub12px-font-size");
      expect(cssRuleIds).toContain("custom-vue/require-tabular-nums");

      const compliantTypoVue = await eslint.lintText(
        `<template>
  <span class="subj-code">SUBJ-001</span>
</template>
<style scoped>
.subj-code {
  font-family: var(--semantic-font-mono);
  font-size: var(--font-size-2xs);
  font-variant-numeric: tabular-nums;
}
</style>
`,
        {
          filePath: path.join(webSrcDir, "views/__SyntheticTypographyGood.vue"),
        }
      );
      const typographyErrors = compliantTypoVue[0].messages.filter((m) =>
        [
          "custom-vue/no-sub12px-font-size",
          "custom-vue/no-custom-font-stack",
          "custom-vue/require-tabular-nums",
        ].includes(m.ruleId)
      );
      expect(typographyErrors).toEqual([]);
    });
  });

  describe("Requirement 7: Shared Clinical Overlay Primitives & AST Backdrop Guardrails (ADR-2545 / ADR-2653)", () => {
    const MIGRATED_WEB_OVERLAY_FILES = [
      "views/DocumentManagerView.vue",
      "components/clinical/ProtocolTrainingConsole.vue",
      "components/clinical/AdaptiveDoseEscalationConsole.vue",
      "components/clinical/CecAdjudicationConsole.vue",
      "components/clinical/CellGeneTherapyCocConsole.vue",
      "components/clinical/ClinicalGanttVisualizer.vue",
      "components/clinical/ClinicalLakehouseConsole.vue",
      "components/clinical/SensorTelemetryConsole.vue",
      "components/clinical/SsuGreenlightConsole.vue",
      "components/clinical/StatisticalComputingConsole.vue",
      "components/econsent/IcfSectionEditor.vue",
      "views/DataLockView.vue",
      "views/ParticipantWalletView.vue",
      "views/ICFBuilderView.vue",
      "views/NotificationsView.vue",
      "components/persona/CrcFormRenderer.vue",
      "components/persona/CraVerificationConsole.vue",
      "components/quality/RactMatrixViewer.vue",
      "views/QualityCockpitView.vue",
      "views/CtmsView.vue",
      "features/signatures/components/SignatureCaptureModal.vue",
      "components/tickets/TicketSignModal.vue",
      "components/ReasonModal.vue",
    ];

    const FORMERLY_SUPPRESSED_BACKDROP_FILES = [
      "views/DocumentManagerView.vue",
      "components/clinical/CecAdjudicationConsole.vue",
      "components/clinical/ClinicalGanttVisualizer.vue",
      "components/clinical/ClinicalLakehouseConsole.vue",
      "components/clinical/SensorTelemetryConsole.vue",
      "views/DataLockView.vue",
      "views/ParticipantWalletView.vue",
      "views/KnowledgeView.vue",
    ];

    it("migrates all 23 apps/web overlay & signature/reason components to ClinicalModal or ClinicalDrawer and removes all backdrop suppressions", () => {
      expect(MIGRATED_WEB_OVERLAY_FILES).toHaveLength(23);
      const rawModalMarkupRe =
        /<(?:div|section|aside)\b[^>]*class=["'][^"']*\b(?:modal-overlay|modal-backdrop)\b/i;

      for (const relPath of MIGRATED_WEB_OVERLAY_FILES) {
        const fullPath = path.join(webSrcDir, relPath);
        const content = fs.readFileSync(fullPath, "utf-8");
        expect(
          /\b(?:ClinicalModal|ClinicalDrawer)\b/.test(content),
          `${relPath} must use ClinicalModal or ClinicalDrawer from packages/ui`
        ).toBe(true);
        const templateOnly =
          content.match(/<template\b[^>]*>([\s\S]*?)<\/template>/i)?.[1] || "";
        expect(
          rawModalMarkupRe.test(templateOnly),
          `${relPath} must not render raw .modal-overlay or .modal-backdrop markup`
        ).toBe(false);
      }

      const backdropSuppressionRe =
        /eslint-disable[^\n]*(?:vuejs-accessibility\/click-events-have-key-events|vuejs-accessibility\/no-static-element-interactions)[\s\S]{0,240}?<(?:div|section|aside)\b[^>]*class=["'][^"']*(?:modal|drawer|dialog)-(?:overlay|backdrop|mask|bg)/i;

      for (const relPath of FORMERLY_SUPPRESSED_BACKDROP_FILES) {
        const content = fs.readFileSync(path.join(webSrcDir, relPath), "utf-8");
        expect(
          backdropSuppressionRe.test(content),
          `${relPath} must not contain vuejs-accessibility backdrop suppressions`
        ).toBe(false);
      }

      const appVueFiles = [
        ...walkFiles(webSrcDir, [".vue"]),
        ...walkFiles(portalSrcDir, [".vue"]),
      ];
      const violatingFiles = appVueFiles
        .filter((file) =>
          backdropSuppressionRe.test(fs.readFileSync(file, "utf-8"))
        )
        .map((file) => path.relative(repoRoot, file));
      expect(violatingFiles).toEqual([]);
    });

    it("enforces prefers-reduced-motion, short-viewport dialog scrolling, and narrow footer wrapping on shared ClinicalModal and ClinicalDrawer surfaces", () => {
      const modalVue = fs.readFileSync(
        path.join(uiDir, "src/components/clinical/ClinicalModal.vue"),
        "utf-8"
      );
      const drawerVue = fs.readFileSync(
        path.join(uiDir, "src/components/clinical/ClinicalDrawer.vue"),
        "utf-8"
      );

      expect(modalVue).toMatch(
        /@media\s*\(\s*prefers-reduced-motion:\s*reduce\s*\)/
      );
      expect(modalVue).toMatch(/@media\s*\(\s*max-height:\s*480px\s*\)/);
      expect(modalVue).toMatch(
        /\.modal-footer\s*\{[\s\S]*?flex-wrap:\s*wrap\s*;/
      );
      expect(drawerVue).toMatch(
        /@media\s*\(\s*prefers-reduced-motion:\s*reduce\s*\)/
      );
    });

    it("rejects raw .modal-overlay / .modal-backdrop divs (even with useFocusTrap) and vuejs-accessibility backdrop suppressions via custom-vue/no-raw-overlays in eslint.config.mjs", async () => {
      const { ESLint } = await import("eslint");
      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });

      const rawOverlayResults = await eslint.lintText(
        `<template>
  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
  <div v-if="open" class="modal-overlay" @click.self="open = false">
    <div class="modal-card"><h3>Raw Dialog</h3></div>
  </div>
</template>
<script setup>
import { ref } from "vue";
const open = ref(true);
</script>
`,
        { filePath: path.join(webSrcDir, "views/__SyntheticRawOverlay.vue") }
      );

      const ruleIds = rawOverlayResults[0].messages.map((m) => m.ruleId);
      expect(
        ruleIds.filter((id) => id === "custom-vue/no-raw-overlays").length
      ).toBeGreaterThanOrEqual(2);

      const guardedRawModalProbes = [
        {
          filePath: path.join(
            webSrcDir,
            "features/signatures/components/SignatureCaptureModal.vue"
          ),
          code: `<template>
  <div v-if="open" class="modal-overlay" role="dialog" aria-modal="true" aria-label="Raw Signature">
    <div class="modal">Body</div>
  </div>
</template>
<script setup>
import { ref } from "vue";
import { useFocusTrap } from "ui";
const open = ref(true);
const modalRef = ref(null);
useFocusTrap(modalRef, { isOpen: open });
</script>
`,
        },
        {
          filePath: path.join(
            webSrcDir,
            "components/tickets/TicketSignModal.vue"
          ),
          code: `<template>
  <div v-if="open" class="modal-backdrop" role="presentation" @click.self="open = false">
    <div ref="modalRef" class="modal-dialog" role="dialog" aria-modal="true" aria-label="Raw Ticket Sign">Body</div>
  </div>
</template>
<script setup>
import { ref } from "vue";
import { useFocusTrap } from "ui";
const open = ref(true);
const modalRef = ref(null);
useFocusTrap(modalRef, { isOpen: open });
</script>
`,
        },
      ];

      for (const probe of guardedRawModalProbes) {
        const [probeResult] = await eslint.lintText(probe.code, {
          filePath: probe.filePath,
        });
        const overlayErrors = probeResult.messages.filter(
          (m) => m.ruleId === "custom-vue/no-raw-overlays"
        );
        expect(
          overlayErrors.length,
          `Expected custom-vue/no-raw-overlays error on ${path.relative(repoRoot, probe.filePath)}`
        ).toBeGreaterThanOrEqual(1);
      }

      const compliantResults = await eslint.lintText(
        `<template>
  <ClinicalModal v-model="open" :teleport="false" title="Compliant Dialog">
    <p>Guarded modal content</p>
  </ClinicalModal>
</template>
<script setup>
import { ref } from "vue";
import { ClinicalModal } from "ui";
const open = ref(true);
</script>
`,
        {
          filePath: path.join(
            webSrcDir,
            "views/__SyntheticCompliantOverlay.vue"
          ),
        }
      );

      const compliantErrors = compliantResults[0].messages.filter(
        (m) => m.ruleId === "custom-vue/no-raw-overlays"
      );
      expect(compliantErrors).toEqual([]);
    });

    it("eliminates all native alert(), confirm(), and prompt() calls and no-alert suppressions across web, subject-portal, and ui sources, and enforces no-alert: error across .js, .ts, and .vue", async () => {
      const { parse } = await import("@babel/parser");
      const { ESLint } = await import("eslint");

      const sourceFiles = [
        ...walkFiles(webSrcDir, [".js", ".ts", ".vue"]),
        ...walkFiles(portalSrcDir, [".js", ".ts", ".vue"]),
        ...walkFiles(path.join(uiDir, "src"), [".js", ".ts", ".vue"]),
      ];

      const suppressedFiles = [];
      const nativeDialogCalls = [];
      const dialogNames = new Set(["alert", "confirm", "prompt"]);
      const globalReceivers = new Set(["window", "globalThis", "self"]);

      function extractScripts(filePath, raw) {
        if (!filePath.endsWith(".vue")) return [raw];
        const blocks = [];
        const scriptRe = /<script\b[^>]*>([\s\S]*?)<\/script>/gi;
        let match;
        while ((match = scriptRe.exec(raw)) !== null) {
          blocks.push(match[1]);
        }
        return blocks;
      }

      function collectDeclaredFunctionsAndVars(ast) {
        const localBindings = new Set();
        function visit(node) {
          if (!node || typeof node !== "object") return;
          if (
            (node.type === "FunctionDeclaration" ||
              node.type === "FunctionExpression") &&
            node.id?.name
          ) {
            localBindings.add(node.id.name);
          } else if (
            node.type === "VariableDeclarator" &&
            node.id?.type === "Identifier"
          ) {
            localBindings.add(node.id.name);
          } else if (
            (node.type === "ImportSpecifier" ||
              node.type === "ImportDefaultSpecifier") &&
            node.local?.name
          ) {
            localBindings.add(node.local.name);
          }
          for (const key of Object.keys(node)) {
            if (key === "loc" || key === "comments") continue;
            const child = node[key];
            if (Array.isArray(child)) child.forEach(visit);
            else if (child && typeof child === "object") visit(child);
          }
        }
        visit(ast);
        return localBindings;
      }

      for (const file of sourceFiles) {
        const raw = fs.readFileSync(file, "utf-8");
        const rel = path.relative(repoRoot, file);
        if (raw.includes("no-alert")) {
          suppressedFiles.push(rel);
        }
        for (const script of extractScripts(file, raw)) {
          const ast = parse(script, {
            sourceType: "module",
            plugins: ["typescript", "jsx"],
          });
          const localBindings = collectDeclaredFunctionsAndVars(ast.program);
          function walkAst(node) {
            if (!node || typeof node !== "object") return;
            if (node.type === "CallExpression") {
              const callee = node.callee;
              if (
                callee?.type === "Identifier" &&
                dialogNames.has(callee.name) &&
                !localBindings.has(callee.name)
              ) {
                nativeDialogCalls.push(
                  `${rel}:${node.loc?.start.line ?? 0} (${callee.name})`
                );
              } else if (
                callee?.type === "MemberExpression" &&
                callee.object?.type === "Identifier" &&
                globalReceivers.has(callee.object.name) &&
                callee.property?.type === "Identifier" &&
                dialogNames.has(callee.property.name)
              ) {
                nativeDialogCalls.push(
                  `${rel}:${node.loc?.start.line ?? 0} (${callee.object.name}.${callee.property.name})`
                );
              }
            }
            for (const key of Object.keys(node)) {
              if (key === "loc" || key === "comments") continue;
              const child = node[key];
              if (Array.isArray(child)) child.forEach(walkAst);
              else if (child && typeof child === "object") walkAst(child);
            }
          }
          walkAst(ast.program);
        }
      }

      expect(suppressedFiles).toEqual([]);
      expect(nativeDialogCalls).toEqual([]);

      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });

      const checkedConfigPaths = [
        "apps/web/src/composables/useConfirmDialog.js",
        "apps/web/src/stores/clinical.ts",
        "apps/web/src/components/clinical/LabGovernanceConsole.vue",
        "apps/web/src/views/RulesView.vue",
        "apps/web/src/views/MdrView.vue",
        "apps/web/src/views/EcrfView.vue",
        "apps/web/src/views/QualityCockpitView.vue",
        "apps/subject-portal/src/main.js",
        "packages/ui/src/components/clinical/ClinicalModal.vue",
      ];

      for (const relPath of checkedConfigPaths) {
        const resolvedConfig = await eslint.calculateConfigForFile(
          path.join(repoRoot, relPath)
        );
        const ruleConfig = resolvedConfig.rules?.["no-alert"];
        const severity = Array.isArray(ruleConfig) ? ruleConfig[0] : ruleConfig;
        expect(severity, `Expected no-alert: error (2) for ${relPath}`).toBe(2);
      }

      // Verify no-alert is enforced as severity 2 ("error") across .js, .ts, and .vue files via lintText
      const probes = [
        {
          filePath: path.join(webSrcDir, "composables/__SyntheticAlert.js"),
          code: 'export function run() {\n  window.confirm("Blocked?");\n}\n',
        },
        {
          filePath: path.join(webSrcDir, "stores/__SyntheticAlert.ts"),
          code: 'export function run(): void {\n  window.alert("Blocked!");\n}\n',
        },
        {
          filePath: path.join(webSrcDir, "views/__SyntheticAlert.vue"),
          code: '<template><button type="button" @click="ask">Ask</button></template>\n<script setup lang="ts">\nfunction ask() {\n  prompt("Reason?");\n}\n</script>\n',
        },
      ];

      for (const probe of probes) {
        const [result] = await eslint.lintText(probe.code, {
          filePath: probe.filePath,
        });
        const alertErrors = result.messages.filter(
          (m) => m.ruleId === "no-alert" && m.severity === 2
        );
        expect(
          alertErrors.length,
          `Expected no-alert error (severity 2) on ${path.relative(repoRoot, probe.filePath)}`
        ).toBe(1);
      }
    });
  });

  describe("Requirement 8: Shared Accessibility Primitives & Label-in-Name AST Gates (ADR-2822)", () => {
    it("defines global .sr-only utility in packages/ui/tokens.css and migrates all 6 upload dropzones onto ClinicalDropzone without role=button containers", () => {
      const tokensCss = fs.readFileSync(
        path.join(uiDir, "tokens.css"),
        "utf-8"
      );
      const srOnlyProps = parseRuleProps(tokensCss, /\.sr-only\s*\{/);
      expect(srOnlyProps).not.toBeNull();
      expect(srOnlyProps.position).toBe("absolute");
      expect(srOnlyProps.width).toBe("1px");
      expect(srOnlyProps.height).toBe("1px");
      expect(srOnlyProps.overflow).toBe("hidden");

      const dropzoneFiles = [
        path.join(uiDir, "src/components/FileUploadModal.vue"),
        path.join(webSrcDir, "components/etmf/BatchUploadDrawer.vue"),
        path.join(webSrcDir, "components/etmf/DocumentGrid.vue"),
        path.join(webSrcDir, "components/SynonymImportDrawer.vue"),
        path.join(
          webSrcDir,
          "components/clinical/CellGeneTherapyCocConsole.vue"
        ),
        path.join(portalSrcDir, "views/WalletView.vue"),
      ];

      for (const filePath of dropzoneFiles) {
        const content = fs.readFileSync(filePath, "utf-8");
        expect(
          content,
          `${path.relative(repoRoot, filePath)} must adopt ClinicalDropzone`
        ).toContain("ClinicalDropzone");
      }

      const dropzonePrimitive = fs.readFileSync(
        path.join(uiDir, "src/components/clinical/ClinicalDropzone.vue"),
        "utf-8"
      );
      expect(dropzonePrimitive).not.toContain('role="button"');
    });

    it("rejects unlabeled glyph buttons (Vue & VitePress JS) and mismatched static aria-labels via custom-vue AST rules", async () => {
      const { ESLint } = await import("eslint");
      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });

      const unlabeledVueResults = await eslint.lintText(
        `<template>
  <div>
    <button type="button" title="Delete row">✕</button>
    <button type="button">{{ isOpen ? "▼" : "▶" }}</button>
    <button type="button" aria-label="Open Batch Document Upload Drawer">📑 Batch Upload</button>
    <button type="button" aria-label="Switch User Persona / Role"><span>{{ activeLabel }}</span></button>
  </div>
</template>
<script setup>
import { ref } from "vue";
const isOpen = ref(false);
const activeLabel = ref("CRA Monitor");
</script>
`,
        {
          filePath: path.join(webSrcDir, "views/__SyntheticA11yViolations.vue"),
        }
      );

      const vueRuleIds = unlabeledVueResults[0].messages.map((m) => m.ruleId);
      expect(
        vueRuleIds.filter((id) => id === "custom-vue/no-unlabeled-glyph-button")
      ).toHaveLength(2);
      expect(
        vueRuleIds.filter((id) => id === "custom-vue/no-aria-label-mismatch")
      ).toHaveLength(2);

      const vitepressJsResults = await eslint.lintText(
        `export function mountToolbar() {
  const btnZoom = document.createElement("button");
  btnZoom.innerText = "+";
  return btnZoom;
}
`,
        {
          filePath: path.join(
            repoRoot,
            "docs/.vitepress/theme/__SyntheticToolbar.js"
          ),
        }
      );
      expect(vitepressJsResults[0].messages.map((m) => m.ruleId)).toContain(
        "custom-vue/no-unlabeled-glyph-button"
      );

      const compliantVueResults = await eslint.lintText(
        `<template>
  <div>
    <ClinicalIconButton label="Delete row 1">✕</ClinicalIconButton>
    <button type="button">Verify <span class="sr-only">Vital Signs</span></button>
    <button type="button" aria-label="Batch Upload — Open Batch Document Upload Drawer">📑 Batch Upload</button>
  </div>
</template>
<script setup>
import { ClinicalIconButton } from "ui";
</script>
`,
        { filePath: path.join(webSrcDir, "views/__SyntheticA11yCompliant.vue") }
      );
      const a11yErrors = compliantVueResults[0].messages.filter(
        (m) =>
          m.ruleId === "custom-vue/no-unlabeled-glyph-button" ||
          m.ruleId === "custom-vue/no-aria-label-mismatch"
      );
      expect(a11yErrors).toEqual([]);
    });

    it("bans vuejs-accessibility eslint-disable suppressions across packages/ui and live operational hub files via custom-vue/no-vuejs-a11y-suppression", async () => {
      const { ESLint } = await import("eslint");
      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });

      const suppressedHubResults = await eslint.lintText(
        `<template>
  <div>
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
    <div class="ticket-card" @click="open">Card</div>
  </div>
</template>
<script setup>
/* eslint-disable vuejs-accessibility/form-control-has-label */
const open = () => {};
</script>
`,
        {
          filePath: path.join(webSrcDir, "views/TicketsView.vue"),
        }
      );

      expect(
        suppressedHubResults[0].messages.filter(
          (m) => m.ruleId === "custom-vue/no-vuejs-a11y-suppression"
        )
      ).toHaveLength(2);

      const suppressedUiResults = await eslint.lintText(
        `<template>
  <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -->
  <div @mousedown="drag">Viewport</div>
</template>
<script setup>
const drag = () => {};
</script>
`,
        {
          filePath: path.join(uiDir, "src/components/MediaPlayer.vue"),
        }
      );

      expect(
        suppressedUiResults[0].messages.filter(
          (m) => m.ruleId === "custom-vue/no-vuejs-a11y-suppression"
        )
      ).toHaveLength(1);

      const cleanHubResults = await eslint.lintText(
        `<template>
  <button type="button" class="ticket-card" @click="open">Open Ticket</button>
</template>
<script setup>
const open = () => {};
</script>
`,
        {
          filePath: path.join(webSrcDir, "views/TicketsView.vue"),
        }
      );

      expect(
        cleanHubResults[0].messages.filter(
          (m) => m.ruleId === "custom-vue/no-vuejs-a11y-suppression"
        )
      ).toEqual([]);
    });
  });

  describe("Requirement 9: Pruned Web Global Stylesheet, Standardized GxP Status Badges & AI Token Exclusivity (ADR-2654)", () => {
    const tokensCss = fs.readFileSync(path.join(uiDir, "tokens.css"), "utf-8");
    const webStyleCss = fs.readFileSync(
      path.join(webSrcDir, "style.css"),
      "utf-8"
    );
    const lightTheme = parseCssBlockVars(tokensCss, /:root\s*\{/);
    const darkTheme = {
      ...lightTheme,
      ...parseCssBlockVars(tokensCss, /:root\[data-theme="dark"\][\s\S]*?\{/),
    };

    it("standardizes .clinical-badge at 0.75rem with tabular-nums and >= 4.5:1 light/dark contrast across all 8 GxP and readiness modifiers", () => {
      const baseBadge = parseRuleProps(tokensCss, /\.clinical-badge\s*\{/);
      expect(baseBadge).not.toBeNull();
      expect(baseBadge["font-size"]).toBe("0.75rem");
      expect(
        resolveCssColorExpr(baseBadge["font-variant-numeric"], lightTheme)
      ).toBe("tabular-nums");

      const modifiers = [
        "draft",
        "verified",
        "queried",
        "frozen",
        "signed",
        "locked",
        "warning",
        "error",
      ];

      for (const mod of modifiers) {
        const rule = parseRuleProps(
          tokensCss,
          new RegExp(`\\.clinical-badge--${mod}\\s*\\{`)
        );
        expect(rule, `Missing .clinical-badge--${mod}`).not.toBeNull();
        for (const [mode, map] of [
          ["light", lightTheme],
          ["dark", darkTheme],
        ]) {
          const fg = resolveCssColorExpr(rule.color, map);
          const bg = resolveCssColorExpr(rule["background-color"], map);
          const ratio = contrastRatio(fg, bg);
          expect(
            ratio,
            `.clinical-badge--${mod} (${mode}): ${fg} on ${bg} = ${ratio.toFixed(2)}:1`
          ).toBeGreaterThanOrEqual(4.5);
        }
      }
    });

    it("prunes !important declarations and colliding shell, modal, badge, and designer selectors from apps/web/src/style.css", () => {
      const cleanWebCss = webStyleCss.replace(/\/\*[\s\S]*?\*\//g, "");
      expect(cleanWebCss).not.toMatch(/!important\b/);

      const forbiddenSelectors = [
        /(?:^|\n)\s*\.app-header(?![-\w])/,
        /(?:^|\n)\s*\.modal(?:-overlay|-header|-title|-body|-footer)?(?![-\w])/,
        /(?:^|\n)\s*\.btn-close(?![-\w])/,
        /(?:^|\n)\s*\.badge(?:\.gxp)?\s*\{/,
        /(?:^|\n)\s*\.authoring-canvas-layout(?![-\w])/,
        /(?:^|\n)\s*\.clinical-visit-matrix(?![-\w])/,
        /(?:^|\n)\s*\.clinical-lookup-container(?![-\w])/,
      ];
      for (const re of forbiddenSelectors) {
        expect(cleanWebCss).not.toMatch(re);
      }

      // Skip link remains high-specificity and keyboard focusable without !important
      expect(cleanWebCss).toMatch(/\.skip-link\.skip-link:focus-visible/);
    });

    it("enforces custom-vue/no-misused-ai-tokens across .vue, .css, and .ts files while permitting governed AI components", async () => {
      const { ESLint } = await import("eslint");
      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });

      const violationProbes = [
        {
          filePath: path.join(webSrcDir, "views/__SyntheticNonAiView.vue"),
          code: `<template><span style="color: var(--semantic-color-ai)">Status</span></template>\n<style scoped>\n.pill { background: var(--semantic-color-ai-bg); }\n</style>\n`,
          expectedCount: 2,
        },
        {
          filePath: path.join(webSrcDir, "__SyntheticStyles.css"),
          code: `.lookup-candidate {\n  border-color: var(--semantic-color-ai-border);\n}\n`,
          expectedCount: 1,
        },
        {
          filePath: path.join(webSrcDir, "stores/__SyntheticStore.ts"),
          code: `export const BADGE_COLOR = "var(--semantic-color-ai)";\n`,
          expectedCount: 1,
        },
      ];

      for (const probe of violationProbes) {
        const [res] = await eslint.lintText(probe.code, {
          filePath: probe.filePath,
        });
        const aiErrors = res.messages.filter(
          (m) =>
            m.ruleId === "custom-vue/no-misused-ai-tokens" && m.severity === 2
        );
        expect(
          aiErrors,
          `Expected ${probe.expectedCount} no-misused-ai-tokens error(s) for ${probe.filePath}`
        ).toHaveLength(probe.expectedCount);
      }

      const allowedProbes = [
        {
          filePath: path.join(webSrcDir, "components/ai/AiLabel.vue"),
          code: `<template><span class="ai-pill">AI-assisted</span></template>\n<style scoped>\n.ai-pill { background: var(--semantic-color-ai-bg); color: var(--semantic-color-ai); }\n</style>\n`,
        },
        {
          filePath: path.join(
            uiDir,
            "src/components/clinical/AiActionButton.vue"
          ),
          code: `<template><button type="button" class="ai-btn">Draft</button></template>\n<style scoped>\n.ai-btn { border-color: var(--semantic-color-ai-border); }\n</style>\n`,
        },
      ];

      for (const probe of allowedProbes) {
        const [res] = await eslint.lintText(probe.code, {
          filePath: probe.filePath,
        });
        const aiErrors = res.messages.filter(
          (m) => m.ruleId === "custom-vue/no-misused-ai-tokens"
        );
        expect(aiErrors).toEqual([]);
      }
    });

    it("enforces minmax(0, 1fr) responsive grid tracks, nested fieldset span resets, relative scroll wrappers, and clinical-input shrink safety (ADR-2663)", () => {
      const responsiveCss = fs.readFileSync(
        path.join(uiDir, "responsive.css"),
        "utf8"
      );
      const cleanResponsive = responsiveCss.replace(/\/\*[\s\S]*?\*\//g, "");

      expect(cleanResponsive).toMatch(
        /\.responsive-grid\s*\{[^}]*grid-template-columns:\s*minmax\(0,\s*1fr\)/
      );
      expect(cleanResponsive).toMatch(
        /\.grid-layout-responsive\s*\{[^}]*grid-template-columns:\s*minmax\(0,\s*1fr\)/
      );
      expect(cleanResponsive).toMatch(
        /\.grid-layout-responsive\s*\{[^}]*grid-template-columns:\s*minmax\(0,\s*1fr\)\s+minmax\(0,\s*2fr\)/
      );
      expect(cleanResponsive).toMatch(
        /\.grid-2-responsive\s*\{[^}]*grid-template-columns:\s*minmax\(0,\s*1fr\)/
      );
      expect(cleanResponsive).toMatch(
        /\.grid-asymmetric-responsive\s*\{[^}]*grid-template-columns:\s*minmax\(0,\s*1fr\)/
      );
      expect(cleanResponsive).toMatch(
        /\.grid-asymmetric-responsive\s*\{[^}]*grid-template-columns:\s*minmax\(0,\s*1fr\)\s+minmax\(0,\s*1\.2fr\)/
      );
      expect(cleanResponsive).toMatch(
        /@media\s*\(max-width:\s*1023px\)\s*\{\s*\.responsive-grid\s*>\s*\*,\s*\.responsive-grid\s+fieldset\s*>\s*\*\s*\{\s*grid-column:\s*span\s+1\s*\/\s*span\s+1\s*!important;/
      );
      expect(cleanResponsive).toMatch(
        /\.scrollable-table-wrapper\s*\{[^}]*position:\s*relative;/
      );

      expect(tokensCss).toMatch(
        /\.clinical-input,\s*\.clinical-radio-grid\s*\{[^}]*min-width:\s*0;/
      );
      expect(tokensCss).toMatch(
        /\.clinical-input\s+\.input-wrapper\s*\{[^}]*min-width:\s*0;/
      );
      expect(tokensCss).toMatch(/\.query-flag\s*\{\s*flex-shrink:\s*0;\s*\}/);
      expect(tokensCss).toMatch(
        /\.clinical-input\s+\.clinical-input-control\s*\{[^}]*min-width:\s*0;/
      );
    });

    it("enforces custom-vue/no-unscoped-clinical-tabs-css against unscoped list-class, tab-class, and active-tab-class selectors while permitting :deep(...) and variants", async () => {
      const { ESLint } = await import("eslint");
      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });

      const invalidCode = `<template>
  <ClinicalTabs
    v-model="active"
    :tabs="tabs"
    aria-label="Workspace sections"
    list-class="custom-tab-bar"
    :tab-class="['custom-tab-btn']"
    active-tab-class="is-selected-tab"
  />
</template>
<script setup>
import { ref } from "vue";
import { ClinicalTabs } from "ui";
const active = ref("a");
const tabs = [{ id: "a", label: "Alpha" }];
</script>
<style scoped>
.custom-tab-bar {
  display: flex;
}
.custom-tab-btn {
  padding: 8px;
}
@media (max-width: 768px) {
  .is-selected-tab {
    font-weight: 700;
  }
}
</style>
`;
      const [invalidResult] = await eslint.lintText(invalidCode, {
        filePath: path.join(webSrcDir, "views/__SyntheticTabsInvalid.vue"),
      });
      const unscopedErrors = invalidResult.messages.filter(
        (m) =>
          m.ruleId === "custom-vue/no-unscoped-clinical-tabs-css" &&
          m.severity === 2
      );
      expect(unscopedErrors).toHaveLength(3);

      const validDeepAndVariantCode = `<template>
  <div>
    <ClinicalTabs
      v-model="active"
      :tabs="tabs"
      variant="pills"
      overflow="scroll"
      aria-label="Primary tabs"
    />
    <ClinicalTabs
      v-model="active"
      :tabs="tabs"
      variant="underline"
      aria-label="Deep escaped tabs"
      list-class="escaped-tab-bar"
      tab-class="escaped-tab-btn"
    />
  </div>
</template>
<script setup>
import { ref } from "vue";
import { ClinicalTabs } from "ui";
const active = ref("a");
const tabs = [{ id: "a", label: "Alpha" }];
</script>
<style scoped>
:deep(.escaped-tab-bar) {
  margin-bottom: 8px;
}
.wrapper :deep(.escaped-tab-btn:not(:disabled)) {
  letter-spacing: 0.01em;
}
</style>
`;
      const [validResult] = await eslint.lintText(validDeepAndVariantCode, {
        filePath: path.join(webSrcDir, "views/__SyntheticTabsValid.vue"),
      });
      expect(
        validResult.messages.filter(
          (m) => m.ruleId === "custom-vue/no-unscoped-clinical-tabs-css"
        )
      ).toEqual([]);
    });
  });

  describe("Requirement 10: Centralized CSS States, Live Clinical Hub Migrations & AST Ratchet Gates (ADR-2673)", () => {
    const MIGRATED_CLINICAL_HUB_FILES = [
      path.join(webSrcDir, "components/canvas/ParticipantsSection.vue"),
      path.join(webSrcDir, "components/canvas/SitesSection.vue"),
      path.join(webSrcDir, "components/canvas/AddParticipantForm.vue"),
      path.join(webSrcDir, "components/canvas/ProtocolVersionsSection.vue"),
      path.join(webSrcDir, "components/canvas/LocksSection.vue"),
      path.join(webSrcDir, "components/canvas/QueriesSection.vue"),
      path.join(webSrcDir, "components/clinical/CtmsGreenlightSection.vue"),
      path.join(webSrcDir, "components/clinical/CtmsMilestonesSection.vue"),
      path.join(webSrcDir, "components/clinical/CtmsMonitoringSection.vue"),
      path.join(webSrcDir, "components/clinical/CtmsDelegationSection.vue"),
      path.join(webSrcDir, "components/clinical/RtsmSupplySection.vue"),
      path.join(webSrcDir, "components/clinical/SiteActivationPanel.vue"),
      path.join(
        webSrcDir,
        "components/clinical/BulkIpKitReconciliationDrawer.vue"
      ),
      path.join(webSrcDir, "components/etmf/CraQcQueue.vue"),
      path.join(webSrcDir, "components/etmf/BatchUploadDrawer.vue"),
      path.join(webSrcDir, "views/DocumentsHubView.vue"),
      path.join(webSrcDir, "views/DocumentManagerView.vue"),
      path.join(webSrcDir, "views/TicketsView.vue"),
      path.join(webSrcDir, "components/tickets/TicketCreateModal.vue"),
      path.join(webSrcDir, "components/tickets/TicketSignModal.vue"),
      path.join(webSrcDir, "components/tickets/TicketDetailDrawer.vue"),
      path.join(portalSrcDir, "views/ComplianceView.vue"),
    ];

    it("migrates live Canvas, CTMS, eTMF, Documents, Tickets, and Compliance views onto ClinicalDataTable / ClinicalInput / ClinicalFieldLayout with zero raw tables", () => {
      for (const fullPath of MIGRATED_CLINICAL_HUB_FILES) {
        const rel = path.relative(repoRoot, fullPath);
        const content = fs.readFileSync(fullPath, "utf-8");
        expect(
          /\b(?:ClinicalDataTable|ClinicalFieldLayout|ClinicalInput)\b/.test(
            content
          ),
          `${rel} must adopt ClinicalDataTable, ClinicalFieldLayout, or ClinicalInput from packages/ui`
        ).toBe(true);
        const templateOnly =
          content.match(/<template\b[^>]*>([\s\S]*?)<\/template>/i)?.[1] || "";
        expect(
          /<table\b/i.test(templateOnly),
          `${rel} must not render raw <table> markup`
        ).toBe(false);
      }
    });

    it("enforces subject-portal focus outline lint, custom-vue/no-raw-clinical-table, and custom-vue/no-unguarded-button-state in ESLint", async () => {
      const { ESLint } = await import("eslint");
      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });

      // 1. Subject portal focus lint rejects bare outline: none and accepts :focus:not(:focus-visible)
      const [portalBadCss] = await eslint.lintText(
        `.global-drawer {\n  outline: none;\n}\n`,
        { filePath: path.join(portalSrcDir, "__SyntheticPortalBad.css") }
      );
      expect(
        portalBadCss.messages.filter(
          (m) => m.ruleId === "custom-vue/no-suppressed-focus-outline"
        )
      ).toHaveLength(1);

      const [portalGoodCss] = await eslint.lintText(
        `.global-drawer:focus:not(:focus-visible),\nh2[tabindex="-1"]:focus-visible {\n  outline: none;\n}\n`,
        { filePath: path.join(portalSrcDir, "__SyntheticPortalGood.css") }
      );
      expect(
        portalGoodCss.messages.filter(
          (m) => m.ruleId === "custom-vue/no-suppressed-focus-outline"
        )
      ).toEqual([]);

      // 2. no-raw-clinical-table blocks raw <table> in migrated hubs while exempting 2D clinical matrices
      const rawTableSfc = `<template>
  <table>
    <thead><tr><th scope="col">ID</th></tr></thead>
    <tbody><tr><td>1</td></tr></tbody>
  </table>
</template>
`;
      const [migratedTableViolation] = await eslint.lintText(rawTableSfc, {
        filePath: path.join(webSrcDir, "components/canvas/SitesSection.vue"),
      });
      expect(
        migratedTableViolation.messages.filter(
          (m) => m.ruleId === "custom-vue/no-raw-clinical-table"
        )
      ).toHaveLength(1);

      const [portalTableViolation] = await eslint.lintText(rawTableSfc, {
        filePath: path.join(portalSrcDir, "views/ComplianceView.vue"),
      });
      expect(
        portalTableViolation.messages.filter(
          (m) => m.ruleId === "custom-vue/no-raw-clinical-table"
        )
      ).toHaveLength(1);

      const [exemptMatrixResult] = await eslint.lintText(rawTableSfc, {
        filePath: path.join(
          webSrcDir,
          "components/clinical/ClinicalSoAMatrix.vue"
        ),
      });
      expect(
        exemptMatrixResult.messages.filter(
          (m) => m.ruleId === "custom-vue/no-raw-clinical-table"
        )
      ).toEqual([]);

      // 3. no-unguarded-button-state flags untyped <button> and unguarded .btn*:hover while passing guarded buttons
      const [unguardedBtnResult] = await eslint.lintText(
        `<template>
  <button class="btn-submit" @click="save">Save</button>
</template>
<script setup>
const save = () => {};
</script>
<style scoped>
.btn-submit:hover {
  opacity: 0.9;
}
</style>
`,
        {
          filePath: path.join(webSrcDir, "views/__SyntheticUnguardedBtn.vue"),
        }
      );
      const btnViolations = unguardedBtnResult.messages.filter(
        (m) => m.ruleId === "custom-vue/no-unguarded-button-state"
      );
      expect(btnViolations).toHaveLength(2);

      const [guardedBtnResult] = await eslint.lintText(
        `<template>
  <button type="button" class="btn-submit" @click="save">Save</button>
</template>
<script setup>
const save = () => {};
</script>
<style scoped>
.btn-submit:not(:disabled):hover {
  opacity: 0.9;
}
</style>
`,
        {
          filePath: path.join(webSrcDir, "views/__SyntheticGuardedBtn.vue"),
        }
      );
      expect(
        guardedBtnResult.messages.filter(
          (m) => m.ruleId === "custom-vue/no-unguarded-button-state"
        )
      ).toEqual([]);
    });
  });

  describe("Requirement 11: Semantic Role Separation, Workflow/Regulatory Badges & Zero Hardcoded Color Literals (ADR-2675)", () => {
    const tokensCss = fs.readFileSync(path.join(uiDir, "tokens.css"), "utf-8");
    const lightTheme = parseCssBlockVars(tokensCss, /:root\s*\{/);
    const darkTheme = {
      ...lightTheme,
      ...parseCssBlockVars(tokensCss, /:root\[data-theme="dark"\][\s\S]*?\{/),
    };

    it("separates neutral primary ink (--text-main) from interactive brand blue (--primary) on AppShell, StudyCanvasView, and Subject Portal root containers", () => {
      const appShell = fs.readFileSync(
        path.join(webSrcDir, "components/AppShell.vue"),
        "utf-8"
      );
      const studyCanvas = fs.readFileSync(
        path.join(webSrcDir, "views/StudyCanvasView.vue"),
        "utf-8"
      );
      const portalStyle = fs.readFileSync(
        path.join(portalSrcDir, "style.css"),
        "utf-8"
      );

      const appShellRule = parseRuleProps(appShell, /\.app-shell\s*\{/);
      expect(appShellRule).not.toBeNull();
      expect(appShellRule.color).toBe("var(--text-main)");

      const canvasRule = parseRuleProps(studyCanvas, /\.canvas\s*\{/);
      expect(canvasRule).not.toBeNull();
      expect(canvasRule.color).toBe("var(--text-main)");

      expect(portalStyle).toMatch(
        /h1,\s*h2,\s*h3,\s*h4\s*\{[^}]*color:\s*var\(--text-main\)/s
      );
    });

    it("provides paired >= 4.5:1 light and dark non-AI workflow and regulatory badge modifiers (.clinical-badge--candidate, --in-progress, --regulatory)", () => {
      for (const mod of ["candidate", "in-progress", "regulatory"]) {
        const rule = parseRuleProps(
          tokensCss,
          new RegExp(`\\.clinical-badge--${mod}\\s*\\{`)
        );
        expect(rule, `Missing .clinical-badge--${mod}`).not.toBeNull();
        for (const [mode, map] of [
          ["light", lightTheme],
          ["dark", darkTheme],
        ]) {
          const fg = resolveCssColorExpr(rule.color, map);
          const bg = resolveCssColorExpr(rule["background-color"], map);
          const ratio = contrastRatio(fg, bg);
          expect(
            ratio,
            `.clinical-badge--${mod} (${mode}): ${fg} on ${bg} = ${ratio.toFixed(2)}:1`
          ).toBeGreaterThanOrEqual(4.5);
        }
      }
    });

    it("maintains zero hardcoded #hex, rgb(), rgba(), hsl(), or hsla() color literals across packages/ui/src, apps/web/src, and apps/subject-portal/src", () => {
      const sourceFiles = [
        path.join(uiDir, "index.js"),
        ...walkFiles(path.join(uiDir, "src"), [".vue", ".js", ".ts", ".css"]),
        ...walkFiles(webSrcDir, [".vue", ".js", ".ts", ".css"]),
        ...walkFiles(portalSrcDir, [".vue", ".js", ".ts", ".css"]),
      ];

      const extractScannableSegments = (filePath, raw) => {
        if (filePath.endsWith(".css")) {
          return [raw.replace(/\/\*[\s\S]*?\*\//g, "")];
        }
        if (filePath.endsWith(".vue")) {
          const segments = [];
          for (const m of raw.matchAll(
            /<script\b[^>]*>([\s\S]*?)<\/script>/gi
          )) {
            segments.push(
              m[1]
                .replace(/\/\*[\s\S]*?\*\//g, "")
                .replace(/(^|[^:])\/\/.*$/gm, "$1")
            );
          }
          for (const m of raw.matchAll(/<style\b[^>]*>([\s\S]*?)<\/style>/gi)) {
            segments.push(m[1].replace(/\/\*[\s\S]*?\*\//g, ""));
          }
          const tplMatch = raw.match(/<template\b[^>]*>([\s\S]*)<\/template>/i);
          if (tplMatch) {
            const tpl = tplMatch[1].replace(/<!--[\s\S]*?-->/g, "");
            for (const tag of tpl.matchAll(/<[a-zA-Z][^>]*>/g)) {
              segments.push(tag[0]);
            }
          }
          return segments;
        }
        return [
          raw
            .replace(/\/\*[\s\S]*?\*\//g, "")
            .replace(/(^|[^:])\/\/.*$/gm, "$1"),
        ];
      };

      const violations = [];
      for (const filePath of sourceFiles) {
        const raw = fs.readFileSync(filePath, "utf-8");
        for (const content of extractScannableSegments(filePath, raw)) {
          if (/(?:rgba?|hsla?)\s*\(/i.test(content)) {
            violations.push(
              `${path.relative(repoRoot, filePath)}: rgb/hsl function`
            );
          }
          for (const m of content.matchAll(
            /#([0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})(?![0-9a-zA-Z_-])/g
          )) {
            const idx = m.index ?? 0;
            const prevChar = idx > 0 ? content[idx - 1] : "";
            if (/[0-9a-zA-Z_$/-]/.test(prevChar) || prevChar === "&") {
              continue;
            }
            violations.push(`${path.relative(repoRoot, filePath)}: ${m[0]}`);
          }
        }
      }
      expect(violations).toEqual([]);
    });
  });

  describe("Requirement 12: Focus Trap Detachment Recovery & Inline Focus Handoffs (ADR-2676)", () => {
    it("enforces custom-vue/require-inline-focus-handoff across dropzones, error alerts, and subject-portal retry scripts (ADR-2676)", async () => {
      const uiExports = await import("../index.js");
      expect(uiExports.handoffRetryFocus).toBeTypeOf("function");
      expect(uiExports.useRetryFocusHandoff).toBeTypeOf("function");

      const dropzoneSrc = fs.readFileSync(
        path.join(uiDir, "src/components/clinical/ClinicalDropzone.vue"),
        "utf-8"
      );
      expect(dropzoneSrc).toContain("handoffFocus");
      expect(dropzoneSrc).toContain(".clinical-dropzone-browse-btn");

      const canvasErrorSrc = fs.readFileSync(
        path.join(webSrcDir, "components/canvas/CanvasErrorAlert.vue"),
        "utf-8"
      );
      expect(canvasErrorSrc).toContain("handoffRetryFocus");

      const portalFeaturesDir = path.join(
        repoRoot,
        "apps/subject-portal/src/features"
      );
      for (const scriptName of ["tasks.js", "inbox.js", "compliance.js"]) {
        const scriptSrc = fs.readFileSync(
          path.join(portalFeaturesDir, scriptName),
          "utf-8"
        );
        expect(
          /handoffRetryFocus|handoffFocus/.test(scriptSrc),
          `Expected ${scriptName} to coordinate retry focus via handoffRetryFocus or handoffFocus`
        ).toBe(true);
      }

      const { ESLint } = await import("eslint");
      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });

      const invalidProbes = [
        {
          filePath: path.join(
            uiDir,
            "src/components/clinical/__SyntheticDropzoneInvalid.vue"
          ),
          code: `<template>\n  <div>\n    <button type="button" class="clinical-dropzone-browse-btn">Browse</button>\n    <button v-if="hasFile" type="button" class="clinical-dropzone-remove-btn" @click="emit('remove')">Remove</button>\n  </div>\n</template>\n<script setup>\ndefineProps({ hasFile: Boolean });\nconst emit = defineEmits(["remove"]);\n</script>\n`,
        },
        {
          filePath: path.join(
            webSrcDir,
            "components/canvas/__SyntheticErrorAlertInvalid.vue"
          ),
          code: `<template>\n  <div role="alert">\n    <button type="button" data-testid="canvas-error-retry" @click="$emit('retry')">Retry</button>\n  </div>\n</template>\n<script setup>\ndefineEmits(["retry"]);\n</script>\n`,
        },
        {
          filePath: path.join(
            portalFeaturesDir,
            "__SyntheticPortalRetryInvalid.js"
          ),
          code: `export async function retrySyntheticVisits() {\n  await Promise.resolve();\n}\n`,
        },
      ];

      for (const probe of invalidProbes) {
        const [result] = await eslint.lintText(probe.code, {
          filePath: probe.filePath,
        });
        const focusErrors = result.messages.filter(
          (m) =>
            m.ruleId === "custom-vue/require-inline-focus-handoff" &&
            m.severity === 2
        );
        expect(
          focusErrors,
          `Expected require-inline-focus-handoff error for ${probe.filePath}`
        ).toHaveLength(1);
      }

      const validProbes = [
        {
          filePath: path.join(
            uiDir,
            "src/components/clinical/ClinicalDropzone.vue"
          ),
          code: dropzoneSrc,
        },
        {
          filePath: path.join(
            webSrcDir,
            "components/canvas/CanvasErrorAlert.vue"
          ),
          code: canvasErrorSrc,
        },
        {
          filePath: path.join(portalFeaturesDir, "compliance.js"),
          code: fs.readFileSync(
            path.join(portalFeaturesDir, "compliance.js"),
            "utf-8"
          ),
        },
      ];

      for (const probe of validProbes) {
        const [result] = await eslint.lintText(probe.code, {
          filePath: probe.filePath,
        });
        expect(
          result.messages.filter(
            (m) => m.ruleId === "custom-vue/require-inline-focus-handoff"
          )
        ).toEqual([]);
      }
    });
  });
});
