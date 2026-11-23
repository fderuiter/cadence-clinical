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
const colorFunctionRegex =
  /(?<![a-zA-Z0-9_-])(?:rgba?|hsla?|hwb|oklab|oklch|lab|lch)\s*\((?:[^()]+|\([^()]*\))*\)/gi;
const dropShadowFunctionRegex =
  /(?<![a-zA-Z0-9_-])drop-shadow\s*\((?:[^()]+|\([^()]*\))*\)/gi;
const boxShadowLayerPattern =
  "(?:var\\(\\s*--[a-zA-Z0-9_-]*shadow[a-zA-Z0-9_-]*\\s*\\)|0(?:px)?\\s+0(?:px)?\\s+0(?:px)?\\s+[0-9.]+(?:px|rem)\\s+var\\(\\s*--(?:semantic-)?color-[a-zA-Z0-9_-]+\\s*\\))";
const allowedBoxShadowValueRegex = new RegExp(
  `^(?:none|inherit|initial|unset|revert|revert-layer|${boxShadowLayerPattern}(?:\\s*,\\s*${boxShadowLayerPattern})*)$`,
  "i"
);
const cssVarDefRegex = /(--[a-zA-Z0-9_-]+)\s*:/g;
const cssVarUseRegex = /var\(\s*(--[a-zA-Z0-9_-]+)/g;

const cssNamedColors = new Set([
  "aliceblue",
  "antiquewhite",
  "aqua",
  "aquamarine",
  "azure",
  "beige",
  "bisque",
  "black",
  "blanchedalmond",
  "blue",
  "blueviolet",
  "brown",
  "burlywood",
  "cadetblue",
  "chartreuse",
  "chocolate",
  "coral",
  "cornflowerblue",
  "cornsilk",
  "crimson",
  "cyan",
  "darkblue",
  "darkcyan",
  "darkgoldenrod",
  "darkgray",
  "darkgreen",
  "darkgrey",
  "darkkhaki",
  "darkmagenta",
  "darkolivegreen",
  "darkorange",
  "darkorchid",
  "darkred",
  "darksalmon",
  "darkseagreen",
  "darkslateblue",
  "darkslategray",
  "darkslategrey",
  "darkturquoise",
  "darkviolet",
  "deeppink",
  "deepskyblue",
  "dimgray",
  "dimgrey",
  "dodgerblue",
  "firebrick",
  "floralwhite",
  "forestgreen",
  "fuchsia",
  "gainsboro",
  "ghostwhite",
  "gold",
  "goldenrod",
  "gray",
  "green",
  "greenyellow",
  "grey",
  "honeydew",
  "hotpink",
  "indianred",
  "indigo",
  "ivory",
  "khaki",
  "lavender",
  "lavenderblush",
  "lawngreen",
  "lemonchiffon",
  "lightblue",
  "lightcoral",
  "lightcyan",
  "lightgoldenrodyellow",
  "lightgray",
  "lightgreen",
  "lightgrey",
  "lightpink",
  "lightsalmon",
  "lightseagreen",
  "lightskyblue",
  "lightslategray",
  "lightslategrey",
  "lightsteelblue",
  "lightyellow",
  "lime",
  "limegreen",
  "linen",
  "magenta",
  "maroon",
  "mediumaquamarine",
  "mediumblue",
  "mediumorchid",
  "mediumpurple",
  "mediumseagreen",
  "mediumslateblue",
  "mediumspringgreen",
  "mediumturquoise",
  "mediumvioletred",
  "midnightblue",
  "mintcream",
  "mistyrose",
  "moccasin",
  "navajowhite",
  "navy",
  "oldlace",
  "olive",
  "olivedrab",
  "orange",
  "orangered",
  "orchid",
  "palegoldenrod",
  "palegreen",
  "paleturquoise",
  "palevioletred",
  "papayawhip",
  "peachpuff",
  "peru",
  "pink",
  "plum",
  "powderblue",
  "purple",
  "rebeccapurple",
  "red",
  "rosybrown",
  "royalblue",
  "saddlebrown",
  "salmon",
  "sandybrown",
  "seagreen",
  "seashell",
  "sienna",
  "silver",
  "skyblue",
  "slateblue",
  "slategray",
  "slategrey",
  "snow",
  "springgreen",
  "steelblue",
  "tan",
  "teal",
  "thistle",
  "tomato",
  "turquoise",
  "violet",
  "wheat",
  "white",
  "whitesmoke",
  "yellow",
  "yellowgreen",
]);

const nonColorCssProperties = new Set([
  "animation",
  "animation-name",
  "content",
  "counter-increment",
  "counter-reset",
  "cursor",
  "font-display",
  "font-family",
  "grid-area",
  "grid-template",
  "grid-template-areas",
  "list-style",
  "list-style-type",
  "transition",
  "transition-property",
  "white-space",
  "will-change",
]);

const colorCssProperties = new Set([
  "accent-color",
  "background",
  "background-color",
  "border",
  "border-bottom",
  "border-bottom-color",
  "border-color",
  "border-left",
  "border-left-color",
  "border-right",
  "border-right-color",
  "border-top",
  "border-top-color",
  "caret-color",
  "color",
  "column-rule",
  "column-rule-color",
  "fill",
  "flood-color",
  "lighting-color",
  "outline",
  "outline-color",
  "stop-color",
  "stroke",
  "text-decoration",
  "text-decoration-color",
]);

const jsStyleColorProperties = new Set([
  "accentColor",
  "background",
  "backgroundColor",
  "border",
  "borderBottom",
  "borderBottomColor",
  "borderColor",
  "borderLeft",
  "borderLeftColor",
  "borderRight",
  "borderRightColor",
  "borderTop",
  "borderTopColor",
  "caretColor",
  "color",
  "columnRuleColor",
  "fill",
  "fillStyle",
  "outline",
  "outlineColor",
  "shadowColor",
  "stopColor",
  "stroke",
  "strokeStyle",
  "textDecorationColor",
]);

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

function stripNonColorVarsAndStringsPreservingLength(valueText) {
  return valueText
    .replace(/url\([^)]*\)/gi, (m) => " ".repeat(m.length))
    .replace(/(?:"[^"]*"|'[^']*')/g, (m) => " ".repeat(m.length))
    .replace(/var\(\s*--[a-zA-Z0-9_-]+\s*\)/g, (m) => " ".repeat(m.length))
    .replace(/var\(\s*--[a-zA-Z0-9_-]+\s*,/g, (prefix) =>
      " ".repeat(prefix.length)
    );
}

function isAllowedBoxShadowValue(rawVal) {
  const withoutImportant = rawVal.replace(/\s*!important\s*$/i, "").trim();
  if (!withoutImportant) return true;
  return allowedBoxShadowValueRegex.test(withoutImportant);
}

function findNamedColorsInCssValue(valueText) {
  const scrubbed = stripNonColorVarsAndStringsPreservingLength(valueText);
  const results = [];
  for (const m of scrubbed.matchAll(
    /(?<![a-zA-Z0-9_-])([a-zA-Z]+)(?![a-zA-Z0-9_(-])/g
  )) {
    const word = m[1];
    if (cssNamedColors.has(word.toLowerCase())) {
      results.push({ color: word, index: m.index });
    }
  }
  return results;
}

function checkCssDeclarationsInText(
  maskedText,
  reportAtOffset,
  onlyKnownColorProps = false
) {
  const declRegex = /(?:^|[{;\s"'])([a-zA-Z-]+)\s*:\s*([^;{}<>`"]+)/g;
  for (const declMatch of maskedText.matchAll(declRegex)) {
    const propName = declMatch[1].toLowerCase();
    const rawValue = declMatch[2];
    const valueOffset = declMatch.index + declMatch[0].length - rawValue.length;

    if (propName === "box-shadow") {
      if (!isAllowedBoxShadowValue(rawValue)) {
        reportAtOffset(valueOffset, "noUntokenizedBoxShadow", {
          value: rawValue.trim(),
        });
      }
      continue;
    }

    if (nonColorCssProperties.has(propName)) {
      continue;
    }
    if (
      onlyKnownColorProps &&
      !colorCssProperties.has(propName) &&
      !propName.startsWith("--")
    ) {
      continue;
    }

    for (const hit of findNamedColorsInCssValue(rawValue)) {
      reportAtOffset(valueOffset + hit.index, "noNamedColorInStyle", {
        color: hit.color,
      });
    }
  }
}

function scanCssBlockForUntokenizedStyles(
  rawCss,
  baseOffset,
  sourceText,
  context,
  isTokensCss = false
) {
  let masked = stripUrlFragmentsPreservingLength(
    stripCssCommentsPreservingNewlines(rawCss)
  );
  if (isTokensCss) {
    masked = masked.replace(
      /(?::root(?:\[[^\]]+\])?|\[data-theme="[^"]+"\]|\.dark)[^{]*\{[^}]*\}/g,
      (rootBlock) => rootBlock.replace(/[^\r\n]/g, " ")
    );
  }

  for (const match of masked.matchAll(hexColorRegex)) {
    context.report({
      loc: offsetToLoc(sourceText, baseOffset + match.index),
      messageId: "noHexInStyle",
      data: { hex: match[0] },
    });
  }

  for (const match of masked.matchAll(colorFunctionRegex)) {
    context.report({
      loc: offsetToLoc(sourceText, baseOffset + match.index),
      messageId: "noColorFunctionInStyle",
      data: { fn: match[0].trim() },
    });
  }

  for (const match of masked.matchAll(dropShadowFunctionRegex)) {
    const inner = match[0]
      .replace(/^drop-shadow\s*\(/i, "")
      .replace(/\)$/, "")
      .trim();
    if (!isAllowedBoxShadowValue(inner)) {
      context.report({
        loc: offsetToLoc(sourceText, baseOffset + match.index),
        messageId: "noUntokenizedBoxShadow",
        data: { value: match[0].trim() },
      });
    }
  }

  // Mask selector and at-rule headers while preserving exact offsets so declarations
  // inside both flat and nested CSS blocks are scanned without matching pseudo-classes (:hover).
  const declOnlyCss = masked.replace(/[^{};]*\{/g, (header) =>
    header.replace(/[^\r\n]/g, " ")
  );
  checkCssDeclarationsInText(
    declOnlyCss,
    (relOffset, messageId, data) => {
      context.report({
        loc: offsetToLoc(sourceText, baseOffset + relOffset),
        messageId,
        data,
      });
    },
    false
  );
}

function isStaticOrDynamicPropertyName(propNode) {
  if (!propNode) return null;
  if (propNode.type === "Identifier") return propNode.name;
  if (propNode.type === "Literal" && typeof propNode.value === "string") {
    return propNode.value;
  }
  return null;
}

function isDomSelectorArgument(node) {
  const parent = node.parent;
  if (!parent || parent.type !== "CallExpression") return false;
  const callee = parent.callee;
  const methodName =
    callee?.type === "MemberExpression"
      ? isStaticOrDynamicPropertyName(callee.property)
      : callee?.type === "Identifier"
        ? callee.name
        : null;
  return (
    methodName === "querySelector" ||
    methodName === "querySelectorAll" ||
    methodName === "closest" ||
    methodName === "matches" ||
    methodName === "getElementById"
  );
}

function inspectScriptStringValue(
  rawStr,
  reportNode,
  context,
  skipHexSelector
) {
  if (!rawStr) return;
  const masked = stripUrlFragmentsPreservingLength(rawStr).replace(
    /[a-zA-Z0-9/_?&=-]+#[0-9a-fA-F]{3,8}\b/g,
    (m) => " ".repeat(m.length)
  );

  if (!skipHexSelector) {
    const hexMatches = masked.match(hexColorRegex);
    if (hexMatches) {
      for (const hex of new Set(hexMatches)) {
        context.report({
          node: reportNode,
          messageId: "noHexInStyle",
          data: { hex },
        });
      }
    }
  }

  const fnMatches = masked.match(colorFunctionRegex);
  if (fnMatches) {
    for (const fn of new Set(fnMatches.map((s) => s.trim()))) {
      context.report({
        node: reportNode,
        messageId: "noColorFunctionInStyle",
        data: { fn },
      });
    }
  }

  const dropMatches = masked.match(dropShadowFunctionRegex);
  if (dropMatches) {
    for (const ds of new Set(dropMatches.map((s) => s.trim()))) {
      const inner = ds
        .replace(/^drop-shadow\s*\(/i, "")
        .replace(/\)$/, "")
        .trim();
      if (!isAllowedBoxShadowValue(inner)) {
        context.report({
          node: reportNode,
          messageId: "noUntokenizedBoxShadow",
          data: { value: ds },
        });
      }
    }
  }

  checkCssDeclarationsInText(
    masked,
    (_offset, messageId, data) => {
      context.report({
        node: reportNode,
        messageId,
        data,
      });
    },
    true
  );
}

function getStaticStringFromAstNode(node) {
  if (!node) return null;
  if (node.type === "Literal" && typeof node.value === "string") {
    return node.value;
  }
  if (node.type === "TemplateLiteral" && node.expressions.length === 0) {
    return node.quasis[0]?.value?.cooked ?? node.quasis[0]?.value?.raw ?? null;
  }
  return null;
}

function createScriptAstColorVisitor(context) {
  return {
    Literal(node) {
      if (typeof node.value !== "string") return;
      inspectScriptStringValue(
        node.value,
        node,
        context,
        isDomSelectorArgument(node)
      );
    },
    TemplateElement(node) {
      const val = node.value?.cooked ?? node.value?.raw ?? "";
      inspectScriptStringValue(
        val,
        node,
        context,
        node.parent ? isDomSelectorArgument(node.parent) : false
      );
    },
    Property(node) {
      const keyName = isStaticOrDynamicPropertyName(node.key);
      if (!keyName) return;
      const strVal = getStaticStringFromAstNode(node.value);
      if (strVal === null) return;
      if (keyName === "boxShadow" || keyName === "box-shadow") {
        if (!isAllowedBoxShadowValue(strVal)) {
          context.report({
            node: node.value,
            messageId: "noUntokenizedBoxShadow",
            data: { value: strVal.trim() },
          });
        }
      } else if (
        jsStyleColorProperties.has(keyName) ||
        colorCssProperties.has(keyName.toLowerCase())
      ) {
        for (const hit of findNamedColorsInCssValue(strVal)) {
          context.report({
            node: node.value,
            messageId: "noNamedColorInStyle",
            data: { color: hit.color },
          });
        }
      }
    },
    AssignmentExpression(node) {
      if (node.left?.type !== "MemberExpression") return;
      const propName = isStaticOrDynamicPropertyName(node.left.property);
      if (!propName) return;
      const strVal = getStaticStringFromAstNode(node.right);
      if (strVal === null) return;
      if (propName === "boxShadow" || propName === "box-shadow") {
        if (!isAllowedBoxShadowValue(strVal)) {
          context.report({
            node: node.right,
            messageId: "noUntokenizedBoxShadow",
            data: { value: strVal.trim() },
          });
        }
      } else if (
        propName === "fillStyle" ||
        propName === "strokeStyle" ||
        propName === "shadowColor" ||
        (node.left.object?.type === "MemberExpression" &&
          isStaticOrDynamicPropertyName(node.left.object.property) ===
            "style" &&
          jsStyleColorProperties.has(propName))
      ) {
        for (const hit of findNamedColorsInCssValue(strVal)) {
          context.report({
            node: node.right,
            messageId: "noNamedColorInStyle",
            data: { color: hit.color },
          });
        }
      }
    },
    CallExpression(node) {
      if (node.callee?.type !== "MemberExpression") return;
      const propName = isStaticOrDynamicPropertyName(node.callee.property);
      if (propName === "addColorStop" && node.arguments.length >= 2) {
        const strVal = getStaticStringFromAstNode(node.arguments[1]);
        if (strVal !== null) {
          for (const hit of findNamedColorsInCssValue(strVal)) {
            context.report({
              node: node.arguments[1],
              messageId: "noNamedColorInStyle",
              data: { color: hit.color },
            });
          }
        }
      }
    },
  };
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
        "Disallow hardcoded hex colors, color functions (rgb/rgba/hsl/hsla), CSS named colors, and untokenized box-shadow declarations across CSS stylesheets, Vue SFCs (<template>, <script>, <style>), and JS/TS modules outside packages/ui/tokens.css theme blocks.",
      category: "Best Practices",
    },
    messages: {
      noHexInStyle:
        'Hardcoded hex color "{{hex}}" is prohibited. Use design system tokens defined in packages/ui/tokens.css (e.g. var(--semantic-color-...)).',
      noColorFunctionInStyle:
        'Hardcoded color function "{{fn}}" is prohibited. Use design system tokens defined in packages/ui/tokens.css (e.g. var(--semantic-color-...)).',
      noNamedColorInStyle:
        'Hardcoded named CSS color "{{color}}" is prohibited. Use design system tokens defined in packages/ui/tokens.css (e.g. var(--semantic-color-surface) or var(--semantic-color-text-on-solid)).',
      noUntokenizedBoxShadow:
        'Untokenized box-shadow "{{value}}" is prohibited. Use crisp 1px borders, box-shadow: none, or centralized shadow tokens (var(--shadow-*) / var(--semantic-shadow-*)) from packages/ui/tokens.css.',
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
          scanCssBlockForUntokenizedStyles(
            sourceText,
            0,
            sourceText,
            context,
            filename.endsWith("/packages/ui/tokens.css")
          );
        },
      };
    }

    // 2. Plain .js / .ts / .mjs / .cjs modules
    if (!filename.endsWith(".vue")) {
      return createScriptAstColorVisitor(context);
    }

    // 3. Vue SFCs: check <template>, <script>, and <style> blocks
    const scriptColorVisitor = createScriptAstColorVisitor(context);
    const scriptAndStyleVisitor = {
      ...scriptColorVisitor,
      "Program:exit"() {
        for (const styleMatch of sourceText.matchAll(
          /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
        )) {
          const contentStart = styleMatch.index + styleMatch[1].length;
          scanCssBlockForUntokenizedStyles(
            styleMatch[2],
            contentStart,
            sourceText,
            context,
            false
          );
        }
      },
    };

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return scriptAndStyleVisitor;
    }

    return parserServices.defineTemplateBodyVisitor(
      {
        VAttribute(node) {
          if (!isStyleOrColorVAttribute(node) || !node.value) return;

          const attrName = !node.directive
            ? node.key.name
            : node.key.argument?.name;

          if (!node.directive && node.value.type === "VLiteral") {
            const rawAttr = node.value.value || "";
            if (attrName === "style") {
              const masked = stripUrlFragmentsPreservingLength(rawAttr);
              const hexMatches = masked.match(hexColorRegex);
              if (hexMatches) {
                for (const hex of new Set(hexMatches)) {
                  context.report({
                    node: node.value,
                    messageId: "noHexInStyle",
                    data: { hex },
                  });
                }
              }
              const fnMatches = masked.match(colorFunctionRegex);
              if (fnMatches) {
                for (const fn of new Set(fnMatches.map((s) => s.trim()))) {
                  context.report({
                    node: node.value,
                    messageId: "noColorFunctionInStyle",
                    data: { fn },
                  });
                }
              }
              const dropMatches = masked.match(dropShadowFunctionRegex);
              if (dropMatches) {
                for (const ds of new Set(dropMatches.map((s) => s.trim()))) {
                  const inner = ds
                    .replace(/^drop-shadow\s*\(/i, "")
                    .replace(/\)$/, "")
                    .trim();
                  if (!isAllowedBoxShadowValue(inner)) {
                    context.report({
                      node: node.value,
                      messageId: "noUntokenizedBoxShadow",
                      data: { value: ds },
                    });
                  }
                }
              }
              checkCssDeclarationsInText(
                masked,
                (_offset, messageId, data) => {
                  context.report({
                    node: node.value,
                    messageId,
                    data,
                  });
                },
                false
              );
            } else {
              // Presentation attribute: fill, stroke, color
              const masked = stripUrlFragmentsPreservingLength(rawAttr);
              const hexMatches = masked.match(hexColorRegex);
              if (hexMatches) {
                for (const hex of new Set(hexMatches)) {
                  context.report({
                    node: node.value,
                    messageId: "noHexInStyle",
                    data: { hex },
                  });
                }
              }
              const fnMatches = masked.match(colorFunctionRegex);
              if (fnMatches) {
                for (const fn of new Set(fnMatches.map((s) => s.trim()))) {
                  context.report({
                    node: node.value,
                    messageId: "noColorFunctionInStyle",
                    data: { fn },
                  });
                }
              }
              for (const hit of findNamedColorsInCssValue(masked)) {
                context.report({
                  node: node.value,
                  messageId: "noNamedColorInStyle",
                  data: { color: hit.color },
                });
              }
            }
            return;
          }

          // Dynamic :style, :fill, :stroke, :color expression
          const rawExprNode =
            node.value.type === "VExpressionContainer" && node.value.expression
              ? node.value.expression
              : node.value;
          const exprText = stripUrlFragmentsPreservingLength(
            context.sourceCode.getText(rawExprNode)
          );
          const hexMatches = exprText.match(hexColorRegex);
          if (hexMatches) {
            for (const hex of new Set(hexMatches)) {
              context.report({
                node: node.value,
                messageId: "noHexInStyle",
                data: { hex },
              });
            }
          }
          const fnMatches = exprText.match(colorFunctionRegex);
          if (fnMatches) {
            for (const fn of new Set(fnMatches.map((s) => s.trim()))) {
              context.report({
                node: node.value,
                messageId: "noColorFunctionInStyle",
                data: { fn },
              });
            }
          }
          // Check string literals inside dynamic :style / :fill / :stroke / :color
          for (const litMatch of exprText.matchAll(
            /(["'`])((?:\\.|(?!\1)[\s\S])*?)\1/g
          )) {
            const innerStr = litMatch[2];
            if (attrName === "style") {
              checkCssDeclarationsInText(
                innerStr,
                (_offset, messageId, data) => {
                  context.report({
                    node: node.value,
                    messageId,
                    data,
                  });
                },
                false
              );
              // Also check object-syntax :style="{ backgroundColor: 'white', boxShadow: '0 2px 4px ...' }"
              const trimmed = innerStr.trim();
              if (cssNamedColors.has(trimmed.toLowerCase())) {
                context.report({
                  node: node.value,
                  messageId: "noNamedColorInStyle",
                  data: { color: trimmed },
                });
              }
            } else {
              for (const hit of findNamedColorsInCssValue(innerStr)) {
                context.report({
                  node: node.value,
                  messageId: "noNamedColorInStyle",
                  data: { color: hit.color },
                });
              }
            }
          }
          if (attrName === "style") {
            for (const objShadowMatch of exprText.matchAll(
              /(?:boxShadow|['"]box-shadow['"])\s*:\s*(['"`])((?:\\.|(?!\1)[\s\S])*?)\1/g
            )) {
              if (!isAllowedBoxShadowValue(objShadowMatch[2])) {
                context.report({
                  node: node.value,
                  messageId: "noUntokenizedBoxShadow",
                  data: { value: objShadowMatch[2].trim() },
                });
              }
            }
          }
        },
      },
      scriptAndStyleVisitor
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
    const isVitePressFile = filename.includes("/docs/.vitepress/");
    const isVarDefined = (varName) =>
      allowedVars.has(varName) ||
      (isVitePressFile && varName.startsWith("--vp-"));

    // Collect locally declared custom properties in the current file
    if (filename.endsWith(".css")) {
      const clean = stripCssCommentsPreservingNewlines(sourceText);
      for (const m of clean.matchAll(cssVarDefRegex)) {
        allowedVars.add(m[1]);
      }
      return {
        Program() {
          for (const match of clean.matchAll(cssVarUseRegex)) {
            if (!isVarDefined(match[1])) {
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
              if (!isVarDefined(match[1])) {
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
              if (!isVarDefined(match[1])) {
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
                if (!isVarDefined(match[1])) {
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
          if (!isVarDefined(match[1])) {
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

const noRawOverlaysRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow raw unguarded modal/drawer overlay divs and vuejs-accessibility backdrop suppressions outside packages/ui primitives.",
      category: "Accessibility",
    },
    messages: {
      noRawOverlay:
        'Raw overlay container "{{className}}" is prohibited. Migrate to ClinicalModal or ClinicalDrawer from "ui" (ADR-2545).',
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
          selectors.every((sel) =>
            /:focus:not\(\s*:focus-visible\s*\)/.test(sel)
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
        "Disallow bare outline: none or outline: 0 rules without :focus-visible across packages/ui and apps/web (WCAG 2.2 SC 2.4.7 / Failure F78).",
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
    if (filename.includes("/apps/subject-portal/")) {
      return {};
    }

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

const customVuePlugin = {
  rules: {
    "no-inline-hex": noInlineHexRule,
    "no-undefined-css-vars": noUndefinedCssVarsRule,
    "no-raw-overlays": noRawOverlaysRule,
    "no-suppressed-focus-outline": noSuppressedFocusOutlineRule,
    "no-unlabeled-glyph-button": noUnlabeledGlyphButtonRule,
    "no-aria-label-mismatch": noAriaLabelMismatchRule,
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
      "**/coverage/**",
      "**/.claude/**",
      "scripts/**",
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
      "custom-vue/no-raw-overlays": "error",
      "custom-vue/no-suppressed-focus-outline": "error",
      "custom-vue/no-unlabeled-glyph-button": "error",
      "custom-vue/no-aria-label-mismatch": "error",
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
    files: [
      "packages/**/*.css",
      "apps/**/*.css",
      "docs/.vitepress/theme/**/*.css",
    ],
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
      "custom-vue/no-suppressed-focus-outline": "error",
    },
  },
  {
    files: [
      "packages/ui/index.js",
      "packages/ui/econsent.js",
      "packages/ui/signing.js",
      "packages/ui/session.js",
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
      "custom-vue/no-unlabeled-glyph-button": "error",
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
      "custom-vue/no-inline-hex": "error",
      "custom-vue/no-undefined-css-vars": "error",
      "custom-vue/no-unlabeled-glyph-button": "error",
    },
  },
  {
    files: ["docs/.vitepress/*.js", "docs/.vitepress/**/*.mjs"],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
    },
    rules: {
      "no-undef": "off",
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
      "no-undef": "off",
      "no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
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
      "no-alert": "error",
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
    files: ["**/tests/**", "tests/**", "**/*.test.*", "**/*.spec.*"],
    rules: {
      "custom-vue/no-inline-hex": "off",
      "vue/one-component-per-file": "off",
      "vue/require-prop-types": "off",
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": "off",
    },
  },
];
