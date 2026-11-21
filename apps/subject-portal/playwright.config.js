import { defineConfig, devices } from "@playwright/test";

const chromiumLaunchOptions = {
  args: [
    "--no-sandbox",
    "--disable-setuid-sandbox",
    "--disable-dev-shm-usage",
    "--disable-gpu",
    "--no-proxy-server",
  ],
};

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
  ],
});
