import { defineConfig, devices } from "@playwright/test";

const useLiveKeycloak = process.env.E2E_KEYCLOAK === "true";

export default defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: false, // run sequentially to prevent race conditions or database locks
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  outputDir: process.env.RECORDING_OUTPUT_DIR || "./test-results",
  use: {
    baseURL: "http://localhost:3000/cadence-clinical/",
    launchOptions: {
      args: [
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-dev-shm-usage",
        "--disable-gpu",
      ],
    },
    trace: "retain-on-failure",
    video:
      process.env.RECORD_VIDEO === "true"
        ? { mode: "on", size: { width: 1920, height: 1080 } }
        : "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "setup",
      testMatch: /auth\.setup\.ts/,
    },
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
        launchOptions: {
          args: [
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--disable-dev-shm-usage",
            "--disable-gpu",
          ],
        },
        storageState: "playwright/.auth/user.json",
      },
      dependencies: ["setup"],
    },
  ],
  webServer: [
    {
      command: useLiveKeycloak
        ? "pnpm --filter web dev"
        : "pnpm --filter web exec vite --mode demo",
      url: "http://localhost:3000/cadence-clinical/",
      reuseExistingServer: !process.env.CI,
      timeout: 120000,
    },
    {
      command: "pnpm --filter subject-portal dev",
      url: "http://localhost:5174/subject-portal/",
      reuseExistingServer: !process.env.CI,
      timeout: 120000,
    },
  ],
});
