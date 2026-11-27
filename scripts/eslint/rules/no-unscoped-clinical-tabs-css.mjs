import path from "node:path";
import {
  extractClassNamesFromJsExpr,
  maskCssStringsPreservingLength,
  offsetToLoc,
  stripCssCommentsPreservingNewlines,
} from "../shared.mjs";

const CLINICAL_TABS_CLASS_PROP_MAP = new Map([
  ["list-class", "list-class"],
  ["listClass", "list-class"],
  ["tab-class", "tab-class"],
  ["tabClass", "tab-class"],
  ["active-tab-class", "active-tab-class"],
  ["activeTabClass", "active-tab-class"],
]);

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

export const noUnscopedClinicalTabsCssRule = {
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
