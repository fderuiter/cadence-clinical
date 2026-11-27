import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium, defineConfig, devices } from "@playwright/test";
import { buildChromiumLaunchOptions } from "../../scripts/playwright-chromium-launcher.mjs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "../..");
const evidenceDir = process.env.CADENCE_TEST_EVIDENCE_DIR?.trim();

function getRequestedProjects(argv: readonly string[]): string[] {
  const projects: string[] = [];
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--project" && i + 1 < argv.length) {
      projects.push(argv[i + 1]);
      i++;
    } else if (arg.startsWith("--project=")) {
      projects.push(arg.slice("--project=".length));
    }
  }
  return projects;
}

const useLiveKeycloak = process.env.E2E_KEYCLOAK === "true";
const demoPreviewUrl = "http://127.0.0.1:3001/cadence-clinical/";
const stagedDemoPreviewUrl = "http://127.0.0.1:3002/cadence-clinical/demo/";
const webBaseUrl = useLiveKeycloak
  ? "http://localhost:3000/cadence-clinical/"
  : demoPreviewUrl;
const requestedProjects = getRequestedProjects(process.argv);
const junitSlug =
  requestedProjects.length > 0
    ? requestedProjects.map((p) => p.replace(/[^a-zA-Z0-9_-]/g, "-")).join("-")
    : "all";
const isStandalonePreviewOnly =
  requestedProjects.length > 0 &&
  requestedProjects.every(
    (project) =>
      project === "accessibility" || project === "staged-demo-subpath"
  );
const wantsAccessibilityServer =
  requestedProjects.length === 0 || requestedProjects.includes("accessibility");
const wantsStagedDemoServer =
  requestedProjects.length === 0 ||
  requestedProjects.includes("staged-demo-subpath");
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
  testMatch: /(?:web-accessibility|drawer-reflow)\.spec\.ts/,
  use: {
    ...devices["Desktop Chrome"],
    channel: "chromium",
    baseURL: demoPreviewUrl,
    serviceWorkers: "block" as const,
    launchOptions: chromiumLaunchOptions,
  },
};

const stagedDemoSubpathProject = {
  name: "staged-demo-subpath",
  testMatch: /staged-demo-subpath\.spec\.ts/,
  use: {
    ...devices["Desktop Chrome"],
    channel: "chromium",
    baseURL: stagedDemoPreviewUrl,
    serviceWorkers: "allow" as const,
    launchOptions: chromiumLaunchOptions,
  },
};

const demoPreviewServer = {
  command:
    "./node_modules/.bin/vite build --mode demo --outDir node_modules/.e2e-demo && exec ./node_modules/.bin/vite preview --outDir node_modules/.e2e-demo --host 127.0.0.1 --port 3001 --strictPort",
  url: demoPreviewUrl,
  reuseExistingServer: false,
  timeout: 120000,
  env: {
    CADENCE_E2E_BUILD: "1",
    VITE_E2E_IN_PROCESS_MSW: "true",
  },
};

const stagedDemoPreviewServer = {
  command:
    "./node_modules/.bin/vite build --mode demo --outDir node_modules/.e2e-staged-demo && exec ./node_modules/.bin/vite preview --outDir node_modules/.e2e-staged-demo --host 127.0.0.1 --port 3002 --strictPort",
  url: stagedDemoPreviewUrl,
  reuseExistingServer: false,
  timeout: 120000,
  env: {
    VITE_BASE_PATH: "/cadence-clinical/demo/",
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
  ...(evidenceDir
    ? {
        reporter: [
          ["list"],
          [
            "junit",
            {
              outputFile: path.resolve(
                repoRoot,
                evidenceDir,
                `playwright-web-${junitSlug}.xml`
              ),
            },
          ],
        ],
      }
    : {}),
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
  projects: [
    {
      name: "setup",
      testMatch: /auth\.setup\.ts/,
    },
    {
      name: "chromium",
      testIgnore:
        /(?:web-accessibility|drawer-reflow|staged-demo-subpath)\.spec\.ts/,
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
    stagedDemoSubpathProject,
  ],
  webServer: isStandalonePreviewOnly
    ? [
        ...(wantsAccessibilityServer ? [demoPreviewServer] : []),
        ...(wantsStagedDemoServer ? [stagedDemoPreviewServer] : []),
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
        demoPreviewServer,
        ...(wantsStagedDemoServer ? [stagedDemoPreviewServer] : []),
        {
          command:
            "pnpm --filter subject-portal exec vite --mode demo --host 127.0.0.1 --port 5174 --strictPort",
          url: "http://127.0.0.1:5174/subject-portal/",
          reuseExistingServer: false,
          timeout: 120000,
          env: {
            VITE_API_BASE_URL: "http://127.0.0.1:5174",
            VITE_KEYCLOAK_URL: "http://127.0.0.1:8080/",
          },
        },
      ],
});
