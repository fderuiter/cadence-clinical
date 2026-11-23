import fs from "fs";
import path from "path";

import { isDraftBlogPostFile } from "./blog.mjs";
import {
  EXCLUDED_PORTAL_DOCS,
  isExcludedPortalDocFile,
  isPublishedOpenApiSpecPath,
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
export function toSidebarLink(rawHref, { docsDir, repoRoot }) {
  const href = rawHref.trim();
  if (!href) return null;

  if (/^([a-z][a-z0-9+.-]*:)/i.test(href)) {
    return href;
  }

  if (href.startsWith("/")) {
    const [cleanRootPath] = splitAnchor(href);
    const normalizedPage = cleanRootPath
      .split("?")[0]
      .replace(/^\//, "")
      .replace(/\.(html|md)$/i, "");
    if (normalizedPage) {
      const excludedCandidate = path.join(docsDir, `${normalizedPage}.md`);
      if (isExcludedPortalDocFile(excludedCandidate, docsDir)) {
        return toRepoUrl(href, docsDir, docsDir, repoRoot);
      }
    }
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
      if (isPublishedOpenApiSpecPath(relToDocs)) {
        return `/${relToDocs}${anchor}`;
      }
      return toRepoUrl(href, docsDir, docsDir, repoRoot);
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
    if (
      mdCandidate &&
      (isDraftBlogPostFile(mdCandidate) ||
        isExcludedPortalDocFile(mdCandidate, docsDir))
    ) {
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

const MAX_INLINE_KERNEL_SIDEBAR_ITEMS = 8;

/**
 * Extracts active Clinical Kernel ADR links, K-decision table items, security ADR routes,
 * and domain section headings from `docs/adr/index.md`.
 *
 * @param {string} adrIndexContent
 */
function parseAdrIndexStructure(adrIndexContent) {
  let kernelHeading = "0. Clinical Kernel (K001–K037)";
  const kernelItems = [];
  const securityItems = [];
  const kDecisionTableItems = [];
  const domainHeadings = [];
  let currentDomain = "";

  if (!adrIndexContent) {
    return {
      kernelHeading,
      kernelItems,
      securityItems,
      kDecisionTableItems,
      domainHeadings,
    };
  }

  let inKernelDecisionTable = false;
  for (const line of adrIndexContent.split(/\r?\n/)) {
    const h2Match = line.match(/^##\s+(.+?)\s*$/);
    if (h2Match) {
      inKernelDecisionTable = /Clinical Kernel Decisions/i.test(
        h2Match[1].trim()
      );
      currentDomain = "";
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
      currentDomain = title;
      if (/^0\.\s*/.test(title) || /Clinical Kernel/i.test(title)) {
        kernelHeading = title;
      }
      domainHeadings.push(title);
      continue;
    }

    if (!currentDomain) continue;

    const linkMatch = line.match(/^-\s*\[(.*?)\]\((.*?)\)/);
    if (linkMatch) {
      const text = linkMatch[1].trim();
      let filename = linkMatch[2].trim();
      if (filename.startsWith("./")) {
        filename = filename.slice(2);
      } else if (filename.startsWith("docs/adr/")) {
        filename = filename.slice("docs/adr/".length);
      }
      filename = splitAnchor(filename)[0].replace(/\.md$/i, "");
      const item = { text, link: `/adr/${filename}` };
      if (
        /^0\.\s*/.test(currentDomain) ||
        /Clinical Kernel/i.test(currentDomain)
      ) {
        kernelItems.push(item);
      } else if (/Security\s*&\s*Identity/i.test(currentDomain)) {
        securityItems.push(item);
      }
    }
  }

  return {
    kernelHeading,
    kernelItems,
    securityItems,
    kDecisionTableItems,
    domainHeadings,
  };
}

/**
 * Builds a compact `/adr/` sidebar listing the ADR overview, active Clinical Kernel decisions,
 * and domain decision log section links into `/adr/index#...`.
 *
 * @param {string} adrIndexContent
 * @param {Array<{text: string, link: string, status?: string}>} indexAdrEntries
 */
export function buildAdrSidebarGroups(adrIndexContent, indexAdrEntries = []) {
  const historicalAdrRoutes = new Set(
    indexAdrEntries
      .filter((entry) => entry.status === "Historical")
      .map((entry) => normalizeRouteKey(entry.link))
  );

  const overviewItems = [{ text: "ADR Overview & Log", link: "/adr/index" }];
  if (
    indexAdrEntries.some(
      (entry) => normalizeRouteKey(entry.link) === "/adr/TEMPLATE"
    ) ||
    /\(TEMPLATE\.md\)/.test(adrIndexContent ?? "")
  ) {
    overviewItems.push({ text: "ADR Template", link: "/adr/TEMPLATE" });
  }

  const { kernelHeading, kernelItems, kDecisionTableItems, domainHeadings } =
    parseAdrIndexStructure(adrIndexContent);

  const candidateKernelSource =
    kDecisionTableItems.length > 0 ? kDecisionTableItems : kernelItems;

  const filteredKernelItems = dedupeItems(
    candidateKernelSource.filter(
      (item) => !historicalAdrRoutes.has(normalizeRouteKey(item.link))
    )
  );

  const compactKernelItems =
    filteredKernelItems.length > MAX_INLINE_KERNEL_SIDEBAR_ITEMS
      ? [
          ...filteredKernelItems.slice(0, MAX_INLINE_KERNEL_SIDEBAR_ITEMS),
          {
            text: "All Kernel Decisions (K001–K037)",
            link: `/adr/index#${vitepressSlugify(kernelHeading)}`,
          },
        ]
      : filteredKernelItems;

  const domainLogItems = dedupeItems(
    domainHeadings
      .filter(
        (heading) =>
          compactKernelItems.length === 0 || heading !== kernelHeading
      )
      .map((heading) => ({
        text: heading,
        link: `/adr/index#${vitepressSlugify(heading)}`,
      }))
  );

  const groups = [
    {
      text: "ADR Overview",
      collapsed: false,
      items: overviewItems,
    },
  ];

  if (compactKernelItems.length > 0) {
    groups.push({
      text: kernelHeading,
      collapsed: false,
      items: compactKernelItems,
    });
  }

  if (domainLogItems.length > 0) {
    groups.push({
      text: "Domain Decision Logs",
      collapsed: false,
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

function makeGroup(text, rawItems, collapsed) {
  const items = dedupeItems(rawItems);
  if (items.length === 0) return null;
  const group = { text, items };
  if (typeof collapsed === "boolean") {
    group.collapsed = collapsed;
  }
  return group;
}

const CONTRACT_MATURITY_OVERRIDES = new Map([
  ["gateway", "Active"],
  ["intelligence", "Active"],
  ["execution_forms", "Active"],
]);

/**
 * Reads `packages/topology/src/ServiceTopology.cs` to resolve `Active` vs `Scaffold`
 * maturity for every OpenAPI contract page under `/api/<contract>`.
 *
 * @param {string} repoRoot
 * @returns {Map<string, "Active" | "Scaffold">}
 */
export function loadServiceContractMaturity(repoRoot) {
  const maturityMap = new Map(CONTRACT_MATURITY_OVERRIDES);
  const topologyPath = path.join(
    repoRoot,
    "packages",
    "topology",
    "src",
    "ServiceTopology.cs"
  );
  if (!fs.existsSync(topologyPath)) {
    return maturityMap;
  }

  const source = fs.readFileSync(topologyPath, "utf-8");
  const entryRegex = /new\(\s*"([a-z0-9_]+)"\s*,[\s\S]*?\)(?=\s*(?:,|\];))/g;
  for (const match of source.matchAll(entryRegex)) {
    const name = match[1];
    const isScaffold = /Maturity:\s*ServiceMaturity\.Scaffold\b/.test(match[0]);
    maturityMap.set(name, isScaffold ? "Scaffold" : "Active");
  }
  return maturityMap;
}

function formatMaturityBadge(maturity) {
  if (maturity === "Scaffold") {
    return '<span class="VPBadge warning">Scaffold</span>';
  }
  if (maturity === "Active") {
    return '<span class="VPBadge tip">Active</span>';
  }
  return "";
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
  const contractMaturity =
    options.contractMaturity ?? loadServiceContractMaturity(repoRoot);

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

    if (entry.section === "Architecture decision records") {
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
      if (routeKey !== "/api/index") {
        const contractName = routeKey.slice("/api/".length);
        const badge = formatMaturityBadge(contractMaturity.get(contractName));
        apiItems.push(
          badge ? { ...entry, text: `${entry.text} ${badge}` } : entry
        );
      } else {
        apiItems.push(entry);
      }
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
    const aKey = normalizeRouteKey(a.link);
    const bKey = normalizeRouteKey(b.link);
    if (aKey === "/api/index") return -1;
    if (bKey === "/api/index") return 1;
    return aKey.localeCompare(bKey);
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

  const sidebar = {};
  const assignSection = (prefixes, groups) => {
    const validGroups = groups.filter(Boolean);
    if (validGroups.length === 0) return;
    for (const prefix of prefixes) {
      sidebar[prefix] = validGroups;
    }
  };

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
  const openapiSectionItems = seenRoutes.has("/api/index")
    ? [{ text: "REST API Reference", link: "/api/index" }, ...openapiItems]
    : openapiItems;

  assignSection(["/blog/"], [makeGroup("Engineering Blog", blogItems, false)]);
  assignSection(
    ["/kernel/"],
    [makeGroup("Kernel & Roadmap", kernelItems, false)]
  );
  assignSection(
    ["/ai/"],
    [makeGroup("AI Agents & Governance", aiItems, false)]
  );
  assignSection(["/ux/"], [makeGroup("Clinical Canvas & UX", uxItems, false)]);
  assignSection(
    ["/strategy/"],
    [makeGroup("Product & Strategy", productItems, false)]
  );
  assignSection(
    ["/components/"],
    [makeGroup("UI Components", componentItems, false)]
  );
  assignSection(["/api/"], [makeGroup("REST API Reference", apiItems, false)]);
  assignSection(
    ["/openapi/"],
    [makeGroup("OpenAPI Specifications", openapiSectionItems, false)]
  );
  assignSection(
    ["/schema/"],
    [makeGroup("Data Model Reference", schemaItems, false)]
  );

  assignSection(
    ["/SDLC/"],
    [
      makeGroup("GxP & Validation", sdlcActiveItems, false),
      makeGroup(
        "Baseline Specifications (Pre-Kernel)",
        sdlcBaselineItems,
        true
      ),
    ]
  );
  assignSection(
    ["/CDISC/"],
    [makeGroup("Standards Reference", standardsItems, false)]
  );
  assignSection(
    ["/agents/"],
    [makeGroup("Repository Policies", policyItems, false)]
  );
  assignSection(
    ["/specs/"],
    [makeGroup("Design Proposals (Pre-Kernel)", specsBaselineItems, false)]
  );

  assignSection(
    ["/migration/"],
    [
      makeGroup(
        "Historical & Migration Records",
        migrationHistoricalItems,
        false
      ),
    ]
  );
  assignSection(
    ["/architecture/repository-migration/"],
    [
      makeGroup(
        "Former Repository Migration",
        repoMigrationHistoricalItems,
        false
      ),
    ]
  );

  if (adrIndexContent || adrEntries.length > 0) {
    assignSection(
      ["/adr/"],
      buildAdrSidebarGroups(adrIndexContent, adrEntries)
    );
  }

  const candidatePortalSections = [
    {
      text: "Clinical Kernel Specification",
      link: "/kernel/CLINICAL_KERNEL_SPECIFICATION",
    },
    { text: "Engineering Blog", link: "/blog/index" },
    { text: "Product & Strategy", link: "/strategy/MARKET_HYPOTHESES" },
    { text: "UI Components", link: "/components/index" },
    { text: "REST API Reference", link: "/api/index" },
    { text: "Data Model Reference", link: "/schema/README" },
    {
      text: "GxP, Standards & Policies",
      link: "/SDLC/Signature_Token_Cryptographic_Contract",
    },
    { text: "Architectural Decision Records", link: "/adr/index" },
    { text: "Historical & Migration Archive", link: "/migration/index" },
  ].filter((item) => seenRoutes.has(item.link));

  const rootGroups = [
    makeGroup("Start Here", startHereItems, false),
    makeGroup("Developing & Running", devItems, false),
    ...[...customActiveSections.entries()].map(([title, items]) =>
      makeGroup(title, items, false)
    ),
    makeGroup("Repository Policies", rootPolicyItems, true),
    makeGroup("Portal Sections", candidatePortalSections, false),
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

  const { kernelItems, securityItems } =
    parseAdrIndexStructure(adrIndexContent);
  const kernelAdrRoutes = new Set(
    kernelItems.map((item) => normalizeRouteKey(item.link))
  );
  const indexedAdrRoutes = new Set([
    ...kernelAdrRoutes,
    ...securityItems.map((item) => normalizeRouteKey(item.link)),
  ]);

  const excludedDocSet = new Set(
    EXCLUDED_PORTAL_DOCS.map((rel) => rel.toLowerCase())
  );

  const toRoute = (relativePath) => {
    const clean = relativePath.replace(/\\/g, "/").replace(/^\/+/, "");
    if (clean.toLowerCase() === "index.md") return "/";
    return `/${clean.replace(/\.md$/i, "")}`;
  };

  const shouldIndexPage = (relativePath, frontmatter = null, rawSrc = "") => {
    if (!relativePath) return false;
    if (
      frontmatter?.draft === true ||
      frontmatter?.draft === "true" ||
      frontmatter?.search === false
    ) {
      return false;
    }

    const rel = relativePath.replace(/\\/g, "/").replace(/^\/+/, "");
    const relLower = rel.toLowerCase();
    if (excludedDocSet.has(relLower)) {
      return false;
    }

    const route = toRoute(rel);
    const activeSet = activeStatusesByRoute.get(route);
    const inactiveSet = inactiveStatusesByRoute.get(route);

    if (relLower.startsWith("adr/")) {
      const fileName = path.basename(rel);
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
      if (indexedAdrRoutes.size > 0 && !indexedAdrRoutes.has(route)) {
        return false;
      }
      return true;
    }

    if (
      relLower.startsWith("migration/") ||
      relLower.startsWith("architecture/repository-migration/") ||
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
    if (rel === "DOCUMENTATION_INDEX.md") {
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
