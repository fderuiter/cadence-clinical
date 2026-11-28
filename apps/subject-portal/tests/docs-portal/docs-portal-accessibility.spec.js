import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const docsDistDir = path.resolve(__dirname, "../../../../docs/.vitepress/dist");

const WCAG_AA_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

const EXPLICIT_PORTAL_A11Y_RULE_IDS = [
  "landmark-one-main",
  "landmark-no-duplicate-banner",
  "landmark-no-duplicate-contentinfo",
  "landmark-banner-is-top-level",
  "landmark-contentinfo-is-top-level",
  "nested-interactive",
  "label-content-name-mismatch",
];

const VIEWPORT_WIDTHS = [375, 768, 960, 1024, 1280, 1366, 1440, 1600, 1920];

const SUBDIRECTORY_HUBS = [
  { route: "blog/", prefix: "/cadence-clinical/blog/" },
  { route: "api/", prefix: "/cadence-clinical/api/" },
  { route: "components/", prefix: "/cadence-clinical/components/" },
  { route: "adr/", prefix: "/cadence-clinical/adr/" },
  { route: "migration/", prefix: "/cadence-clinical/migration/" },
];

const PUBLISHED_BLOG_POSTS = [
  "blog/2026-09-29-clinical-kernel-v02-ontology-and-invariants.html",
  "blog/2026-09-30-dotnet-10-modernization-and-python-retirement.html",
  "blog/2026-10-20-governed-ai-proposals-in-one-runtime.html",
  "blog/2026-11-19-keeping-cadence-in-one-repository.html",
  "blog/2026-11-20-postgres-rls-deterministic-iclock-and-gxp-release-evidence.html",
  "blog/2026-11-20-self-service-trial-bootstrap-protocol-import-and-cohosted-demos.html",
];

const AXE_AUDIT_PAGES = [
  { name: "Home", route: "./" },
  { name: "Blog Hub", route: "blog/" },
  {
    name: "Blog Post",
    route: "blog/2026-11-19-keeping-cadence-in-one-repository.html",
  },
  { name: "Documentation Index", route: "DOCUMENTATION_INDEX.html" },
  { name: "API Hub", route: "api/" },
  { name: "API Reference (Kernel)", route: "api/kernel.html" },
  { name: "Components Hub", route: "components/" },
  { name: "ADR Hub", route: "adr/" },
  {
    name: "ADR-2820 Contrast Tokens",
    route:
      "adr/2026-11-19-recalibrate-shared-color-tokens-and-wcag-22-aa-contrast-gates.html",
  },
  { name: "Migration Hub", route: "migration/" },
  { name: "Schema Hub", route: "schema/README.html" },
  { name: "Schema Reference (Kernel)", route: "schema/kernel.html" },
];

function collectPortalHtmlFiles(dir, relativeDir = "") {
  const results = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const relPath = relativeDir ? `${relativeDir}/${entry.name}` : entry.name;
    if (entry.isDirectory()) {
      if (relPath === "demo" || relPath === "subject-portal") {
        continue;
      }
      results.push(
        ...collectPortalHtmlFiles(path.join(dir, entry.name), relPath)
      );
    } else if (entry.isFile() && entry.name.endsWith(".html")) {
      results.push(relPath);
    }
  }
  return results.sort();
}

function decodeHtmlEntities(raw) {
  return raw
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&amp;/g, "&");
}

function stripTags(html) {
  return decodeHtmlEntities(html.replace(/<[^>]+>/g, ""));
}

async function runPortalAxeAudit(page, theme) {
  await page.evaluate((mode) => {
    let styleEl = document.getElementById("cadence-axe-disable-transitions");
    if (!styleEl) {
      styleEl = document.createElement("style");
      styleEl.id = "cadence-axe-disable-transitions";
      styleEl.textContent =
        "*, *::before, *::after { transition: none !important; animation: none !important; }";
      document.head.appendChild(styleEl);
    }
    window.localStorage.setItem("vitepress-theme-appearance", mode);
    document.documentElement.classList.toggle("dark", mode === "dark");
    void document.documentElement.offsetHeight;
  }, theme);

  if (theme === "dark") {
    await expect(page.locator("html")).toHaveClass(/\bdark\b/);
  } else {
    await expect(page.locator("html")).not.toHaveClass(/\bdark\b/);
  }

  const wcagResults = await new AxeBuilder({ page })
    .withTags(WCAG_AA_TAGS)
    .analyze();
  const explicitResults = await new AxeBuilder({ page })
    .withRules(EXPLICIT_PORTAL_A11Y_RULE_IDS)
    .analyze();

  const mergedById = new Map();
  for (const v of [...wcagResults.violations, ...explicitResults.violations]) {
    if (!mergedById.has(v.id)) {
      mergedById.set(v.id, v);
    }
  }

  return Array.from(mergedById.values()).map((v) => ({
    id: v.id,
    impact: v.impact,
    help: v.help,
    nodes: v.nodes.slice(0, 5).map((n) => ({
      target: n.target,
      failureSummary: n.failureSummary,
    })),
  }));
}

