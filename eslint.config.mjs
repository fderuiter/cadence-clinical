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
            masked = masked.replace(
              /(?::root(?:\[[^\]]+\])?|\[data-theme="[^"]+"\]|\.dark)[^{]*\{[^}]*\}/g,
              (rootBlock) => rootBlock.replace(/[^\r\n]/g, " ")
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

function parseCssRuleBlocks(maskedText, baseOffset = 0) {
  const blocks = [];
  let i = 0;
  let stmtStart = 0;
  const len = maskedText.length;

  while (i < len) {
    const ch = maskedText[i];
    if (ch === "'" || ch === '"') {
      const quote = ch;
      i++;
      while (i < len && maskedText[i] !== quote) {
        if (maskedText[i] === "\\") i++;
        i++;
      }
      i++;
      continue;
    }
    if (ch === ";" && stmtStart <= i) {
      stmtStart = i + 1;
      i++;
      continue;
    }
    if (ch === "{") {
      const header = maskedText.slice(stmtStart, i);
      const selectorTrimmed = header.trim();
      const bodyStart = i + 1;
      let depth = 1;
      i++;
      while (i < len && depth > 0) {
        const c = maskedText[i];
        if (c === "'" || c === '"') {
          const q = c;
          i++;
          while (i < len && maskedText[i] !== q) {
            if (maskedText[i] === "\\") i++;
            i++;
          }
        } else if (c === "{") {
          depth++;
        } else if (c === "}") {
          depth--;
          if (depth === 0) break;
        }
        i++;
      }
      const bodyEnd = i;
      const innerBody = maskedText.slice(bodyStart, bodyEnd);
      if (/^@(?:media|supports|layer|container)\b/i.test(selectorTrimmed)) {
        blocks.push(...parseCssRuleBlocks(innerBody, baseOffset + bodyStart));
      } else if (!/^@(?:-webkit-|-moz-)?keyframes\b/i.test(selectorTrimmed)) {
        blocks.push({
          selector: selectorTrimmed,
          selectorOffset: baseOffset + stmtStart,
          body: innerBody,
          bodyOffset: baseOffset + bodyStart,
        });
      }
      i++;
      stmtStart = i;
      continue;
    }
    i++;
  }
  return blocks;
}

function extractCssDeclarations(body, bodyOffset) {
  const decls = [];
  let start = 0;
  let parenDepth = 0;
  const len = body.length;

  const pushChunk = (endIdx) => {
    const rawChunk = body.slice(start, endIdx);
    const leadWs = rawChunk.match(/^\s*/)?.[0]?.length ?? 0;
    const trimmed = rawChunk.trim();
    if (!trimmed) return;
    const colonIdx = trimmed.indexOf(":");
    if (colonIdx <= 0) return;
    const prop = trimmed.slice(0, colonIdx).trim().toLowerCase();
    if (!/^(?:--[a-z0-9_-]+|[a-z-]+)$/i.test(prop)) return;
    const rawValue = trimmed
      .slice(colonIdx + 1)
      .replace(/!important\s*$/i, "")
      .trim();
    decls.push({
      prop,
      value: rawValue,
      offset: bodyOffset + start + leadWs,
    });
  };

  for (let i = 0; i < len; i++) {
    const ch = body[i];
    if (ch === "'" || ch === '"') {
      const q = ch;
      i++;
      while (i < len && body[i] !== q) {
        if (body[i] === "\\") i++;
        i++;
      }
      continue;
    }
    if (ch === "(") parenDepth++;
    else if (ch === ")" && parenDepth > 0) parenDepth--;
    else if (ch === ";" && parenDepth === 0) {
      pushChunk(i);
      start = i + 1;
    }
  }
  pushChunk(len);
  return decls;
}

function splitTopLevelCssList(text, delimiter = ",") {
  const parts = [];
  let start = 0;
  let paren = 0;
  let bracket = 0;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (ch === "'" || ch === '"') {
      const q = ch;
      i++;
      while (i < text.length && text[i] !== q) {
        if (text[i] === "\\") i++;
        i++;
      }
      continue;
    }
    if (ch === "(") paren++;
    else if (ch === ")" && paren > 0) paren--;
    else if (ch === "[") bracket++;
    else if (ch === "]" && bracket > 0) bracket--;
    else if (ch === delimiter && paren === 0 && bracket === 0) {
      parts.push(text.slice(start, i));
      start = i + 1;
    }
  }
  parts.push(text.slice(start));
  return parts;
}

