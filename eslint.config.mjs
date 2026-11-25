// ESLint configuration for Vue single-file components
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import js from "@eslint/js";
import globals from "globals";
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

const hexColorRegex = /#(?:[0-9a-fA-F]{3,4}){1,2}(?![0-9a-zA-Z_-])/g;
const colorFunctionRegex = /\b(?:rgba?|hsla?)\s*\([^)]*\)/gi;
const namedColorDeclRegex =
  /(?:^|[;{\s"'`])(?:background(?:-color)?|color|border(?:-(?:top|right|bottom|left))?(?:-color)?|fill|stroke|outline(?:-color)?)\s*:\s*(white|black)\b/gi;
const cssVarDefRegex = /(--[a-zA-Z0-9_-]+)\s*:/g;
const cssVarUseRegex = /var\(\s*(--[a-zA-Z0-9_-]+)/g;
const aiTokenRegex = /--(?:semantic-)?color-ai(?:-[a-zA-Z0-9_-]+)?\b/g;

function isAllowedAiTokenFile(filename) {
  if (filename.endsWith("/packages/ui/tokens.css")) return true;
  if (filename.includes("/apps/web/src/components/ai/")) return true;
  if (/\/packages\/ui\/src\/components\/clinical\/Ai[^/]*\.vue$/.test(filename))
    return true;
  return false;
}

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

function findColorViolationsInCssText(text) {
  const violations = [];
  for (const match of text.matchAll(hexColorRegex)) {
    violations.push({ index: match.index, literal: match[0] });
  }
  for (const match of text.matchAll(colorFunctionRegex)) {
    violations.push({ index: match.index, literal: match[0] });
  }
  for (const match of text.matchAll(namedColorDeclRegex)) {
    const colorWord = match[1];
    const wordOffset = match[0]
      .toLowerCase()
      .lastIndexOf(colorWord.toLowerCase());
    violations.push({
      index: match.index + Math.max(0, wordOffset),
      literal: colorWord,
    });
  }
  return violations;
}

function collectColorLiteralsFromScriptString(rawText) {
  const cleaned = stripUrlFragmentsPreservingLength(rawText);
  const found = new Set();
  for (const m of cleaned.matchAll(hexColorRegex)) found.add(m[0]);
  for (const m of cleaned.matchAll(colorFunctionRegex)) found.add(m[0]);
  for (const m of cleaned.matchAll(namedColorDeclRegex)) found.add(m[1]);
  return [...found];
}

function isModuleSpecifierNode(node) {
  const parent = node.parent;
  if (!parent) return false;
  return (
    (parent.type === "ImportDeclaration" ||
      parent.type === "ExportNamedDeclaration" ||
      parent.type === "ExportAllDeclaration" ||
      parent.type === "ImportExpression") &&
    parent.source === node
  );
}

const noInlineHexRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow hardcoded color literals (#hex, rgb/rgba/hsl/hsla, named white/black declarations) in Vue templates, <script> blocks, <style> blocks, JS/TS modules, and CSS stylesheets outside packages/ui/tokens.css theme blocks.",
      category: "Best Practices",
    },
    messages: {
      noHexInStyle:
        'Hardcoded color literal "{{hex}}" is prohibited. Use design system tokens defined in packages/ui/tokens.css (e.g. var(--semantic-color-...)).',
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
            masked = masked.replace(
              /(?::root(?:\[[^\]]+\])?|\[data-theme="[^"]+"\]|\.dark)[^{]*\{[^}]*\}/g,
              (rootBlock) => rootBlock.replace(/[^\r\n]/g, " ")
            );
          }
          for (const v of findColorViolationsInCssText(masked)) {
            const loc = offsetToLoc(sourceText, v.index);
            context.report({
              loc,
              messageId: "noHexInStyle",
              data: { hex: v.literal },
            });
          }
        },
      };
    }

    const scriptColorVisitors = {
      Literal(node) {
        if (typeof node.value !== "string" || isModuleSpecifierNode(node)) {
          return;
        }
        const trimmed = node.value.trim().toLowerCase();
        const isCanvasNamedColor =
          (trimmed === "white" || trimmed === "black") &&
          node.parent?.type === "AssignmentExpression" &&
          node.parent.right === node &&
          node.parent.left?.type === "MemberExpression" &&
          (node.parent.left.property?.name === "fillStyle" ||
            node.parent.left.property?.name === "strokeStyle");
        const literals = collectColorLiteralsFromScriptString(node.value);
        if (isCanvasNamedColor) literals.push(node.value.trim());
        for (const hex of new Set(literals)) {
          context.report({
            node,
            messageId: "noHexInStyle",
            data: { hex },
          });
        }
      },
      TemplateElement(node) {
        const raw = node.value?.raw || "";
        if (!raw) return;
        for (const hex of collectColorLiteralsFromScriptString(raw)) {
          context.report({
            node,
            messageId: "noHexInStyle",
            data: { hex },
          });
        }
      },
    };

    // 2. Vue SFCs & JS/TS modules: check <template> style/presentation attributes, <script> AST literals, and <style> blocks
    const scriptAndStyleCheck = {
      ...scriptColorVisitors,
      ...(filename.endsWith(".vue")
        ? {
            "Program:exit"() {
              for (const styleMatch of sourceText.matchAll(
                /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
              )) {
                const contentStart = styleMatch.index + styleMatch[1].length;
                const masked = stripUrlFragmentsPreservingLength(
                  stripCssCommentsPreservingNewlines(styleMatch[2])
                );
                for (const v of findColorViolationsInCssText(masked)) {
                  const loc = offsetToLoc(sourceText, contentStart + v.index);
                  context.report({
                    loc,
                    messageId: "noHexInStyle",
                    data: { hex: v.literal },
                  });
                }
              }
            },
          }
        : {}),
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
            const rawAttr = context.sourceCode.getText(node.value);
            const literals = collectColorLiteralsFromScriptString(rawAttr);
            const attrName = !node.directive
              ? node.key?.name
              : node.key?.argument?.name;
            if (
              (attrName === "fill" ||
                attrName === "stroke" ||
                attrName === "color") &&
              /^['"]?(?:white|black)['"]?$/i.test(rawAttr.trim())
            ) {
              literals.push(rawAttr.replace(/['"]/g, "").trim());
            }
            for (const hex of new Set(literals)) {
              context.report({
                node: node.value,
                messageId: "noHexInStyle",
                data: { hex },
              });
            }
          }
        },
      },
      scriptAndStyleCheck
    );
  },
};

const SCOPED_NAVIGATION_CODEC_FILES = [
  "apps/web/src/canvas/palette.ts",
  "apps/web/src/canvas/ecrfRedirect.ts",
  "apps/web/src/composables/useScopedNavigation.ts",
  "apps/web/src/router/index.js",
  "apps/web/src/router/availability.js",
  "apps/web/src/views/ObservationView.vue",
];

const PRE_TRIAL_ENTRY_FILES = [
  "apps/web/src/views/PlansView.vue",
  "apps/web/src/views/TrialSetupView.vue",
  "apps/web/src/views/LoginView.vue",
  "apps/web/src/views/ForbiddenView.vue",
  "apps/web/src/views/EcrfLiveWorkspace.vue",
];

const WORKFLOW_ROUTE_PREFIXES = [
  "/studies",
  "/ecrf",
  "/ctms",
  "/ssu",
  "/training",
  "/etmf",
  "/tickets",
  "/knowledge",
  "/overview",
  "/observations",
  "/mdr",
  "/digitization",
  "/icf-builder",
  "/econsent-authoring",
  "/amendment-diff",
  "/imaging",
  "/devices",
  "/device-investigation",
  "/adjudication",
  "/cec",
  "/audit",
  "/auditor",
  "/rules",
  "/coding",
  "/data-lock",
  "/lakehouse",
  "/sensors",
  "/labs",
  "/dct",
  "/safety",
  "/exports",
  "/quality",
  "/notifications",
  "/documents",
  "/participant-wallet",
  "/lay-summaries",
];

function isBareWorkflowRoutePath(rawValue) {
  if (typeof rawValue !== "string") return false;
  const trimmed = rawValue.trim();
  if (!trimmed.startsWith("/") || trimmed.includes("?")) return false;
  const pathname = trimmed.split("#")[0];
  return WORKFLOW_ROUTE_PREFIXES.some(
    (prefix) => pathname === prefix || pathname.startsWith(`${prefix}/`)
  );
}

function getStaticPropertyKey(prop) {
  if (!prop || prop.type !== "Property" || prop.computed) return null;
  if (prop.key?.type === "Identifier") return prop.key.name;
  if (prop.key?.type === "Literal" && typeof prop.key.value === "string") {
    return prop.key.value;
  }
  return null;
}

function getStaticStringValue(expr) {
  if (!expr) return null;
  if (expr.type === "Literal" && typeof expr.value === "string") {
    return expr.value;
  }
  if (
    expr.type === "TemplateLiteral" &&
    expr.expressions.length === 0 &&
    expr.quasis.length === 1
  ) {
    return expr.quasis[0].value.cooked ?? expr.quasis[0].value.raw ?? null;
  }
  return null;
}

function expressionPreservesStudyScope(expr) {
  if (!expr) return false;
  if (expr.type === "ObjectExpression") {
    return expr.properties.some((prop) => {
      if (prop.type === "SpreadElement") return true;
      const keyName = getStaticPropertyKey(prop);
      return keyName === "site" || keyName === "participant";
    });
  }
  if (expr.type === "Identifier") {
    return expr.name === "nextQuery" || expr.name === "routeQuery";
  }
  if (expr.type === "CallExpression") {
    const calleeName =
      expr.callee?.type === "Identifier"
        ? expr.callee.name
        : expr.callee?.type === "MemberExpression" &&
            !expr.callee.computed &&
            expr.callee.property?.type === "Identifier"
          ? expr.callee.property.name
          : "";
    return calleeName.startsWith("scoped");
  }
  if (expr.type === "ConditionalExpression") {
    return (
      expressionPreservesStudyScope(expr.consequent) ||
      expressionPreservesStudyScope(expr.alternate)
    );
  }
  return false;
}

function isInsideNamedFunction(node, fnName) {
  let current = node?.parent;
  while (current) {
    if (
      current.type === "FunctionDeclaration" &&
      current.id?.type === "Identifier" &&
      current.id.name === fnName
    ) {
      return true;
    }
    current = current.parent;
  }
  return false;
}

function isInsideZoomStudiesElement(node) {
  let current = node?.parent;
  while (current) {
    if (current.type === "VElement") {
      const idAttr = (current.startTag?.attributes || []).find(
        (attr) =>
          !attr.directive &&
          attr.key?.type === "VIdentifier" &&
          attr.key.name === "id"
      );
      if (idAttr?.value?.value === "zoom-studies") {
        return true;
      }
    }
    current = current.parent;
  }
  return false;
}

const noUnscopedNavigationRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Enforce canonical study, site, and participant route scope across apps/web/src by blocking unscoped { name: 'study' } route objects, buildStudyScopedPath menu calls, and bare workflow path strings.",
      category: "Best Practices",
    },
    messages: {
      unscopedStudyObject:
        'Unscoped { name: "study" } route object drops active site/participant context. Use useScopedNavigation().scopedStudyLocation(...) or buildScopedStudyLocation(...) instead.',
      noBuildStudyScopedPath:
        "Do not call buildStudyScopedPath(...) for workspace or preview navigation; use buildScopedWorkflowPath(...) or useScopedNavigation().scopedWorkflowPath(...) so site and participant scope are preserved.",
      bareWorkflowPath:
        'Bare workflow route target "{{target}}" drops active study/site/participant context. Use useScopedNavigation() or buildScopedWorkflowPath(...) / buildStudiesWorkspacePath(...) instead.',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    const isWebSrc =
      filename.includes("/apps/web/src/") ||
      filename.startsWith("apps/web/src/");
    if (!isWebSrc) return {};

    const isCodecFile = SCOPED_NAVIGATION_CODEC_FILES.some(
      (suffix) => filename === suffix || filename.endsWith(`/${suffix}`)
    );
    const isPreTrialEntryFile = PRE_TRIAL_ENTRY_FILES.some(
      (suffix) => filename === suffix || filename.endsWith(`/${suffix}`)
    );
    const isStudyCanvasView =
      filename === "apps/web/src/views/StudyCanvasView.vue" ||
      filename.endsWith("/apps/web/src/views/StudyCanvasView.vue");
    const isAvailabilityCatalog =
      filename === "apps/web/src/router/availability.js" ||
      filename.endsWith("/apps/web/src/router/availability.js");

    function checkCallExpression(node) {
      const callee = node.callee;
      const calleeName =
        callee?.type === "Identifier"
          ? callee.name
          : callee?.type === "MemberExpression" &&
              !callee.computed &&
              callee.property?.type === "Identifier"
            ? callee.property.name
            : "";

      if (calleeName === "buildStudyScopedPath") {
        context.report({
          node,
          messageId: "noBuildStudyScopedPath",
        });
        return;
      }

      if (
        !isCodecFile &&
        !isPreTrialEntryFile &&
        callee?.type === "MemberExpression" &&
        !callee.computed &&
        callee.property?.type === "Identifier" &&
        (callee.property.name === "push" || callee.property.name === "replace")
      ) {
        const firstArg = node.arguments?.[0];
        const literalPath = getStaticStringValue(firstArg);
        if (literalPath && isBareWorkflowRoutePath(literalPath)) {
          context.report({
            node: firstArg,
            messageId: "bareWorkflowPath",
            data: { target: literalPath },
          });
        }
      }
    }

    function checkObjectExpression(node) {
      let isStudyRouteObject = false;
      let isRouteRecordDefinition = false;
      let queryProp = null;
      let pathProp = null;

      for (const prop of node.properties || []) {
        const keyName = getStaticPropertyKey(prop);
        if (!keyName) continue;
        if (
          keyName === "name" &&
          getStaticStringValue(prop.value) === "study"
        ) {
          isStudyRouteObject = true;
        } else if (
          keyName === "component" ||
          keyName === "components" ||
          keyName === "redirect" ||
          keyName === "meta"
        ) {
          isRouteRecordDefinition = true;
        } else if (keyName === "query") {
          queryProp = prop;
        } else if (keyName === "path") {
          pathProp = prop;
        }
      }

      if (isRouteRecordDefinition) return;

      if (isStudyRouteObject && !pathProp) {
        if (isCodecFile) return;
        if (
          isStudyCanvasView &&
          (isInsideZoomStudiesElement(node) ||
            isInsideNamedFunction(node, "studyLocation"))
        ) {
          return;
        }
        if (!queryProp || !expressionPreservesStudyScope(queryProp.value)) {
          context.report({
            node,
            messageId: "unscopedStudyObject",
          });
        }
        return;
      }

      if (!isCodecFile && !isPreTrialEntryFile && pathProp && !queryProp) {
        const literalPath = getStaticStringValue(pathProp.value);
        if (literalPath && isBareWorkflowRoutePath(literalPath)) {
          context.report({
            node: pathProp.value,
            messageId: "bareWorkflowPath",
            data: { target: literalPath },
          });
        }
      }
    }

    function checkReturnOrConditionalDefaultPath(expr) {
      if (isAvailabilityCatalog || !expr) return;
      if (
        expr.type === "MemberExpression" &&
        !expr.computed &&
        expr.property?.type === "Identifier" &&
        expr.property.name === "defaultPath"
      ) {
        const parent = expr.parent;
        if (
          parent?.type === "ReturnStatement" ||
          (parent?.type === "ConditionalExpression" &&
            (parent.consequent === expr || parent.alternate === expr))
        ) {
          context.report({
            node: expr,
            messageId: "bareWorkflowPath",
            data: {
              target: context.sourceCode.getText(expr),
            },
          });
        }
      }
    }

    const scriptVisitors = {
      CallExpression: checkCallExpression,
      ObjectExpression: checkObjectExpression,
      MemberExpression: checkReturnOrConditionalDefaultPath,
    };

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return scriptVisitors;
    }

    return parserServices.defineTemplateBodyVisitor(
      {
        CallExpression: checkCallExpression,
        ObjectExpression: checkObjectExpression,
        MemberExpression: checkReturnOrConditionalDefaultPath,
        VAttribute(node) {
          if (isCodecFile || isPreTrialEntryFile) return;
          const parentElement = node.parent?.parent;
          const rawTag = parentElement?.rawName || parentElement?.name || "";
          const normalizedTag = rawTag.toLowerCase();
          const isRouterLink =
            normalizedTag === "router-link" || normalizedTag === "routerlink";
          if (!isRouterLink) return;

          if (
            !node.directive &&
            node.key?.type === "VIdentifier" &&
            node.key.name === "to" &&
            node.value?.value &&
            isBareWorkflowRoutePath(node.value.value)
          ) {
            context.report({
              node: node.value,
              messageId: "bareWorkflowPath",
              data: { target: node.value.value },
            });
            return;
          }

          if (
            node.directive &&
            node.key?.type === "VDirectiveKey" &&
            node.key.name?.name === "bind" &&
            node.key.argument?.type === "VIdentifier" &&
            node.key.argument.name === "to" &&
            node.value?.expression
          ) {
            const literalPath = getStaticStringValue(node.value.expression);
            if (literalPath && isBareWorkflowRoutePath(literalPath)) {
              context.report({
                node: node.value.expression,
                messageId: "bareWorkflowPath",
                data: { target: literalPath },
              });
            }
          }
        },
      },
      scriptVisitors
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

const overlayClassTokenRegex =
  /(?:^|[\s"'`[{])([a-z0-9_-]*(?:modal|drawer|dialog)-(?:overlay|backdrop|mask|bg)|overlay-backdrop)(?:$|[\s"'`\]}:,])/i;

function getElementOverlayClassToken(elementNode, sourceCode) {
  if (!elementNode?.startTag?.attributes) return null;
  for (const attr of elementNode.startTag.attributes) {
    const isStaticClass =
      !attr.directive &&
      attr.key?.type === "VIdentifier" &&
      attr.key.name === "class";
    const isDynamicClass =
      attr.directive &&
      attr.key?.type === "VDirectiveKey" &&
      attr.key.name?.name === "bind" &&
      attr.key.argument?.name === "class";
    if ((isStaticClass || isDynamicClass) && attr.value) {
      const rawVal = sourceCode.getText(attr.value);
      const match = rawVal.match(overlayClassTokenRegex);
      if (match) return match[1];
    }
  }
  return null;
}

function getStaticAttrValue(elementNode, attrName) {
  if (!elementNode?.startTag?.attributes) return null;
  for (const attr of elementNode.startTag.attributes) {
    if (
      !attr.directive &&
      attr.key?.type === "VIdentifier" &&
      attr.key.name === attrName
    ) {
      return attr.value?.value ?? "";
    }
  }
  return null;
}

function hasStaticOrBoundAttr(elementNode, attrName) {
  if (!elementNode?.startTag?.attributes) return false;
  return elementNode.startTag.attributes.some((attr) => {
    if (!attr.directive && attr.key?.type === "VIdentifier") {
      return attr.key.name === attrName;
    }
    if (attr.directive && attr.key?.type === "VDirectiveKey") {
      return (
        attr.key.name?.name === "bind" && attr.key.argument?.name === attrName
      );
    }
    return false;
  });
}

function hasClickDirective(elementNode) {
  if (!elementNode?.startTag?.attributes) return false;
  return elementNode.startTag.attributes.some(
    (attr) =>
      attr.directive &&
      attr.key?.type === "VDirectiveKey" &&
      attr.key.name?.name === "on" &&
      attr.key.argument?.name === "click"
  );
}

function isDialogVElement(node) {
  if (!node || node.type !== "VElement") return false;
  const role = getStaticAttrValue(node, "role");
  if (role !== "dialog" && role !== "alertdialog") return false;
  return hasStaticOrBoundAttr(node, "aria-modal");
}

function hasDialogDescendantOrSelf(node) {
  if (!node || node.type !== "VElement") return false;
  if (isDialogVElement(node)) return true;
  for (const child of node.children || []) {
    if (child.type === "VElement" && hasDialogDescendantOrSelf(child)) {
      return true;
    }
  }
  return false;
}

function hasDialogSibling(node) {
  const siblings = node?.parent?.children || [];
  return siblings.some(
    (sib) => sib !== node && sib.type === "VElement" && isDialogVElement(sib)
  );
}

const rawModalOverlayClassRegex =
  /(?:modal|dialog)-(?:overlay|backdrop|mask|bg)|overlay-backdrop/i;

// Shrink-only legacy allowlist of unmigrated apps/web modal files that still rely on
// manual useFocusTrap + dialog ARIA markup. Do not add new entries; migrate to ClinicalModal.
const LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST = new Set([
  "components/CodingQueueTable.vue",
  "components/DictionaryBrowserModal.vue",
  "components/auditor/AuditorExportModal.vue",
  "components/clinical/AggregateSafetyConsole.vue",
  "components/clinical/BulkIpKitReconciliationDrawer.vue",
  "components/clinical/DeviceInvestigationConsole.vue",
  "components/clinical/EhrReconciliationDrawer.vue",
  "components/clinical/LabGovernanceConsole.vue",
  "components/clinical/TelehealthDtpConsole.vue",
  "components/etmf/CraQcQueue.vue",
  "components/etmf/DocumentGrid.vue",
  "components/etmf/PdfPreviewModal.vue",
  "components/fileshare/MediaPreviewModal.vue",
  "components/imaging/BatchDicomDropzoneModal.vue",
  "components/imaging/DualReaderAdjudicationPanel.vue",
  "components/imaging/OncologyCalipers.vue",
  "components/quality/StatisticalForensicsViewer.vue",
  "components/shared/ReAuthModal.vue",
  "components/tickets/TicketCreateModal.vue",
  "components/tickets/TicketDetailDrawer.vue",
  "views/AmendmentDiffView.vue",
  "views/MdrView.vue",
]);

const noRawOverlaysRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow raw modal/drawer overlay divs and vuejs-accessibility backdrop suppressions outside packages/ui primitives.",
      category: "Accessibility",
    },
    messages: {
      noRawOverlay:
        'Raw overlay container "{{className}}" is prohibited. Migrate to ClinicalModal or ClinicalDrawer from "ui" (ADR-2545 / ADR-2653).',
      noBackdropSuppression:
        'Suppressing vuejs-accessibility rules on overlay/backdrop elements is prohibited. Migrate to ClinicalModal or ClinicalDrawer (or use role="presentation" on focus-trapped backdrops).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    if (
      !filename.endsWith(".vue") ||
      filename.includes("/packages/ui/src/components/")
    ) {
      return {};
    }

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return {};
    }

    const webSrcMarker = "/apps/web/src/";
    const webSrcIdx = filename.lastIndexOf(webSrcMarker);
    const relWebSrcPath =
      webSrcIdx >= 0 ? filename.slice(webSrcIdx + webSrcMarker.length) : null;

    const sourceText = context.sourceCode.getText();
    const scriptContent = Array.from(
      sourceText.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/gi),
      (m) => m[1]
    ).join("\n");
    const importsFocusTrap = /\buseFocusTrap\b/.test(scriptContent);

    const nativeOverlayElements = [];

    return parserServices.defineTemplateBodyVisitor(
      {
        VElement(node) {
          const rawTag = node.rawName || node.name;
          if (!["div", "section", "aside"].includes(rawTag)) return;

          const overlayClass = getElementOverlayClassToken(
            node,
            context.sourceCode
          );
          if (!overlayClass) return;

          nativeOverlayElements.push(node);

          const isDisallowedWebRawModalOverlay =
            relWebSrcPath !== null &&
            rawModalOverlayClassRegex.test(overlayClass) &&
            !LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST.has(relWebSrcPath);

          if (isDisallowedWebRawModalOverlay) {
            context.report({
              node: node.startTag,
              messageId: "noRawOverlay",
              data: { className: overlayClass },
            });
            return;
          }

          const hasGuardedDialog =
            importsFocusTrap &&
            (hasDialogDescendantOrSelf(node) || hasDialogSibling(node));
          const role = getStaticAttrValue(node, "role");
          const validBackdropInteraction =
            isDialogVElement(node) ||
            !hasClickDirective(node) ||
            role === "presentation" ||
            role === "none";

          if (!hasGuardedDialog || !validBackdropInteraction) {
            context.report({
              node: node.startTag,
              messageId: "noRawOverlay",
              data: { className: overlayClass },
            });
          }
        },
      },
      {
        "Program:exit"(programNode) {
          const templateComments = programNode.templateBody?.comments || [];
          for (const comment of templateComments) {
            const text = comment.value || "";
            if (
              !/eslint-disable/.test(text) ||
              !/(?:vuejs-accessibility\/click-events-have-key-events|vuejs-accessibility\/no-static-element-interactions)/.test(
                text
              )
            ) {
              continue;
            }
            const commentEndLine = comment.loc.end.line;
            const targetsOverlay = nativeOverlayElements.some((el) => {
              const startLine = el.startTag.loc.start.line;
              return (
                startLine >= commentEndLine && startLine <= commentEndLine + 3
              );
            });
            if (targetsOverlay) {
              context.report({
                loc: comment.loc,
                messageId: "noBackdropSuppression",
              });
            }
          }
        },
      }
    );
  },
};

const suppressedOutlineDeclRegex =
  /\boutline(?:-style|-width)?\s*:\s*(?:none|0(?:px)?)\b/gi;

function maskCssStringsPreservingLength(text) {
  return text.replace(/(["'])(?:\\.|(?!\1)[^\\\r\n])*\1/g, (s) =>
    " ".repeat(s.length)
  );
}

function splitTopLevelCommas(selectorText) {
  const parts = [];
  let depth = 0;
  let start = 0;
  for (let i = 0; i < selectorText.length; i++) {
    const ch = selectorText[i];
    if (ch === "(" || ch === "[") depth++;
    else if (ch === ")" || ch === "]") depth = Math.max(0, depth - 1);
    else if (ch === "," && depth === 0) {
      const piece = selectorText.slice(start, i).trim();
      if (piece) parts.push(piece);
      start = i + 1;
    }
  }
  const tail = selectorText.slice(start).trim();
  if (tail) parts.push(tail);
  return parts;
}

function findUnscopedSuppressedOutlinesInCss(cssText) {
  const noComments = stripCssCommentsPreservingNewlines(cssText);
  const clean = maskCssStringsPreservingLength(noComments);
  const violations = [];
  const stack = [];
  let segStart = 0;

  for (let i = 0; i < clean.length; i++) {
    const ch = clean[i];
    if (ch === ";") {
      if (stack.length === 0 || stack[stack.length - 1].isAtRule) {
        segStart = i + 1;
      }
    } else if (ch === "{") {
      const header = clean.slice(segStart, i).trim();
      if (header.startsWith("@")) {
        stack.push({ isAtRule: true });
      } else {
        stack.push({
          isAtRule: false,
          selectorText: noComments.slice(segStart, i).trim(),
          bodyStart: i + 1,
        });
      }
      segStart = i + 1;
    } else if (ch === "}") {
      const entry = stack.pop();
      if (entry && !entry.isAtRule) {
        const body = clean.slice(entry.bodyStart, i);
        const selectors = splitTopLevelCommas(entry.selectorText);
        const isProgrammaticFocusOnly =
          selectors.length > 0 &&
          selectors.every(
            (sel) =>
              /:focus:not\(\s*:focus-visible\s*\)/.test(sel) ||
              (/\[tabindex=["']?-1["']?\]:focus(?:-visible)?\b/.test(sel) &&
                !/\b(?:button|input|select|textarea|a|summary)\b|\[role=["']?(?:button|link|tab|menuitem|checkbox|radio|switch|option|combobox|textbox)["']?\]/i.test(
                  sel
                ))
          );
        if (!isProgrammaticFocusOnly) {
          for (const match of body.matchAll(suppressedOutlineDeclRegex)) {
            violations.push({
              offset: entry.bodyStart + match.index,
              declaration: match[0].trim(),
            });
          }
        }
      }
      segStart = i + 1;
    }
  }
  return violations;
}

const noSuppressedFocusOutlineRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow bare outline: none or outline: 0 rules without :focus-visible across packages/ui, apps/web, and apps/subject-portal (WCAG 2.2 SC 2.4.7 / Failure F78).",
      category: "Accessibility",
    },
    messages: {
      noSuppressedOutline:
        'Bare "{{declaration}}" without :focus-visible is prohibited (WCAG 2.2 F78). Use the shared two-color :focus-visible indicator, or scope programmatic dialog/main focus suppression to :focus:not(:focus-visible).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");

    const sourceText = context.sourceCode.getText();

    if (filename.endsWith(".css")) {
      return {
        Program() {
          for (const v of findUnscopedSuppressedOutlinesInCss(sourceText)) {
            context.report({
              loc: offsetToLoc(sourceText, v.offset),
              messageId: "noSuppressedOutline",
              data: { declaration: v.declaration },
            });
          }
        },
      };
    }

    if (!filename.endsWith(".vue")) {
      return {};
    }

    const styleBlockCheck = {
      "Program:exit"() {
        for (const styleMatch of sourceText.matchAll(
          /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
        )) {
          const contentStart = styleMatch.index + styleMatch[1].length;
          for (const v of findUnscopedSuppressedOutlinesInCss(styleMatch[2])) {
            context.report({
              loc: offsetToLoc(sourceText, contentStart + v.offset),
              messageId: "noSuppressedOutline",
              data: { declaration: v.declaration },
            });
          }
        }
      },
    };

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return styleBlockCheck;
    }

    return parserServices.defineTemplateBodyVisitor(
      {
        VAttribute(node) {
          const isStaticStyle =
            !node.directive &&
            node.key?.type === "VIdentifier" &&
            node.key.name === "style";
          const isDynamicStyle =
            node.directive &&
            node.key?.type === "VDirectiveKey" &&
            node.key.name?.name === "bind" &&
            node.key.argument?.name === "style";
          if ((isStaticStyle || isDynamicStyle) && node.value) {
            const rawVal = context.sourceCode.getText(node.value);
            for (const match of rawVal.matchAll(suppressedOutlineDeclRegex)) {
              context.report({
                node: node.value,
                messageId: "noSuppressedOutline",
                data: { declaration: match[0].trim() },
              });
            }
          }
        },
      },
      styleBlockCheck
    );
  },
};

function isAriaHiddenElement(elementNode) {
  return getStaticAttrValue(elementNode, "aria-hidden") === "true";
}

function isSrOnlyElement(elementNode) {
  const cls = getStaticAttrValue(elementNode, "class") || "";
  return /\b(?:sr-only|visually-hidden)\b/.test(cls);
}

function hasNonEmptyAriaLabelOrLabelledBy(elementNode) {
  if (!elementNode?.startTag?.attributes) return false;
  for (const attr of elementNode.startTag.attributes) {
    if (
      !attr.directive &&
      attr.key?.type === "VIdentifier" &&
      (attr.key.name === "aria-label" || attr.key.name === "aria-labelledby")
    ) {
      if ((attr.value?.value ?? "").trim().length > 0) return true;
    }
    if (
      attr.directive &&
      attr.key?.type === "VDirectiveKey" &&
      attr.key.name?.name === "bind" &&
      (attr.key.argument?.name === "aria-label" ||
        attr.key.argument?.name === "aria-labelledby")
    ) {
      const expr = attr.value?.expression;
      if (!expr) continue;
      if (expr.type === "Literal") {
        if (String(expr.value ?? "").trim().length > 0) return true;
      } else if (
        expr.type === "TemplateLiteral" &&
        expr.expressions.length === 0
      ) {
        const raw = expr.quasis.map((q) => q.value.cooked ?? "").join("");
        if (raw.trim().length > 0) return true;
      } else {
        return true;
      }
    }
  }
  return false;
}

function extractStaticExpressionStrings(expr) {
  if (!expr) return { allLiteral: true, strings: [] };
  if (expr.type === "Literal") {
    return { allLiteral: true, strings: [String(expr.value ?? "")] };
  }
  if (expr.type === "TemplateLiteral" && expr.expressions.length === 0) {
    return {
      allLiteral: true,
      strings: [expr.quasis.map((q) => q.value.cooked ?? "").join("")],
    };
  }
  if (expr.type === "ConditionalExpression") {
    const cons = extractStaticExpressionStrings(expr.consequent);
    const alt = extractStaticExpressionStrings(expr.alternate);
    return {
      allLiteral: cons.allLiteral && alt.allLiteral,
      strings: [...cons.strings, ...alt.strings],
    };
  }
  if (expr.type === "LogicalExpression") {
    const left = extractStaticExpressionStrings(expr.left);
    const right = extractStaticExpressionStrings(expr.right);
    return {
      allLiteral: left.allLiteral && right.allLiteral,
      strings: [...left.strings, ...right.strings],
    };
  }
  return { allLiteral: false, strings: [] };
}

function normalizeLabelWords(text) {
  return String(text || "")
    .replace(/[^\p{L}\p{N}]+/gu, " ")
    .toLowerCase()
    .trim()
    .replace(/\s+/g, " ");
}

function isCreateButtonCall(node) {
  if (!node || node.type !== "CallExpression") return false;
  const callee = node.callee;
  const isCreateEl =
    callee?.type === "MemberExpression" &&
    !callee.computed &&
    callee.property?.type === "Identifier" &&
    callee.property.name === "createElement";
  if (!isCreateEl || node.arguments.length === 0) return false;
  const firstArg = node.arguments[0];
  return (
    firstArg?.type === "Literal" &&
    typeof firstArg.value === "string" &&
    firstArg.value.toLowerCase() === "button"
  );
}

function getStaticJsString(node) {
  if (!node) return null;
  if (node.type === "Literal" && typeof node.value === "string") {
    return node.value;
  }
  if (node.type === "TemplateLiteral" && node.expressions.length === 0) {
    return node.quasis.map((q) => q.value.cooked ?? "").join("");
  }
  return null;
}

const noUnlabeledGlyphButtonRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow glyph-only or empty <button> elements without a programmatic accessible name via aria-label or aria-labelledby (WCAG 2.2 SC 4.1.2).",
      category: "Accessibility",
    },
    messages: {
      unlabeledGlyphButton:
        'Glyph or icon-only <button> lacks an accessible name (WCAG 2.2 SC 4.1.2 / Failure F68). Use ClinicalIconButton from "ui" or provide a non-empty aria-label / aria-labelledby.',
    },
  },
  create(context) {
    const buttonDecls = new Map();

    const jsVisitor = {
      VariableDeclarator(node) {
        if (node.id?.type === "Identifier" && isCreateButtonCall(node.init)) {
          buttonDecls.set(node.id.name, {
            node: node.init,
            assignedGlyphOnly: false,
            hasAccessibleName: false,
          });
        }
      },
      AssignmentExpression(node) {
        if (
          node.left?.type !== "MemberExpression" ||
          node.left.computed ||
          node.left.object?.type !== "Identifier" ||
          node.left.property?.type !== "Identifier"
        ) {
          return;
        }
        const entry = buttonDecls.get(node.left.object.name);
        if (!entry) return;
        const prop = node.left.property.name;
        if (prop === "ariaLabel") {
          const val = getStaticJsString(node.right);
          if (val === null || val.trim().length > 0) {
            entry.hasAccessibleName = true;
          }
          return;
        }
        if (
          prop === "innerText" ||
          prop === "textContent" ||
          prop === "innerHTML"
        ) {
          const val = getStaticJsString(node.right);
          if (val !== null) {
            const stripped =
              prop === "innerHTML" ? val.replace(/<[^>]+>/g, " ") : val;
            if (/[\p{L}\p{N}]/u.test(stripped)) {
              entry.hasAccessibleName = true;
            } else {
              entry.assignedGlyphOnly = true;
            }
          } else {
            entry.hasAccessibleName = true;
          }
        }
      },
      CallExpression(node) {
        if (
          node.callee?.type !== "MemberExpression" ||
          node.callee.computed ||
          node.callee.object?.type !== "Identifier" ||
          node.callee.property?.type !== "Identifier"
        ) {
          return;
        }
        const entry = buttonDecls.get(node.callee.object.name);
        if (!entry) return;
        if (
          node.callee.property.name === "setAttribute" &&
          node.arguments.length >= 2
        ) {
          const attrName = getStaticJsString(node.arguments[0]);
          if (attrName === "aria-label" || attrName === "aria-labelledby") {
            const attrVal = getStaticJsString(node.arguments[1]);
            if (attrVal === null || attrVal.trim().length > 0) {
              entry.hasAccessibleName = true;
            }
          }
        }
      },
      "Program:exit"() {
        for (const entry of buttonDecls.values()) {
          if (entry.assignedGlyphOnly && !entry.hasAccessibleName) {
            context.report({
              node: entry.node,
              messageId: "unlabeledGlyphButton",
            });
          }
        }
      },
    };

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return jsVisitor;
    }

    return parserServices.defineTemplateBodyVisitor(
      {
        VElement(node) {
          const rawTag = node.rawName || node.name;
          if (rawTag !== "button") return;
          if (hasNonEmptyAriaLabelOrLabelledBy(node)) return;

          let staticText = "";
          const exprStrings = [];
          let hasDynamicText = false;
          let hasSlotOrComponent = false;

          function collectButtonContent(children) {
            for (const child of children || []) {
              if (child.type === "VText") {
                staticText += child.value;
              } else if (child.type === "VExpressionContainer") {
                const info = extractStaticExpressionStrings(child.expression);
                if (info.allLiteral) {
                  exprStrings.push(...info.strings);
                } else {
                  hasDynamicText = true;
                }
              } else if (child.type === "VElement") {
                const childTag = child.rawName || child.name;
                if (childTag === "slot") {
                  if (!child.children || child.children.length === 0) {
                    hasSlotOrComponent = true;
                  } else {
                    collectButtonContent(child.children);
                  }
                } else if (
                  /^[A-Z]/.test(childTag) ||
                  childTag.includes("-") ||
                  childTag === "component"
                ) {
                  hasSlotOrComponent = true;
                } else if (childTag === "img") {
                  const alt = getStaticAttrValue(child, "alt");
                  if (alt && /[\p{L}\p{N}]/u.test(alt)) {
                    staticText += ` ${alt}`;
                  } else if (
                    hasStaticOrBoundAttr(child, "alt") &&
                    alt === null
                  ) {
                    hasDynamicText = true;
                  }
                } else if (!isAriaHiddenElement(child)) {
                  collectButtonContent(child.children);
                }
              }
            }
          }

          collectButtonContent(node.children);
          if (hasDynamicText || hasSlotOrComponent) return;

          const combined = `${staticText} ${exprStrings.join(" ")}`.trim();
          if (!/[\p{L}\p{N}]/u.test(combined)) {
            context.report({
              node: node.startTag,
              messageId: "unlabeledGlyphButton",
            });
          }
        },
      },
      jsVisitor
    );
  },
};

const noAriaLabelMismatchRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow static aria-label values on buttons that omit or override visible text (WCAG 2.2 SC 2.5.3 Label in Name / Failure F96).",
      category: "Accessibility",
    },
    messages: {
      ariaLabelMismatch:
        'Static aria-label "{{ariaLabel}}" omits visible button text "{{visibleText}}" (WCAG 2.2 SC 2.5.3 Failure F96). Keep visible text at the start of the accessible name or append context inside a .sr-only span.',
      ariaLabelOverridesDynamicText:
        'Static aria-label "{{ariaLabel}}" overrides dynamic visible button text (WCAG 2.2 SC 2.5.3 Failure F96). Append supplementary context in a .sr-only span instead of overriding aria-label.',
    },
  },
  create(context) {
    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return {};
    }

    return parserServices.defineTemplateBodyVisitor({
      VElement(node) {
        const rawTag = node.rawName || node.name;
        const role = getStaticAttrValue(node, "role");
        if (rawTag !== "button" && role !== "button") return;

        const ariaLabel = getStaticAttrValue(node, "aria-label");
        if (!ariaLabel || !ariaLabel.trim()) return;

        const segments = [""];
        let hasDynamicVisible = false;

        function collectVisibleSegments(children) {
          for (const child of children || []) {
            if (child.type === "VText") {
              segments[segments.length - 1] += child.value;
            } else if (child.type === "VExpressionContainer") {
              const info = extractStaticExpressionStrings(child.expression);
              if (!info.allLiteral) {
                hasDynamicVisible = true;
              } else if (info.strings.some((s) => /[\p{L}\p{N}]/u.test(s))) {
                segments.push(...info.strings);
              }
              segments.push("");
            } else if (child.type === "VElement") {
              const childTag = child.rawName || child.name;
              if (isAriaHiddenElement(child) || isSrOnlyElement(child)) {
                continue;
              }
              if (
                childTag === "slot" ||
                /^[A-Z]/.test(childTag) ||
                childTag.includes("-")
              ) {
                continue;
              }
              collectVisibleSegments(child.children);
            }
          }
        }

        collectVisibleSegments(node.children);

        const normAria = normalizeLabelWords(ariaLabel);
        const normSegments = segments.map(normalizeLabelWords).filter(Boolean);

        if (normSegments.length > 0) {
          let searchFrom = 0;
          let matchedAll = true;
          for (const seg of normSegments) {
            const idx = normAria.indexOf(seg, searchFrom);
            if (idx === -1) {
              matchedAll = false;
              break;
            }
            searchFrom = idx + seg.length;
          }
          if (!matchedAll) {
            context.report({
              node: node.startTag,
              messageId: "ariaLabelMismatch",
              data: {
                ariaLabel: ariaLabel.trim(),
                visibleText: normSegments.join(" "),
              },
            });
          }
        } else if (hasDynamicVisible) {
          context.report({
            node: node.startTag,
            messageId: "ariaLabelOverridesDynamicText",
            data: {
              ariaLabel: ariaLabel.trim(),
            },
          });
        }
      },
    });
  },
};

const noMisusedAiTokensRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Reserve --semantic-color-ai* and --color-ai* exclusively for governed AI provenance components and packages/ui/tokens.css definitions (UX-8 / ADR-2635 / ADR-2654).",
      category: "Best Practices",
    },
    messages: {
      misusedAiToken:
        'AI token "{{tokenName}}" is reserved exclusively for governed AI components (apps/web/src/components/ai/** and packages/ui/src/components/clinical/Ai*.vue). Use semantic status, interactive, or categorical tokens instead.',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    if (isAllowedAiTokenFile(filename)) {
      return {};
    }

    const sourceText = context.sourceCode.getText();

    if (filename.endsWith(".css")) {
      const clean = stripCssCommentsPreservingNewlines(sourceText);
      return {
        Program() {
          for (const match of clean.matchAll(aiTokenRegex)) {
            context.report({
              loc: offsetToLoc(sourceText, match.index),
              messageId: "misusedAiToken",
              data: { tokenName: match[0] },
            });
          }
        },
      };
    }

    if (filename.endsWith(".vue")) {
      const scriptAndStyleCheck = {
        "Program:exit"() {
          for (const sm of sourceText.matchAll(
            /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
          )) {
            const contentStart = sm.index + sm[1].length;
            const clean = stripCssCommentsPreservingNewlines(sm[2]);
            for (const match of clean.matchAll(aiTokenRegex)) {
              context.report({
                loc: offsetToLoc(sourceText, contentStart + match.index),
                messageId: "misusedAiToken",
                data: { tokenName: match[0] },
              });
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
            for (const match of clean.matchAll(aiTokenRegex)) {
              context.report({
                loc: offsetToLoc(sourceText, contentStart + match.index),
                messageId: "misusedAiToken",
                data: { tokenName: match[0] },
              });
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
            if (!node.value) return;
            const text = context.sourceCode.getText(node.value);
            for (const match of text.matchAll(aiTokenRegex)) {
              context.report({
                node: node.value,
                messageId: "misusedAiToken",
                data: { tokenName: match[0] },
              });
            }
          },
        },
        scriptAndStyleCheck
      );
    }

    return {
      Program() {
        const clean = stripCssCommentsPreservingNewlines(sourceText).replace(
          /\/\/.*$/gm,
          (c) => " ".repeat(c.length)
        );
        for (const match of clean.matchAll(aiTokenRegex)) {
          context.report({
            loc: offsetToLoc(sourceText, match.index),
            messageId: "misusedAiToken",
            data: { tokenName: match[0] },
          });
        }
      },
    };
  },
};

const TABULAR_NATIVE_INPUT_TYPES = new Set([
  "number",
  "date",
  "datetime-local",
  "time",
  "month",
  "week",
  "tel",
  "range",
  "numeric",
]);

const NON_TEXT_INPUT_TYPES = new Set([
  "checkbox",
  "radio",
  "hidden",
  "file",
  "submit",
  "button",
  "reset",
  "image",
]);

const TABULAR_INPUT_MODES = new Set(["numeric", "decimal"]);

const COUNTDOWN_CLASS_SET = new Set([
  "countdown-badge",
  "overview-countdown-text",
  "sla-mini-badge",
  "retry-countdown-banner",
  "sla-timer",
  "countdown-timer",
]);

const COUNT_BADGE_CLASS_SET = new Set([
  "badge-count",
  "count-badge",
  "column-count",
  "bulk-count-badge",
  "progress-badge",
  "sync-badge",
  "badge-unread",
  "vas-value-badge",
  "grade-badge",
]);

const KPI_VALUE_CLASS_SET = new Set([
  "kpi-value",
  "metric-value",
  "stat-value",
  "score-value",
  "kpi-number",
  "metric-number",
]);

const COUNTDOWN_ID_REGEX =
  /(?:^|[-_])(?:[a-z0-9]+-)*(?:countdown|sla-timer|retry-timer|time-remaining)(?:[-_]|$)/i;
const COUNTDOWN_EXPR_REGEX =
  /\b(?:remainingSeconds|days_remaining|trialDaysRemaining|formatSlaRemaining|retryCountdown|countdownSeconds|slaRemaining|timeRemaining)\b/;
const BADGE_CLASS_TOKEN_REGEX = /(?:^|-)(?:badge|pill)(?:-|$)/i;
const BADGE_COUNT_EXPR_REGEX =
  /\.(?:length|size)\b|\b[a-zA-Z0-9_]*(?:Count|Pct|Percent|Total|Unread|Queued)\b/;
const KPI_ID_REGEX =
  /^(?:observation-value|observation-version|compliance-rate-pct|compliance-(?:completed|pending|overdue)-count|wallet-balance-amount|wallet-lifetime-[a-z-]+|wallet-taxable-ytd)$/;
const KPI_EXPR_REGEX =
  /\b(?:formatCurrency|formatSignedCurrency|formatObservationValue)\s*\(/;

function extractTabularCssRulesFromText(rawCss, alignedClasses, scopedPairs) {
  if (!rawCss) return;
  const clean = stripCssCommentsPreservingNewlines(rawCss);
  const blockRegex = /([^{};@]+)\{([^{}]*)\}/g;
  for (const match of clean.matchAll(blockRegex)) {
    const selectorGroup = match[1].trim();
    const body = match[2];
    const decls = [
      ...body.matchAll(
        /font-variant-numeric\s*:\s*([^;!]+?)(?:\s*!important)?\s*(?:;|$)/gi
      ),
    ];
    if (decls.length === 0) continue;
    const lastVal = decls[decls.length - 1][1].trim();
    const isTabular =
      /\btabular-nums\b/.test(lastVal) ||
      /--(?:semantic-)?font-variant-numeric-tabular\b/.test(lastVal);
    if (!isTabular || /\bnormal\b/.test(lastVal)) continue;

    for (const rawSel of selectorGroup.split(",")) {
      const sel = rawSel
        .replace(/:not\([^)]*\)/g, "")
        .replace(/::?[a-zA-Z-]+(?:\([^)]*\))?/g, "")
        .trim();
      if (!sel || /\[/.test(sel)) continue;
      const parts = sel.split(/[\s>+~]+/).filter(Boolean);
      if (parts.length === 1 && parts[0].startsWith(".")) {
        for (const cm of parts[0].matchAll(/\.([a-zA-Z0-9_-]+)/g)) {
          alignedClasses.add(cm[1]);
        }
      } else if (
        parts.length === 2 &&
        parts[0].startsWith(".") &&
        parts[1].startsWith(".")
      ) {
        const anc = parts[0].slice(1).split(".")[0];
        const child = parts[1].slice(1).split(".")[0];
        if (anc && child) {
          scopedPairs.push({ ancestorClass: anc, className: child });
        }
      }
    }
  }
}

function getBaseTabularCssConfig(filename) {
  const normalized = filename.split(path.sep).join("/");
  const alignedClasses = new Set(["tabular-nums", "numeric", "clinical-badge"]);
  const scopedPairs = [];
  if (fs.existsSync(tokensCssPath)) {
    extractTabularCssRulesFromText(
      fs.readFileSync(tokensCssPath, "utf8"),
      alignedClasses,
      scopedPairs
    );
  }
  if (normalized.includes("/apps/web/") && fs.existsSync(webStyleCssPath)) {
    extractTabularCssRulesFromText(
      fs.readFileSync(webStyleCssPath, "utf8"),
      alignedClasses,
      scopedPairs
    );
  } else if (
    normalized.includes("/apps/subject-portal/") &&
    fs.existsSync(portalStyleCssPath)
  ) {
    extractTabularCssRulesFromText(
      fs.readFileSync(portalStyleCssPath, "utf8"),
      alignedClasses,
      scopedPairs
    );
  }
  return { alignedClasses, scopedPairs };
}

const requireTabularNumsRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Require tabular numerals (tabular-nums, data-tabular-nums, <time>, or --font-variant-numeric-tabular) on numeric/date form controls, countdown timers, count badges, and KPI readouts.",
      category: "Design System",
    },
    messages: {
      unalignedNumericControl:
        'Numeric or date control <{{name}}> must align digits using tabular numerals (e.g., data-tabular-nums, class="tabular-nums", inputmode="numeric", or native numeric/date input type).',
      unalignedCountdown:
        'Live countdown or timer element <{{name}}> must apply tabular numerals (e.g., class="tabular-nums", <time>, or font-variant-numeric: var(--font-variant-numeric-tabular)).',
      unalignedBadge:
        'Numeric count badge <{{name}}> must apply tabular numerals (e.g., class="clinical-badge", class="tabular-nums", or font-variant-numeric: var(--font-variant-numeric-tabular)).',
      unalignedKpi:
        'Clinical value, KPI, or currency readout <{{name}}> must apply tabular numerals (e.g., class="tabular-nums" or font-variant-numeric: var(--font-variant-numeric-tabular)).',
      tabularOverrideForbidden:
        "Overriding tabular numerals with 'font-variant-numeric: normal' is prohibited; rely on shared tokens in packages/ui/tokens.css.",
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    const sourceText = context.sourceCode.getText();
    const normalOverrideRegex =
      /font-variant-numeric\s*:\s*normal\b|fontVariantNumeric\s*:\s*['"`]normal['"`]/gi;

    if (filename.endsWith(".css")) {
      return {
        Program() {
          const clean = stripCssCommentsPreservingNewlines(sourceText);
          for (const m of clean.matchAll(normalOverrideRegex)) {
            context.report({
              loc: offsetToLoc(sourceText, m.index),
              messageId: "tabularOverrideForbidden",
            });
          }
        },
      };
    }

    if (filename.endsWith(".vue")) {
      const { alignedClasses, scopedPairs } = getBaseTabularCssConfig(filename);
      for (const sm of sourceText.matchAll(
        /<style\b[^>]*>([\s\S]*?)<\/style>/gi
      )) {
        extractTabularCssRulesFromText(sm[1], alignedClasses, scopedPairs);
      }

      const styleBlockCheck = {
        "Program:exit"() {
          for (const sm of sourceText.matchAll(
            /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
          )) {
            const contentStart = sm.index + sm[1].length;
            const clean = stripCssCommentsPreservingNewlines(sm[2]);
            for (const m of clean.matchAll(normalOverrideRegex)) {
              context.report({
                loc: offsetToLoc(sourceText, contentStart + m.index),
                messageId: "tabularOverrideForbidden",
              });
            }
          }
        },
      };

      const parserServices =
        context.sourceCode?.parserServices || context.parserServices;
      if (!parserServices?.defineTemplateBodyVisitor) {
        return styleBlockCheck;
      }

      function getStaticAttrValue(el, attrName) {
        const attr = el.startTag.attributes.find(
          (a) =>
            !a.directive &&
            a.key.type === "VIdentifier" &&
            a.key.name.toLowerCase() === attrName.toLowerCase()
        );
        if (!attr) return null;
        return attr.value ? attr.value.value : "";
      }

      function getBoundAttr(el, argName) {
        return (
          el.startTag.attributes.find(
            (a) =>
              a.directive &&
              a.key.type === "VDirectiveKey" &&
              a.key.name?.name === "bind" &&
              a.key.argument?.type === "VIdentifier" &&
              a.key.argument.name.toLowerCase() === argName.toLowerCase()
          ) || null
        );
      }

      function collectClassesFromExpr(expr, out) {
        if (!expr) return;
        if (expr.type === "Literal" && typeof expr.value === "string") {
          for (const tok of expr.value.split(/\s+/).filter(Boolean)) {
            out.add(tok);
          }
        } else if (expr.type === "ArrayExpression") {
          for (const elem of expr.elements) {
            if (
              elem &&
              elem.type === "Literal" &&
              typeof elem.value === "string"
            ) {
              for (const tok of elem.value.split(/\s+/).filter(Boolean)) {
                out.add(tok);
              }
            } else if (elem && elem.type === "ObjectExpression") {
              collectClassesFromExpr(elem, out);
            }
          }
        } else if (expr.type === "ObjectExpression") {
          for (const prop of expr.properties) {
            if (
              prop.type === "Property" &&
              prop.value?.type === "Literal" &&
              prop.value.value === true
            ) {
              const keyName =
                prop.key.type === "Identifier"
                  ? prop.key.name
                  : prop.key.type === "Literal"
                    ? String(prop.key.value)
                    : "";
              for (const tok of keyName.split(/\s+/).filter(Boolean)) {
                out.add(tok);
              }
            }
          }
        }
      }

      function getElementClasses(el) {
        const out = new Set();
        const staticClass = getStaticAttrValue(el, "class");
        if (staticClass) {
          for (const tok of staticClass.split(/\s+/).filter(Boolean)) {
            out.add(tok);
          }
        }
        const boundClass = getBoundAttr(el, "class");
        if (boundClass?.value?.expression) {
          collectClassesFromExpr(boundClass.value.expression, out);
        }
        return out;
      }

      function getStyleAttrText(el) {
        let txt = "";
        const staticStyle = getStaticAttrValue(el, "style");
        if (staticStyle) txt += staticStyle + ";";
        const boundStyle = getBoundAttr(el, "style");
        if (boundStyle?.value) {
          txt += context.sourceCode.getText(boundStyle.value);
        }
        return txt;
      }

      function isExplicitlyDisabledTabular(el) {
        const staticData = getStaticAttrValue(el, "data-tabular-nums");
        if (staticData === "false") return true;
        const boundData = getBoundAttr(el, "data-tabular-nums");
        if (boundData?.value) {
          const raw = context.sourceCode.getText(boundData.value).trim();
          if (
            raw === '"false"' ||
            raw === "'false'" ||
            raw === `"'false'"` ||
            raw === `'"false"'` ||
            raw === '"false"'
          ) {
            return true;
          }
          if (
            boundData.value.expression?.type === "Literal" &&
            (boundData.value.expression.value === false ||
              boundData.value.expression.value === "false")
          ) {
            return true;
          }
        }
        const boundProp =
          getBoundAttr(el, "tabular-nums") || getBoundAttr(el, "tabularnums");
        if (
          boundProp?.value?.expression?.type === "Literal" &&
          boundProp.value.expression.value === false
        ) {
          return true;
        }
        const styleTxt = getStyleAttrText(el);
        if (/font-variant-numeric\s*:\s*normal\b/i.test(styleTxt)) return true;
        return false;
      }

      function hasDataTabularNums(el, isClinicalInput) {
        const staticData = getStaticAttrValue(el, "data-tabular-nums");
        if (staticData !== null && staticData !== "false") return true;
        const boundData = getBoundAttr(el, "data-tabular-nums");
        if (boundData && !isExplicitlyDisabledTabular(el)) return true;
        if (isClinicalInput) {
          const staticProp =
            getStaticAttrValue(el, "tabular-nums") ??
            getStaticAttrValue(el, "tabularnums");
          if (staticProp !== null && staticProp !== "false") return true;
          const boundProp =
            getBoundAttr(el, "tabular-nums") || getBoundAttr(el, "tabularnums");
          if (boundProp && !isExplicitlyDisabledTabular(el)) return true;
          const boundAttrs = getBoundAttr(el, "attributes");
          if (boundAttrs?.value) {
            const raw = context.sourceCode.getText(boundAttrs.value);
            if (
              /data-tabular-nums/.test(raw) &&
              !/data-tabular-nums['"]?\s*:\s*(?:false|['"]false['"])/.test(raw)
            ) {
              return true;
            }
          }
        }
        return false;
      }

      function hasInlineTabularStyle(el) {
        const txt = getStyleAttrText(el);
        return (
          /\btabular-nums\b/.test(txt) ||
          /--(?:semantic-)?font-variant-numeric-tabular\b/.test(txt)
        );
      }

      function hasAlignedCssClass(el, classes) {
        for (const c of classes) {
          if (alignedClasses.has(c)) return true;
        }
        for (const pair of scopedPairs) {
          if (classes.has(pair.className)) {
            let anc = el.parent;
            while (anc && anc.type === "VElement") {
              if (getElementClasses(anc).has(pair.ancestorClass)) return true;
              anc = anc.parent;
            }
          }
        }
        return false;
      }

      function isControlTabularAligned(el, tag, isClinicalInput) {
        if (isExplicitlyDisabledTabular(el)) return false;
        if (hasDataTabularNums(el, isClinicalInput)) return true;
        if (hasInlineTabularStyle(el)) return true;
        const classes = getElementClasses(el);
        if (hasAlignedCssClass(el, classes)) return true;

        if (tag === "input" || isClinicalInput) {
          const staticType = (
            getStaticAttrValue(el, "type") || ""
          ).toLowerCase();
          if (TABULAR_NATIVE_INPUT_TYPES.has(staticType)) return true;
          const boundType = getBoundAttr(el, "type");
          if (
            boundType?.value?.expression?.type === "Literal" &&
            TABULAR_NATIVE_INPUT_TYPES.has(
              String(boundType.value.expression.value || "").toLowerCase()
            )
          ) {
            return true;
          }
          const staticMode = (
            getStaticAttrValue(el, "inputmode") || ""
          ).toLowerCase();
          if (TABULAR_INPUT_MODES.has(staticMode)) return true;
        }

        let anc = el.parent;
        while (anc && anc.type === "VElement") {
          if (isExplicitlyDisabledTabular(anc)) return false;
          const ancTag = (anc.rawName || anc.name).toLowerCase();
          if (ancTag === "td" || ancTag === "th") return true;
          const ancClasses = getElementClasses(anc);
          if (
            ancClasses.has("tabular-nums") ||
            ancClasses.has("numeric") ||
            hasDataTabularNums(anc, false)
          ) {
            return true;
          }
          anc = anc.parent;
        }
        return false;
      }

      function isReadoutTabularAligned(el) {
        let cur = el;
        while (cur && cur.type === "VElement") {
          if (isExplicitlyDisabledTabular(cur)) return false;
          const curTag = (cur.rawName || cur.name).toLowerCase();
          if (
            curTag === "time" ||
            curTag === "table" ||
            curTag === "td" ||
            curTag === "th"
          ) {
            return true;
          }
          if (hasDataTabularNums(cur, false) || hasInlineTabularStyle(cur)) {
            return true;
          }
          const curClasses = getElementClasses(cur);
          if (hasAlignedCssClass(cur, curClasses)) return true;
          cur = cur.parent;
        }
        return false;
      }

      function getDirectExpressionText(el) {
        let out = "";
        for (const child of el.children || []) {
          if (child.type === "VExpressionContainer" && child.expression) {
            out += " " + context.sourceCode.getText(child.expression);
          }
        }
        return out;
      }

      function getDirectStaticProseLength(el) {
        let text = "";
        for (const child of el.children || []) {
          if (child.type === "VText") {
            text += child.value;
          }
        }
        return text.replace(/\s+/g, " ").trim().length;
      }

      return parserServices.defineTemplateBodyVisitor(
        {
          VAttribute(node) {
            if (isStyleOrColorVAttribute(node) && node.value) {
              const text = context.sourceCode.getText(node.value);
              if (
                /font-variant-numeric\s*:\s*normal\b|fontVariantNumeric\s*:\s*['"`]normal['"`]/i.test(
                  text
                )
              ) {
                context.report({
                  node: node.value,
                  messageId: "tabularOverrideForbidden",
                });
              }
            }
          },
          VElement(node) {
            const rawName = node.rawName || node.name;
            const tag = rawName.toLowerCase();
            if (
              tag === "textarea" ||
              tag === "svg" ||
              tag === "path" ||
              tag === "style" ||
              tag === "script"
            ) {
              return;
            }

            const isClinicalInput =
              tag === "clinicalinput" || tag === "clinical-input";
            if (tag === "input" || tag === "select" || isClinicalInput) {
              const staticType = (
                getStaticAttrValue(node, "type") || ""
              ).toLowerCase();
              if (NON_TEXT_INPUT_TYPES.has(staticType)) return;

              const hasVModelNumber = node.startTag.attributes.some(
                (a) =>
                  a.directive &&
                  a.key.type === "VDirectiveKey" &&
                  a.key.name?.name === "model" &&
                  a.key.modifiers?.some((m) => m.name === "number")
              );
              const hasNumericRangeConstraint = node.startTag.attributes.some(
                (a) => {
                  if (!a.directive && a.key.type === "VIdentifier") {
                    const n = a.key.name.toLowerCase();
                    return n === "step" || n === "min" || n === "max";
                  }
                  if (
                    a.directive &&
                    a.key.type === "VDirectiveKey" &&
                    a.key.name?.name === "bind" &&
                    a.key.argument?.type === "VIdentifier"
                  ) {
                    const n = a.key.argument.name.toLowerCase();
                    return n === "step" || n === "min" || n === "max";
                  }
                  return false;
                }
              );
              const staticPattern = getStaticAttrValue(node, "pattern") || "";
              const hasDigitPattern = /^\[0-9|\\d/.test(staticPattern);
              const isNativeTabularControl =
                TABULAR_NATIVE_INPUT_TYPES.has(staticType) ||
                TABULAR_INPUT_MODES.has(
                  (getStaticAttrValue(node, "inputmode") || "").toLowerCase()
                );

              if (
                (hasVModelNumber ||
                  hasNumericRangeConstraint ||
                  hasDigitPattern ||
                  isNativeTabularControl) &&
                !isControlTabularAligned(node, tag, isClinicalInput)
              ) {
                context.report({
                  node: node.startTag,
                  messageId: "unalignedNumericControl",
                  data: { name: rawName },
                });
              }
              return;
            }

            const classes = getElementClasses(node);
            const idVal = getStaticAttrValue(node, "id") || "";
            const testIdVal = getStaticAttrValue(node, "data-testid") || "";
            const exprText = getDirectExpressionText(node);

            const isCountdown =
              [...classes].some((c) => COUNTDOWN_CLASS_SET.has(c)) ||
              COUNTDOWN_ID_REGEX.test(idVal) ||
              COUNTDOWN_ID_REGEX.test(testIdVal) ||
              COUNTDOWN_EXPR_REGEX.test(exprText);

            if (isCountdown && !isReadoutTabularAligned(node)) {
              context.report({
                node: node.startTag,
                messageId: "unalignedCountdown",
                data: { name: rawName },
              });
              return;
            }

            const isCountBadge =
              [...classes].some((c) => COUNT_BADGE_CLASS_SET.has(c)) ||
              ([...classes].some((c) => BADGE_CLASS_TOKEN_REGEX.test(c)) &&
                BADGE_COUNT_EXPR_REGEX.test(exprText));

            if (isCountBadge && !isReadoutTabularAligned(node)) {
              context.report({
                node: node.startTag,
                messageId: "unalignedBadge",
                data: { name: rawName },
              });
              return;
            }

            const isKpi =
              [...classes].some((c) => KPI_VALUE_CLASS_SET.has(c)) ||
              KPI_ID_REGEX.test(idVal) ||
              KPI_ID_REGEX.test(testIdVal) ||
              (KPI_EXPR_REGEX.test(exprText) &&
                getDirectStaticProseLength(node) <= 45);

            if (isKpi && !isReadoutTabularAligned(node)) {
              context.report({
                node: node.startTag,
                messageId: "unalignedKpi",
                data: { name: rawName },
              });
            }
          },
        },
        styleBlockCheck
      );
    }

    // JS / TS UI helper & widget files
    const { alignedClasses } = getBaseTabularCssConfig(filename);
    const fileHasTabularConst =
      /--(?:semantic-)?font-variant-numeric-tabular\b/.test(sourceText) ||
      /font-variant-numeric\s*:\s*tabular-nums\b/.test(sourceText);

    function checkHtmlFragment(node, htmlText) {
      if (/font-variant-numeric\s*:\s*normal\b/i.test(htmlText)) {
        context.report({
          node,
          messageId: "tabularOverrideForbidden",
        });
      }
      const openTagRegex = /<([a-zA-Z][a-zA-Z0-9-]*)\b([^>]*)>/g;
      for (const m of htmlText.matchAll(openTagRegex)) {
        const tagName = m[1];
        const lowerTag = tagName.toLowerCase();
        const attrs = m[2];
        const classMatch = attrs.match(/\bclass\s*=\s*["']([^"']*)["']/i);
        const classTokens = classMatch
          ? classMatch[1].split(/\s+/).filter(Boolean)
          : [];
        const hasAlignedClass = classTokens.some((c) => alignedClasses.has(c));
        const hasDataAttr =
          /\bdata-tabular-nums\b/.test(attrs) &&
          !/\bdata-tabular-nums\s*=\s*["']false["']/i.test(attrs);
        const hasTabularInterpolation = /\$\{[^}]*tabular[^}]*\}/i.test(attrs);
        const hasInlineStyleTabular =
          /\btabular-nums\b|--(?:semantic-)?font-variant-numeric-tabular\b/.test(
            attrs
          ) ||
          (/\bstyle\s*=\s*["'][^"']*\$\{[^}]*BadgeStyle[^}]*\}/i.test(attrs) &&
            fileHasTabularConst);

        if (lowerTag === "input" || lowerTag === "select") {
          const explicitFalse = /\bdata-tabular-nums\s*=\s*["']false["']/i.test(
            attrs
          );
          const staticTypeMatch = attrs.match(
            /\btype\s*=\s*["']([a-zA-Z-]+)["']/i
          );
          const staticType = staticTypeMatch
            ? staticTypeMatch[1].toLowerCase()
            : "";
          if (NON_TEXT_INPUT_TYPES.has(staticType)) continue;
          const isDynamicInputType = /\btype\s*=\s*["']\$\{/.test(attrs);
          const hasRangeAttrs = /\b(?:step|min|max)\s*=/.test(attrs);
          const isNativeTabular =
            TABULAR_NATIVE_INPUT_TYPES.has(staticType) ||
            /\binputmode\s*=\s*["'](?:numeric|decimal)["']/i.test(attrs);

          const controlAligned =
            !explicitFalse &&
            (hasAlignedClass ||
              hasDataAttr ||
              hasTabularInterpolation ||
              hasInlineStyleTabular ||
              (isNativeTabular && !isDynamicInputType));

          if (
            (explicitFalse ||
              isDynamicInputType ||
              hasRangeAttrs ||
              (isNativeTabular && explicitFalse)) &&
            !controlAligned
          ) {
            context.report({
              node,
              messageId: "unalignedNumericControl",
              data: { name: tagName },
            });
          }
          continue;
        }

        const tagAligned =
          lowerTag === "time" ||
          hasAlignedClass ||
          hasDataAttr ||
          hasTabularInterpolation ||
          hasInlineStyleTabular;

        if (
          classTokens.some((c) => COUNTDOWN_CLASS_SET.has(c)) &&
          !tagAligned
        ) {
          context.report({
            node,
            messageId: "unalignedCountdown",
            data: { name: tagName },
          });
        } else if (
          classTokens.some((c) => COUNT_BADGE_CLASS_SET.has(c)) &&
          !tagAligned
        ) {
          context.report({
            node,
            messageId: "unalignedBadge",
            data: { name: tagName },
          });
        } else if (
          classTokens.some((c) => KPI_VALUE_CLASS_SET.has(c)) &&
          !tagAligned
        ) {
          context.report({
            node,
            messageId: "unalignedKpi",
            data: { name: tagName },
          });
        }
      }
    }

    return {
      TemplateLiteral(node) {
        const raw = context.sourceCode.getText(node);
        if (raw.includes("<")) {
          checkHtmlFragment(node, raw.slice(1, -1));
        }
      },
      Literal(node) {
        if (typeof node.value === "string" && node.value.includes("<")) {
          checkHtmlFragment(node, node.value);
        } else if (
          typeof node.value === "string" &&
          /font-variant-numeric\s*:\s*normal\b/i.test(node.value)
        ) {
          context.report({
            node,
            messageId: "tabularOverrideForbidden",
          });
        }
      },
      Property(node) {
        const keyName =
          node.key?.type === "Identifier"
            ? node.key.name
            : node.key?.type === "Literal"
              ? String(node.key.value)
              : "";
        if (
          (keyName === "fontVariantNumeric" ||
            keyName === "font-variant-numeric") &&
          node.value?.type === "Literal" &&
          typeof node.value.value === "string" &&
          /\bnormal\b/i.test(node.value.value)
        ) {
          context.report({
            node: node.value,
            messageId: "tabularOverrideForbidden",
          });
        }
      },
    };
  },
};

const ALLOWED_TOKENIZED_FONT_FAMILIES = new Set([
  "inherit",
  "var(--semantic-font-mono)",
  "var(--semantic-font-family)",
  "var(--font-sans)",
  "var(--font)",
  "var(--font-mono)",
]);

function detectSub12pxLength(rawValue) {
  const cleaned = rawValue.replace(/\s*!important\s*$/i, "").trim();
  for (const m of cleaned.matchAll(/([0-9]*\.?[0-9]+)\s*(px|rem|em|%)/gi)) {
    const num = parseFloat(m[1]);
    const unit = m[2].toLowerCase();
    if (
      (unit === "px" && num > 0 && num < 12) ||
      (unit === "rem" && num > 0 && num < 0.75) ||
      (unit === "em" && num > 0 && num < 0.75) ||
      (unit === "%" && num > 0 && num < 75)
    ) {
      return `${m[1]}${m[2]}`;
    }
  }
  return null;
}

function createTypographyScanVisitors(
  context,
  { onCssBlock, onVueStyleAttr, onJsProgram }
) {
  const filename = (context.filename || context.getFilename() || "")
    .split(path.sep)
    .join("/");
  const sourceText = context.sourceCode.getText();

  if (filename.endsWith(".css")) {
    return {
      Program() {
        onCssBlock(
          stripUrlFragmentsPreservingLength(
            stripCssCommentsPreservingNewlines(sourceText)
          ),
          0,
          sourceText,
          filename
        );
      },
    };
  }

  if (filename.endsWith(".vue")) {
    const styleBlockCheck = {
      "Program:exit"() {
        for (const styleMatch of sourceText.matchAll(
          /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
        )) {
          const contentStart = styleMatch.index + styleMatch[1].length;
          onCssBlock(
            stripUrlFragmentsPreservingLength(
              stripCssCommentsPreservingNewlines(styleMatch[2])
            ),
            contentStart,
            sourceText,
            filename
          );
        }
      },
    };

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return styleBlockCheck;
    }

    return parserServices.defineTemplateBodyVisitor(
      {
        VAttribute(node) {
          if (isStyleOrColorVAttribute(node) && node.value) {
            onVueStyleAttr(node.value, context.sourceCode.getText(node.value));
          }
        },
      },
      styleBlockCheck
    );
  }

  return {
    Program() {
      const cleanJs = stripCssCommentsPreservingNewlines(sourceText).replace(
        /\/\/.*$/gm,
        (c) => " ".repeat(c.length)
      );
      onJsProgram(cleanJs, sourceText);
    },
  };
}

const noSub12pxFontSizeRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow font-size declarations smaller than 12px (0.75rem) across Vue templates, Vue <style> blocks, CSS stylesheets, and JS/TS modules.",
      category: "Design System",
    },
    messages: {
      sub12pxFontSize:
        'Font size "{{size}}" is smaller than the 12px clinical floor. Use var(--font-size-2xs) (12px) or larger typography tokens from packages/ui/tokens.css.',
    },
  },
  create(context) {
    const cssFontSizeRe =
      /(?:font-size|--(?:semantic-)?font-size-[a-zA-Z0-9_-]+)\s*:\s*([^;}"'`]+)/gi;
    const jsFontSizeRe = /fontSize\s*:\s*(['"`])([^'"`]+)\1/g;

    return createTypographyScanVisitors(context, {
      onCssBlock(cleanCss, baseOffset, sourceText) {
        for (const match of cleanCss.matchAll(cssFontSizeRe)) {
          const sub12 = detectSub12pxLength(match[1]);
          if (sub12) {
            context.report({
              loc: offsetToLoc(sourceText, baseOffset + match.index),
              messageId: "sub12pxFontSize",
              data: { size: sub12 },
            });
          }
        }
      },
      onVueStyleAttr(valueNode, attrText) {
        for (const [re, idx] of [
          [cssFontSizeRe, 1],
          [jsFontSizeRe, 2],
        ]) {
          for (const match of attrText.matchAll(re)) {
            const sub12 = detectSub12pxLength(match[idx]);
            if (sub12) {
              context.report({
                node: valueNode,
                messageId: "sub12pxFontSize",
                data: { size: sub12 },
              });
            }
          }
        }
      },
      onJsProgram(cleanJs, sourceText) {
        for (const [re, idx] of [
          [cssFontSizeRe, 1],
          [jsFontSizeRe, 2],
        ]) {
          for (const match of cleanJs.matchAll(re)) {
            const sub12 = detectSub12pxLength(match[idx]);
            if (sub12) {
              context.report({
                loc: offsetToLoc(sourceText, match.index),
                messageId: "sub12pxFontSize",
                data: { size: sub12 },
              });
            }
          }
        }
      },
    });
  },
};

const noCustomFontStackRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow custom or raw font-family stacks outside packages/ui/tokens.css :root and disallow generic-first --font-mono stacks.",
      category: "Design System",
    },
    messages: {
      customFontStack:
        'Custom font stack "{{stack}}" is prohibited. Use var(--semantic-font-mono) or var(--semantic-font-family) from packages/ui/tokens.css.',
      genericFirstFontMono:
        '--font-mono in packages/ui/tokens.css must list platform monospace fonts (e.g. "JetBrains Mono", ui-monospace) before generic monospace.',
    },
  },
  create(context) {
    const cssFontFamilyRe = /font-family\s*:\s*([^;}]+)/gi;
    const inlineFontFamilyRe = /font-family\s*:\s*([^;}"'`]+)/gi;
    const jsFontFamilyRe = /fontFamily\s*:\s*(['"`])([^'"`]+)\1/g;

    function findDisallowedStack(rawStack) {
      const normalized = rawStack
        .replace(/\s*!important\s*$/i, "")
        .replace(/\s+/g, " ")
        .trim();
      if (!normalized || ALLOWED_TOKENIZED_FONT_FAMILIES.has(normalized)) {
        return null;
      }
      return normalized;
    }

    return createTypographyScanVisitors(context, {
      onCssBlock(cleanCss, baseOffset, sourceText, filename) {
        let cssToScan = cleanCss;
        if (filename.endsWith("/packages/ui/tokens.css")) {
          for (const monoMatch of cleanCss.matchAll(
            /--font-mono\s*:\s*([^;}]+)/gi
          )) {
            const parts = monoMatch[1]
              .replace(/\s+/g, " ")
              .trim()
              .split(",")
              .map((s) =>
                s
                  .trim()
                  .replace(/^['"]|['"]$/g, "")
                  .toLowerCase()
              );
            const genericIdx = parts.indexOf("monospace");
            if (genericIdx === 0 || (genericIdx !== -1 && parts.length === 1)) {
              context.report({
                loc: offsetToLoc(sourceText, monoMatch.index),
                messageId: "genericFirstFontMono",
              });
            }
          }
          cssToScan = cleanCss.replace(
            /@font-face\s*\{[\s\S]*?\}/gi,
            (block) => {
              const famMatch = block.match(/font-family\s*:\s*([^;}]+)/i);
              const fam = famMatch
                ? famMatch[1].trim().replace(/^['"]|['"]$/g, "")
                : "";
              if (fam === "Inter" || fam === "JetBrains Mono") {
                return block.replace(/[^\n]/g, " ");
              }
              return block;
            }
          );
        }
        for (const match of cssToScan.matchAll(cssFontFamilyRe)) {
          const bad = findDisallowedStack(match[1]);
          if (bad) {
            context.report({
              loc: offsetToLoc(sourceText, baseOffset + match.index),
              messageId: "customFontStack",
              data: { stack: bad },
            });
          }
        }
      },
      onVueStyleAttr(valueNode, rawAttr) {
        const inner =
          rawAttr.length >= 2 &&
          (rawAttr.startsWith('"') || rawAttr.startsWith("'"))
            ? rawAttr.slice(1, -1)
            : rawAttr;
        for (const [re, idx] of [
          [/font-family\s*:\s*([^;]+)/gi, 1],
          [jsFontFamilyRe, 2],
        ]) {
          for (const match of inner.matchAll(re)) {
            const bad = findDisallowedStack(match[idx]);
            if (bad) {
              context.report({
                node: valueNode,
                messageId: "customFontStack",
                data: { stack: bad },
              });
            }
          }
        }
      },
      onJsProgram(cleanJs, sourceText) {
        for (const [re, idx] of [
          [inlineFontFamilyRe, 1],
          [jsFontFamilyRe, 2],
        ]) {
          for (const match of cleanJs.matchAll(re)) {
            const bad = findDisallowedStack(match[idx]);
            if (bad) {
              context.report({
                loc: offsetToLoc(sourceText, match.index),
                messageId: "customFontStack",
                data: { stack: bad },
              });
            }
          }
        }
      },
    });
  },
};

const ALLOWED_FONT_SIZE_TOKEN_RE =
  /^var\(\s*--(?:semantic-)?font-size-(?:2xs|xs|sm|base|md|lg|xl|2xl|3xl|4xl|5xl)\s*\)$/;
const ALLOWED_MAX_1REM_RE =
  /^max\(\s*1rem\s*,\s*var\(\s*--(?:semantic-)?font-size-md\s*\)\s*\)$/i;
const SUB_1REM_FONT_TOKEN_RE =
  /^var\(\s*--(?:semantic-)?font-size-(?:2xs|xs|sm|base)(?:\s*,[^)]+)?\s*\)$/i;

function isAllowedTokenizedFontSize(rawVal) {
  const val = rawVal.replace(/\s*!important\s*$/i, "").trim();
  if (!val || val === "inherit" || val === "0") return true;
  if (ALLOWED_FONT_SIZE_TOKEN_RE.test(val)) return true;
  if (ALLOWED_MAX_1REM_RE.test(val)) return true;
  return false;
}

function detectSub1RemFontSize(rawVal) {
  const val = rawVal.replace(/\s*!important\s*$/i, "").trim();
  if (!val || val === "inherit" || ALLOWED_MAX_1REM_RE.test(val)) return null;
  if (SUB_1REM_FONT_TOKEN_RE.test(val)) return val;
  for (const m of val.matchAll(/([0-9]*\.?[0-9]+)\s*(px|rem|em|%)/gi)) {
    const num = parseFloat(m[1]);
    const unit = m[2].toLowerCase();
    if (
      (unit === "px" && num > 0 && num < 16) ||
      (unit === "rem" && num > 0 && num < 1) ||
      (unit === "em" && num > 0 && num < 1) ||
      (unit === "%" && num > 0 && num < 100)
    ) {
      return `${m[1]}${m[2]}`;
    }
  }
  return null;
}

function isTouchControlSelectorSubject(
  selectorPart,
  templateControlClasses,
  templateControlIds
) {
  let p = selectorPart.trim();
  if (!p || p.startsWith("@")) return false;
  if (
    /\[type\s*=\s*["']?(?:checkbox|radio|hidden|file)["']?\]/i.test(p) &&
    !/not\(\s*\[type/i.test(p)
  ) {
    return false;
  }
  p = p.replace(/:deep\(([^)]+)\)/g, "$1").trim();
  const segments = p.split(/\s+|(?<!\\)[>+~]\s*/).filter(Boolean);
  const subject = segments[segments.length - 1] || "";
  if (!subject || /::(before|after)/i.test(subject)) return false;
  const cleanSubject = subject.replace(/:+[a-zA-Z0-9_-]+(\([^)]*\))?/g, "");
  if (/^(?:input|select|textarea)(?:[.#[].*)?$/i.test(cleanSubject)) {
    return true;
  }
  for (const cls of cleanSubject.matchAll(/\.([a-zA-Z0-9_-]+)/g)) {
    const name = cls[1];
    if (templateControlClasses?.has(name)) return true;
    if (
      /(?:^|-)(?:input|select|textarea)(?:-(?:sm|md|lg|text|control|field))?$/i.test(
        name
      ) &&
      !/(?:group|wrapper|row|label|hint|error|header|btn|button|icon|container|panel|section|list|bar|badge|pill)$/i.test(
        name
      )
    ) {
      return true;
    }
    if (
      /^(?:form-control|form-select|form-control-sm|form-select-sm|select-control|text-control|textarea-control|input-sm|select-sm|input-text|input-text-gxp)$/i.test(
        name
      )
    ) {
      return true;
    }
  }
  for (const idMatch of cleanSubject.matchAll(/#([a-zA-Z0-9_-]+)/g)) {
    if (templateControlIds?.has(idMatch[1])) return true;
  }
  return false;
}

function splitTopLevelSelectors(selectorText) {
  const parts = [];
  let depth = 0;
  let cur = "";
  for (const ch of selectorText) {
    if (ch === "(") depth++;
    else if (ch === ")" && depth > 0) depth--;
    if (ch === "," && depth === 0) {
      parts.push(cur);
      cur = "";
    } else {
      cur += ch;
    }
  }
  if (cur) parts.push(cur);
  return parts;
}

const noUntokenizedFontSizeRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow untokenized font-size literals across Vue, CSS, and JS/TS files and enforce rem-based --font-size-* scale tokens in packages/ui/tokens.css.",
      category: "Design System",
    },
    messages: {
      untokenizedFontSize:
        'Untokenized font-size "{{size}}" is prohibited. Use shared rem typography tokens (var(--font-size-2xs) .. var(--font-size-5xl)) from packages/ui/tokens.css.',
      nonRemScaleToken:
        'Typography token "{{prop}}: {{size}}" in packages/ui/tokens.css must use rem units (>= 0.75rem), not px.',
    },
  },
  create(context) {
    const cssFontSizeRe = /(?:^|[;{\s])font-size\s*:\s*([^;}"'`\n]+)/gi;
    const inlineFontSizeRe = /(?:^|[;\s])font-size\s*:\s*([^;}"'`\n]+)/gi;
    const jsFontSizeRe = /fontSize\s*:\s*(['"`])([^'"`]+)\1/g;

    return createTypographyScanVisitors(context, {
      onCssBlock(cleanCss, baseOffset, sourceText, filename) {
        if (filename.endsWith("/packages/ui/tokens.css")) {
          for (const tokMatch of cleanCss.matchAll(
            /(--font-size-[a-zA-Z0-9_-]+)\s*:\s*([^;}\n]+)/gi
          )) {
            const prop = tokMatch[1];
            const val = tokMatch[2].trim();
            if (!/^[0-9]*\.?[0-9]+rem$/i.test(val)) {
              context.report({
                loc: offsetToLoc(sourceText, baseOffset + tokMatch.index),
                messageId: "nonRemScaleToken",
                data: { prop, size: val },
              });
            }
          }
        }
        for (const match of cleanCss.matchAll(cssFontSizeRe)) {
          const rawVal = match[1].trim();
          if (!isAllowedTokenizedFontSize(rawVal)) {
            context.report({
              loc: offsetToLoc(sourceText, baseOffset + match.index),
              messageId: "untokenizedFontSize",
              data: { size: rawVal },
            });
          }
        }
      },
      onVueStyleAttr(valueNode, attrText) {
        const inner =
          attrText.length >= 2 &&
          (attrText.startsWith('"') || attrText.startsWith("'"))
            ? attrText.slice(1, -1)
            : attrText;
        for (const [re, idx] of [
          [inlineFontSizeRe, 1],
          [jsFontSizeRe, 2],
        ]) {
          for (const match of inner.matchAll(re)) {
            const rawVal = match[idx].trim();
            if (!isAllowedTokenizedFontSize(rawVal)) {
              context.report({
                node: valueNode,
                messageId: "untokenizedFontSize",
                data: { size: rawVal },
              });
            }
          }
        }
      },
      onJsProgram(cleanJs, sourceText) {
        for (const [re, idx] of [
          [inlineFontSizeRe, 1],
          [jsFontSizeRe, 2],
        ]) {
          for (const match of cleanJs.matchAll(re)) {
            const rawVal = match[idx].trim();
            if (/\$\{/.test(rawVal)) continue;
            if (!isAllowedTokenizedFontSize(rawVal)) {
              context.report({
                loc: offsetToLoc(sourceText, match.index),
                messageId: "untokenizedFontSize",
                data: { size: rawVal },
              });
            }
          }
        }
      },
    });
  },
};

const noSub1RemTouchControlRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow sub-1rem (< 16px) font-size declarations on input, select, and textarea controls outside @media (pointer: fine).",
      category: "Accessibility",
    },
    messages: {
      sub1RemTouchControl:
        'Touch control font-size "{{size}}" resolves below 1rem (16px) and triggers mobile viewport auto-zoom. Use var(--font-size-md) (1rem) or larger, or scope compact desktop overrides inside @media (pointer: fine).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    const sourceText = context.sourceCode.getText();
    const templateControlClasses = new Set();
    const templateControlIds = new Set();

    function scanCssRules(cleanCss, baseOffset) {
      let i = 0;
      const mediaStack = [];
      while (i < cleanCss.length) {
        if (/\s/.test(cleanCss[i])) {
          i++;
          continue;
        }
        if (cleanCss[i] === "}") {
          if (mediaStack.length > 0) mediaStack.pop();
          i++;
          continue;
        }
        let headerStart = i;
        let inQuote = null;
        let parenDepth = 0;
        while (i < cleanCss.length) {
          const ch = cleanCss[i];
          if (inQuote) {
            if (ch === "\\") i += 2;
            else {
              if (ch === inQuote) inQuote = null;
              i++;
            }
            continue;
          }
          if (ch === '"' || ch === "'") {
            inQuote = ch;
            i++;
            continue;
          }
          if (ch === "(") parenDepth++;
          else if (ch === ")" && parenDepth > 0) parenDepth--;
          else if (parenDepth === 0 && (ch === "{" || ch === ";" || ch === "}"))
            break;
          i++;
        }
        const header = cleanCss.slice(headerStart, i).trim();
        if (i >= cleanCss.length) break;
        if (cleanCss[i] === ";") {
          i++;
          continue;
        }
        if (cleanCss[i] === "}") continue;
        if (/^@(?:media|supports|layer|container)\b/i.test(header)) {
          mediaStack.push(header);
          i++;
          continue;
        }
        let bodyStart = i + 1;
        let braceDepth = 1;
        i++;
        inQuote = null;
        while (i < cleanCss.length && braceDepth > 0) {
          const ch = cleanCss[i];
          if (inQuote) {
            if (ch === "\\") i += 2;
            else {
              if (ch === inQuote) inQuote = null;
              i++;
            }
            continue;
          }
          if (ch === '"' || ch === "'") {
            inQuote = ch;
            i++;
            continue;
          }
          if (ch === "{") braceDepth++;
          else if (ch === "}") braceDepth--;
          i++;
        }
        const body = cleanCss.slice(bodyStart, i - 1);
        const inPointerFine = mediaStack.some((m) =>
          /\(\s*pointer\s*:\s*fine\s*\)/i.test(m)
        );
        if (inPointerFine || header.startsWith("@")) continue;
        const targetsControl = splitTopLevelSelectors(header).some((part) =>
          isTouchControlSelectorSubject(
            part,
            templateControlClasses,
            templateControlIds
          )
        );
        if (!targetsControl) continue;

        for (const fsMatch of body.matchAll(
          /(?:^|[;{\s])font-size\s*:\s*([^;}\n]+)/gi
        )) {
          const badSize = detectSub1RemFontSize(fsMatch[1]);
          if (badSize) {
            context.report({
              loc: offsetToLoc(
                sourceText,
                baseOffset + bodyStart + fsMatch.index
              ),
              messageId: "sub1RemTouchControl",
              data: { size: badSize },
            });
          }
        }
      }
    }

    if (filename.endsWith(".css")) {
      return {
        Program() {
          scanCssRules(
            stripUrlFragmentsPreservingLength(
              stripCssCommentsPreservingNewlines(sourceText)
            ),
            0
          );
        },
      };
    }

    if (filename.endsWith(".vue")) {
      const styleExit = {
        "Program:exit"() {
          for (const styleMatch of sourceText.matchAll(
            /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
          )) {
            const contentStart = styleMatch.index + styleMatch[1].length;
            scanCssRules(
              stripUrlFragmentsPreservingLength(
                stripCssCommentsPreservingNewlines(styleMatch[2])
              ),
              contentStart
            );
          }
        },
      };
      const parserServices =
        context.sourceCode?.parserServices || context.parserServices;
      if (!parserServices?.defineTemplateBodyVisitor) return styleExit;

      return parserServices.defineTemplateBodyVisitor(
        {
          VElement(node) {
            const tag = node.rawName?.toLowerCase() || node.name?.toLowerCase();
            if (tag !== "input" && tag !== "select" && tag !== "textarea") {
              return;
            }
            const attrs = node.startTag?.attributes || [];
            if (tag === "input") {
              const typeAttr = attrs.find(
                (a) => !a.directive && a.key?.name === "type"
              );
              const t = typeAttr?.value?.value?.toLowerCase();
              if (
                t === "checkbox" ||
                t === "radio" ||
                t === "hidden" ||
                t === "file"
              ) {
                return;
              }
            }
            for (const a of attrs) {
              if (!a.directive && a.key?.name === "class" && a.value?.value) {
                for (const c of a.value.value.split(/\s+/).filter(Boolean)) {
                  templateControlClasses.add(c);
                }
              }
              if (!a.directive && a.key?.name === "id" && a.value?.value) {
                templateControlIds.add(a.value.value.trim());
              }
              if (isStyleOrColorVAttribute(a) && a.value) {
                const rawAttr = context.sourceCode.getText(a.value);
                const inner =
                  rawAttr.length >= 2 &&
                  (rawAttr.startsWith('"') || rawAttr.startsWith("'"))
                    ? rawAttr.slice(1, -1)
                    : rawAttr;
                for (const [re, idx] of [
                  [/(?:^|[;\s])font-size\s*:\s*([^;}"'`\n]+)/gi, 1],
                  [/fontSize\s*:\s*(['"`])([^'"`]+)\1/g, 2],
                ]) {
                  for (const m of inner.matchAll(re)) {
                    const badSize = detectSub1RemFontSize(m[idx]);
                    if (badSize) {
                      context.report({
                        node: a.value,
                        messageId: "sub1RemTouchControl",
                        data: { size: badSize },
                      });
                    }
                  }
                }
              }
            }
          },
        },
        styleExit
      );
    }

    return {
      Program() {
        const cleanJs = stripCssCommentsPreservingNewlines(sourceText).replace(
          /\/\/.*$/gm,
          (c) => " ".repeat(c.length)
        );
        for (const tagMatch of cleanJs.matchAll(
          /<(input|select|textarea)\b([^>]*?)>/gi
        )) {
          const tag = tagMatch[1].toLowerCase();
          const attrs = tagMatch[2];
          if (
            tag === "input" &&
            /\btype\s*=\s*(?:"(?:checkbox|radio|hidden|file)"|'(?:checkbox|radio|hidden|file)')/i.test(
              attrs
            )
          ) {
            continue;
          }
          for (const stMatch of attrs.matchAll(
            /\bstyle\s*=\s*(["'])([\s\S]*?)\1/gi
          )) {
            for (const fsMatch of stMatch[2].matchAll(
              /(?:^|[;\s])font-size\s*:\s*([^;]+)/gi
            )) {
              const badSize = detectSub1RemFontSize(fsMatch[1]);
              if (badSize) {
                context.report({
                  loc: offsetToLoc(sourceText, tagMatch.index),
                  messageId: "sub1RemTouchControl",
                  data: { size: badSize },
                });
              }
            }
          }
        }
      },
    };
  },
};

const noVuejsA11ySuppressionRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow eslint-disable directives targeting vuejs-accessibility rules across shared UI primitives and live operational hub files.",
      category: "Accessibility",
    },
    messages: {
      noVuejsA11ySuppression:
        "Suppressing vuejs-accessibility rules via eslint-disable is prohibited in shared UI primitives and live clinical hubs. Remediate the underlying semantic HTML, keyboard, or ARIA label defect instead.",
    },
  },
  create(context) {
    function inspectComments(programNode) {
      const seen = new Set();
      const comments = [
        ...(context.sourceCode?.getAllComments?.() || []),
        ...(programNode?.templateBody?.comments || []),
      ];
      for (const comment of comments) {
        const key = comment.range
          ? `${comment.range[0]}:${comment.range[1]}`
          : `${comment.loc?.start.line}:${comment.loc?.start.column}`;
        if (seen.has(key)) continue;
        seen.add(key);

        const text = comment.value || "";
        if (
          /\beslint-disable(?:-next-line|-line)?\b/.test(text) &&
          /\bvuejs-accessibility(?:\/|\b)/.test(text)
        ) {
          context.report({
            loc: comment.loc,
            messageId: "noVuejsA11ySuppression",
          });
        }
      }
    }

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (parserServices?.defineTemplateBodyVisitor) {
      return parserServices.defineTemplateBodyVisitor(
        {},
        {
          "Program:exit"(programNode) {
            inspectComments(programNode);
          },
        }
      );
    }

    return {
      "Program:exit"(programNode) {
        inspectComments(programNode);
      },
    };
  },
};

const CLINICAL_TABS_CLASS_PROP_MAP = new Map([
  ["list-class", "list-class"],
  ["listClass", "list-class"],
  ["tab-class", "tab-class"],
  ["tabClass", "tab-class"],
  ["active-tab-class", "active-tab-class"],
  ["activeTabClass", "active-tab-class"],
]);

function extractClassNamesFromJsExpr(expr, out) {
  if (!expr) return;
  if (expr.type === "Literal" && typeof expr.value === "string") {
    for (const token of expr.value.split(/\s+/)) {
      if (token) out.add(token);
    }
    return;
  }
  if (expr.type === "TemplateLiteral" && expr.expressions.length === 0) {
    const raw = expr.quasis.map((q) => q.value.cooked ?? "").join("");
    for (const token of raw.split(/\s+/)) {
      if (token) out.add(token);
    }
    return;
  }
  if (expr.type === "ArrayExpression") {
    for (const elem of expr.elements || []) {
      extractClassNamesFromJsExpr(elem, out);
    }
    return;
  }
  if (expr.type === "ObjectExpression") {
    for (const prop of expr.properties || []) {
      if (prop.type !== "Property" || prop.computed) continue;
      if (prop.key?.type === "Identifier" && prop.key.name) {
        out.add(prop.key.name);
      } else if (
        prop.key?.type === "Literal" &&
        typeof prop.key.value === "string"
      ) {
        for (const token of prop.key.value.split(/\s+/)) {
          if (token) out.add(token);
        }
      }
    }
    return;
  }
  if (expr.type === "ConditionalExpression") {
    extractClassNamesFromJsExpr(expr.consequent, out);
    extractClassNamesFromJsExpr(expr.alternate, out);
    return;
  }
  if (expr.type === "LogicalExpression") {
    extractClassNamesFromJsExpr(expr.left, out);
    extractClassNamesFromJsExpr(expr.right, out);
  }
}

function splitTopLevelCommasWithOffsets(selectorText, baseOffset) {
  const parts = [];
  let depth = 0;
  let start = 0;
  for (let i = 0; i < selectorText.length; i++) {
    const ch = selectorText[i];
    if (ch === "(" || ch === "[") depth++;
    else if (ch === ")" || ch === "]") depth = Math.max(0, depth - 1);
    else if (ch === "," && depth === 0) {
      const rawPiece = selectorText.slice(start, i);
      const trimmed = rawPiece.trim();
      if (trimmed) {
        const lead = rawPiece.length - rawPiece.trimStart().length;
        parts.push({ selector: trimmed, offset: baseOffset + start + lead });
      }
      start = i + 1;
    }
  }
  const rawTail = selectorText.slice(start);
  const trimmedTail = rawTail.trim();
  if (trimmedTail) {
    const lead = rawTail.length - rawTail.trimStart().length;
    parts.push({ selector: trimmedTail, offset: baseOffset + start + lead });
  }
  return parts;
}

function maskVueScopedEscapeSpans(selector) {
  const chars = selector.split("");
  const escapePattern = /(?::deep|::v-deep|:global)\s*\(/gi;
  let match;
  while ((match = escapePattern.exec(selector)) !== null) {
    const startIdx = match.index;
    const openParenIdx = startIdx + match[0].length - 1;
    let depth = 1;
    let idx = openParenIdx + 1;
    while (idx < selector.length && depth > 0) {
      if (selector[idx] === "(") depth++;
      else if (selector[idx] === ")") depth--;
      idx++;
    }
    for (let k = startIdx; k < idx; k++) {
      if (chars[k] !== "\n" && chars[k] !== "\r") {
        chars[k] = " ";
      }
    }
  }
  return chars.join("");
}

function escapeRegExpChars(str) {
  return str.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function findUnscopedClinicalTabsSelectorsInCss(cssText, classPropMap) {
  const clean = maskCssStringsPreservingLength(
    stripCssCommentsPreservingNewlines(cssText)
  );
  const violations = [];
  const stack = [];
  let segStart = 0;

  for (let i = 0; i < clean.length; i++) {
    const ch = clean[i];
    if (ch === ";") {
      if (stack.length === 0 || stack[stack.length - 1].isAtRule) {
        segStart = i + 1;
      }
    } else if (ch === "{") {
      const rawHeader = clean.slice(segStart, i);
      const trimmedHeader = rawHeader.trim();
      const inKeyframes = stack.some((item) => item.isKeyframes);
      if (trimmedHeader.startsWith("@")) {
        stack.push({
          isAtRule: true,
          isKeyframes: /^@(?:-[a-z]+-)?keyframes\b/i.test(trimmedHeader),
        });
      } else {
        stack.push({ isAtRule: false, isKeyframes: inKeyframes });
        if (!inKeyframes && trimmedHeader) {
          const selectors = splitTopLevelCommasWithOffsets(rawHeader, segStart);
          for (const { selector, offset } of selectors) {
            const masked = maskVueScopedEscapeSpans(selector);
            for (const [className, propName] of classPropMap.entries()) {
              const clsRegex = new RegExp(
                `(?:^|[^a-zA-Z0-9_-])(\\.${escapeRegExpChars(className)})(?![a-zA-Z0-9_-])`
              );
              const m = clsRegex.exec(masked);
              if (m) {
                const dotOffset = offset + m.index + m[0].length - m[1].length;
                violations.push({
                  offset: dotOffset,
                  selector,
                  className,
                  propName,
                });
              }
            }
          }
        }
      }
      segStart = i + 1;
    } else if (ch === "}") {
      stack.pop();
      segStart = i + 1;
    }
  }

  return violations;
}

const noUnscopedClinicalTabsCssRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow <style scoped> selectors targeting ClinicalTabs class props (list-class, tab-class, active-tab-class) without :deep(...), as Vue 3 drops parent scoped selectors on multi-root child components.",
      category: "Possible Errors",
    },
    messages: {
      unscopedClinicalTabsCss:
        'Scoped selector "{{selector}}" targets ClinicalTabs {{propName}} ".{{className}}" without :deep(...). Vue 3 drops parent <style scoped> rules on multi-root child components; prefer shared ClinicalTabs variant/overflow props or wrap child overrides in :deep(.{{className}}).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    if (!filename.endsWith(".vue")) {
      return {};
    }

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return {};
    }

    const collectedClassProps = new Map();
    const sourceText = context.sourceCode.getText();

    return parserServices.defineTemplateBodyVisitor(
      {
        VElement(node) {
          const rawTag = node.rawName || node.name || "";
          if (rawTag !== "ClinicalTabs" && rawTag !== "clinical-tabs") {
            return;
          }
          for (const attr of node.startTag?.attributes || []) {
            if (!attr.directive && attr.key?.type === "VIdentifier") {
              const canonicalProp = CLINICAL_TABS_CLASS_PROP_MAP.get(
                attr.key.name
              );
              if (canonicalProp && attr.value?.value) {
                for (const token of attr.value.value.split(/\s+/)) {
                  if (token && !collectedClassProps.has(token)) {
                    collectedClassProps.set(token, canonicalProp);
                  }
                }
              }
            } else if (
              attr.directive &&
              attr.key?.type === "VDirectiveKey" &&
              attr.key.name?.name === "bind" &&
              attr.key.argument?.type === "VIdentifier"
            ) {
              const canonicalProp = CLINICAL_TABS_CLASS_PROP_MAP.get(
                attr.key.argument.name
              );
              if (canonicalProp && attr.value?.expression) {
                const tokens = new Set();
                extractClassNamesFromJsExpr(attr.value.expression, tokens);
                for (const token of tokens) {
                  if (token && !collectedClassProps.has(token)) {
                    collectedClassProps.set(token, canonicalProp);
                  }
                }
              }
            }
          }
        },
      },
      {
        "Program:exit"() {
          if (collectedClassProps.size === 0) return;
          for (const styleMatch of sourceText.matchAll(
            /(<style\b([^>]*)>)([\s\S]*?)<\/style>/gi
          )) {
            const styleAttrs = styleMatch[2] || "";
            if (!/\bscoped\b/i.test(styleAttrs)) continue;
            const contentStart = styleMatch.index + styleMatch[1].length;
            const violations = findUnscopedClinicalTabsSelectorsInCss(
              styleMatch[3],
              collectedClassProps
            );
            for (const v of violations) {
              context.report({
                loc: offsetToLoc(sourceText, contentStart + v.offset),
                messageId: "unscopedClinicalTabsCss",
                data: {
                  selector: v.selector,
                  className: v.className,
                  propName: v.propName,
                },
              });
            }
          }
        },
      }
    );
  },
};

// Exempt 2D multi-header clinical matrices and inline cell grids from ClinicalDataTable replacement (ADR-2673).
const EXEMPT_2D_CLINICAL_MATRICES = new Set([
  "components/canvas/ParticipantFormsPanel.vue",
  "components/clinical/ClinicalSoAMatrix.vue",
  "components/clinical/SoAMatrixEditor.vue",
  "components/crf/widgets/RepeatingLogGridWidget.vue",
  "components/etmf/DocumentGrid.vue",
  "components/imaging/OncologyCalipers.vue",
  "components/quality/RactMatrixViewer.vue",
]);

// Shrink-only legacy allowlist of unmigrated apps/web table views. Do not add new entries;
// migrate clinical dataset tables to ClinicalDataTable from "ui" (ADR-2656 / ADR-2673).
const LEGACY_WEB_RAW_TABLE_ALLOWLIST = new Set([
  "components/CodingQueueTable.vue",
  "components/SynonymImportDrawer.vue",
  "components/auditor/AuditTrailViewer.vue",
  "components/clinical/AdaptiveDoseEscalationConsole.vue",
  "components/clinical/AggregateSafetyConsole.vue",
  "components/clinical/CecAdjudicationConsole.vue",
  "components/clinical/CellGeneTherapyCocConsole.vue",
  "components/clinical/ClinicalLakehouseConsole.vue",
  "components/clinical/DeviceInvestigationConsole.vue",
  "components/clinical/IECriteriaTable.vue",
  "components/clinical/LabGovernanceConsole.vue",
  "components/clinical/ProtocolTrainingConsole.vue",
  "components/clinical/SensorTelemetryConsole.vue",
  "components/clinical/SsuGreenlightConsole.vue",
  "components/clinical/StatisticalComputingConsole.vue",
  "components/clinical/TelehealthDtpConsole.vue",
  "components/clinical/TherapeuticAreaWizard.vue",
  "components/crf/CanvasFieldWidget.vue",
  "components/econsent/IcfSectionEditor.vue",
  "components/ecrf/EcrfItemTable.vue",
  "components/ecrf/EcrfSignaturesSection.vue",
  "components/imaging/DualReaderAdjudicationPanel.vue",
  "components/persona/CraVerificationConsole.vue",
  "components/quality/StatisticalForensicsViewer.vue",
  "views/AmendmentDiffView.vue",
  "views/AuditView.vue",
  "views/ImagingView.vue",
  "views/KnowledgeView.vue",
  "views/LaySummaryPortal.vue",
  "views/ObservationView.vue",
  "views/ParticipantWalletView.vue",
  "views/QualityCockpitView.vue",
  "views/RulesView.vue",
  "views/demo/CtmsDemoWorkspace.vue",
]);

const noRawClinicalTableRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow raw <table> markup in migrated clinical hubs and apps/subject-portal; require ClinicalDataTable from packages/ui unless exempted as a 2D clinical matrix (ADR-2673).",
      category: "Accessibility",
    },
    messages: {
      noRawClinicalTable:
        'Raw <table> markup is prohibited outside exempted 2D clinical matrices. Use ClinicalDataTable from "ui" to preserve keyboard sorting, focusable row activation, sr-only captions, and polite pagination status (ADR-2656 / ADR-2673).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    if (
      !filename.endsWith(".vue") ||
      filename.includes("/packages/ui/src/components/")
    ) {
      return {};
    }

    const webSrcMarker = "/apps/web/src/";
    const webSrcIdx = filename.lastIndexOf(webSrcMarker);
    const relWebSrcPath =
      webSrcIdx >= 0 ? filename.slice(webSrcIdx + webSrcMarker.length) : null;

    if (
      relWebSrcPath !== null &&
      (EXEMPT_2D_CLINICAL_MATRICES.has(relWebSrcPath) ||
        LEGACY_WEB_RAW_TABLE_ALLOWLIST.has(relWebSrcPath))
    ) {
      return {};
    }

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return {};
    }

    return parserServices.defineTemplateBodyVisitor({
      VElement(node) {
        const rawTag = node.rawName || node.name || "";
        if (rawTag === "table") {
          context.report({
            node: node.startTag,
            messageId: "noRawClinicalTable",
          });
        }
      },
    });
  },
};

// Shrink-only legacy allowlist of unmigrated apps/web Vue files with untyped <button> elements
// or unguarded button :hover selectors. Do not add new entries (ADR-2673).
const LEGACY_WEB_UNGUARDED_BUTTON_ALLOWLIST = new Set([
  "components/AppShell.vue",
  "components/CodingQueueTable.vue",
  "components/ContextualHelpDrawer.vue",
  "components/OnboardingChecklistDrawer.vue",
  "components/auditor/AuditorExportModal.vue",
  "components/clinical/AdaptiveDoseEscalationConsole.vue",
  "components/clinical/AggregateSafetyConsole.vue",
  "components/clinical/CdiscPipelineConsole.vue",
  "components/clinical/CecAdjudicationConsole.vue",
  "components/clinical/ClinicalLakehouseConsole.vue",
  "components/clinical/DeviceInvestigationConsole.vue",
  "components/clinical/EhrReconciliationDrawer.vue",
  "components/clinical/LabGovernanceConsole.vue",
  "components/clinical/ProtocolTrainingConsole.vue",
  "components/clinical/SensorTelemetryConsole.vue",
  "components/clinical/SsuGreenlightConsole.vue",
  "components/clinical/StatisticalComputingConsole.vue",
  "components/clinical/TelehealthDtpConsole.vue",
  "components/clinical/TherapeuticAreaWizard.vue",
  "components/crf/CanvasFieldWidget.vue",
  "components/crf/CrfAuthoringCanvas.vue",
  "components/crf/FormSectionContainer.vue",
  "components/crf/RuleTracerPanel.vue",
  "components/econsent/ComprehensionQuizBuilder.vue",
  "components/econsent/IcfSectionEditor.vue",
  "components/econsent/QuizTemplateDrawer.vue",
  "components/econsent/ReadabilityDrawer.vue",
  "components/fileshare/ActivityFeed.vue",
  "components/fileshare/MediaPreviewModal.vue",
  "components/fileshare/ShareDrawer.vue",
  "components/imaging/BatchDicomDropzoneModal.vue",
  "components/imaging/DicomMprViewer.vue",
  "components/imaging/DualReaderAdjudicationPanel.vue",
  "components/imaging/OncologyCalipers.vue",
  "components/persona/CrcFormRenderer.vue",
  "components/persona/DesignerSchemaPanel.vue",
  "components/quality/RactMatrixViewer.vue",
  "components/quality/StatisticalForensicsViewer.vue",
  "components/shared/ReAuthModal.vue",
  "views/AmendmentDiffView.vue",
  "views/AuditView.vue",
  "views/AuditorView.vue",
  "views/ConsentAuthoringView.vue",
  "views/ForbiddenView.vue",
  "views/ICFBuilderView.vue",
  "views/KnowledgeView.vue",
  "views/LaySummaryPortal.vue",
  "views/LoginView.vue",
  "views/MdrView.vue",
  "views/MedicalCodingView.vue",
  "views/NotificationsView.vue",
  "views/OverviewView.vue",
  "views/ParticipantWalletView.vue",
  "views/ProtocolDigitizationView.vue",
  "views/QualityCockpitView.vue",
  "views/RulesView.vue",
  "views/demo/CtmsDemoWorkspace.vue",
]);

const buttonHoverCompoundRegex =
  /(?:^|[\s>+~(])(?:\.(?![a-z0-9_-]*btn-(?:group|row|bar|wrap|wrapper|container|list|stack|grid|actions|slot)\b)[a-z0-9_-]*btn[a-z0-9_-]*|button)(?:[:.#[]\S*?)?:hover\b/i;

function findUnguardedButtonHoverSelectorsInCss(cssText) {
  const clean = maskCssStringsPreservingLength(
    stripCssCommentsPreservingNewlines(cssText)
  );
  const violations = [];
  const stack = [];
  let segStart = 0;

  for (let i = 0; i < clean.length; i++) {
    const ch = clean[i];
    if (ch === ";") {
      if (stack.length === 0 || stack[stack.length - 1].isAtRule) {
        segStart = i + 1;
      }
    } else if (ch === "{") {
      const header = clean.slice(segStart, i).trim();
      if (header.startsWith("@")) {
        stack.push({ isAtRule: true });
      } else {
        stack.push({
          isAtRule: false,
          selectorText: header,
          headerOffset:
            segStart + (clean.slice(segStart, i).length - header.length),
        });
      }
      segStart = i + 1;
    } else if (ch === "}") {
      const entry = stack.pop();
      if (entry && !entry.isAtRule) {
        for (const sel of splitTopLevelCommas(entry.selectorText)) {
          if (
            buttonHoverCompoundRegex.test(sel) &&
            !/:not\(\s*:disabled\s*\)/.test(sel)
          ) {
            violations.push({
              offset: entry.headerOffset,
              selector: sel.replace(/\s+/g, " ").trim(),
            });
          }
        }
      }
      segStart = i + 1;
    }
  }
  return violations;
}

const VALID_BUTTON_TYPES = new Set(["button", "submit", "reset"]);

const noUnguardedButtonStateRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Require explicit button type attributes and :not(:disabled) hover guards on button and .btn selectors across packages/ui, apps/subject-portal, and migrated apps/web views (ADR-2673).",
      category: "Accessibility",
    },
    messages: {
      missingButtonType:
        'Native <button> is missing an explicit type ("button", "submit", or "reset"). Unspecified buttons default to type="submit" (ADR-2673).',
      unguardedButtonHover:
        'Button hover selector "{{selector}}" is missing a :not(:disabled) guard, allowing disabled buttons to highlight on hover (ADR-2673).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    const sourceText = context.sourceCode.getText();

    if (filename.endsWith(".css")) {
      return {
        Program() {
          for (const v of findUnguardedButtonHoverSelectorsInCss(sourceText)) {
            context.report({
              loc: offsetToLoc(sourceText, v.offset),
              messageId: "unguardedButtonHover",
              data: { selector: v.selector },
            });
          }
        },
      };
    }

    if (!filename.endsWith(".vue")) {
      return {};
    }

    const webSrcMarker = "/apps/web/src/";
    const webSrcIdx = filename.lastIndexOf(webSrcMarker);
    const relWebSrcPath =
      webSrcIdx >= 0 ? filename.slice(webSrcIdx + webSrcMarker.length) : null;
    if (
      relWebSrcPath !== null &&
      LEGACY_WEB_UNGUARDED_BUTTON_ALLOWLIST.has(relWebSrcPath)
    ) {
      return {};
    }

    const styleBlockCheck = {
      "Program:exit"() {
        for (const styleMatch of sourceText.matchAll(
          /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
        )) {
          const contentStart = styleMatch.index + styleMatch[1].length;
          for (const v of findUnguardedButtonHoverSelectorsInCss(
            styleMatch[2]
          )) {
            context.report({
              loc: offsetToLoc(sourceText, contentStart + v.offset),
              messageId: "unguardedButtonHover",
              data: { selector: v.selector },
            });
          }
        }
      },
    };

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return styleBlockCheck;
    }

    return parserServices.defineTemplateBodyVisitor(
      {
        VElement(node) {
          const rawTag = node.rawName || node.name || "";
          if (rawTag !== "button") return;

          const hasValidType = (node.startTag?.attributes || []).some(
            (attr) => {
              if (!attr.directive && attr.key?.type === "VIdentifier") {
                return (
                  attr.key.name === "type" &&
                  VALID_BUTTON_TYPES.has(attr.value?.value)
                );
              }
              if (
                attr.directive &&
                attr.key?.type === "VDirectiveKey" &&
                attr.key.name?.name === "bind"
              ) {
                return attr.key.argument?.name === "type";
              }
              return false;
            }
          );

          if (!hasValidType) {
            context.report({
              node: node.startTag,
              messageId: "missingButtonType",
            });
          }
        },
      },
      styleBlockCheck
    );
  },
};

const FOCUS_HANDOFF_CALLEES = new Set([
  "handoffFocus",
  "handoffRetryFocus",
  "useInlineFocusHandoff",
  "useRetryFocusHandoff",
  "retryWithFocus",
]);

function astContainsFocusHandoffCall(rootNode) {
  if (!rootNode || typeof rootNode !== "object") return false;
  const visited = new WeakSet();
  const stack = [rootNode];
  while (stack.length > 0) {
    const node = stack.pop();
    if (!node || typeof node !== "object" || visited.has(node)) continue;
    visited.add(node);
    if (node.type === "CallExpression") {
      const callee = node.callee;
      if (
        callee?.type === "Identifier" &&
        FOCUS_HANDOFF_CALLEES.has(callee.name)
      ) {
        return true;
      }
      if (
        callee?.type === "MemberExpression" &&
        callee.property?.type === "Identifier" &&
        (FOCUS_HANDOFF_CALLEES.has(callee.property.name) ||
          callee.property.name === "handoff" ||
          callee.property.name === "restoreTrigger")
      ) {
        return true;
      }
    }
    for (const [key, value] of Object.entries(node)) {
      if (key === "parent" || !value) continue;
      if (Array.isArray(value)) {
        for (const item of value) {
          if (item && typeof item === "object") stack.push(item);
        }
      } else if (typeof value === "object") {
        stack.push(value);
      }
    }
  }
  return false;
}

function vElementHasClassToken(vElement, targetToken) {
  for (const attr of vElement.startTag?.attributes || []) {
    if (
      !attr.directive &&
      attr.key?.type === "VIdentifier" &&
      attr.key.name === "class" &&
      typeof attr.value?.value === "string"
    ) {
      if (attr.value.value.split(/\s+/).includes(targetToken)) {
        return true;
      }
    } else if (
      attr.directive &&
      attr.key?.type === "VDirectiveKey" &&
      attr.key.name?.name === "bind" &&
      attr.key.argument?.type === "VIdentifier" &&
      attr.key.argument.name === "class" &&
      attr.value?.expression
    ) {
      const tokens = new Set();
      extractClassNamesFromJsExpr(attr.value.expression, tokens);
      if (tokens.has(targetToken)) {
        return true;
      }
    }
  }
  return false;
}

function vElementHasAttrValue(vElement, attrName, attrVal) {
  for (const attr of vElement.startTag?.attributes || []) {
    if (
      !attr.directive &&
      attr.key?.type === "VIdentifier" &&
      attr.key.name === attrName &&
      attr.value?.value === attrVal
    ) {
      return true;
    }
  }
  return false;
}

function vElementDirectlyEmitsRetryOnClick(vElement) {
  for (const attr of vElement.startTag?.attributes || []) {
    if (
      attr.directive &&
      attr.key?.type === "VDirectiveKey" &&
      attr.key.name?.name === "on" &&
      attr.key.argument?.type === "VIdentifier" &&
      attr.key.argument.name === "click" &&
      attr.value?.expression
    ) {
      let expr = attr.value.expression;
      if (
        expr.type === "VOnExpression" &&
        Array.isArray(expr.body) &&
        expr.body.length === 1 &&
        expr.body[0].type === "ExpressionStatement"
      ) {
        expr = expr.body[0].expression;
      }
      if (
        expr?.type === "CallExpression" &&
        expr.callee?.type === "Identifier" &&
        (expr.callee.name === "$emit" || expr.callee.name === "emit") &&
        expr.arguments?.[0]?.type === "Literal" &&
        expr.arguments[0].value === "retry"
      ) {
        return true;
      }
    }
  }
  return false;
}

const requireInlineFocusHandoffRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Require explicit focus handoff (handoffFocus / handoffRetryFocus) in dropzone remove controls, error alert retry controls, and subject-portal retry* feature handlers",
    },
    schema: [],
    messages: {
      missingDropzoneFocusHandoff:
        "Dropzone remove button (.clinical-dropzone-remove-btn) unmounts on selection removal and must hand focus off via handoffFocus or useInlineFocusHandoff (WCAG 2.2 SC 2.4.3).",
      missingErrorAlertRetryHandoff:
        "Error alert retry button (data-testid=\"canvas-error-retry\") unmounts on retry and must coordinate focus recovery via handoffRetryFocus, useRetryFocusHandoff, or handoffFocus instead of raw emit('retry') (WCAG 2.2 SC 2.4.3).",
      missingPortalRetryFocusHandoff:
        'Subject-portal retry handler "{{fnName}}" hides its failure container on retry and must restore focus via handoffRetryFocus or handoffFocus from "ui" (WCAG 2.2 SC 2.4.3).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");

    if (filename.endsWith(".vue")) {
      const parserServices =
        context.sourceCode?.parserServices || context.parserServices;
      if (!parserServices?.defineTemplateBodyVisitor) {
        return {};
      }
      const dropzoneRemoveNodes = [];
      const errorAlertRetryNodes = [];
      let scriptHasFocusHandoff = false;

      return parserServices.defineTemplateBodyVisitor(
        {
          VElement(node) {
            const rawTag = (node.rawName || node.name || "").toLowerCase();
            if (rawTag !== "button" && rawTag !== "clinicaliconbutton") {
              return;
            }
            if (vElementHasClassToken(node, "clinical-dropzone-remove-btn")) {
              dropzoneRemoveNodes.push(node);
            }
            if (
              vElementHasAttrValue(node, "data-testid", "canvas-error-retry")
            ) {
              errorAlertRetryNodes.push(node);
            }
          },
        },
        {
          Program(programNode) {
            scriptHasFocusHandoff = astContainsFocusHandoffCall(programNode);
          },
          "Program:exit"() {
            for (const removeNode of dropzoneRemoveNodes) {
              if (!scriptHasFocusHandoff) {
                context.report({
                  node: removeNode,
                  messageId: "missingDropzoneFocusHandoff",
                });
              }
            }
            for (const retryNode of errorAlertRetryNodes) {
              if (
                !scriptHasFocusHandoff ||
                vElementDirectlyEmitsRetryOnClick(retryNode)
              ) {
                context.report({
                  node: retryNode,
                  messageId: "missingErrorAlertRetryHandoff",
                });
              }
            }
          },
        }
      );
    }

    if (
      filename.includes("apps/subject-portal/src/features/") &&
      filename.endsWith(".js")
    ) {
      return {
        "ExportNamedDeclaration > FunctionDeclaration"(fnNode) {
          const fnName = fnNode.id?.name || "";
          if (!/^retry[A-Z]/.test(fnName)) return;
          if (!astContainsFocusHandoffCall(fnNode.body)) {
            context.report({
              node: fnNode.id || fnNode,
              messageId: "missingPortalRetryFocusHandoff",
              data: { fnName },
            });
          }
        },
      };
    }

    return {};
  },
};

const customVuePlugin = {
  rules: {
    "no-inline-hex": noInlineHexRule,
    "no-undefined-css-vars": noUndefinedCssVarsRule,
    "no-misused-ai-tokens": noMisusedAiTokensRule,
    "no-raw-overlays": noRawOverlaysRule,
    "no-suppressed-focus-outline": noSuppressedFocusOutlineRule,
    "no-unlabeled-glyph-button": noUnlabeledGlyphButtonRule,
    "no-aria-label-mismatch": noAriaLabelMismatchRule,
    "require-tabular-nums": requireTabularNumsRule,
    "no-sub12px-font-size": noSub12pxFontSizeRule,
    "no-custom-font-stack": noCustomFontStackRule,
    "no-untokenized-font-size": noUntokenizedFontSizeRule,
    "no-sub1rem-touch-control": noSub1RemTouchControlRule,
    "no-vuejs-a11y-suppression": noVuejsA11ySuppressionRule,
    "no-unscoped-clinical-tabs-css": noUnscopedClinicalTabsCssRule,
    "no-raw-clinical-table": noRawClinicalTableRule,
    "no-unguarded-button-state": noUnguardedButtonStateRule,
    "require-inline-focus-handoff": requireInlineFocusHandoffRule,
    "no-unscoped-navigation": noUnscopedNavigationRule,
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
      "**/.cadence/**",
      "**/venv/**",
      "**/.pytest_cache/**",
      "**/playwright-report/**",
      "**/test-results/**",
      "**/TestResults/**",
      "**/coverage/**",
      "**/bin/**",
      "**/obj/**",
      "**/.claude/worktrees/**",
      "docs/.vitepress/.temp/**",
      "docs/.vitepress/cache/**",
      "docs/.vitepress/dist/**",
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
      "custom-vue/no-misused-ai-tokens": "error",
      "custom-vue/no-raw-overlays": "error",
      "custom-vue/no-suppressed-focus-outline": "error",
      "custom-vue/no-unlabeled-glyph-button": "error",
      "custom-vue/no-aria-label-mismatch": "error",
      "custom-vue/require-tabular-nums": "error",
      "custom-vue/no-sub12px-font-size": "error",
      "custom-vue/no-custom-font-stack": "error",
      "custom-vue/no-untokenized-font-size": "error",
      "custom-vue/no-sub1rem-touch-control": "error",
      "custom-vue/no-unscoped-clinical-tabs-css": "error",
      "custom-vue/no-raw-clinical-table": "error",
      "custom-vue/no-unguarded-button-state": "error",
      "custom-vue/require-inline-focus-handoff": "error",
      "custom-vue/no-unscoped-navigation": "error",
      "vue/max-attributes-per-line": "off",
      "vue/singleline-html-element-content-newline": "off",
      "vue/multiline-html-element-content-newline": "off",
      "vue/html-closing-bracket-newline": "off",
      "vue/html-closing-bracket-spacing": "off",
      "vue/html-indent": "off",
      "vue/html-self-closing": "off",
      "vue/attributes-order": "off",
      "vue/require-default-prop": "off",
    },
  },
  {
    files: [
      "apps/web/src/components/ContextualHelpDrawer.vue",
      "apps/web/src/components/econsent/IcfSectionEditor.vue",
      "apps/web/src/views/KnowledgeView.vue",
      "apps/web/src/views/RulesView.vue",
    ],
    rules: {
      "vue/no-v-html": "off",
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
      "custom-vue/no-misused-ai-tokens": "error",
      "custom-vue/no-suppressed-focus-outline": "error",
      "custom-vue/no-unguarded-button-state": "error",
      "custom-vue/require-tabular-nums": "error",
      "custom-vue/no-sub12px-font-size": "error",
      "custom-vue/no-custom-font-stack": "error",
      "custom-vue/no-untokenized-font-size": "error",
      "custom-vue/no-sub1rem-touch-control": "error",
    },
  },
  {
    files: ["docs/.vitepress/**/*.css"],
    languageOptions: {
      parser: cssStubParser,
    },
    plugins: {
      "custom-vue": customVuePlugin,
    },
    rules: {
      ...disabledCoreRulesForCss,
      "custom-vue/no-sub12px-font-size": "error",
      "custom-vue/no-custom-font-stack": "error",
      "custom-vue/no-untokenized-font-size": "error",
      "custom-vue/no-sub1rem-touch-control": "error",
    },
  },
  {
    files: [
      "packages/ui/index.js",
      "packages/ui/src/**/*.js",
      "packages/ui/src/**/*.ts",
      "apps/subject-portal/src/**/*.js",
      "apps/subject-portal/src/**/*.ts",
      "apps/web/src/**/*.js",
      "apps/web/src/**/*.ts",
    ],
    plugins: {
      "custom-vue": customVuePlugin,
    },
    rules: {
      "custom-vue/no-inline-hex": "error",
      "custom-vue/no-undefined-css-vars": "error",
      "custom-vue/no-misused-ai-tokens": "error",
      "custom-vue/no-unlabeled-glyph-button": "error",
      "custom-vue/require-tabular-nums": "error",
      "custom-vue/no-sub12px-font-size": "error",
      "custom-vue/no-custom-font-stack": "error",
      "custom-vue/no-untokenized-font-size": "error",
      "custom-vue/no-sub1rem-touch-control": "error",
      "custom-vue/require-inline-focus-handoff": "error",
      "custom-vue/no-unscoped-navigation": "error",
    },
  },
  {
    files: ["scripts/**/*.{js,mjs,cjs}"],
    rules: {
      "no-alert": "error",
      "no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
    },
  },
  {
    files: ["docs/.vitepress/theme/**/*.js"],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
    },
    plugins: {
      "custom-vue": customVuePlugin,
    },
    rules: {
      "no-undef": "off",
      "no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
      "custom-vue/no-unlabeled-glyph-button": "error",
    },
  },
  ...tseslint.configs.recommended.map((config) => ({
    ...config,
    files: config.files ?? ["**/*.ts", "**/*.tsx", "**/*.mts", "**/*.cts"],
  })),
  {
    files: ["docs/.vitepress/**/*.mjs"],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
    },
    rules: {
      "no-control-regex": "off",
      "no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
    },
  },
  {
    files: ["docs/.vitepress/**/*.cjs"],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "commonjs",
    },
    rules: {
      "no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
    },
  },
  {
    files: ["**/*.ts", "**/*.tsx", "**/*.mts", "**/*.cts"],
    languageOptions: {
      parser: tseslint.parser,
      parserOptions: {
        sourceType: "module",
        ecmaVersion: 2022,
      },
      globals: {
        ...globals.browser,
        ...globals.node,
      },
    },
    plugins: {
      "@typescript-eslint": tseslint.plugin,
    },
    rules: {
      "no-alert": "error",
      "no-undef": "off",
      "no-unused-vars": "off",
      "@typescript-eslint/no-explicit-any": "off",
      "@typescript-eslint/no-empty-object-type": "off",
      "@typescript-eslint/no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
    },
  },
  {
    files: ["**/*.js", "**/*.jsx", "**/*.mjs", "**/*.cjs"],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
      globals: {
        ...globals.browser,
      },
    },
  },
  {
    files: ["**/*.cjs"],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "commonjs",
      globals: {
        ...globals.node,
      },
    },
  },
  {
    files: [
      "*.config.{js,mjs,cjs}",
      "apps/*/*.config.{js,mjs,cjs}",
      "packages/*/*.config.{js,mjs,cjs}",
      "apps/*/scripts/**/*.{js,mjs,cjs}",
      "packages/*/scripts/**/*.{js,mjs,cjs}",
      "scripts/**/*.{js,mjs,cjs}",
      ".claude/**/*.{js,mjs,cjs}",
      "docs/**/*.mjs",
      "tests/**/*.{js,mjs,cjs}",
    ],
    languageOptions: {
      ecmaVersion: 2022,
      globals: {
        ...globals.node,
      },
    },
  },
  {
    files: [
      "apps/web/scripts/**/*.js",
      "packages/ui/scripts/**/*.js",
      "scripts/**/*.js",
    ],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "commonjs",
      globals: {
        ...globals.node,
      },
    },
  },
  {
    files: ["apps/subject-portal/public/sw.js"],
    languageOptions: {
      ecmaVersion: 2022,
      globals: {
        ...globals.serviceworker,
      },
    },
  },
  {
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
      globals: {
        ...globals.browser,
      },
    },
    rules: {
      "no-alert": "error",
      "no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
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
      "packages/ui/index.js",
      "packages/ui/src/**/*.{js,ts,vue}",
      "apps/subject-portal/src/**/*.{js,ts,vue}",
      "apps/web/src/App.vue",
      "apps/web/src/composables/**/*.{js,ts}",
      "apps/web/src/components/*.vue",
      "apps/web/src/components/canvas/**/*.vue",
      "apps/web/src/components/common/**/*.vue",
      "apps/web/src/components/shared/**/*.vue",
      "apps/web/src/components/ctms/**/*.vue",
      "apps/web/src/components/ecrf/**/*.vue",
      "apps/web/src/components/etmf/**/*.vue",
      "apps/web/src/components/tickets/**/*.vue",
      "apps/web/src/components/clinical/SsuGreenlightConsole.vue",
      "apps/web/src/components/clinical/ProtocolTrainingConsole.vue",
      "apps/web/src/components/clinical/SensorTelemetryConsole.vue",
      "apps/web/src/components/clinical/LabGovernanceConsole.vue",
      "apps/web/src/views/OverviewView.vue",
      "apps/web/src/views/PlansView.vue",
      "apps/web/src/views/TrialSetupView.vue",
      "apps/web/src/views/LoginView.vue",
      "apps/web/src/views/ForbiddenView.vue",
      "apps/web/src/views/UnavailableView.vue",
      "apps/web/src/views/StudyCanvasView.vue",
      "apps/web/src/views/ObservationView.vue",
      "apps/web/src/views/EcrfView.vue",
      "apps/web/src/views/EcrfLiveWorkspace.vue",
      "apps/web/src/views/demo/EcrfDemoWorkspace.vue",
      "apps/web/src/views/CtmsView.vue",
      "apps/web/src/views/CtmsLiveWorkspace.vue",
      "apps/web/src/views/demo/CtmsDemoWorkspace.vue",
      "apps/web/src/views/DocumentManagerView.vue",
      "apps/web/src/views/StudyTrainingView.vue",
      "apps/web/src/views/TicketsView.vue",
      "apps/web/src/views/KnowledgeView.vue",
      "apps/web/src/views/DataLockView.vue",
      "apps/web/src/views/ExportWizardView.vue",
    ],
    plugins: {
      "custom-vue": customVuePlugin,
    },
    rules: {
      "custom-vue/no-vuejs-a11y-suppression": "error",
    },
  },
  {
    files: [
      "**/tests/**/*.{js,jsx,ts,tsx,mjs,cjs,vue}",
      "tests/**/*.{js,jsx,ts,tsx,mjs,cjs,vue}",
      "**/*.test.{js,jsx,ts,tsx,mjs,cjs}",
      "**/*.spec.{js,jsx,ts,tsx,mjs,cjs}",
    ],
    languageOptions: {
      globals: {
        ...globals.browser,
        ...globals.node,
        ...globals.commonjs,
        ...globals.vitest,
      },
    },
    plugins: {
      "custom-vue": customVuePlugin,
    },
    rules: {
      "vue/one-component-per-file": "off",
      "vue/require-prop-types": "off",
      "custom-vue/no-inline-hex": "off",
      "custom-vue/no-misused-ai-tokens": "off",
      "custom-vue/no-sub12px-font-size": "off",
      "custom-vue/no-custom-font-stack": "off",
      "custom-vue/no-untokenized-font-size": "off",
      "custom-vue/no-sub1rem-touch-control": "off",
      "custom-vue/no-unscoped-navigation": "off",
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": "off",
    },
  },
];