test.describe("Documentation Portal Accessibility, Navigation & Table Integrity Suite", () => {
  test.use({ serviceWorkers: "block" });

  test("compiled HTML pages contain zero orphaned pipe paragraphs, zero anchors inside code spans, and full Documentation Index tables", async () => {
    const htmlFiles = collectPortalHtmlFiles(docsDistDir);
    expect(htmlFiles.length).toBeGreaterThanOrEqual(50);
    expect(htmlFiles).toEqual(
      expect.arrayContaining([
        "index.html",
        "DOCUMENTATION_INDEX.html",
        "blog/index.html",
        "api/index.html",
        "components/index.html",
        "adr/index.html",
        "migration/index.html",
      ])
    );

    const orphanedPipeFindings = [];
    const codeAnchorFindings = [];

    for (const relPath of htmlFiles) {
      const fullPath = path.join(docsDistDir, relPath);
      const html = fs.readFileSync(fullPath, "utf8");

      // Inspect rendered paragraphs inside .vp-doc for orphaned pipe-table rows
      for (const pMatch of html.matchAll(/<p(?:\s[^>]*)?>([\s\S]*?)<\/p>/gi)) {
        const text = stripTags(pMatch[1]).trim();
        if (text.startsWith("|") && text.includes("|", 1)) {
          orphanedPipeFindings.push(`${relPath}: ${text.slice(0, 120)}`);
        }
      }

      // Inspect inline <code>...</code> spans (excluding <pre><code> blocks) for swallowed <a> tags
      const withoutPre = html.replace(/<pre\b[^>]*>[\s\S]*?<\/pre>/gi, "");
      for (const codeMatch of withoutPre.matchAll(
        /<code(?:\s[^>]*)?>([\s\S]*?)<\/code>/gi
      )) {
        if (/<a\s/i.test(codeMatch[1])) {
          codeAnchorFindings.push(`${relPath}: ${codeMatch[0].slice(0, 140)}`);
        }
      }
    }

    expect(orphanedPipeFindings).toEqual([]);
    expect(codeAnchorFindings).toEqual([]);

    const countTbodyRows = (html) =>
      Array.from(
        html.matchAll(/<tbody(?:\s[^>]*)?>([\s\S]*?)<\/tbody>/gi),
        (m) => m[1]
      ).reduce(
        (sum, tbody) => sum + (tbody.match(/<tr(?:\s|>)/gi) || []).length,
        0
      );

    const docIndexHtml = fs.readFileSync(
      path.join(docsDistDir, "DOCUMENTATION_INDEX.html"),
      "utf8"
    );
    const shardHubRowCount = countTbodyRows(docIndexHtml);
    expect(shardHubRowCount).toBeGreaterThanOrEqual(25);

    const shardHrefs = [
      ...new Set(
        Array.from(
          docIndexHtml.matchAll(/<tbody(?:\s[^>]*)?>([\s\S]*?)<\/tbody>/gi),
          (m) => m[1]
        ).flatMap((tbody) =>
          Array.from(tbody.matchAll(/href="([^"#?]+)"/gi), (h) => h[1])
        )
      ),
    ];

    const missingShardFiles = [];
    let categoryTableRowCount = 0;
    let adrDecisionEntryCount = 0;

    for (const href of shardHrefs) {
      const relShardPath = href.replace(/^(?:\.\/|\/cadence-clinical\/)/, "");
      const shardHtmlPath =
        relShardPath === ""
          ? path.join(docsDistDir, "index.html")
          : relShardPath.endsWith("/")
            ? path.join(docsDistDir, relShardPath, "index.html")
            : relShardPath.endsWith(".html")
              ? path.join(docsDistDir, relShardPath)
              : path.join(docsDistDir, `${relShardPath}.html`);

      if (!fs.existsSync(shardHtmlPath)) {
        missingShardFiles.push(`${href} -> ${shardHtmlPath}`);
        continue;
      }

      const shardHtml = fs.readFileSync(shardHtmlPath, "utf8");
      if (relShardPath.startsWith("adr/")) {
        const docContent =
          shardHtml.match(/<div class="vp-doc[\s\S]*?<\/main>/i)?.[0] ||
          shardHtml;
        adrDecisionEntryCount += (
          docContent.match(
            /<li(?:\s[^>]*)?>\s*<a\s[^>]*>\s*ADR-[A-Z0-9-]+:/gi
          ) || []
        ).length;
      } else {
        categoryTableRowCount += countTbodyRows(shardHtml);
      }
    }

    expect(missingShardFiles).toEqual([]);
    expect(categoryTableRowCount).toBeGreaterThanOrEqual(136);
    expect(
      categoryTableRowCount + adrDecisionEntryCount
    ).toBeGreaterThanOrEqual(715);
  });

  test("navbar items and portal layout render without clipping, overlap, or horizontal scroll across 375px–1920px viewports", async ({
    page,
  }) => {
    const routesToMeasure = ["./", "api/", "DOCUMENTATION_INDEX.html"];

    for (const width of VIEWPORT_WIDTHS) {
      await page.setViewportSize({ width, height: 900 });

      for (const route of routesToMeasure) {
        await page.goto(route, { waitUntil: "domcontentloaded" });

        const metrics = await page.evaluate((vw) => {
          const scrollWidth = document.documentElement.scrollWidth;
          const innerWidth = window.innerWidth;

          function isVisible(el) {
            if (!el) return false;
            const style = window.getComputedStyle(el);
            if (style.display === "none" || style.visibility === "hidden") {
              return false;
            }
            const rect = el.getBoundingClientRect();
            return rect.width > 0 && rect.height > 0;
          }

          if (vw < 768) {
            const hamburger = document.querySelector(".VPNavBarHamburger");
            const rect = hamburger?.getBoundingClientRect();
            return {
              scrollWidth,
              innerWidth,
              hamburgerVisible: isVisible(hamburger),
              hamburgerInViewport: rect
                ? rect.left >= 0 && rect.right <= vw + 1
                : false,
              clippedControls: [],
              overlappingPairs: [],
            };
          }

          const menuItems = Array.from(
            document.querySelectorAll(
              ".VPNavBarMenu > .VPNavBarMenuLink, .VPNavBarMenu > .VPNavBarMenuGroup"
            )
          ).filter(isVisible);

          const trailingControls = [
            document.querySelector(".VPNavBarSearch"),
            document.querySelector(".VPNavBarMenu"),
            document.querySelector(".VPNavBarAppearance"),
            document.querySelector(".VPNavBarExtra"),
          ].filter(isVisible);

          const clippedControls = [];
          for (const el of [...menuItems, ...trailingControls]) {
            const r = el.getBoundingClientRect();
            const label =
              el.textContent?.trim().replace(/\s+/g, " ").slice(0, 30) ||
              el.className;
            if (r.left < -1 || r.right > vw + 1) {
              clippedControls.push(
                `${label} [${Math.round(r.left)}..${Math.round(r.right)} vs 0..${vw}]`
              );
            }
          }

          const orderedBoxes = menuItems
            .map((el) => ({
              label:
                el.textContent?.trim().replace(/\s+/g, " ") || el.className,
              rect: el.getBoundingClientRect(),
            }))
            .sort((a, b) => a.rect.left - b.rect.left);

          const overlappingPairs = [];
          for (let i = 1; i < orderedBoxes.length; i++) {
            const prev = orderedBoxes[i - 1];
            const curr = orderedBoxes[i];
            if (prev.rect.right > curr.rect.left + 1) {
              overlappingPairs.push(
                `${prev.label} (${Math.round(prev.rect.right)}) overlaps ${curr.label} (${Math.round(curr.rect.left)})`
              );
            }
          }

          return {
            scrollWidth,
            innerWidth,
            menuItemCount: menuItems.length,
            clippedControls,
            overlappingPairs,
          };
        }, width);

        expect(
          metrics.scrollWidth,
          `Horizontal overflow on ${route} at ${width}px`
        ).toBeLessThanOrEqual(metrics.innerWidth);
        expect(
          metrics.clippedControls,
          `Clipped navbar controls on ${route} at ${width}px`
        ).toEqual([]);
        expect(
          metrics.overlappingPairs,
          `Overlapping navbar items on ${route} at ${width}px`
        ).toEqual([]);

        if (width < 768) {
          expect(metrics.hamburgerVisible).toBe(true);
          expect(metrics.hamburgerInViewport).toBe(true);
        } else {
          expect(metrics.menuItemCount).toBe(5);
        }
      }
    }

    // Verify <= 2 click reachability of /demo/, /subject-portal/, /blog/, and /feed.xml from an interior page
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("api/", { waitUntil: "domcontentloaded" });

    const demosTrigger = page.locator(".VPNavBarMenu .VPNavBarMenuGroup", {
      hasText: "Demos",
    });
    await demosTrigger.locator("button").click();
    await expect(
      demosTrigger.locator('a[href$="/cadence-clinical/demo/"]')
    ).toBeVisible();
    await expect(
      demosTrigger.locator('a[href$="/cadence-clinical/subject-portal/"]')
    ).toBeVisible();

    const blogTrigger = page.locator(".VPNavBarMenu .VPNavBarMenuGroup", {
      hasText: "Blog",
    });
    await blogTrigger.locator("button").click();
    await expect(
      blogTrigger.locator('a[href$="/cadence-clinical/blog/"]')
    ).toBeVisible();
    await expect(blogTrigger.locator('a[href$="/feed.xml"]')).toBeVisible();
  });

  test("all 5 subdirectory hubs highlight the active sidebar link, expand parent groups without nested buttons, and expose a sequential Next link", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 900 });

    for (const hub of SUBDIRECTORY_HUBS) {
      await page.goto(hub.route, { waitUntil: "domcontentloaded" });

      const sidebarState = await page.evaluate((expectedPrefix) => {
        const activeItems = Array.from(
          document.querySelectorAll(
            ".VPSidebar .VPSidebarItem.is-active > .item a.link"
          )
        ).map((a) => a.getAttribute("href"));

        const collapsedAncestorCount = Array.from(
          document.querySelectorAll(".VPSidebar .VPSidebarItem.is-active")
        ).reduce((count, item) => {
          let parent = item.parentElement?.closest(".VPSidebarItem");
          while (parent) {
            if (parent.classList.contains("collapsed")) count++;
            parent = parent.parentElement?.closest(".VPSidebarItem");
          }
          return count;
        }, 0);

        const nestedInteractive = Array.from(
          document.querySelectorAll(
            ".VPSidebar button button, .VPSidebar button [role='button'], .VPSidebar [role='button'] button, .VPSidebar a button, .VPSidebar button a"
          )
        ).map((el) => el.outerHTML.slice(0, 120));

        const roleButtons = Array.from(
          document.querySelectorAll(".VPSidebar [role='button']")
        ).map((el) => el.outerHTML.slice(0, 120));

        const toggleButtons = Array.from(
          document.querySelectorAll(".VPSidebar button.item-toggle")
        );
        const invalidExpandedAttrs = toggleButtons
          .map((btn) => btn.getAttribute("aria-expanded"))
          .filter((val) => val !== "true" && val !== "false");

        const nextHref =
          document
            .querySelector(".VPDocFooter .pager-link.next")
            ?.getAttribute("href") || null;

        return {
          activeItems,
          collapsedAncestorCount,
          nestedInteractive,
          roleButtons,
          toggleButtonCount: toggleButtons.length,
          invalidExpandedAttrs,
          nextHref,
          matchesExpectedPrefix: activeItems.includes(expectedPrefix),
        };
      }, hub.prefix);

      expect(
        sidebarState.matchesExpectedPrefix,
        `Active sidebar link missing for ${hub.route}: got ${JSON.stringify(sidebarState.activeItems)}`
      ).toBe(true);
      expect(sidebarState.collapsedAncestorCount).toBe(0);
      expect(sidebarState.nestedInteractive).toEqual([]);
      expect(sidebarState.roleButtons).toEqual([]);
      expect(sidebarState.toggleButtonCount).toBe(0);
      expect(sidebarState.invalidExpandedAttrs).toEqual([]);
      expect(sidebarState.nextHref).not.toBeNull();
      expect(sidebarState.nextHref).toContain(hub.prefix);
      expect(sidebarState.nextHref).not.toBe(hub.prefix);
    }
  });

  test("Home renders a main landmark, search hides shortcut keys from ARIA name, and all 6 blog posts render one banner, zero footer pager links, and un-nested chronological navigation", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("./", { waitUntil: "domcontentloaded" });

    await expect(page.locator("main#VPContent")).toHaveCount(1);
    const searchBtn = page.getByRole("button", { name: "Search", exact: true });
    await expect(searchBtn).toBeVisible();
    await expect(page.locator(".DocSearch-Button-Keys")).toHaveAttribute(
      "aria-hidden",
      "true"
    );
    await expect(page.locator(".DocSearch-Search-Icon")).toHaveAttribute(
      "aria-hidden",
      "true"
    );

    await page.setViewportSize({ width: 375, height: 900 });
    await expect(
      page.getByRole("button", { name: "Search", exact: true })
    ).toBeVisible();
    await page.setViewportSize({ width: 1280, height: 900 });

    for (const postRoute of PUBLISHED_BLOG_POSTS) {
      await page.goto(postRoute, { waitUntil: "domcontentloaded" });

      const postLandmarks = await page.evaluate(() => {
        const sectioningSelector = "article, aside, main, nav, section";
        const topLevelBanners = Array.from(
          document.querySelectorAll("header, [role='banner']")
        ).filter((el) => !el.parentElement?.closest(sectioningSelector));

        const topLevelContentinfos = Array.from(
          document.querySelectorAll("footer, [role='contentinfo']")
        ).filter((el) => !el.parentElement?.closest(sectioningSelector));

        const nestedFooters = document.querySelectorAll("footer footer").length;
        const mainCount = document.querySelectorAll(
          "main, [role='main']"
        ).length;
        const vpDocPagerLinks = document.querySelectorAll(
          ".VPDocFooter .pager-link"
        ).length;
        const blogPostHeaderTag =
          document.querySelector(".blog-post-header")?.tagName || null;
        const blogPostFooterTag =
          document.querySelector(".blog-post-footer")?.tagName || null;
        const footerInsideVpDocFooter = Boolean(
          document.querySelector(".VPDocFooter .blog-post-footer")
        );
        const chronoCards = document.querySelectorAll(
          ".blog-chronological-nav .blog-chrono-card"
        ).length;

        return {
          topLevelBannerCount: topLevelBanners.length,
          topLevelContentinfoCount: topLevelContentinfos.length,
          nestedFooters,
          mainCount,
          vpDocPagerLinks,
          blogPostHeaderTag,
          blogPostFooterTag,
          footerInsideVpDocFooter,
          chronoCards,
        };
      });

      expect(postLandmarks.mainCount).toBe(1);
      expect(postLandmarks.topLevelBannerCount).toBe(1);
      expect(postLandmarks.topLevelContentinfoCount).toBeLessThanOrEqual(1);
      expect(postLandmarks.nestedFooters).toBe(0);
      expect(postLandmarks.vpDocPagerLinks).toBe(0);
      expect(postLandmarks.blogPostHeaderTag).toBe("DIV");
      expect(postLandmarks.blogPostFooterTag).toBe("DIV");
      expect(postLandmarks.footerInsideVpDocFooter).toBe(false);
      expect(postLandmarks.chronoCards).toBeGreaterThanOrEqual(1);
    }
  });

  for (const target of AXE_AUDIT_PAGES) {
    test(`passes axe-core WCAG 2.2 Level AA, landmark, nested-interactive, and label-in-name checks in light and dark modes: ${target.name}`, async ({
      page,
    }) => {
      await page.setViewportSize({ width: 1280, height: 900 });
      await page.goto(target.route, { waitUntil: "domcontentloaded" });

      const lightViolations = await runPortalAxeAudit(page, "light");
      expect(
        lightViolations,
        `Light-mode axe violations on ${target.name} (${target.route})`
      ).toEqual([]);

      const darkViolations = await runPortalAxeAudit(page, "dark");
      expect(
        darkViolations,
        `Dark-mode axe violations on ${target.name} (${target.route})`
      ).toEqual([]);
    });
  }
});
