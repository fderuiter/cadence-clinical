import { describe, it, expect } from "vitest";
import fs from "fs";
import path from "path";
import * as rootUi from "../index.js";

const uiDir = path.resolve(__dirname, "..");
const repoRoot = path.resolve(uiDir, "../..");
const webSrcDir = path.join(repoRoot, "apps/web/src");
const portalSrcDir = path.join(repoRoot, "apps/subject-portal/src");

/**
 * Intentionally staged shared UI primitives awaiting production rollout (ADR-2677).
 * Once adopted in apps/web/src or apps/subject-portal/src, entries must graduate out
 * of this allowlist so future regressions are caught automatically.
 */
export const STAGED_UI_PRIMITIVES = new Set([
  "ClinicalExplanation",
  "ClinicalGatedAction",
]);

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

const REGEX_PREFIX_KEYWORDS = new Set([
  "await",
  "case",
  "delete",
  "in",
  "instanceof",
  "new",
  "of",
  "return",
  "throw",
  "typeof",
  "void",
  "yield",
]);

function isRegexPrefix(prevNonWsChar, prevWord) {
  if (!prevNonWsChar) return true;
  if ("([{:;,=!?&|+-*%<>^~".includes(prevNonWsChar)) return true;
  return Boolean(prevWord && REGEX_PREFIX_KEYWORDS.has(prevWord));
}

export function stripJsComments(code) {
  let out = "";
  let i = 0;
  let prevNonWs = "";
  let prevWord = "";
  const len = code.length;

  while (i < len) {
    const ch = code[i];
    const next = code[i + 1];

    if (ch === "/" && next === "/") {
      const nl = code.indexOf("\n", i + 2);
      if (nl === -1) break;
      i = nl;
      continue;
    }

    if (ch === "/" && next === "*") {
      const end = code.indexOf("*/", i + 2);
      if (end === -1) break;
      out += " ";
      i = end + 2;
      continue;
    }

    if (ch === "'" || ch === '"' || ch === "`") {
      const quote = ch;
      out += ch;
      i++;
      while (i < len) {
        const c = code[i];
        out += c;
        i++;
        if (c === "\\" && i < len) {
          out += code[i];
          i++;
          continue;
        }
        if (c === quote) break;
      }
      prevNonWs = quote;
      prevWord = "";
      continue;
    }

    if (ch === "/" && isRegexPrefix(prevNonWs, prevWord)) {
      out += ch;
      i++;
      let inCharClass = false;
      while (i < len) {
        const c = code[i];
        out += c;
        i++;
        if (c === "\\" && i < len) {
          out += code[i];
          i++;
          continue;
        }
        if (c === "[") inCharClass = true;
        else if (c === "]") inCharClass = false;
        else if ((c === "/" && !inCharClass) || c === "\n") break;
      }
      prevNonWs = "/";
      prevWord = "";
      continue;
    }

    if (/[A-Za-z_$]/.test(ch)) {
      let word = ch;
      i++;
      while (i < len && /[A-Za-z0-9_$]/.test(code[i])) {
        word += code[i];
        i++;
      }
      out += word;
      prevNonWs = word[word.length - 1];
      prevWord = word;
      continue;
    }

    out += ch;
    if (!/\s/.test(ch)) {
      prevNonWs = ch;
      prevWord = "";
    }
    i++;
  }

  return out;
}

export function stripSourceComments(source, filePath = "") {
  if (filePath.endsWith(".vue") || source.includes("<template")) {
    const withoutHtmlComments = source.replace(/<!--[\s\S]*?-->/g, " ");
    return withoutHtmlComments
      .replace(
        /(<script\b[^>]*>)([\s\S]*?)(<\/script>)/gi,
        (_, openTag, scriptBody, closeTag) =>
          `${openTag}${stripJsComments(scriptBody)}${closeTag}`
      )
      .replace(
        /(<style\b[^>]*>)([\s\S]*?)(<\/style>)/gi,
        (_, openTag, styleBody, closeTag) =>
          `${openTag}${styleBody.replace(/\/\*[\s\S]*?\*\//g, " ")}${closeTag}`
      );
  }
  return stripJsComments(source);
}

function toKebabCase(pascalName) {
  return pascalName.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}

