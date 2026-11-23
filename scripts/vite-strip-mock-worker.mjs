import fs from "fs";
import path from "path";

const VIRTUAL_TLDTS_SHIM_ID = "\0virtual:tldts-browser-shim";

const TLDTS_BROWSER_SHIM_SOURCE = `
function extractHost(input) {
  if (typeof input !== "string" || !input) return "";
  let host = input.trim().toLowerCase();
  if (host.includes("://")) {
    try {
      host = new URL(host).hostname.toLowerCase();
    } catch {
      return "";
    }
  } else {
    const slashIdx = host.indexOf("/");
    if (slashIdx >= 0) host = host.slice(0, slashIdx);
  }
  return host.endsWith(".") ? host.slice(0, -1) : host;
}

export function getDomain(input) {
  const host = extractHost(input);
  if (!host || host.includes(":") || /^\\d+(?:\\.\\d+){3}$/.test(host)) {
    return null;
  }
  const parts = host.split(".");
  if (parts.length < 2 || parts.some((p) => !p)) {
    return null;
  }
  if (parts.length >= 2 && parts.slice(-2).join(".") === "github.io") {
    return parts.length >= 3 ? parts.slice(-3).join(".") : null;
  }
  return parts.slice(-2).join(".");
}
`;

/**
 * Removes `dist/mockServiceWorker.js` from standard production bundles while
 * retaining it in demo builds (`VITE_DEMO_MODE=true` or `VITE_ENABLE_MSW=true`),
 * and replaces `tough-cookie`'s 304 KiB `tldts` public-suffix trie in `msw/browser`
 * chunks with a compact registrable-domain helper for local and GitHub Pages origins.
 *
 * @param {string} appDir - Absolute directory of the Vite app.
 * @returns {import("vite").Plugin}
 */
export function createStripMockWorkerInProdPlugin(appDir) {
  let shouldStripMsw = false;
  return {
    name: "strip-mock-worker-in-prod",
    enforce: "pre",
    apply: "build",
    configResolved(resolved) {
      shouldStripMsw =
        resolved.mode === "production" &&
        process.env.VITE_DEMO_MODE !== "true" &&
        process.env.VITE_ENABLE_MSW !== "true";
    },
    resolveId(source) {
      if (source === "tldts") {
        return VIRTUAL_TLDTS_SHIM_ID;
      }
      return null;
    },
    load(id) {
      if (id === VIRTUAL_TLDTS_SHIM_ID) {
        return TLDTS_BROWSER_SHIM_SOURCE;
      }
      return null;
    },
    closeBundle() {
      if (!shouldStripMsw) return;
      const workerFile = path.resolve(appDir, "dist/mockServiceWorker.js");
      if (fs.existsSync(workerFile)) {
        fs.unlinkSync(workerFile);
      }
    },
  };
}
