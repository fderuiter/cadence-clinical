import { defineConfig } from "vitepress";
import { withMermaid } from "vitepress-plugin-mermaid";
import crypto from "crypto";
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

const config = defineConfig({
  title: "Cadence Clinical Portal",
  description:
    "Metadata-Driven Clinical Execution Platform Documentation Portal",
  base: process.env.VITEPRESS_BASE || "/cadence-clinical/",
  outDir: path.resolve(__dirname, "dist"),
  srcExclude: ["**/SDLC/runs/**", "**/runs/**"],
  head: [
    [
      "meta",
      {
        "http-equiv": "Content-Security-Policy",
        content: buildPortalCspContent(),
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
  themeConfig: {
    search: {
      provider: "local",
    },
    nav: [
      { text: "Home", link: "/" },
      { text: "Staff Demo", link: "/demo/", target: "_self" },
      { text: "Participant Demo", link: "/subject-portal/", target: "_self" },
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
