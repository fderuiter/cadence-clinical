import fs from "fs";
import path from "path";

import { buildAdrSidebarGroupsFromDisk } from "./adr.mjs";
import { getBlogSidebarItems, isDraftBlogPostFile } from "./blog.mjs";
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

function extractFirstHeadingText(filePath, fallback) {
  try {
    const content = fs.readFileSync(filePath, "utf-8");
    for (const line of content.split(/\r?\n/)) {
      const trimmed = line.trim();
      if (trimmed.startsWith("# ")) {
        return trimmed.slice(2).replace(/`/g, "").trim() || fallback;
      }
    }
  } catch {
    // Use fallback label if file cannot be read.
  }
  return fallback;
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
 * Builds domain-grouped sidebar entries for architectural decision records directly from `docs/adr/` on disk.
 *
 * @param {string} docsDir
 * @param {Array<{text: string, link: string}>} [indexAdrEntries]
 */
export function buildAdrSidebarGroups(docsDir, indexAdrEntries = []) {
  const fromDisk =
    typeof docsDir === "string" && fs.existsSync(path.join(docsDir, "adr"))
      ? buildAdrSidebarGroupsFromDisk(docsDir)
      : [];

  if (fromDisk.length > 0) {
    return fromDisk;
  }

  return dedupeItems(indexAdrEntries);
}

function discoverBlogItemsFromDisk(docsDir, indexBlogEntries) {
  const blogDir = path.join(docsDir, "blog");
  if (!fs.existsSync(blogDir)) {
    return indexBlogEntries;
  }

  const hasOverviewOnDisk = fs.existsSync(path.join(blogDir, "index.md"));
  const diskItems = getBlogSidebarItems(docsDir).filter(
    (item) => item.link !== "/blog/index" || hasOverviewOnDisk
  );

  const indexOverview = indexBlogEntries.find(
    (item) => normalizeRouteKey(item.link) === "/blog/index"
  );
  if (indexOverview && diskItems[0]?.link === "/blog/index") {
    diskItems[0] = { text: indexOverview.text, link: "/blog/index" };
  }

  return diskItems;
}

function discoverApiItemsFromDisk(docsDir, indexApiEntries) {
  const apiDir = path.join(docsDir, "api");
  if (!fs.existsSync(apiDir)) {
    return indexApiEntries;
  }

  const items = [];
  const indexOverview = indexApiEntries.find(
    (entry) => normalizeRouteKey(entry.link) === "/api/index"
  );
  const overviewPath = path.join(apiDir, "index.md");
  if (fs.existsSync(overviewPath)) {
    items.push({
      text: indexOverview?.text ?? "REST API reference",
      link: "/api/index",
    });
  }

  const serviceFiles = fs
    .readdirSync(apiDir)
    .filter((file) => file.endsWith(".md") && file.toLowerCase() !== "index.md")
    .sort((a, b) => a.localeCompare(b));

  for (const file of serviceFiles) {
    const slug = file.replace(/\.md$/i, "");
    const fullPath = path.join(apiDir, file);
    items.push({
      text: extractFirstHeadingText(fullPath, `${slug} API`),
      link: `/api/${slug}`,
    });
  }

  return [...items, ...indexApiEntries];
}

function discoverSchemaItemsFromDisk(docsDir, indexSchemaEntries) {
  const schemaDir = path.join(docsDir, "schema");
  if (!fs.existsSync(schemaDir)) {
    return indexSchemaEntries;
  }

  const items = [];
  const indexOverview = indexSchemaEntries.find(
    (entry) => normalizeRouteKey(entry.link) === "/schema/README"
  );
  const overviewPath = path.join(schemaDir, "README.md");
  if (fs.existsSync(overviewPath)) {
    items.push({
      text: indexOverview?.text ?? "Database schema",
      link: "/schema/README",
    });
  }

  const moduleFiles = fs
    .readdirSync(schemaDir)
    .filter(
      (file) =>
        file.endsWith(".md") &&
        file.toLowerCase() !== "readme.md" &&
        file.toLowerCase() !== "index.md"
    )
    .sort((a, b) => a.localeCompare(b));

  for (const file of moduleFiles) {
    const slug = file.replace(/\.md$/i, "");
    const fullPath = path.join(schemaDir, file);
    items.push({
      text: extractFirstHeadingText(fullPath, `${slug} database schema`),
      link: `/schema/${slug}`,
    });
  }

  return [...items, ...indexSchemaEntries];
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
 * Synthesizes the complete VitePress sidebar from `docs/DOCUMENTATION_INDEX.md` plus disk discovery
 * for `docs/adr/`, `docs/blog/`, `docs/api/`, and `docs/schema/`.
 *
 * @param {string} indexPath
 * @param {{
 *   docsDir?: string,
 *   repoRoot?: string,
 *   indexContent?: string,
 * }} [options]
 */
export function synthesizeSidebarFromIndex(indexPath, options = {}) {
  const docsDir = options.docsDir ?? path.dirname(indexPath);
  const repoRoot = options.repoRoot ?? path.resolve(docsDir, "..");

  const indexContent =
    options.indexContent ??
    (fs.existsSync(indexPath) ? fs.readFileSync(indexPath, "utf-8") : "");

  const allEntries = parseDocumentationIndex(indexContent, {
    docsDir,
    repoRoot,
  });

  const startHereItems = [
    { text: "Portal Home", link: "/" },
    { text: "Documentation Index", link: "/DOCUMENTATION_INDEX" },
  ];
  const indexBlogItems = [];
  const kernelItems = [];
  const devItems = [];
  const productItems = [];
  const componentItems = [];
  const indexApiItems = [];
  const indexSchemaItems = [];
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
      indexApiItems.push(entry);
      continue;
    }

    if (routeKey.startsWith("/schema/")) {
      indexSchemaItems.push(entry);
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
          indexBlogItems.push(entry);
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

  const blogItems = discoverBlogItemsFromDisk(docsDir, indexBlogItems);
  const apiItems = discoverApiItemsFromDisk(docsDir, indexApiItems);
  const schemaItems = discoverSchemaItemsFromDisk(docsDir, indexSchemaItems);

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

  const adrGroups = buildAdrSidebarGroups(docsDir, adrEntries);
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
