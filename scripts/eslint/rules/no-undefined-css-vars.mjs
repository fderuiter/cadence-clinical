import path from "node:path";
import {
  cssVarDefRegex,
  cssVarUseRegex,
  getBaseDefinedVars,
  isStyleOrColorVAttribute,
  offsetToLoc,
  stripCssCommentsPreservingNewlines,
} from "../shared.mjs";

export const noUndefinedCssVarsRule = {
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
