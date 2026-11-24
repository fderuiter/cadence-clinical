import fs from "fs";
import path from "path";

const VIRTUAL_LIGHTWEIGHT_TLDTS_ID = "\0virtual:msw-lightweight-tldts";

const LIGHTWEIGHT_TLDTS_SOURCE = `
const MULTI_PART_SUFFIXES = new Set([
  "co.uk",
  "org.uk",
  "ac.uk",
  "gov.uk",
  "com.au",
  "co.jp",
  "co.nz",
  "github.io",
]);

export function getDomain(input) {
  if (typeof input !== "string" || !input) return null;
  const host = input.trim().toLowerCase().replace(/\\.+$/, "");
  if (!host || host.includes(":") || /^\\d+(?:\\.\\d+){3}$/.test(host)) {
    return null;
  }
  const labels = host.split(".");
  if (labels.length < 2 || labels.some((part) => !part)) {
    return null;
  }
  const tailTwo = \`\${labels[labels.length - 2]}.\${labels[labels.length - 1]}\`;
  if (MULTI_PART_SUFFIXES.has(tailTwo)) {
    return labels.length >= 3
      ? \`\${labels[labels.length - 3]}.\${tailTwo}\`
      : null;
  }
  return tailTwo;
}
`;

/**
 * Removes `dist/mockServiceWorker.js` from standard production bundles while
 * retaining it in demo builds (`VITE_DEMO_MODE=true` or `VITE_ENABLE_MSW=true`),
 * and replaces `tough-cookie`'s 304 KiB ICANN `tldts` trie with a lightweight
 * registrable-domain helper during static client builds.
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
    resolveId(source, importer) {
      if (
        source === "tldts" &&
        importer &&
        importer.replace(/\\/g, "/").includes("tough-cookie")
      ) {
        return VIRTUAL_LIGHTWEIGHT_TLDTS_ID;
      }
      return null;
    },
    load(id) {
      if (id === VIRTUAL_LIGHTWEIGHT_TLDTS_ID) {
        return LIGHTWEIGHT_TLDTS_SOURCE;
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
