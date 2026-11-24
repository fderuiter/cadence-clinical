import crypto from "crypto";
import fs from "fs";
import os from "os";
import path from "path";
import { fileURLToPath } from "url";
import { createMarkdownRenderer } from "vitepress";
import { afterEach, describe, expect, it } from "vitest";

import { buildPortalCspContent, injectHashedPortalCsp } from "./csp.mjs";
import portalConfig from "./config.mjs";

import {
  assertThemeLinksExist,
  dualHeadingAnchorsPlugin,
  extractMarkdownAnchors,
  githubSlugify,
  mermaidFencePlugin,
  repoLinksPlugin,
  toRepoUrl,
  vitepressSlugify,
} from "./repoLinks.mjs";
import { createRequire } from "module";
import rootDocsModule from "./rootDocs.cjs";
import {
  buildPortalSearchCatalog,
  parseDocumentationIndex,
  synthesizeSidebarFromIndex,
  toSidebarLink,
} from "./sidebar.mjs";

const require = createRequire(import.meta.url);
const { STAGED_ROOT_DOCS } = rootDocsModule;
const {
  extractMermaidBlocks,
  validatePortalMermaidDiagrams,
} = require("../../scripts/build-docs.js");

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const docsDir = path.resolve(__dirname, "..");
const repoRoot = path.resolve(docsDir, "..");
const indexPath = path.join(docsDir, "DOCUMENTATION_INDEX.md");

function collectSidebarLinks(sidebarOrItems, out = new Set()) {
  if (!sidebarOrItems) return out;
  if (Array.isArray(sidebarOrItems)) {
    for (const item of sidebarOrItems) {
      if (item.link) {
        out.add(item.link.split("#")[0]);
      }
      if (item.items) {
        collectSidebarLinks(item.items, out);
      }
    }
    return out;
  }

  for (const sectionItems of Object.values(sidebarOrItems)) {
    collectSidebarLinks(sectionItems, out);
  }
  return out;
}

