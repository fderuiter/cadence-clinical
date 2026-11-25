import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";

const uiDir = path.resolve(__dirname, "..");
const repoRoot = path.resolve(uiDir, "../..");
const webSrcDir = path.join(repoRoot, "apps/web/src");

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

function parseRuleProps(cssText, selectorRegex) {
  const clean = cssText.replace(/\/\*[\s\S]*?\*\//g, "");
  const match = clean.match(selectorRegex);
  if (!match) return null;
  const startBrace = clean.indexOf("{", match.index);
  let depth = 0;
  let endBrace = startBrace;
  for (let i = startBrace; i < clean.length; i++) {
    if (clean[i] === "{") depth++;
    else if (clean[i] === "}") {
      depth--;
      if (depth === 0) {
        endBrace = i;
        break;
      }
    }
  }
  const body = clean.slice(startBrace + 1, endBrace);
  const props = {};
  for (const decl of body.matchAll(/([a-zA-Z-]+)\s*:\s*([^;]+);/g)) {
    props[decl[1].toLowerCase()] = decl[2].trim().replace(/\s+/g, " ");
  }
  return props;
}

describe("Clinical WOFF2 Font Bundles, Rem Typography Scale, Zero-Emoji Personas & Touch Lint Gates (ADR-2834)", () => {
  it("bundles self-hosted subsetted variable Inter and JetBrains Mono WOFF2 fonts (65 KB to 95 KB combined) with OFL 1.1 notices and relative @font-face declarations", () => {
    const fontsDir = path.join(uiDir, "fonts");
    const interWoff2 = path.join(fontsDir, "InterVariable-latin.woff2");
    const jbWoff2 = path.join(fontsDir, "JetBrainsMonoVariable-latin.woff2");
    const interOfl = path.join(fontsDir, "OFL-Inter.txt");
    const jbOfl = path.join(fontsDir, "OFL-JetBrainsMono.txt");

    expect(fs.existsSync(interWoff2)).toBe(true);
    expect(fs.existsSync(jbWoff2)).toBe(true);
    expect(fs.existsSync(interOfl)).toBe(true);
    expect(fs.existsSync(jbOfl)).toBe(true);

    const interBytes = fs.statSync(interWoff2).size;
    const jbBytes = fs.statSync(jbWoff2).size;
    const combinedBytes = interBytes + jbBytes;

    expect(fs.readFileSync(interWoff2).subarray(0, 4).toString("ascii")).toBe(
      "wOF2"
    );
    expect(fs.readFileSync(jbWoff2).subarray(0, 4).toString("ascii")).toBe(
      "wOF2"
    );
    expect(combinedBytes).toBeGreaterThanOrEqual(65 * 1024);
    expect(combinedBytes).toBeLessThanOrEqual(95 * 1000);

    expect(fs.readFileSync(interOfl, "utf-8")).toContain(
      "SIL OPEN FONT LICENSE Version 1.1"
    );
    expect(fs.readFileSync(jbOfl, "utf-8")).toContain(
      "SIL OPEN FONT LICENSE Version 1.1"
    );

    const pkgJson = JSON.parse(
      fs.readFileSync(path.join(uiDir, "package.json"), "utf-8")
    );
    expect(pkgJson.exports["./fonts/*"]).toBe("./fonts/*");

    const tokensCss = fs.readFileSync(path.join(uiDir, "tokens.css"), "utf-8");
    expect(tokensCss).toContain(
      'src: url("./fonts/InterVariable-latin.woff2") format("woff2")'
    );
    expect(tokensCss).toContain(
      'src: url("./fonts/JetBrainsMonoVariable-latin.woff2") format("woff2")'
    );
    expect(tokensCss).toContain("font-display: swap;");

    const rootVars = parseCssBlockVars(tokensCss, /:root\s*\{/);
    for (const [tok, expectedRem] of [
      ["--font-size-2xs", "0.75rem"],
      ["--font-size-xs", "0.8125rem"],
      ["--font-size-sm", "0.875rem"],
      ["--font-size-base", "0.9375rem"],
      ["--font-size-md", "1rem"],
      ["--font-size-lg", "1.125rem"],
      ["--font-size-xl", "1.375rem"],
      ["--font-size-2xl", "1.5rem"],
      ["--font-size-3xl", "1.75rem"],
      ["--font-size-4xl", "2rem"],
      ["--font-size-5xl", "2.5rem"],
      ["--semantic-font-size-4xl", "var(--font-size-4xl)"],
      ["--semantic-font-size-5xl", "var(--font-size-5xl)"],
    ]) {
      expect(rootVars[tok]).toBe(expectedRem);
    }

    const webStyleCss = fs.readFileSync(
      path.join(webSrcDir, "style.css"),
      "utf-8"
    );
    expect(webStyleCss).toMatch(
      /@media\s*\(\s*pointer:\s*coarse\s*\)[\s\S]*?:where\(input,\s*select,\s*textarea\):not\(\[type="checkbox"\]\):not\(\[type="radio"\]\)/
    );
  });

  it("enforces clinical monospace on NodeRefChip, EcrfSignaturesSection, CanvasFormWorkspace, and AuditView and zero-emoji persona formatting", () => {
    const nodeRefChip = fs.readFileSync(
      path.join(webSrcDir, "components/canvas/NodeRefChip.vue"),
      "utf-8"
    );
    const nodeRefChipRule = parseRuleProps(nodeRefChip, /\.noderef-chip\s*\{/);
    expect(nodeRefChipRule).not.toBeNull();
    expect(nodeRefChipRule["font-family"]).toBe("var(--semantic-font-mono)");
    expect(nodeRefChipRule["border"]).toBe(
      "1px solid var(--semantic-color-border-control)"
    );
    expect(nodeRefChipRule["background-color"]).toBe(
      "var(--semantic-color-surface)"
    );

    const sigSection = fs.readFileSync(
      path.join(webSrcDir, "components/ecrf/EcrfSignaturesSection.vue"),
      "utf-8"
    );
    expect(sigSection).toContain(".ecrf-signature-actor");
    expect(sigSection).toContain(".ecrf-signature-timestamp");
    expect(sigSection).toContain("var(--semantic-font-mono)");

    const canvasWorkspace = fs.readFileSync(
      path.join(webSrcDir, "components/canvas/CanvasFormWorkspace.vue"),
      "utf-8"
    );
    expect(canvasWorkspace).toContain("workspace-meta-code");
    expect(canvasWorkspace).toContain("var(--semantic-font-mono)");

    const auditView = fs.readFileSync(
      path.join(webSrcDir, "views/AuditView.vue"),
      "utf-8"
    );
    expect(auditView).toMatch(
      /class="hash-code"[\s\S]*?font-family:\s*var\(--semantic-font-mono\)/
    );

    const authStoreSrc = fs.readFileSync(
      path.join(webSrcDir, "stores/auth.js"),
      "utf-8"
    );
    const personaBlockMatch = authStoreSrc.match(
      /export const PERSONA_PRESETS\s*=\s*\[[\s\S]*?\];/
    );
    expect(personaBlockMatch).not.toBeNull();
    expect(personaBlockMatch[0]).not.toMatch(/\p{Extended_Pictographic}/u);
    expect(authStoreSrc).toContain("export function formatPersonaLabel(");
  });

  it("enforces custom-vue/no-untokenized-font-size and custom-vue/no-sub1rem-touch-control across Vue, CSS, and JS/TS files", async () => {
    const { ESLint } = await import("eslint");
    const eslint = new ESLint({
      cwd: repoRoot,
      overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
    });

    const badControlVue = await eslint.lintText(
      `<template>
  <div>
    <label for="bad-sel">Filter</label>
    <select id="bad-sel" class="compact-select" style="font-size: var(--font-size-xs)">
      <option value="all">All</option>
    </select>
    <p style="font-size: 14px">Untokenized prose</p>
  </div>
</template>
<style scoped>
.compact-select {
  font-size: var(--font-size-sm);
}
</style>
`,
      {
        filePath: path.join(webSrcDir, "views/__SyntheticTouchControlBad.vue"),
      }
    );
    const badMsgs = badControlVue[0].messages;
    expect(
      badMsgs.filter((m) => m.ruleId === "custom-vue/no-untokenized-font-size")
    ).toHaveLength(1);
    expect(
      badMsgs.filter((m) => m.ruleId === "custom-vue/no-sub1rem-touch-control")
    ).toHaveLength(2);

    const badTokensCss = await eslint.lintText(
      `:root {
  --font-size-sm: 14px;
}
`,
      {
        filePath: path.join(uiDir, "tokens.css"),
      }
    );
    expect(
      badTokensCss[0].messages.some(
        (m) => m.ruleId === "custom-vue/no-untokenized-font-size"
      )
    ).toBe(true);

    const goodControlVue = await eslint.lintText(
      `<template>
  <div>
    <label for="good-sel">Filter</label>
    <select id="good-sel" class="touch-safe-select">
      <option value="all">All</option>
    </select>
  </div>
</template>
<style scoped>
.touch-safe-select {
  font-size: var(--font-size-md);
}
@media (pointer: fine) {
  .touch-safe-select {
    font-size: var(--font-size-sm);
  }
}
</style>
`,
      {
        filePath: path.join(webSrcDir, "views/__SyntheticTouchControlGood.vue"),
      }
    );
    const goodErrors = goodControlVue[0].messages.filter((m) =>
      [
        "custom-vue/no-untokenized-font-size",
        "custom-vue/no-sub1rem-touch-control",
        "custom-vue/no-sub12px-font-size",
        "custom-vue/no-custom-font-stack",
      ].includes(m.ruleId)
    );
    expect(goodErrors).toEqual([]);

    // Edge cases: checkbox/radio/hidden/file inputs are exempt from the 1rem touch floor
    const nonTextInputsVue = await eslint.lintText(
      `<template>
  <div>
    <input id="chk-1" type="checkbox" class="chk-control" style="font-size: var(--font-size-xs)" />
    <input id="rad-1" type="radio" class="rad-control" style="font-size: var(--font-size-xs)" />
  </div>
</template>
<style scoped>
input[type="checkbox"].chk-control {
  font-size: var(--font-size-xs);
}
</style>
`,
      {
        filePath: path.join(webSrcDir, "views/__SyntheticCheckboxExempt.vue"),
      }
    );
    expect(
      nonTextInputsVue[0].messages.filter(
        (m) => m.ruleId === "custom-vue/no-sub1rem-touch-control"
      )
    ).toEqual([]);

    // Edge cases: standalone CSS touch-control selectors and JS inline HTML templates are caught,
    // and unauthorized @font-face font families in tokens.css are rejected
    const badCssSheet = await eslint.lintText(
      `.form-select {
  font-size: var(--font-size-sm);
}
`,
      {
        filePath: path.join(webSrcDir, "components/shared/__SyntheticBad.css"),
      }
    );
    expect(
      badCssSheet[0].messages.some(
        (m) => m.ruleId === "custom-vue/no-sub1rem-touch-control"
      )
    ).toBe(true);

    const badJsTemplate = await eslint.lintText(
      `export const tpl = '<input type="text" style="font-size: var(--font-size-xs)" />';\n`,
      {
        filePath: path.join(webSrcDir, "utils/__SyntheticBadControl.js"),
      }
    );
    expect(
      badJsTemplate[0].messages.some(
        (m) => m.ruleId === "custom-vue/no-sub1rem-touch-control"
      )
    ).toBe(true);

    const unauthorizedFontFaceCss = await eslint.lintText(
      `@font-face {
  font-family: "Comic Sans MS";
  src: url("./fonts/comic.woff2") format("woff2");
}
`,
      {
        filePath: path.join(uiDir, "tokens.css"),
      }
    );
    expect(
      unauthorizedFontFaceCss[0].messages.some(
        (m) => m.ruleId === "custom-vue/no-custom-font-stack"
      )
    ).toBe(true);
  });
});
