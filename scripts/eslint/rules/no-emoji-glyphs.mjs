const emojiGlyphScanRegex =
  /(?:(?![\u00A9\u00AE\u2122](?!\uFE0F))\p{Extended_Pictographic})|\p{Regional_Indicator}|[#*0-9]\uFE0F?\u20E3|\u20E3/gu;

export const noEmojiGlyphsRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow Unicode emoji, regional indicator flag, and keycap glyphs in clinical UI source and locale files.",
    },
    schema: [],
    messages: {
      noEmojiGlyph:
        "Unicode emoji glyph '{{glyph}}' ({{codePoint}}) is forbidden in clinical UI files; use ClinicalIcon or 1.75px Lucide vector icons with accessible labels (ADR-2679).",
    },
  },
  create(context) {
    return {
      Program() {
        const text = context.sourceCode.getText();
        emojiGlyphScanRegex.lastIndex = 0;
        let match;
        while ((match = emojiGlyphScanRegex.exec(text)) !== null) {
          const glyph = match[0];
          const loc = context.sourceCode.getLocFromIndex(match.index);
          const endLoc = context.sourceCode.getLocFromIndex(
            match.index + glyph.length
          );
          const codePoint = Array.from(glyph)
            .map(
              (ch) =>
                "U+" +
                ch.codePointAt(0).toString(16).toUpperCase().padStart(4, "0")
            )
            .join(" ");
          context.report({
            loc: { start: loc, end: endLoc },
            messageId: "noEmojiGlyph",
            data: { glyph, codePoint },
          });
        }
      },
    };
  },
};