function isRootThemeSelector(selector) {
  const parts = splitTopLevelCssList(selector, ",")
    .map((s) => s.trim())
    .filter(Boolean);
  if (parts.length === 0) return false;
  return parts.every((p) =>
    /^(?::root(?:\[[^\]]+\])?|\[data-theme="[^"]+"\]|\.dark)$/.test(p)
  );
}

function isAllowedFontFamilyValue(rawValue) {
  const val = rawValue.replace(/!important\s*$/i, "").trim();
  if (!val) return true;
  if (
    /^(?:inherit|initial|unset|revert|revert-layer)$/i.test(val) ||
    /^var\(\s*--(?:font|semantic-font|vp-font-family)[a-zA-Z0-9_-]*\s*\)$/.test(
      val
    )
  ) {
    return true;
  }
  return false;
}

let cachedBaseRootTokens = null;

function getBaseRootTokens() {
  if (cachedBaseRootTokens) return cachedBaseRootTokens;
  const map = new Map();
  if (fs.existsSync(tokensCssPath)) {
    const cleanTokens = stripCssCommentsPreservingNewlines(
      fs.readFileSync(tokensCssPath, "utf8")
    );
    for (const block of parseCssRuleBlocks(cleanTokens)) {
      if (isRootThemeSelector(block.selector)) {
        for (const d of extractCssDeclarations(block.body, block.bodyOffset)) {
          if (d.prop.startsWith("--") && !map.has(d.prop)) {
            map.set(d.prop, d.value);
          }
        }
      }
    }
  }
  cachedBaseRootTokens = map;
  return map;
}

function buildTokenRemMap(extraCssTexts = []) {
  const rawVars = new Map(getBaseRootTokens());
  for (const cssText of extraCssTexts) {
    const clean = stripCssCommentsPreservingNewlines(cssText);
    for (const block of parseCssRuleBlocks(clean)) {
      for (const d of extractCssDeclarations(block.body, block.bodyOffset)) {
        if (d.prop.startsWith("--")) {
          rawVars.set(d.prop, d.value);
        }
      }
    }
  }

  const resolveRem = (expr, seen = new Set()) => {
    if (!expr) return null;
    const trimmed = expr.replace(/!important\s*$/i, "").trim();
    const varMatch = trimmed.match(
      /^var\(\s*(--[a-zA-Z0-9_-]+)(?:\s*,\s*([^)]+))?\s*\)$/
    );
    if (varMatch) {
      const varName = varMatch[1].toLowerCase();
      if (!seen.has(varName) && rawVars.has(varName)) {
        const nextSeen = new Set(seen);
        nextSeen.add(varName);
        const resolved = resolveRem(rawVars.get(varName), nextSeen);
        if (resolved !== null) return resolved;
      }
      if (varMatch[2]) {
        return resolveRem(varMatch[2], seen);
      }
      return null;
    }

    const clampMatch = trimmed.match(/^clamp\(\s*([^,]+),/i);
    if (clampMatch) {
      return resolveRem(clampMatch[1], seen);
    }

    const unitMatch = trimmed.match(
      /^([+-]?(?:\d+\.?\d*|\.\d+))(rem|em|px|%|pt)$/i
    );
    if (!unitMatch) return null;
    const num = Number.parseFloat(unitMatch[1]);
    const unit = unitMatch[2].toLowerCase();
    if (unit === "rem" || unit === "em") return num;
    if (unit === "px") return num / 16;
    if (unit === "%") return num / 100;
    if (unit === "pt") return (num * (96 / 72)) / 16;
    return null;
  };

  return { rawVars, resolveRem };
}

const KNOWN_FORM_CONTROL_CLASSES = new Set([
  "clinical-input",
  "clinical-lookup-input",
  "form-control",
  "form-input",
  "form-select",
  "form-textarea",
]);

const NON_TEXT_INPUT_TYPES = new Set([
  "checkbox",
  "radio",
  "range",
  "hidden",
  "file",
  "color",
  "button",
  "submit",
  "reset",
  "image",
]);

