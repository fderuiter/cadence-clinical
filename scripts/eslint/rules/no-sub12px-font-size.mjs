import { createTypographyScanVisitors, offsetToLoc } from "../shared.mjs";

function detectSub12pxLength(rawValue) {
  const cleaned = rawValue.replace(/\s*!important\s*$/i, "").trim();
  for (const m of cleaned.matchAll(/([0-9]*\.?[0-9]+)\s*(px|rem|em|%)/gi)) {
    const num = parseFloat(m[1]);
    const unit = m[2].toLowerCase();
    if (
      (unit === "px" && num > 0 && num < 12) ||
      (unit === "rem" && num > 0 && num < 0.75) ||
      (unit === "em" && num > 0 && num < 0.75) ||
      (unit === "%" && num > 0 && num < 75)
    ) {
      return `${m[1]}${m[2]}`;
    }
  }
  return null;
}

export const noSub12pxFontSizeRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow font-size declarations smaller than 12px (0.75rem) across Vue templates, Vue <style> blocks, CSS stylesheets, and JS/TS modules.",
      category: "Design System",
    },
    messages: {
      sub12pxFontSize:
        'Font size "{{size}}" is smaller than the 12px clinical floor. Use var(--font-size-2xs) (12px) or larger typography tokens from packages/ui/tokens.css.',
    },
  },
  create(context) {
    const cssFontSizeRe =
      /(?:font-size|--(?:semantic-)?font-size-[a-zA-Z0-9_-]+)\s*:\s*([^;}"'`]+)/gi;
    const jsFontSizeRe = /fontSize\s*:\s*(['"`])([^'"`]+)\1/g;

    return createTypographyScanVisitors(context, {
      onCssBlock(cleanCss, baseOffset, sourceText) {
        for (const match of cleanCss.matchAll(cssFontSizeRe)) {
          const sub12 = detectSub12pxLength(match[1]);
          if (sub12) {
            context.report({
              loc: offsetToLoc(sourceText, baseOffset + match.index),
              messageId: "sub12pxFontSize",
              data: { size: sub12 },
            });
          }
        }
      },
      onVueStyleAttr(valueNode, attrText) {
        for (const [re, idx] of [
          [cssFontSizeRe, 1],
          [jsFontSizeRe, 2],
        ]) {
          for (const match of attrText.matchAll(re)) {
            const sub12 = detectSub12pxLength(match[idx]);
            if (sub12) {
              context.report({
                node: valueNode,
                messageId: "sub12pxFontSize",
                data: { size: sub12 },
              });
            }
          }
        }
      },
      onJsProgram(cleanJs, sourceText) {
        for (const [re, idx] of [
          [cssFontSizeRe, 1],
          [jsFontSizeRe, 2],
        ]) {
          for (const match of cleanJs.matchAll(re)) {
            const sub12 = detectSub12pxLength(match[idx]);
            if (sub12) {
              context.report({
                loc: offsetToLoc(sourceText, match.index),
                messageId: "sub12pxFontSize",
                data: { size: sub12 },
              });
            }
          }
        }
      },
    });
  },
};
