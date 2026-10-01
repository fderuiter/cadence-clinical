import { defineConfig } from "vitepress";
import { withMermaid } from "vitepress-plugin-mermaid";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

import { assertThemeLinksExist, repoLinksPlugin } from "./repoLinks.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

/**
 * Extracts custom document title from H1 header or YAML frontmatter,
 * falling back to a cleaned file/directory name.
 */
function extractDocTitle(filePath, fallbackName) {
  try {
    const content = fs.readFileSync(filePath, "utf8");
    if (content.startsWith("---")) {
      const endFm = content.indexOf("\n---", 3);
      if (endFm !== -1) {
        const fm = content.substring(3, endFm);
        const titleMatch = fm.match(/^title:\s*["']?(.*?)["']?\s*$/m);
        if (titleMatch && titleMatch[1].trim()) {
          return titleMatch[1].trim();
        }
      }
    }
    const lines = content.split("\n");
    for (let line of lines) {
      line = line.trim();
      if (line.startsWith("# ")) {
        return line.substring(2).trim().replace(/[*`]/g, "");
      }
    }
  } catch (e) {}

  return cleanName(fallbackName);
}

function cleanName(name) {
  return name
    .replace(/\.md$/, "")
    .replace(/[-_]/g, " ")
    .replace(/\b\w/g, (c) => c.toUpperCase());
}

function shouldExcludePath(relPath) {
  const norm = relPath.replace(/\\/g, "/");
  if (norm.includes("SDLC/runs/") || norm.includes("/runs/")) return true;
  if (norm.includes("node_modules") || norm.includes(".vitepress")) return true;
  if (norm === "index.md") return true; // Root index.md is Home
  return false;
}

/**
 * Recursively scans a directory under docs/ to build hierarchical sidebar items.
 */
function getSubtreeItems(dirPath, baseDocsDir) {
  if (!fs.existsSync(dirPath)) return [];
  const entries = fs.readdirSync(dirPath, { withFileTypes: true });
  entries.sort((a, b) => {
    if (a.isDirectory() && !b.isDirectory()) return -1;
    if (!a.isDirectory() && b.isDirectory()) return 1;
    return a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: "base" });
  });

  const items = [];

  for (const entry of entries) {
    const fullPath = path.join(dirPath, entry.name);
    const relFromDocs = path.relative(baseDocsDir, fullPath).replace(/\\/g, "/");

    if (shouldExcludePath(relFromDocs)) continue;

    if (entry.isDirectory()) {
      const subItems = getSubtreeItems(fullPath, baseDocsDir);
      if (subItems.length > 0) {
        let groupTitle = cleanName(entry.name);
        const indexPath = path.join(fullPath, "index.md");
        if (fs.existsSync(indexPath)) {
          groupTitle = extractDocTitle(indexPath, entry.name);
        }
        items.push({
          text: groupTitle,
          collapsed: true,
          items: subItems,
        });
      }
    } else if (entry.isFile() && entry.name.endsWith(".md")) {
      const title = extractDocTitle(fullPath, entry.name);
      const linkPath = "/" + relFromDocs.replace(/\.md$/, "");
      items.push({
        text: title,
        link: linkPath,
      });
    }
  }

  return items;
}

/**
 * Dynamically builds hierarchical sidebar categories for all non-excluded markdown files in docs/.
 */
function getFullDynamicSidebar(baseDocsDir) {
  const categoryConfig = [
    {
      key: "(root)",
      title: "Overview & Guides",
    },
    {
      key: "kernel",
      title: "Clinical Kernel",
    },
    {
      key: "adr",
      title: "Architectural Decision Records (ADRs)",
    },
    {
      key: "specs",
      title: "Design & Platform Specifications",
    },
    {
      key: "migration",
      title: "Python-to-.NET Port & Migration",
    },
    {
      key: "SDLC",
      title: "SDLC Compliance & Regulatory Guidelines",
    },
    {
      key: "api",
      title: "REST API Reference",
    },
    {
      key: "components",
      title: "Enterprise UI & User Experience",
      mergeKeys: ["components", "ux", "web"],
    },
    {
      key: "CDISC",
      title: "Clinical Standards & Reference Data",
      mergeKeys: ["CDISC", "NCI", "schema", "openapi"],
    },
    {
      key: "agents",
      title: "Agent Working Notes",
    },
  ];

  const processedTopDirs = new Set();
  const sidebar = [];

  // 1. Root files group ("Overview & Guides")
  const rootFilesGroup = {
    text: "Overview & Guides",
    items: [
      { text: "Welcome & Readme", link: "/" },
      { text: "Documentation Index", link: "/DOCUMENTATION_INDEX" },
      { text: "System Architecture", link: "/ARCHITECTURE" },
      { text: "Local Development Environment", link: "/LOCAL_DEV_ENVIRONMENT" },
      { text: "Environment Variables", link: "/ENVIRONMENT_VARIABLES" },
      { text: "Database and Migrations", link: "/OPERATIONAL_DATABASE" },
      { text: "Testing and Requirement Qualification", link: "/TESTING" },
      { text: "CI and Quality Gates", link: "/CI" },
      { text: "Developer CLI (`cadence`)", link: "/cli" },
      { text: "Agent & Contributor Rules", link: "/AGENTS" },
    ],
  };

  const rootEntries = fs.readdirSync(baseDocsDir, { withFileTypes: true });
  const existingRootLinks = new Set(rootFilesGroup.items.map((i) => i.link));

  for (const entry of rootEntries) {
    if (entry.isFile() && entry.name.endsWith(".md") && entry.name !== "index.md") {
      const relPath = entry.name;
      const link = "/" + relPath.replace(/\.md$/, "");
      if (!existingRootLinks.has(link)) {
        const title = extractDocTitle(path.join(baseDocsDir, entry.name), entry.name);
        rootFilesGroup.items.push({ text: title, link });
      }
    }
  }

  sidebar.push(rootFilesGroup);
  processedTopDirs.add("(root)");

  const getDirItems = (dirName) => {
    const fullDirPath = path.join(baseDocsDir, dirName);
    return getSubtreeItems(fullDirPath, baseDocsDir);
  };

  // 2. Predefined categories
  for (const cat of categoryConfig) {
    if (cat.key === "(root)") continue;

    const keysToMerge = cat.mergeKeys || [cat.key];
    const catItems = [];

    for (const key of keysToMerge) {
      processedTopDirs.add(key);
      const items = getDirItems(key);
      if (items.length > 0) {
        if (keysToMerge.length > 1) {
          catItems.push({
            text: cleanName(key),
            collapsed: true,
            items,
          });
        } else {
          catItems.push(...items);
        }
      }
    }

    if (catItems.length > 0) {
      sidebar.push({
        text: cat.title,
        collapsed: cat.key !== "kernel" && cat.key !== "specs",
        items: catItems,
      });
    }
  }

  // 3. Dynamic scan for any newly added top-level directories
  for (const entry of rootEntries) {
    if (entry.isDirectory() && entry.name !== ".vitepress" && entry.name !== "node_modules") {
      if (!processedTopDirs.has(entry.name)) {
        const items = getDirItems(entry.name);
        if (items.length > 0) {
          sidebar.push({
            text: cleanName(entry.name),
            collapsed: true,
            items,
          });
        }
      }
    }
  }

  return sidebar;
}

const docsDir = path.resolve(__dirname, "..");

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
    sidebar: getFullDynamicSidebar(docsDir),
  },
});

// VitePress checks links inside pages but not the nav and sidebar, so check those here.
assertThemeLinksExist(config.themeConfig, path.resolve(__dirname, ".."));

export default withMermaid(config);
