import { describe, it, expect } from "vitest";
import { ESLint } from "eslint";
import fs from "fs";
import path from "path";

function parseRootTokens(cssContent) {
  const rootMatch = cssContent.match(/:root\s*\{([\s\S]*?)\}/);
  if (!rootMatch) {
    throw new Error("Could not locate :root block in tokens.css");
  }
  const rawTokens = new Map();
  const cleanRoot = rootMatch[1].replace(/\/\*[\s\S]*?\*\//g, "");
  for (const match of cleanRoot.matchAll(
    /(--[a-zA-Z0-9_-]+)\s*:\s*([^;]+);/g
  )) {
    rawTokens.set(match[1], match[2].trim());
  }

  function resolveValue(tokenName, seen = new Set()) {
    if (seen.has(tokenName)) {
      throw new Error(`Circular token reference: ${tokenName}`);
    }
    const raw = rawTokens.get(tokenName);
    if (!raw) {
      throw new Error(`Undefined token in tokens.css: ${tokenName}`);
    }
    const varMatch = raw.match(/^var\(\s*(--[a-zA-Z0-9_-]+)\s*\)$/);
    if (varMatch) {
      const nextSeen = new Set(seen);
      nextSeen.add(tokenName);
      return resolveValue(varMatch[1], nextSeen);
    }
    return raw;
  }

  const resolved = new Map();
  for (const key of rawTokens.keys()) {
    resolved.set(key, resolveValue(key));
  }
  return resolved;
}

function parseThemeOverlayTokens(cssContent, blockRegex, baseTokens) {
  const match = cssContent.match(blockRegex);
  if (!match) {
    throw new Error(`Could not locate theme block matching ${blockRegex}`);
  }
  const rawOverlay = new Map();
  const cleanBlock = match[1].replace(/\/\*[\s\S]*?\*\//g, "");
  for (const m of cleanBlock.matchAll(/(--[a-zA-Z0-9_-]+)\s*:\s*([^;]+);/g)) {
    rawOverlay.set(m[1], m[2].trim());
  }

  function resolveOverlayValue(tokenName, seen = new Set()) {
    if (seen.has(tokenName)) {
      throw new Error(`Circular token reference: ${tokenName}`);
    }
    const raw = rawOverlay.has(tokenName)
      ? rawOverlay.get(tokenName)
      : baseTokens.get(tokenName);
    if (!raw) {
      throw new Error(`Undefined token in theme overlay: ${tokenName}`);
    }
    const varMatch = raw.match(/^var\(\s*(--[a-zA-Z0-9_-]+)\s*\)$/);
    if (varMatch) {
      const nextSeen = new Set(seen);
      nextSeen.add(tokenName);
      return resolveOverlayValue(varMatch[1], nextSeen);
    }
    return raw;
  }

  const merged = new Map(baseTokens);
  for (const key of new Set([...baseTokens.keys(), ...rawOverlay.keys()])) {
    merged.set(key, resolveOverlayValue(key));
  }
  return merged;
}

function hexToRgb(hex) {
  const normalized = hex.trim().replace(/^#/, "");
  const expanded =
    normalized.length === 3
      ? normalized
          .split("")
          .map((c) => c + c)
          .join("")
      : normalized;
  if (!/^[0-9a-fA-F]{6}$/.test(expanded)) {
    throw new Error(`Invalid 6-digit hex color: ${hex}`);
  }
  return [
    parseInt(expanded.slice(0, 2), 16),
    parseInt(expanded.slice(2, 4), 16),
    parseInt(expanded.slice(4, 6), 16),
  ];
}

function relativeLuminance(hex) {
  const [r, g, b] = hexToRgb(hex).map((channel) => {
    const srgb = channel / 255;
    return srgb <= 0.04045 ? srgb / 12.92 : ((srgb + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrastRatio(hexA, hexB) {
  const lumA = relativeLuminance(hexA);
  const lumB = relativeLuminance(hexB);
  const lighter = Math.max(lumA, lumB);
  const darker = Math.min(lumA, lumB);
  return (lighter + 0.05) / (darker + 0.05);
}

describe("CSS Assets and Package Exports", () => {
  const uiDir = path.resolve(__dirname, "..");
  const repoRoot = path.resolve(uiDir, "../..");

  it("should have correct exports in package.json", () => {
    const pkgPath = path.join(uiDir, "package.json");
    const pkg = JSON.parse(fs.readFileSync(pkgPath, "utf-8"));

    expect(pkg.exports).toBeDefined();
    expect(pkg.exports["."]).toBe("./index.js");
    expect(pkg.exports["./tokens.css"]).toBe("./tokens.css");
    expect(pkg.exports["./responsive.css"]).toBe("./responsive.css");
    expect(pkg.exports["./package.json"]).toBe("./package.json");
  });

  it("should have tokens.css with correct root custom properties", () => {
    const tokensPath = path.join(uiDir, "tokens.css");
    expect(fs.existsSync(tokensPath)).toBe(true);

    const content = fs.readFileSync(tokensPath, "utf-8");
    expect(content).toContain(":root");
    expect(content).toContain("--color-primary");
    expect(content).toContain("--color-accent");
    expect(content).toContain("--color-success");
    expect(content).toContain("--color-warning");
    expect(content).toContain("--color-error");
    expect(content).toContain("--color-surface");
    expect(content).toContain("--color-text");
    expect(content).toContain("--color-border-control");
    expect(content).toContain("--semantic-color-border-control");
    expect(content).toContain("--spacing-md");
    expect(content).toContain("--font-size-base");
    expect(content).toContain("--breakpoint-tablet");
    expect(content).toContain("--touch-target-min");
    expect(content).toContain("--control-target-min");
    expect(content).toContain("--semantic-touch-target-min");
    expect(content).toContain("--semantic-control-target-min");
    expect(content).toContain("--drawer-width-sm");
    expect(content).toContain("--drawer-width-md");
    expect(content).toContain("--drawer-width-lg");
    expect(content).toContain("--drawer-width-xl");
    expect(content).toContain("--semantic-drawer-width-sm");
    expect(content).toContain("--semantic-drawer-width-md");
    expect(content).toContain("--semantic-drawer-width-lg");
    expect(content).toContain("--semantic-drawer-width-xl");
  });

  it("should have responsive.css with all required utility classes", () => {
    const responsivePath = path.join(uiDir, "responsive.css");
    expect(fs.existsSync(responsivePath)).toBe(true);

    const content = fs.readFileSync(responsivePath, "utf-8");
    expect(content).toContain(".responsive-grid");
    expect(content).toContain(".grid-layout-responsive");
    expect(content).toContain(".grid-2-responsive");
    expect(content).toContain(".container");
    expect(content).toContain(".touch-target");
    expect(content).toContain(".scrollable-table-wrapper");
    expect(content).toContain("@media (max-width: 640px)");
    expect(content).toContain(".drawer-mobile-full");
    expect(content).toContain(".drawer-panel-mobile-full");
  });

  describe("WCAG 2.2 Relative Luminance Contrast Gates", () => {
    const tokensPath = path.join(uiDir, "tokens.css");
    const tokensContent = fs.readFileSync(tokensPath, "utf-8");
    const tokens = parseRootTokens(tokensContent);
    const darkTokens = parseThemeOverlayTokens(
      tokensContent,
      /:root\[data-theme="dark"\][\s\S]*?\{([\s\S]*?)\}/,
      tokens
    );
    const lightSurfaces = ["#ffffff", "#f8fafc", "#f1f5f9", "#eff6ff"];
    const darkSurfaces = ["#0f172a", "#1e293b", "#1e293d", "#172554"];

    it("delivers >= 4.5:1 contrast for all semantic text and interactive foreground tokens across light and dark surfaces (SC 1.4.3 / G18)", () => {
      const normalTextTokens = [
        "--semantic-color-text",
        "--semantic-color-text-dark",
        "--semantic-color-text-body",
        "--semantic-color-text-muted",
        "--semantic-color-text-secondary",
        "--semantic-color-text-subtle",
        "--semantic-color-primary",
        "--semantic-color-primary-dark",
        "--semantic-color-accent",
        "--semantic-color-interactive-primary",
        "--semantic-color-interactive-primary-hover",
        "--semantic-color-interactive-brand",
        "--semantic-color-info",
        "--semantic-color-success",
        "--semantic-color-success-dark",
        "--semantic-color-success-interactive",
        "--semantic-color-success-accent",
        "--semantic-color-warning",
        "--semantic-color-warning-text",
        "--semantic-color-warning-text-dark",
        "--semantic-color-warning-dark",
        "--semantic-color-warning-action",
        "--semantic-color-error",
        "--semantic-color-error-dark",
        "--semantic-color-error-text-dark",
        "--semantic-color-error-text-deep",
        "--semantic-color-ai",
        "--semantic-color-ai-dark",
        "--semantic-color-teal-text",
        "--semantic-color-indigo-text",
        "--semantic-color-purple-text",
        "--semantic-color-orange-text",
        "--semantic-color-rose-text",
        "--semantic-color-pink-text",
      ];

      for (const tokenName of normalTextTokens) {
        const lightFgHex = tokens.get(tokenName);
        expect(lightFgHex, `Light ${tokenName} must be defined`).toBeDefined();
        for (const bgHex of lightSurfaces) {
          const ratio = contrastRatio(lightFgHex, bgHex);
          expect(
            ratio,
            `Light ${tokenName} (${lightFgHex}) on ${bgHex} achieved ${ratio.toFixed(2)}:1 (< 4.5:1)`
          ).toBeGreaterThanOrEqual(4.5);
        }

        const darkFgHex = darkTokens.get(tokenName);
        expect(darkFgHex, `Dark ${tokenName} must be defined`).toBeDefined();
        for (const bgHex of darkSurfaces) {
          const ratio = contrastRatio(darkFgHex, bgHex);
          expect(
            ratio,
            `Dark ${tokenName} (${darkFgHex}) on ${bgHex} achieved ${ratio.toFixed(2)}:1 (< 4.5:1)`
          ).toBeGreaterThanOrEqual(4.5);
        }
      }
    });

    it("delivers >= 4.5:1 normal-text contrast and >= 3:1 large-scale figure contrast (G145) for filled buttons, badges, and wallet gradient cards in light and dark themes", () => {
      const onPrimaryFills = [
        "--semantic-color-primary",
        "--semantic-color-interactive-primary",
        "--semantic-color-interactive-primary-hover",
        "--semantic-color-accent",
        "--semantic-color-ai",
      ];
      const onSolidFills = [
        "--semantic-color-primary-dark",
        "--semantic-color-warning-dark",
        "--semantic-color-warning-action",
        "--semantic-color-success",
        "--semantic-color-success-dark",
        "--semantic-color-success-interactive",
        "--semantic-color-success-accent",
        "--semantic-color-error",
        "--semantic-color-wallet-start",
        "--semantic-color-wallet-end",
      ];

      for (const [mode, map] of [
        ["light", tokens],
        ["dark", darkTokens],
      ]) {
        const textOnPrimary = map.get("--semantic-color-text-on-primary");
        for (const bgToken of onPrimaryFills) {
          const bgHex = map.get(bgToken);
          const ratio = contrastRatio(textOnPrimary, bgHex);
          expect(
            ratio,
            `${mode} --semantic-color-text-on-primary (${textOnPrimary}) on ${bgToken} (${bgHex}) achieved ${ratio.toFixed(2)}:1 (< 4.5:1)`
          ).toBeGreaterThanOrEqual(4.5);
        }

        const textOnSolid = map.get("--semantic-color-text-on-solid");
        for (const bgToken of onSolidFills) {
          const bgHex = map.get(bgToken);
          const ratio = contrastRatio(textOnSolid, bgHex);
          expect(
            ratio,
            `${mode} --semantic-color-text-on-solid (${textOnSolid}) on ${bgToken} (${bgHex}) achieved ${ratio.toFixed(2)}:1 (< 4.5:1)`
          ).toBeGreaterThanOrEqual(4.5);
        }
      }

      // Large-scale heading and wallet gradient tokens against light & dark surfaces (G145 >= 3:1)
      for (const largeToken of [
        "--semantic-color-wallet-start",
        "--semantic-color-wallet-end",
        "--semantic-color-primary",
        "--semantic-color-primary-dark",
      ]) {
        const lightFgHex = tokens.get(largeToken);
        for (const bgHex of lightSurfaces) {
          expect(contrastRatio(lightFgHex, bgHex)).toBeGreaterThanOrEqual(3);
        }
        const darkFgHex = darkTokens.get(largeToken);
        for (const bgHex of darkSurfaces) {
          expect(contrastRatio(darkFgHex, bgHex)).toBeGreaterThanOrEqual(3);
        }
      }
    });

    it("delivers >= 4.5:1 contrast for semantic status badge foreground/background pairs and inverse chrome tokens across light and dark themes", () => {
      const badgePairs = [
        ["--semantic-color-success", "--semantic-color-success-bg"],
        ["--semantic-color-success-dark", "--semantic-color-success-bg"],
        ["--semantic-color-success", "--semantic-color-success-bg-soft"],
        ["--semantic-color-warning", "--semantic-color-warning-bg"],
        ["--semantic-color-warning-dark", "--semantic-color-warning-bg-badge"],
        ["--semantic-color-warning", "--semantic-color-warning-bg-soft"],
        ["--semantic-color-warning", "--semantic-color-warning-bg-light"],
        ["--semantic-color-error", "--semantic-color-error-bg"],
        ["--semantic-color-error-text-dark", "--semantic-color-error-bg"],
        ["--semantic-color-error", "--semantic-color-error-bg-soft"],
        ["--semantic-color-error", "--semantic-color-error-bg-row"],
        ["--semantic-color-info", "--semantic-color-info-bg"],
        ["--semantic-color-info", "--semantic-color-info-bg-soft"],
        ["--semantic-color-info", "--semantic-color-info-light"],
        ["--semantic-color-primary", "--semantic-color-primary-light"],
        ["--semantic-color-primary-dark", "--semantic-color-primary-light"],
        ["--semantic-color-ai", "--semantic-color-ai-bg"],
        ["--semantic-color-ai-dark", "--semantic-color-ai-bg"],
        ["--semantic-color-text-inverse", "--semantic-color-surface-inverse"],
        [
          "--semantic-color-text-inverse-muted",
          "--semantic-color-surface-inverse",
        ],
        [
          "--semantic-color-text-inverse-muted",
          "--semantic-color-surface-elevated",
        ],
        ["--semantic-color-accent-inverse", "--semantic-color-surface-inverse"],
        [
          "--semantic-color-emerald-inverse-text",
          "--semantic-color-surface-inverse",
        ],
      ];

      for (const [mode, map] of [
        ["light", tokens],
        ["dark", darkTokens],
      ]) {
        for (const [fgToken, bgToken] of badgePairs) {
          const fgHex = map.get(fgToken);
          const bgHex = map.get(bgToken);
          const ratio = contrastRatio(fgHex, bgHex);
          expect(
            ratio,
            `${mode} ${fgToken} (${fgHex}) on ${bgToken} (${bgHex}) achieved ${ratio.toFixed(2)}:1 (< 4.5:1)`
          ).toBeGreaterThanOrEqual(4.5);
        }
      }
    });

    it("delivers >= 3:1 non-text boundary contrast for control, AI, and semantic status borders across light and dark themes while keeping passive --semantic-color-border subtle (SC 1.4.11)", () => {
      const lightControlBorder = tokens.get("--semantic-color-border-control");
      const lightPassiveBorder = tokens.get("--semantic-color-border");
      expect(lightControlBorder).toBe(tokens.get("--color-border-control"));
      expect(lightPassiveBorder).toBe("#e2e8f0");
      expect(
        contrastRatio(
          lightPassiveBorder,
          tokens.get("--semantic-color-surface")
        )
      ).toBeLessThan(3);

      for (const bgHex of lightSurfaces) {
        const ratio = contrastRatio(lightControlBorder, bgHex);
        expect(
          ratio,
          `light --semantic-color-border-control (${lightControlBorder}) on ${bgHex} achieved ${ratio.toFixed(2)}:1 (< 3:1)`
        ).toBeGreaterThanOrEqual(3);
      }

      const darkControlBorder = darkTokens.get(
        "--semantic-color-border-control"
      );
      const darkPassiveBorder = darkTokens.get("--semantic-color-border");
      expect(darkControlBorder).toBe(darkTokens.get("--color-border-control"));
      expect(darkPassiveBorder).toBe("#334155");
      expect(
        contrastRatio(
          darkPassiveBorder,
          darkTokens.get("--semantic-color-surface")
        )
      ).toBeLessThan(3);

      for (const bgHex of darkSurfaces) {
        const ratio = contrastRatio(darkControlBorder, bgHex);
        expect(
          ratio,
          `dark --semantic-color-border-control (${darkControlBorder}) on ${bgHex} achieved ${ratio.toFixed(2)}:1 (< 3:1)`
        ).toBeGreaterThanOrEqual(3);
      }

      const semanticBorderPairs = [
        ["--semantic-color-ai-border", "--semantic-color-ai-bg"],
        ["--semantic-color-info-border", "--semantic-color-info-bg"],
        ["--semantic-color-info-border-accent", "--semantic-color-info-bg"],
        ["--semantic-color-warning-border", "--semantic-color-warning-bg"],
        [
          "--semantic-color-warning-border-strong",
          "--semantic-color-warning-bg",
        ],
        [
          "--semantic-color-warning-action-border",
          "--semantic-color-warning-bg",
        ],
        ["--semantic-color-success-border", "--semantic-color-success-bg"],
        [
          "--semantic-color-success-border-soft",
          "--semantic-color-success-bg-soft",
        ],
        ["--semantic-color-error-border", "--semantic-color-error-bg"],
        [
          "--semantic-color-error-border-soft",
          "--semantic-color-error-bg-soft",
        ],
      ];

      for (const [mode, map] of [
        ["light", tokens],
        ["dark", darkTokens],
      ]) {
        const primarySurfaceHex = map.get("--semantic-color-surface");
        for (const [borderToken, statusBgToken] of semanticBorderPairs) {
          const borderHex = map.get(borderToken);
          const statusBgHex = map.get(statusBgToken);

          const surfaceRatio = contrastRatio(borderHex, primarySurfaceHex);
          expect(
            surfaceRatio,
            `${mode} ${borderToken} (${borderHex}) on --semantic-color-surface (${primarySurfaceHex}) achieved ${surfaceRatio.toFixed(2)}:1 (< 3:1)`
          ).toBeGreaterThanOrEqual(3);

          const statusBgRatio = contrastRatio(borderHex, statusBgHex);
          expect(
            statusBgRatio,
            `${mode} ${borderToken} (${borderHex}) on ${statusBgToken} (${statusBgHex}) achieved ${statusBgRatio.toFixed(2)}:1 (< 3:1)`
          ).toBeGreaterThanOrEqual(3);
        }
      }
    });

    it("standardizes 24px --control-target-min while preserving 48px --touch-target-min (SC 2.5.8 / SC 2.5.5)", () => {
      expect(tokens.get("--control-target-min")).toBe("24px");
      expect(tokens.get("--semantic-control-target-min")).toBe("24px");
      expect(tokens.get("--touch-target-min")).toBe("48px");
      expect(tokens.get("--semantic-touch-target-min")).toBe("48px");
      expect(tokensContent).toContain("@media (forced-colors: active)");
      expect(tokensContent).toContain("outline: 2px solid Highlight");
    });

    it("delivers >= 3:1 (specifically >= 6.34:1) dual focus-visible ring contrast across #ffffff, #f8fafc, #026597, #1e293b, #0f172a, and #090d16 (SC 2.4.7 / SC 1.4.11 / C40)", () => {
      const requiredSurfaces = [
        "#ffffff",
        "#f8fafc",
        "#026597",
        "#1e293b",
        "#0f172a",
        "#090d16",
      ];

      const innerHaloHex = tokens.get("--semantic-color-text-inverse");
      const lightOuterOutlineHex = tokens.get("--semantic-color-accent");
      expect(innerHaloHex).toBe("#ffffff");
      expect(lightOuterOutlineHex).toBe("#4338ca");

      // Adjacent contrast between inner halo (#ffffff) and outer outline (#4338ca) exceeds 3:1 (8.69:1)
      expect(
        contrastRatio(innerHaloHex, lightOuterOutlineHex)
      ).toBeGreaterThanOrEqual(3);

      // Across all six light, brand, and dark surfaces, at least one ring of the two-color indicator achieves >= 6.34:1 (>= 3:1 SC 1.4.11)
      for (const surfaceHex of requiredSurfaces) {
        const innerContrast = contrastRatio(innerHaloHex, surfaceHex);
        const outerContrast = contrastRatio(lightOuterOutlineHex, surfaceHex);
        const dualRingContrast = Math.max(innerContrast, outerContrast);
        expect(
          dualRingContrast,
          `Light-theme dual focus ring (${innerHaloHex} + ${lightOuterOutlineHex}) on ${surfaceHex} achieved ${dualRingContrast.toFixed(2)}:1 (< 3:1)`
        ).toBeGreaterThanOrEqual(6.34);
      }

      // Dark-theme token overlay: --semantic-color-text-inverse (#ffffff) + --semantic-color-accent (#a5b4fc)
      const darkInnerHaloHex = darkTokens.get("--semantic-color-text-inverse");
      const darkOuterOutlineHex = darkTokens.get("--semantic-color-accent");
      expect(darkInnerHaloHex).toBe("#ffffff");
      expect(darkOuterOutlineHex).toBe("#a5b4fc");

      for (const darkOrBrandSurface of [
        "#026597",
        "#1e293b",
        "#0f172a",
        "#090d16",
      ]) {
        const dualRingContrast = Math.max(
          contrastRatio(darkInnerHaloHex, darkOrBrandSurface),
          contrastRatio(darkOuterOutlineHex, darkOrBrandSurface)
        );
        expect(
          dualRingContrast,
          `Dark-theme dual focus ring (${darkInnerHaloHex} + ${darkOuterOutlineHex}) on ${darkOrBrandSurface} achieved ${dualRingContrast.toFixed(2)}:1 (< 3:1)`
        ).toBeGreaterThanOrEqual(6.34);
      }

      // Dark-theme outer accent (#a5b4fc) also independently exceeds 3:1 against dark surfaces
      for (const darkSurface of ["#1e293b", "#0f172a", "#090d16"]) {
        expect(
          contrastRatio(darkOuterOutlineHex, darkSurface)
        ).toBeGreaterThanOrEqual(3);
      }
    });
  });

  describe("ESLint Static Token Enforcement Gates", () => {
    it("rejects hardcoded hex colors and undefined CSS variables across Vue templates, Vue <style> blocks, and .css stylesheets", async () => {
      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });

      const [vueResult] = await eslint.lintText(
        `<template>\n  <div style="color: #ff00aa; background: var(--nonexistent-token)">Test</div>\n</template>\n<style scoped>\n.box {\n  border-color: #123456;\n  color: var(--missing-style-var);\n}\n</style>\n`,
        {
          filePath: path.join(
            repoRoot,
            "apps/web/src/components/LintGateProbe.vue"
          ),
        }
      );
      const vueRules = vueResult.messages.map((m) => m.ruleId);
      expect(vueRules).toContain("custom-vue/no-inline-hex");
      expect(vueRules).toContain("custom-vue/no-undefined-css-vars");
      expect(
        vueResult.messages.filter(
          (m) => m.ruleId === "custom-vue/no-inline-hex"
        )
      ).toHaveLength(2);
      expect(
        vueResult.messages.filter(
          (m) => m.ruleId === "custom-vue/no-undefined-css-vars"
        )
      ).toHaveLength(2);

      const [cssResult] = await eslint.lintText(
        `.probe {\n  color: #abcdef;\n  background-color: var(--undefined-sheet-token);\n}\n`,
        {
          filePath: path.join(repoRoot, "apps/web/src/probe.css"),
        }
      );
      const cssRules = cssResult.messages.map((m) => m.ruleId);
      expect(cssRules).toContain("custom-vue/no-inline-hex");
      expect(cssRules).toContain("custom-vue/no-undefined-css-vars");
    });

    it("permits hex color definitions only inside canonical theme blocks in packages/ui/tokens.css and rejects local theme hex overrides elsewhere", async () => {
      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });

      const [tokensCanonicalResult] = await eslint.lintText(
        fs.readFileSync(path.join(uiDir, "tokens.css"), "utf-8"),
        {
          filePath: path.join(uiDir, "tokens.css"),
        }
      );
      expect(tokensCanonicalResult.messages).toEqual([]);

      // Non-theme class selector inside packages/ui/tokens.css must NOT be masked
      const [tokensRogueRuleResult] = await eslint.lintText(
        `:root {\n  --color-primary: #0369a1;\n}\n.dark-card {\n  color: #ff00aa;\n}\n`,
        {
          filePath: path.join(uiDir, "tokens.css"),
        }
      );
      expect(tokensRogueRuleResult.messages.map((m) => m.ruleId)).toContain(
        "custom-vue/no-inline-hex"
      );

      // Consumer stylesheets must NOT define local hex overrides even inside [data-theme="dark"]
      for (const consumerCssPath of [
        path.join(repoRoot, "apps/web/src/style.css"),
        path.join(repoRoot, "apps/subject-portal/src/style.css"),
      ]) {
        const [consumerResult] = await eslint.lintText(
          `[data-theme="dark"] {\n  --local-override: #ff00aa;\n}\n`,
          { filePath: consumerCssPath }
        );
        expect(consumerResult.messages.map((m) => m.ruleId)).toContain(
          "custom-vue/no-inline-hex"
        );
      }
    });

    it("rejects synthetic Vue and CSS fixtures with bare outline: none / outline: 0 without :focus-visible while permitting :focus:not(:focus-visible)", async () => {
      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });

      const [badVueResult] = await eslint.lintText(
        `<template>\n  <button type="button" style="outline: none">Action</button>\n</template>\n<style scoped>\n.suppressed-btn {\n  outline: none;\n}\n.suppressed-input:focus {\n  outline: 0;\n}\n</style>\n`,
        {
          filePath: path.join(
            repoRoot,
            "apps/web/src/components/FocusSuppressionProbe.vue"
          ),
        }
      );
      const badVueFocusMsgs = badVueResult.messages.filter(
        (m) => m.ruleId === "custom-vue/no-suppressed-focus-outline"
      );
      expect(badVueFocusMsgs).toHaveLength(3);

      const [badCssResult] = await eslint.lintText(
        `.toolbar-select {\n  outline: none;\n}\n.btn-compact:focus {\n  outline-width: 0;\n}\n`,
        {
          filePath: path.join(repoRoot, "apps/web/src/focus-probe.css"),
        }
      );
      const badCssFocusMsgs = badCssResult.messages.filter(
        (m) => m.ruleId === "custom-vue/no-suppressed-focus-outline"
      );
      expect(badCssFocusMsgs).toHaveLength(2);

      const [allowedVueResult] = await eslint.lintText(
        `<template>\n  <div class="dialog-panel" tabindex="-1">Dialog</div>\n</template>\n<style scoped>\n.dialog-panel:focus:not(:focus-visible) {\n  outline: none;\n}\n</style>\n`,
        {
          filePath: path.join(
            repoRoot,
            "packages/ui/src/components/clinical/AllowedDialogProbe.vue"
          ),
        }
      );
      expect(
        allowedVueResult.messages.filter(
          (m) => m.ruleId === "custom-vue/no-suppressed-focus-outline"
        )
      ).toHaveLength(0);
    });
  });
});
