import fs from "fs";
import path from "path";

import { isDraftBlogPostFile } from "./blog.mjs";
import { splitAnchor, toRepoUrl } from "./repoLinks.mjs";
import rootDocsModule from "./rootDocs.cjs";

const { STAGED_ROOT_DOCS } = rootDocsModule;

const ACTIVE_STATUSES = new Set(["Normative", "Current", "Generated"]);
const MARKDOWN_LINK_RE = /\[((?:[^\]]|\\\])+)\]\(([^)]+)\)/g;

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
export function toSidebarLink(rawHref, { docsDir, repoRoot }) {
  const href = rawHref.trim();
  if (!href) return null;

  if (/^([a-z][a-z0-9+.-]*:)/i.test(href)) {
    return href;
  }

  if (href.startsWith("/")) {
    return href;
  }

  const [rawTarget, anchor = ""] = splitAnchor(href);
  const cleanTarget = rawTarget.split("?")[0].trim();
  if (!cleanTarget) {
    return anchor ? `/DOCUMENTATION_INDEX${anchor}` : "/DOCUMENTATION_INDEX";
  }

  const absolute = path.resolve(docsDir, decodeURI(cleanTarget));
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
        return relToDocs === "" ? `/${anchor}` : `/${relToDocs}/index${anchor}`;
      }
      return toRepoUrl(href, docsDir, docsDir, repoRoot);
    }

    if (exists && !/\.(md|html)$/i.test(absolute)) {
      return toRepoUrl(href, docsDir, docsDir, repoRoot);
    }

    const mdCandidate = exists
      ? absolute
      : fs.existsSync(`${absolute}.md`)
        ? `${absolute}.md`
        : null;
    if (mdCandidate && isDraftBlogPostFile(mdCandidate)) {
      return toRepoUrl(href, docsDir, docsDir, repoRoot);
    }

    if (relToDocs.toLowerCase() === "index.md") {
      return `/${anchor}`;
    }

    return `/${relToDocs.replace(/\.(md|html)$/i, "")}${anchor}`;
  }

  return toRepoUrl(href, docsDir, docsDir, repoRoot);
}

/**
 * Parses `docs/DOCUMENTATION_INDEX.md` into structured row records with section, status, label, rawHref, and portal link.
 *
 * @param {string} indexContent
 * @param {{ docsDir: string, repoRoot: string }} options
 */
export function parseDocumentationIndex(indexContent, { docsDir, repoRoot }) {
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
      const link = toSidebarLink(rawHref, { docsDir, repoRoot });
      if (!link) continue;
      entries.push({
        section: currentSection,
        status,
        text,
        rawHref,
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
        const link = toSidebarLink(rawHref, { docsDir, repoRoot });
        if (!link) continue;
        entries.push({
          section: currentSection,
          status: "Current",
          text: unescapeLabel(m[1]),
          rawHref,
          link,
          fromDescription: true,
        });
      }
    }
  }

  return entries;
}

/**
 * Builds domain-grouped sidebar entries for architectural decision records from `docs/adr/index.md`,
 * ensuring any ADR listed in `docs/DOCUMENTATION_INDEX.md` is also reachable.
 *
 * @param {string} adrIndexContent
 * @param {Array<{text: string, link: string}>} indexAdrEntries
 */
