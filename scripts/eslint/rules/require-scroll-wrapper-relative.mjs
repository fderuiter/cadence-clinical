import {
  createCssAndVueStyleVisitors,
  offsetToLoc,
  walkCssRuleBlocks,
} from "../shared.mjs";

const overflowScrollDeclRegex = /\boverflow(?:-x)?\s*:\s*(?:auto|scroll)\b/gi;
const positionedDeclRegex =
  /\bposition\s*:\s*(?:relative|sticky|absolute|fixed)\b/i;

function findUnpositionedScrollWrappersInCss(cssText) {
  const violations = [];
  walkCssRuleBlocks(cssText, ({ rawSelectorText, bodyStart, body }) => {
    if (positionedDeclRegex.test(body)) return;
    for (const match of body.matchAll(overflowScrollDeclRegex)) {
      violations.push({
        offset: bodyStart + match.index,
        selector: rawSelectorText.replace(/\s+/g, " ").trim(),
        declaration: match[0].trim(),
      });
    }
  });
  return violations;
}

export const requireScrollWrapperRelativeRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Require position: relative (or sticky/absolute/fixed) on horizontal and overflow scroll wrappers so .sr-only spans and sticky headers remain contained inside the scrollport.",
      category: "Accessibility",
    },
    schema: [],
    messages: {
      missingScrollWrapperRelative:
        'Scroll wrapper "{{target}}" declares "{{declaration}}" without positioned containment. Add "position: relative;" so .sr-only spans cannot escape and inflate document scrollWidth.',
    },
  },
  create(context) {
    return createCssAndVueStyleVisitors(context, {
      ignoreTests: true,
      onCssText(cssText, contentStart, fullSourceText) {
        for (const v of findUnpositionedScrollWrappersInCss(cssText)) {
          context.report({
            loc: offsetToLoc(fullSourceText, contentStart + v.offset),
            messageId: "missingScrollWrapperRelative",
            data: {
              target: v.selector,
              declaration: v.declaration,
            },
          });
        }
      },
      onStyleAttr(valueNode, rawVal) {
        if (positionedDeclRegex.test(rawVal)) return;
        for (const match of rawVal.matchAll(overflowScrollDeclRegex)) {
          context.report({
            node: valueNode,
            messageId: "missingScrollWrapperRelative",
            data: {
              target: "inline style",
              declaration: match[0].trim(),
            },
          });
        }
      },
    });
  },
};
