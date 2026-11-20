import { defineConfig } from "vitepress";
import { withMermaid } from "vitepress-plugin-mermaid";
import path from "path";
import { fileURLToPath } from "url";

import {
  assertThemeLinksExist,
  dualHeadingAnchorsPlugin,
  repoLinksPlugin,
} from "./repoLinks.mjs";
import { synthesizeSidebarFromIndex } from "./sidebar.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const docsDir = path.resolve(__dirname, "..");
const repoRoot = path.resolve(__dirname, "../..");
const indexPath = path.join(docsDir, "DOCUMENTATION_INDEX.md");

const config = defineConfig({
  title: "Cadence Clinical Portal",
  description:
    "Metadata-Driven Clinical Execution Platform Documentation Portal",
  base: process.env.VITEPRESS_BASE || "/cadence-clinical/",
  outDir: path.resolve(__dirname, "dist"),
  srcExclude: ["**/SDLC/runs/**", "**/runs/**"],
  // Links VitePress cannot serve are pointed at GitHub by repoLinksPlugin; any other dead link or anchor fails the build.
  ignoreDeadLinks: false,
  markdown: {
    config(md) {
      md.use(dualHeadingAnchorsPlugin);
      md.use(repoLinksPlugin, {
        docsDir,
        repoRoot,
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
      { text: "Glossary", link: "/CONTEXT" },
      { text: "API Reference", link: "/api/index" },
      { text: "Data Models", link: "/schema/README" },
      { text: "ADRs", link: "/adr/index" },
    ],
    sidebar: synthesizeSidebarFromIndex(indexPath, {
      docsDir,
      repoRoot,
    }),
  },
});

// VitePress checks links inside pages without fragments; assertThemeLinksExist validates nav and sidebar pages and anchors.
assertThemeLinksExist(config.themeConfig, docsDir);

export default withMermaid(config);
