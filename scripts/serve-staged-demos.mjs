import fs from "fs";
import http from "http";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "..");

const MIME_TYPES = {
  ".css": "text/css; charset=utf-8",
  ".gif": "image/gif",
  ".html": "text/html; charset=utf-8",
  ".ico": "image/x-icon",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".js": "application/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".mjs": "application/javascript; charset=utf-8",
  ".png": "image/png",
  ".svg": "image/svg+xml",
  ".txt": "text/plain; charset=utf-8",
  ".wasm": "application/wasm",
  ".webmanifest": "application/manifest+json; charset=utf-8",
  ".webp": "image/webp",
  ".woff": "font/woff",
  ".woff2": "font/woff2",
  ".xml": "application/xml; charset=utf-8",
};

function parseCliPort(argv) {
  const idx = argv.indexOf("--port");
  if (idx >= 0 && argv[idx + 1]) {
    const parsed = Number.parseInt(argv[idx + 1], 10);
    if (Number.isFinite(parsed) && parsed > 0) return parsed;
  }
  return Number.parseInt(process.env.PORT || "4180", 10);
}

const port = parseCliPort(process.argv.slice(2));
const host = process.env.HOST || "127.0.0.1";
const mountBase = (process.env.VITEPRESS_BASE || "/cadence-clinical/").replace(
  /\/?$/,
  "/"
);
const distRoot = path.resolve(
  repoRoot,
  process.env.DOCS_DIST_DIR || "docs/.vitepress/dist"
);

if (!fs.existsSync(path.join(distRoot, "index.html"))) {
  console.error(
    `[serve-staged-demos] Staged output missing at ${distRoot}. Run 'pnpm docs:build && pnpm docs:stage-demos' first.`
  );
  process.exit(1);
}

function resolveCandidateFile(relativeRequestPath) {
  const safeRelative = relativeRequestPath.replace(/^\/+/, "");
  const candidate = path.resolve(distRoot, safeRelative);
  if (
    candidate !== distRoot &&
    !candidate.startsWith(`${distRoot}${path.sep}`)
  ) {
    return null;
  }

  if (fs.existsSync(candidate)) {
    const stat = fs.statSync(candidate);
    if (stat.isFile()) return candidate;
    if (stat.isDirectory()) {
      const indexFile = path.join(candidate, "index.html");
      if (fs.existsSync(indexFile) && fs.statSync(indexFile).isFile()) {
        return indexFile;
      }
    }
  }

  if (!path.extname(candidate)) {
    const htmlFallback = `${candidate}.html`;
    if (fs.existsSync(htmlFallback) && fs.statSync(htmlFallback).isFile()) {
      return htmlFallback;
    }
  }

  return null;
}

const server = http.createServer((req, res) => {
  const method = req.method || "GET";
  if (method !== "GET" && method !== "HEAD") {
    res.writeHead(405, { "Content-Type": "text/plain; charset=utf-8" });
    res.end("Method Not Allowed");
    return;
  }

  const parsedUrl = new URL(req.url || "/", `http://${host}:${port}`);
  let pathname = decodeURIComponent(parsedUrl.pathname);

  if (pathname === "/" || pathname === mountBase.slice(0, -1)) {
    res.writeHead(302, { Location: mountBase });
    res.end();
    return;
  }

  if (!pathname.startsWith(mountBase)) {
    res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
    res.end("Not Found");
    return;
  }

  const subPath = pathname.slice(mountBase.length);
  const filePath = resolveCandidateFile(subPath);
  if (!filePath) {
    res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
    res.end("Not Found");
    return;
  }

  const ext = path.extname(filePath).toLowerCase();
  const contentType = MIME_TYPES[ext] || "application/octet-stream";
  res.writeHead(200, {
    "Content-Type": contentType,
    "Cache-Control": "no-cache",
  });
  if (method === "HEAD") {
    res.end();
    return;
  }
  fs.createReadStream(filePath).pipe(res);
});

server.listen(port, host, () => {
  console.log(
    `[serve-staged-demos] Serving ${distRoot} at http://${host}:${port}${mountBase}`
  );
});
