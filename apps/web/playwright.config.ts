import { defineConfig, devices } from "@playwright/test";

const hasExplicitFullStackTarget = process.argv.some(
  (arg) =>
    arg.includes("chromium") ||
    arg.includes("setup") ||
    arg.includes("clinical_workflows") ||
    arg.includes("crf_authoring") ||
    arg.includes("etmf_workflow") ||
    arg.includes("suites")
);

const isAccessibilityOnly =
  process.env.CADENCE_TEST_BROWSER === "1" ||
  process.argv.some((arg) => arg.includes("accessibility")) ||
  (process.env.E2E_KEYCLOAK !== "true" &&
    process.env.RECORD_VIDEO !== "true" &&
    !hasExplicitFullStackTarget);

const accessibilityProject = {
  name: "accessibility",
  testMatch: /web-accessibility\.spec\.ts/,
  use: {
    ...devices["Desktop Chrome"],
    baseURL: "http://127.0.0.1:3001/cadence-clinical/",
    launchOptions: {
      ...(process.env.PLAYWRIGHT_CHROMIUM_PATH
        ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_PATH }
        : {}),
      args: [
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-dev-shm-usage",
        "--disable-gpu",
      ],
    },
  },
};

export default defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: false, // run sequentially to prevent race conditions or database locks
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  expect: {
    timeout: 15000,
  },
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
  projects: isAccessibilityOnly
    ? [accessibilityProject]
    : [
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
        accessibilityProject,
      ],
  webServer: isAccessibilityOnly
    ? [
        {
          command:
            "./node_modules/.bin/vite build --mode demo --outDir node_modules/.e2e-demo && exec ./node_modules/.bin/vite preview --outDir node_modules/.e2e-demo --host 127.0.0.1 --port 3001 --strictPort",
          url: "http://127.0.0.1:3001/cadence-clinical/",
          reuseExistingServer: false,
          timeout: 120000,
        },
      ]
    : [
        {
          command: "pnpm --filter web dev",
          url: "http://localhost:3000/cadence-clinical/",
          reuseExistingServer: !process.env.CI,
          timeout: 120000,
        },
        {
          command:
            "./node_modules/.bin/vite build --mode demo --outDir node_modules/.e2e-demo && exec ./node_modules/.bin/vite preview --outDir node_modules/.e2e-demo --host 127.0.0.1 --port 3001 --strictPort",
          url: "http://127.0.0.1:3001/cadence-clinical/",
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
