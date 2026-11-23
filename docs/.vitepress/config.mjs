import { defineConfig } from "vitepress";
import crypto from "crypto";
import path from "path";
import process from "process";
import { fileURLToPath } from "url";

import {
  BLOG_FEED_PATH,
  getDraftBlogPostExcludes,
  writeRssFeed,
} from "./blog.mjs";
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

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const docsDir = path.resolve(__dirname, "..");
const repoRoot = path.resolve(__dirname, "../..");
const indexPath = path.join(docsDir, "DOCUMENTATION_INDEX.md");
const siteBase = process.env.VITEPRESS_BASE || "/cadence-clinical/";
const isDevServer = process.argv.includes("dev");

function buildPortalCspContent(inlineScriptHashes = []) {
  const scriptSources = ["'self'", ...inlineScriptHashes].join(" ");
  return [
    "default-src 'self'",
    `script-src ${scriptSources}`,
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob:",
    "font-src 'self' data:",
    "media-src 'self'",
    "worker-src 'self'",
    "connect-src 'self'",
    "manifest-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
  ].join("; ");
}

function injectHashedPortalCsp(html) {
  const inlineHashes = new Set();
  const inlineScriptRegex =
    /<script\b(?![^>]*\bsrc\s*=)[^>]*>([\s\S]*?)<\/script>/gi;
  let match;
  while ((match = inlineScriptRegex.exec(html)) !== null) {
    const scriptBody = match[1];
    if (scriptBody.length > 0) {
      const digest = crypto
        .createHash("sha256")
        .update(scriptBody, "utf8")
        .digest("base64");
      inlineHashes.add(`'sha256-${digest}'`);
    }
  }

  const cspTag = `<meta http-equiv="Content-Security-Policy" content="${buildPortalCspContent([...inlineHashes])}">`;
  const existingCspRegex =
    /<meta\b[^>]*\bhttp-equiv=["']Content-Security-Policy["'][^>]*>/i;
  if (existingCspRegex.test(html)) {
    return html.replace(existingCspRegex, cspTag);
  }

  return html.replace(/<head>/i, `<head>\n    ${cspTag}`);
}

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
      "meta",
      {
        "http-equiv": "Content-Security-Policy",
        content: buildPortalCspContent(),
      },
    ],
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
      md.use(mermaidFencePlugin);
      md.use(dualHeadingAnchorsPlugin);
      md.use(repoLinksPlugin, {
        docsDir,
        repoRoot,
      });
    },
  },
  vite: {
    resolve: {
      alias: [
        {
          find: /^.*\/VPSidebarItem\.vue$/,
          replacement: fileURLToPath(
            new URL("./theme/components/VPSidebarItem.vue", import.meta.url)
          ),
        },
        {
          find: /^.*\/VPNavBarSearchButton\.vue$/,
          replacement: fileURLToPath(
            new URL(
              "./theme/components/VPNavBarSearchButton.vue",
              import.meta.url
            )
          ),
        },
        {
          find: /^.*\/VPContent\.vue$/,
          replacement: fileURLToPath(
            new URL("./theme/components/VPContent.vue", import.meta.url)
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
          if (!searchCatalog.shouldIndexPage(relPath, null, src)) {
            return "";
          }
          const preparedSrc = searchCatalog.prepareSearchMarkdown(relPath, src);
          const html = md.render(preparedSrc, env);
          if (!searchCatalog.shouldIndexPage(relPath, env.frontmatter, src)) {
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
        activeMatch:
          "^/(DOCUMENTATION_INDEX|kernel/CLINICAL_KERNEL_SPECIFICATION|ARCHITECTURE|CONTEXT|README)(?:\\.html)?$",
        items: [
          { text: "Doc Index", link: "/DOCUMENTATION_INDEX" },
          {
            text: "Kernel Spec",
            link: "/kernel/CLINICAL_KERNEL_SPECIFICATION",
          },
          { text: "Architecture", link: "/ARCHITECTURE" },
          { text: "Glossary", link: "/CONTEXT" },
          { text: "Repository Overview", link: "/README" },
        ],
      },
      {
        text: "Reference",
        activeMatch: "^/(api|components|schema|adr)/",
        items: [
          { text: "API Reference", link: "/api/" },
          { text: "UI Components", link: "/components/" },
          { text: "Data Models", link: "/schema/README" },
          { text: "ADRs", link: "/adr/" },
        ],
      },
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

export default config;