function unwrapScopedPseudoSelectors(sel) {
  let current = sel;
  let prev = "";
  while (current !== prev) {
    prev = current;
    current = current.replace(
      /:(?:deep|slotted|global)\(\s*([^()]*)\s*\)/gi,
      " $1 "
    );
  }
  return current;
}

function extractRightmostCompoundSelector(complexSelector) {
  const unwrapped = unwrapScopedPseudoSelectors(complexSelector).trim();
  let paren = 0;
  let bracket = 0;
  let lastBoundary = 0;
  for (let i = 0; i < unwrapped.length; i++) {
    const ch = unwrapped[i];
    if (ch === "'" || ch === '"') {
      const q = ch;
      i++;
      while (i < unwrapped.length && unwrapped[i] !== q) {
        if (unwrapped[i] === "\\") i++;
        i++;
      }
      continue;
    }
    if (ch === "(") paren++;
    else if (ch === ")" && paren > 0) paren--;
    else if (ch === "[") bracket++;
    else if (ch === "]" && bracket > 0) bracket--;
    else if (
      paren === 0 &&
      bracket === 0 &&
      (ch === ">" || ch === "+" || ch === "~" || /\s/.test(ch))
    ) {
      lastBoundary = i + 1;
    }
  }
  return unwrapped.slice(lastBoundary).trim();
}

function selectorTargetsTextFormControl(
  selectorList,
  templateControlClasses = new Set()
) {
  for (const rawBranch of splitTopLevelCssList(selectorList, ",")) {
    const branch = rawBranch.trim();
    if (!branch || branch.startsWith("@") || branch.includes("::")) continue;
    const compound = extractRightmostCompoundSelector(branch);
    if (!compound) continue;

    const withoutNot = compound.replace(/:not\([^)]*\)/gi, "");
    if (
      /\[type\s*=\s*["']?(?:checkbox|radio|range|hidden|file|color|button|submit|reset|image)["']?\s*\]/i.test(
        withoutNot
      )
    ) {
      continue;
    }

    const strippedAttrsAndPseudos = withoutNot
      .replace(/\[[^\]]*\]/g, "")
      .replace(/:[a-zA-Z-]+(?:\([^)]*\))?/g, "");

    if (/^(?:input|select|textarea)(?=$|[.#])/i.test(strippedAttrsAndPseudos)) {
      return true;
    }

    for (const clsMatch of strippedAttrsAndPseudos.matchAll(
      /\.([a-zA-Z0-9_-]+)/g
    )) {
      const cls = clsMatch[1];
      if (
        KNOWN_FORM_CONTROL_CLASSES.has(cls) ||
        templateControlClasses.has(cls)
      ) {
        return true;
      }
    }
  }
  return false;
}

function isVueTextFormControlElement(elementNode) {
  const tag = elementNode.rawName || elementNode.name || "";
  const lowerTag = tag.toLowerCase();
  const controlTags = new Set([
    "input",
    "select",
    "textarea",
    "clinicalinput",
    "clinical-input",
    "clinicallookupinput",
    "clinical-lookup-input",
  ]);
  if (!controlTags.has(lowerTag)) return false;
  if (lowerTag === "input") {
    for (const attr of elementNode.startTag?.attributes || []) {
      if (
        !attr.directive &&
        attr.key?.type === "VIdentifier" &&
        attr.key.name === "type" &&
        attr.value?.value &&
        NON_TEXT_INPUT_TYPES.has(attr.value.value.trim().toLowerCase())
      ) {
        return false;
      }
    }
  }
  return true;
}

function collectStaticElementClasses(elementNode) {
  const classes = [];
  for (const attr of elementNode.startTag?.attributes || []) {
    if (
      !attr.directive &&
      attr.key?.type === "VIdentifier" &&
      attr.key.name === "class" &&
      attr.value?.value
    ) {
      for (const token of attr.value.value.split(/\s+/).filter(Boolean)) {
        classes.push(token);
      }
    }
  }
  return classes;
}