function hasComponentReference(corpus, componentName) {
  const kebab = toKebabCase(componentName);
  const pascalRe = new RegExp(`\\b${componentName}\\b`);
  const kebabRe = new RegExp(`<${kebab}[\\s>/]`);
  return pascalRe.test(corpus) || kebabRe.test(corpus);
}

export function parseExportedBuilderBodies(buildersSource) {
  const stripped = stripJsComments(buildersSource);
  const builders = [];
  const declRe = /export\s+function\s+([A-Za-z0-9_]+)\s*\(/g;
  let match;
  while ((match = declRe.exec(stripped)) !== null) {
    const name = match[1];
    let parenDepth = 0;
    let paramEnd = match.index;
    for (let i = stripped.indexOf("(", match.index); i < stripped.length; i++) {
      if (stripped[i] === "(") parenDepth++;
      else if (stripped[i] === ")") {
        parenDepth--;
        if (parenDepth === 0) {
          paramEnd = i + 1;
          break;
        }
      }
    }
    const openBrace = stripped.indexOf("{", paramEnd);
    let depth = 0;
    let closeBrace = openBrace;
    for (let i = openBrace; i < stripped.length; i++) {
      if (stripped[i] === "{") depth++;
      else if (stripped[i] === "}") {
        depth--;
        if (depth === 0) {
          closeBrace = i;
          break;
        }
      }
    }
    builders.push({
      name,
      body: stripped.slice(openBrace + 1, closeBrace),
    });
  }
  return builders;
}

export function evaluateUiConsumerReachability({
  componentSources,
  composableModules,
  builderEntries,
  consumerCorpus,
  stagedPrimitives,
}) {
  const strippedComponents = componentSources.map((comp) => ({
    name: comp.name,
    source: stripSourceComments(comp.source, `${comp.name}.vue`),
  }));

  const directlyAdoptedStagedPrimitives = new Set();
  const reachableComponents = new Set();

  for (const comp of strippedComponents) {
    const adoptedInApps = hasComponentReference(consumerCorpus, comp.name);
    if (stagedPrimitives.has(comp.name) && adoptedInApps) {
      directlyAdoptedStagedPrimitives.add(comp.name);
    }
    if (stagedPrimitives.has(comp.name) || adoptedInApps) {
      reachableComponents.add(comp.name);
    }
  }

  let changed = true;
  while (changed) {
    changed = false;
    const reachableVueCorpus = strippedComponents
      .filter((comp) => reachableComponents.has(comp.name))
      .map((comp) => comp.source)
      .join("\n");
    for (const comp of strippedComponents) {
      if (reachableComponents.has(comp.name)) continue;
      if (hasComponentReference(reachableVueCorpus, comp.name)) {
        reachableComponents.add(comp.name);
        changed = true;
      }
    }
  }

  const reachableVueCorpus = strippedComponents
    .filter((comp) => reachableComponents.has(comp.name))
    .map((comp) => comp.source)
    .join("\n");
  const combinedConsumerAndLiveUiCorpus = `${consumerCorpus}\n${reachableVueCorpus}`;

  const unreachableComposables = [];
  for (const mod of composableModules) {
    const stagedMatch = [mod.stem, ...mod.exportedSymbols].filter((sym) =>
      stagedPrimitives.has(sym)
    );
    const hasCaller = mod.exportedSymbols.some((sym) =>
      new RegExp(`\\b${sym}\\b`).test(combinedConsumerAndLiveUiCorpus)
    );
    if (stagedMatch.length > 0 && hasCaller) {
      for (const sym of stagedMatch) {
        directlyAdoptedStagedPrimitives.add(sym);
      }
    }
    if (stagedMatch.length === 0 && !hasCaller) {
      unreachableComposables.push(mod.stem);
    }
  }

  const reachableBuilders = new Set();
  for (const builder of builderEntries) {
    const hasDirectCaller = new RegExp(`\\b${builder.name}\\b`).test(
      combinedConsumerAndLiveUiCorpus
    );
    if (stagedPrimitives.has(builder.name) && hasDirectCaller) {
      directlyAdoptedStagedPrimitives.add(builder.name);
    }
    if (stagedPrimitives.has(builder.name) || hasDirectCaller) {
      reachableBuilders.add(builder.name);
    }
  }

  changed = true;
  while (changed) {
    changed = false;
    const reachableBuilderBodies = builderEntries
      .filter((b) => reachableBuilders.has(b.name))
      .map((b) => b.body)
      .join("\n");
    for (const builder of builderEntries) {
      if (reachableBuilders.has(builder.name)) continue;
      if (
        new RegExp(`\\b${builder.name}\\s*\\(`).test(reachableBuilderBodies)
      ) {
        reachableBuilders.add(builder.name);
        changed = true;
      }
    }
  }

  return {
    unreachableComponents: strippedComponents
      .map((c) => c.name)
      .filter((name) => !reachableComponents.has(name))
      .sort(),
    unreachableComposables: [...unreachableComposables].sort(),
    unreachableBuilders: builderEntries
      .map((b) => b.name)
      .filter((name) => !reachableBuilders.has(name))
      .sort(),
    staleStagedPrimitives: [...directlyAdoptedStagedPrimitives].sort(),
  };
}

describe("Shared UI Consumer Reachability Guard (ADR-2677)", () => {
  const isProductionAppFile = (filePath) =>
    !/\.(?:test|spec)\.[jt]sx?$/.test(filePath);

  const appConsumerFiles = [
    ...walkFiles(
      webSrcDir,
      [".vue", ".ts", ".js"],
      new Set(["__tests__", "tests"])
    ).filter(isProductionAppFile),
    ...walkFiles(
      portalSrcDir,
      [".vue", ".ts", ".js"],
      new Set(["__tests__", "tests"])
    ).filter(isProductionAppFile),
  ];

  const strippedConsumerCorpus = appConsumerFiles
    .map((filePath) =>
      stripSourceComments(fs.readFileSync(filePath, "utf-8"), filePath)
    )
    .join("\n");

  const componentSources = walkFiles(path.join(uiDir, "src/components"), [
    ".vue",
  ])
    .map((filePath) => ({
      name: path.basename(filePath, ".vue"),
      source: fs.readFileSync(filePath, "utf-8"),
    }))
    .sort((a, b) => a.name.localeCompare(b.name));

  const composableModules = walkFiles(path.join(uiDir, "src/composables"), [
    ".js",
    ".ts",
  ])
    .filter(
      (filePath) => !filePath.endsWith(".d.ts") && isProductionAppFile(filePath)
    )
    .map((filePath) => {
      const stem = path.basename(filePath, path.extname(filePath));
      const source = stripJsComments(fs.readFileSync(filePath, "utf-8"));
      const exportedSymbols = new Set([stem]);
      const exportRe =
        /export\s+(?:async\s+)?(?:function|const)\s+([A-Za-z0-9_]+)\b/g;
      let match;
      while ((match = exportRe.exec(source)) !== null) {
        if (match[1] in rootUi) {
          exportedSymbols.add(match[1]);
        }
      }
      return {
        stem,
        exportedSymbols: [...exportedSymbols],
      };
    })
    .sort((a, b) => a.stem.localeCompare(b.stem));

  const buildersSource = fs.readFileSync(
    path.join(uiDir, "src/builders.js"),
    "utf-8"
  );
  const builderEntries = parseExportedBuilderBodies(buildersSource);

  it("rejects any non-staged packages/ui component, composable, or HTML builder with zero callers in apps/web/src or apps/subject-portal/src", () => {
    expect([...STAGED_UI_PRIMITIVES].sort()).toEqual([
      "ClinicalExplanation",
      "ClinicalGatedAction",
    ]);
    for (const stagedName of STAGED_UI_PRIMITIVES) {
      expect(
        rootUi[stagedName],
        `STAGED_UI_PRIMITIVES entry ${stagedName} must exist in packages/ui`
      ).toBeDefined();
    }

    expect(appConsumerFiles.length).toBeGreaterThan(50);
    expect(builderEntries.length).toBeGreaterThanOrEqual(5);

    const liveReport = evaluateUiConsumerReachability({
      componentSources,
      composableModules,
      builderEntries,
      consumerCorpus: strippedConsumerCorpus,
      stagedPrimitives: STAGED_UI_PRIMITIVES,
    });

    expect(
      liveReport.unreachableComponents,
      "Unreferenced packages/ui components outside STAGED_UI_PRIMITIVES"
    ).toEqual([]);
    expect(
      liveReport.unreachableComposables,
      "Unreferenced packages/ui composables outside STAGED_UI_PRIMITIVES"
    ).toEqual([]);
    expect(
      liveReport.unreachableBuilders,
      "Unreferenced packages/ui HTML builders outside STAGED_UI_PRIMITIVES"
    ).toEqual([]);
    expect(
      liveReport.staleStagedPrimitives,
      "Adopted primitives must be removed from STAGED_UI_PRIMITIVES"
    ).toEqual([]);
    expect(STAGED_UI_PRIMITIVES.has("ClinicalSplitWorkspace")).toBe(false);
    expect(STAGED_UI_PRIMITIVES.has("useResponsiveSplitWorkspace")).toBe(false);
    expect(rootUi.ClinicalSplitWorkspace).toBeDefined();
    expect(rootUi.useResponsiveSplitWorkspace).toBeTypeOf("function");
  });

  it("keeps retired ADR-2677 UI components, composables, builders, and signing helpers absent from root exports", () => {
    const retiredExports = [
      "AiActionButton",
      "AiSuggestionDrawer",
      "AuditLogViewer",
      "ConfidenceBadge",
      "FileUploadModal",
      "PersonaSwitcher",
      "useAsyncRetry",
      "createClinicalLookupInput",
      "createSoaBuilderMatrix",
      "createClinicalVisitMatrix",
      "verifyCanonicalSignature",
      "generateJwtHS256",
    ];
    for (const symbol of retiredExports) {
      expect(rootUi[symbol], `${symbol} should remain retired`).toBeUndefined();
    }
  });

  it("flags orphaned components, composables, chained dead builders, comment-only mentions, and stale staged entries", () => {
    const commentHeavyConsumer = stripSourceComments(
      String.raw`<template>
        <!-- <CommentOnlyWidget /> <comment-only-widget /> -->
        <p data-note="It's active at https://cadence.example/path">Live view</p>
      </template>
      <script setup lang="ts">
      // Migrated off CommentOnlyWidget and useCommentOnlyComposable()
      /* Previously called createCommentOnlyBuilder() */
      const DocsUrl = "https://cadence.example//not-a-comment";
      const quoteRegex = /'/g;
      function matchesHttps(url: string) {
        return /https?:\/\/.*KeywordRegexWidget/.test(url); // trailing comment with CommentOnlyWidget
      }
      void DocsUrl;
      void quoteRegex;
      void matchesHttps;
      </script>`,
      "SyntheticView.vue"
    );

    const syntheticReport = evaluateUiConsumerReachability({
      componentSources: [
        ...componentSources,
        {
          name: "SyntheticUnreferencedWidget",
          source: "<template><div>Unused</div></template>",
        },
        {
          name: "CommentOnlyWidget",
          source: "<template><div>Only in comments</div></template>",
        },
        {
          name: "GraduatedStagedWidget",
          source: "<template><div>Now used in production</div></template>",
        },
        {
          name: "KeywordRegexWidget",
          source:
            "<template><div>Referenced after // inside return regex</div></template>",
        },
      ],
      composableModules: [
        ...composableModules,
        {
          stem: "useCommentOnlyComposable",
          exportedSymbols: ["useCommentOnlyComposable"],
        },
      ],
      builderEntries: [
        ...builderEntries,
        {
          name: "createDeadAlphaBuilder",
          body: "return createDeadBetaBuilder();",
        },
        {
          name: "createDeadBetaBuilder",
          body: "return createDeadAlphaBuilder();",
        },
        {
          name: "createCommentOnlyBuilder",
          body: 'return "<div></div>";',
        },
      ],
      consumerCorpus: `${strippedConsumerCorpus}\n${commentHeavyConsumer}\n<graduated-staged-widget />`,
      stagedPrimitives: new Set([
        ...STAGED_UI_PRIMITIVES,
        "GraduatedStagedWidget",
      ]),
    });

    expect(syntheticReport.unreachableComponents).toEqual([
      "CommentOnlyWidget",
      "SyntheticUnreferencedWidget",
    ]);
    expect(syntheticReport.unreachableComposables).toEqual([
      "useCommentOnlyComposable",
    ]);
    expect(syntheticReport.unreachableBuilders).toEqual([
      "createCommentOnlyBuilder",
      "createDeadAlphaBuilder",
      "createDeadBetaBuilder",
    ]);
    expect(syntheticReport.staleStagedPrimitives).toEqual([
      "GraduatedStagedWidget",
    ]);
  });
});
