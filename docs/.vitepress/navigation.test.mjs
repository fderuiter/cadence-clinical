import fs from "fs";
import os from "os";
import path from "path";
import { fileURLToPath } from "url";
import { createMarkdownRenderer } from "vitepress";
import { afterEach, describe, expect, it } from "vitest";

import config from "./config.mjs";
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
import rootDocsModule from "./rootDocs.cjs";
import {
  buildPortalSearchCatalog,
  parseDocumentationIndex,
  synthesizeSidebarFromIndex,
  toSidebarLink,
} from "./sidebar.mjs";

const { STAGED_ROOT_DOCS } = rootDocsModule;

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

  it("includes all 80 active non-ADR pages from docs/DOCUMENTATION_INDEX.md across route-scoped sidebars", () => {
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

    expect(activeDocsAndHomeRoutes.size).toBe(80);

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

    // 80 active docs/ + home pages plus the 7 additional staged root guides/policies = 87 portal pages
    expect(allActiveNonAdrPortalRoutes.size).toBe(87);

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
    expect(sidebarLinks.has("/adr/")).toBe(true);
    expect(sidebarLinks.has("/migration/")).toBe(true);

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

  it("synthesizes the Engineering Blog group newest-first under /blog/ and excludes draft blog posts from portal routes", () => {
    const tmpRepo = makeTempWorkspace();
    const tmpDocs = path.join(tmpRepo, "docs");
    const tmpBlog = path.join(tmpDocs, "blog");
    fs.mkdirSync(tmpBlog, { recursive: true });

    fs.writeFileSync(path.join(tmpBlog, "index.md"), "# Engineering Blog\n");
    fs.writeFileSync(
      path.join(tmpBlog, "2026-09-29-older.md"),
      '---\ntitle: "Older Post"\ndraft: false\n---\n# Older Post\n'
    );
    fs.writeFileSync(
      path.join(tmpBlog, "2026-11-19-newer.md"),
      '---\ntitle: "Newer Post"\ndraft: false\n---\n# Newer Post\n'
    );
    fs.writeFileSync(
      path.join(tmpBlog, "2026-11-25-draft.md"),
      '---\ntitle: "Draft Post"\ndraft: true\n---\n# Draft Post\n'
    );

    const sampleIndex = [
      "# Documentation Index",
      "",
      "## Engineering blog",
      "",
      "| Document | Status | What it is |",
      "| --- | --- | --- |",
      "| [Older Post](blog/2026-09-29-older.md) | Current | Older milestone |",
      "| [Cadence Engineering Blog](blog/index.md) | Current | Blog overview |",
      "| [Newer Post](blog/2026-11-19-newer.md) | Current | Newer milestone |",
      "| [Draft Post](blog/2026-11-25-draft.md) | Current | Draft milestone |",
      "",
    ].join("\n");

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
    expect(blogGroup.items.map((item) => item.link)).toEqual([
      "/blog/",
      "/blog/2026-11-19-newer",
      "/blog/2026-09-29-older",
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
    expect(apiGroup.collapsed).toBe(false);
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

  it("validates nested dropdown nav menus, resolves trailing-slash hub routes to index.md, and keeps demos and RSS within two clicks", () => {
    const tmpRepo = makeTempWorkspace();
    const tmpDocs = path.join(tmpRepo, "docs");
    fs.mkdirSync(path.join(tmpDocs, "api"), { recursive: true });
    fs.mkdirSync(path.join(tmpDocs, "adr"), { recursive: true });
    fs.writeFileSync(path.join(tmpDocs, "index.md"), "# Home\n");
    fs.writeFileSync(
      path.join(tmpDocs, "api", "index.md"),
      "# API Hub\n\n## Endpoints\n"
    );
    fs.writeFileSync(path.join(tmpDocs, "adr", "index.md"), "# ADR Log\n");

    expect(() =>
      assertThemeLinksExist(
        {
          nav: [
            { text: "Home", link: "/" },
            {
              text: "Demos",
              items: [
                { text: "Staff Demo", link: "/demo/" },
                { text: "Participant Demo", link: "/subject-portal/" },
              ],
            },
            {
              text: "Reference",
              items: [
                { text: "API", link: "/api/#endpoints" },
                { text: "ADRs", link: "/adr/" },
                { text: "RSS", link: "/feed.xml" },
              ],
            },
          ],
          sidebar: [],
        },
        tmpDocs
      )
    ).not.toThrow();

    expect(() =>
      assertThemeLinksExist(
        {
          nav: [
            {
              text: "Reference",
              items: [{ text: "Missing Hub", link: "/missing-hub/" }],
            },
          ],
          sidebar: [],
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
              items: [{ text: "Missing Anchor", link: "/api/#no-such-anchor" }],
            },
          ],
          sidebar: [],
        },
        tmpDocs
      )
    ).toThrow(/missing anchors: \/api\/#no-such-anchor/);

    // Verify configured themeConfig.nav consolidates links into dropdowns and keeps /demo/, /subject-portal/, and /feed.xml within <= 2 clicks.
    const nav = config.themeConfig.nav;
    expect(nav.length).toBeLessThanOrEqual(7);
    expect(nav.some((entry) => Array.isArray(entry.items))).toBe(true);

    const clickDepthByLink = new Map();
    const walkNav = (entries, depth) => {
      for (const entry of entries ?? []) {
        if (entry.link) {
          clickDepthByLink.set(entry.link, depth);
        }
        if (Array.isArray(entry.items)) {
          walkNav(entry.items, depth + 1);
        }
      }
    };
    walkNav(nav, 1);

    expect(clickDepthByLink.get("/demo/")).toBeLessThanOrEqual(2);
    expect(clickDepthByLink.get("/subject-portal/")).toBeLessThanOrEqual(2);
    expect(clickDepthByLink.get("/feed.xml")).toBeLessThanOrEqual(2);
    expect(clickDepthByLink.has("/blog/index")).toBe(false);
    expect(clickDepthByLink.has("/api/index")).toBe(false);
    expect(clickDepthByLink.has("/adr/index")).toBe(false);
  });
});
