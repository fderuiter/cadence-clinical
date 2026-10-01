import { defineConfig } from "vitepress";
import { withMermaid } from "vitepress-plugin-mermaid";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

import { assertThemeLinksExist, repoLinksPlugin } from "./repoLinks.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

/**
 * Formats a filename or folder name into a Title Case display string.
 * @param {string} name
 * @return {string}
 */
function formatTitle(name) {
  return name
    .replace(/\.md$/i, "")
    .replace(/^(\d+)[-_]/, "")
    .replace(/[-_]/g, " ")
    .replace(/\b\w/g, (c) => c.toUpperCase())
    .trim();
}

/**
 * Parses frontmatter and first H1 title from a Markdown document.
 * @param {string} filePath
 * @param {string} defaultTitle
 * @return {{ title: string, excludeFromSidebar: boolean }}
 */
function getDocMetadata(filePath, defaultTitle) {
  try {
    const content = fs.readFileSync(filePath, "utf-8");
    const fmMatch = content.match(/^---\r?\n([\s\S]*?)\r?\n---/);
    let excludeFromSidebar = false;
    let title = null;

    if (fmMatch) {
      const yaml = fmMatch[1];
      if (/exclude_from_sidebar:\s*true/i.test(yaml)) {
        excludeFromSidebar = true;
      }
      const titleMatch = yaml.match(/^title:\s*["']?(.*?)["']?$/m);
      if (titleMatch) {
        title = titleMatch[1].trim();
      }
    }

    if (excludeFromSidebar) {
      return { excludeFromSidebar: true };
    }

    if (!title) {
      const h1Match = content.match(/^#\s+(.*)/m);
      if (h1Match) {
        title = h1Match[1].trim();
      }
    }

    return { title: title || defaultTitle, excludeFromSidebar: false };
  } catch (e) {
    return { title: defaultTitle, excludeFromSidebar: false };
  }
}

/**
 * Dynamically crawls a directory relative to `docs/` and builds sidebar items.
 * @param {string} dirRelativePath
 * @return {Array<{text: string, link?: string, collapsed?: boolean, items?: Array}>}
 */
function crawlDirectory(dirRelativePath) {
  const docsDir = path.resolve(__dirname, "..");
  const targetDir = path.join(docsDir, dirRelativePath);
  if (!fs.existsSync(targetDir)) return [];

  function scan(currentDir) {
    const items = [];
    const entries = fs.readdirSync(currentDir, { withFileTypes: true });
    entries.sort((a, b) => a.name.localeCompare(b.name));

    for (const entry of entries) {
      const fullPath = path.join(currentDir, entry.name);
      const relToDocs = path.relative(docsDir, fullPath).replace(/\\/g, "/");

      if (entry.isDirectory()) {
        if (
          entry.name === "runs" ||
          entry.name === ".vitepress" ||
          entry.name === "node_modules"
        ) {
          continue;
        }
        const subItems = scan(fullPath);
        if (subItems.length > 0) {
          items.push({
            text: formatTitle(entry.name),
            collapsed: true,
            items: subItems,
          });
        }
      } else if (entry.isFile() && entry.name.endsWith(".md")) {
        if (
          relToDocs.includes("/SDLC/runs/") ||
          relToDocs.includes("/runs/")
        ) {
          continue;
        }
        const meta = getDocMetadata(fullPath, formatTitle(entry.name));
        if (meta.excludeFromSidebar) continue;

        const link = "/" + relToDocs.replace(/\.md$/, "");
        items.push({
          text: meta.title,
          link: link,
        });
      }
    }
    return items;
  }

  return scan(targetDir);
}

function getKernelSidebar() {
  return crawlDirectory("kernel");
}

function getMigrationSidebar() {
  return crawlDirectory("migration");
}

function getSpecsSidebar() {
  return crawlDirectory("specs");
}

function getAgentsSidebar() {
  return crawlDirectory("agents");
}

function getCdiscSidebar() {
  return crawlDirectory("CDISC");
}

function getSchemaSidebar() {
  return crawlDirectory("schema");
}

/**
 * Builds domain-grouped sidebar entries for architectural decision records.
 * Parses `docs/adr/index.md` to extract domain headings and markdown links.
 *
 * @return {Array<{text: string, collapsed?: boolean, items: Array<{text: string, link: string}>}>} Grouped ADR sidebar entries.
 */
function getAdrSidebarGroups() {
  const indexPath = path.resolve(__dirname, "../adr/index.md");
  if (!fs.existsSync(indexPath)) return [];

  const content = fs.readFileSync(indexPath, "utf-8");
  const lines = content.split("\n");

  const groups = [];
  let currentGroup = null;

  for (const line of lines) {
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
        filename = filename.replace("docs/adr/", "");
      }
      if (filename.endsWith(".md")) {
        filename = filename.replace(".md", "");
      }
      currentGroup.items.push({
        text,
        link: `/adr/${filename}`,
      });
    }
  }

  if (currentGroup && currentGroup.items.length > 0) {
    groups.push(currentGroup);
  }

  return [
    {
      text: "ADR Overview & Log",
      link: "/adr/index",
    },
    ...groups,
  ];
}

/**
 * Builds sidebar entries from the Markdown files in the SDLC documentation directory.
 * @return {Array<{text: string, link: string}>} Sorted sidebar entries using each file's first H1 heading or filename as its text.
 */
function getSdlcSidebar() {
  const sdlcDir = path.resolve(__dirname, "../SDLC");
  if (!fs.existsSync(sdlcDir)) return [];
  const files = fs.readdirSync(sdlcDir);
  return files
    .filter((file) => file.endsWith(".md"))
    .sort()
    .map((file) => {
      const filePath = path.join(sdlcDir, file);
      const meta = getDocMetadata(filePath, file.replace(".md", ""));
      return { text: meta.title, link: `/SDLC/${file}` };
    });
}

/**
 * Builds sidebar entries for programmatically generated API Reference documentation.
 * @return {Array<{text: string, link?: string, collapsed?: boolean, items?: Array<{text: string, link: string}>}>}
 */
function getApiSidebarGroups() {
  return [
    {
      text: "API Overview & Architecture",
      link: "/api/index",
    },
    {
      text: "Core Kernel API",
      link: "/api/kernel",
    },
    {
      text: "Protocol & Study Design",
      collapsed: false,
      items: [
        { text: "Designer / USDM (8001)", link: "/api/designer" },
        { text: "Knowledge & SOPs (8015)", link: "/api/knowledge" },
        { text: "AI Gateway (8017)", link: "/api/ai_gateway" },
      ],
    },
    {
      text: "Clinical Operations & Execution",
      collapsed: false,
      items: [
        { text: "EDC Execution (8002)", link: "/api/execution" },
        { text: "CTMS Operations (8007)", link: "/api/ctms" },
        { text: "Organization Directory (8012)", link: "/api/org" },
        { text: "Clinical Issue Hub (8009)", link: "/api/tickets" },
        { text: "Payments & Grants (8016)", link: "/api/payments" },
      ],
    },
    {
      text: "Patient & Interoperability",
      collapsed: false,
      items: [
        { text: "FHIR & eSource Gateway (8004)", link: "/api/interop" },
        { text: "Notifications Dispatcher (8006)", link: "/api/notifications" },
        { text: "Electronic Consent (8011)", link: "/api/econsent" },
      ],
    },
    {
      text: "Safety, Quality & Governance",
      collapsed: false,
      items: [
        { text: "Safety & Pharmacovigilance (8008)", link: "/api/safety" },
        { text: "Quality & CAPA (8005)", link: "/api/quality" },
        { text: "Blinded Imaging Adjudication (8014)", link: "/api/imaging" },
        { text: "eTMF Document Archive (8003)", link: "/api/etmf" },
        { text: "eISF Site Binders (8010)", link: "/api/eisf" },
        { text: "FileShare & StoragePort (8013)", link: "/api/fileshare" },
      ],
    },
    {
      text: "OpenAPI Specifications",
      link: "/openapi/README",
    },
    {
      text: "Gateway",
      collapsed: false,
      items: [
        {
          text: "API Gateway (8000)",
          link: "https://github.com/fderuiter/cadence-clinical/blob/main/apps/gateway/README.md",
        },
      ],
    },
  ];
}

const config = defineConfig({
  title: "Cadence Clinical Portal",
  description:
    "Metadata-Driven Clinical Execution Platform Documentation Portal",
  base: process.env.VITEPRESS_BASE || "/cadence-clinical/",
  outDir: path.resolve(__dirname, "dist"),
  srcExclude: ["**/SDLC/runs/**", "**/runs/**"],
  // Links VitePress cannot serve are pointed at GitHub by repoLinksPlugin; any other dead link fails the build.
  ignoreDeadLinks: false,
  markdown: {
    config(md) {
      md.use(repoLinksPlugin, {
        docsDir: path.resolve(__dirname, ".."),
        repoRoot: path.resolve(__dirname, "../.."),
      });
    },
  },
  vite: {
    build: {
      chunkSizeWarningLimit: 2000,
    },
  },
  themeConfig: {
    search: {
      provider: "local",
    },
    nav: [
      { text: "Home", link: "/" },
      { text: "Doc Index", link: "/DOCUMENTATION_INDEX" },
      { text: "Kernel Spec", link: "/kernel/CLINICAL_KERNEL_SPECIFICATION" },
      { text: "Architecture", link: "/ARCHITECTURE" },
      { text: "ADRs", link: "/adr/index" },
      { text: "API Reference", link: "/api/index" },
      { text: "CLI Reference", link: "/cli" },
      { text: "Agents", link: "/AGENTS" },
    ],
    sidebar: [
      {
        text: "Overview & Guides",
        items: [
          { text: "Welcome & Readme", link: "/" },
          { text: "Documentation Index", link: "/DOCUMENTATION_INDEX" },
          { text: "System Architecture", link: "/ARCHITECTURE" },
          {
            text: "Local Development Environment",
            link: "/LOCAL_DEV_ENVIRONMENT",
          },
          { text: "Environment Variables", link: "/ENVIRONMENT_VARIABLES" },
          { text: "Database and Migrations", link: "/OPERATIONAL_DATABASE" },
          { text: "Testing and Requirement Qualification", link: "/TESTING" },
          { text: "CI and Quality Gates", link: "/CI" },
          { text: "Developer CLI (`cadence`)", link: "/cli" },
          { text: "Agent & Contributor Rules", link: "/AGENTS" },
          { text: "Project License", link: "/LICENSE" },
          { text: "Inbound Email Integration", link: "/OPERATIONAL_INBOUND_EMAIL" },
          { text: "Laboratory Reference Ranges", link: "/OPERATIONAL_LAB_RANGES" },
          { text: "Python Reference Inventory", link: "/PYTHON_REFERENCE_INVENTORY" },
          { text: "Migration Workflow Playbook", link: "/MIGRATION_WORKFLOW" },
          { text: "Web API Migration", link: "/web/API_MIGRATION" },
        ],
      },
      {
        text: "Clinical Kernel",
        items: getKernelSidebar(),
      },
      {
        text: "Design Specifications",
        collapsed: true,
        items: getSpecsSidebar(),
      },
      {
        text: "Agent Working Notes",
        collapsed: true,
        items: getAgentsSidebar(),
      },
      {
        text: "Python-to-.NET Port (Historical)",
        collapsed: true,
        items: getMigrationSidebar(),
      },
      {
        text: "REST API Reference",
        items: getApiSidebarGroups(),
      },
      {
        text: "Enterprise UI Components",
        items: [
          { text: "Component Catalog Overview", link: "/components/index" },
          { text: "ClinicalDataTable", link: "/components/data-table" },
          { text: "ClinicalModal", link: "/components/modal" },
          { text: "PersonaSwitcher", link: "/components/persona-switcher" },
          { text: "AuditLogViewer", link: "/components/audit-viewer" },
        ],
      },
      {
        text: "Clinical Canvas & UX",
        items: [
          { text: "Clinical Canvas UX Interaction Model", link: "/ux/CLINICAL_CANVAS" },
        ],
      },
      {
        text: "CDISC Data Standards",
        collapsed: true,
        items: getCdiscSidebar(),
      },
      {
        text: "Database Schemas",
        collapsed: true,
        items: getSchemaSidebar(),
      },
      {
        text: "Baseline Specifications",
        items: [
          { text: "Data Lifecycle Management", link: "/DATA_LIFECYCLE" },
          { text: "Feature Matrix", link: "/FEATURE_MATRIX" },
          { text: "Functional Product Specification", link: "/FUNCTIONAL_PRODUCT_SPECIFICATION" },
          { text: "Competitive Analysis & Market Superiority", link: "/COMPETITIVE_ANALYSIS_AND_MARKET_SUPERIORITY" },
          { text: "System Requirements Specification (SRS)", link: "/SRS" },
        ],
      },
      {
        text: "SDLC Compliance Guidelines",
        items: getSdlcSidebar(),
      },
      {
        text: "Architectural Decision Records (ADRs)",
        items: getAdrSidebarGroups(),
      },
    ],
  },
});

// VitePress checks links inside pages but not the nav and sidebar, so check those here.
assertThemeLinksExist(config.themeConfig, path.resolve(__dirname, ".."));

export default withMermaid(config);
