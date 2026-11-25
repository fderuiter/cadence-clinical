import fs from "fs";
import os from "os";
import path from "path";
import { afterEach, describe, expect, it } from "vitest";
import {
  buildRssFeedXml,
  escapeXml,
  filterBlogPostsByTag,
  getBlogSidebarItems,
  getDraftBlogPostExcludes,
  isDraftBlogPostFile,
  loadPublishedBlogPosts,
  normalizeBlogPost,
  normalizeSiteLink,
  parseBlogFrontmatter,
  resolveBaseHref,
  resolveBlogLinkRel,
  resolveBlogLinkTarget,
  transformContentLoaderPosts,
  writeRssFeed,
} from "../../../docs/.vitepress/blog.mjs";
import {
  assertThemeLinksExist,
  repoLinksPlugin,
  toRepoUrl,
} from "../../../docs/.vitepress/repoLinks.mjs";

describe("VitePress Engineering Blog Engine", () => {
  const tempDirs = [];

  function makeTempDocsDir() {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "cadence-blog-test-"));
    tempDirs.push(root);
    fs.mkdirSync(path.join(root, "docs", "blog"), { recursive: true });
    return root;
  }

  afterEach(() => {
    while (tempDirs.length > 0) {
      const dir = tempDirs.pop();
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  it("parses YAML frontmatter scalars, inline arrays, block lists, booleans, and CRLF newlines", () => {
    const raw = [
      "---",
      'title: "Kernel v0.2: \\"Truth\\" & Evidence"',
      'date: "2026-10-20"',
      "authors:",
      '  - "@fderuiter"',
      '  - "@clinical-lead"',
      'summary: "Bitemporal clinical evidence in PostgreSQL."',
      'tags: ["kernel", "architecture", "gxp"]',
      "draft: false",
      "related:",
      '  - text: "ADR-2244"',
      '    link: "../adr/2026-09-29-cdisc-odm-v2-canonical-clinical-data-backbone.md"',
      "---",
      "",
      "# Kernel v0.2",
      "",
      "Body paragraph.",
    ].join("\r\n");

    const { frontmatter, body } = parseBlogFrontmatter(raw);

    expect(frontmatter.title).toBe('Kernel v0.2: "Truth" & Evidence');
    expect(frontmatter.date).toBe("2026-10-20");
    expect(frontmatter.authors).toEqual(["@fderuiter", "@clinical-lead"]);
    expect(frontmatter.summary).toBe(
      "Bitemporal clinical evidence in PostgreSQL."
    );
    expect(frontmatter.tags).toEqual(["kernel", "architecture", "gxp"]);
    expect(frontmatter.draft).toBe(false);
    expect(frontmatter.related).toEqual([
      {
        text: "ADR-2244",
        link: "../adr/2026-09-29-cdisc-odm-v2-canonical-clinical-data-backbone.md",
      },
    ]);
    expect(body).toContain("# Kernel v0.2");
  });

  it("returns empty frontmatter when a Markdown file has no YAML header", () => {
    const { frontmatter, body } = parseBlogFrontmatter(
      "# Plain Markdown\nHi\n"
    );
    expect(frontmatter).toEqual({});
    expect(body).toContain("# Plain Markdown");
  });

  it("excludes blog index and draft posts while sorting published posts newest-first", () => {
    const transformed = transformContentLoaderPosts([
      {
        url: "/blog/",
        frontmatter: { title: "Engineering Blog" },
      },
      {
        url: "/blog/2026-09-29-older-post.html",
        frontmatter: {
          title: "Older Post",
          date: "2026-09-29",
          tags: ["Kernel", "Architecture"],
          draft: false,
        },
      },
      {
        url: "/blog/2026-11-25-upcoming-draft.html",
        frontmatter: {
          title: "Upcoming Draft",
          date: "2026-11-25",
          tags: ["ai"],
          draft: true,
        },
      },
      {
        url: "/blog/2026-11-19-newer-post.html",
        frontmatter: {
          title: "Newer Post",
          date: "2026-11-19",
          tags: ["Architecture", "Modular-Monolith"],
        },
      },
    ]);

    expect(transformed.map((p) => p.slug)).toEqual([
      "2026-11-19-newer-post",
      "2026-09-29-older-post",
    ]);
    expect(
      filterBlogPostsByTag(transformed, "modular-monolith").map((p) => p.slug)
    ).toEqual(["2026-11-19-newer-post"]);
    expect(filterBlogPostsByTag(transformed, "all")).toHaveLength(2);
    expect(
      normalizeBlogPost({
        url: "/blog/2026-11-25-draft.html",
        frontmatter: { title: "Draft", draft: "true" },
      })
    ).toBeNull();
  });

  it("parses YAML folded and literal block scalars including nested related fields", () => {
    const raw = [
      "---",
      "title: >-",
      "  Folded Title",
      "  Across Lines",
      'date: "2026-11-20T16:00:00Z"',
      "summary: |-",
      "  Line one.",
      "  Line two.",
      "notes: |+",
      "  Keep trailing newline.",
      "",
      "related:",
      "  - text: >",
      "      Companion",
      "      Decision",
      '    link: "../adr/index.md"',
      "draft: false",
      "---",
      "",
      "# Body",
    ].join("\n");

    const { frontmatter } = parseBlogFrontmatter(raw);
    expect(frontmatter.title).toBe("Folded Title Across Lines");
    expect(frontmatter.summary).toBe("Line one.\nLine two.");
    expect(frontmatter.notes).toBe("Keep trailing newline.\n\n");
    expect(frontmatter.related).toEqual([
      {
        text: "Companion Decision",
        link: "../adr/index.md",
      },
    ]);
  });

  it("escapes XML special characters, emits full-content CDATA RSS 2.0 with absolutized URLs, and preserves ISO-8601 timestamps", () => {
    expect(escapeXml(`R&D <Kernel> "v0.2" 'quote'`)).toBe(
      "R&amp;D &lt;Kernel&gt; &quot;v0.2&quot; &apos;quote&apos;"
    );

    const xml = buildRssFeedXml(
      [
        {
          title: `Governed AI & "Proposals" <K037>`,
          slug: "2026-10-20-governed-ai",
          url: "/blog/2026-10-20-governed-ai.html",
          date: "2026-10-20",
          publishedAt: "2026-10-20T14:30:00Z",
          authors: ["@fderuiter"],
          summary: " human-attested & 'grounded' proposals <100%>.",
          body: [
            "# Governed AI",
            "",
            "Read [ADR-2603](../adr/2026-11-19-keep-cadence-in-one-repository.md), jump to [Details](#details), or inspect `]]>` safely.",
            "",
            "## Details",
            "",
            "- Item one with **bold** and `inline code`",
          ].join("\n"),
          tags: ["ai", "gxp&audit"],
          draft: false,
        },
        {
          title: "Pre-Rendered HTML Item",
          slug: "2026-10-19-prerendered",
          url: "/blog/2026-10-19-prerendered.html",
          date: "2026-10-19",
          authors: ["@fderuiter"],
          summary: "Exercises CDATA splitting.",
          html: "<p>Raw ]]> sequence</p>",
          tags: ["rss"],
          draft: false,
        },
        {
          title: "Secret Draft Post",
          slug: "2026-11-30-secret-draft",
          url: "/blog/2026-11-30-secret-draft.html",
          date: "2026-11-30",
          authors: ["@fderuiter"],
          summary: "Should never appear in RSS.",
          tags: ["draft"],
          draft: true,
        },
      ],
      {
        siteOrigin: "https://docs.example.org/",
        base: "/cadence-clinical/",
      }
    );

    expect(xml).toContain('<?xml version="1.0" encoding="UTF-8"?>');
    expect(xml).toContain(
      'xmlns:content="http://purl.org/rss/1.0/modules/content/"'
    );
    expect(xml).toContain(
      '<atom:link href="https://docs.example.org/cadence-clinical/feed.xml" rel="self" type="application/rss+xml" />'
    );
    expect(xml).toContain("<pubDate>Tue, 20 Oct 2026 14:30:00 GMT</pubDate>");
    expect(xml).toContain("<content:encoded><![CDATA[");
    expect(xml).toContain(
      'href="https://docs.example.org/cadence-clinical/adr/2026-11-19-keep-cadence-in-one-repository.html"'
    );
    expect(xml).toContain(
      'href="https://docs.example.org/cadence-clinical/blog/2026-10-20-governed-ai.html#details"'
    );
    expect(xml).toContain("<code>]]&gt;</code>");
    expect(xml).toContain("]]]]><![CDATA[>");
    expect(xml).toContain(
      "<title>Governed AI &amp; &quot;Proposals&quot; &lt;K037&gt;</title>"
    );
    expect(xml).toContain("<category>gxp&amp;audit</category>");
    expect(xml).not.toContain("Secret Draft Post");
  });

  it("scans disk posts, computes srcExclude draft globs, and honors VITEPRESS_SITE_ORIGIN in writeRssFeed", () => {
    const repoRoot = makeTempDocsDir();
    const docsDir = path.join(repoRoot, "docs");
    const outDir = path.join(docsDir, ".vitepress", "dist");

    fs.writeFileSync(
      path.join(docsDir, "blog", "index.md"),
      "---\ntitle: Blog\n---\n# Blog\n"
    );
    fs.writeFileSync(
      path.join(docsDir, "blog", "2026-10-01-published.md"),
      '---\ntitle: "Published Story"\ndate: "2026-10-01T09:15:00Z"\nsummary: "Live post"\ntags: ["kernel"]\ndraft: false\n---\n\n# Published Story\n\nBody with [Index](./index.md).\n'
    );
    const draftPath = path.join(docsDir, "blog", "2026-10-02-draft.md");
    fs.writeFileSync(
      draftPath,
      '---\ntitle: "Hidden Draft"\ndate: "2026-10-02"\nsummary: "Hidden"\ntags: ["kernel"]\ndraft: true\n---\n\n# Hidden Draft\n'
    );

    expect(isDraftBlogPostFile(draftPath)).toBe(true);
    expect(getDraftBlogPostExcludes(docsDir)).toEqual([
      "blog/2026-10-02-draft.md",
    ]);

    const published = loadPublishedBlogPosts(docsDir);
    expect(published).toHaveLength(1);
    expect(published[0].title).toBe("Published Story");

    const sidebarItems = getBlogSidebarItems(docsDir);
    expect(sidebarItems.map((i) => i.link)).toEqual([
      "/blog/",
      "/blog/2026-10-01-published",
    ]);

    const previousOrigin = process.env.VITEPRESS_SITE_ORIGIN;
    process.env.VITEPRESS_SITE_ORIGIN = "https://preview.example.com/";
    try {
      const feedPath = writeRssFeed({
        srcDir: docsDir,
        outDir,
        site: { base: "/cadence-clinical/" },
      });
      const feedXml = fs.readFileSync(feedPath, "utf-8");
      expect(feedXml).toContain(
        "https://preview.example.com/cadence-clinical/feed.xml"
      );
      expect(feedXml).toContain("Thu, 01 Oct 2026 09:15:00 GMT");
      expect(feedXml).toContain("<content:encoded><![CDATA[");
      expect(feedXml).toContain(
        'href="https://preview.example.com/cadence-clinical/blog/"'
      );
      expect(feedXml).not.toContain("Hidden Draft");
    } finally {
      if (previousOrigin === undefined) {
        delete process.env.VITEPRESS_SITE_ORIGIN;
      } else {
        process.env.VITEPRESS_SITE_ORIGIN = previousOrigin;
      }
    }
  });

  it("accepts base-prefixed /feed.xml in assertThemeLinksExist and rejects draft or broken frontmatter.related links in repoLinksPlugin", () => {
    const repoRoot = makeTempDocsDir();
    const docsDir = path.join(repoRoot, "docs");
    fs.mkdirSync(path.join(docsDir, "adr"), { recursive: true });
    fs.mkdirSync(path.join(docsDir, "SDLC"), { recursive: true });
    fs.writeFileSync(path.join(docsDir, "blog", "index.md"), "# Blog\n");
    fs.writeFileSync(
      path.join(docsDir, "SDLC", "03_API_Integration_Specification.md"),
      "# API Integration Spec\n\n## Endpoints\n"
    );
    fs.writeFileSync(
      path.join(docsDir, "adr", "2026-10-01-sample.md"),
      "# ADR-1: Sample\n\n## Known Anchor\n"
    );
    fs.writeFileSync(
      path.join(docsDir, "blog", "2026-10-01-live.md"),
      "---\ntitle: Live\ndraft: false\n---\n# Live\n"
    );
    fs.writeFileSync(
      path.join(docsDir, "blog", "2026-10-02-draft.md"),
      "---\ntitle: Draft\ndraft: true\n---\n# Draft\n"
    );

    expect(() =>
      assertThemeLinksExist(
        {
          nav: [
            { text: "Blog", link: "/blog/" },
            { text: "RSS", link: "/cadence-clinical/feed.xml" },
          ],
          sidebar: [],
        },
        docsDir,
        { base: "/cadence-clinical/" }
      )
    ).not.toThrow();

    expect(() =>
      assertThemeLinksExist(
        {
          sidebar: [{ text: "Draft", link: "/blog/2026-10-02-draft" }],
        },
        docsDir,
        { base: "/cadence-clinical/" }
      )
    ).toThrow(/missing pages/);

    expect(
      toRepoUrl("blog/2026-10-02-draft.md", docsDir, docsDir, repoRoot)
    ).toBeNull();

    const rules = [];
    const fakeMd = {
      core: {
        ruler: {
          push(name, fn) {
            rules.push({ name, fn });
          },
        },
      },
    };
    repoLinksPlugin(fakeMd, { docsDir, repoRoot, isDevServer: false });

    const makeLinkToken = (initialHref) => {
      let currentHref = initialHref;
      return {
        type: "link_open",
        attrGet: (attr) => (attr === "href" ? currentHref : null),
        attrSet: (attr, val) => {
          if (attr === "href") currentHref = val;
        },
        getHref: () => currentHref,
      };
    };

    const liveToken = makeLinkToken("blog/2026-10-01-live.md");
    const excludedToken = makeLinkToken(
      "SDLC/03_API_Integration_Specification.md#endpoints"
    );
    rules[0].fn({
      env: { relativePath: "DOCUMENTATION_INDEX.md" },
      tokens: [{ children: [liveToken, excludedToken] }],
    });
    expect(liveToken.getHref()).toBe("blog/2026-10-01-live.md");
    expect(excludedToken.getHref()).toBe(
      "https://github.com/fderuiter/cadence-clinical/blob/main/docs/SDLC/03_API_Integration_Specification.md#endpoints"
    );

    const draftToken = makeLinkToken("blog/2026-10-02-draft.md");
    expect(() =>
      rules[0].fn({
        env: { relativePath: "DOCUMENTATION_INDEX.md" },
        tokens: [{ children: [draftToken] }],
      })
    ).toThrow(/targets draft blog post/);

    expect(() =>
      rules[0].fn({
        env: {
          relativePath: "blog/2026-10-01-live.md",
          frontmatter: {
            related: [{ text: "Draft", link: "./2026-10-02-draft.md" }],
          },
        },
        tokens: [],
      })
    ).toThrow(/Draft blog post referenced in frontmatter\.related/);

    expect(() =>
      rules[0].fn({
        env: {
          relativePath: "blog/2026-10-01-live.md",
          frontmatter: {
            related: [
              {
                text: "Bad Anchor",
                link: "../adr/2026-10-01-sample.md#missing",
              },
            ],
          },
        },
        tokens: [],
      })
    ).toThrow(/Broken heading anchor in frontmatter\.related/);
  });

  it("keeps docs/.vitepress/blogShared.mjs free of Node-only imports and loads all published milestone posts including 2026-11-20 ADRs", () => {
    const blogSharedSource = fs.readFileSync(
      path.resolve(__dirname, "../../../docs/.vitepress/blogShared.mjs"),
      "utf-8"
    );
    expect(blogSharedSource).not.toMatch(
      /\bfrom\s+["'](?:node:)?(?:fs|path|process|url)["']/
    );
    expect(blogSharedSource).not.toMatch(
      /\brequire\(\s*["'](?:node:)?(?:fs|path|process|url)["']\s*\)/
    );

    const repoDocsDir = path.resolve(__dirname, "../../../docs");
    const posts = loadPublishedBlogPosts(repoDocsDir);

    expect(posts.length).toBeGreaterThanOrEqual(6);
    expect(posts.map((p) => p.date)).toEqual(
      [...posts.map((p) => p.date)].sort((a, b) => b.localeCompare(a))
    );

    for (const post of posts) {
      expect(post.title.length).toBeGreaterThan(5);
      expect(post.summary.length).toBeGreaterThan(20);
      expect(post.authors.length).toBeGreaterThan(0);
      expect(post.tags.length).toBeGreaterThan(0);
      expect(post.related.length).toBeGreaterThan(0);
    }

    const allRelatedLinks = posts.flatMap((p) => p.related.map((r) => r.link));
    for (const adrSlug of [
      "/adr/2026-11-20-synchronous-odm-usdm-protocol-draft-import.html",
      "/adr/2026-11-20-keycloak-self-registration-and-clinical-kernel-trial-bootstrap.html",
      "/adr/2026-11-20-export-canonical-demo-manifest-from-kernel-seed.html",
      "/adr/2026-11-20-co-host-interactive-demos-on-github-pages-subpaths.html",
      "/adr/2026-11-20-focus-active-ecrf-view-on-backed-kernel-forms-outside-demo-mode.html",
      "/adr/2026-11-20-standalone-postgres-rls-and-shared-scope-guards.html",
      "/adr/2026-11-20-clinical-and-gateway-endpoints-take-time-from-iclock-and-ban-timeprovider.html",
      "/adr/2026-11-20-share-session-logout-and-signature-primitives.html",
      "/adr/2026-11-20-contextual-sop-resolution-and-audited-ticket-escalation.html",
      "/adr/2026-11-20-forward-verified-ci-gxp-evidence-and-sync-service-migrations.html",
    ]) {
      expect(allRelatedLinks).toContain(adrSlug);
    }
  });

  it("normalizes sibling blog posts, staged root guides, portal pages, and GitHub repo files cleanly", () => {
    expect(
      normalizeSiteLink("./2026-11-19-keeping-cadence-in-one-repository.md")
    ).toBe("/blog/2026-11-19-keeping-cadence-in-one-repository.html");
    expect(
      normalizeSiteLink("2026-11-19-keeping-cadence-in-one-repository.md")
    ).toBe("/blog/2026-11-19-keeping-cadence-in-one-repository.html");
    expect(
      normalizeSiteLink("../../ARCHITECTURE.md#1-target-architecture")
    ).toBe("/ARCHITECTURE.html#1-target-architecture");
    expect(normalizeSiteLink("../../LICENSE")).toBe("/LICENSE.html");
    expect(normalizeSiteLink("../adr/index.md")).toBe("/adr/");
    expect(normalizeSiteLink("../PROJECT_PLAN.md")).toBe("/PROJECT_PLAN.html");
    expect(
      normalizeSiteLink("../SDLC/03_API_Integration_Specification.md#endpoints")
    ).toBe(
      "https://github.com/fderuiter/cadence-clinical/blob/main/docs/SDLC/03_API_Integration_Specification.md#endpoints"
    );
    expect(normalizeSiteLink("/demo/")).toBe("/demo/");
    expect(normalizeSiteLink("/subject-portal/")).toBe("/subject-portal/");
    expect(normalizeSiteLink("../../packages/kernel/README.md")).toBe(
      "https://github.com/fderuiter/cadence-clinical/blob/main/packages/kernel/README.md"
    );
    expect(
      resolveBaseHref(
        normalizeSiteLink("../../ARCHITECTURE.md"),
        "/cadence-clinical/"
      )
    ).toBe("/cadence-clinical/ARCHITECTURE.html");
    expect(resolveBaseHref("/demo/", "/cadence-clinical/")).toBe(
      "/cadence-clinical/demo/"
    );
    expect(resolveBlogLinkTarget("/demo/")).toBe("_self");
    expect(resolveBlogLinkTarget("/subject-portal/")).toBe("_self");
    expect(
      resolveBlogLinkTarget("https://github.com/fderuiter/cadence-clinical")
    ).toBe("_blank");
    expect(resolveBlogLinkTarget("/blog/index.html")).toBeUndefined();
    expect(
      resolveBlogLinkRel("https://github.com/fderuiter/cadence-clinical")
    ).toBe("noopener");
    expect(resolveBlogLinkRel("/demo/")).toBeUndefined();
  });

  it("enforces custom CSS ESLint rules and resolves VitePress --vp-* variables in docs/.vitepress/**/*.css", async () => {
    const repoRoot = path.resolve(__dirname, "../../..");
    const customCssPath = path.join(
      repoRoot,
      "docs/.vitepress/theme/custom.css"
    );
    const { ESLint } = await import("eslint");
    const eslint = new ESLint({
      cwd: repoRoot,
      overrideConfigFile: path.join(repoRoot, "eslint.config.mjs"),
    });

    const [cleanResult] = await eslint.lintFiles([customCssPath]);
    expect(cleanResult.messages).toEqual([]);

    const [validVpResult] = await eslint.lintText(
      ".mermaid-btn:hover:not(:disabled) {\n  color: var(--vp-c-text-1);\n  box-shadow: var(--vp-shadow-2);\n  font-family: var(--semantic-font-family);\n}\n",
      { filePath: path.join(repoRoot, "docs/.vitepress/theme/__probe.css") }
    );
    expect(validVpResult.messages).toEqual([]);

    const [invalidVpResult] = await eslint.lintText(
      ".mermaid-btn:hover {\n  color: #ff00aa;\n  background: var(--unknown-vp-var);\n  outline: none;\n}\n",
      { filePath: path.join(repoRoot, "docs/.vitepress/theme/__probe.css") }
    );
    const ruleIds = invalidVpResult.messages.map((m) => m.ruleId);
    expect(ruleIds).toContain("custom-vue/no-unguarded-button-state");
    expect(ruleIds).toContain("custom-vue/no-inline-hex");
    expect(ruleIds).toContain("custom-vue/no-undefined-css-vars");
    expect(ruleIds).toContain("custom-vue/no-suppressed-focus-outline");
  });
});
