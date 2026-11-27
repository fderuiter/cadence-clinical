export const SITE_ORIGIN = "https://fderuiter.github.io";
export const BLOG_FEED_PATH = "/feed.xml";
export const REPO_URL = "https://github.com/fderuiter/cadence-clinical";
export const REPO_BRANCH = "main";

export const EXCLUDED_PORTAL_DOCS = Object.freeze([
  "SDLC/03_API_Integration_Specification.md",
]);

export const STAGED_ROOT_DOCS_MAP = Object.freeze({
  "README.md": "/README.html",
  "ARCHITECTURE.md": "/ARCHITECTURE.html",
  "AGENTS.md": "/AGENTS.html",
  "CONTEXT.md": "/CONTEXT.html",
  "CONTRIBUTING.md": "/CONTRIBUTING.html",
  "SECURITY.md": "/SECURITY.html",
  "SUPPORT.md": "/SUPPORT.html",
  LICENSE: "/LICENSE.html",
});

/**
 * Prefixes internal site paths with the VitePress base URL without requiring vitepress runtime mocks in unit tests.
 *
 * @param {string} link
 * @param {string} [baseUrl]
 * @return {string}
 */
export function resolveBaseHref(
  link,
  baseUrl = import.meta.env?.BASE_URL || "/cadence-clinical/"
) {
  const raw = String(link ?? "");
  if (!raw || /^([a-z][a-z0-9+.-]*:|#)/i.test(raw) || !raw.startsWith("/")) {
    return raw;
  }
  const normalizedBase =
    `/${String(baseUrl || "/").replace(/^\/+|\/+$/g, "")}/`.replace(
      /^\/\/$/,
      "/"
    );
  if (normalizedBase !== "/" && raw.startsWith(normalizedBase)) {
    return raw;
  }
  return `${normalizedBase}${raw}`.replace(/\/+/g, "/");
}

/**
 * Resolves the anchor `target` attribute for normalized blog links (`_blank` for external URLs, `_self` for co-hosted SPAs).
 *
 * @param {string} link
 * @return {string | undefined}
 */
export function resolveBlogLinkTarget(link) {
  const value = String(link ?? "");
  if (/^https?:\/\//i.test(value)) return "_blank";
  if (
    value === "/demo/" ||
    value.startsWith("/demo/") ||
    value === "/subject-portal/" ||
    value.startsWith("/subject-portal/")
  ) {
    return "_self";
  }
  return undefined;
}

/**
 * Resolves the anchor `rel` attribute for normalized blog links.
 *
 * @param {string} link
 * @return {string | undefined}
 */
export function resolveBlogLinkRel(link) {
  return /^https?:\/\//i.test(String(link ?? "")) ? "noopener" : undefined;
}

/**
 * Escapes XML special characters for RSS/Atom text nodes and attributes.
 *
 * @param {unknown} value
 * @return {string}
 */
export function escapeXml(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&apos;");
}

function stripInlineYamlComment(raw) {
  let quote = null;
  for (let i = 0; i < raw.length; i += 1) {
    const ch = raw[i];
    if (quote) {
      if (ch === "\\" && quote === '"' && i + 1 < raw.length) {
        i += 1;
        continue;
      }
      if (ch === quote) {
        quote = null;
      }
      continue;
    }
    if (ch === '"' || ch === "'") {
      quote = ch;
      continue;
    }
    if (ch === "#" && (i === 0 || /\s/.test(raw[i - 1]))) {
      return raw.slice(0, i).trim();
    }
  }
  return raw.trim();
}

/**
 * Unquotes a YAML scalar string value.
 *
 * @param {string} raw
 * @return {string | boolean}
 */
function parseYamlScalar(raw) {
  const trimmed = stripInlineYamlComment(String(raw ?? ""));
  if (/^true$/i.test(trimmed)) return true;
  if (/^false$/i.test(trimmed)) return false;
  if (trimmed.length >= 2 && trimmed.startsWith('"') && trimmed.endsWith('"')) {
    return trimmed
      .slice(1, -1)
      .replace(/\\n/g, "\n")
      .replace(/\\r/g, "\r")
      .replace(/\\t/g, "\t")
      .replace(/\\"/g, '"')
      .replace(/\\\\/g, "\\");
  }
  if (trimmed.length >= 2 && trimmed.startsWith("'") && trimmed.endsWith("'")) {
    return trimmed.slice(1, -1).replace(/''/g, "'");
  }
  return trimmed;
}

function countLeadingSpaces(line) {
  let count = 0;
  while (count < line.length && line[count] === " ") {
    count += 1;
  }
  return count;
}

/**
 * Checks whether a YAML value header starts a folded (`>`, `>-`, `>+`) or literal (`|`, `|-`, `|+`) block scalar.
 *
 * @param {string} rawHeader
 * @return {{ style: ">" | "|", chomp: "clip" | "strip" | "keep", indentHint: number } | null}
 */
function parseBlockScalarHeader(rawHeader) {
  const cleaned = stripInlineYamlComment(rawHeader);
  const match = cleaned.match(/^([>|])([+-]?)(\d*)$/);
  if (!match) {
    const altMatch = cleaned.match(/^([>|])(\d+)([+-]?)$/);
    if (!altMatch) return null;
    return {
      style: altMatch[1],
      chomp:
        altMatch[3] === "-" ? "strip" : altMatch[3] === "+" ? "keep" : "clip",
      indentHint: Number.parseInt(altMatch[2], 10) || 0,
    };
  }
  return {
    style: match[1],
    chomp: match[2] === "-" ? "strip" : match[2] === "+" ? "keep" : "clip",
    indentHint: match[3] ? Number.parseInt(match[3], 10) : 0,
  };
}

/**
 * Consumes an indented YAML block scalar starting at `startIndex`.
 *
 * @param {string[]} lines
 * @param {number} startIndex
 * @param {number} parentIndent
 * @param {{ style: ">" | "|", chomp: "clip" | "strip" | "keep", indentHint: number }} descriptor
 * @return {{ value: string, nextIndex: number }}
 */
function consumeYamlBlockScalar(lines, startIndex, parentIndent, descriptor) {
  const rawBlockLines = [];
  let idx = startIndex;

  while (idx < lines.length) {
    const candidate = lines[idx];
    if (candidate.trim() === "") {
      rawBlockLines.push(candidate);
      idx += 1;
      continue;
    }
    const indent = countLeadingSpaces(candidate);
    if (indent <= parentIndent) {
      break;
    }
    rawBlockLines.push(candidate);
    idx += 1;
  }

  let blockIndent =
    descriptor.indentHint > 0 ? parentIndent + descriptor.indentHint : 0;
  if (blockIndent === 0) {
    for (const rawLine of rawBlockLines) {
      if (rawLine.trim() !== "") {
        const ind = countLeadingSpaces(rawLine);
        if (blockIndent === 0 || ind < blockIndent) {
          blockIndent = ind;
        }
      }
    }
  }

  if (blockIndent === 0) {
    return { value: "", nextIndex: idx };
  }

  let trailingEmptyCount = 0;
  while (
    rawBlockLines.length > 0 &&
    rawBlockLines[rawBlockLines.length - 1].trim() === ""
  ) {
    rawBlockLines.pop();
    trailingEmptyCount += 1;
  }

  if (rawBlockLines.length === 0) {
    return {
      value: descriptor.chomp === "keep" ? "\n".repeat(trailingEmptyCount) : "",
      nextIndex: idx,
    };
  }

  const stripped = rawBlockLines.map((rawLine) => {
    if (rawLine.trim() === "") return "";
    const spaces = Math.min(countLeadingSpaces(rawLine), blockIndent);
    return rawLine.slice(spaces);
  });

  let bodyText;
  if (descriptor.style === "|") {
    bodyText = stripped.join("\n");
  } else {
    let out = "";
    let pendingEmpty = 0;
    let prevWasNormal = false;
    let prevWasIndented = false;

    for (const contentLine of stripped) {
      if (contentLine === "") {
        pendingEmpty += 1;
        continue;
      }
      const isMoreIndented = contentLine.startsWith(" ");
      if (out === "") {
        if (pendingEmpty > 0) {
          out += "\n".repeat(pendingEmpty);
          pendingEmpty = 0;
        }
        out += contentLine;
      } else if (pendingEmpty > 0) {
        const extraBreaks =
          prevWasIndented || isMoreIndented ? pendingEmpty + 1 : pendingEmpty;
        out += "\n".repeat(extraBreaks) + contentLine;
        pendingEmpty = 0;
      } else if (prevWasNormal && !isMoreIndented) {
        out += ` ${contentLine}`;
      } else {
        out += `\n${contentLine}`;
      }
      prevWasNormal = !isMoreIndented;
      prevWasIndented = isMoreIndented;
    }
    bodyText = out;
  }

  if (descriptor.chomp === "strip") {
    return { value: bodyText, nextIndex: idx };
  }
  if (descriptor.chomp === "keep") {
    return {
      value: `${bodyText}\n${"\n".repeat(trailingEmptyCount)}`,
      nextIndex: idx,
    };
  }
  return { value: bodyText, nextIndex: idx };
}

/**
 * Parses an inline YAML array such as `["kernel", "architecture"]`.
 *
 * @param {string} raw
 * @return {string[]}
 */
function parseInlineArray(raw) {
  const cleaned = stripInlineYamlComment(raw);
  const inner = cleaned.slice(1, -1).trim();
  if (!inner) return [];

  const items = [];
  let current = "";
  let quote = null;

  for (let i = 0; i < inner.length; i += 1) {
    const ch = inner[i];
    if (quote) {
      if (ch === "\\" && quote === '"' && i + 1 < inner.length) {
        current += ch + inner[i + 1];
        i += 1;
        continue;
      }
      if (ch === quote) {
        quote = null;
      }
      current += ch;
      continue;
    }

    if (ch === '"' || ch === "'") {
      quote = ch;
      current += ch;
      continue;
    }

    if (ch === ",") {
      const parsed = String(parseYamlScalar(current)).trim();
      if (parsed) items.push(parsed);
      current = "";
      continue;
    }

    current += ch;
  }

  const tail = String(parseYamlScalar(current)).trim();
  if (tail) items.push(tail);
  return items;
}

/**
 * Parses YAML frontmatter and Markdown body from a blog document without external dependencies.
 *
 * @param {string} rawMarkdown
 * @return {{ frontmatter: Record<string, any>, body: string }}
 */
export function parseBlogFrontmatter(rawMarkdown) {
  const normalized = String(rawMarkdown ?? "")
    .replace(/^\uFEFF/, "")
    .replace(/\r\n/g, "\n");

  if (!normalized.startsWith("---\n")) {
    return { frontmatter: {}, body: normalized };
  }

  const endMarkerMatch = normalized.slice(4).match(/\n(?:---|\.\.\.)(?:\n|$)/);
  if (!endMarkerMatch || endMarkerMatch.index === undefined) {
    return { frontmatter: {}, body: normalized };
  }

  const yamlBlock = normalized.slice(4, 4 + endMarkerMatch.index);
  const body = normalized.slice(
    4 + endMarkerMatch.index + endMarkerMatch[0].length
  );

  const frontmatter = {};
  const lines = yamlBlock.split("\n");
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) {
      i += 1;
      continue;
    }

    const keyMatch = line.match(/^([A-Za-z0-9_-]+):\s*(.*)$/);
    if (!keyMatch) {
      i += 1;
      continue;
    }

    const key = keyMatch[1];
    const rest = keyMatch[2].trim();
    const blockHeader = parseBlockScalarHeader(rest);

    if (blockHeader) {
      const consumed = consumeYamlBlockScalar(lines, i + 1, 0, blockHeader);
      frontmatter[key] = consumed.value;
      i = consumed.nextIndex;
      continue;
    }

    const uncommentedRest = stripInlineYamlComment(rest);
    if (uncommentedRest.startsWith("[") && uncommentedRest.endsWith("]")) {
      frontmatter[key] = parseInlineArray(uncommentedRest);
      i += 1;
      continue;
    }

    if (uncommentedRest === "") {
      const list = [];
      i += 1;
      while (i < lines.length) {
        const itemLine = lines[i];
        const itemTrimmed = itemLine.trim();
        if (!itemTrimmed || itemTrimmed.startsWith("#")) {
          i += 1;
          continue;
        }
        if (/^[A-Za-z0-9_-]+:\s*/.test(itemLine)) {
          break;
        }

        const listMatch = itemLine.match(/^(\s*)-\s*(.*)$/);
        if (!listMatch) {
          i += 1;
          continue;
        }

        const dashIndent = listMatch[1].length;
        const firstEntry = listMatch[2].trim();
        const itemBlockHeader = parseBlockScalarHeader(firstEntry);
        if (itemBlockHeader) {
          const consumed = consumeYamlBlockScalar(
            lines,
            i + 1,
            dashIndent,
            itemBlockHeader
          );
          list.push(consumed.value);
          i = consumed.nextIndex;
          continue;
        }

        const objectFieldMatch = firstEntry.match(/^([A-Za-z0-9_-]+):\s*(.*)$/);
        if (objectFieldMatch) {
          const firstFieldKey = objectFieldMatch[1];
          const firstFieldRaw = objectFieldMatch[2].trim();
          const firstFieldIndent = itemLine.indexOf(firstFieldKey);
          const firstBlockHeader = parseBlockScalarHeader(firstFieldRaw);
          const obj = {};
          if (firstBlockHeader) {
            const consumed = consumeYamlBlockScalar(
              lines,
              i + 1,
              firstFieldIndent,
              firstBlockHeader
            );
            obj[firstFieldKey] = consumed.value;
            i = consumed.nextIndex;
          } else {
            obj[firstFieldKey] = parseYamlScalar(firstFieldRaw);
            i += 1;
          }

          while (i < lines.length) {
            const subLine = lines[i];
            const subTrimmed = subLine.trim();
            if (!subTrimmed || subTrimmed.startsWith("#")) {
              i += 1;
              continue;
            }
            if (
              /^[A-Za-z0-9_-]+:\s*/.test(subLine) ||
              /^\s*-\s+/.test(subLine)
            ) {
              break;
            }
            const subMatch = subLine.match(/^(\s+)([A-Za-z0-9_-]+):\s*(.*)$/);
            if (subMatch) {
              const subIndent = subMatch[1].length;
              const subKey = subMatch[2];
              const subRaw = subMatch[3].trim();
              const subBlockHeader = parseBlockScalarHeader(subRaw);
              if (subBlockHeader) {
                const consumed = consumeYamlBlockScalar(
                  lines,
                  i + 1,
                  subIndent,
                  subBlockHeader
                );
                obj[subKey] = consumed.value;
                i = consumed.nextIndex;
                continue;
              }
              obj[subKey] = parseYamlScalar(subRaw);
            }
            i += 1;
          }
          list.push(obj);
        } else {
          list.push(parseYamlScalar(firstEntry));
          i += 1;
        }
      }
      frontmatter[key] = list;
      continue;
    }

    frontmatter[key] = parseYamlScalar(rest);
    i += 1;
  }

  return { frontmatter, body };
}

function splitHrefSuffix(rawHref) {
  const hashIndex = rawHref.indexOf("#");
  const withoutHash = hashIndex === -1 ? rawHref : rawHref.slice(0, hashIndex);
  const hashPart = hashIndex === -1 ? "" : rawHref.slice(hashIndex);
  const queryIndex = withoutHash.indexOf("?");
  const pathPart =
    queryIndex === -1 ? withoutHash : withoutHash.slice(0, queryIndex);
  const queryPart = queryIndex === -1 ? "" : withoutHash.slice(queryIndex);
  return { pathPart, suffix: `${queryPart}${hashPart}` };
}

function formatPortalPath(portalRoute, suffix) {
  let cleaned = portalRoute.startsWith("/") ? portalRoute : `/${portalRoute}`;
  cleaned = cleaned.replace(/\/+/g, "/");

  if (cleaned === "/" || cleaned.endsWith("/")) {
    return `${cleaned}${suffix}`;
  }

  if (cleaned === BLOG_FEED_PATH || /\.xml$/i.test(cleaned)) {
    return `${cleaned}${suffix}`;
  }

  const candidateDocRel = `${cleaned
    .replace(/^\/+/, "")
    .replace(/\.(?:md|html)$/i, "")}.md`;
  const matchedExcluded = EXCLUDED_PORTAL_DOCS.find(
    (excluded) => excluded.toLowerCase() === candidateDocRel.toLowerCase()
  );
  if (matchedExcluded) {
    return `${REPO_URL}/blob/${REPO_BRANCH}/docs/${matchedExcluded}${suffix}`;
  }

  if (/\/index(?:\.(?:md|html))?$/i.test(cleaned)) {
    const dirPath = cleaned.replace(/\/index(?:\.(?:md|html))?$/i, "/");
    return `${dirPath}${suffix}`;
  }

  const lastSegment = cleaned.split("/").pop() ?? "";
  const dotIndex = lastSegment.lastIndexOf(".");
  if (dotIndex > 0) {
    const ext = lastSegment.slice(dotIndex).toLowerCase();
    if (ext !== ".md" && ext !== ".html") {
      const repoDocsPath = `docs${cleaned}`;
      return `${REPO_URL}/blob/${REPO_BRANCH}/${repoDocsPath}${suffix}`;
    }
  }

  cleaned = cleaned.replace(/\.(?:md|html)$/i, "");
  return `${cleaned}.html${suffix}`;
}

/**
 * Normalizes a documentation or external link (authored relative to `docs/blog/` or site root)
 * for rendering in VitePress components and RSS feeds without Node-only imports.
 *
 * @param {string} rawLink
 * @return {string}
 */
export function normalizeSiteLink(rawLink) {
  const link = String(rawLink ?? "").trim();
  if (!link) return "/";
  if (/^([a-z][a-z0-9+.-]*:|#)/i.test(link)) return link;

  const { pathPart, suffix } = splitHrefSuffix(link);
  if (!pathPart) {
    return suffix || "/";
  }

  if (pathPart.startsWith("/")) {
    const segments = [];
    const trailingSlash = pathPart.length > 1 && pathPart.endsWith("/");
    for (const part of pathPart.split("/")) {
      if (!part || part === ".") continue;
      if (part === "..") {
        segments.pop();
      } else {
        segments.push(part);
      }
    }
    if (segments[0] === "docs") {
      segments.shift();
    }
    const rebuilt =
      segments.length === 0
        ? "/"
        : `/${segments.join("/")}${trailingSlash ? "/" : ""}`;
    return formatPortalPath(rebuilt, suffix);
  }

  if (pathPart.startsWith("docs/")) {
    const subPath = pathPart.slice("docs".length);
    return formatPortalPath(subPath, suffix);
  }

  const trailingSlash = pathPart.endsWith("/");
  const stack = ["docs", "blog"];
  for (const part of pathPart.split("/")) {
    if (!part || part === ".") continue;
    if (part === "..") {
      if (stack.length > 0) {
        stack.pop();
      }
    } else {
      stack.push(part);
    }
  }

  const repoRel = stack.join("/");
  if (!repoRel) {
    return `${REPO_URL}${suffix}`;
  }

  if (STAGED_ROOT_DOCS_MAP[repoRel]) {
    return `${STAGED_ROOT_DOCS_MAP[repoRel]}${suffix}`;
  }

  if (repoRel === "docs") {
    return `/${suffix}`;
  }

  if (repoRel.startsWith("docs/")) {
    const portalSubPath = `/${repoRel.slice("docs/".length)}${trailingSlash ? "/" : ""}`;
    return formatPortalPath(portalSubPath, suffix);
  }

  if (trailingSlash) {
    return `${REPO_URL}/tree/${REPO_BRANCH}/${repoRel}${suffix}`;
  }

  return `${REPO_URL}/blob/${REPO_BRANCH}/${repoRel}${suffix}`;
}

/**
 * Normalizes related decision entries from frontmatter.
 *
 * @param {unknown} rawRelated
 * @return {Array<{ text: string, link: string }>}
 */
export function normalizeRelatedLinks(rawRelated) {
  if (!Array.isArray(rawRelated)) return [];
  const items = [];

  for (const entry of rawRelated) {
    if (typeof entry === "string" && entry.trim()) {
      const normalized = normalizeSiteLink(entry);
      items.push({ text: entry.trim(), link: normalized });
    } else if (entry && typeof entry === "object") {
      const link = String(entry.link ?? entry.href ?? "").trim();
      const text = String(entry.text ?? entry.title ?? link).trim();
      if (link && text) {
        items.push({ text, link: normalizeSiteLink(link) });
      }
    }
  }

  return items;
}

/**
 * Normalizes an ISO date value (`YYYY-MM-DD`, ISO-8601 timestamp, or `Date` instance) to `YYYY-MM-DD`.
 *
 * @param {unknown} value
 * @param {string} fallbackSlug
 * @return {string}
 */
export function normalizeIsoDate(value, fallbackSlug = "") {
  if (value instanceof Date && !Number.isNaN(value.getTime())) {
    return value.toISOString().slice(0, 10);
  }
  const str = String(value ?? "").trim();
  const isoMatch = str.match(/^(\d{4}-\d{2}-\d{2})/);
  if (isoMatch) return isoMatch[1];

  const slugMatch = String(fallbackSlug).match(/^(\d{4}-\d{2}-\d{2})/);
  return slugMatch ? slugMatch[1] : "1970-01-01";
}

/**
 * Preserves full ISO-8601 timestamps (including time-of-day and offset) when supplied,
 * falling back to `<YYYY-MM-DD>T00:00:00Z` for date-only values.
 *
 * @param {unknown} value
 * @param {string} fallbackDate
 * @return {string}
 */
export function normalizeIsoTimestamp(value, fallbackDate = "1970-01-01") {
  if (value instanceof Date && !Number.isNaN(value.getTime())) {
    return value.toISOString();
  }
  const str = String(value ?? "").trim();
  if (/^\d{4}-\d{2}-\d{2}$/.test(str)) {
    return `${str}T00:00:00Z`;
  }
  if (/^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/.test(str)) {
    const withT = str.replace(" ", "T");
    const withZone = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(withT)
      ? withT
      : `${withT}Z`;
    const parsed = new Date(withZone);
    if (!Number.isNaN(parsed.getTime())) {
      return withZone;
    }
  }
  const baseDate = normalizeIsoDate(str, fallbackDate);
  return `${baseDate}T00:00:00Z`;
}

function parseTimestampDate(value, fallbackDate = "1970-01-01") {
  const iso = normalizeIsoTimestamp(value, fallbackDate);
  const parsed = new Date(iso);
  if (!Number.isNaN(parsed.getTime())) {
    return parsed;
  }
  return new Date(`${normalizeIsoDate(fallbackDate)}T00:00:00Z`);
}

/**
 * Formats an ISO `YYYY-MM-DD` date string into a human-readable UTC date.
 *
 * @param {string} isoDate
 * @return {string}
 */
export function formatDisplayDate(isoDate) {
  const normalized = normalizeIsoDate(isoDate);
  const parsed = new Date(`${normalized}T00:00:00Z`);
  if (Number.isNaN(parsed.getTime())) return normalized;
  return parsed.toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
    timeZone: "UTC",
  });
}

/**
 * Normalizes a raw blog post record (from VitePress `createContentLoader` or filesystem scan).
 * Returns `null` when the entry is the blog index or a draft.
 *
 * @param {{ url: string, frontmatter?: Record<string, any>, body?: string, slug?: string }} entry
 * @return {{
 *   title: string,
 *   slug: string,
 *   url: string,
 *   date: string,
 *   publishedAt: string,
 *   displayDate: string,
 *   authors: string[],
 *   summary: string,
 *   body: string,
 *   tags: string[],
 *   related: Array<{ text: string, link: string }>,
 *   draft: boolean
 * } | null}
 */
export function normalizeBlogPost(entry) {
  const url = normalizeSiteLink(entry?.url ?? "");
  if (url === "/blog/" || url.endsWith("/blog/index.html")) {
    return null;
  }

  const frontmatter = entry?.frontmatter ?? {};
  if (
    frontmatter.draft === true ||
    /^true$/i.test(String(frontmatter.draft ?? "").trim())
  ) {
    return null;
  }

  const lastSegment = url.split("/").filter(Boolean).pop() ?? "";
  const slug = entry?.slug || lastSegment.replace(/\.(?:html|md)$/i, "");

  let fallbackTitle = "";
  let fallbackSummary = "";
  const rawBody = typeof entry?.body === "string" ? entry.body : "";
  if (rawBody) {
    const titleMatch = rawBody.match(/^#\s+(.+)$/m);
    if (titleMatch) fallbackTitle = titleMatch[1].trim();
    const lines = rawBody.split("\n");
    for (const line of lines) {
      const trimmed = line.trim();
      if (
        trimmed &&
        !trimmed.startsWith("#") &&
        !trimmed.startsWith(">") &&
        !trimmed.startsWith("|") &&
        !trimmed.startsWith("```") &&
        !trimmed.startsWith("-") &&
        !trimmed.startsWith("<")
      ) {
        fallbackSummary = trimmed.replace(/\[([^\]]+)\]\([^)]+\)/g, "$1");
        break;
      }
    }
  }

  const title = String(frontmatter.title || fallbackTitle || slug)
    .replace(/\s+/g, " ")
    .trim();
  const date = normalizeIsoDate(frontmatter.date, slug);
  const publishedAt = normalizeIsoTimestamp(frontmatter.date, date);
  const rawAuthors = Array.isArray(frontmatter.authors)
    ? frontmatter.authors
    : frontmatter.author
      ? [frontmatter.author]
      : ["@fderuiter"];
  const authors = rawAuthors.map((a) => String(a).trim()).filter(Boolean);

  const summary = String(
    frontmatter.summary || frontmatter.description || fallbackSummary || title
  )
    .replace(/\s+/g, " ")
    .trim();

  const rawTags = Array.isArray(frontmatter.tags)
    ? frontmatter.tags
    : typeof frontmatter.tags === "string"
      ? frontmatter.tags.split(",")
      : [];
  const seenTags = new Set();
  const tags = [];
  for (const rawTag of rawTags) {
    const tag = String(rawTag).trim().toLowerCase();
    if (tag && !seenTags.has(tag)) {
      seenTags.add(tag);
      tags.push(tag);
    }
  }

  const related = normalizeRelatedLinks(frontmatter.related);

  return {
    title,
    slug,
    url,
    date,
    publishedAt,
    displayDate: formatDisplayDate(date),
    authors: authors.length > 0 ? authors : ["@fderuiter"],
    summary,
    body: rawBody,
    tags,
    related,
    draft: false,
  };
}

