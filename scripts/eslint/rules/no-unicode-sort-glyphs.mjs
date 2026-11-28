import path from "node:path";

const unicodeSortGlyphRegex =
  /[\u2191\u2193\u2195\u21C5\u25B2\u25B3\u25B4\u25B5\u25BC\u25BD\u25BE\u25BF]/gu;

function maskCommentsPreservingLength(source) {
  return source
    .replace(/<!--[\s\S]*?-->/g, (m) => m.replace(/[^\r\n]/g, " "))
    .replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\r\n]/g, " "))
    .replace(
      /(^|[^:])\/\/.*$/gm,
      (m, p1) => p1 + " ".repeat(m.length - p1.length)
    );
}

export const noUnicodeSortGlyphsRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow Unicode sort glyphs (⇅, ▲, ▼, ↑, ↓, ↕) in ClinicalDataTable and shared UI primitives; use 16px Lucide vector icons with 1.75px stroke.",
      category: "Design System",
    },
    schema: [],
    messages: {
      noUnicodeSortGlyph:
        "Unicode sort glyph '{{glyph}}' ({{codePoint}}) is prohibited in shared data table primitives; use 16px lucide-vue-next vector icons with 1.75px stroke.",
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

    const isSharedUiOrDataTable =
      filename.includes("/packages/ui/") ||
      filename.startsWith("packages/ui/") ||
      /ClinicalDataTable/i.test(path.basename(filename));

    if (!isSharedUiOrDataTable) {
      return {};
    }

    return {
      Program() {
        const rawText = context.sourceCode.getText();
        const masked = maskCommentsPreservingLength(rawText);
        unicodeSortGlyphRegex.lastIndex = 0;
        let match;
        while ((match = unicodeSortGlyphRegex.exec(masked)) !== null) {
          const glyph = match[0];
          const start = context.sourceCode.getLocFromIndex(match.index);
          const end = context.sourceCode.getLocFromIndex(
            match.index + glyph.length
          );
          const codePoint =
            "U+" +
            glyph.codePointAt(0).toString(16).toUpperCase().padStart(4, "0");
          context.report({
            loc: { start, end },
            messageId: "noUnicodeSortGlyph",
            data: { glyph, codePoint },
          });
        }
      },
    };
  },
};
