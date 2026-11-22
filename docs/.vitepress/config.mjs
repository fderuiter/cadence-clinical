import { defineConfig } from "vitepress";
import { withMermaid } from "vitepress-plugin-mermaid";
import path from "path";
import { fileURLToPath } from "url";

import {
  BLOG_FEED_PATH,
  getDraftBlogPostExcludes,
  writeRssFeed,
} from "./blog.mjs";
import { buildPortalCspContent, injectHashedPortalCsp } from "./csp.mjs";
import {
  assertThemeLinksExist,
  dualHeadingAnchorsPlugin,
  repoLinksPlugin,
} from "./repoLinks.mjs";
import { synthesizeSidebarFromIndex } from "./sidebar.mjs";

export { buildPortalCspContent, injectHashedPortalCsp };

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const docsDir = path.resolve(__dirname, "..");
const repoRoot = path.resolve(__dirname, "../..");
const indexPath = path.join(docsDir, "DOCUMENTATION_INDEX.md");
const siteBase = process.env.VITEPRESS_BASE || "/cadence-clinical/";
const isDevServer = process.argv.includes("dev");

const config = defineConfig({
  title: "Cadence Clinical Portal",
  description:
    "Metadata-Driven Clinical Execution Platform Documentation Portal",
  base: siteBase,
  outDir: path.resolve(__dirname, "dist"),
  srcExclude: [
    "**/SDLC/runs/**",
    "**/runs/**",
    ...(isDevServer ? [] : getDraftBlogPostExcludes(docsDir)),
  ],
  head: [
    [
      "link",
      {
        rel: "alternate",
        type: "application/rss+xml",
        title: "Cadence Clinical Engineering Blog RSS Feed",
        href: `${siteBase.replace(/\/?$/, "/")}feed.xml`,
      },
    ],
  ],
  transformHtml(code) {
    return injectHashedPortalCsp(code);
  },
  // Links VitePress cannot serve are pointed at GitHub by repoLinksPlugin; co-hosted demo subpaths are staged
  // into dist/demo and dist/subject-portal after VitePress compiles, while any other dead link or anchor fails the build.
  ignoreDeadLinks: [/^\/(demo|subject-portal)(\/|$)/],
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
  async buildEnd(siteConfig) {
    writeRssFeed(siteConfig);
  },
  themeConfig: {
    search: {
      provider: "local",
      options: {
        _render(src, env, md) {
          if (
            env.frontmatter?.draft === true ||
            env.frontmatter?.draft === "true"
          ) {
            return "";
          }
          return md.render(src, env);
        },
      },
    },
    nav: [
      { text: "Home", link: "/" },
      { text: "Staff Demo", link: "/demo/", target: "_self" },
      { text: "Participant Demo", link: "/subject-portal/", target: "_self" },
      { text: "Blog", link: "/blog/index", activeMatch: "^/blog/" },
      { text: "Doc Index", link: "/DOCUMENTATION_INDEX" },
      { text: "Kernel Spec", link: "/kernel/CLINICAL_KERNEL_SPECIFICATION" },
      { text: "Architecture", link: "/ARCHITECTURE" },
      { text: "Glossary", link: "/CONTEXT" },
      { text: "API Reference", link: "/api/index" },
      { text: "Data Models", link: "/schema/README" },
      { text: "ADRs", link: "/adr/index" },
      { text: "RSS", link: BLOG_FEED_PATH, target: "_blank" },
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
