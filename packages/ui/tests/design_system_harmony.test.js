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
  "ClinicalDataTable",
  "ClinicalDrawer",
  "ClinicalDropzone",
  "ClinicalFieldLayout",
  "ClinicalFormField",
  "ClinicalIconButton",
  "ClinicalInput",
  "ClinicalLookupInput",
  "ClinicalModal",
  "ClinicalQueryFlag",
  "ClinicalQueryPanel",
  "ClinicalRadioGroup",
  "ConfidenceBadge",
  "EmptyState",
  "FileUploadModal",
  "MediaPlayer",
  "ModalErrorAlert",
  "PersonaSwitcher",
  "SignOutConfirmModal",
];

const EXPECTED_KEYBOARD_HELPERS = [
  "useFocusTrap",
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
    it("exposes all 22 shared Vue components from root index.js with zero unexported SFCs", () => {
      const discoveredVueNames = walkFiles(path.join(uiDir, "src/components"), [
        ".vue",
      ])
        .map((f) => path.basename(f, ".vue"))
        .sort();

      expect(discoveredVueNames).toEqual(
        [...EXPECTED_SHARED_COMPONENTS].sort()
      );
      expect(EXPECTED_SHARED_COMPONENTS).toHaveLength(22);
      for (const name of EXPECTED_SHARED_COMPONENTS) {
        expect(rootUi[name], `root index.js missing ${name}`).toBeDefined();
      }
    });

    it("exposes all 3 keyboard interaction helpers, createSrOnlyActionButton, and useAsyncRetry from root index.js", () => {
      expect(EXPECTED_KEYBOARD_HELPERS).toHaveLength(3);
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

    it("declares complete TypeScript definitions for all 22 components and helpers in index.d.ts", () => {
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
            /\.(status-[a-z-]+|validation-[a-z-]+|sync-badge|has-error)[^{]*\{[^}]*\}/g
          ) || [];
        for (const block of statusBlocks) {
          expect(block).not.toContain("--semantic-color-ai");
        }
      }
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

      const webRulesToVerify = [
        { name: ".btn-add-field", re: /\.btn-add-field\s*\{/ },
        {
          name: ".empty-dropzone-placeholder",
          re: /\.empty-dropzone-placeholder\s*\{/,
        },
        { name: ".sdtm-tag-badge", re: /\.sdtm-tag-badge\s*\{/ },
        { name: ".status-n-a", re: /\.status-n-a\s*\{/ },
        { name: ".section-field-count", re: /\.section-field-count\s*\{/ },
        {
          name: ".clinical-soa-matrix th.arm-header",
          re: /\.clinical-soa-matrix\s+th\.arm-header\s*\{/,
        },
      ];

      for (const { name, re } of webRulesToVerify) {
        const props = parseRuleProps(webStyleCss, re);
        expect(props, `Rule not found: ${name}`).not.toBeNull();
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

  describe("Requirement 6: Tabular Numeral Alignment Across Shared & Application Data Tables", () => {
    it("enforces global font-variant-numeric: tabular-nums in tokens.css and ClinicalDataTable.vue with zero redundant app scoped table blocks", () => {
      const tokensCss = fs.readFileSync(
        path.join(uiDir, "tokens.css"),
        "utf-8"
      );
      const globalTableMatch = tokensCss.match(
        /table,\s*th,\s*td,\s*\.clinical-table,\s*\.clinical-visit-matrix,\s*\.data-table\s*\{([\s\S]*?)\}/
      );
      expect(globalTableMatch).not.toBeNull();
      expect(globalTableMatch[1]).toContain(
        "font-variant-numeric: tabular-nums"
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

      const appVueFiles = [
        ...walkFiles(webSrcDir, [".vue"]),
        ...walkFiles(portalSrcDir, [".vue"]),
      ];
      const allVueFiles = [
        ...walkFiles(path.join(uiDir, "src"), [".vue"]),
        ...appVueFiles,
      ];
      const tableVueFiles = allVueFiles.filter((file) =>
        /<table[\s>]/.test(fs.readFileSync(file, "utf-8"))
      );
      expect(tableVueFiles.length + 2).toBeGreaterThanOrEqual(52);

      // Ensure no table component overrides global tabular-nums with normal numerals
      const overridingTabularNums = tableVueFiles
        .filter((file) =>
          /font-variant-numeric:\s*normal\b/.test(
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
    });
  });

  describe("Requirement 7: Shared Clinical Overlay Primitives & AST Backdrop Guardrails (ADR-2545)", () => {
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

    it("migrates all 20 formerly unguarded apps/web overlay components to ClinicalModal or ClinicalDrawer and removes all backdrop suppressions", () => {
      for (const relPath of MIGRATED_WEB_OVERLAY_FILES) {
        const fullPath = path.join(webSrcDir, relPath);
        const content = fs.readFileSync(fullPath, "utf-8");
        expect(
          /\b(?:ClinicalModal|ClinicalDrawer)\b/.test(content),
          `${relPath} must use ClinicalModal or ClinicalDrawer from packages/ui`
        ).toBe(true);
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

    it("enforces prefers-reduced-motion rules on shared ClinicalModal and ClinicalDrawer surfaces", () => {
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
      expect(drawerVue).toMatch(
        /@media\s*\(\s*prefers-reduced-motion:\s*reduce\s*\)/
      );
    });

    it("rejects raw overlay divs and vuejs-accessibility backdrop suppressions via custom-vue/no-raw-overlays in eslint.config.mjs", async () => {
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
  });
});
