import { defineConfig, devices } from "@playwright/test";

/**
 * The browser-to-database check of the observation correction (ADR-2620), run by
 * KernelObservationHostPostgresTests when CADENCE_TEST_BROWSER=1. The dev server's /api proxy reaches the test's
 * signing proxy on port 8000, which stands in for Keycloak and the gateway. PLAYWRIGHT_CHROMIUM_PATH points at a
 * preinstalled Chromium when the bundled one is not downloaded.
 */
export default defineConfig({
  testDir: "./tests/browser",
  workers: 1,
  retries: 0,
  outputDir:
    process.env.RECORDING_OUTPUT_DIR || "./test-results/correction-check",
  reporter: [["list"]],
  use: {
    baseURL: "http://localhost:3000/cadence-clinical/",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
        launchOptions: process.env.PLAYWRIGHT_CHROMIUM_PATH
          ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_PATH }
          : {},
      },
    },
  ],
  webServer: {
    command: "pnpm exec vite",
    url: "http://localhost:3000/cadence-clinical/tests/browser/observationHarness.ts",
    reuseExistingServer: false,
    timeout: 120000,
    env: { VITE_API_BASE_URL: "http://localhost:3000" },
  },
});
