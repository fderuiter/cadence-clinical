function escapeHtml(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

export function slugifySopHeading(heading: string): string {
  const cleaned = String(heading || "")
    .trim()
    .replace(/^#+/, "")
    .replace(/&/g, " and ")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return cleaned || "section";
}

function formatInlineSopMarkdown(rawLine: string): string {
  if (!rawLine) return "";
  const parts = rawLine.split(/(`[^`]+`)/g);
  return parts
    .map((part) => {
      if (part.length >= 2 && part.startsWith("`") && part.endsWith("`")) {
        return `<code>${escapeHtml(part.slice(1, -1))}</code>`;
      }
      const escaped = escapeHtml(part);
      const withStrong = escaped.replace(
        /\*\*([^*]+)\*\*/g,
        "<strong>$1</strong>"
      );
      return withStrong.replace(/(^|[^*])\*([^*]+)\*/g, "$1<em>$2</em>");
    })
    .join("");
}

export function compileSopMarkdownToHtml(
  markdown: string,
  primaryAnchor?: string | null,
  headingAnchors?: Record<string, string>
): string {
  if (!markdown || !markdown.trim()) return "";
  const lines = markdown
    .replace(/\r\n/g, "\n")
    .replace(/\r/g, "\n")
    .split("\n");
  const usedIds = new Set<string>();
  const normalizedPrimary = (primaryAnchor || "").trim().replace(/^#+/, "");
  let primaryApplied = false;

  const allocateId = (desired: string): string => {
    const baseId = (desired || "").trim().replace(/^#+/, "") || "section";
    if (!usedIds.has(baseId)) {
      usedIds.add(baseId);
      return baseId;
    }
    let suffix = 2;
    while (usedIds.has(`${baseId}-${suffix}`)) {
      suffix++;
    }
    const unique = `${baseId}-${suffix}`;
    usedIds.add(unique);
    return unique;
  };

  let html = "";
  const paragraphLines: string[] = [];
  let listTag: "ol" | "ul" | null = null;
  const listItems: string[] = [];

  const flushParagraph = () => {
    if (paragraphLines.length === 0) return;
    html += `<p>${formatInlineSopMarkdown(paragraphLines.join(" "))}</p>`;
    paragraphLines.length = 0;
  };

  const flushList = () => {
    if (!listTag || listItems.length === 0) {
      listTag = null;
      listItems.length = 0;
      return;
    }
    html += `<${listTag}>${listItems
      .map((item) => `<li>${formatInlineSopMarkdown(item)}</li>`)
      .join("")}</${listTag}>`;
    listTag = null;
    listItems.length = 0;
  };

  for (const rawLine of lines) {
    const trimmed = rawLine.trim();
    if (!trimmed) {
      flushParagraph();
      flushList();
      continue;
    }

    const headingMatch = trimmed.match(/^(#{2,4})\s+(.+?)$/);
    if (headingMatch) {
      flushParagraph();
      flushList();
      const level = headingMatch[1].length;
      const headingText = headingMatch[2].trim();
      const mapped = headingAnchors?.[headingText];
      let desiredId: string;
      if (mapped) {
        desiredId = mapped;
        if (
          normalizedPrimary &&
          mapped.replace(/^#+/, "") === normalizedPrimary
        ) {
          primaryApplied = true;
        }
      } else if (level === 2 && !primaryApplied && normalizedPrimary) {
        desiredId = normalizedPrimary;
        primaryApplied = true;
      } else {
        desiredId = slugifySopHeading(headingText);
        if (normalizedPrimary && desiredId === normalizedPrimary) {
          primaryApplied = true;
        }
      }
      const uniqueId = allocateId(desiredId);
      html += `<h${level} id="${escapeHtml(uniqueId)}">${formatInlineSopMarkdown(headingText)}</h${level}>`;
      continue;
    }

    const olMatch = trimmed.match(/^\d+\.\s+(.+?)$/);
    if (olMatch) {
      flushParagraph();
      if (listTag !== "ol") {
        flushList();
        listTag = "ol";
      }
      listItems.push(olMatch[1].trim());
      continue;
    }

    const ulMatch = trimmed.match(/^[-*]\s+(.+?)$/);
    if (ulMatch) {
      flushParagraph();
      if (listTag !== "ul") {
        flushList();
        listTag = "ul";
      }
      listItems.push(ulMatch[1].trim());
      continue;
    }

    flushList();
    paragraphLines.push(trimmed);
  }

  flushParagraph();
  flushList();
  return html;
}
