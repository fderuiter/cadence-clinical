// ESLint configuration for Vue single-file components
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import js from "@eslint/js";
import vuePlugin from "eslint-plugin-vue";
import vuejsAccessibility from "eslint-plugin-vuejs-accessibility";
import vueParser from "vue-eslint-parser";
import tseslint from "typescript-eslint";

const repoRoot = path.dirname(fileURLToPath(import.meta.url));
const tokensCssPath = path.join(repoRoot, "packages/ui/tokens.css");
const webStyleCssPath = path.join(repoRoot, "apps/web/src/style.css");
const portalStyleCssPath = path.join(
  repoRoot,
  "apps/subject-portal/src/style.css"
);

const hexColorRegex = /#(?:[0-9a-fA-F]{3,4}){1,2}\b/g;
const cssVarDefRegex = /(--[a-zA-Z0-9_-]+)\s*:/g;
const cssVarUseRegex = /var\(\s*(--[a-zA-Z0-9_-]+)/g;

function extractDefinedVarsFromFile(filePath) {
  const vars = new Set();
  if (!fs.existsSync(filePath)) return vars;
  const raw = fs
    .readFileSync(filePath, "utf8")
    .replace(/\/\*[\s\S]*?\*\//g, "");
  for (const m of raw.matchAll(cssVarDefRegex)) {
    vars.add(m[1]);
  }
  return vars;
}

function getBaseDefinedVars(filename) {
  const normalized = filename.split(path.sep).join("/");
  const vars = extractDefinedVarsFromFile(tokensCssPath);
  if (normalized.includes("/apps/web/")) {
    for (const v of extractDefinedVarsFromFile(webStyleCssPath)) vars.add(v);
  } else if (normalized.includes("/apps/subject-portal/")) {
    for (const v of extractDefinedVarsFromFile(portalStyleCssPath)) vars.add(v);
  }
  return vars;
}

function stripCssCommentsPreservingNewlines(text) {
  return text.replace(/\/\*[\s\S]*?\*\//g, (comment) =>
    comment.replace(/[^\r\n]/g, " ")
  );
}

function stripUrlFragmentsPreservingLength(text) {
  return text.replace(/url\(\s*['"]?#[^)]*\)/gi, (u) => " ".repeat(u.length));
}

function offsetToLoc(fullText, offset) {
  const upTo = fullText.slice(0, offset);
  const lines = upTo.split(/\r?\n/);
  return {
    line: lines.length,
    column: lines[lines.length - 1].length,
  };
}

function isStyleOrColorVAttribute(node) {
  if (!node.key) return false;
  const targetNames = new Set(["style", "fill", "stroke", "color"]);
  const isStatic =
    !node.directive &&
    node.key.type === "VIdentifier" &&
    targetNames.has(node.key.name);
  const isDynamic =
    node.directive &&
    node.key.type === "VDirectiveKey" &&
    node.key.name?.name === "bind" &&
    targetNames.has(node.key.argument?.name);
  return isStatic || isDynamic;
}

const cssStubParser = {
  meta: { name: "css-stub-parser", version: "1.0.0" },
  parseForESLint(code) {
    const lines = code.split(/\r?\n/);
    const lastLine = Math.max(1, lines.length);
    const lastCol = lines[lastLine - 1]?.length ?? 0;
    return {
      ast: {
        type: "Program",
        body: [],
        sourceType: "module",
        range: [0, code.length],
        loc: {
          start: { line: 1, column: 0 },
          end: { line: lastLine, column: lastCol },
        },
        tokens: [],
        comments: [],
      },
    };
  },
};

const noInlineHexRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow hardcoded hex color codes in Vue templates, Vue <style> blocks, and CSS stylesheets outside packages/ui/tokens.css :root.",
      category: "Best Practices",
    },
    messages: {
      noHexInStyle:
        'Hardcoded hex color "{{hex}}" is prohibited. Use design system tokens defined in packages/ui/tokens.css (e.g. var(--semantic-color-...)).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    const sourceText = context.sourceCode.getText();

    // 1. Plain .css files
    if (filename.endsWith(".css")) {
      return {
        Program() {
          let masked = stripUrlFragmentsPreservingLength(
            stripCssCommentsPreservingNewlines(sourceText)
          );
          if (filename.endsWith("/packages/ui/tokens.css")) {
            masked = masked.replace(/:root\s*\{[\s\S]*?\}/, (rootBlock) =>
              rootBlock.replace(/[^\r\n]/g, " ")
            );
          }
          for (const match of masked.matchAll(hexColorRegex)) {
            const loc = offsetToLoc(sourceText, match.index);
            context.report({
              loc,
              messageId: "noHexInStyle",
              data: { hex: match[0] },
            });
          }
        },
      };
    }

    // 2. Vue SFCs: check both <template> style/presentation attributes and <style> blocks
    const styleBlockCheck = {
      "Program:exit"() {
        for (const styleMatch of sourceText.matchAll(
          /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
        )) {
          const contentStart = styleMatch.index + styleMatch[1].length;
          const masked = stripUrlFragmentsPreservingLength(
            stripCssCommentsPreservingNewlines(styleMatch[2])
          );
          for (const match of masked.matchAll(hexColorRegex)) {
            const loc = offsetToLoc(sourceText, contentStart + match.index);
            context.report({
              loc,
              messageId: "noHexInStyle",
              data: { hex: match[0] },
            });
          }
        }
      },
    };

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return filename.endsWith(".vue") ? styleBlockCheck : {};
    }

    return parserServices.defineTemplateBodyVisitor(
      {
        VAttribute(node) {
          if (isStyleOrColorVAttribute(node) && node.value) {
            const text = stripUrlFragmentsPreservingLength(
              context.sourceCode.getText(node.value)
            );
            const matches = text.match(hexColorRegex);
            if (matches) {
              for (const hex of new Set(matches)) {
                context.report({
                  node: node.value,
                  messageId: "noHexInStyle",
                  data: { hex },
                });
              }
            }
          }
        },
      },
      styleBlockCheck
    );
  },
};

const noUndefinedCssVarsRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow references to undefined CSS custom properties in var(--...) across Vue templates, Vue <style> blocks, CSS stylesheets, and JS/TS modules.",
      category: "Best Practices",
    },
    messages: {
      undefinedCssVar:
        'CSS custom property "{{varName}}" is not defined in packages/ui/tokens.css or local stylesheet scope.',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    const sourceText = context.sourceCode.getText();
    const allowedVars = getBaseDefinedVars(filename);

    // Collect locally declared custom properties in the current file
    if (filename.endsWith(".css")) {
      const clean = stripCssCommentsPreservingNewlines(sourceText);
      for (const m of clean.matchAll(cssVarDefRegex)) {
        allowedVars.add(m[1]);
      }
      return {
        Program() {
          for (const match of clean.matchAll(cssVarUseRegex)) {
            if (!allowedVars.has(match[1])) {
              context.report({
                loc: offsetToLoc(sourceText, match.index),
                messageId: "undefinedCssVar",
                data: { varName: match[1] },
              });
            }
          }
        },
      };
    }

    if (filename.endsWith(".vue")) {
      for (const sm of sourceText.matchAll(
        /<style\b[^>]*>([\s\S]*?)<\/style>/gi
      )) {
        const clean = stripCssCommentsPreservingNewlines(sm[1]);
        for (const m of clean.matchAll(cssVarDefRegex)) {
          allowedVars.add(m[1]);
        }
      }

      const scriptAndStyleCheck = {
        "Program:exit"() {
          for (const sm of sourceText.matchAll(
            /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
          )) {
            const contentStart = sm.index + sm[1].length;
            const clean = stripCssCommentsPreservingNewlines(sm[2]);
            for (const match of clean.matchAll(cssVarUseRegex)) {
              if (!allowedVars.has(match[1])) {
                context.report({
                  loc: offsetToLoc(sourceText, contentStart + match.index),
                  messageId: "undefinedCssVar",
                  data: { varName: match[1] },
                });
              }
            }
          }
          for (const scm of sourceText.matchAll(
            /(<script\b[^>]*>)([\s\S]*?)<\/script>/gi
          )) {
            const contentStart = scm.index + scm[1].length;
            const clean = stripCssCommentsPreservingNewlines(scm[2]).replace(
              /\/\/.*$/gm,
              (c) => " ".repeat(c.length)
            );
            for (const match of clean.matchAll(cssVarUseRegex)) {
              if (!allowedVars.has(match[1])) {
                context.report({
                  loc: offsetToLoc(sourceText, contentStart + match.index),
                  messageId: "undefinedCssVar",
                  data: { varName: match[1] },
                });
              }
            }
          }
        },
      };

      const parserServices =
        context.sourceCode?.parserServices || context.parserServices;
      if (!parserServices?.defineTemplateBodyVisitor) {
        return scriptAndStyleCheck;
      }

      return parserServices.defineTemplateBodyVisitor(
        {
          VAttribute(node) {
            if (isStyleOrColorVAttribute(node) && node.value) {
              const text = context.sourceCode.getText(node.value);
              for (const m of text.matchAll(cssVarDefRegex)) {
                allowedVars.add(m[1]);
              }
              for (const match of text.matchAll(cssVarUseRegex)) {
                if (!allowedVars.has(match[1])) {
                  context.report({
                    node: node.value,
                    messageId: "undefinedCssVar",
                    data: { varName: match[1] },
                  });
                }
              }
            }
          },
        },
        scriptAndStyleCheck
      );
    }

    // JS / TS files
    return {
      Program() {
        const clean = stripCssCommentsPreservingNewlines(sourceText).replace(
          /\/\/.*$/gm,
          (c) => " ".repeat(c.length)
        );
        for (const m of clean.matchAll(cssVarDefRegex)) {
          allowedVars.add(m[1]);
        }
        for (const match of clean.matchAll(cssVarUseRegex)) {
          if (!allowedVars.has(match[1])) {
            context.report({
              loc: offsetToLoc(sourceText, match.index),
              messageId: "undefinedCssVar",
              data: { varName: match[1] },
            });
          }
        }
      },
    };
  },
};

