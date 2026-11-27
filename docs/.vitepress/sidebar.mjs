import fs from "fs";
import path from "path";

import {
  getBlogSidebarItems,
  isDraftBlogPostFile,
  parseBlogFrontmatter,
} from "./blog.mjs";
import {
  EXCLUDED_PORTAL_DOCS,
  isExcludedPortalDocFile,
  splitAnchor,
  toRepoUrl,
  vitepressSlugify,
} from "./repoLinks.mjs";
import rootDocsModule from "./rootDocs.cjs";

const { STAGED_ROOT_DOCS } = rootDocsModule;

const ACTIVE_STATUSES = new Set(["Normative", "Current", "Generated"]);
const MARKDOWN_LINK_RE = /\[((?:[^\]]|\\\])+)\]\(([^)]+)\)/g;
const CLINICAL_KERNEL_INCEPTION_DATE = "2026-09-29";
const ADR_DATE_PREFIX_RE = /^(\d{4}-\d{2}-\d{2})-/;
const ADR_DOMAIN_SHARDS = [
  {
    slug: "kernel",
    label: "0. Clinical Kernel & Study Execution",
    headingPattern: /^0\.\s*|Clinical Kernel/i,
  },
  {
    slug: "platform",
    label: "1. Core Platform & Architecture",
    headingPattern: /^1\.\s*|Core Platform/i,
  },
  {
    slug: "security",
    label: "2. Security & Identity",
    headingPattern: /^2\.\s*|Security\s*&\s*Identity/i,
  },
  {
    slug: "standards",
    label: "3. Interoperability, Standards & Data Exchange",
    headingPattern: /^3\.\s*|Interoperability/i,
  },
  {
    slug: "operations",
    label: "4. Clinical Operations, Biostatistics & AI",
    headingPattern: /^4\.\s*|Clinical Operations/i,
  },
  {
    slug: "compliance",
    label: "5. Compliance, Audit & Regulatory Validation",
    headingPattern: /^5\.\s*|Compliance,\s*Audit/i,
  },
  {
    slug: "frontend",
    label: "6. Frontend & Design System",
    headingPattern: /^6\.\s*|Frontend\s*&\s*Design/i,
  },
  {
    slug: "tooling",
    label: "7. DevOps, Tooling & Quality Gates",
    headingPattern: /^7\.\s*|DevOps,\s*Tooling/i,
  },
];

function isCategoryShardRelPath(relToDocs) {
  return (
    relToDocs === "ROOT_INDEX.md" ||
    /^[^/]+\/(?:INDEX|DOC_INDEX)\.md$/.test(relToDocs)
  );
}

const ADR_SUPERSEDED_RE =
  /^\s*(?:[-*]\s*)?(?:\*\*)?Status:?(?:\*\*)?:?\s*(?:\*\*)?Superseded\b|^\s*>\s*(?:\[!(?:WARNING|IMPORTANT|NOTE)\]\s*)?(?:\*\*)?Superseded\b|^\s*Superseded\s+by\b|^\s*##\s+Status\s*\r?\n+\s*Superseded\b/im;

/**
 * Splits a Markdown table row into trimmed cells, preserving escaped pipes (`\|`).
 *
 * @param {string} line
 * @returns {string[]}
 */
function splitTableRow(line) {
  const trimmed = line.trim();
  if (!trimmed.startsWith("|") || !trimmed.endsWith("|")) return [];
  return trimmed
    .slice(1, -1)
    .split(/(?<!\\)\|/)
    .map((cell) => cell.replace(/\\\|/g, "|").trim());
}

function isSeparatorRow(cells) {
  return cells.length > 0 && cells.every((cell) => /^:?-+:?$/.test(cell));
}

function unescapeLabel(label) {
  return label.replace(/\\([[\]|])/g, "$1").trim();
}

/**
 * Converts a raw Markdown href (written relative to `docs/DOCUMENTATION_INDEX.md`)
 * into a VitePress sidebar link (`/...` for portal pages, external URL otherwise).
 *
 * @param {string} rawHref
 * @param {{ docsDir: string, repoRoot: string }} options
 * @returns {string | null}
 */
