import path from "node:path";
import {
  aiTokenRegex,
  isAllowedAiTokenFile,
  offsetToLoc,
  stripCssCommentsPreservingNewlines,
} from "../shared.mjs";

export const noMisusedAiTokensRule = {
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
