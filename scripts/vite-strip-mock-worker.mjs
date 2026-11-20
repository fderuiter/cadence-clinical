import fs from "fs";
import path from "path";

/**
 * Removes `dist/mockServiceWorker.js` from standard production bundles while
 * retaining it in demo builds (`VITE_DEMO_MODE=true` or `VITE_ENABLE_MSW=true`).
 *
 * @param {string} appDir - Absolute directory of the Vite app.
 * @returns {import("vite").Plugin}
 */
export function createStripMockWorkerInProdPlugin(appDir) {
  let shouldStripMsw = false;
  return {
    name: "strip-mock-worker-in-prod",
    apply: "build",
    configResolved(resolved) {
      shouldStripMsw =
        resolved.mode === "production" &&
        process.env.VITE_DEMO_MODE !== "true" &&
        process.env.VITE_ENABLE_MSW !== "true";
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
