import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: false, // run sequentially to prevent race conditions or database locks
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  outputDir: process.env.RECORDING_OUTPUT_DIR || "./test-results",
  use: {
    baseURL: "http://localhost:3000/cadence-clinical/",
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
      testIgnore: /web-accessibility\.spec\.ts/,
      use: {
        ...devices["Desktop Chrome"],
        storageState: "playwright/.auth/user.json",
      },
      dependencies: ["setup"],
    },
    {
      name: "accessibility",
      testMatch: /web-accessibility\.spec\.ts/,
      use: {
        ...devices["Desktop Chrome"],
        baseURL: "http://localhost:3001/cadence-clinical/",
      },
    },
  ],
  webServer: [
    {
      command: "pnpm --filter web dev",
      url: "http://localhost:3000/cadence-clinical/",
      reuseExistingServer: !process.env.CI,
      timeout: 120000,
    },
    {
      command:
        "pnpm --filter web build:demo && pnpm --filter web exec vite preview --port 3001 --strictPort",
      url: "http://localhost:3001/cadence-clinical/",
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