/**
 * Sorts normalized blog posts in reverse-chronological order.
 *
 * @param {Array<{ date: string, publishedAt?: string, slug?: string, url: string }>} posts
 * @return {Array<any>}
 */
export function sortBlogPostsReverseChronological(posts) {
  return [...posts].sort((a, b) => {
    const dateCmp = String(b.date ?? "").localeCompare(String(a.date ?? ""));
    if (dateCmp !== 0) return dateCmp;

    const aTime = parseTimestampDate(a.publishedAt || a.date, a.date).getTime();
    const bTime = parseTimestampDate(b.publishedAt || b.date, b.date).getTime();
    if (bTime !== aTime) return bTime - aTime;

    return String(b.slug || b.url).localeCompare(String(a.slug || a.url));
  });
}

/**
 * Transforms VitePress `createContentLoader` records into sorted published blog posts.
 *
 * @param {Array<{ url: string, frontmatter: Record<string, any> }>} rawItems
 * @return {Array<any>}
 */
export function transformContentLoaderPosts(rawItems) {
  const published = (rawItems ?? [])
    .map((item) => normalizeBlogPost(item))
    .filter(Boolean);
  return sortBlogPostsReverseChronological(published);
}

/**
 * Filters published blog posts by topic tag (`"all"` or empty returns all posts).
 *
 * @param {Array<{ tags?: string[] }>} posts
 * @param {string} activeTag
 * @return {Array<any>}
 */