function extractInlineFontDeclsFromAttrText(attrText) {
  const results = [];
  for (const m of attrText.matchAll(
    /(?:^|[;"'`{\s,])(font-family|fontFamily|font-size|fontSize)\s*:\s*([^;"'`}\n,]+(?:\([^)]*\)[^;"'`}\n,]*)?)/g
  )) {
    const rawProp = m[1];
    const cleanVal = m[2]
      .trim()
      .replace(/^['"`]|['"`]$/g, "")
      .replace(/!important\s*$/i, "")
      .trim();
    results.push({
      prop:
        rawProp === "fontFamily"
          ? "font-family"
          : rawProp === "fontSize"
            ? "font-size"
            : rawProp,
      value: cleanVal,
    });
  }
  return results;
}

function stripJsCommentsPreservingLayout(sourceText) {
  return stripCssCommentsPreservingNewlines(sourceText).replace(
    /\/\/.*$/gm,
    (c) => " ".repeat(c.length)
  );
}

const noRawFontFamilyRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow hardcoded font-family stacks outside packages/ui/fonts.css and packages/ui/tokens.css :root.",
      category: "Best Practices",
    },
    messages: {
      noRawFontFamily:
        'Hardcoded font-family "{{value}}" is prohibited. Reference var(--font-sans), var(--font-mono), or var(--font) from packages/ui/tokens.css.',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    const sourceText = context.sourceCode.getText();

    if (filename.endsWith("/packages/ui/fonts.css")) {
      return {};
    }

    const checkCssText = (rawCss, baseOffset, isTokensCss = false) => {
      const masked = stripCssCommentsPreservingNewlines(rawCss);
      for (const block of parseCssRuleBlocks(masked, baseOffset)) {
        if (isTokensCss && isRootThemeSelector(block.selector)) continue;
        if (/^@font-face\b/i.test(block.selector) && isTokensCss) continue;
        for (const decl of extractCssDeclarations(
          block.body,
          block.bodyOffset
        )) {
          if (
            decl.prop === "font-family" &&
            !isAllowedFontFamilyValue(decl.value)
          ) {
            context.report({
              loc: offsetToLoc(sourceText, decl.offset),
              messageId: "noRawFontFamily",
              data: { value: decl.value },
            });
          }
        }
      }
    };

    const checkJsScriptText = (rawScript, baseOffset) => {
      const clean = stripJsCommentsPreservingLayout(rawScript);
      for (const m of clean.matchAll(
        /(?:style\.fontFamily\s*=\s*['"`]([^'"`]+)['"`]|\bfontFamily\s*:\s*['"`]([^'"`]+)['"`]|font-family\s*:\s*([^;"'`}\n]+))/g
      )) {
        const val = (m[1] ?? m[2] ?? m[3] ?? "").trim();
        if (val && !isAllowedFontFamilyValue(val)) {
          context.report({
            loc: offsetToLoc(sourceText, baseOffset + m.index),
            messageId: "noRawFontFamily",
            data: { value: val },
          });
        }
      }
    };

    if (filename.endsWith(".css")) {
      return {
        Program() {
          checkCssText(
            sourceText,
            0,
            filename.endsWith("/packages/ui/tokens.css")
          );
        },
      };
    }

    if (filename.endsWith(".vue")) {
      const scriptAndStyleVisitor = {
        "Program:exit"() {
          for (const sm of sourceText.matchAll(
            /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
          )) {
            checkCssText(sm[2], sm.index + sm[1].length, false);
          }
          for (const scm of sourceText.matchAll(
            /(<script\b[^>]*>)([\s\S]*?)<\/script>/gi
          )) {
            checkJsScriptText(scm[2], scm.index + scm[1].length);
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
            if (isStyleOrColorVAttribute(node) && node.value) {
              const attrText = context.sourceCode.getText(node.value);
              for (const d of extractInlineFontDeclsFromAttrText(attrText)) {
                if (
                  d.prop === "font-family" &&
                  !isAllowedFontFamilyValue(d.value)
                ) {
                  context.report({
                    node: node.value,
                    messageId: "noRawFontFamily",
                    data: { value: d.value },
                  });
                }
              }
            }
          },
        },
        scriptAndStyleVisitor
      );
    }

    return {
      Program() {
        checkJsScriptText(sourceText, 0);
      },
    };
  },
};

const noPxFontSizeRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow raw px font-size declarations outside :root so browser root font-size scaling works cleanly to 200%.",
      category: "Best Practices",
    },
    messages: {
      noPxFontSize:
        'Raw px font-size "{{value}}" is prohibited outside :root. Use rem tokens from packages/ui/tokens.css (e.g. var(--font-size-sm)).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    const sourceText = context.sourceCode.getText();
    const pxUnitRegex = /(?:^|[^\w-])\d+(?:\.\d+)?px\b/i;

    const checkCssText = (rawCss, baseOffset) => {
      const masked = stripCssCommentsPreservingNewlines(rawCss);
      for (const block of parseCssRuleBlocks(masked, baseOffset)) {
        if (isRootThemeSelector(block.selector)) continue;
        for (const decl of extractCssDeclarations(
          block.body,
          block.bodyOffset
        )) {
          if (
            (decl.prop === "font-size" || decl.prop.includes("font-size")) &&
            pxUnitRegex.test(decl.value)
          ) {
            context.report({
              loc: offsetToLoc(sourceText, decl.offset),
              messageId: "noPxFontSize",
              data: { value: decl.value },
            });
          }
        }
      }
    };

    const checkJsScriptText = (rawScript, baseOffset) => {
      const clean = stripJsCommentsPreservingLayout(rawScript);
      for (const m of clean.matchAll(
        /(?:style\.fontSize\s*=\s*['"`]([^'"`]+)['"`]|\bfontSize\s*:\s*['"`]([^'"`]+)['"`]|font-size\s*:\s*([^;"'`}\n]+))/g
      )) {
        const val = (m[1] ?? m[2] ?? m[3] ?? "").trim();
        if (val && pxUnitRegex.test(val)) {
          context.report({
            loc: offsetToLoc(sourceText, baseOffset + m.index),
            messageId: "noPxFontSize",
            data: { value: val },
          });
        }
      }
    };

    if (filename.endsWith(".css")) {
      return {
        Program() {
          checkCssText(sourceText, 0);
        },
      };
    }

    if (filename.endsWith(".vue")) {
      const scriptAndStyleVisitor = {
        "Program:exit"() {
          for (const sm of sourceText.matchAll(
            /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
          )) {
            checkCssText(sm[2], sm.index + sm[1].length);
          }
          for (const scm of sourceText.matchAll(
            /(<script\b[^>]*>)([\s\S]*?)<\/script>/gi
          )) {
            checkJsScriptText(scm[2], scm.index + scm[1].length);
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
            if (isStyleOrColorVAttribute(node) && node.value) {
              const attrText = context.sourceCode.getText(node.value);
              for (const d of extractInlineFontDeclsFromAttrText(attrText)) {
                if (d.prop === "font-size" && pxUnitRegex.test(d.value)) {
                  context.report({
                    node: node.value,
                    messageId: "noPxFontSize",
                    data: { value: d.value },
                  });
                }
              }
            }
          },
        },
        scriptAndStyleVisitor
      );
    }

    return {
      Program() {
        checkJsScriptText(sourceText, 0);
      },
    };
  },
};

const noSubfloorFontSizeRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow font-size declarations resolving below the 0.75rem (12px) minimum clinical legibility floor.",
      category: "Best Practices",
    },
    messages: {
      subfloorFontSize:
        'Font size "{{value}}" resolves below the 0.75rem (12px) legibility floor. Use at least var(--font-size-2xs) (0.75rem).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    const sourceText = context.sourceCode.getText();
    const MIN_FLOOR_REM = 0.75 - 1e-6;

    const checkCssText = (rawCss, baseOffset, resolver) => {
      const masked = stripCssCommentsPreservingNewlines(rawCss);
      for (const block of parseCssRuleBlocks(masked, baseOffset)) {
        for (const decl of extractCssDeclarations(
          block.body,
          block.bodyOffset
        )) {
          if (decl.prop === "font-size" || decl.prop.includes("font-size")) {
            const remVal = resolver.resolveRem(decl.value);
            if (remVal !== null && remVal < MIN_FLOOR_REM) {
              context.report({
                loc: offsetToLoc(sourceText, decl.offset),
                messageId: "subfloorFontSize",
                data: { value: decl.value },
              });
            }
          }
        }
      }
    };

    const checkJsScriptText = (rawScript, baseOffset, resolver) => {
      const clean = stripJsCommentsPreservingLayout(rawScript);
      for (const m of clean.matchAll(
        /(?:style\.fontSize\s*=\s*['"`]([^'"`]+)['"`]|\bfontSize\s*:\s*['"`]([^'"`]+)['"`]|font-size\s*:\s*([^;"'`}\n]+))/g
      )) {
        const val = (m[1] ?? m[2] ?? m[3] ?? "").trim();
        if (val) {
          const remVal = resolver.resolveRem(val);
          if (remVal !== null && remVal < MIN_FLOOR_REM) {
            context.report({
              loc: offsetToLoc(sourceText, baseOffset + m.index),
              messageId: "subfloorFontSize",
              data: { value: val },
            });
          }
        }
      }
    };

    if (filename.endsWith(".css")) {
      const resolver = buildTokenRemMap([sourceText]);
      return {
        Program() {
          checkCssText(sourceText, 0, resolver);
        },
      };
    }

    if (filename.endsWith(".vue")) {
      const styleTexts = Array.from(
        sourceText.matchAll(/<style\b[^>]*>([\s\S]*?)<\/style>/gi),
        (m) => m[1]
      );
      const resolver = buildTokenRemMap(styleTexts);

      const scriptAndStyleVisitor = {
        "Program:exit"() {
          for (const sm of sourceText.matchAll(
            /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
          )) {
            checkCssText(sm[2], sm.index + sm[1].length, resolver);
          }
          for (const scm of sourceText.matchAll(
            /(<script\b[^>]*>)([\s\S]*?)<\/script>/gi
          )) {
            checkJsScriptText(scm[2], scm.index + scm[1].length, resolver);
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
            if (isStyleOrColorVAttribute(node) && node.value) {
              const attrText = context.sourceCode.getText(node.value);
              for (const d of extractInlineFontDeclsFromAttrText(attrText)) {
                if (d.prop === "font-size") {
                  const remVal = resolver.resolveRem(d.value);
                  if (remVal !== null && remVal < MIN_FLOOR_REM) {
                    context.report({
                      node: node.value,
                      messageId: "subfloorFontSize",
                      data: { value: d.value },
                    });
                  }
                }
              }
            }
          },
        },
        scriptAndStyleVisitor
      );
    }

    const resolver = buildTokenRemMap();
    return {
      Program() {
        checkJsScriptText(sourceText, 0, resolver);
      },
    };
  },
};

const noSub1RemFormControlRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow font-size declarations resolving below 1rem (16px) on text inputs, selects, textareas, and .clinical-input controls to prevent mobile auto-zoom.",
      category: "Best Practices",
    },
    messages: {
      sub1RemControl:
        'Form control font-size "{{value}}" resolves below 1rem (16px) and triggers iOS/iPadOS viewport auto-zoom. Use var(--semantic-font-size-control) or var(--font-size-md) (1rem).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    const sourceText = context.sourceCode.getText();
    const MIN_CONTROL_REM = 1.0 - 1e-6;

    const checkCssBlocks = (
      rawCss,
      baseOffset,
      resolver,
      templateControlClasses
    ) => {
      const masked = stripCssCommentsPreservingNewlines(rawCss);
      for (const block of parseCssRuleBlocks(masked, baseOffset)) {
        if (
          !selectorTargetsTextFormControl(
            block.selector,
            templateControlClasses
          )
        ) {
          continue;
        }
        for (const decl of extractCssDeclarations(
          block.body,
          block.bodyOffset
        )) {
          if (decl.prop === "font-size") {
            const remVal = resolver.resolveRem(decl.value);
            if (remVal !== null && remVal < MIN_CONTROL_REM) {
              context.report({
                loc: offsetToLoc(sourceText, decl.offset),
                messageId: "sub1RemControl",
                data: { value: decl.value },
              });
            }
          }
        }
      }
    };

    if (filename.endsWith(".css")) {
      const resolver = buildTokenRemMap([sourceText]);
      return {
        Program() {
          checkCssBlocks(sourceText, 0, resolver, new Set());
        },
      };
    }

    if (filename.endsWith(".vue")) {
      const styleTexts = Array.from(
        sourceText.matchAll(/<style\b[^>]*>([\s\S]*?)<\/style>/gi),
        (m) => m[1]
      );
      const resolver = buildTokenRemMap(styleTexts);
      const templateControlClasses = new Set();

      const styleExitVisitor = {
        "Program:exit"() {
          for (const sm of sourceText.matchAll(
            /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
          )) {
            checkCssBlocks(
              sm[2],
              sm.index + sm[1].length,
              resolver,
              templateControlClasses
            );
          }
        },
      };

      const parserServices =
        context.sourceCode?.parserServices || context.parserServices;
      if (!parserServices?.defineTemplateBodyVisitor) {
        return styleExitVisitor;
      }

      return parserServices.defineTemplateBodyVisitor(
        {
          VElement(node) {
            if (!isVueTextFormControlElement(node)) return;
            for (const cls of collectStaticElementClasses(node)) {
              templateControlClasses.add(cls);
            }
            for (const attr of node.startTag?.attributes || []) {
              if (isStyleOrColorVAttribute(attr) && attr.value) {
                const attrText = context.sourceCode.getText(attr.value);
                for (const d of extractInlineFontDeclsFromAttrText(attrText)) {
                  if (d.prop === "font-size") {
                    const remVal = resolver.resolveRem(d.value);
                    if (remVal !== null && remVal < MIN_CONTROL_REM) {
                      context.report({
                        node: attr.value,
                        messageId: "sub1RemControl",
                        data: { value: d.value },
                      });
                    }
                  }
                }
              }
            }
          },
        },
        styleExitVisitor
      );
    }

    // JS / TS files: check embedded CSS rules targeting form controls
    const resolver = buildTokenRemMap();
    return {
      Program() {
        for (const tplMatch of sourceText.matchAll(/`([\s\S]*?)`/g)) {
          if (tplMatch[1].includes("{") && tplMatch[1].includes("font-size")) {
            checkCssBlocks(
              tplMatch[1],
              tplMatch.index + 1,
              resolver,
              new Set()
            );
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
    "no-raw-overlays": noRawOverlaysRule,
    "no-unlabeled-glyph-button": noUnlabeledGlyphButtonRule,
    "no-aria-label-mismatch": noAriaLabelMismatchRule,
    "no-raw-font-family": noRawFontFamilyRule,
    "no-px-font-size": noPxFontSizeRule,
    "no-subfloor-font-size": noSubfloorFontSizeRule,
    "no-sub-1rem-form-control": noSub1RemFormControlRule,
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
      "custom-vue/no-unlabeled-glyph-button": "error",
      "custom-vue/no-aria-label-mismatch": "error",
      "custom-vue/no-raw-font-family": "error",
      "custom-vue/no-px-font-size": "error",
      "custom-vue/no-subfloor-font-size": "error",
      "custom-vue/no-sub-1rem-form-control": "error",
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
      "custom-vue/no-raw-font-family": "error",
      "custom-vue/no-px-font-size": "error",
      "custom-vue/no-subfloor-font-size": "error",
      "custom-vue/no-sub-1rem-form-control": "error",
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
      "custom-vue/no-inline-hex": "error",
      "custom-vue/no-raw-font-family": "error",
      "custom-vue/no-px-font-size": "error",
      "custom-vue/no-subfloor-font-size": "error",
      "custom-vue/no-sub-1rem-form-control": "error",
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
      "custom-vue/no-unlabeled-glyph-button": "error",
      "custom-vue/no-raw-font-family": "error",
      "custom-vue/no-px-font-size": "error",
      "custom-vue/no-subfloor-font-size": "error",
      "custom-vue/no-sub-1rem-form-control": "error",
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
      "custom-vue/no-raw-font-family": "error",
      "custom-vue/no-px-font-size": "error",
      "custom-vue/no-subfloor-font-size": "error",
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
    files: ["**/*.mjs", "**/*.cjs"],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
    },
    rules: {
      "no-undef": "off",
    },
  },
  {
    files: ["**/*.cjs"],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "commonjs",
    },
  },
  {
    files: ["docs/.vitepress/repoLinks.mjs"],
    rules: {
      "no-control-regex": "off",
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