export function buildAdrSidebarGroups(adrIndexContent, indexAdrEntries = []) {
  const groups = [];
  let currentGroup = null;
  const coveredLinks = new Set(["/adr/index", "/adr/TEMPLATE"]);

  if (adrIndexContent) {
    for (const line of adrIndexContent.split(/\r?\n/)) {
      const headingMatch = line.match(/^###\s+(.*)/);
      if (headingMatch) {
        if (currentGroup && currentGroup.items.length > 0) {
          groups.push(currentGroup);
        }
        currentGroup = {
          text: headingMatch[1].trim(),
          collapsed: true,
          items: [],
        };
        continue;
      }

      const linkMatch = line.match(/^-\s*\[(.*?)\]\((.*?)\)/);
      if (linkMatch && currentGroup) {
        const text = linkMatch[1].trim();
        let filename = linkMatch[2].trim();
        if (filename.startsWith("docs/adr/")) {
          filename = filename.slice("docs/adr/".length);
        }
        filename = splitAnchor(filename)[0].replace(/\.md$/i, "");
        const link = `/adr/${filename}`;
        currentGroup.items.push({ text, link });
        coveredLinks.add(link);
      }
    }

    if (currentGroup && currentGroup.items.length > 0) {
      groups.push(currentGroup);
    }
  }

  const uncategorized = dedupeItems(
    indexAdrEntries
      .filter((entry) => !coveredLinks.has(splitAnchor(entry.link)[0]))
      .map((entry) => ({ text: entry.text, link: entry.link }))
  );

  if (uncategorized.length > 0) {
    groups.push({
      text: "Additional Decision Records",
      collapsed: true,
      items: uncategorized,
    });
  }

  const headerItems = [{ text: "ADR Overview & Log", link: "/adr/index" }];
  if (indexAdrEntries.some((entry) => entry.link === "/adr/TEMPLATE")) {
    headerItems.push({ text: "ADR Template", link: "/adr/TEMPLATE" });
  }

  return [...headerItems, ...groups];
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

/**
 * Synthesizes the complete VitePress sidebar from `docs/DOCUMENTATION_INDEX.md`.
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
  });

  const startHereItems = [
    { text: "Documentation Index", link: "/DOCUMENTATION_INDEX" },
  ];
  const blogItems = [];
  const kernelItems = [];
  const devItems = [];
  const productItems = [];
  const componentItems = [];
  const apiItems = [];
  const schemaItems = [];
  const gxpStandardsPolicyItems = [];
  const adrEntries = [];
  const baselineItems = [];
  const historicalItems = [];
  const customActiveSections = new Map();

  for (const entry of allEntries) {
    const routeKey = normalizeRouteKey(entry.link);

    if (entry.status === "Baseline") {
      baselineItems.push(entry);
      continue;
    }

    if (entry.status === "Historical") {
      historicalItems.push(entry);
      continue;
    }

    if (!ACTIVE_STATUSES.has(entry.status)) {
      continue;
    }

    if (entry.section === "Architecture decision records") {
      adrEntries.push(entry);
      continue;
    }

    if (routeKey.startsWith("/api/")) {
      apiItems.push(entry);
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
      case "Standards reference material":
      case "Repository policies":
        gxpStandardsPolicyItems.push(entry);
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

  // Keep the blog overview at the top and sort published dated posts newest-first.
  blogItems.sort((a, b) => {
    const aKey = normalizeRouteKey(a.link);
    const bKey = normalizeRouteKey(b.link);
    if (aKey === "/blog/index") return -1;
    if (bKey === "/blog/index") return 1;
    return bKey.localeCompare(aKey);
  });

  // Keep overview pages at the top of generated reference and component sections.
  apiItems.sort((a, b) => {
    if (normalizeRouteKey(a.link) === "/api/index") return -1;
    if (normalizeRouteKey(b.link) === "/api/index") return 1;
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
    if (normalizeRouteKey(a.link) === "/components/index") return -1;
    if (normalizeRouteKey(b.link) === "/components/index") return 1;
    return 0;
  });

  const sidebar = [];

  const pushGroup = (text, rawItems, collapsed) => {
    const items = dedupeItems(rawItems);
    if (items.length === 0) return;
    const group = { text, items };
    if (typeof collapsed === "boolean") {
      group.collapsed = collapsed;
    }
    sidebar.push(group);
  };

  pushGroup("Start Here", startHereItems, false);
  pushGroup("Engineering Blog", blogItems, false);
  pushGroup("Kernel & Roadmap", kernelItems, false);
  pushGroup("Developing & Running", devItems, false);
  pushGroup("Product & Strategy", productItems, false);

  for (const [sectionTitle, items] of customActiveSections.entries()) {
    pushGroup(sectionTitle, items, false);
  }

  pushGroup("UI Components", componentItems, true);
  pushGroup("REST API Reference", apiItems, true);
  pushGroup("Data Model Reference", schemaItems, true);
  pushGroup("GxP, Standards & Policies", gxpStandardsPolicyItems, true);

  const adrGroups = buildAdrSidebarGroups(adrIndexContent, adrEntries);
  if (adrGroups.length > 0) {
    sidebar.push({
      text: "Architectural Decision Records (ADRs)",
      collapsed: true,
      items: adrGroups,
    });
  }

  const archiveSubgroups = [];
  const dedupedBaseline = dedupeItems(baselineItems);
  if (dedupedBaseline.length > 0) {
    archiveSubgroups.push({
      text: "Baseline Specifications (Pre-Kernel)",
      collapsed: true,
      items: dedupedBaseline,
    });
  }
  const dedupedHistorical = dedupeItems(historicalItems);
  if (dedupedHistorical.length > 0) {
    archiveSubgroups.push({
      text: "Historical & Migration Records",
      collapsed: true,
      items: dedupedHistorical,
    });
  }
  if (archiveSubgroups.length > 0) {
    sidebar.push({
      text: "Baseline & Historical Records",
      collapsed: true,
      items: archiveSubgroups,
    });
  }

  return sidebar;
}
