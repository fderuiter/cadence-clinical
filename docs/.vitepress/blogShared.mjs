export const SITE_ORIGIN = "https://fderuiter.github.io";
export const BLOG_FEED_PATH = "/feed.xml";

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
  return `${baseUrl}${raw}`.replace(/\/+/g, "/");
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

/**
 * Unquotes a YAML scalar string value.
 *
 * @param {string} raw
 * @return {string | boolean}
 */
function parseYamlScalar(raw) {
  const trimmed = raw.trim();
  if (trimmed === "true") return true;
  if (trimmed === "false") return false;
  if (trimmed.length >= 2 && trimmed.startsWith('"') && trimmed.endsWith('"')) {
    return trimmed
      .slice(1, -1)
      .replace(/\\n/g, "\n")
      .replace(/\\"/g, '"')
      .replace(/\\\\/g, "\\");
  }
  if (trimmed.length >= 2 && trimmed.startsWith("'") && trimmed.endsWith("'")) {
    return trimmed.slice(1, -1).replace(/''/g, "'");
  }
  return trimmed;
}

/**
 * Parses an inline YAML array such as `["kernel", "architecture"]`.
 *
 * @param {string} raw
 * @return {string[]}
 */
function parseInlineArray(raw) {
  const inner = raw.trim().slice(1, -1).trim();
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

function countLeadingSpaces(line) {
  let count = 0;
  while (count < line.length && line[count] === " ") {
    count += 1;
  }
  return count;
}

function hasClosingQuote(text, quoteChar) {
  if (text.length < 2 || !text.endsWith(quoteChar)) {
    return false;
  }
  if (quoteChar === "'") {
    const trailingQuotes = text.slice(1).match(/'+$/)?.[0].length ?? 0;
    return trailingQuotes % 2 === 1;
  }
  let backslashes = 0;
  for (let idx = text.length - 2; idx >= 1 && text[idx] === "\\"; idx -= 1) {
    backslashes += 1;
  }
  return backslashes % 2 === 0;
}

function readMultilineQuotedScalar(lines, startIndex, minIndent, firstChunk) {
  const quoteChar = firstChunk[0];
  const parts = [firstChunk];
  let idx = startIndex;
  while (!hasClosingQuote(parts.join(" "), quoteChar) && idx < lines.length) {
    const nextLine = lines[idx];
    if (nextLine.trim() !== "" && countLeadingSpaces(nextLine) < minIndent) {
      break;
    }
    parts.push(nextLine.trim());
    idx += 1;
  }
  return {
    value: parseYamlScalar(parts.join(" ").replace(/\s+/g, " ")),
    nextIndex: idx,
  };
}

function tryReadWrappedScalar(lines, startIndex, minIndent, inlineRest) {
  if (
    (inlineRest.startsWith('"') && !hasClosingQuote(inlineRest, '"')) ||
    (inlineRest.startsWith("'") && !hasClosingQuote(inlineRest, "'"))
  ) {
    return readMultilineQuotedScalar(lines, startIndex, minIndent, inlineRest);
  }

  if (inlineRest !== "" && inlineRest !== ">" && inlineRest !== ">-") {
    return { value: parseYamlScalar(inlineRest), nextIndex: startIndex };
  }

  let peek = startIndex;
  while (peek < lines.length) {
    const trimmed = lines[peek].trim();
    if (!trimmed || trimmed.startsWith("#")) {
      peek += 1;
      continue;
    }
    break;
  }

  if (peek >= lines.length) {
    return null;
  }

  const firstLine = lines[peek];
  const firstTrimmed = firstLine.trim();
  if (
    countLeadingSpaces(firstLine) < minIndent ||
    (/^\s*-\s+/.test(firstLine) && inlineRest === "")
  ) {
    return null;
  }

  if (firstTrimmed.startsWith('"') || firstTrimmed.startsWith("'")) {
    return readMultilineQuotedScalar(lines, peek + 1, minIndent, firstTrimmed);
  }

  const parts = [firstTrimmed];
  let idx = peek + 1;
  while (idx < lines.length) {
    const contLine = lines[idx];
    const contTrimmed = contLine.trim();
    if (!contTrimmed) {
      break;
    }
    if (
      countLeadingSpaces(contLine) < minIndent ||
      /^\s*-\s+/.test(contLine) ||
      /^\s*[A-Za-z0-9_-]+:\s*/.test(contLine)
    ) {
      break;
    }
    parts.push(contTrimmed);
    idx += 1;
  }

  return {
    value: parseYamlScalar(parts.join(" ")),
    nextIndex: idx,
  };
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

  const endMarkerMatch = normalized.slice(4).match(/\n---(?:\n|$)/);
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

    if (rest.startsWith("[") && rest.endsWith("]")) {
      frontmatter[key] = parseInlineArray(rest);
      i += 1;
      continue;
    }

    const wrappedTopScalar = tryReadWrappedScalar(lines, i + 1, 1, rest);
    if (wrappedTopScalar !== null) {
      frontmatter[key] = wrappedTopScalar.value;
      i = wrappedTopScalar.nextIndex;
      continue;
    }

    if (rest === "") {
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

        const listMatch = itemLine.match(/^(\s*-\s+)(.*)$/);
        if (!listMatch) {
          i += 1;
          continue;
        }

        const fieldBaseIndent = listMatch[1].length;
        const firstEntry = listMatch[2].trim();
        const objectFieldMatch = firstEntry.match(/^([A-Za-z0-9_-]+):\s*(.*)$/);
        if (objectFieldMatch) {
          const firstFieldRest = objectFieldMatch[2].trim();
          const firstWrapped = tryReadWrappedScalar(
            lines,
            i + 1,
            fieldBaseIndent + 1,
            firstFieldRest
          );
          const obj = {
            [objectFieldMatch[1]]:
              firstWrapped !== null
                ? firstWrapped.value
                : parseYamlScalar(firstFieldRest),
          };
          i = firstWrapped !== null ? firstWrapped.nextIndex : i + 1;
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
              const subRest = subMatch[3].trim();
              const subWrapped = tryReadWrappedScalar(
                lines,
                i + 1,
                subIndent + 1,
                subRest
              );
              obj[subMatch[2]] =
                subWrapped !== null
                  ? subWrapped.value
                  : parseYamlScalar(subRest);
              i = subWrapped !== null ? subWrapped.nextIndex : i + 1;
            } else {
              i += 1;
            }
          }
          list.push(obj);
        } else {
          const itemWrapped = tryReadWrappedScalar(
            lines,
            i + 1,
            fieldBaseIndent,
            firstEntry
          );
          list.push(
            itemWrapped !== null
              ? itemWrapped.value
              : parseYamlScalar(firstEntry)
          );
          i = itemWrapped !== null ? itemWrapped.nextIndex : i + 1;
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

/**
 * Normalizes a documentation or external link for rendering in VitePress components.
 *
 * @param {string} rawLink
 * @return {string}
 */
export function normalizeSiteLink(rawLink) {
  const link = String(rawLink ?? "").trim();
  if (!link) return "/";
  if (/^([a-z][a-z0-9+.-]*:|#)/i.test(link)) return link;

  const hashIndex = link.indexOf("#");
  const pathPart = hashIndex === -1 ? link : link.slice(0, hashIndex);
  const hashPart = hashIndex === -1 ? "" : link.slice(hashIndex);

  let cleaned = pathPart
    .replace(/^(?:\.\.\/)+/, "/")
    .replace(/^\.\//, "/")
    .replace(/^\/?docs\//, "/");

  if (!cleaned.startsWith("/")) {
    cleaned = `/${cleaned}`;
  }

  if (cleaned === "/" || cleaned.endsWith("/")) {
    return `${cleaned}${hashPart}`;
  }

  if (/\/index(?:\.(?:md|html))?$/i.test(cleaned)) {
    const dirPath = cleaned.replace(/\/index(?:\.(?:md|html))?$/i, "/");
    return `${dirPath}${hashPart}`;
  }

  cleaned = cleaned.replace(/\.(?:md|html)$/i, "");
  return `${cleaned}.html${hashPart}`;
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
 * Normalizes an ISO date value (`YYYY-MM-DD` or `Date` instance) to `YYYY-MM-DD`.
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
 *   displayDate: string,
 *   authors: string[],
 *   summary: string,
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
  if (frontmatter.draft === true || frontmatter.draft === "true") {
    return null;
  }

  const lastSegment = url.split("/").filter(Boolean).pop() ?? "";
  const slug = entry?.slug || lastSegment.replace(/\.(?:html|md)$/i, "");

  let fallbackTitle = "";
  let fallbackSummary = "";
  if (entry?.body) {
    const titleMatch = entry.body.match(/^#\s+(.+)$/m);
    if (titleMatch) fallbackTitle = titleMatch[1].trim();
    const lines = entry.body.split("\n");
    for (const line of lines) {
      const trimmed = line.trim();
      if (
        trimmed &&
        !trimmed.startsWith("#") &&
        !trimmed.startsWith(">") &&
        !trimmed.startsWith("|") &&
        !trimmed.startsWith("```") &&
        !trimmed.startsWith("-")
      ) {
        fallbackSummary = trimmed.replace(/\[([^\]]+)\]\([^)]+\)/g, "$1");
        break;
      }
    }
  }

  const title = String(frontmatter.title || fallbackTitle || slug).trim();
  const date = normalizeIsoDate(frontmatter.date, slug);
  const rawAuthors = Array.isArray(frontmatter.authors)
    ? frontmatter.authors
    : frontmatter.author
      ? [frontmatter.author]
      : ["@fderuiter"];
  const authors = rawAuthors.map((a) => String(a).trim()).filter(Boolean);

  const summary = String(
    frontmatter.summary || frontmatter.description || fallbackSummary || title
  ).trim();

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
    displayDate: formatDisplayDate(date),
    authors: authors.length > 0 ? authors : ["@fderuiter"],
    summary,
    tags,
    related,
    draft: false,
  };
}

/**
 * Sorts normalized blog posts in reverse-chronological order.
 *
 * @param {Array<{ date: string, slug?: string, url: string }>} posts
 * @return {Array<any>}
 */
export function sortBlogPostsReverseChronological(posts) {
  return [...posts].sort((a, b) => {
    const dateCmp = String(b.date).localeCompare(String(a.date));
    if (dateCmp !== 0) return dateCmp;
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
  const siteOrigin = (options.siteOrigin || SITE_ORIGIN).replace(/\/+$/, "");
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
  const latestDate = published.length > 0 ? published[0].date : "2026-11-19";
  const lastBuildDate = new Date(`${latestDate}T00:00:00Z`).toUTCString();

  const itemsXml = published
    .map((post) => {
      const itemUrl = toAbsoluteUrl(post.url);
      const pubDate = new Date(`${post.date}T00:00:00Z`).toUTCString();
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
        optionalLines,
        "    </item>",
      ]
        .filter(Boolean)
        .join("\n");
    })
    .join("\n");

  return [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom" xmlns:dc="http://purl.org/dc/elements/1.1/">',
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
