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

const customVuePlugin = {
  rules: {
    "no-inline-hex": noInlineHexRule,
    "no-undefined-css-vars": noUndefinedCssVarsRule,
    "no-misused-ai-tokens": noMisusedAiTokensRule,
    "no-raw-overlays": noRawOverlaysRule,
    "no-suppressed-focus-outline": noSuppressedFocusOutlineRule,
    "no-unlabeled-glyph-button": noUnlabeledGlyphButtonRule,
    "no-aria-label-mismatch": noAriaLabelMismatchRule,
    "no-vuejs-a11y-suppression": noVuejsA11ySuppressionRule,
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
      "custom-vue/no-misused-ai-tokens": "error",
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
      "custom-vue/no-misused-ai-tokens": "error",
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
      "custom-vue/no-unlabeled-glyph-button": "error",
    },
  },
  {
    files: ["docs/.vitepress/**/*.mjs"],
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
    files: ["**/tests/**", "tests/**", "**/*.test.*", "**/*.spec.*"],
    plugins: {
      "custom-vue": customVuePlugin,
    },
    rules: {
      "vue/one-component-per-file": "off",
      "vue/require-prop-types": "off",
      "custom-vue/no-misused-ai-tokens": "off",
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": "off",
    },
  },
];
