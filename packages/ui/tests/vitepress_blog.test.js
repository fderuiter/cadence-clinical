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
  transformContentLoaderPosts,
  writeRssFeed,
} from "../../../docs/.vitepress/blog.mjs";
import {
  assertThemeLinksExist,
  repoLinksPlugin,
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

  it("escapes XML special characters and excludes drafts from generated RSS 2.0 XML", () => {
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
          authors: ["@fderuiter"],
          summary: " human-attested & 'grounded' proposals <100%>.",
          tags: ["ai", "gxp&audit"],
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
        siteOrigin: "https://fderuiter.github.io",
        base: "/cadence-clinical/",
      }
    );

    expect(xml).toContain('<?xml version="1.0" encoding="UTF-8"?>');
    expect(xml).toContain('<rss version="2.0"');
    expect(xml).toContain(
      '<atom:link href="https://fderuiter.github.io/cadence-clinical/feed.xml" rel="self" type="application/rss+xml" />'
    );
    expect(xml).toContain(
      "<title>Governed AI &amp; &quot;Proposals&quot; &lt;K037&gt;</title>"
    );
    expect(xml).toContain("<category>gxp&amp;audit</category>");
    expect(xml).not.toContain("Secret Draft Post");
  });

  it("scans disk posts, computes srcExclude draft globs, and writes feed.xml", () => {
    const repoRoot = makeTempDocsDir();
    const docsDir = path.join(repoRoot, "docs");
    const outDir = path.join(docsDir, ".vitepress", "dist");

    fs.writeFileSync(
      path.join(docsDir, "blog", "index.md"),
      "---\ntitle: Blog\n---\n# Blog\n"
    );
    fs.writeFileSync(
      path.join(docsDir, "blog", "2026-10-01-published.md"),
      '---\ntitle: "Published Story"\ndate: "2026-10-01"\nsummary: "Live post"\ntags: ["kernel"]\ndraft: false\n---\n\n# Published Story\n'
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
      "/blog/index",
      "/blog/2026-10-01-published",
    ]);

    const feedPath = writeRssFeed({
      srcDir: docsDir,
      outDir,
      site: { base: "/cadence-clinical/" },
    });
    const feedXml = fs.readFileSync(feedPath, "utf-8");
    expect(feedXml).toContain("Published Story");
    expect(feedXml).not.toContain("Hidden Draft");
  });

  it("allows /feed.xml in assertThemeLinksExist and rewrites draft post links in repoLinksPlugin", () => {
    const repoRoot = makeTempDocsDir();
    const docsDir = path.join(repoRoot, "docs");
    fs.writeFileSync(path.join(docsDir, "blog", "index.md"), "# Blog\n");
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
            { text: "Blog", link: "/blog/index" },
            { text: "RSS", link: "/feed.xml" },
          ],
          sidebar: [],
        },
        docsDir
      )
    ).not.toThrow();

    expect(() =>
      assertThemeLinksExist(
        {
          nav: [{ text: "Broken", link: "/does-not-exist" }],
        },
        docsDir
      )
    ).toThrow(/missing pages/);

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
    repoLinksPlugin(fakeMd, { docsDir, repoRoot });

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
    const draftToken = makeLinkToken("blog/2026-10-02-draft.md");
    rules[0].fn({
      env: { relativePath: "DOCUMENTATION_INDEX.md" },
      tokens: [{ children: [liveToken, draftToken] }],
    });

    expect(liveToken.getHref()).toBe("blog/2026-10-01-live.md");
    expect(draftToken.getHref()).toBe(
      "https://github.com/fderuiter/cadence-clinical/blob/main/docs/blog/2026-10-02-draft.md"
    );
  });

  it("loads the repository founding blog posts with complete metadata and related decisions", () => {
    const repoDocsDir = path.resolve(__dirname, "../../../docs");
    const posts = loadPublishedBlogPosts(repoDocsDir);

    expect(posts.length).toBeGreaterThanOrEqual(4);
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
  });

  it("normalizes staged root guide links and co-hosted demo subpaths cleanly", () => {
    expect(normalizeSiteLink("../../ARCHITECTURE.md#system-topology")).toBe(
      "/ARCHITECTURE.html#system-topology"
    );
    expect(normalizeSiteLink("../adr/index.md")).toBe("/adr/");
    expect(normalizeSiteLink("/demo/")).toBe("/demo/");
    expect(normalizeSiteLink("/subject-portal/")).toBe("/subject-portal/");
    expect(
      resolveBaseHref(
        normalizeSiteLink("../../ARCHITECTURE.md"),
        "/cadence-clinical/"
      )
    ).toBe("/cadence-clinical/ARCHITECTURE.html");
    expect(resolveBaseHref("/demo/", "/cadence-clinical/")).toBe(
      "/cadence-clinical/demo/"
    );
  });
});