export function toSidebarLink(rawHref, { docsDir, repoRoot, baseDir }) {
  const effectiveBaseDir = baseDir ?? docsDir;
  const href = rawHref.trim();
  if (!href) return null;

  if (/^([a-z][a-z0-9+.-]*:)/i.test(href)) {
    return href;
  }

  if (href.startsWith("/")) {
    const [cleanRootPath, rootAnchor = ""] = splitAnchor(href);
    const rawRootPath = cleanRootPath.split("?")[0];
    const normalizedPage = rawRootPath
      .replace(/^\//, "")
      .replace(/\.(html|md)$/i, "");
    if (normalizedPage) {
      const rootCandidate = path.join(docsDir, `${normalizedPage}.md`);
      if (isDraftBlogPostFile(rootCandidate)) {
        return null;
      }
      if (isExcludedPortalDocFile(rootCandidate, docsDir)) {
        return toRepoUrl(href, docsDir, docsDir, repoRoot);
      }
    }
    if (/^\/index(?:\.(?:html|md))?$/.test(rawRootPath)) {
      return `/${rootAnchor}`;
    }
    if (/\/index(?:\.(?:html|md))?$/.test(rawRootPath)) {
      const hubPrefix = rawRootPath.replace(/\/index(?:\.(?:html|md))?$/, "/");
      return `${hubPrefix}${rootAnchor}`;
    }
    return href;
  }

  const [rawTarget, anchor = ""] = splitAnchor(href);
  const cleanTarget = rawTarget.split("?")[0].trim();
  if (!cleanTarget) {
    return anchor ? `/DOCUMENTATION_INDEX${anchor}` : "/DOCUMENTATION_INDEX";
  }

  const absolute = path.resolve(effectiveBaseDir, decodeURI(cleanTarget));
  const relToRepo = path.relative(repoRoot, absolute).replace(/\\/g, "/");

  const stagedRoot = STAGED_ROOT_DOCS[relToRepo];
  if (stagedRoot) {
    if (stagedRoot === "index.md") {
      return `/${anchor}`;
    }
    return `/${stagedRoot.replace(/\.md$/i, "")}${anchor}`;
  }

  const relToDocs = path.relative(docsDir, absolute).replace(/\\/g, "/");
  const insideDocs = !relToDocs.startsWith("..") && !path.isAbsolute(relToDocs);

  if (insideDocs) {
    const exists = fs.existsSync(absolute);
    const isDir =
      cleanTarget.endsWith("/") ||
      (exists && fs.statSync(absolute).isDirectory());

    if (isDir) {
      if (fs.existsSync(path.join(absolute, "index.md"))) {
        return relToDocs === "" ? `/${anchor}` : `/${relToDocs}/${anchor}`;
      }
      return toRepoUrl(href, effectiveBaseDir, docsDir, repoRoot);
    }

    if (exists && !/\.(md|html)$/i.test(absolute)) {
      return toRepoUrl(href, effectiveBaseDir, docsDir, repoRoot);
    }

    const mdCandidate = exists
      ? /\.html$/i.test(absolute) &&
        fs.existsSync(absolute.replace(/\.html$/i, ".md"))
        ? absolute.replace(/\.html$/i, ".md")
        : absolute
      : fs.existsSync(`${absolute}.md`)
        ? `${absolute}.md`
        : /\.html$/i.test(absolute) &&
            fs.existsSync(absolute.replace(/\.html$/i, ".md"))
          ? absolute.replace(/\.html$/i, ".md")
          : null;
    if (mdCandidate && isDraftBlogPostFile(mdCandidate)) {
      return null;
    }
    if (mdCandidate && isExcludedPortalDocFile(mdCandidate, docsDir)) {
      return toRepoUrl(href, effectiveBaseDir, docsDir, repoRoot);
    }

    if (relToDocs === "index.md") {
      return `/${anchor}`;
    }
    if (/\/index\.(md|html)$/.test(relToDocs)) {
      return `/${relToDocs.replace(/\/index\.(md|html)$/, "/")}${anchor}`;
    }

    return `/${relToDocs.replace(/\.(md|html)$/i, "")}${anchor}`;
  }

  return toRepoUrl(href, effectiveBaseDir, docsDir, repoRoot);
}

function normalizeHrefRelativeToDocs(rawHref, baseDir, docsDir) {
  const trimmed = rawHref.trim();
  if (
    !trimmed ||
    trimmed.startsWith("/") ||
    trimmed.startsWith("#") ||
    /^([a-z][a-z0-9+.-]*:)/i.test(trimmed)
  ) {
    return trimmed;
  }
  const [rawTarget, anchor = ""] = splitAnchor(trimmed);
  const cleanTarget = rawTarget.split("?")[0].trim();
  if (!cleanTarget) return trimmed;
  const absolute = path.resolve(baseDir, decodeURI(cleanTarget));
  let rel = path.relative(docsDir, absolute).replace(/\\/g, "/");
  if (cleanTarget.endsWith("/") && !rel.endsWith("/")) {
    rel += "/";
  }
  return `${rel}${anchor}`;
}

/**
 * Parses `docs/DOCUMENTATION_INDEX.md` into structured row records with section, status, label, rawHref, and portal link.
 *
 * @param {string} indexContent
 * @param {{ docsDir: string, repoRoot: string }} options
 */
export function parseDocumentationIndex(
  indexContent,
  { docsDir, repoRoot, baseDir, expandShards = true, shardOverrides = null }
) {
  const effectiveBaseDir = baseDir ?? docsDir;
  const entries = [];
  let currentSection = "";
  let hasStatusColumn = true;

  for (const line of indexContent.split(/\r?\n/)) {
    const sectionMatch = line.match(/^##\s+(.+?)\s*$/);
    if (sectionMatch) {
      currentSection = sectionMatch[1].trim();
      hasStatusColumn = true;
      continue;
    }

    if (!line.trim().startsWith("|")) continue;
    const cells = splitTableRow(line);
    if (cells.length < 2 || isSeparatorRow(cells)) continue;

    if (cells[0] === "Document" || cells[0] === "Location") {
      hasStatusColumn = cells.includes("Status");
      continue;
    }

    const primaryMatches = [...cells[0].matchAll(MARKDOWN_LINK_RE)];
    if (primaryMatches.length === 0) continue;

    const status = hasStatusColumn ? cells[1] : "Current";
    const descriptionCell = hasStatusColumn
      ? (cells[2] ?? "")
      : (cells[1] ?? "");

    for (const m of primaryMatches) {
      const text = unescapeLabel(m[1]);
      const rawHref = m[2].trim();

      if (expandShards && !/^([a-z][a-z0-9+.-]*:|\/|#)/i.test(rawHref)) {
        const cleanTarget = splitAnchor(rawHref)[0].split("?")[0].trim();
        const candidateAbs = path.resolve(
          effectiveBaseDir,
          decodeURI(cleanTarget)
        );
        const relToDocs = path
          .relative(docsDir, candidateAbs)
          .replace(/\\/g, "/");
        if (
          isCategoryShardRelPath(relToDocs) &&
          (shardOverrides?.[relToDocs] !== undefined ||
            fs.existsSync(candidateAbs))
        ) {
          const shardContent =
            shardOverrides?.[relToDocs] ??
            fs.readFileSync(candidateAbs, "utf-8");
          const shardEntries = parseDocumentationIndex(shardContent, {
            docsDir,
            repoRoot,
            baseDir: path.dirname(candidateAbs),
            expandShards: false,
            shardOverrides,
          });
          entries.push(...shardEntries);
          continue;
        }
      }

      const link = toSidebarLink(rawHref, {
        docsDir,
        repoRoot,
        baseDir: effectiveBaseDir,
      });
      if (!link) continue;
      entries.push({
        section: currentSection,
        status,
        text,
        rawHref: normalizeHrefRelativeToDocs(
          rawHref,
          effectiveBaseDir,
          docsDir
        ),
        link,
      });
    }

    // Include companion docs linked in 2-column policy/reference tables (e.g. agents/issue-tracker.md, agents/triage-labels.md).
    if (!hasStatusColumn && descriptionCell) {
      for (const m of descriptionCell.matchAll(MARKDOWN_LINK_RE)) {
        const rawHref = m[2].trim();
        if (/^([a-z][a-z0-9+.-]*:|#)/i.test(rawHref)) continue;
        const cleanTarget = splitAnchor(rawHref)[0].trim();
        if (!cleanTarget.endsWith(".md")) continue;
        const link = toSidebarLink(rawHref, {
          docsDir,
          repoRoot,
          baseDir: effectiveBaseDir,
        });
        if (!link) continue;
        entries.push({
          section: currentSection,
          status: "Current",
          text: unescapeLabel(m[1]),
          rawHref: normalizeHrefRelativeToDocs(
            rawHref,
            effectiveBaseDir,
            docsDir
          ),
          link,
          fromDescription: true,
        });
      }
    }
  }

  return entries;
}

const MAX_INLINE_KERNEL_SIDEBAR_ITEMS = 8;

/**
 * Extracts active Clinical Kernel ADR links, K-decision table items, security ADR routes,
 * and domain section headings from `docs/adr/index.md`.
 *
 * @param {string} adrIndexContent
 */
function parseAdrShardEntries(shardContent, domainSlug) {
  const items = [];
  if (!shardContent) return items;
  let inDecisions = false;
  for (const line of shardContent.split(/\r?\n/)) {
    const h2 = line.match(/^##\s+(.+?)\s*$/);
    if (h2) {
      inDecisions = /Decisions/i.test(h2[1].trim());
      continue;
    }
    if (!inDecisions) continue;
    const linkMatch = line.match(/^-\s*\[(.*?)\]\((.*?)\)/);
    if (!linkMatch) continue;
    const text = linkMatch[1].trim();
    let filename = linkMatch[2].trim();
    if (filename.startsWith("./")) filename = filename.slice(2);
    if (filename.startsWith("../") || filename.includes("://")) continue;
    filename = splitAnchor(filename)[0].replace(/\.md$/i, "");
    if (!filename || filename.toLowerCase() === "index") continue;
    items.push({
      text,
      link: `/adr/${domainSlug}/${filename}`,
      domain: domainSlug,
    });
  }
  return items;
}

/**
 * Extracts active Clinical Kernel ADR links, K-decision table items, security ADR routes,
 * domain shard entries, and domain section headings across `docs/adr/index.md`,
 * `docs/adr/legacy-index.md`, and `docs/adr/<domain>/index.md`.
 *
 * @param {string} adrIndexContent
 * @param {{ adrDir?: string }} [options]
 */
function parseAdrIndexStructure(adrIndexContent, options = {}) {
  let kernelHeading = "0. Clinical Kernel (K001–K037)";
  const kernelItems = [];
  const securityItems = [];
  const domainShardItems = [];
  const availableDomainShards = new Set();
  let hasLegacyIndex = false;
  const kDecisionTableItems = [];
  const domainHeadings = [];

  const parseBulletBlock = (markdownContent) => {
    if (!markdownContent) return;
    let domainTitle = "";
    for (const line of markdownContent.split(/\r?\n/)) {
      const h2Match = line.match(/^##\s+(.+?)\s*$/);
      if (h2Match) {
        domainTitle = "";
        continue;
      }
      const h3Match = line.match(/^###\s+(.+?)\s*$/);
      if (h3Match) {
        domainTitle = h3Match[1].trim();
        continue;
      }
      if (!domainTitle) continue;
      const linkMatch = line.match(/^-\s*\[(.*?)\]\((.*?)\)/);
      if (!linkMatch) continue;
      const text = linkMatch[1].trim();
      let filename = linkMatch[2].trim();
      if (filename.startsWith("./")) {
        filename = filename.slice(2);
      } else if (filename.startsWith("docs/adr/")) {
        filename = filename.slice("docs/adr/".length);
      }
      const cleanFile = splitAnchor(filename)[0];
      if (
        !cleanFile ||
        cleanFile === "legacy-index.md" ||
        /\/index\.md$/i.test(cleanFile)
      ) {
        continue;
      }
      const stem = cleanFile.replace(/\.md$/i, "");
      const item = { text, link: `/adr/${stem}` };
      if (/^0\.\s*/.test(domainTitle) || /Clinical Kernel/i.test(domainTitle)) {
        kernelItems.push(item);
      } else if (/Security\s*&\s*Identity/i.test(domainTitle)) {
        securityItems.push(item);
      }
    }
  };

  if (adrIndexContent) {
    let inKernelDecisionTable = false;
    for (const line of adrIndexContent.split(/\r?\n/)) {
      const h2Match = line.match(/^##\s+(.+?)\s*$/);
      if (h2Match) {
        inKernelDecisionTable = /Clinical Kernel Decisions/i.test(
          h2Match[1].trim()
        );
        continue;
      }

      if (inKernelDecisionTable && line.trim().startsWith("|")) {
        const cells = splitTableRow(line);
        if (
          cells.length >= 3 &&
          /^K\d+$/i.test(cells[0]) &&
          !isSeparatorRow(cells)
        ) {
          const linkMatch = cells[2].match(/\[([^\]]+)\]\(([^)]+)\)/);
          if (linkMatch) {
            let filename = linkMatch[2].trim();
            if (filename.startsWith("./")) filename = filename.slice(2);
            else if (filename.startsWith("docs/adr/")) {
              filename = filename.slice("docs/adr/".length);
            }
            filename = splitAnchor(filename)[0].replace(/\.md$/i, "");
            const subject = cells[1].replace(/\s*\([^)]*\)\s*/g, " ").trim();
            const shortSubject =
              subject.length > 44
                ? `${subject.slice(0, 41).trimEnd()}…`
                : subject;
            kDecisionTableItems.push({
              text: `${cells[0]}: ${shortSubject}`,
              link: `/adr/${filename}`,
            });
          }
        }
        continue;
      }

      const headingMatch = line.match(/^###\s+(.+?)\s*$/);
      if (headingMatch) {
        const title = headingMatch[1].trim();
        if (/^0\.\s*/.test(title) || /Clinical Kernel/i.test(title)) {
          kernelHeading = title;
        }
        domainHeadings.push(title);
      }
    }

    parseBulletBlock(adrIndexContent);
  }

  const adrDir = options.adrDir;
  if (adrDir && fs.existsSync(adrDir)) {
    const legacyIndexPath = path.join(adrDir, "legacy-index.md");
    if (fs.existsSync(legacyIndexPath)) {
      hasLegacyIndex = true;
      parseBulletBlock(fs.readFileSync(legacyIndexPath, "utf-8"));
    }

    for (const { slug } of ADR_DOMAIN_SHARDS) {
      const shardPath = path.join(adrDir, slug, "index.md");
      if (!fs.existsSync(shardPath)) continue;
      availableDomainShards.add(slug);
      const parsedShard = parseAdrShardEntries(
        fs.readFileSync(shardPath, "utf-8"),
        slug
      );
      domainShardItems.push(...parsedShard);
      if (slug === "kernel") {
        kernelItems.push(...parsedShard);
      } else if (slug === "security") {
        securityItems.push(...parsedShard);
      }
    }
  }

  return {
    kernelHeading,
    kernelItems,
    securityItems,
    domainShardItems,
    availableDomainShards,
    hasLegacyIndex,
    kDecisionTableItems,
    domainHeadings,
  };
}

/**
 * Builds a compact `/adr/` sidebar listing the ADR overview, active Clinical Kernel decisions,
 * and domain decision shard links.
 *
 * @param {string} adrIndexContent
 * @param {Array<{text: string, link: string, status?: string}>} indexAdrEntries
 * @param {{ adrDir?: string }} [options]
 */
export function buildAdrSidebarGroups(
  adrIndexContent,
  indexAdrEntries = [],
  options = {}
) {
  const historicalAdrRoutes = new Set(
    indexAdrEntries
      .filter(
        (entry) =>
          entry.status === "Historical" &&
          normalizeRouteKey(entry.link) !== "/adr/legacy-index"
      )
      .map((entry) => normalizeRouteKey(entry.link))
  );

  const {
    kernelHeading,
    kernelItems,
    domainShardItems,
    availableDomainShards,
    hasLegacyIndex,
    kDecisionTableItems,
    domainHeadings,
  } = parseAdrIndexStructure(adrIndexContent, options);

  const overviewItems = [{ text: "ADR Overview & Log", link: "/adr/" }];
  if (
    indexAdrEntries.some(
      (entry) => normalizeRouteKey(entry.link) === "/adr/TEMPLATE"
    ) ||
    /\(TEMPLATE\.md\)/.test(adrIndexContent ?? "")
  ) {
    overviewItems.push({ text: "ADR Template", link: "/adr/TEMPLATE" });
  }
  if (
    hasLegacyIndex ||
    indexAdrEntries.some(
      (entry) => normalizeRouteKey(entry.link) === "/adr/legacy-index"
    )
  ) {
    overviewItems.push({
      text: "Frozen Legacy ADRs",
      link: "/adr/legacy-index",
    });
  }

  const kernelShardAdrs = domainShardItems.filter(
    (item) => item.domain === "kernel"
  );
  const candidateKernelSource =
    kDecisionTableItems.length > 0
      ? [...kernelShardAdrs, ...kDecisionTableItems]
      : kernelItems;

  const filteredKernelItems = dedupeItems(
    candidateKernelSource.filter(
      (item) => !historicalAdrRoutes.has(normalizeRouteKey(item.link))
    )
  );

  const kernelShardItems = domainShardItems.filter(
    (item) => item.domain === "kernel"
  );
  const kernelFooterItems = [];
  if (availableDomainShards.has("kernel")) {
    kernelFooterItems.push({
      text: "Kernel Domain Shard",
      link: "/adr/kernel/",
    });
  }
  kernelFooterItems.push(...kernelShardItems);
  kernelFooterItems.push({
    text: "All Kernel Decisions (K001–K037)",
    link: `/adr/#${vitepressSlugify(kernelHeading)}`,
  });

  const compactKernelItems =
    filteredKernelItems.length > MAX_INLINE_KERNEL_SIDEBAR_ITEMS
      ? [
          ...filteredKernelItems.slice(0, MAX_INLINE_KERNEL_SIDEBAR_ITEMS),
          ...kernelFooterItems,
        ]
      : [
          ...filteredKernelItems,
          ...(availableDomainShards.has("kernel")
            ? [{ text: "Kernel Domain Shard", link: "/adr/kernel/" }]
            : []),
          ...kernelShardItems,
        ];

  const matchedDomainSlugs = new Set();
  if (compactKernelItems.length > 0) {
    matchedDomainSlugs.add("kernel");
  }

  const rawDomainLogItems = [];
  for (const heading of domainHeadings) {
    if (compactKernelItems.length > 0 && heading === kernelHeading) {
      continue;
    }
    const matchedDomain = ADR_DOMAIN_SHARDS.find(({ headingPattern }) =>
      headingPattern.test(heading)
    );
    if (matchedDomain && availableDomainShards.has(matchedDomain.slug)) {
      matchedDomainSlugs.add(matchedDomain.slug);
      rawDomainLogItems.push({
        text: heading,
        link: `/adr/${matchedDomain.slug}/`,
      });
      for (const shardItem of domainShardItems) {
        if (shardItem.domain === matchedDomain.slug) {
          rawDomainLogItems.push(shardItem);
        }
      }
    } else {
      rawDomainLogItems.push({
        text: heading,
        link: `/adr/#${vitepressSlugify(heading)}`,
      });
    }
  }

  for (const domain of ADR_DOMAIN_SHARDS) {
    if (
      matchedDomainSlugs.has(domain.slug) ||
      !availableDomainShards.has(domain.slug)
    ) {
      continue;
    }
    rawDomainLogItems.push({
      text: domain.label,
      link: `/adr/${domain.slug}/`,
    });
    for (const shardItem of domainShardItems) {
      if (shardItem.domain === domain.slug) {
        rawDomainLogItems.push(shardItem);
      }
    }
  }
  const domainLogItems = dedupeItems(rawDomainLogItems);

  const groups = [
    {
      text: "ADR Overview",
      items: overviewItems,
    },
  ];

  if (compactKernelItems.length > 0) {
    groups.push({
      text: kernelHeading,
      items: compactKernelItems,
    });
  }

  if (domainLogItems.length > 0) {
    groups.push({
      text: "Domain Decision Logs",
      items: domainLogItems,
    });
  }

  return groups;
}

function dedupeItems(items) {
  const seen = new Set();
  const out = [];
  for (const item of items) {
    if (!item?.link || seen.has(item.link)) continue;
    seen.add(item.link);
    out.push({ text: item.text, link: item.link });
  }
  return out;
}

function normalizeRouteKey(link) {
  return splitAnchor(link)[0];
}

function makeGroup(text, rawItems) {
  const items = dedupeItems(rawItems);
  if (items.length === 0) return null;
  return { text, items };
}

/**
 * Synthesizes a route-keyed VitePress sidebar map (`Record<string, SidebarGroup[]>`)
 * from `docs/DOCUMENTATION_INDEX.md` so each section renders only its own navigation menu.
 *
 * @param {string} indexPath
 * @param {{
 *   docsDir?: string,
 *   repoRoot?: string,
 *   adrIndexPath?: string,
 *   indexContent?: string,
 *   adrIndexContent?: string,
 * }} [options]
 */
export function synthesizeSidebarFromIndex(indexPath, options = {}) {
  const docsDir = options.docsDir ?? path.dirname(indexPath);
  const repoRoot = options.repoRoot ?? path.resolve(docsDir, "..");
  const adrIndexPath =
    options.adrIndexPath ?? path.join(docsDir, "adr", "index.md");

  const indexContent =
    options.indexContent ??
    (fs.existsSync(indexPath) ? fs.readFileSync(indexPath, "utf-8") : "");
  const adrIndexContent =
    options.adrIndexContent ??
    (fs.existsSync(adrIndexPath) ? fs.readFileSync(adrIndexPath, "utf-8") : "");

  const allEntries = parseDocumentationIndex(indexContent, {
    docsDir,
    repoRoot,
    shardOverrides: options.shardOverrides ?? null,
  });

  const hasRootPortalPages =
    fs.existsSync(path.join(docsDir, "index.md")) ||
    fs.existsSync(path.join(docsDir, "DOCUMENTATION_INDEX.md")) ||
    allEntries.some((entry) => entry.section === "Start here");

  const startHereItems = hasRootPortalPages
    ? [
        { text: "Portal Home", link: "/" },
        { text: "Documentation Index", link: "/DOCUMENTATION_INDEX" },
      ]
    : [];
  const blogItems = [];
  const kernelItems = [];
  const devItems = [];
  const productItems = [];
  const componentItems = [];
  const apiItems = [];
  const openapiItems = [];
  const schemaItems = [];
  const sdlcActiveItems = [];
  const standardsItems = [];
  const policyItems = [];
  const rootPolicyItems = [];
  const adrEntries = [];
  const sdlcBaselineItems = [];
  const specsBaselineItems = [];
  const migrationHistoricalItems = [];
  const repoMigrationHistoricalItems = [];
  const customActiveSections = new Map();
  const seenRoutes = new Set();

  for (const entry of allEntries) {
    const routeKey = normalizeRouteKey(entry.link);
    if (routeKey.startsWith("/")) {
      seenRoutes.add(routeKey);
    }

    if (
      entry.section === "Architecture decision records" ||
      routeKey.startsWith("/adr/")
    ) {
      adrEntries.push(entry);
      continue;
    }

    if (entry.status === "Baseline") {
      if (routeKey.startsWith("/specs/")) {
        specsBaselineItems.push(entry);
      } else {
        sdlcBaselineItems.push(entry);
      }
      continue;
    }

    if (entry.status === "Historical") {
      if (routeKey.startsWith("/architecture/repository-migration/")) {
        repoMigrationHistoricalItems.push(entry);
      } else {
        migrationHistoricalItems.push(entry);
      }
      continue;
    }

    if (!ACTIVE_STATUSES.has(entry.status)) {
      continue;
    }

    if (routeKey.startsWith("/api/")) {
      apiItems.push(entry);
      continue;
    }

    if (routeKey.startsWith("/openapi/")) {
      openapiItems.push(entry);
      continue;
    }

    if (routeKey.startsWith("/schema/")) {
      schemaItems.push(entry);
      continue;
    }

    if (routeKey.startsWith("/components/")) {
      componentItems.push(entry);
      continue;
    }

    switch (entry.section) {
      case "Start here":
        startHereItems.push(entry);
        break;
      case "Engineering blog":
        if (entry.link.startsWith("/")) {
          blogItems.push(entry);
        }
        break;
      case "Kernel and roadmap":
        kernelItems.push(entry);
        break;
      case "Developing and running":
        devItems.push(entry);
        break;
      case "Product":
        productItems.push(entry);
        break;
      case "Requirements, validation and GxP":
        sdlcActiveItems.push(entry);
        break;
      case "Standards reference material":
        standardsItems.push(entry);
        break;
      case "Repository policies":
        policyItems.push(entry);
        if (
          routeKey.startsWith("/") &&
          routeKey.split("/").filter(Boolean).length === 1
        ) {
          rootPolicyItems.push(entry);
        }
        break;
      default: {
        const title = entry.section || "Additional Documentation";
        if (!customActiveSections.has(title)) {
          customActiveSections.set(title, []);
        }
        customActiveSections.get(title).push(entry);
        break;
      }
    }
  }

  // Populate Engineering Blog from published frontmatter titles sorted newest-first by date via getBlogSidebarItems,
  // while preserving synthetic indexContent fixtures that supply non-disk blog entries or omit the section.
  let resolvedBlogItems = [];
  const hasDiskBlogDir = fs.existsSync(path.join(docsDir, "blog"));
  const shouldIncludeBlogGroup =
    options.indexContent === undefined
      ? hasDiskBlogDir || blogItems.length > 0
      : blogItems.length > 0;

  if (shouldIncludeBlogGroup) {
    if (hasDiskBlogDir) {
      const diskBlogItems = getBlogSidebarItems(docsDir);
      const coveredBlogRoutes = new Set(
        diskBlogItems.map((item) => normalizeRouteKey(item.link))
      );
      const syntheticExtraItems = blogItems
        .filter(
          (entry) => !coveredBlogRoutes.has(normalizeRouteKey(entry.link))
        )
        .sort((a, b) =>
          normalizeRouteKey(b.link).localeCompare(normalizeRouteKey(a.link))
        );
      resolvedBlogItems = [...diskBlogItems, ...syntheticExtraItems];
    } else {
      blogItems.sort((a, b) => {
        const aKey = normalizeRouteKey(a.link);
        const bKey = normalizeRouteKey(b.link);
        if (aKey === "/blog/") return -1;
        if (bKey === "/blog/") return 1;
        return bKey.localeCompare(aKey);
      });
      resolvedBlogItems = blogItems;
    }
  }

  // Keep overview pages at the top of generated reference and component sections.
  apiItems.sort((a, b) => {
    if (normalizeRouteKey(a.link) === "/api/") return -1;
    if (normalizeRouteKey(b.link) === "/api/") return 1;
    return 0;
  });
  schemaItems.sort((a, b) => {
    const aIsOverview = normalizeRouteKey(a.link) === "/schema/README";
    const bIsOverview = normalizeRouteKey(b.link) === "/schema/README";
    if (aIsOverview && !bIsOverview) return -1;
    if (!aIsOverview && bIsOverview) return 1;
    return 0;
  });
  componentItems.sort((a, b) => {
    if (normalizeRouteKey(a.link) === "/components/") return -1;
    if (normalizeRouteKey(b.link) === "/components/") return 1;
    return 0;
  });
  migrationHistoricalItems.sort((a, b) => {
    const aKey = normalizeRouteKey(a.link);
    const bKey = normalizeRouteKey(b.link);
    if (aKey === "/migration/") return -1;
    if (bKey === "/migration/") return 1;
    const aInMigration = aKey.startsWith("/migration/");
    const bInMigration = bKey.startsWith("/migration/");
    if (aInMigration !== bInMigration) return aInMigration ? -1 : 1;
    return 0;
  });

  const sidebar = {};
  const assignSection = (prefixes, groups) => {
    const validGroups = groups.filter(Boolean);
    if (validGroups.length === 0) return;
    for (const prefix of prefixes) {
      sidebar[prefix] = validGroups;
    }
  };

  for (const item of resolvedBlogItems) {
    const key = normalizeRouteKey(item.link);
    if (key.startsWith("/")) {
      seenRoutes.add(key);
    }
  }

  const aiItems = kernelItems.filter((entry) => {
    const key = normalizeRouteKey(entry.link);
    return (
      key.startsWith("/ai/") || key === "/kernel/CLINICAL_KERNEL_SPECIFICATION"
    );
  });
  const uxItems = kernelItems.filter((entry) => {
    const key = normalizeRouteKey(entry.link);
    return (
      key.startsWith("/ux/") || key === "/kernel/CLINICAL_KERNEL_SPECIFICATION"
    );
  });
  const openapiSectionItems = seenRoutes.has("/api/")
    ? [{ text: "REST API Reference", link: "/api/" }, ...openapiItems]
    : openapiItems;

  assignSection(["/blog/"], [makeGroup("Engineering Blog", resolvedBlogItems)]);
  assignSection(["/kernel/"], [makeGroup("Kernel & Roadmap", kernelItems)]);
  assignSection(["/ai/"], [makeGroup("AI Agents & Governance", aiItems)]);
  assignSection(["/ux/"], [makeGroup("Clinical Canvas & UX", uxItems)]);
  assignSection(
    ["/strategy/"],
    [makeGroup("Product & Strategy", productItems)]
  );
  assignSection(["/components/"], [makeGroup("UI Components", componentItems)]);
  assignSection(["/api/"], [makeGroup("REST API Reference", apiItems)]);
  assignSection(
    ["/openapi/"],
    [makeGroup("OpenAPI Specifications", openapiSectionItems)]
  );
  assignSection(["/schema/"], [makeGroup("Data Model Reference", schemaItems)]);

  assignSection(
    ["/SDLC/"],
    [
      makeGroup("GxP & Validation", sdlcActiveItems),
      makeGroup("Baseline Specifications (Pre-Kernel)", sdlcBaselineItems),
    ]
  );
  assignSection(
    ["/CDISC/"],
    [makeGroup("Standards Reference", standardsItems)]
  );
  assignSection(["/agents/"], [makeGroup("Repository Policies", policyItems)]);
  assignSection(
    ["/specs/"],
    [makeGroup("Design Proposals (Pre-Kernel)", specsBaselineItems)]
  );

  assignSection(
    ["/migration/"],
    [makeGroup("Historical & Migration Records", migrationHistoricalItems)]
  );
  assignSection(
    ["/architecture/repository-migration/"],
    [makeGroup("Former Repository Migration", repoMigrationHistoricalItems)]
  );

  if (adrIndexContent || adrEntries.length > 0) {
    assignSection(
      ["/adr/"],
      buildAdrSidebarGroups(adrIndexContent, adrEntries, {
        adrDir:
          options.adrIndexContent === undefined
            ? path.dirname(adrIndexPath)
            : undefined,
      })
    );
  }

  const candidatePortalSections = [
    {
      text: "Clinical Kernel Specification",
      link: "/kernel/CLINICAL_KERNEL_SPECIFICATION",
    },
    { text: "Engineering Blog", link: "/blog/" },
    { text: "Product & Strategy", link: "/strategy/MARKET_HYPOTHESES" },
    { text: "UI Components", link: "/components/" },
    { text: "REST API Reference", link: "/api/" },
    { text: "Data Model Reference", link: "/schema/README" },
    {
      text: "GxP, Standards & Policies",
      link: "/SDLC/Signature_Token_Cryptographic_Contract",
    },
    { text: "Architectural Decision Records", link: "/adr/" },
    { text: "Historical & Migration Archive", link: "/migration/" },
  ].filter((item) => seenRoutes.has(item.link));

  const rootGroups = [
    makeGroup("Start Here", startHereItems),
    makeGroup("Developing & Running", devItems),
    ...[...customActiveSections.entries()].map(([title, items]) =>
      makeGroup(title, items)
    ),
    makeGroup("Repository Policies", rootPolicyItems),
    makeGroup("Portal Sections", candidatePortalSections),
  ];

  assignSection(["/"], rootGroups);

  return sidebar;
}

function stripHtmlTags(raw) {
  return raw
    .replace(/<[^>]+>/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

/**
 * Builds the build-time search filter and ranking catalog for VitePress MiniSearch.
 * Excludes `Baseline`, `Historical`, excluded portal specs, and superseded or pre-kernel ADRs,
 * and boosts `Normative` and Clinical Kernel documentation without client-side runtime functions.
 *
 * @param {string} indexPath
 * @param {{
 *   docsDir?: string,
 *   repoRoot?: string,
 *   adrIndexPath?: string,
 * }} [options]
 */
export function buildPortalSearchCatalog(indexPath, options = {}) {
  const docsDir = options.docsDir ?? path.dirname(indexPath);
  const repoRoot = options.repoRoot ?? path.resolve(docsDir, "..");
  const adrIndexPath =
    options.adrIndexPath ?? path.join(docsDir, "adr", "index.md");

  const indexContent = fs.existsSync(indexPath)
    ? fs.readFileSync(indexPath, "utf-8")
    : "";
  const adrIndexContent = fs.existsSync(adrIndexPath)
    ? fs.readFileSync(adrIndexPath, "utf-8")
    : "";

  const entries = parseDocumentationIndex(indexContent, { docsDir, repoRoot });
  const activeStatusesByRoute = new Map();
  const inactiveStatusesByRoute = new Map();

  for (const entry of entries) {
    if (!entry.link.startsWith("/")) continue;
    const route = normalizeRouteKey(entry.link);
    const targetSetMap = ACTIVE_STATUSES.has(entry.status)
      ? activeStatusesByRoute
      : inactiveStatusesByRoute;
    if (!targetSetMap.has(route)) {
      targetSetMap.set(route, new Set());
    }
    targetSetMap.get(route).add(entry.status);
  }

  const { kernelItems, securityItems, domainShardItems } =
    parseAdrIndexStructure(adrIndexContent, {
      adrDir: path.dirname(adrIndexPath),
    });
  const kernelAdrRoutes = new Set(
    kernelItems.map((item) => normalizeRouteKey(item.link))
  );
  const indexedAdrRoutes = new Set([
    ...kernelAdrRoutes,
    ...securityItems.map((item) => normalizeRouteKey(item.link)),
    ...domainShardItems.map((item) => normalizeRouteKey(item.link)),
  ]);

  const excludedDocSet = new Set(
    EXCLUDED_PORTAL_DOCS.map((rel) => rel.toLowerCase())
  );

  const toRoute = (relativePath) => {
    const clean = relativePath.replace(/\\/g, "/").replace(/^\/+/, "");
    if (clean === "index.md") return "/";
    if (/\/index\.md$/.test(clean)) {
      return `/${clean.replace(/\/index\.md$/, "/")}`;
    }
    return `/${clean.replace(/\.md$/i, "")}`;
  };

  const shouldIndexPage = (relativePath, frontmatter = null, rawSrc = "") => {
    if (!relativePath) return false;
    if (
      frontmatter?.draft === true ||
      /^true$/i.test(String(frontmatter?.draft ?? "").trim()) ||
      frontmatter?.search === false
    ) {
      return false;
    }

    const rel = relativePath.replace(/\\/g, "/").replace(/^\/+/, "");
    const relLower = rel.toLowerCase();
    if (!frontmatter && rawSrc && relLower.startsWith("blog/")) {
      const parsedDraft = parseBlogFrontmatter(rawSrc).frontmatter?.draft;
      if (
        parsedDraft === true ||
        /^true$/i.test(String(parsedDraft ?? "").trim())
      ) {
        return false;
      }
    }
    if (excludedDocSet.has(relLower)) {
      return false;
    }

    const route = toRoute(rel);
    const activeSet = activeStatusesByRoute.get(route);
    const inactiveSet = inactiveStatusesByRoute.get(route);

    if (relLower.startsWith("adr/")) {
      const fileName = path.basename(rel);
      if (relLower === "adr/legacy-index.md") return false;
      if (fileName.toLowerCase() === "index.md") return true;
      if (fileName.toLowerCase() === "template.md") return false;
      if (inactiveSet?.has("Historical") || inactiveSet?.has("Baseline")) {
        return false;
      }
      if (rawSrc && ADR_SUPERSEDED_RE.test(rawSrc)) {
        return false;
      }
      const dateMatch = fileName.match(ADR_DATE_PREFIX_RE);
      if (dateMatch && dateMatch[1] < CLINICAL_KERNEL_INCEPTION_DATE) {
        return false;
      }
      const segments = relLower.split("/");
      const isDomainShardAdr =
        segments.length === 3 &&
        ADR_DOMAIN_SHARDS.some(({ slug }) => slug === segments[1]);
      if (
        !isDomainShardAdr &&
        indexedAdrRoutes.size > 0 &&
        !indexedAdrRoutes.has(route)
      ) {
        return false;
      }
      return true;
    }

    if (
      relLower.startsWith("migration/") ||
      relLower.startsWith("architecture/") ||
      relLower.startsWith("specs/") ||
      relLower === "migration_workflow.md" ||
      relLower === "python_reference_inventory.md"
    ) {
      return false;
    }

    if (
      inactiveSet &&
      inactiveSet.size > 0 &&
      (!activeSet || activeSet.size === 0)
    ) {
      return false;
    }

    return true;
  };

  const prepareSearchMarkdown = (relativePath, src) => {
    const rel = relativePath.replace(/\\/g, "/").replace(/^\/+/, "");
    if (rel === "DOCUMENTATION_INDEX.md" || isCategoryShardRelPath(rel)) {
      const filteredLines = [];
      let skipSection = false;
      for (const line of src.split(/\r?\n/)) {
        const secMatch = line.match(/^##\s+(.+?)\s*$/);
        if (secMatch) {
          const sec = secMatch[1].trim();
          skipSection =
            sec === "Architecture decision records" ||
            sec === "Python-to-.NET port (completed)" ||
            sec === "Former repository migration program" ||
            sec === "Design proposals";
        }
        if (skipSection) continue;
        if (line.trim().startsWith("|")) {
          const cells = splitTableRow(line);
          if (
            cells.length >= 3 &&
            (cells[1] === "Baseline" || cells[1] === "Historical")
          ) {
            continue;
          }
        }
        filteredLines.push(line);
      }
      return filteredLines.join("\n");
    }

    if (rel === "adr/index.md") {
      const splitIdx = src.indexOf("\n### 1.");
      return splitIdx === -1 ? src : src.slice(0, splitIdx);
    }

    return src;
  };

  const getSearchBoostMultiplier = (relativePath) => {
    const rel = relativePath.replace(/\\/g, "/").replace(/^\/+/, "");
    const route = toRoute(rel);
    const activeSet = activeStatusesByRoute.get(route);

    if (activeSet?.has("Normative")) {
      return 3;
    }

    if (
      rel.startsWith("kernel/") ||
      rel.startsWith("ai/") ||
      rel.startsWith("ux/") ||
      rel.startsWith("adr/kernel/") ||
      kernelAdrRoutes.has(route) ||
      rel === "ARCHITECTURE.md" ||
      rel === "OPERATIONAL_DATABASE.md" ||
      rel === "SDLC/Signature_Token_Cryptographic_Contract.md" ||
      rel === "api/kernel.md" ||
      rel === "schema/kernel.md"
    ) {
      return 1;
    }

    return 0;
  };

  const applyHeadingBoostHtml = (relativePath, html) => {
    const multiplier = getSearchBoostMultiplier(relativePath);
    if (multiplier <= 0 || !html) return html;

    const h1Match = html.match(/<h1\b[^>]*>([\s\S]*?)<\/h1>/i);
    const docTitle = h1Match ? stripHtmlTags(h1Match[1]) : "";
    const extraPrefix = docTitle
      ? `${docTitle} clinical kernel `
      : "clinical kernel ";

    return html.replace(
      /<h([1-3])\b([^>]*)>([\s\S]*?)<\/h\1>/gi,
      (fullHeading, _level, _attrs, innerHtml) => {
        const headingText = stripHtmlTags(innerHtml);
        if (!headingText) return fullHeading;
        const repeated = Array.from(
          { length: multiplier },
          () => `${extraPrefix}${headingText}`
        ).join(" ");
        return `${fullHeading}\n<p>${repeated}</p>`;
      }
    );
  };

  return {
    shouldIndexPage,
    prepareSearchMarkdown,
    getSearchBoostMultiplier,
    applyHeadingBoostHtml,
    kernelAdrRoutes,
  };
}
