import fs from "fs";
import path from "path";
import {
  ADR_DOMAINS,
  groupAdrEntriesByDomain,
  resolveAdrDomain,
} from "./adrShared.mjs";

export * from "./adrShared.mjs";

const IGNORED_ADR_FILES = new Set(["index.md", "template.md"]);
const HEADING_RE = /^#\s+(ADR-(\d{3,})):\s*(.+?)\s*$/;
const DOMAIN_META_RE = /^\s*[-*]\s*\*\*Domain:\*\*\s*(.+?)\s*$/i;
const STATUS_META_RE = /^\s*[-*]\s*\*\*Status:\*\*\s*(.+?)\s*$/i;
const DATE_META_RE = /^\s*[-*]\s*\*\*Date:\*\*\s*(\d{4}-\d{2}-\d{2})\b/i;
const ALSO_KNOWN_RE = /^Also known as:\s*(.+?)\s*$/i;
const INLINE_LINK_RE = /\[([^\]]+)\]\([^)]+\)/g;

function cleanInlineMarkdown(raw) {
  return String(raw || "")
    .replace(INLINE_LINK_RE, "$1")
    .replace(/[*_`]/g, "")
    .trim();
}

/**
 * Parses a single ADR Markdown file into a structured catalog item.
 *
 * @param {string} fullPath
 * @param {string} fileName
 */
export function parseAdrMarkdownFile(fullPath, fileName) {
  const raw = fs.readFileSync(fullPath, "utf-8");
  const lines = raw.split(/\r?\n/);
  const slug = fileName.replace(/\.md$/i, "");

  const firstLine = (lines[0] ?? "").trim();
  const headingMatch = firstLine.match(HEADING_RE);
  const code = headingMatch ? headingMatch[1] : slug;
  const number = headingMatch ? Number.parseInt(headingMatch[2], 10) : null;
  const title = headingMatch
    ? headingMatch[3].trim()
    : slug.replace(/^\d{4}-\d{2}-\d{2}-/, "").replace(/-/g, " ");

  let rawDomain = null;
  let status = "Accepted";
  let date = /^\d{4}-\d{2}-\d{2}/.test(slug) ? slug.slice(0, 10) : "";
  let alsoKnownAs = "";
  let inLegacyStatusSection = false;

  for (let i = 1; i < Math.min(lines.length, 45); i++) {
    const line = lines[i].trim();
    if (!line) continue;

    if (!alsoKnownAs && i <= 5) {
      const akaMatch = line.match(ALSO_KNOWN_RE);
      if (akaMatch) {
        alsoKnownAs = cleanInlineMarkdown(akaMatch[1]);
        continue;
      }
    }

    if (line.startsWith("## ")) {
      inLegacyStatusSection = line === "## Status";
      if (!inLegacyStatusSection) break;
      continue;
    }

    if (inLegacyStatusSection) {
      status = cleanInlineMarkdown(line);
      inLegacyStatusSection = false;
      continue;
    }

    const domainMatch = line.match(DOMAIN_META_RE);
    if (domainMatch) {
      rawDomain = domainMatch[1].trim();
      continue;
    }

    const statusMatch = line.match(STATUS_META_RE);
    if (statusMatch) {
      status = cleanInlineMarkdown(statusMatch[1]);
      continue;
    }

    const dateMatch = line.match(DATE_META_RE);
    if (dateMatch) {
      date = dateMatch[1];
    }
  }

  const domain = resolveAdrDomain({ rawDomain, slug, number });
  const isSuperseded = /^superseded\b/i.test(status);

  return {
    slug,
    url: `/adr/${slug}.html`,
    sidebarLink: `/adr/${slug}`,
    code,
    number,
    title,
    date,
    status,
    isSuperseded,
    alsoKnownAs,
    domainIndex: domain.index,
    domainSlug: domain.slug,
    domainLabel: domain.label,
    domainShortLabel: domain.shortLabel,
  };
}

/**
 * Scans `docs/adr/*.md` on disk and returns all ADR records ordered by domain, date, and ADR number.
 *
 * @param {string} docsDir
 */
export function loadAdrCatalog(docsDir) {
  const adrDir = path.join(docsDir, "adr");
  if (!fs.existsSync(adrDir)) return [];

  const entries = [];
  for (const file of fs.readdirSync(adrDir)) {
    if (!file.endsWith(".md")) continue;
    if (IGNORED_ADR_FILES.has(file.toLowerCase())) continue;
    const fullPath = path.join(adrDir, file);
    if (!fs.statSync(fullPath).isFile()) continue;
    entries.push(parseAdrMarkdownFile(fullPath, file));
  }

  entries.sort((a, b) => {
    if (a.domainIndex !== b.domainIndex) {
      return a.domainIndex - b.domainIndex;
    }
    const aNum = a.number ?? Number.MAX_SAFE_INTEGER;
    const bNum = b.number ?? Number.MAX_SAFE_INTEGER;
    if (a.date !== b.date) {
      return a.date.localeCompare(b.date);
    }
    if (aNum !== bNum) {
      return aNum - bNum;
    }
    return a.slug.localeCompare(b.slug);
  });

  return entries;
}

const CLINICAL_KERNEL_INCEPTION_DATE = "2026-09-29";
const MAX_SIDEBAR_ITEMS_PER_ADR_DOMAIN = 10;

/**
 * Builds compact, domain-grouped `/adr/` VitePress sidebar groups directly from `docs/adr/*.md` on disk.
 * Historical pre-kernel and superseded ADRs remain searchable in `<AdrIndex />` on `/adr/index` without
 * bloating the serialized sidebar metadata on every `/adr/` page.
 *
 * @param {string} docsDir
 */
export function buildAdrSidebarGroupsFromDisk(docsDir) {
  const adrDir = path.join(docsDir, "adr");
  if (!fs.existsSync(adrDir)) return [];

  const indexFilePath = path.join(adrDir, "index.md");
  const hasIndex = fs.existsSync(indexFilePath);
  const hasTemplate = fs.existsSync(path.join(adrDir, "TEMPLATE.md"));
  const hasDomainLogHeading =
    hasIndex &&
    /^##\s+Domain-Based Decisions Log\s*$/m.test(
      fs.readFileSync(indexFilePath, "utf-8")
    );

  const groups = [];
  const overviewItems = [];
  if (hasIndex) {
    overviewItems.push({ text: "ADR Overview & Log", link: "/adr/index" });
  }
  if (hasTemplate) {
    overviewItems.push({ text: "ADR Template", link: "/adr/TEMPLATE" });
  }
  if (overviewItems.length > 0) {
    groups.push({
      text: "ADR Overview",
      collapsed: false,
      items: overviewItems,
    });
  }

  const activeCatalog = loadAdrCatalog(docsDir).filter(
    (adr) =>
      !adr.isSuperseded &&
      (!adr.date || adr.date >= CLINICAL_KERNEL_INCEPTION_DATE)
  );

  for (const group of groupAdrEntriesByDomain(activeCatalog)) {
    const selectedAdrs =
      group.items.length > MAX_SIDEBAR_ITEMS_PER_ADR_DOMAIN
        ? [
            group.items[0],
            ...group.items.slice(-(MAX_SIDEBAR_ITEMS_PER_ADR_DOMAIN - 1)),
          ]
        : group.items;

    const items = selectedAdrs.map((adr) => ({
      text: `${adr.code}: ${adr.title}`,
      link: adr.sidebarLink,
    }));

    if (
      group.items.length > MAX_SIDEBAR_ITEMS_PER_ADR_DOMAIN &&
      hasDomainLogHeading
    ) {
      items.push({
        text: `All ${group.shortLabel} ADRs (${group.items.length})`,
        link: "/adr/index#domain-based-decisions-log",
      });
    }

    groups.push({
      text: group.label ?? ADR_DOMAINS[group.index]?.label,
      collapsed: group.index !== 0,
      items,
    });
  }

  return groups;
}