const customVuePlugin = {
  rules: {
    "no-inline-hex": noInlineHexRule,
    "no-undefined-css-vars": noUndefinedCssVarsRule,
  },
};

const disabledCoreRulesForCss = Object.fromEntries(
  Object.keys(js.configs.recommended.rules).map((ruleName) => [ruleName, "off"])
);

export default [
  {
    ignores: [
      "**/dist/**",
      "**/node_modules/**",
      "**/.venv/**",
      "**/venv/**",
      "**/.pytest_cache/**",
      "**/playwright-report/**",
      "**/test-results/**",
      "**/coverage/**",
      "scripts/**",
      "docs/.vitepress/**",
      "**/mockServiceWorker.js",
    ],
  },
  js.configs.recommended,
  ...vuePlugin.configs["flat/recommended"],
  ...vuejsAccessibility.configs["flat/recommended"],
  {
    files: ["**/*.vue"],
    languageOptions: {
      parser: vueParser,
      parserOptions: {
        parser: tseslint.parser,
        sourceType: "module",
        ecmaVersion: 2022,
      },
    },
    plugins: {
      "custom-vue": customVuePlugin,
    },
    rules: {
      "custom-vue/no-inline-hex": "error",
      "custom-vue/no-undefined-css-vars": "error",
    },
  },
  {
    files: ["packages/**/*.css", "apps/**/*.css"],
    languageOptions: {
      parser: cssStubParser,
    },
    plugins: {
      "custom-vue": customVuePlugin,
    },
    rules: {
      ...disabledCoreRulesForCss,
      "custom-vue/no-inline-hex": "error",
      "custom-vue/no-undefined-css-vars": "error",
    },
  },
  {
    files: [
      "packages/ui/index.js",
      "packages/ui/src/**/*.js",
      "apps/subject-portal/src/**/*.js",
      "apps/web/src/**/*.js",
      "apps/web/src/**/*.ts",
    ],
    plugins: {
      "custom-vue": customVuePlugin,
    },
    rules: {
      "custom-vue/no-undefined-css-vars": "error",
    },
  },
  {
    files: ["**/*.ts", "**/*.tsx"],
    languageOptions: {
      parser: tseslint.parser,
      parserOptions: {
        sourceType: "module",
        ecmaVersion: 2022,
      },
    },
    plugins: {
      "@typescript-eslint": tseslint.plugin,
    },
    rules: {
      "no-undef": "off",
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
    },
  },
  {
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
    },
    rules: {
      "no-alert": "error",
      "no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
      "no-undef": "off",
      "vue/multi-word-component-names": "off",
      "vuejs-accessibility/label-has-for": [
        "error",
        {
          required: {
            some: ["nesting", "id"],
          },
        },
      ],
      "vuejs-accessibility/click-events-have-key-events": "error",
      "vuejs-accessibility/no-static-element-interactions": "error",
      "vuejs-accessibility/form-control-has-label": "error",
    },
    files: [
      "apps/**/*.js",
      "packages/**/*.js",
      "apps/**/*.vue",
      "packages/**/*.vue",
    ],
  },
  {
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
    },
    rules: {
      "vuejs-accessibility/label-has-for": [
        "error",
        {
          required: {
            some: ["nesting", "id"],
          },
        },
      ],
      "vuejs-accessibility/click-events-have-key-events": "error",
      "vuejs-accessibility/no-static-element-interactions": "error",
      "vuejs-accessibility/form-control-has-label": "error",
      "vue/html-self-closing": [
        "error",
        {
          html: {
            void: "always",
            normal: "never",
            component: "always",
          },
          svg: "always",
          math: "always",
        },
      ],
    },
    files: ["packages/ui/**/*.js", "packages/ui/**/*.vue"],
  },
  {
    files: ["**/*.vue"],
    plugins: {
      "@typescript-eslint": tseslint.plugin,
    },
    rules: {
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
    },
  },
  {
    files: [
      "apps/subject-portal/src/**/*.js",
      "apps/web/src/components/**/*.vue",
      "apps/web/src/composables/**/*.js",
      "apps/web/src/views/RulesView.vue",
      "apps/web/src/views/MdrView.vue",
      "apps/web/src/views/EcrfView.vue",
      "apps/web/src/views/QualityCockpitView.vue",
    ],
    rules: {
      "no-alert": "warn",
    },
  },
  {
    files: ["**/tests/**", "tests/**", "**/*.test.*", "**/*.spec.*"],
    rules: {
      "vue/one-component-per-file": "off",
      "vue/require-prop-types": "off",
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": "off",
    },
  },
];
