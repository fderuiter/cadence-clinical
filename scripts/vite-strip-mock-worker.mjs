import fs from "fs";
import { createRequire } from "module";
import path from "path";

/**
 * Removes `dist/mockServiceWorker.js` from standard production bundles while
 * retaining it in demo builds (`VITE_DEMO_MODE=true` or `VITE_ENABLE_MSW=true`),
 * and resolves MSW's transitive `tldts` import to its pre-packed ESM entry so
 * Rolldown does not duplicate the suffix-trie string literal across loops.
 *
 * @param {string} appDir - Absolute directory of the Vite app.
 * @returns {import("vite").Plugin}
 */
export function createStripMockWorkerInProdPlugin(appDir) {
  let shouldStripMsw = false;
  let hasSlimDemoTldtsStub = false;
  return {
    name: "strip-mock-worker-in-prod",
    apply: "build",
    configResolved(resolved) {
      shouldStripMsw =
        resolved.mode === "production" &&
        process.env.VITE_DEMO_MODE !== "true" &&
        process.env.VITE_ENABLE_MSW !== "true";
      hasSlimDemoTldtsStub =
        resolved.mode === "demo" &&
        resolved.plugins.some((p) => p?.name === "slim-demo-msw-tldts");
    },
    resolveId(source, importer) {
      if (source !== "tldts" || !importer || importer.startsWith("\0")) {
        return null;
      }
      if (
        hasSlimDemoTldtsStub &&
        importer.replace(/\\/g, "/").includes("/tough-cookie/")
      ) {
        return null;
      }
      try {
        const cleanImporter = importer.split("?")[0];
        return createRequire(cleanImporter).resolve(
          "tldts/dist/index.esm.min.js"
        );
      } catch {
        return null;
      }
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
