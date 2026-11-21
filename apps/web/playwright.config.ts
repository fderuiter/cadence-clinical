import { chromium, defineConfig, devices } from "@playwright/test";
import { buildChromiumLaunchOptions } from "../../scripts/playwright-chromium-launcher.mjs";

const useLiveKeycloak = process.env.E2E_KEYCLOAK === "true";
const demoPreviewUrl = "http://127.0.0.1:3001/cadence-clinical/";
const webBaseUrl = useLiveKeycloak
  ? "http://localhost:3000/cadence-clinical/"
  : demoPreviewUrl;
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
const chromiumLaunchOptions = buildChromiumLaunchOptions(chromium, {
  ...(process.env.PLAYWRIGHT_CHROME_EXECUTABLE
    ? { executablePath: process.env.PLAYWRIGHT_CHROME_EXECUTABLE }
    : {}),
  args: [
    "--no-sandbox",
    "--disable-setuid-sandbox",
    "--disable-dev-shm-usage",
    "--disable-gpu",
    "--no-proxy-server",
  ],
});

const accessibilityProject = {
  name: "accessibility",
  testMatch: /web-accessibility\.spec\.ts/,
  use: {
    ...devices["Desktop Chrome"],
    channel: "chromium",
    baseURL: demoPreviewUrl,
    serviceWorkers: "block" as const,
    launchOptions: chromiumLaunchOptions,
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
    baseURL: webBaseUrl,
    serviceWorkers: useLiveKeycloak ? "allow" : "block",
    launchOptions: chromiumLaunchOptions,
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
            channel: "chromium",
            serviceWorkers: useLiveKeycloak ? "allow" : "block",
            storageState: "playwright/.auth/user.json",
            launchOptions: chromiumLaunchOptions,
          },
          dependencies: ["setup"],
        },
        accessibilityProject,
      ],
  webServer: isAccessibilityOnly
    ? [
        {
          command:
            "CADENCE_E2E_BUILD=1 VITE_E2E_IN_PROCESS_MSW=true ./node_modules/.bin/vite build --mode demo --outDir node_modules/.e2e-demo && exec env CADENCE_E2E_BUILD=1 ./node_modules/.bin/vite preview --outDir node_modules/.e2e-demo --host 127.0.0.1 --port 3001 --strictPort",
          url: demoPreviewUrl,
          reuseExistingServer: false,
          timeout: 120000,
        },
      ]
    : [
        ...(useLiveKeycloak
          ? [
              {
                command: "pnpm --filter web dev",
                url: "http://localhost:3000/cadence-clinical/",
                reuseExistingServer: !process.env.CI,
                timeout: 120000,
              },
            ]
          : []),
        {
          command:
            "CADENCE_E2E_BUILD=1 VITE_E2E_IN_PROCESS_MSW=true ./node_modules/.bin/vite build --mode demo --outDir node_modules/.e2e-demo && exec env CADENCE_E2E_BUILD=1 ./node_modules/.bin/vite preview --outDir node_modules/.e2e-demo --host 127.0.0.1 --port 3001 --strictPort",
          url: demoPreviewUrl,
          reuseExistingServer: false,
          timeout: 120000,
        },
        {
          command:
            "VITE_KEYCLOAK_URL=http://127.0.0.1:8080/ pnpm --filter subject-portal exec vite --host 127.0.0.1 --port 5174 --strictPort",
          url: "http://127.0.0.1:5174/subject-portal/",
          reuseExistingServer: !process.env.CI,
          timeout: 120000,
        },
      ],
});
