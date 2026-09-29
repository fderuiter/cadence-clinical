import { defineConfig } from "vitepress";
import { withMermaid } from "vitepress-plugin-mermaid";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

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
      const content = fs.readFileSync(filePath, "utf-8");
      const titleMatch = content.match(/^#\s+(.*)/m);
      const title = titleMatch ? titleMatch[1].trim() : file.replace(".md", "");
      return { text: title, link: `/SDLC/${file}` };
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
      text: "Gateway & Platform Infrastructure",
      collapsed: false,
      items: [
        { text: "API Gateway Proxy (8000)", link: "/api/gateway" },
        { text: "Compliance Engine", link: "/api/compliance" },
      ],
    },
  ];
}

export default withMermaid(
  defineConfig({
    title: "Cadence Clinical Portal",
    description:
      "Metadata-Driven Clinical Execution Platform Documentation Portal",
    base: process.env.VITEPRESS_BASE || "/cadence-clinical/",
    outDir: path.resolve(__dirname, "dist"),
    srcExclude: ["**/SDLC/runs/**", "**/runs/**"],
    ignoreDeadLinks: true,
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
            { text: "CI and Quality Gates", link: "/CI" },
            { text: "Developer CLI (`cadence`)", link: "/cli" },
            { text: "Agent & Contributor Rules", link: "/AGENTS" },
          ],
        },
        {
          text: "Clinical Kernel",
          items: [
            {
              text: "Kernel Specification v0.2",
              link: "/kernel/CLINICAL_KERNEL_SPECIFICATION",
            },
            {
              text: "Roadmap (§15)",
              link: "/kernel/CLINICAL_KERNEL_SPECIFICATION#_15-roadmap",
            },
            {
              text: "Execution Migration Inventory",
              link: "/kernel/EXECUTION_MIGRATION_INVENTORY",
            },
            {
              text: "Clock Migration Backlog",
              link: "/kernel/CLOCK_MIGRATION_BACKLOG",
            },
          ],
        },
        {
          text: "Python-to-.NET Port (Historical)",
          collapsed: true,
          items: [
            { text: "Modernization Hub Overview", link: "/migration/index" },
            {
              text: "Surface & Data Topology Audit",
              link: "/migration/SYSTEM_TOPOLOGY_AUDIT",
            },
            {
              text: "Dependency & Parity Catalog",
              link: "/migration/DEPENDENCY_AND_PARITY_CATALOG",
            },
            {
              text: "Bounded Context Migration Matrix",
              link: "/migration/BOUNDED_CONTEXT_MIGRATION_MATRIX",
            },
            {
              text: "Solution Scaffold Guide",
              link: "/migration/SCAFFOLD_GUIDE",
            },
            {
              text: "Migration Workflow Playbook",
              link: "/MIGRATION_WORKFLOW",
            },
          ],
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
          text: "Baseline Specifications",
          items: [
            { text: "Data Lifecycle Management", link: "/DATA_LIFECYCLE" },
            { text: "Feature Matrix", link: "/FEATURE_MATRIX" },
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
  })
);
