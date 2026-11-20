import fs from "fs";
import path from "path";

import rootDocsModule from "./rootDocs.cjs";

const { STAGED_ROOT_DOCS } = rootDocsModule;

const REPO_URL = "https://github.com/fderuiter/cadence-clinical";
const BRANCH = "main";

const CONTROL_CHARS_RE = /[\u0000-\u001f]/g;
const COMBINING_MARKS_RE = /[\u0300-\u036f]/g;
const SPECIAL_CHARS_RE = /[\s~`!@#$%^&*()\-_+=[\]{}|\\;:"'<>,.?/]+/g;
const HTML_ID_ATTR_RE = /\b(?:id|name)\s*=\s*["']([^"']+)["']/gi;

const anchorCache = new Map();

/**
 * Strips inline Markdown syntax and HTML tags from a heading line to compute its text.
 *
 * @param {string} rawHeading
 * @returns {string}
 */
export function stripInlineMarkdown(rawHeading) {
  return rawHeading
    .replace(/!\[([^\]]*)\]\([^)]+\)/g, "$1")
    .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
    .replace(/(`[^`]+`)|<[^>]+>/g, "$1")
    .replace(/`([^`]+)`/g, "$1")
    .replace(/\*\*([^*]+)\*\*/g, "$1")
    .replace(/__([^_]+)__/g, "$1")
    .replace(/\*([^*]+)\*/g, "$1")
    .replace(/&amp;/g, "&")
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .trim();
}

/**
 * Computes VitePress's default heading anchor slug (`@mdit-vue/shared` slugify).
 *
 * @param {string} text
 * @returns {string}
 */
export function vitepressSlugify(text) {
  return stripInlineMarkdown(text)
    .normalize("NFKD")
    .replace(COMBINING_MARKS_RE, "")
    .replace(CONTROL_CHARS_RE, "")
    .replace(SPECIAL_CHARS_RE, "-")
    .replace(/-{2,}/g, "-")
    .replace(/^-+|-+$/g, "")
    .replace(/^(\d)/, "_$1")
    .toLowerCase();
}

/**
 * Computes GitHub's heading anchor slug (letters, digits, hyphens and underscores kept; spaces to hyphens).
 *
 * @param {string} text
 * @returns {string}
 */
export function githubSlugify(text) {
  const cleaned = stripInlineMarkdown(text).toLowerCase();
  let slug = "";
  for (const ch of cleaned) {
    if (/[\p{L}\p{N}_-]/u.test(ch)) {
      slug += ch;
    } else if (/\s/.test(ch)) {
      slug += "-";
    }
  }
  return slug;
}

/**
 * Extracts all valid anchor IDs from a Markdown document, supporting both VitePress and GitHub
 * heading slugs (with duplicate `-1`, `-2` suffixes), `{#custom-id}` attributes, and HTML `id`/`name` attributes.
 *
 * @param {string} markdown
 * @returns {Set<string>}
 */
export function extractMarkdownAnchors(markdown) {
  const anchors = new Set();
  const vpCounts = new Map();
  const ghCounts = new Map();
  let fenceChar = "";
  let fenceLen = 0;

  for (const line of markdown.split(/\r?\n/)) {
    const trimmed = line.trimStart();
    const fenceMatch = trimmed.match(/^(`{3,}|~{3,})(.*)$/);
    if (fenceMatch) {
      const marker = fenceMatch[1];
      const rest = fenceMatch[2].trim();
      if (!fenceChar) {
        fenceChar = marker[0];
        fenceLen = marker.length;
        continue;
      }
      if (marker[0] === fenceChar && marker.length >= fenceLen && rest === "") {
        fenceChar = "";
        fenceLen = 0;
        continue;
      }
    }

    if (fenceChar) continue;

    for (const attrMatch of line.matchAll(HTML_ID_ATTR_RE)) {
      if (attrMatch[1]) {
        anchors.add(attrMatch[1]);
      }
    }

    const headingMatch = trimmed.match(/^(#{1,6})\s+(.+?)\s*$/);
    if (!headingMatch) continue;

    let headingText = headingMatch[2].replace(/\s+#+\s*$/, "");
    const customAttr = headingText.match(/\s*\{#([^}\s]+)\}\s*$/);
    if (customAttr) {
      anchors.add(customAttr[1]);
      headingText = headingText.slice(0, customAttr.index).trim();
    }

    const vpSlug = vitepressSlugify(headingText);
    if (vpSlug) {
      const vpSeen = vpCounts.get(vpSlug) ?? 0;
      vpCounts.set(vpSlug, vpSeen + 1);
      anchors.add(vpSeen === 0 ? vpSlug : `${vpSlug}-${vpSeen}`);
    }

    const ghSlug = githubSlugify(headingText);
    if (ghSlug) {
      const ghSeen = ghCounts.get(ghSlug) ?? 0;
      ghCounts.set(ghSlug, ghSeen + 1);
      anchors.add(ghSeen === 0 ? ghSlug : `${ghSlug}-${ghSeen}`);
    }
  }

  return anchors;
}

function getFileAnchors(filePath) {
  const stat = fs.statSync(filePath);
  const cached = anchorCache.get(filePath);
  if (cached && cached.mtimeMs === stat.mtimeMs) {
    return cached.anchors;
  }
  const content = fs.readFileSync(filePath, "utf-8");
  const anchors = extractMarkdownAnchors(content);
  anchorCache.set(filePath, { mtimeMs: stat.mtimeMs, anchors });
  return anchors;
}

function escapeHtmlAttr(value) {
  return value
    .replace(/&/g, "&amp;")
    .replace(/"/g, "&quot;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

/**
 * Markdown-it plugin: emits a secondary GitHub-compatible anchor `<span id="...">` inside headings
 * whenever the GitHub slug differs from VitePress's primary heading `id`, so links using numbered,
 * punctuated, or `schema.table_name` anchors resolve in both GitHub and the compiled portal.
 *
 * @param {import("markdown-it")} md
 */
export function dualHeadingAnchorsPlugin(md) {
  const defaultCodeInline =
    md.renderer.rules.code_inline ??
    ((tokens, idx, _options, _env, self) =>
      `<code${self.renderAttrs(tokens[idx])}>${md.utils.escapeHtml(tokens[idx].content)}</code>`);

  md.renderer.rules.code_inline = (tokens, idx, options, env, self) => {
    tokens[idx].attrSet("v-pre", "");
    return defaultCodeInline(tokens, idx, options, env, self);
  };

  md.core.ruler.push("cadence_dual_heading_anchors", (state) => {
    const existingIds = new Set();
    for (const token of state.tokens) {
      const tokenId = token.attrGet?.("id");
      if (tokenId) existingIds.add(tokenId);
      if (token.type === "html_block" && token.content) {
        for (const m of token.content.matchAll(HTML_ID_ATTR_RE)) {
          if (m[1]) existingIds.add(m[1]);
        }
      }
      for (const child of token.children ?? []) {
        const childId = child.attrGet?.("id");
        if (childId) existingIds.add(childId);
        if (child.type === "html_inline" && child.content) {
          for (const m of child.content.matchAll(HTML_ID_ATTR_RE)) {
            if (m[1]) existingIds.add(m[1]);
          }
        }
      }
    }

    const ghCounts = new Map();
    for (let i = 0; i < state.tokens.length; i++) {
      const token = state.tokens[i];
      if (token.type !== "heading_open") continue;

      const inline = state.tokens[i + 1];
      if (!inline || inline.type !== "inline" || !inline.children) continue;

      let rawTitle = "";
      for (const child of inline.children) {
        if (
          child.type === "link_open" &&
          (child.attrGet("class") ?? "").split(/\s+/).includes("header-anchor")
        ) {
          break;
        }
        if (child.type === "text" || child.type === "code_inline") {
          rawTitle += child.content;
        }
      }

      const baseGhSlug = githubSlugify(rawTitle);
      if (!baseGhSlug) continue;

      const seen = ghCounts.get(baseGhSlug) ?? 0;
      ghCounts.set(baseGhSlug, seen + 1);
      const ghSlug = seen === 0 ? baseGhSlug : `${baseGhSlug}-${seen}`;

      const vpSlug = token.attrGet("id") ?? "";
      if (ghSlug !== vpSlug && !existingIds.has(ghSlug)) {
        const aliasToken = new state.Token("html_inline", "", 0);
        aliasToken.content = `<span id="${escapeHtmlAttr(ghSlug)}" class="heading-alias-anchor" tabindex="-1"></span>`;
        inline.children.unshift(aliasToken);
        existingIds.add(ghSlug);
      }
    }
  });
}

/**
 * Markdown-it plugin: keeps staged root-guide links inside the VitePress portal, points non-portal
 * repository links at GitHub, and validates `#anchor` fragments on internal Markdown links.
 *
 * @param {import("markdown-it")} md
 * @param {{ docsDir: string, repoRoot: string }} options
 */
export function repoLinksPlugin(md, { docsDir, repoRoot }) {
  md.core.ruler.push("cadence_repo_links", (state) => {
    const relativePath = state.env?.relativePath;
    if (!relativePath) return;
    const currentFile = path.join(docsDir, relativePath);
    const pageDir = path.dirname(currentFile);
    let samePageAnchors = null;

    for (const block of state.tokens) {
      for (const token of block.children ?? []) {
        if (token.type !== "link_open") continue;
        const href = token.attrGet("href");
        if (!href) continue;

        const resolution = resolveLinkTarget(
          href,
          pageDir,
          docsDir,
          repoRoot,
          currentFile
        );
        if (resolution.rewrittenHref) {
          token.attrSet("href", resolution.rewrittenHref);
        }

        const fragment = decodeAnchorFragment(resolution.anchor);
        if (!fragment) continue;

        if (resolution.samePage) {
          if (!samePageAnchors) {
            samePageAnchors =
              typeof state.src === "string"
                ? extractMarkdownAnchors(state.src)
                : fs.existsSync(currentFile)
                  ? new Set(getFileAnchors(currentFile))
                  : new Set();
            for (const t of state.tokens) {
              const tid = t.attrGet?.("id");
              if (tid) samePageAnchors.add(tid);
              for (const c of t.children ?? []) {
                const cid = c.attrGet?.("id");
                if (cid) samePageAnchors.add(cid);
                if (c.type === "html_inline" && c.content) {
                  for (const m of c.content.matchAll(HTML_ID_ATTR_RE)) {
                    if (m[1]) samePageAnchors.add(m[1]);
                  }
                }
              }
            }
          }
          if (!samePageAnchors.has(fragment)) {
            throw new Error(
              `Broken heading anchor '${href}' in docs/${relativePath}`
            );
          }
        } else if (
          resolution.portalTargetFile &&
          fs.existsSync(resolution.portalTargetFile)
        ) {
          const targetAnchors = getFileAnchors(resolution.portalTargetFile);
          if (!targetAnchors.has(fragment)) {
            throw new Error(
              `Broken heading anchor '${href}' in docs/${relativePath} (target: docs/${path.relative(docsDir, resolution.portalTargetFile).replace(/\\/g, "/")})`
            );
          }
        }
      }
    }
  });
}

/**
 * Resolves a Markdown link href to either an internal portal rewrite, a GitHub fallback URL,
 * or an existing internal portal page for fragment validation.
 *
 * @param {string} href
 * @param {string} pageDir
 * @param {string} docsDir
 * @param {string} repoRoot
 * @param {string} [currentFile]
 */
export function toRepoUrl(href, pageDir, docsDir, repoRoot) {
  return resolveLinkTarget(href, pageDir, docsDir, repoRoot).rewrittenHref;
}

function resolveLinkTarget(href, pageDir, docsDir, repoRoot, currentFile = "") {
  if (!href) {
    return {
      rewrittenHref: null,
      portalTargetFile: null,
      anchor: "",
      samePage: false,
    };
  }

  if (href.startsWith("#")) {
    return {
      rewrittenHref: null,
      portalTargetFile: currentFile || null,
      anchor: href,
      samePage: true,
    };
  }

  if (/^([a-z][a-z0-9+.-]*:)/i.test(href)) {
    return {
      rewrittenHref: null,
      portalTargetFile: null,
      anchor: "",
      samePage: false,
    };
  }

  if (href.startsWith("/")) {
    if (isCohostedSubpathLink(href)) {
      return {
        rewrittenHref: null,
        portalTargetFile: null,
        anchor: "",
        samePage: false,
      };
    }
    const [, rawAnchor] = splitAnchor(href);
    return {
      rewrittenHref: null,
      portalTargetFile: resolveThemePagePath(docsDir, href),
      anchor: rawAnchor,
      samePage: false,
    };
  }

  const [rawTarget, anchor = ""] = splitAnchor(href);
  const cleanTarget = rawTarget.split("?")[0];
  if (!cleanTarget) {
    return {
      rewrittenHref: null,
      portalTargetFile: currentFile || null,
      anchor,
      samePage: true,
    };
  }

  const absolute = path.resolve(pageDir, decodeURI(cleanTarget));
  const relToRepo = path.relative(repoRoot, absolute).replace(/\\/g, "/");
  if (relToRepo.startsWith("..") || path.isAbsolute(relToRepo)) {
    return {
      rewrittenHref: null,
      portalTargetFile: null,
      anchor: "",
      samePage: false,
    };
  }

  const stagedDest = STAGED_ROOT_DOCS[relToRepo];
  if (stagedDest) {
    const stagedAbs = path.join(docsDir, stagedDest);
    if (fs.existsSync(stagedAbs)) {
      const relHref =
        path.relative(pageDir, stagedAbs).replace(/\\/g, "/") ||
        path.basename(stagedAbs);
      return {
        rewrittenHref: `${relHref}${anchor}`,
        portalTargetFile: stagedAbs,
        anchor,
        samePage: path.resolve(stagedAbs) === path.resolve(currentFile || ""),
      };
    }
  }

  const relToDocs = path.relative(docsDir, absolute);
  const insideDocs = !relToDocs.startsWith("..") && !path.isAbsolute(relToDocs);
  const exists = fs.existsSync(absolute);
  const isDir = exists && fs.statSync(absolute).isDirectory();

  if (insideDocs) {
    if (isDir && !fs.existsSync(path.join(absolute, "index.md"))) {
      return {
        rewrittenHref: `${REPO_URL}/tree/${BRANCH}/${relToRepo}`,
        portalTargetFile: null,
        anchor: "",
        samePage: false,
      };
    }
    if (exists && !isDir && !/\.(md|html)$/i.test(absolute)) {
      return {
        rewrittenHref: `${REPO_URL}/blob/${BRANCH}/${relToRepo}${anchor}`,
        portalTargetFile: null,
        anchor: "",
        samePage: false,
      };
    }

    let portalTargetFile = null;
    if (isDir) {
      portalTargetFile = path.join(absolute, "index.md");
    } else if (exists && /\.md$/i.test(absolute)) {
      portalTargetFile = absolute;
    } else if (!exists && fs.existsSync(`${absolute}.md`)) {
      portalTargetFile = `${absolute}.md`;
    } else if (
      exists &&
      /\.html$/i.test(absolute) &&
      fs.existsSync(absolute.replace(/\.html$/i, ".md"))
    ) {
      portalTargetFile = absolute.replace(/\.html$/i, ".md");
    }

    return {
      rewrittenHref: null,
      portalTargetFile,
      anchor,
      samePage:
        Boolean(portalTargetFile) &&
        path.resolve(portalTargetFile) === path.resolve(currentFile || ""),
    };
  }

  if (isDir) {
    return {
      rewrittenHref: `${REPO_URL}/tree/${BRANCH}/${relToRepo}`,
      portalTargetFile: null,
      anchor: "",
      samePage: false,
    };
  }
  if (!exists && fs.existsSync(`${absolute}.md`)) {
    return {
      rewrittenHref: `${REPO_URL}/blob/${BRANCH}/${relToRepo}.md${anchor}`,
      portalTargetFile: null,
      anchor: "",
      samePage: false,
    };
  }
  return {
    rewrittenHref: `${REPO_URL}/blob/${BRANCH}/${relToRepo}${anchor}`,
    portalTargetFile: null,
    anchor: "",
    samePage: false,
  };
}

export function splitAnchor(href) {
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

function decodeAnchorFragment(anchor) {
  if (!anchor || !anchor.startsWith("#")) return "";
  const raw = anchor.slice(1).trim();
  if (!raw) return "";
  try {
    return decodeURIComponent(raw);
  } catch {
    return raw;
  }
}

/**
 * Throws when a nav or sidebar entry links to a missing page or missing heading anchor under `docs/`,
 * except for declared co-hosted subpath applications staged into the portal bundle.
 *
 * @param {{ nav?: object[], sidebar?: object[] }} themeConfig
 * @param {string} docsDir
 * @param {{ cohostedSubpaths?: string[] }} [options]
 */
export function assertThemeLinksExist(themeConfig, docsDir, options = {}) {
  const cohostedSubpaths =
    options.cohostedSubpaths ?? COHOSTED_SUBPATH_PREFIXES;
  const missingPages = [];
  const missingAnchors = [];

  const visit = (items) => {
    for (const item of items ?? []) {
      const link = item.link;
      if (
        link &&
        link.startsWith("/") &&
        !isCohostedSubpathLink(link, cohostedSubpaths)
      ) {
        const filePath = resolveThemePagePath(docsDir, link);
        if (!filePath) {
          missingPages.push(link);
        } else {
          const [, rawAnchor] = splitAnchor(link);
          const fragment = decodeAnchorFragment(rawAnchor);
          if (fragment) {
            const anchors = getFileAnchors(filePath);
            if (!anchors.has(fragment)) {
              missingAnchors.push(link);
            }
          }
        }
      }
      visit(item.items);
    }
  };

  visit(themeConfig.nav);
  visit(themeConfig.sidebar);

  if (missingPages.length > 0 || missingAnchors.length > 0) {
    const details = [];
    if (missingPages.length > 0) {
      details.push(`missing pages: ${missingPages.join(", ")}`);
    }
    if (missingAnchors.length > 0) {
      details.push(`missing anchors: ${missingAnchors.join(", ")}`);
    }
    throw new Error(`Docs nav or sidebar links to ${details.join("; ")}`);
  }
}

function resolveThemePagePath(docsDir, link) {
  const page = splitAnchor(link)[0]
    .replace(/^\//, "")
    .replace(/\.(html|md)$/, "");
  if (page === "" || page.endsWith("/")) {
    const indexCandidate = path.join(docsDir, page, "index.md");
    return fs.existsSync(indexCandidate) ? indexCandidate : null;
  }
  const mdCandidate = path.join(docsDir, `${page}.md`);
  if (fs.existsSync(mdCandidate)) return mdCandidate;
  const dirIndexCandidate = path.join(docsDir, page, "index.md");
  return fs.existsSync(dirIndexCandidate) ? dirIndexCandidate : null;
}
