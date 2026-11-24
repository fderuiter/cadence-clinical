import { defineConfig } from "vitepress";
import path from "path";
import process from "process";
import { fileURLToPath } from "url";

import {
  BLOG_FEED_PATH,
  getDraftBlogPostExcludes,
  resolveBaseHref,
  writeRssFeed,
} from "./blog.mjs";
import { buildPortalCspContent, injectHashedPortalCsp } from "./csp.mjs";
import {
  EXCLUDED_PORTAL_DOCS,
  assertThemeLinksExist,
  dualHeadingAnchorsPlugin,
  mermaidFencePlugin,
  repoLinksPlugin,
} from "./repoLinks.mjs";
import {
  buildPortalSearchCatalog,
  synthesizeSidebarFromIndex,
} from "./sidebar.mjs";

export { buildPortalCspContent, injectHashedPortalCsp };

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const docsDir = path.resolve(__dirname, "..");
const repoRoot = path.resolve(__dirname, "../..");
const indexPath = path.join(docsDir, "DOCUMENTATION_INDEX.md");
const siteBase = process.env.VITEPRESS_BASE || "/cadence-clinical/";
const rssFeedHref = resolveBaseHref(BLOG_FEED_PATH, siteBase);
const isDevServer = process.argv.includes("dev");

const searchCatalog = buildPortalSearchCatalog(indexPath, {
  docsDir,
  repoRoot,
});

const config = defineConfig({
  title: "Cadence Clinical Portal",
  description:
    "Metadata-Driven Clinical Execution Platform Documentation Portal",
  base: siteBase,
  metaChunk: true,
  outDir: path.resolve(__dirname, "dist"),
  srcExclude: [
    "**/SDLC/runs/**",
    "**/runs/**",
    ...EXCLUDED_PORTAL_DOCS,
    ...(isDevServer ? [] : getDraftBlogPostExcludes(docsDir)),
  ],
  head: [
    [
      "link",
      {
        rel: "alternate",
        type: "application/rss+xml",
        title: "Cadence Clinical Engineering Blog RSS Feed",
        href: rssFeedHref,
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
      md.use(mermaidFencePlugin);
      md.use(dualHeadingAnchorsPlugin);
      md.use(repoLinksPlugin, {
        docsDir,
        repoRoot,
        isDevServer,
      });
    },
  },
  vite: {
    resolve: {
      alias: [
        {
          find: /^.*\/VPSidebarItem\.vue$/,
          replacement: path.resolve(
            __dirname,
            "theme/components/VPSidebarItem.vue"
          ),
        },
        {
          find: /^.*\/VPNavBarSearchButton\.vue$/,
          replacement: path.resolve(
            __dirname,
            "theme/components/VPNavBarSearchButton.vue"
          ),
        },
        {
          find: /^.*\/VPContent\.vue$/,
          replacement: path.resolve(
            __dirname,
            "theme/components/VPContent.vue"
          ),
        },
      ],
    },
    build: {
      chunkSizeWarningLimit: 2500,
    },
  },
  async buildEnd(siteConfig) {
    writeRssFeed(siteConfig);
  },
  themeConfig: {
    search: {
      provider: "local",
      options: {
        miniSearch: {
          searchOptions: {
            fuzzy: 0.2,
            prefix: true,
            boost: {
              title: 5,
              titles: 3,
              text: 1,
            },
          },
        },
        _render(src, env, md) {
          const relPath = env.relativePath ?? "";
          if (
            env.frontmatter?.draft === true ||
            /^true$/i.test(String(env.frontmatter?.draft ?? "").trim()) ||
            !searchCatalog.shouldIndexPage(relPath, env.frontmatter, src)
          ) {
            return "";
          }
          const preparedSrc = searchCatalog.prepareSearchMarkdown(relPath, src);
          const html = md.render(preparedSrc, env);
          if (
            env.frontmatter?.draft === true ||
            /^true$/i.test(String(env.frontmatter?.draft ?? "").trim()) ||
            !searchCatalog.shouldIndexPage(relPath, env.frontmatter, src)
          ) {
            return "";
          }
          return searchCatalog.applyHeadingBoostHtml(relPath, html);
        },
      },
    },
    nav: [
      { text: "Home", link: "/" },
      {
        text: "Demos",
        items: [
          { text: "Staff Demo", link: "/demo/", target: "_self" },
          {
            text: "Participant Demo",
            link: "/subject-portal/",
            target: "_self",
          },
        ],
      },
      { text: "Blog", link: "/blog/", activeMatch: "^/blog/" },
      {
        text: "Guides",
        activeMatch: "^/(DOCUMENTATION_INDEX|kernel/|ARCHITECTURE|CONTEXT)",
        items: [
          { text: "Documentation Index", link: "/DOCUMENTATION_INDEX" },
          {
            text: "Clinical Kernel Spec",
            link: "/kernel/CLINICAL_KERNEL_SPECIFICATION",
          },
          { text: "System Architecture", link: "/ARCHITECTURE" },
          { text: "Domain Glossary", link: "/CONTEXT" },
        ],
      },
      {
        text: "Reference",
        activeMatch: "^/(api|schema|components|adr)/",
        items: [
          { text: "API Reference", link: "/api/" },
          { text: "Data Models", link: "/schema/README" },
          { text: "UI Components", link: "/components/" },
          { text: "ADR Log", link: "/adr/" },
        ],
      },
      { text: "RSS", link: rssFeedHref, target: "_blank" },
    ],
    sidebar: synthesizeSidebarFromIndex(indexPath, {
      docsDir,
      repoRoot,
    }),
  },
});

// VitePress checks links inside pages without fragments; assertThemeLinksExist validates nav and sidebar pages and anchors.
assertThemeLinksExist(config.themeConfig, docsDir, { base: siteBase });

export default config;
