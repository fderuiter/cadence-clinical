import path from "node:path";
import { LEGACY_WEB_UNGUARDED_BUTTON_ALLOWLIST } from "../allowlists/index.mjs";
import {
  offsetToLoc,
  splitTopLevelCommas,
  walkCssRuleBlocks,
} from "../shared.mjs";

const buttonHoverCompoundRegex =
  /(?:^|[\s>+~(])(?:\.(?![a-z0-9_-]*btn-(?:group|row|bar|wrap|wrapper|container|list|stack|grid|actions|slot)\b)[a-z0-9_-]*btn[a-z0-9_-]*|button)(?:[:.#[]\S*?)?:hover\b/i;

function findUnguardedButtonHoverSelectorsInCss(cssText) {
  const violations = [];
  walkCssRuleBlocks(cssText, ({ cleanSelectorText, headerOffset }) => {
    for (const sel of splitTopLevelCommas(cleanSelectorText)) {
      if (
        buttonHoverCompoundRegex.test(sel) &&
        !/:not\(\s*:disabled\s*\)/.test(sel)
      ) {
        violations.push({
          offset: headerOffset,
          selector: sel.replace(/\s+/g, " ").trim(),
        });
      }
    }
  });
  return violations;
}

const VALID_BUTTON_TYPES = new Set(["button", "submit", "reset"]);

export const noUnguardedButtonStateRule = {
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
