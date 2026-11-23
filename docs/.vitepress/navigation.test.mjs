import fs from "fs";
import os from "os";
import path from "path";
import { fileURLToPath } from "url";
import { createMarkdownRenderer } from "vitepress";
import { afterEach, describe, expect, it } from "vitest";

import {
  assertThemeLinksExist,
  dualHeadingAnchorsPlugin,
  extractMarkdownAnchors,
  githubSlugify,
  repoLinksPlugin,
  toRepoUrl,
  vitepressSlugify,
} from "./repoLinks.mjs";
import { createRequire } from "module";
import rootDocsModule from "./rootDocs.cjs";
import {
  parseDocumentationIndex,
  synthesizeSidebarFromIndex,
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

function collectSidebarLinks(items, out = new Set()) {
  for (const item of items ?? []) {
    if (item.link) {
      out.add(item.link.split("#")[0]);
    }
    if (item.items) {
      collectSidebarLinks(item.items, out);
    }
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

  it("includes all 80 active non-ADR pages from docs/DOCUMENTATION_INDEX.md without manual edits", () => {
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
    const sidebarLinks = collectSidebarLinks(sidebar);

    for (const route of allActiveNonAdrPortalRoutes) {
      expect(
        sidebarLinks.has(route),
        `Expected sidebar to contain ${route}`
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
    expect(sidebarLinks.has("/blog/index")).toBe(true);
  });

  it("synthesizes the Engineering Blog group newest-first and excludes draft blog posts from portal routes", () => {
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

    const blogGroup = sidebar.find((g) => g.text === "Engineering Blog");
    expect(blogGroup).toBeDefined();
    expect(blogGroup.collapsed).toBe(false);
    expect(blogGroup.items.map((item) => item.link)).toEqual([
      "/blog/index",
      "/blog/2026-11-19-newer",
      "/blog/2026-09-29-older",
    ]);
  });

  it("groups REST API and Data Model references into collapsed sections separate from historical records", () => {
    const sidebar = synthesizeSidebarFromIndex(indexPath, {
      docsDir,
      repoRoot,
    });

    const apiGroup = sidebar.find((g) => g.text === "REST API Reference");
    const schemaGroup = sidebar.find((g) => g.text === "Data Model Reference");
    const archiveGroup = sidebar.find(
      (g) => g.text === "Baseline & Historical Records"
    );

    expect(apiGroup).toBeDefined();
    expect(apiGroup.collapsed).toBe(true);
    expect(apiGroup.items[0].link).toBe("/api/index");

    expect(schemaGroup).toBeDefined();
    expect(schemaGroup.collapsed).toBe(true);
    expect(schemaGroup.items[0].link).toBe("/schema/README");
    expect(schemaGroup.items.length).toBeGreaterThanOrEqual(18);

    expect(archiveGroup).toBeDefined();
    expect(archiveGroup.collapsed).toBe(true);

    const archiveLinks = collectSidebarLinks(archiveGroup.items);
    expect(archiveLinks.has("/api/kernel")).toBe(false);
    expect(archiveLinks.has("/schema/kernel")).toBe(false);
  });

  it("automatically places a newly added active Start here document row into the sidebar", () => {
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

    const startHere = sidebar.find((g) => g.text === "Start Here");
    expect(startHere).toBeDefined();
    expect(
      startHere.items.some((item) => item.link === "/ONBOARDING_TEST_GUIDE")
    ).toBe(true);
  });

  it("stages all 8 root documents in STAGED_ROOT_DOCS and keeps their links internal while routing non-portal links to GitHub", () => {
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
      toRepoUrl("../packages/kernel/README.md", tmpDocs, tmpDocs, tmpRepo)
    ).toBe(
      "https://github.com/fderuiter/cadence-clinical/blob/main/packages/kernel/README.md"
    );
    expect(toRepoUrl("../.env.example", tmpDocs, tmpDocs, tmpRepo)).toBe(
      "https://github.com/fderuiter/cadence-clinical/blob/main/.env.example"
    );
  });

  it("emits both VitePress and GitHub heading anchor IDs for numbered, punctuated, and schema table headings", async () => {
    const tmpDir = makeTempWorkspace();
    const md = await createMarkdownRenderer(tmpDir);
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

    // Slug helpers agree with extracted anchor set
    expect(vitepressSlugify("15. Roadmap")).toBe("_15-roadmap");
    expect(githubSlugify("15. Roadmap")).toBe("15-roadmap");
    expect(githubSlugify("kernel.activity_entries")).toBe(
      "kernelactivity_entries"
    );
  });

  it("fails closed in repoLinksPlugin and assertThemeLinksExist when a link targets a missing heading anchor", async () => {
    const tmpRepo = makeTempWorkspace();
    const tmpDocs = path.join(tmpRepo, "docs");
    fs.mkdirSync(tmpDocs, { recursive: true });

    fs.writeFileSync(
      path.join(tmpDocs, "target.md"),
      "# Target Page\n\n## Valid Heading\n"
    );
    fs.writeFileSync(
      path.join(tmpDocs, "source.md"),
      "# Source Page\n\nSee [Target](target.md#valid-heading).\n"
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
          sidebar: [{ text: "Broken", link: "/target#missing-sidebar-anchor" }],
        },
        tmpDocs
      )
    ).toThrow(/missing anchors: \/target#missing-sidebar-anchor/);

    const anchors = extractMarkdownAnchors(
      "# Title\n\n## Duplicate\n\n## Duplicate\n"
    );
    expect(anchors.has("duplicate")).toBe(true);
    expect(anchors.has("duplicate-1")).toBe(true);
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
