import { defineConfig, devices } from "@playwright/test";

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
    launchOptions: {
      args: [
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-dev-shm-usage",
        "--disable-gpu",
        "--host-resolver-rules=MAP localhost 127.0.0.1",
      ],
    },
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
        launchOptions: {
          args: [
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--disable-dev-shm-usage",
            "--disable-gpu",
            "--host-resolver-rules=MAP localhost 127.0.0.1",
          ],
        },
      },
    },
  ],
  webServer: [
    {
      command:
        "VITE_API_BASE_URL=http://localhost:5174 pnpm --filter subject-portal exec vite build --base=/subject-portal/ --outDir node_modules/.e2e-prod && pnpm --filter subject-portal exec vite preview --outDir node_modules/.e2e-prod --host 127.0.0.1 --port 5174 --strictPort",
      url: "http://127.0.0.1:5174/subject-portal/",
      reuseExistingServer: false,
      timeout: 120000,
    },
    {
      command:
        "pnpm --filter subject-portal exec vite build --mode demo --base=/subject-portal/ --outDir node_modules/.e2e-demo && pnpm --filter subject-portal exec vite preview --outDir node_modules/.e2e-demo --host 127.0.0.1 --port 5175 --strictPort",
      url: "http://127.0.0.1:5175/subject-portal/",
      reuseExistingServer: false,
      timeout: 120000,
    },
  ],
});