describe("VitePress navigation synthesis and link/anchor plugins", () => {
  const tempDirs = [];

  function makeTempWorkspace() {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "cadence-vitepress-"));
    tempDirs.push(dir);
    return dir;
  }

  afterEach(() => {
    while (tempDirs.length > 0) {
      const dir = tempDirs.pop();
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  it("includes all 82 active non-ADR pages from docs/DOCUMENTATION_INDEX.md across route-scoped sidebars", () => {
    const indexContent = fs.readFileSync(indexPath, "utf-8");
    const parsedEntries = parseDocumentationIndex(indexContent, {
      docsDir,
      repoRoot,
    });

    const activeDocsAndHomeRoutes = new Set(
      parsedEntries
        .filter(
          (entry) =>
            ["Normative", "Current", "Generated"].includes(entry.status) &&
            entry.section !== "Architecture decision records" &&
            (!entry.rawHref.startsWith("../") ||
              entry.rawHref === "../README.md") &&
            entry.link.startsWith("/") &&
            !entry.fromDescription
        )
        .map((entry) => entry.link.split("#")[0])
    );

    expect(activeDocsAndHomeRoutes.size).toBe(82);

    const allActiveNonAdrPortalRoutes = new Set(
      parsedEntries
        .filter(
          (entry) =>
            ["Normative", "Current", "Generated"].includes(entry.status) &&
            entry.section !== "Architecture decision records" &&
            entry.link.startsWith("/") &&
            !entry.fromDescription
        )
        .map((entry) => entry.link.split("#")[0])
    );

    // 82 active docs/ + home pages plus the 7 additional staged root guides/policies = 89 portal pages
    expect(allActiveNonAdrPortalRoutes.size).toBe(89);

    const sidebar = synthesizeSidebarFromIndex(indexPath, {
      docsDir,
      repoRoot,
    });
    expect(Array.isArray(sidebar)).toBe(false);
    expect(typeof sidebar).toBe("object");

    const sidebarLinks = collectSidebarLinks(sidebar);

    for (const route of allActiveNonAdrPortalRoutes) {
      expect(
        sidebarLinks.has(route),
        `Expected route-scoped sidebars to contain ${route}`
      ).toBe(true);
    }

    // Key previously omitted pages and both Portal Home + staged README are present
    expect(sidebarLinks.has("/")).toBe(true);
    expect(sidebarLinks.has("/README")).toBe(true);
    expect(sidebarLinks.has("/CONTEXT")).toBe(true);
    expect(sidebarLinks.has("/CONTRIBUTING")).toBe(true);
    expect(sidebarLinks.has("/SECURITY")).toBe(true);
    expect(sidebarLinks.has("/SUPPORT")).toBe(true);
    expect(sidebarLinks.has("/api/kernel")).toBe(true);
    expect(sidebarLinks.has("/api/execution_forms")).toBe(true);
    expect(sidebarLinks.has("/api/intelligence")).toBe(true);
    expect(sidebarLinks.has("/schema/README")).toBe(true);
    expect(sidebarLinks.has("/schema/kernel")).toBe(true);
    expect(sidebarLinks.has("/ux/CLINICAL_CANVAS")).toBe(true);
    expect(sidebarLinks.has("/web/API_MIGRATION")).toBe(true);
    expect(sidebarLinks.has("/blog/")).toBe(true);
    expect(sidebarLinks.has("/api/")).toBe(true);
    expect(sidebarLinks.has("/components/")).toBe(true);
    expect(sidebarLinks.has("/migration/")).toBe(true);

    // Subdirectory hub links are normalized to trailing slashes rather than /.../index
    expect(sidebarLinks.has("/blog/index")).toBe(false);
    expect(sidebarLinks.has("/api/index")).toBe(false);
    expect(sidebarLinks.has("/components/index")).toBe(false);
    expect(sidebarLinks.has("/adr/index")).toBe(false);
    expect(sidebarLinks.has("/migration/index")).toBe(false);

    // Verify each route prefix is compact rather than repeating all 600+ portal links
    const rootLinks = collectSidebarLinks(sidebar["/"]);
    expect(rootLinks.size).toBeLessThan(45);

    const adrLinks = collectSidebarLinks(sidebar["/adr/"]);
    expect(adrLinks.has("/adr/")).toBe(true);
    expect(adrLinks.has("/adr/TEMPLATE")).toBe(true);
    expect(
      adrLinks.has(
        "/adr/2026-09-29-clinical-kernel-modular-monolith-architecture"
      )
    ).toBe(true);
    expect(adrLinks.has("/adr/2023-01-01-neo4j-graph-database")).toBe(false);
    expect(adrLinks.size).toBeLessThan(90);
  });

  it("synthesizes the Engineering Blog group from published frontmatter newest-first under /blog/ and excludes draft blog posts", () => {
    const tmpRepo = makeTempWorkspace();
    const tmpDocs = path.join(tmpRepo, "docs");
    const tmpBlog = path.join(tmpDocs, "blog");
    fs.mkdirSync(tmpBlog, { recursive: true });

    fs.writeFileSync(path.join(tmpBlog, "index.md"), "# Engineering Blog\n");
    fs.writeFileSync(
      path.join(tmpBlog, "2026-11-19-alpha-slug.md"),
      '---\ntitle: "Updated Frontmatter Title Older"\ndate: "2026-09-29"\ndraft: false\n---\n# Heading\n'
    );
    fs.writeFileSync(
      path.join(tmpBlog, "2026-09-29-zeta-slug.md"),
      '---\ntitle: "Updated Frontmatter Title Newer"\ndate: "2026-11-20"\ndraft: false\n---\n# Heading\n'
    );
    fs.writeFileSync(
      path.join(tmpBlog, "2026-11-25-draft.md"),
      '---\ntitle: "Draft Post"\ndate: "2026-11-25"\ndraft: true\n---\n# Draft Post\n'
    );

    const sampleIndex = [
      "# Documentation Index",
      "",
      "## Engineering blog",
      "",
      "| Document | Status | What it is |",
      "| --- | --- | --- |",
      "| [Stale Older Label](blog/2026-11-19-alpha-slug.md) | Current | Older milestone |",
      "| [Cadence Engineering Blog](blog/index.md) | Current | Blog overview |",
      "| [Stale Newer Label](blog/2026-09-29-zeta-slug.md) | Current | Newer milestone |",
      "| [Draft Post](blog/2026-11-25-draft.md) | Current | Draft milestone |",
      "",
    ].join("\n");

    const parsed = parseDocumentationIndex(sampleIndex, {
      docsDir: tmpDocs,
      repoRoot: tmpRepo,
    });
    expect(parsed.some((e) => e.rawHref.includes("2026-11-25-draft.md"))).toBe(
      false
    );

    const sidebar = synthesizeSidebarFromIndex(
      path.join(tmpDocs, "DOCUMENTATION_INDEX.md"),
      {
        docsDir: tmpDocs,
        repoRoot: tmpRepo,
        indexContent: sampleIndex,
      }
    );

    const blogSection = sidebar["/blog/"];
    expect(blogSection).toBeDefined();
    const blogGroup = blogSection.find((g) => g.text === "Engineering Blog");
    expect(blogGroup).toBeDefined();
    expect(blogGroup.collapsed).toBe(false);
    expect(blogGroup.items).toEqual([
      { text: "All Engineering Posts", link: "/blog/" },
      {
        text: "Updated Frontmatter Title Newer",
        link: "/blog/2026-09-29-zeta-slug",
      },
      {
        text: "Updated Frontmatter Title Older",
        link: "/blog/2026-11-19-alpha-slug",
      },
    ]);
  });

  it("scopes REST API, Data Model, and Historical sections to their own prefixes without mixing generated and archive pages", () => {
    const sidebar = synthesizeSidebarFromIndex(indexPath, {
      docsDir,
      repoRoot,
    });

    const apiGroup = sidebar["/api/"]?.find(
      (g) => g.text === "REST API Reference"
    );
    const schemaGroup = sidebar["/schema/"]?.find(
      (g) => g.text === "Data Model Reference"
    );
    const historicalGroup = sidebar["/migration/"]?.find(
      (g) => g.text === "Historical & Migration Records"
    );

    expect(apiGroup).toBeDefined();
    expect(apiGroup.items[0].link).toBe("/api/");

    expect(schemaGroup).toBeDefined();
    expect(schemaGroup.items[0].link).toBe("/schema/README");
    expect(schemaGroup.items.length).toBeGreaterThanOrEqual(18);

    expect(historicalGroup).toBeDefined();
    expect(historicalGroup.items[0].link).toBe("/migration/");
    expect(historicalGroup.items[1].link.startsWith("/migration/")).toBe(true);

    const archiveLinks = collectSidebarLinks(sidebar["/migration/"]);
    expect(archiveLinks.has("/api/kernel")).toBe(false);
    expect(archiveLinks.has("/schema/kernel")).toBe(false);
    expect(archiveLinks.has("/adr/2023-01-01-neo4j-graph-database")).toBe(
      false
    );
  });

  it("automatically places a newly added active Start here document row into the / sidebar", () => {
    const originalContent = fs.readFileSync(indexPath, "utf-8");
    const modifiedContent = originalContent.replace(
      "| [CONTRIBUTING.md](../CONTRIBUTING.md) |",
      "| [New Onboarding Guide](ONBOARDING_TEST_GUIDE.md) | Current | Step-by-step evaluator walkthrough |\n| [CONTRIBUTING.md](../CONTRIBUTING.md) |"
    );

    const sidebar = synthesizeSidebarFromIndex(indexPath, {
      docsDir,
      repoRoot,
      indexContent: modifiedContent,
    });

    const startHere = sidebar["/"]?.find((g) => g.text === "Start Here");
    expect(startHere).toBeDefined();
    expect(
      startHere.items.some((item) => item.link === "/ONBOARDING_TEST_GUIDE")
    ).toBe(true);
  });

  it("rewrites excluded SDLC/03_API_Integration_Specification.md links to GitHub while keeping staged root docs internal", () => {
    expect(Object.keys(STAGED_ROOT_DOCS).sort()).toEqual([
      "AGENTS.md",
      "ARCHITECTURE.md",
      "CONTEXT.md",
      "CONTRIBUTING.md",
      "LICENSE",
      "README.md",
      "SECURITY.md",
      "SUPPORT.md",
    ]);

    const tmpRepo = makeTempWorkspace();
    const tmpDocs = path.join(tmpRepo, "docs");
    const tmpSdlc = path.join(tmpDocs, "SDLC");
    const tmpPkg = path.join(tmpRepo, "packages", "kernel");
    fs.mkdirSync(tmpSdlc, { recursive: true });
    fs.mkdirSync(tmpPkg, { recursive: true });

    for (const [src, dest] of Object.entries(STAGED_ROOT_DOCS)) {
      fs.writeFileSync(path.join(tmpRepo, src), `# ${src}\n`);
      fs.writeFileSync(path.join(tmpDocs, dest), `# ${dest}\n`);
    }
    fs.writeFileSync(path.join(tmpPkg, "README.md"), "# Kernel Package\n");
    fs.writeFileSync(path.join(tmpRepo, ".env.example"), "FOO=bar\n");
    fs.writeFileSync(
      path.join(tmpSdlc, "03_API_Integration_Specification.md"),
      "# Baseline API Spec\n"
    );

    expect(toRepoUrl("../CONTEXT.md", tmpDocs, tmpDocs, tmpRepo)).toBe(
      "CONTEXT.md"
    );
    expect(
      toRepoUrl("../../CONTRIBUTING.md#local-checks", tmpSdlc, tmpDocs, tmpRepo)
    ).toBe("../CONTRIBUTING.md#local-checks");
    expect(toRepoUrl("../README.md#overview", tmpDocs, tmpDocs, tmpRepo)).toBe(
      "README.md#overview"
    );
    expect(toRepoUrl("../LICENSE", tmpDocs, tmpDocs, tmpRepo)).toBe(
      "LICENSE.md"
    );

    expect(
      toRepoUrl(
        "SDLC/03_API_Integration_Specification.md#endpoints",
        tmpDocs,
        tmpDocs,
        tmpRepo
      )
    ).toBe(
      "https://github.com/fderuiter/cadence-clinical/blob/main/docs/SDLC/03_API_Integration_Specification.md#endpoints"
    );
    expect(
      toSidebarLink("SDLC/03_API_Integration_Specification.md", {
        docsDir: tmpDocs,
        repoRoot: tmpRepo,
      })
    ).toBe(
      "https://github.com/fderuiter/cadence-clinical/blob/main/docs/SDLC/03_API_Integration_Specification.md"
    );

    expect(
      toRepoUrl("../packages/kernel/README.md", tmpDocs, tmpDocs, tmpRepo)
    ).toBe(
      "https://github.com/fderuiter/cadence-clinical/blob/main/packages/kernel/README.md"
    );
    expect(toRepoUrl("../.env.example", tmpDocs, tmpDocs, tmpRepo)).toBe(
      "https://github.com/fderuiter/cadence-clinical/blob/main/.env.example"
    );
  });

  it("emits both VitePress and GitHub heading anchor IDs and renders deferred mermaid containers", async () => {
    const tmpDir = makeTempWorkspace();
    const md = await createMarkdownRenderer(tmpDir);
    md.use(mermaidFencePlugin);
    md.use(dualHeadingAnchorsPlugin);

    const html = md.render(
      [
        "# Sample Reference",
        "",
        "## 15. Roadmap",
        "",
        "## kernel.activity_entries",
        "",
        "## How the RTM decides a requirement's state",
        "",
        "## `packages/ddf`",
        "",
        "Artifact `gxp-test-evidence-${{ github.run_id }}`.",
        "",
        "```mermaid",
        "flowchart LR",
        "  A[Kernel] --> B[PostgreSQL]",
        "```",
      ].join("\n"),
      { relativePath: "sample.md" }
    );

    // Primary VitePress IDs remain intact
    expect(html).toContain('id="_15-roadmap"');
    expect(html).toContain('id="kernel-activity-entries"');

    // Secondary GitHub-compatible IDs are emitted inside headings
    expect(html).toContain('id="15-roadmap"');
    expect(html).toContain('id="kernelactivity_entries"');
    expect(html).toContain('id="how-the-rtm-decides-a-requirements-state"');
    expect(html).toContain('id="packagesddf"');
    expect(html).toContain("<code v-pre");

    // Mermaid fences render inert v-pre placeholders without build-time Mermaid imports
    expect(html).toContain('<div class="mermaid" v-pre data-mermaid-code="');

    // Slug helpers agree with extracted anchor set
    expect(vitepressSlugify("15. Roadmap")).toBe("_15-roadmap");
    expect(githubSlugify("15. Roadmap")).toBe("15-roadmap");
    expect(githubSlugify("kernel.activity_entries")).toBe(
      "kernelactivity_entries"
    );
  });

  it("fails closed in repoLinksPlugin and assertThemeLinksExist for both array and route-keyed sidebars", async () => {
    const tmpRepo = makeTempWorkspace();
    const tmpDocs = path.join(tmpRepo, "docs");
    fs.mkdirSync(path.join(tmpDocs, "SDLC"), { recursive: true });

    fs.writeFileSync(
      path.join(tmpDocs, "target.md"),
      "# Target Page\n\n## Valid Heading\n"
    );
    fs.writeFileSync(
      path.join(tmpDocs, "source.md"),
      "# Source Page\n\nSee [Target](target.md#valid-heading).\n"
    );
    fs.writeFileSync(
      path.join(tmpDocs, "SDLC", "03_API_Integration_Specification.md"),
      "# Excluded Baseline\n"
    );

    const md = await createMarkdownRenderer(tmpDocs);
    md.use(repoLinksPlugin, { docsDir: tmpDocs, repoRoot: tmpRepo });

    expect(() =>
      md.render("# Source Page\n\nSee [Valid](target.md#valid-heading).\n", {
        relativePath: "source.md",
      })
    ).not.toThrow();

    expect(() =>
      md.render("# Source Page\n\nSee [Missing](#no-such-heading).\n", {
        relativePath: "source.md",
      })
    ).toThrow(/Broken heading anchor '#no-such-heading'/);

    expect(() =>
      md.render(
        "# Source Page\n\nSee [Missing Cross](target.md#missing-anchor).\n",
        { relativePath: "source.md" }
      )
    ).toThrow(/Broken heading anchor 'target\.md#missing-anchor'/);

    expect(() =>
      assertThemeLinksExist(
        {
          nav: [{ text: "Valid", link: "/target#valid-heading" }],
          sidebar: {
            "/": [
              {
                text: "Section",
                items: [
                  { text: "Broken", link: "/target#missing-sidebar-anchor" },
                ],
              },
            ],
          },
        },
        tmpDocs
      )
    ).toThrow(/missing anchors: \/target#missing-sidebar-anchor/);

    expect(() =>
      assertThemeLinksExist(
        {
          nav: [],
          sidebar: {
            "/SDLC/": [
              {
                text: "Excluded",
                items: [
                  {
                    text: "Spec",
                    link: "/SDLC/03_API_Integration_Specification",
                  },
                ],
              },
            ],
          },
        },
        tmpDocs
      )
    ).toThrow(/missing pages: \/SDLC\/03_API_Integration_Specification/);

    const anchors = extractMarkdownAnchors(
      "# Title\n\n## Duplicate\n\n## Duplicate\n"
    );
    expect(anchors.has("duplicate")).toBe(true);
    expect(anchors.has("duplicate-1")).toBe(true);
  });

  it("validates nested nav dropdown items, trailing-slash directory hubs, and <= 2-click reachability for demos and RSS", () => {
    const tmpRepo = makeTempWorkspace();
    const tmpDocs = path.join(tmpRepo, "docs");
    fs.mkdirSync(path.join(tmpDocs, "api"), { recursive: true });
    fs.writeFileSync(
      path.join(tmpDocs, "api", "index.md"),
      "# API Hub\n\n## Services\n"
    );

    expect(() =>
      assertThemeLinksExist(
        {
          nav: [
            {
              text: "Reference",
              items: [
                { text: "API Hub", link: "/api/#services" },
                { text: "Staff Demo", link: "/demo/" },
                { text: "RSS", link: "/cadence-clinical/feed.xml" },
              ],
            },
          ],
          sidebar: {},
        },
        tmpDocs,
        { base: "/cadence-clinical/" }
      )
    ).not.toThrow();

    expect(() =>
      assertThemeLinksExist(
        {
          nav: [
            {
              text: "Reference",
              items: [{ text: "Missing Nested", link: "/missing-hub/" }],
            },
          ],
          sidebar: {},
        },
        tmpDocs
      )
    ).toThrow(/missing pages: \/missing-hub\//);

    expect(() =>
      assertThemeLinksExist(
        {
          nav: [
            {
              text: "Reference",
              items: [{ text: "Broken Nested Anchor", link: "/api/#unknown" }],
            },
          ],
          sidebar: {},
        },
        tmpDocs
      )
    ).toThrow(/missing anchors: \/api\/#unknown/);

    // Verify configured top-level nav keeps <= 6 items and exposes Staff Demo, Participant Demo, and RSS in <= 2 clicks
    const configuredNav = portalConfig.themeConfig.nav;
    expect(configuredNav.length).toBeLessThanOrEqual(6);

    const clickDepthByTarget = new Map();
    const recordNavDepth = (items, depth) => {
      for (const item of items ?? []) {
        if (item.link) {
          const prev = clickDepthByTarget.get(item.link);
          if (prev === undefined || depth < prev) {
            clickDepthByTarget.set(item.link, depth);
          }
        }
        if (Array.isArray(item.items)) {
          recordNavDepth(item.items, depth + 1);
        }
      }
    };
    recordNavDepth(configuredNav, 1);

    expect(clickDepthByTarget.get("/demo/")).toBeLessThanOrEqual(2);
    expect(clickDepthByTarget.get("/subject-portal/")).toBeLessThanOrEqual(2);
    expect(
      clickDepthByTarget.get("/cadence-clinical/feed.xml")
    ).toBeLessThanOrEqual(2);
    expect(clickDepthByTarget.has("/blog/index")).toBe(false);
    expect(clickDepthByTarget.has("/api/index")).toBe(false);
    expect(clickDepthByTarget.has("/adr/index")).toBe(false);
  });

  it("hoists hashed Content-Security-Policy <meta> before all <script> and <link> tags and removes trailing placeholders", () => {
    const inlineBody =
      "(() => { const saved = localStorage.getItem('vitepress-theme-appearance'); })();";
    const expectedDigest = crypto
      .createHash("sha256")
      .update(inlineBody, "utf8")
      .digest("base64");

    const vitepressSampleHtml = [
      "<!DOCTYPE html>",
      '<html lang="en-US" dir="ltr">',
      "  <head>",
      '    <meta charset="utf-8">',
      '    <meta name="viewport" content="width=device-width,initial-scale=1">',
      "    <title>Architecture | Cadence Clinical Portal</title>",
      '    <link rel="preload" href="/cadence-clinical/assets/style.abc123.css" as="style">',
      '    <script type="module" src="/cadence-clinical/assets/app.BhpmuQal.js"></script>',
      '    <link rel="modulepreload" href="/cadence-clinical/assets/chunks/framework.xyz.js">',
      `    <meta http-equiv="Content-Security-Policy" content="${buildPortalCspContent()}">`,
      `    <script id="check-dark-mode">${inlineBody}</script>`,
      "  </head>",
      '  <body><div id="app"></div></body>',
      "</html>",
    ].join("\n");

    const transformed = injectHashedPortalCsp(vitepressSampleHtml);
    const cspMatches = [
      ...transformed.matchAll(
        /<meta\b[^>]*\bhttp-equiv=["']Content-Security-Policy["'][^>]*>/gi
      ),
    ];
    expect(cspMatches).toHaveLength(1);

    const cspIndex = cspMatches[0].index;
    const firstScriptIndex = transformed.search(/<script\b/i);
    const firstLinkIndex = transformed.search(/<link\b/i);

    expect(cspIndex).toBeGreaterThan(0);
    expect(cspIndex).toBeLessThan(firstLinkIndex);
    expect(cspIndex).toBeLessThan(firstScriptIndex);
    expect(cspMatches[0][0]).toContain(`'sha256-${expectedDigest}'`);
    expect(cspMatches[0][0]).toContain("frame-src 'self'");
    expect(cspMatches[0][0]).not.toContain("'unsafe-inline' 'sha256-");
  });

  it("excludes Baseline, Historical, and superseded/pre-kernel ADRs from search while boosting Normative and Kernel docs", () => {
    const catalog = buildPortalSearchCatalog(indexPath, { docsDir, repoRoot });

    expect(
      catalog.shouldIndexPage("kernel/CLINICAL_KERNEL_SPECIFICATION.md")
    ).toBe(true);
    expect(catalog.shouldIndexPage("ARCHITECTURE.md")).toBe(true);
    expect(
      catalog.shouldIndexPage(
        "adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md"
      )
    ).toBe(true);

    expect(
      catalog.shouldIndexPage("SDLC/03_API_Integration_Specification.md")
    ).toBe(false);
    expect(
      catalog.shouldIndexPage("SDLC/01_Product_Requirements_Document_PRD.md")
    ).toBe(false);
    expect(catalog.shouldIndexPage("migration/index.md")).toBe(false);
    expect(
      catalog.shouldIndexPage(
        "architecture/repository-migration/MIGRATION_PLAN.md"
      )
    ).toBe(false);
    expect(
      catalog.shouldIndexPage("adr/2023-01-01-neo4j-graph-database.md")
    ).toBe(false);
    expect(
      catalog.shouldIndexPage(
        "adr/2026-07-22-gateway-authentication-propagation.md"
      )
    ).toBe(false);
    expect(
      catalog.shouldIndexPage(
        "adr/2026-10-06-published-form-version-registry-and-schema-hash-validation.md",
        null,
        "# ADR-2501\n\n- **Status:** Superseded by ADR-2622\n"
      )
    ).toBe(false);

    expect(
      catalog.getSearchBoostMultiplier(
        "kernel/CLINICAL_KERNEL_SPECIFICATION.md"
      )
    ).toBeGreaterThan(catalog.getSearchBoostMultiplier("CI.md"));
    expect(
      catalog.getSearchBoostMultiplier(
        "adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md"
      )
    ).toBeGreaterThan(catalog.getSearchBoostMultiplier("CI.md"));
  });

  it("parses all 41 Mermaid diagrams across 20 portal pages cleanly via JSDOM and preserves inline code v-pre", async () => {
    const result = await validatePortalMermaidDiagrams(docsDir, {
      repoRoot,
      includeUnstagedRootDocs: true,
      silent: true,
    });

    expect(result.pagesWithMermaid).toBe(20);
    expect(result.diagramCount).toBe(41);

    const dataLifecycleMd = fs.readFileSync(
      path.join(docsDir, "DATA_LIFECYCLE.md"),
      "utf-8"
    );
    const dataLifecycleBlocks = extractMermaidBlocks(
      dataLifecycleMd,
      "docs/DATA_LIFECYCLE.md"
    );
    expect(dataLifecycleBlocks.length).toBe(3);

    const md = await createMarkdownRenderer(docsDir);
    md.use(dualHeadingAnchorsPlugin);
    const ciSnippetHtml = md.render(
      "Workflow expression `${{ steps.cadence-cli-cache.outputs.cache-hit }}`.\n",
      { relativePath: "CI.md" }
    );
    expect(ciSnippetHtml).toContain("<code v-pre");
  }, 30000);

  it("rejects invalid Mermaid syntax in staged root docs and docs pages with exact file and line numbers", async () => {
    const tmpDocs = makeTempWorkspace();
    fs.writeFileSync(
      path.join(tmpDocs, "ARCHITECTURE.md"),
      [
        "# Architecture",
        "",
        "```mermaid",
        "sequenceDiagram",
        "    S-->>C: stale; 403",
        "```",
        "",
      ].join("\n")
    );
    fs.writeFileSync(
      path.join(tmpDocs, "workflow.md"),
      [
        "# Workflow",
        "",
        "```mermaid",
        "graph TD",
        "    A -->|POST /api/protocols/{id}/amend| B",
        "```",
        "",
      ].join("\n")
    );

    await expect(
      validatePortalMermaidDiagrams(tmpDocs, { repoRoot, silent: true })
    ).rejects.toThrow(/ARCHITECTURE\.md:3[\s\S]*docs\/workflow\.md:3/);
  }, 30000);
});
