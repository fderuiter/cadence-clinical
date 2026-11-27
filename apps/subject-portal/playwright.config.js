import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium, defineConfig, devices } from "@playwright/test";
import { buildChromiumLaunchOptions } from "../../scripts/playwright-chromium-launcher.mjs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "../..");
const evidenceDir = process.env.CADENCE_TEST_EVIDENCE_DIR?.trim();

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

export default defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: false, // run sequentially to avoid race conditions or database locks
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  timeout: 60000,
  reporter: [
    ["html", { open: "never" }],
    [
      "junit",
      {
        outputFile: evidenceDir
          ? path.resolve(repoRoot, evidenceDir, "playwright-subject-portal.xml")
          : "playwright-report.xml",
      },
    ],
  ],
  use: {
    baseURL: "http://127.0.0.1:5174/subject-portal/",
    launchOptions: chromiumLaunchOptions,
    trace: "retain-on-failure",
    video: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
        channel: "chromium",
        launchOptions: chromiumLaunchOptions,
      },
    },
  ],
  webServer: [
    {
      command:
        "VITE_API_BASE_URL=http://127.0.0.1:5174 VITE_KEYCLOAK_URL=http://127.0.0.1:8080/ ./node_modules/.bin/vite build --base=/subject-portal/ --outDir node_modules/.e2e-prod && exec ./node_modules/.bin/vite preview --outDir node_modules/.e2e-prod --host 127.0.0.1 --port 5174 --strictPort",
      url: "http://127.0.0.1:5174/subject-portal/",
      reuseExistingServer: false,
      timeout: 120000,
    },
    {
      command:
        "VITE_API_BASE_URL=http://127.0.0.1:5175 VITE_KEYCLOAK_URL=http://127.0.0.1:8080/ ./node_modules/.bin/vite build --mode demo --base=/subject-portal/ --outDir node_modules/.e2e-demo && exec ./node_modules/.bin/vite preview --outDir node_modules/.e2e-demo --host 127.0.0.1 --port 5175 --strictPort",
      url: "http://127.0.0.1:5175/subject-portal/",
      reuseExistingServer: false,
      timeout: 120000,
    },
    {
      command:
        "VITE_BASE_PATH=/cadence-clinical/subject-portal/ ./node_modules/.bin/vite build --mode demo --outDir node_modules/.e2e-staged-subpath && exec env VITE_BASE_PATH=/cadence-clinical/subject-portal/ ./node_modules/.bin/vite preview --outDir node_modules/.e2e-staged-subpath --host 127.0.0.1 --port 5176 --strictPort",
      url: "http://127.0.0.1:5176/cadence-clinical/subject-portal/",
      reuseExistingServer: false,
      timeout: 120000,
    },
  ],
});
