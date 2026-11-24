import fs from "fs";
import path from "path";
import process from "process";
import {
  SITE_ORIGIN,
  buildRssFeedXml,
  normalizeBlogPost,
  parseBlogFrontmatter,
  sortBlogPostsReverseChronological,
} from "./blogShared.mjs";

export * from "./blogShared.mjs";

/**
 * Checks whether a Markdown file on disk is a blog post marked with `draft: true`.
 *
 * @param {string} filePath
 * @return {boolean}
 */
export function isDraftBlogPostFile(filePath) {
  if (!fs.existsSync(filePath)) return false;
  const normalizedPath = filePath.replace(/\\/g, "/");
  if (
    !normalizedPath.includes("/blog/") ||
    normalizedPath.endsWith("/blog/index.md")
  ) {
    return false;
  }
  const content = fs.readFileSync(filePath, "utf-8");
  const { frontmatter } = parseBlogFrontmatter(content);
  return (
    frontmatter.draft === true ||
    /^true$/i.test(String(frontmatter.draft ?? "").trim())
  );
}

/**
 * Returns `srcExclude` patterns (relative to `docs/`) for draft blog posts.
 *
 * @param {string} docsDir
 * @return {string[]}
 */
export function getDraftBlogPostExcludes(docsDir) {
  const blogDir = path.join(docsDir, "blog");
  if (!fs.existsSync(blogDir)) return [];

  return fs
    .readdirSync(blogDir)
    .filter((file) => file.endsWith(".md") && file !== "index.md")
    .sort()
    .filter((file) => isDraftBlogPostFile(path.join(blogDir, file)))
    .map((file) => `blog/${file}`);
}

/**
 * Scans `docs/blog/*.md` on disk and returns published posts sorted newest-first.
 *
 * @param {string} docsDir
 * @return {Array<any>}
 */
export function loadPublishedBlogPosts(docsDir) {
  const blogDir = path.join(docsDir, "blog");
  if (!fs.existsSync(blogDir)) return [];

  const posts = [];
  for (const file of fs.readdirSync(blogDir)) {
    if (!file.endsWith(".md") || file === "index.md") continue;
    const fullPath = path.join(blogDir, file);
    const raw = fs.readFileSync(fullPath, "utf-8");
    const { frontmatter, body } = parseBlogFrontmatter(raw);
    const slug = file.replace(/\.md$/i, "");
    const normalized = normalizeBlogPost({
      url: `/blog/${slug}.html`,
      slug,
      frontmatter,
      body,
    });
    if (normalized) {
      posts.push(normalized);
    }
  }

  return sortBlogPostsReverseChronological(posts);
}

/**
 * Builds VitePress sidebar entries for the Engineering Blog.
 *
 * @param {string} docsDir
 * @return {Array<{ text: string, link: string }>}
 */
export function getBlogSidebarItems(docsDir) {
  const published = loadPublishedBlogPosts(docsDir);
  return [
    { text: "All Engineering Posts", link: "/blog/" },
    ...published.map((post) => ({
      text: post.title,
      link: `/blog/${post.slug}`,
    })),
  ];
}

/**
 * Writes `feed.xml` into VitePress `siteConfig.outDir` during `buildEnd`.
 *
 * @param {{ srcDir: string, outDir: string, site?: { title?: string, description?: string, base?: string } }} siteConfig
 * @return {string} Path to generated `feed.xml`.
 */
export function writeRssFeed(siteConfig) {
  const posts = loadPublishedBlogPosts(siteConfig.srcDir);
  const xml = buildRssFeedXml(posts, {
    siteTitle: "Cadence Clinical Engineering Blog",
    siteDescription:
      "Curated architectural milestones and engineering updates from Cadence Clinical Research Software.",
    siteOrigin:
      siteConfig.siteOrigin || process.env.VITEPRESS_SITE_ORIGIN || SITE_ORIGIN,
    base: siteConfig.site?.base || "/cadence-clinical/",
  });

  fs.mkdirSync(siteConfig.outDir, { recursive: true });
  const feedPath = path.join(siteConfig.outDir, "feed.xml");
  fs.writeFileSync(feedPath, xml, "utf-8");
  return feedPath;
}