export function filterBlogPostsByTag(posts, activeTag) {
  const normalizedTag = String(activeTag ?? "")
    .trim()
    .toLowerCase();
  if (!normalizedTag || normalizedTag === "all") {
    return posts ?? [];
  }
  return (posts ?? []).filter((post) =>
    (post.tags ?? []).some((tag) => tag.toLowerCase() === normalizedTag)
  );
}

function escapeHtml(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function resolveAbsoluteFeedUrl(
  rawUrl,
  { siteOrigin, normalizedBase, itemUrl }
) {
  const trimmed = String(rawUrl ?? "").trim();
  if (!trimmed) return itemUrl;
  if (/^[a-z][a-z0-9+.-]*:/i.test(trimmed)) return trimmed;
  if (trimmed.startsWith("#")) return `${itemUrl}${trimmed}`;

  if (normalizedBase !== "/" && trimmed.startsWith(normalizedBase)) {
    return `${siteOrigin}${trimmed}`;
  }

  const normalized = normalizeSiteLink(trimmed);
  if (/^[a-z][a-z0-9+.-]*:/i.test(normalized)) {
    return normalized;
  }

  const cleanRoute = normalized.replace(/^\/+/, "");
  return `${siteOrigin}${normalizedBase}${cleanRoute}`;
}

/**
 * Rewrites relative or root-relative `href` and `src` attributes in HTML into absolute URLs.
 *
 * @param {string} html
 * @param {{ siteOrigin: string, normalizedBase: string, itemUrl: string }} context
 * @return {string}
 */
export function absolutizeFeedHtmlUrls(html, context) {
  return String(html ?? "").replace(
    /\b(href|src)=(["'])([^"']+)\2/gi,
    (_match, attr, quote, url) => {
      const resolved = resolveAbsoluteFeedUrl(url, context);
      return `${attr}=${quote}${escapeHtml(resolved)}${quote}`;
    }
  );
}

function renderInlineMarkdown(text, urlContext) {
  const codePlaceholders = [];
  const withCodeTokens = String(text ?? "").replace(
    /`([^`\n]+)`/g,
    (_match, code) => {
      const idx = codePlaceholders.length;
      codePlaceholders.push(`<code>${escapeHtml(code)}</code>`);
      return `\u0000CODE${idx}\u0000`;
    }
  );

  const linkPlaceholders = [];
  const withMediaAndLinks = withCodeTokens
    .replace(/!\[([^\]]*)\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g, (_m, alt, src) => {
      const idx = linkPlaceholders.length;
      const absSrc = resolveAbsoluteFeedUrl(src, urlContext);
      linkPlaceholders.push(
        `<img src="${escapeHtml(absSrc)}" alt="${escapeHtml(alt)}" />`
      );
      return `\u0000LINK${idx}\u0000`;
    })
    .replace(/\[([^\]]+)\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g, (_m, label, href) => {
      const idx = linkPlaceholders.length;
      const absHref = resolveAbsoluteFeedUrl(href, urlContext);
      const renderedLabel = renderInlineMarkdown(label, urlContext);
      linkPlaceholders.push(
        `<a href="${escapeHtml(absHref)}">${renderedLabel}</a>`
      );
      return `\u0000LINK${idx}\u0000`;
    });

  let formatted = escapeHtml(withMediaAndLinks)
    .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
    .replace(/__([^_]+)__/g, "<strong>$1</strong>")
    .replace(/(^|[^*])\*([^*\n]+)\*/g, "$1<em>$2</em>");

  formatted = formatted.replace(
    /\u0000LINK(\d+)\u0000/g,
    (_m, idx) => linkPlaceholders[Number(idx)] ?? ""
  );
  formatted = formatted.replace(
    /\u0000CODE(\d+)\u0000/g,
    (_m, idx) => codePlaceholders[Number(idx)] ?? ""
  );
  return formatted;
}

function consumeMarkdownList(lines, startIndex, itemRegex, tag, urlContext) {
  let i = startIndex;
  const items = [];
  while (i < lines.length) {
    const itemMatch = lines[i].match(itemRegex);
    if (!itemMatch) break;
    const itemParts = [itemMatch[1].trim()];
    i += 1;
    while (
      i < lines.length &&
      lines[i].trim() !== "" &&
      /^\s{2,}\S/.test(lines[i]) &&
      !/^\s*[-*+]\s+/.test(lines[i]) &&
      !/^\s*\d+\.\s+/.test(lines[i])
    ) {
      itemParts.push(lines[i].trim());
      i += 1;
    }
    items.push(
      `<li>${renderInlineMarkdown(itemParts.join(" "), urlContext)}</li>`
    );
  }
  return {
    html: `<${tag}>${items.join("")}</${tag}>`,
    nextIndex: i,
  };
}

/**
 * Renders Markdown post body into clean semantic HTML with absolute links for RSS `<content:encoded>`.
 *
 * @param {string} markdown
 * @param {{ siteOrigin: string, normalizedBase: string, itemUrl: string }} urlContext
 * @return {string}
 */
export function renderBlogMarkdownToHtml(markdown, urlContext) {
  const normalized = String(markdown ?? "")
    .replace(/\r\n/g, "\n")
    .trim();
  if (!normalized) return "";

  const lines = normalized.split("\n");
  const blocks = [];
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];
    const trimmed = line.trim();

    if (!trimmed || /^<Blog[A-Za-z0-9_]*\s*\/>$/.test(trimmed)) {
      i += 1;
      continue;
    }

    const fenceMatch = trimmed.match(/^(`{3,}|~{3,})(.*)$/);
    if (fenceMatch) {
      const marker = fenceMatch[1];
      const lang = fenceMatch[2].trim().split(/\s+/)[0] || "";
      const codeLines = [];
      i += 1;
      while (i < lines.length) {
        const closeTrimmed = lines[i].trim();
        if (
          closeTrimmed.startsWith(marker[0].repeat(marker.length)) &&
          closeTrimmed.replace(new RegExp(`^${marker[0]}+`), "").trim() === ""
        ) {
          i += 1;
          break;
        }
        codeLines.push(lines[i]);
        i += 1;
      }
      const classAttr = lang ? ` class="language-${escapeHtml(lang)}"` : "";
      blocks.push(
        `<pre><code${classAttr}>${escapeHtml(codeLines.join("\n"))}</code></pre>`
      );
      continue;
    }

    if (/^(?:-{3,}|\*{3,}|_{3,})$/.test(trimmed)) {
      blocks.push("<hr />");
      i += 1;
      continue;
    }

    const headingMatch = trimmed.match(/^(#{1,6})\s+(.+?)\s*$/);
    if (headingMatch) {
      const level = headingMatch[1].length;
      const rawHeading = headingMatch[2]
        .replace(/\s+#+\s*$/, "")
        .replace(/\s*\{#[^}\s]+\}\s*$/, "");
      blocks.push(
        `<h${level}>${renderInlineMarkdown(rawHeading, urlContext)}</h${level}>`
      );
      i += 1;
      continue;
    }

    if (trimmed.startsWith(">")) {
      const quoteLines = [];
      while (i < lines.length && lines[i].trim().startsWith(">")) {
        quoteLines.push(lines[i].trim().replace(/^>\s?/, ""));
        i += 1;
      }
      blocks.push(
        `<blockquote>${renderBlogMarkdownToHtml(quoteLines.join("\n"), urlContext)}</blockquote>`
      );
      continue;
    }

    if (
      trimmed.startsWith("|") &&
      trimmed.endsWith("|") &&
      i + 1 < lines.length &&
      /^\|\s*:?-+/.test(lines[i + 1].trim())
    ) {
      const splitCells = (row) =>
        row
          .trim()
          .slice(1, -1)
          .split(/(?<!\\)\|/)
          .map((c) => c.replace(/\\\|/g, "|").trim());
      const headers = splitCells(trimmed);
      i += 2;
      const bodyRows = [];
      while (
        i < lines.length &&
        lines[i].trim().startsWith("|") &&
        lines[i].trim().endsWith("|")
      ) {
        bodyRows.push(splitCells(lines[i]));
        i += 1;
      }
      const thead = `<thead><tr>${headers
        .map((h) => `<th>${renderInlineMarkdown(h, urlContext)}</th>`)
        .join("")}</tr></thead>`;
      const tbody =
        bodyRows.length > 0
          ? `<tbody>${bodyRows
              .map(
                (r) =>
                  `<tr>${r
                    .map(
                      (c) => `<td>${renderInlineMarkdown(c, urlContext)}</td>`
                    )
                    .join("")}</tr>`
              )
              .join("")}</tbody>`
          : "";
      blocks.push(`<table>${thead}${tbody}</table>`);
      continue;
    }

    const ulListRegex = /^\s*[-*+]\s+(.+)$/;
    const olListRegex = /^\s*\d+\.\s+(.+)$/;
    if (ulListRegex.test(line)) {
      const parsedList = consumeMarkdownList(
        lines,
        i,
        ulListRegex,
        "ul",
        urlContext
      );
      blocks.push(parsedList.html);
      i = parsedList.nextIndex;
      continue;
    }

    if (olListRegex.test(line)) {
      const parsedList = consumeMarkdownList(
        lines,
        i,
        olListRegex,
        "ol",
        urlContext
      );
      blocks.push(parsedList.html);
      i = parsedList.nextIndex;
      continue;
    }

    const paraLines = [];
    while (i < lines.length) {
      const pTrim = lines[i].trim();
      if (
        !pTrim ||
        pTrim.startsWith("#") ||
        pTrim.startsWith(">") ||
        pTrim.startsWith("```") ||
        pTrim.startsWith("~~~") ||
        /^(?:-{3,}|\*{3,}|_{3,})$/.test(pTrim) ||
        /^\s*[-*+]\s+/.test(lines[i]) ||
        /^\s*\d+\.\s+/.test(lines[i]) ||
        (pTrim.startsWith("|") && pTrim.endsWith("|"))
      ) {
        break;
      }
      paraLines.push(pTrim);
      i += 1;
    }
    if (paraLines.length > 0) {
      blocks.push(
        `<p>${renderInlineMarkdown(paraLines.join(" "), urlContext)}</p>`
      );
    } else {
      i += 1;
    }
  }

  return blocks.join("\n");
}

function wrapCdata(html) {
  const safe = String(html ?? "").replace(/\]\]>/g, "]]]]><![CDATA[>");
  return `<![CDATA[${safe}]]>`;
}

/**
 * Renders a deterministic RSS 2.0 XML document for published blog posts.
 *
 * @param {Array<any>} posts
 * @param {{
 *   siteTitle?: string,
 *   siteDescription?: string,
 *   siteOrigin?: string,
 *   base?: string
 * }} options
 * @return {string}
 */
export function buildRssFeedXml(posts, options = {}) {
  const siteTitle = options.siteTitle || "Cadence Clinical Engineering Blog";
  const siteDescription =
    options.siteDescription ||
    "Curated architectural milestones and platform engineering updates from Cadence Clinical Research Software.";
  const configuredOrigin =
    options.siteOrigin || import.meta.env?.VITEPRESS_SITE_ORIGIN || SITE_ORIGIN;
  const siteOrigin = String(configuredOrigin).trim().replace(/\/+$/, "");
  const rawBase = options.base || "/cadence-clinical/";
  const normalizedBase = `/${rawBase.replace(/^\/+|\/+$/g, "")}/`.replace(
    /^\/\/$/,
    "/"
  );

  const published = sortBlogPostsReverseChronological(
    (posts ?? []).filter((post) => post && !post.draft)
  );

  const toAbsoluteUrl = (routePath) => {
    const cleanRoute = String(routePath).replace(/^\/+/, "");
    return `${siteOrigin}${normalizedBase}${cleanRoute}`;
  };

  const blogUrl = toAbsoluteUrl("blog/");
  const feedUrl = toAbsoluteUrl("feed.xml");

  let latestBuildInstant = new Date("2026-11-20T00:00:00Z");
  if (published.length > 0) {
    latestBuildInstant = published
      .map((post) =>
        parseTimestampDate(post.publishedAt || post.date, post.date)
      )
      .reduce((max, cur) => (cur.getTime() > max.getTime() ? cur : max));
  }
  const lastBuildDate = latestBuildInstant.toUTCString();

  const itemsXml = published
    .map((post) => {
      const itemUrl = toAbsoluteUrl(post.url);
      const pubDate = parseTimestampDate(
        post.publishedAt || post.date,
        post.date
      ).toUTCString();
      const urlContext = { siteOrigin, normalizedBase, itemUrl };

      let renderedHtml;
      if (typeof post.contentHtml === "string" && post.contentHtml.trim()) {
        renderedHtml = absolutizeFeedHtmlUrls(post.contentHtml, urlContext);
      } else if (typeof post.html === "string" && post.html.trim()) {
        renderedHtml = absolutizeFeedHtmlUrls(post.html, urlContext);
      } else if (typeof post.body === "string" && post.body.trim()) {
        renderedHtml = renderBlogMarkdownToHtml(post.body, urlContext);
      } else {
        renderedHtml = `<p>${escapeHtml(post.summary || post.title)}</p>`;
      }

      const categoriesXml = (post.tags ?? [])
        .map((tag) => `      <category>${escapeXml(tag)}</category>`)
        .join("\n");
      const creatorsXml = (post.authors ?? [])
        .map((author) => `      <dc:creator>${escapeXml(author)}</dc:creator>`)
        .join("\n");

      const optionalLines = [creatorsXml, categoriesXml]
        .filter(Boolean)
        .join("\n");

      return [
        "    <item>",
        `      <title>${escapeXml(post.title)}</title>`,
        `      <link>${escapeXml(itemUrl)}</link>`,
        `      <guid isPermaLink="true">${escapeXml(itemUrl)}</guid>`,
        `      <pubDate>${escapeXml(pubDate)}</pubDate>`,
        `      <description>${escapeXml(post.summary)}</description>`,
        `      <content:encoded>${wrapCdata(renderedHtml)}</content:encoded>`,
        optionalLines,
        "    </item>",
      ]
        .filter(Boolean)
        .join("\n");
    })
    .join("\n");

  return [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:content="http://purl.org/rss/1.0/modules/content/">',
    "  <channel>",
    `    <title>${escapeXml(siteTitle)}</title>`,
    `    <link>${escapeXml(blogUrl)}</link>`,
    `    <description>${escapeXml(siteDescription)}</description>`,
    "    <language>en-us</language>",
    `    <lastBuildDate>${escapeXml(lastBuildDate)}</lastBuildDate>`,
    `    <atom:link href="${escapeXml(feedUrl)}" rel="self" type="application/rss+xml" />`,
    itemsXml,
    "  </channel>",
    "</rss>",
    "",
  ]
    .filter((line, idx, arr) => line !== "" || idx === arr.length - 1)
    .join("\n");
}
