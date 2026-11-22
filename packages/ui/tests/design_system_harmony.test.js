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
  "ClinicalFieldLayout",
  "ClinicalFormField",
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
    it("exposes all 20 shared Vue components from root index.js with zero unexported SFCs", () => {
      const discoveredVueNames = walkFiles(path.join(uiDir, "src/components"), [
        ".vue",
      ])
        .map((f) => path.basename(f, ".vue"))
        .sort();

      expect(discoveredVueNames).toEqual(
        [...EXPECTED_SHARED_COMPONENTS].sort()
      );
      expect(EXPECTED_SHARED_COMPONENTS).toHaveLength(20);
      for (const name of EXPECTED_SHARED_COMPONENTS) {
        expect(rootUi[name], `root index.js missing ${name}`).toBeDefined();
      }
    });

    it("exposes all 3 keyboard interaction helpers and useAsyncRetry from root index.js", () => {
      expect(EXPECTED_KEYBOARD_HELPERS).toHaveLength(3);
      for (const helperName of [
        ...EXPECTED_KEYBOARD_HELPERS,
        "useAsyncRetry",
      ]) {
        expect(
          rootUi[helperName],
          `root index.js missing ${helperName}`
        ).toBeTypeOf("function");
      }
    });

    it("declares complete TypeScript definitions for all 20 components and 3 keyboard helpers in index.d.ts", () => {
      const dts = fs.readFileSync(path.join(uiDir, "index.d.ts"), "utf-8");
      for (const componentName of EXPECTED_SHARED_COMPONENTS) {
        expect(dts).toMatch(
          new RegExp(`export (?:declare )?const ${componentName}:`)
        );
      }
      for (const helperName of [
        ...EXPECTED_KEYBOARD_HELPERS,
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
    const webStyleCss = fs.readFileSync(
      path.join(webSrcDir, "style.css"),
      "utf-8"
    );
    const portalStyleCss = fs.readFileSync(
      path.join(portalSrcDir, "style.css"),
      "utf-8"
    );

    it("standardizes shared :root semantic aliases identically across clinician and participant stylesheets", () => {
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
        expect(webAliases[alias], `web missing ${alias}`).toBeDefined();
        expect(portalAliases[alias], `portal missing ${alias}`).toBeDefined();
        expect(webAliases[alias]).toBe(portalAliases[alias]);
        expect(webAliases[alias]).toMatch(/^var\(--semantic-/);
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

    it("achieves >= 4.5:1 WCAG AA contrast on portal .sync-badge and clinician CRF designer badges/buttons", () => {
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
        { name: ".btn-primary", re: /\.btn-primary\s*\{/ },
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
    it("enforces font-variant-numeric: tabular-nums in tokens.css, ClinicalDataTable.vue, and all 52+ application tables", () => {
      const tokensCss = fs.readFileSync(
        path.join(uiDir, "tokens.css"),
        "utf-8"
      );
      expect(tokensCss).toContain("font-variant-numeric: tabular-nums");

      const dataTableVue = fs.readFileSync(
        path.join(uiDir, "src/components/clinical/ClinicalDataTable.vue"),
        "utf-8"
      );
      expect(dataTableVue).toContain("font-variant-numeric: tabular-nums");

      const allVueFiles = [
        ...walkFiles(path.join(uiDir, "src"), [".vue"]),
        ...walkFiles(webSrcDir, [".vue"]),
        ...walkFiles(portalSrcDir, [".vue"]),
      ];
      const tableVueFiles = allVueFiles.filter((file) =>
        /<table[\s>]/.test(fs.readFileSync(file, "utf-8"))
      );

      const webStyleCss = fs.readFileSync(
        path.join(webSrcDir, "style.css"),
        "utf-8"
      );
      const portalStyleCss = fs.readFileSync(
        path.join(portalSrcDir, "style.css"),
        "utf-8"
      );
      expect(webStyleCss).toMatch(
        /\.clinical-visit-matrix[\s\S]*?font-variant-numeric:\s*tabular-nums/
      );
      expect(portalStyleCss).toMatch(
        /\.compliance-history-table[\s\S]*?font-variant-numeric:\s*tabular-nums/
      );

      expect(tableVueFiles.length + 2).toBeGreaterThanOrEqual(52);
      const missingTabularNums = tableVueFiles
        .filter(
          (file) => !fs.readFileSync(file, "utf-8").includes("tabular-nums")
        )
        .map((file) => path.relative(repoRoot, file));

      expect(missingTabularNums).toEqual([]);
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

    it("retains 0 vuejs-accessibility disable comments across packages/ui, subject-portal, and live web routes", async () => {
      const a11yDisableRe = /eslint-disable[^\r\n]*vuejs-accessibility/;

      const uiSourceFiles = walkFiles(path.join(uiDir, "src"), [
        ".vue",
        ".js",
        ".ts",
      ]);
      const portalSourceFiles = walkFiles(portalSrcDir, [".vue", ".js", ".ts"]);

      for (const file of [...uiSourceFiles, ...portalSourceFiles]) {
        const relPath = path.relative(repoRoot, file);
        expect(
          a11yDisableRe.test(fs.readFileSync(file, "utf-8")),
          `${relPath} must retain 0 vuejs-accessibility disable comments`
        ).toBe(false);
      }

      const routerIndexPath = path.join(webSrcDir, "router/index.js");
      const availabilitySource = fs.readFileSync(
        path.join(webSrcDir, "router/availability.js"),
        "utf-8"
      );
      const routerSource = fs.readFileSync(routerIndexPath, "utf-8");

      const parseSetItems = (varName) => {
        const m = availabilitySource.match(
          new RegExp(
            `export const ${varName} = new Set\\(\\[([\\s\\S]*?)\\]\\)`
          )
        );
        if (!m) return [];
        return Array.from(m[1].matchAll(/"([^"]+)"/g), (hit) => hit[1]);
      };

      const liveRouteNames = new Set([
        ...parseSetItems("AVAILABLE_SCREENS"),
        ...parseSetItems("ALWAYS_AVAILABLE"),
      ]);
      expect(liveRouteNames.size).toBeGreaterThanOrEqual(13);

      const routeViewEntrypoints = [path.join(webSrcDir, "App.vue")];
      for (const block of routerSource.matchAll(
        /name:\s*"([^"]+)"[\s\S]*?import\("([^"]+)"\)/g
      )) {
        const [, routeName, importSpec] = block;
        if (liveRouteNames.has(routeName)) {
          routeViewEntrypoints.push(
            path.resolve(path.dirname(routerIndexPath), importSpec)
          );
        }
      }

      const resolveLocalImport = (fromFile, spec) => {
        if (!spec.startsWith(".")) return null;
        const base = path.resolve(path.dirname(fromFile), spec);
        for (const candidate of [
          base,
          `${base}.vue`,
          `${base}.js`,
          `${base}.ts`,
          path.join(base, "index.js"),
          path.join(base, "index.ts"),
        ]) {
          if (fs.existsSync(candidate) && fs.statSync(candidate).isFile()) {
            return candidate;
          }
        }
        return null;
      };

      const visitedLiveFiles = new Set();
      const queue = [...routeViewEntrypoints];
      while (queue.length > 0) {
        const cur = queue.shift();
        if (
          !cur ||
          cur === routerIndexPath ||
          visitedLiveFiles.has(cur) ||
          !fs.existsSync(cur)
        ) {
          continue;
        }
        visitedLiveFiles.add(cur);
        const content = fs.readFileSync(cur, "utf-8");
        for (const imp of content.matchAll(
          /(?:import\s+(?:[\s\S]*?from\s+)?|import\s*\(\s*)["']([^"']+)["']/g
        )) {
          const resolved = resolveLocalImport(cur, imp[1]);
          if (
            resolved &&
            resolved !== routerIndexPath &&
            !visitedLiveFiles.has(resolved)
          ) {
            queue.push(resolved);
          }
        }
      }

      const liveDisableViolations = [...visitedLiveFiles]
        .filter((file) => a11yDisableRe.test(fs.readFileSync(file, "utf-8")))
        .map((file) => path.relative(repoRoot, file));
      expect(liveDisableViolations).toEqual([]);

      const { ESLint } = await import("eslint");
      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });
      const unusedDisableCheck = await eslint.lintText(
        `<template>
  <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
  <button type="button">Clean button</button>
</template>
`,
        { filePath: path.join(webSrcDir, "views/__SyntheticUnusedDisable.vue") }
      );
      expect(
        unusedDisableCheck[0].messages.some(
          (m) => m.severity === 2 && m.message.includes("Unused eslint-disable")
        )
      ).toBe(true);
    });
  });
});
