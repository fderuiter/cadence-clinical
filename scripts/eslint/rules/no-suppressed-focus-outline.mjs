import {
  createCssAndVueStyleVisitors,
  offsetToLoc,
  splitTopLevelCommas,
  walkCssRuleBlocks,
} from "../shared.mjs";

const suppressedOutlineDeclRegex =
  /\boutline(?:-style|-width)?\s*:\s*(?:none|0(?:px)?)\b/gi;

function findUnscopedSuppressedOutlinesInCss(cssText) {
  const violations = [];
  walkCssRuleBlocks(cssText, ({ rawSelectorText, bodyStart, body }) => {
    const selectors = splitTopLevelCommas(rawSelectorText);
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
          offset: bodyStart + match.index,
          declaration: match[0].trim(),
        });
      }
    }
  });
  return violations;
}

export const noSuppressedFocusOutlineRule = {
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
    return createCssAndVueStyleVisitors(context, {
      onCssText(cssText, contentStart, fullSourceText) {
        for (const v of findUnscopedSuppressedOutlinesInCss(cssText)) {
          context.report({
            loc: offsetToLoc(fullSourceText, contentStart + v.offset),
            messageId: "noSuppressedOutline",
            data: { declaration: v.declaration },
          });
        }
      },
      onStyleAttr(valueNode, rawVal) {
        for (const match of rawVal.matchAll(suppressedOutlineDeclRegex)) {
          context.report({
            node: valueNode,
            messageId: "noSuppressedOutline",
            data: { declaration: match[0].trim() },
          });
        }
      },
    });
  },
};
