import fs from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const repoRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../.."
);
export const tokensCssPath = path.join(repoRoot, "packages/ui/tokens.css");
export const webStyleCssPath = path.join(repoRoot, "apps/web/src/style.css");
export const portalStyleCssPath = path.join(
  repoRoot,
  "apps/subject-portal/src/style.css"
);

function resolveVitePressVarsCssPath() {
  const directPath = path.join(
    repoRoot,
    "node_modules/vitepress/dist/client/theme-default/styles/vars.css"
  );
  if (fs.existsSync(directPath)) return directPath;
  try {
    const require = createRequire(import.meta.url);
    return require.resolve("vitepress/dist/client/theme-default/styles/vars.css");
  } catch {
    return directPath;
  }
}

export const vitepressVarsCssPath = resolveVitePressVarsCssPath();

export const hexColorRegex = /#(?:[0-9a-fA-F]{3,4}){1,2}(?![0-9a-zA-Z_-])/g;
export const colorFunctionRegex = /\b(?:rgba?|hsla?)\s*\([^)]*\)/gi;
export const namedColorDeclRegex =
  /(?:^|[;{\s"'`])(?:background(?:-color)?|color|border(?:-(?:top|right|bottom|left))?(?:-color)?|fill|stroke|outline(?:-color)?)\s*:\s*(white|black)\b/gi;
export const cssVarDefRegex = /(--[a-zA-Z0-9_-]+)\s*:/g;
export const cssVarUseRegex = /var\(\s*(--[a-zA-Z0-9_-]+)/g;
export const aiTokenRegex = /--(?:semantic-)?color-ai(?:-[a-zA-Z0-9_-]+)?\b/g;

export function isAllowedAiTokenFile(filename) {
  if (filename.endsWith("/packages/ui/tokens.css")) return true;
  if (filename.includes("/apps/web/src/components/ai/")) return true;
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

export function getBaseDefinedVars(filename) {
  const normalized = filename.split(path.sep).join("/");
  const vars = extractDefinedVarsFromFile(tokensCssPath);
  if (normalized.includes("/apps/web/") || normalized.startsWith("apps/web/")) {
    for (const v of extractDefinedVarsFromFile(webStyleCssPath)) vars.add(v);
  } else if (
    normalized.includes("/apps/subject-portal/") ||
    normalized.startsWith("apps/subject-portal/")
  ) {
    for (const v of extractDefinedVarsFromFile(portalStyleCssPath)) vars.add(v);
  } else if (
    normalized.includes("/docs/.vitepress/") ||
    normalized.startsWith("docs/.vitepress/")
  ) {
    for (const v of extractDefinedVarsFromFile(vitepressVarsCssPath)) {
      vars.add(v);
    }
  }
  return vars;
}

export function stripCssCommentsPreservingNewlines(text) {
  return text.replace(/\/\*[\s\S]*?\*\//g, (comment) =>
    comment.replace(/[^\r\n]/g, " ")
  );
}

export function stripUrlFragmentsPreservingLength(text) {
  return text.replace(/url\(\s*['"]?#[^)]*\)/gi, (u) => " ".repeat(u.length));
}

export function offsetToLoc(fullText, offset) {
  const upTo = fullText.slice(0, offset);
  const lines = upTo.split(/\r?\n/);
  return {
    line: lines.length,
    column: lines[lines.length - 1].length,
  };
}

export function isStyleOrColorVAttribute(node) {
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

export function getStaticAttrValue(elementNode, attrName) {
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

export function hasStaticOrBoundAttr(elementNode, attrName) {
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

export function maskCssStringsPreservingLength(text) {
  return text.replace(/(["'])(?:\\.|(?!\1)[^\\\r\n])*\1/g, (s) =>
    " ".repeat(s.length)
  );
}

export function splitTopLevelCommas(selectorText) {
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

export function walkCssRuleBlocks(cssText, onBlock) {
  const noComments = stripCssCommentsPreservingNewlines(cssText);
  const clean = maskCssStringsPreservingLength(noComments);
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
          rawSelectorText: noComments.slice(segStart, i).trim(),
          cleanSelectorText: header,
          headerOffset:
            segStart + (clean.slice(segStart, i).length - header.length),
          bodyStart: i + 1,
        });
      }
      segStart = i + 1;
    } else if (ch === "}") {
      const entry = stack.pop();
      if (entry && !entry.isAtRule) {
        onBlock({
          rawSelectorText: entry.rawSelectorText,
          cleanSelectorText: entry.cleanSelectorText,
          headerOffset: entry.headerOffset,
          bodyStart: entry.bodyStart,
          body: clean.slice(entry.bodyStart, i),
        });
      }
      segStart = i + 1;
    }
  }
}

export function isAriaHiddenElement(elementNode) {
  return getStaticAttrValue(elementNode, "aria-hidden") === "true";
}

export function isSrOnlyElement(elementNode) {
  const cls = getStaticAttrValue(elementNode, "class") || "";
  return /\b(?:sr-only|visually-hidden)\b/.test(cls);
}

export function extractStaticExpressionStrings(expr) {
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

export function normalizeLabelWords(text) {
  return String(text || "")
    .replace(/[^\p{L}\p{N}]+/gu, " ")
    .toLowerCase()
    .trim()
    .replace(/\s+/g, " ");
}

export const ALLOWED_TOKENIZED_FONT_FAMILIES = new Set([
  "inherit",
  "var(--semantic-font-mono)",
  "var(--semantic-font-family)",
  "var(--font-sans)",
  "var(--font)",
  "var(--font-mono)",
]);

function buildVueStyleVisitors(
  context,
  sourceText,
  { matchAttribute, onStyleBlock, onAttrValue }
) {
  const styleBlockCheck = {
    "Program:exit"() {
      for (const styleMatch of sourceText.matchAll(
        /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
      )) {
        const contentStart = styleMatch.index + styleMatch[1].length;
        onStyleBlock(styleMatch[2], contentStart);
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
        if (matchAttribute(node) && node.value) {
          onAttrValue(node.value, context.sourceCode.getText(node.value));
        }
      },
    },
    styleBlockCheck
  );
}

function isStyleVAttribute(node) {
  const isStaticStyle =
    !node.directive &&
    node.key?.type === "VIdentifier" &&
    node.key.name === "style";
  const isDynamicStyle =
    node.directive &&
    node.key?.type === "VDirectiveKey" &&
    node.key.name?.name === "bind" &&
    node.key.argument?.name === "style";
  return isStaticStyle || isDynamicStyle;
}

export function createTypographyScanVisitors(
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
    return buildVueStyleVisitors(context, sourceText, {
      matchAttribute: isStyleOrColorVAttribute,
      onStyleBlock(rawStyle, contentStart) {
        onCssBlock(
          stripUrlFragmentsPreservingLength(
            stripCssCommentsPreservingNewlines(rawStyle)
          ),
          contentStart,
          sourceText,
          filename
        );
      },
      onAttrValue: onVueStyleAttr,
    });
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

export function extractClassNamesFromJsExpr(expr, out) {
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

export function createCssAndVueStyleVisitors(
  context,
  { ignoreTests = false, onCssText, onStyleAttr }
) {
  const filename = (context.filename || context.getFilename() || "")
    .split(path.sep)
    .join("/");

  if (
    ignoreTests &&
    (filename.includes("/tests/") || /\.(?:test|spec)\.[a-z]+$/i.test(filename))
  ) {
    return {};
  }

  const sourceText = context.sourceCode.getText();

  if (filename.endsWith(".css")) {
    return {
      Program() {
        onCssText(sourceText, 0, sourceText);
      },
    };
  }

  if (!filename.endsWith(".vue")) {
    return {};
  }

  return buildVueStyleVisitors(context, sourceText, {
    matchAttribute: isStyleVAttribute,
    onStyleBlock(rawStyle, contentStart) {
      onCssText(rawStyle, contentStart, sourceText);
    },
    onAttrValue: onStyleAttr,
  });
}
