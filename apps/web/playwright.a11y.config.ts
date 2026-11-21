import { defineConfig, devices } from "@playwright/test";

/**
 * Standalone WCAG 2.1 AA Playwright accessibility verification for apps/web.
 * Runs in the parallel browser accessibility CI check without requiring
 * Docker Compose Keycloak or retired Python services.
 */
export default defineConfig({
  testDir: "./tests/e2e",
  testMatch: /web-accessibility\.spec\.ts/,
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  outputDir: process.env.RECORDING_OUTPUT_DIR || "./test-results/a11y",
  reporter: [["list"]],
  expect: {
    timeout: 15000,
  },
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
    command: "pnpm --filter web dev",
    url: "http://localhost:3000/cadence-clinical/",
    reuseExistingServer: !process.env.CI,
    timeout: 120000,
  },
});
