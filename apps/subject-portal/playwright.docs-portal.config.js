import path from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig } from "@playwright/test";
import baseConfig from "./playwright.config.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "../..");
const evidenceDir = process.env.CADENCE_TEST_EVIDENCE_DIR?.trim();
const docsPortalUrl = "http://127.0.0.1:4173/cadence-clinical/";

export default defineConfig({
  ...baseConfig,
  testDir: "./tests/docs-portal",
  timeout: 90000,
  fullyParallel: true,
  workers: 2,
  reporter: [
    ["html", { open: "never" }],
    [
      "junit",
      {
        outputFile: evidenceDir
          ? path.resolve(repoRoot, evidenceDir, "playwright-docs-portal.xml")
          : "playwright-report-docs-portal.xml",
      },
    ],
  ],
  use: {
    ...baseConfig.use,
    baseURL: docsPortalUrl,
  },
  webServer: [
    {
      command: "node ../../scripts/serve-docs-dist.mjs --port 4173",
      url: docsPortalUrl,
      reuseExistingServer: !process.env.CI,
      timeout: 30000,
    },
  ],
});
