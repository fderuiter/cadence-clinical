import fs from "fs";
import path from "path";

const REPO_URL = "https://github.com/fderuiter/cadence-clinical";
const BRANCH = "main";

/**
 * Markdown-it plugin: points relative links that the docs site cannot serve at the file on GitHub.
 *
 * The same Markdown is read on GitHub and on the VitePress site. On GitHub every relative link works; on the
 * site only pages under `docs/` exist. A relative link whose target is outside `docs/`, is not a Markdown page
 * (for example a `.json`, `.sh` or `.env.example` file), or is a directory without an `index.md` is rewritten
 * to its GitHub URL. Every other relative link is left for VitePress, which fails the build when it is dead.
 *
 * @param {import("markdown-it")} md
 * @param {{ docsDir: string, repoRoot: string }} options
 */
export function repoLinksPlugin(md, { docsDir, repoRoot }) {
  md.core.ruler.push("cadence_repo_links", (state) => {
    const relativePath = state.env?.relativePath;
    if (!relativePath) return;
    const pageDir = path.dirname(path.join(docsDir, relativePath));

    for (const block of state.tokens) {
      for (const token of block.children ?? []) {
        if (token.type !== "link_open") continue;
        const href = token.attrGet("href");
        const url = toRepoUrl(href, pageDir, docsDir, repoRoot);
        if (url) token.attrSet("href", url);
      }
    }
  });
}

function toRepoUrl(href, pageDir, docsDir, repoRoot) {
  if (!href || /^([a-z][a-z0-9+.-]*:|#|\/)/i.test(href)) return null;

  const [target, anchor = ""] = splitAnchor(href);
  const absolute = path.resolve(pageDir, decodeURI(target));
  const relToRepo = path.relative(repoRoot, absolute).replace(/\\/g, "/");
  if (relToRepo.startsWith("..")) return null;

  const insideDocs = !path.relative(docsDir, absolute).startsWith("..");
  const exists = fs.existsSync(absolute);
  const isDir = exists && fs.statSync(absolute).isDirectory();

  if (insideDocs) {
    if (isDir && !fs.existsSync(path.join(absolute, "index.md"))) {
      return `${REPO_URL}/tree/${BRANCH}/${relToRepo}`;
    }
    if (exists && !isDir && !/\.(md|html)$/i.test(absolute)) {
      return `${REPO_URL}/blob/${BRANCH}/${relToRepo}${anchor}`;
    }
    return null;
  }

  if (isDir) return `${REPO_URL}/tree/${BRANCH}/${relToRepo}`;
  if (!exists && fs.existsSync(`${absolute}.md`)) {
    return `${REPO_URL}/blob/${BRANCH}/${relToRepo}.md${anchor}`;
  }
  return `${REPO_URL}/blob/${BRANCH}/${relToRepo}${anchor}`;
}

function splitAnchor(href) {
  const index = href.indexOf("#");
  return index === -1 ? [href, ""] : [href.slice(0, index), href.slice(index)];
}

export const COHOSTED_SUBPATH_PREFIXES = ["/demo/", "/subject-portal/"];

export function isCohostedSubpathLink(
  link,
  prefixes = COHOSTED_SUBPATH_PREFIXES
) {
  if (!link || !link.startsWith("/")) return false;
  const cleanPath = splitAnchor(link)[0].split("?")[0];
  return prefixes.some((prefix) => {
    const normalizedPrefix = prefix.endsWith("/") ? prefix : `${prefix}/`;
    const withoutTrailing = normalizedPrefix.slice(0, -1);
    return (
      cleanPath === withoutTrailing || cleanPath.startsWith(normalizedPrefix)
    );
  });
}

/**
 * Throws when a nav or sidebar entry links to a page that does not exist under `docs/`,
 * except for declared co-hosted subpath applications staged into the portal bundle.
 *
 * @param {{ nav?: object[], sidebar?: object[] }} themeConfig
 * @param {string} docsDir
 * @param {{ cohostedSubpaths?: string[] }} [options]
 */
export function assertThemeLinksExist(themeConfig, docsDir, options = {}) {
  const cohostedSubpaths =
    options.cohostedSubpaths ?? COHOSTED_SUBPATH_PREFIXES;
  const missing = [];
  const visit = (items) => {
    for (const item of items ?? []) {
      const link = item.link;
      if (
        link &&
        link.startsWith("/") &&
        !isCohostedSubpathLink(link, cohostedSubpaths) &&
        !pageExists(docsDir, link)
      ) {
        missing.push(link);
      }
      visit(item.items);
    }
  };
  visit(themeConfig.nav);
  visit(themeConfig.sidebar);
  if (missing.length > 0) {
    throw new Error(
      `Docs nav or sidebar links to missing pages: ${missing.join(", ")}`
    );
  }
}

function pageExists(docsDir, link) {
  const page = splitAnchor(link)[0]
    .replace(/^\//, "")
    .replace(/\.(html|md)$/, "");
  if (page === "" || page.endsWith("/")) {
    return fs.existsSync(path.join(docsDir, page, "index.md"));
  }
  return fs.existsSync(path.join(docsDir, `${page}.md`));
}
