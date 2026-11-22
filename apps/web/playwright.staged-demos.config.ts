import os from "os";
import path from "path";
import { chromium, defineConfig, devices } from "@playwright/test";
import { buildChromiumLaunchOptions } from "../../scripts/playwright-chromium-launcher.mjs";

const stagedPort = Number.parseInt(process.env.STAGED_DEMO_PORT || "4180", 10);
const stagedBaseUrl = `http://127.0.0.1:${stagedPort}/cadence-clinical/`;

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
  testDir: "./tests/e2e-staged",
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  timeout: 180000,
  expect: {
    timeout: 20000,
  },
  // Write test results outside docs/.vitepress/dist so staged deployment artifacts stay clean
  outputDir: path.join(os.tmpdir(), "cadence-staged-demo-playwright"),
  use: {
    ...devices["Desktop Chrome"],
    channel: "chromium",
    baseURL: stagedBaseUrl,
    serviceWorkers: "allow",
    launchOptions: chromiumLaunchOptions,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  webServer: {
    command: `node ../../scripts/serve-staged-demos.mjs --port ${stagedPort}`,
    url: stagedBaseUrl,
    reuseExistingServer: !process.env.CI,
    timeout: 60000,
  },
});
