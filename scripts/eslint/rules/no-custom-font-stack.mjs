import {
  ALLOWED_TOKENIZED_FONT_FAMILIES,
  createTypographyScanVisitors,
  offsetToLoc,
} from "../shared.mjs";

export const noCustomFontStackRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow custom or raw font-family stacks outside packages/ui/tokens.css :root and disallow generic-first --font-mono stacks.",
      category: "Design System",
    },
    messages: {
      customFontStack:
        'Custom font stack "{{stack}}" is prohibited. Use var(--semantic-font-mono) or var(--semantic-font-family) from packages/ui/tokens.css.',
      genericFirstFontMono:
        '--font-mono in packages/ui/tokens.css must list platform monospace fonts (e.g. "JetBrains Mono", ui-monospace) before generic monospace.',
    },
  },
  create(context) {
    const cssFontFamilyRe = /font-family\s*:\s*([^;}]+)/gi;
    const inlineFontFamilyRe = /font-family\s*:\s*([^;}"'`]+)/gi;
    const jsFontFamilyRe = /fontFamily\s*:\s*(['"`])([^'"`]+)\1/g;

    function findDisallowedStack(rawStack) {
      const normalized = rawStack
        .replace(/\s*!important\s*$/i, "")
        .replace(/\s+/g, " ")
        .trim();
      if (!normalized || ALLOWED_TOKENIZED_FONT_FAMILIES.has(normalized)) {
        return null;
      }
      return normalized;
    }

    return createTypographyScanVisitors(context, {
      onCssBlock(cleanCss, baseOffset, sourceText, filename) {
        if (filename.endsWith("/packages/ui/tokens.css")) {
          for (const monoMatch of cleanCss.matchAll(
            /--font-mono\s*:\s*([^;}]+)/gi
          )) {
            const parts = monoMatch[1]
              .replace(/\s+/g, " ")
              .trim()
              .split(",")
              .map((s) =>
                s
                  .trim()
                  .replace(/^['"]|['"]$/g, "")
                  .toLowerCase()
              );
            const genericIdx = parts.indexOf("monospace");
            if (genericIdx === 0 || (genericIdx !== -1 && parts.length === 1)) {
              context.report({
                loc: offsetToLoc(sourceText, monoMatch.index),
                messageId: "genericFirstFontMono",
              });
            }
          }
        }
        for (const match of cleanCss.matchAll(cssFontFamilyRe)) {
          const bad = findDisallowedStack(match[1]);
          if (bad) {
            context.report({
              loc: offsetToLoc(sourceText, baseOffset + match.index),
              messageId: "customFontStack",
              data: { stack: bad },
            });
          }
        }
      },
      onVueStyleAttr(valueNode, rawAttr) {
        const inner =
          rawAttr.length >= 2 &&
          (rawAttr.startsWith('"') || rawAttr.startsWith("'"))
            ? rawAttr.slice(1, -1)
            : rawAttr;
        for (const [re, idx] of [
          [/font-family\s*:\s*([^;]+)/gi, 1],
          [jsFontFamilyRe, 2],
        ]) {
          for (const match of inner.matchAll(re)) {
            const bad = findDisallowedStack(match[idx]);
            if (bad) {
              context.report({
                node: valueNode,
                messageId: "customFontStack",
                data: { stack: bad },
              });
            }
          }
        }
      },
      onJsProgram(cleanJs, sourceText) {
        for (const [re, idx] of [
          [inlineFontFamilyRe, 1],
          [jsFontFamilyRe, 2],
        ]) {
          for (const match of cleanJs.matchAll(re)) {
            const bad = findDisallowedStack(match[idx]);
            if (bad) {
              context.report({
                loc: offsetToLoc(sourceText, match.index),
                messageId: "customFontStack",
                data: { stack: bad },
              });
            }
          }
        }
      },
    });
  },
};
