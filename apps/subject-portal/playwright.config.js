import { chromium, defineConfig, devices } from "@playwright/test";
import { buildChromiumLaunchOptions } from "../../scripts/playwright-chromium-launcher.mjs";

export default defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: false, // run sequentially to avoid race conditions or database locks
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  reporter: [
    ["html", { open: "never" }],
    ["junit", { outputFile: "playwright-report.xml" }],
  ],
  use: {
    baseURL: "http://localhost:5174/subject-portal/",
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
        launchOptions: buildChromiumLaunchOptions(chromium, {
          args: [
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--disable-dev-shm-usage",
          ],
        }),
      },
    },
  ],
  webServer: [
    {
      command:
        "VITE_API_BASE_URL=http://localhost:5174 pnpm --filter subject-portal exec vite build --base=/subject-portal/ --outDir node_modules/.e2e-prod && pnpm --filter subject-portal exec vite preview --outDir node_modules/.e2e-prod --port 5174 --strictPort",
      url: "http://localhost:5174/subject-portal/",
      reuseExistingServer: false,
      timeout: 120000,
    },
    {
      command:
        "pnpm --filter subject-portal exec vite build --mode demo --base=/subject-portal/ --outDir node_modules/.e2e-demo && pnpm --filter subject-portal exec vite preview --outDir node_modules/.e2e-demo --port 5175 --strictPort",
      url: "http://localhost:5175/subject-portal/",
      reuseExistingServer: false,
      timeout: 120000,
    },
  ],
});
