import path from "node:path";
import { offsetToLoc, walkCssRuleBlocks } from "../shared.mjs";

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
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");

    if (
      filename.includes("/tests/") ||
      /\.(?:test|spec)\.[a-z]+$/i.test(filename)
    ) {
      return {};
    }

    const sourceText = context.sourceCode.getText();

    if (filename.endsWith(".css")) {
      return {
        Program() {
          for (const v of findUnpositionedScrollWrappersInCss(sourceText)) {
            context.report({
              loc: offsetToLoc(sourceText, v.offset),
              messageId: "missingScrollWrapperRelative",
              data: {
                target: v.selector,
                declaration: v.declaration,
              },
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
          for (const v of findUnpositionedScrollWrappersInCss(styleMatch[2])) {
            context.report({
              loc: offsetToLoc(sourceText, contentStart + v.offset),
              messageId: "missingScrollWrapperRelative",
              data: {
                target: v.selector,
                declaration: v.declaration,
              },
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
            if (positionedDeclRegex.test(rawVal)) return;
            for (const match of rawVal.matchAll(overflowScrollDeclRegex)) {
              context.report({
                node: node.value,
                messageId: "missingScrollWrapperRelative",
                data: {
                  target: "inline style",
                  declaration: match[0].trim(),
                },
              });
            }
          }
        },
      },
      styleBlockCheck
    );
  },
};
