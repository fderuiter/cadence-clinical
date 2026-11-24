import { describe, it, expect } from "vitest";
import fs from "fs";
import path from "path";
import * as rootUi from "../index.js";

const uiDir = path.resolve(__dirname, "..");
const repoRoot = path.resolve(uiDir, "../..");
const webSrcDir = path.join(repoRoot, "apps/web/src");
const portalSrcDir = path.join(repoRoot, "apps/subject-portal/src");
const docsComponentsDir = path.join(repoRoot, "docs/components");

const EXPECTED_SHARED_COMPONENTS = [
  "AiActionButton",
  "AiSuggestionDrawer",
  "AuditLogViewer",
  "ClinicalBreadcrumbs",
  "ClinicalContextBar",
  "ClinicalDataTable",
  "ClinicalDrawer",
  "ClinicalDropzone",
  "ClinicalFieldLayout",
  "ClinicalFormField",
  "ClinicalIcon",
  "ClinicalIconButton",
  "ClinicalInput",
  "ClinicalLookupInput",
  "ClinicalModal",
  "ClinicalQueryFlag",
  "ClinicalQueryPanel",
  "ClinicalRadioGroup",
  "ClinicalTabs",
  "ClinicalToastHost",
  "ConfidenceBadge",
  "EmptyState",
  "EntityLink",
  "FileUploadModal",
  "MediaPlayer",
  "ModalErrorAlert",
  "PersonaSwitcher",
  "SignOutConfirmModal",
];

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
    it("exposes all 28 shared Vue components from root index.js with zero unexported SFCs", () => {
      const discoveredVueNames = walkFiles(path.join(uiDir, "src/components"), [
        ".vue",
      ])
        .map((f) => path.basename(f, ".vue"))
        .sort();

      expect(discoveredVueNames).toEqual(
        [...EXPECTED_SHARED_COMPONENTS].sort()
      );
      expect(EXPECTED_SHARED_COMPONENTS).toHaveLength(28);
      for (const name of EXPECTED_SHARED_COMPONENTS) {
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

    it("declares complete TypeScript definitions for all 28 components and helpers in index.d.ts", () => {
      const dts = fs.readFileSync(path.join(uiDir, "index.d.ts"), "utf-8");
      for (const componentName of EXPECTED_SHARED_COMPONENTS) {
        expect(dts).toMatch(
          new RegExp(`export (?:declare )?const ${componentName}:`)
        );
      }
      for (const helperName of [
        ...EXPECTED_KEYBOARD_HELPERS,
        "createSrOnlyActionButton",
        "useAsyncRetry",
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
        /:where\(\[role="button"\],\s*\[tabindex\]\):focus-visible\s*\{/
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
        /\.can-hover\s+\.btn-primary:hover,\s*\.can-hover\s+\.btn\.btn-primary:hover\s*\{/
      );
      expect(btnPrimaryHover).not.toBeNull();

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

      const tableVueFiles = [
        ...walkFiles(path.join(uiDir, "src"), [".vue"]),
        ...appVueFiles,
      ].filter((file) => /<table[\s>]/.test(fs.readFileSync(file, "utf-8")));
      expect(tableVueFiles.length + 2).toBeGreaterThanOrEqual(52);

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

      // ADR-2672: Zero raw modal/drawer overlay containers or raw dialog elements across apps/web and apps/subject-portal
      const rawAnyOverlayRe =
        /<(?:div|section|aside)\b[^>]*class=["'][^"']*\b(?:(?:modal|drawer|dialog)-(?:overlay|backdrop|mask|bg)|overlay-backdrop|[a-z0-9-]+-drawer-overlay|[a-z0-9-]+-modal-backdrop)\b/i;
      const rawDialogElementRe =
        /<(?:div|section|aside)\b[^>]*\brole=["'](?:dialog|alertdialog)["']/i;
      const templateBlockRe = /<template\b[^>]*>([\s\S]*?)<\/template>/i;
      const rawOverlayViolations = [];
      for (const file of appVueFiles) {
        const content = fs.readFileSync(file, "utf-8");
        const templateOnly = content.match(templateBlockRe)?.[1] || "";
        if (
          rawAnyOverlayRe.test(templateOnly) ||
          rawDialogElementRe.test(templateOnly)
        ) {
          rawOverlayViolations.push(path.relative(repoRoot, file));
        }
      }
      expect(rawOverlayViolations).toEqual([]);

      const eslintConfigContent = fs.readFileSync(
        path.join(repoRoot, "eslint.config.mjs"),
        "utf-8"
      );
      expect(eslintConfigContent).not.toContain(
        "LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST"
      );
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
            "components/fileshare/ShareDrawer.vue"
          ),
          code: `<template>
  <div v-if="open" class="drawer-overlay" role="presentation" @click.self="open = false">
    <aside ref="drawerRef" class="drawer-panel" role="dialog" aria-modal="true" aria-label="Raw Drawer">Body</aside>
  </div>
</template>
<script setup>
import { ref } from "vue";
import { useFocusTrap } from "ui";
const open = ref(true);
const drawerRef = ref(null);
useFocusTrap(drawerRef, { isOpen: open });
</script>
`,
        },
        {
          filePath: path.join(portalSrcDir, "components/SyncDrawer.vue"),
          code: `<template>
  <div v-if="open" class="modal-backdrop" role="dialog" aria-modal="true" aria-label="Raw Portal Modal">
    <div ref="modalRef" class="modal-card">Body</div>
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
});
