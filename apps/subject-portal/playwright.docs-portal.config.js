import { defineConfig } from "@playwright/test";
import baseConfig from "./playwright.config.js";

const docsPortalUrl = "http://127.0.0.1:4173/cadence-clinical/";

export default defineConfig({
  ...baseConfig,
  testDir: "./tests/docs-portal",
  timeout: 90000,
  fullyParallel: true,
  workers: 2,
  reporter: [
    ["html", { open: "never" }],
    ["junit", { outputFile: "playwright-report-docs-portal.xml" }],
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
