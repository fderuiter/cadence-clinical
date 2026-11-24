import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "..");
const distDir = path.join(repoRoot, "docs", ".vitepress", "dist");
const basePrefix = "/cadence-clinical";

const MIME_TYPES = {
  ".css": "text/css; charset=utf-8",
  ".gif": "image/gif",
  ".html": "text/html; charset=utf-8",
  ".ico": "image/x-icon",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".mjs": "text/javascript; charset=utf-8",
  ".png": "image/png",
  ".svg": "image/svg+xml",
  ".ttf": "font/ttf",
  ".txt": "text/plain; charset=utf-8",
  ".wasm": "application/wasm",
  ".webp": "image/webp",
  ".woff": "font/woff",
  ".woff2": "font/woff2",
  ".xml": "application/xml; charset=utf-8",
};

function parsePort(argv) {
  const idx = argv.indexOf("--port");
  if (idx >= 0 && argv[idx + 1]) {
    const parsed = Number.parseInt(argv[idx + 1], 10);
    if (Number.isFinite(parsed) && parsed > 0) {
      return parsed;
    }
  }
  return 4173;
}

if (!fs.existsSync(path.join(distDir, "index.html"))) {
  console.error(
    `Missing compiled documentation portal at ${distDir}. Run 'pnpm docs:build' before serving.`
  );
  process.exit(1);
}

function safeResolveInDist(relativePathname) {
  const decoded = decodeURIComponent(relativePathname);
  const resolved = path.resolve(distDir, `.${decoded}`);
  if (resolved !== distDir && !resolved.startsWith(`${distDir}${path.sep}`)) {
    return null;
  }
  return resolved;
}

const port = parsePort(process.argv.slice(2));

const server = http.createServer((req, res) => {
  const rawUrl = req.url || "/";
  const pathname = rawUrl.split(/[?#]/)[0] || "/";

  if (pathname === "/" || pathname === basePrefix) {
    res.writeHead(302, { Location: `${basePrefix}/` });
    res.end();
    return;
  }

  if (!pathname.startsWith(`${basePrefix}/`)) {
    res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
    res.end("Not Found");
    return;
  }

  const subPath = pathname.slice(basePrefix.length);
  const candidate = safeResolveInDist(subPath);
  if (!candidate) {
    res.writeHead(400, { "Content-Type": "text/plain; charset=utf-8" });
    res.end("Bad Request");
    return;
  }

  let filePath = null;
  if (subPath.endsWith("/")) {
    const indexHtml = path.join(candidate, "index.html");
    if (fs.existsSync(indexHtml) && fs.statSync(indexHtml).isFile()) {
      filePath = indexHtml;
    }
  } else if (fs.existsSync(candidate) && fs.statSync(candidate).isFile()) {
    filePath = candidate;
  } else if (
    fs.existsSync(`${candidate}.html`) &&
    fs.statSync(`${candidate}.html`).isFile()
  ) {
    filePath = `${candidate}.html`;
  } else if (
    fs.existsSync(path.join(candidate, "index.html")) &&
    fs.statSync(path.join(candidate, "index.html")).isFile()
  ) {
    res.writeHead(302, { Location: `${pathname}/` });
    res.end();
    return;
  }

  if (!filePath) {
    const notFoundHtml = path.join(distDir, "404.html");
    if (fs.existsSync(notFoundHtml)) {
      res.writeHead(404, { "Content-Type": "text/html; charset=utf-8" });
      fs.createReadStream(notFoundHtml).pipe(res);
      return;
    }
    res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
    res.end("Not Found");
    return;
  }

  const ext = path.extname(filePath).toLowerCase();
  const contentType = MIME_TYPES[ext] || "application/octet-stream";
  res.writeHead(200, { "Content-Type": contentType });
  if (req.method === "HEAD") {
    res.end();
    return;
  }
  fs.createReadStream(filePath).pipe(res);
});

server.keepAliveTimeout = 65000;
server.headersTimeout = 66000;

server.listen(port, "127.0.0.1", () => {
  console.log(
    `Serving docs/.vitepress/dist at http://127.0.0.1:${port}${basePrefix}/`
  );
});
