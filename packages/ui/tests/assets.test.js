import { describe, it, expect } from "vitest";
import { ESLint } from "eslint";
import fs from "fs";
import path from "path";
import zlib from "zlib";

const WOFF2_KNOWN_TAGS = [
  "cmap",
  "head",
  "hhea",
  "hmtx",
  "maxp",
  "name",
  "OS/2",
  "post",
  "cvt ",
  "fpgm",
  "glyf",
  "loca",
  "prep",
  "CFF ",
  "VORG",
  "EBDT",
  "EBLC",
  "gasp",
  "hdmx",
  "kern",
  "LTSH",
  "PCLT",
  "VDMX",
  "vhea",
  "vmtx",
  "BASE",
  "GDEF",
  "GPOS",
  "GSUB",
  "EBSC",
  "JSTF",
  "MATH",
  "CBDT",
  "CBLC",
  "COLR",
  "CPAL",
  "SVG ",
  "sbix",
  "acnt",
  "avar",
  "bdat",
  "bloc",
  "bsln",
  "cvar",
  "fdsc",
  "feat",
  "fmtx",
  "fvar",
  "gvar",
  "hsty",
  "just",
  "lcar",
  "mort",
  "morx",
  "opbd",
  "prop",
  "trak",
  "Zapf",
  "Silf",
  "Glat",
  "Gloc",
  "Feat",
  "Sill",
];

function readUIntBase128(buf, startOffset) {
  let accum = 0;
  let offset = startOffset;
  for (let i = 0; i < 5; i++) {
    const byte = buf[offset++];
    accum = (accum << 7) | (byte & 0x7f);
    if ((byte & 0x80) === 0) {
      return { value: accum >>> 0, nextOffset: offset };
    }
  }
  throw new Error("Invalid UIntBase128 in WOFF2 table directory");
}

