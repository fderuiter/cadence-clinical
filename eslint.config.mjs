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
  const alignedClasses = new Set(["tabular-nums", "clinical-badge"]);
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
    const normalOverrideRegex = /font-variant-numeric\s*:\s*normal\b/gi;

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
              if (/font-variant-numeric\s*:\s*normal\b/i.test(text)) {
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
    "require-tabular-nums": requireTabularNumsRule,
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
      "custom-vue/require-tabular-nums": "error",
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
      "custom-vue/require-tabular-nums": "error",
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
      "custom-vue/require-tabular-nums": "error",
    },
  },
  {
    files: ["docs/.vitepress/*.mjs", "docs/.vitepress/*.cjs"],
    languageOptions: {
      ecmaVersion: 2022,
    },
    rules: {
      "no-undef": "off",
      "no-control-regex": "off",
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
      "vue/one-component-per-file": "off",
      "vue/require-prop-types": "off",
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": "off",
    },
  },
];