function inspectWoff2Binary(filePath) {
  const buf = fs.readFileSync(filePath);
  const magic = buf.toString("ascii", 0, 4);
  const declaredLength = buf.readUInt32BE(8);
  const numTables = buf.readUInt16BE(12);
  const totalCompressedSize = buf.readUInt32BE(20);

  let offset = 48;
  const tableTags = [];
  for (let i = 0; i < numTables; i++) {
    const flags = buf[offset++];
    const tagIndex = flags & 0x3f;
    let tag;
    if (tagIndex === 63) {
      tag = buf.toString("ascii", offset, offset + 4);
      offset += 4;
    } else {
      tag = WOFF2_KNOWN_TAGS[tagIndex];
    }
    tableTags.push(tag);
    const xformVersion = (flags >> 6) & 0x03;
    offset = readUIntBase128(buf, offset).nextOffset;
    const hasTransformLength =
      tag === "glyf" || tag === "loca"
        ? xformVersion === 0
        : xformVersion !== 0;
    if (hasTransformLength) {
      offset = readUIntBase128(buf, offset).nextOffset;
    }
  }

  const compressedStream = buf.subarray(offset, offset + totalCompressedSize);
  const decompressed = zlib.brotliDecompressSync(compressedStream);
  return {
    magic,
    byteLength: buf.length,
    declaredLength,
    numTables,
    tableTags,
    decompressed,
  };
}

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
    expect(pkg.exports["./fonts.css"]).toBe("./fonts.css");
    expect(pkg.exports["./tokens.css"]).toBe("./tokens.css");
    expect(pkg.exports["./responsive.css"]).toBe("./responsive.css");
    expect(pkg.exports["./package.json"]).toBe("./package.json");
  });

  it("vendors SIL OFL 1.1 Inter v4 and JetBrains Mono variable WOFF2 subsets with OpenType fvar and GSUB cv05/cv08 features", () => {
    const fontsDir = path.join(uiDir, "fonts");
    const fontsCssPath = path.join(uiDir, "fonts.css");
    expect(fs.existsSync(fontsCssPath)).toBe(true);

    const fontsCss = fs.readFileSync(fontsCssPath, "utf-8");
    expect(fontsCss).not.toMatch(/https?:\/\/|url\(\s*['"]?\/\//i);
    expect(fontsCss).toContain('font-family: "Inter"');
    expect(fontsCss).toContain('font-family: "JetBrains Mono"');
    expect(fontsCss).toContain("font-display: swap");
    expect(fontsCss).toMatch(/"cv05"\s+1,\s*"cv08"\s+1/);

    for (const licenseFile of ["Inter-OFL.txt", "JetBrainsMono-OFL.txt"]) {
      const licenseText = fs.readFileSync(
        path.join(fontsDir, licenseFile),
        "utf-8"
      );
      expect(licenseText).toContain("SIL OPEN FONT LICENSE Version 1.1");
    }

    const expectedFonts = [
      {
        file: "InterVariable.woff2",
        features: ["wght", "opsz", "cv05", "cv08", "tnum"],
      },
      {
        file: "InterVariable-Italic.woff2",
        features: ["wght", "opsz", "cv05", "cv08", "tnum"],
      },
      {
        file: "JetBrainsMonoVariable.woff2",
        features: ["wght", "zero"],
      },
      {
        file: "JetBrainsMonoVariable-Italic.woff2",
        features: ["wght", "zero"],
      },
    ];

    for (const { file, features } of expectedFonts) {
      expect(fontsCss).toContain(`./fonts/${file}`);
      const info = inspectWoff2Binary(path.join(fontsDir, file));
      expect(info.magic).toBe("wOF2");
      expect(info.byteLength).toBe(info.declaredLength);
      expect(info.byteLength).toBeGreaterThan(20_000);
      expect(info.tableTags).toContain("fvar");
      expect(info.tableTags).toContain("GSUB");
      for (const tag of features) {
        expect(
          info.decompressed.includes(Buffer.from(tag, "ascii")),
          `${file} missing OpenType tag "${tag}"`
        ).toBe(true);
      }
    }
  });

  it("defines a rem typography scale anchored at 0.75rem floor and 1rem form controls in tokens.css", () => {
    const tokensPath = path.join(uiDir, "tokens.css");
    const rawCss = fs.readFileSync(tokensPath, "utf-8");
    const tokens = parseRootTokens(rawCss);

    expect(rawCss).toContain('@import "./fonts.css";');
    expect(tokens.get("--font-sans")).toMatch(
      /^"Inter",\s*system-ui,\s*-apple-system\b/
    );
    expect(tokens.get("--font-feature-sans")).toBe('"cv05" 1, "cv08" 1');
    expect(tokens.get("--font-mono")).toMatch(
      /^"JetBrains Mono",\s*ui-monospace\b/
    );
    expect(tokens.get("--semantic-font-mono")).toBe(tokens.get("--font-mono"));

    expect(tokens.get("--font-size-2xs")).toBe("0.75rem");
    expect(tokens.get("--font-size-xs")).toBe("0.8125rem");
    expect(tokens.get("--font-size-sm")).toBe("0.875rem");
    expect(tokens.get("--font-size-base")).toBe("0.9375rem");
    expect(tokens.get("--font-size-md")).toBe("1rem");
    expect(tokens.get("--font-size-lg")).toBe("1.125rem");
    expect(tokens.get("--font-size-xl")).toBe("1.375rem");
    expect(tokens.get("--font-size-2xl")).toBe("1.5rem");
    expect(tokens.get("--font-size-3xl")).toBe("1.75rem");
    expect(tokens.get("--semantic-font-size-body")).toBe("0.9375rem");
    expect(tokens.get("--semantic-font-size-control")).toBe("1rem");
    expect(tokens.get("--semantic-font-size-heading")).toBe("1.375rem");

    expect(rawCss).toMatch(
      /\.clinical-badge\s*\{[^}]*font-size:\s*var\(--font-size-2xs\)/
    );
    expect(rawCss).toMatch(
      /\.clinical-input\s+input\[type="text"\][\s\S]*?\.clinical-input\s+\.clinical-input-control\s*\{[^}]*font-size:\s*var\(--semantic-font-size-control\)/
    );
    expect(rawCss).toMatch(
      /\.validation-error-msg\s*\{[^}]*font-size:\s*var\(--font-size-xs\)/
    );
    expect(rawCss).toMatch(
      /\.font-mono\s*\{[^}]*font-family:\s*var\(--font-mono\)/
    );
    expect(rawCss).toMatch(
      /\.tabular-nums\s*\{[^}]*font-variant-numeric:\s*tabular-nums/
    );
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
    const tokens = parseRootTokens(fs.readFileSync(tokensPath, "utf-8"));
    const lightSurfaces = ["#ffffff", "#f8fafc", "#f1f5f9", "#eff6ff"];

    it("delivers >= 4.5:1 contrast for all semantic text and interactive foreground tokens across #ffffff, #f8fafc, #f1f5f9, and #eff6ff (SC 1.4.3 / G18)", () => {
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
        const fgHex = tokens.get(tokenName);
        expect(fgHex, `${tokenName} must be defined`).toBeDefined();
        for (const bgHex of lightSurfaces) {
          const ratio = contrastRatio(fgHex, bgHex);
          expect(
            ratio,
            `${tokenName} (${fgHex}) on ${bgHex} achieved ${ratio.toFixed(2)}:1 (< 4.5:1)`
          ).toBeGreaterThanOrEqual(4.5);
        }
      }
    });

    it("delivers >= 4.5:1 normal-text contrast and >= 3:1 large-scale figure contrast (G145) for filled buttons, badges, and wallet gradient cards", () => {
      const surfaceHex = tokens.get("--semantic-color-surface");
      const filledActionAndWalletTokens = [
        "--semantic-color-primary",
        "--semantic-color-primary-dark",
        "--semantic-color-accent",
        "--semantic-color-interactive-primary",
        "--semantic-color-interactive-primary-hover",
        "--semantic-color-warning-action",
        "--semantic-color-success",
        "--semantic-color-success-dark",
        "--semantic-color-error",
        "--semantic-color-ai",
        "--semantic-color-wallet-start",
        "--semantic-color-wallet-end",
      ];

      for (const bgToken of filledActionAndWalletTokens) {
        const bgHex = tokens.get(bgToken);
        const ratio = contrastRatio(surfaceHex, bgHex);
        expect(
          ratio,
          `White text (${surfaceHex}) on ${bgToken} (${bgHex}) achieved ${ratio.toFixed(2)}:1 (< 4.5:1)`
        ).toBeGreaterThanOrEqual(4.5);
      }

      // Large-scale heading and wallet gradient tokens against light surfaces (G145 >= 3:1)
      for (const largeToken of [
        "--semantic-color-wallet-start",
        "--semantic-color-wallet-end",
        "--semantic-color-primary",
        "--semantic-color-primary-dark",
      ]) {
        const fgHex = tokens.get(largeToken);
        for (const bgHex of lightSurfaces) {
          expect(contrastRatio(fgHex, bgHex)).toBeGreaterThanOrEqual(3);
        }
      }
    });

    it("delivers >= 4.5:1 contrast for semantic status badge foreground/background pairs and inverse chrome tokens", () => {
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

      for (const [fgToken, bgToken] of badgePairs) {
        const fgHex = tokens.get(fgToken);
        const bgHex = tokens.get(bgToken);
        const ratio = contrastRatio(fgHex, bgHex);
        expect(
          ratio,
          `${fgToken} (${fgHex}) on ${bgToken} (${bgHex}) achieved ${ratio.toFixed(2)}:1 (< 4.5:1)`
        ).toBeGreaterThanOrEqual(4.5);
      }
    });

    it("delivers >= 3:1 non-text boundary contrast for --semantic-color-border-control across light surfaces while preserving subtle --semantic-color-border (SC 1.4.11)", () => {
      const controlBorderHex = tokens.get("--semantic-color-border-control");
      const passiveBorderHex = tokens.get("--semantic-color-border");

      expect(controlBorderHex).toBe(tokens.get("--color-border-control"));
      expect(passiveBorderHex).toBe("#e2e8f0");

      for (const bgHex of lightSurfaces) {
        const ratio = contrastRatio(controlBorderHex, bgHex);
        expect(
          ratio,
          `--semantic-color-border-control (${controlBorderHex}) on ${bgHex} achieved ${ratio.toFixed(2)}:1 (< 3:1)`
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

    it("blocks hardcoded font-family stacks, raw px font sizes outside :root, sub-0.75rem text, and sub-1rem form controls via ESLint", async () => {
      const eslint = new ESLint({
        cwd: repoRoot,
        overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
      });

      const [vueViolation] = await eslint.lintText(
        `<template>
  <div>
    <span style="font-family: monospace; font-size: 10px">Code</span>
    <input class="custom-field" type="text" aria-label="Search" />
  </div>
</template>
<style scoped>
.custom-field {
  font-size: var(--font-size-sm);
}
</style>
`,
        {
          filePath: path.join(
            repoRoot,
            "apps/web/src/components/__TypographyProbe.vue"
          ),
        }
      );
      const vueRuleIds = vueViolation.messages.map((m) => m.ruleId);
      expect(vueRuleIds).toContain("custom-vue/no-raw-font-family");
      expect(vueRuleIds).toContain("custom-vue/no-px-font-size");
      expect(vueRuleIds).toContain("custom-vue/no-subfloor-font-size");
      expect(vueRuleIds).toContain("custom-vue/no-sub-1rem-form-control");

      const [cssViolation] = await eslint.lintText(
        `.caption {\n  font-family: "Inter", sans-serif;\n  font-size: 0.65rem;\n}\n.label {\n  font-size: 14px;\n}\nselect.filter {\n  font-size: 0.875rem;\n}\n`,
        {
          filePath: path.join(repoRoot, "apps/subject-portal/src/__probe.css"),
        }
      );
      const cssRuleIds = cssViolation.messages.map((m) => m.ruleId);
      expect(cssRuleIds).toContain("custom-vue/no-raw-font-family");
      expect(cssRuleIds).toContain("custom-vue/no-px-font-size");
      expect(cssRuleIds).toContain("custom-vue/no-subfloor-font-size");
      expect(cssRuleIds).toContain("custom-vue/no-sub-1rem-form-control");
    });
  });
});
