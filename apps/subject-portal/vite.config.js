import { defineConfig, loadEnv } from "vite";
import vue from "@vitejs/plugin-vue";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { createStripMockWorkerInProdPlugin } from "../../scripts/vite-strip-mock-worker.mjs";
import {
  buildModeAwareCspContent,
  configureVitePreviewTimeouts,
  createSameOriginApiInDevPlugin,
  createThemeBootstrapPlugin,
  createViteGatewayProxy,
  injectHoistedCspMeta as injectHoistedPortalCspMeta,
  resolveMergedViteEnv,
  resolveViteOidcOrigin as resolvePortalOidcOrigin,
} from "../../scripts/vite-hoist-csp.mjs";
import {
  createConstantFoldDemoModePlugin,
  createDemoFixturesPlugin,
  createUiWorkspaceAliases,
} from "../../scripts/vite-strip-demo-fixtures.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const DEV_PORT = 5174;
// Where the dev server's /api proxy forwards: the API gateway (the AppHost passes its endpoint; docker compose names
// the front proxy).
const GATEWAY_URL = process.env.CADENCE_GATEWAY_URL || "http://localhost:8000";

export { injectHoistedPortalCspMeta, resolvePortalOidcOrigin };

export function buildSubjectPortalCspContent(options = {}) {
  return buildModeAwareCspContent(options);
}

export default defineConfig(({ mode }) => ({
  base: process.env.VITE_BASE_PATH || "/subject-portal/",
  cacheDir: mode === "demo" ? "node_modules/.vite-demo" : "node_modules/.vite",
  optimizeDeps: {
    entries: [
      "index.html",
      "src/**/*.js",
      "src/**/*.vue",
      "../../packages/ui/index.js",
      "../../packages/usdm-schemas/src/epro-validator.ts",
    ],
    include: [
      "vue",
      "vue-router",
      "pinia",
      "keycloak-js",
      "msw",
      "msw/browser",
    ],
  },
  plugins: [
    vue(),
    createThemeBootstrapPlugin(),
    createSameOriginApiInDevPlugin({
      rootDir: __dirname,
      devPort: DEV_PORT,
      loadEnv,
    }),
    (() => {
      let resolvedPortalCsp = buildSubjectPortalCspContent();
      return {
        name: "html-transform",
        configResolved(resolved) {
          resolvedPortalCsp = buildSubjectPortalCspContent({
            mode: resolved.mode,
            isProduction: Boolean(resolved.isProduction),
            env: resolveMergedViteEnv(resolved, __dirname, loadEnv),
          });
        },
        transformIndexHtml: {
          order: "post",
          handler(html) {
            const title =
              process.env.VITE_APP_TITLE || "Subject Portal - Cadence Clinical";
            const withTitle = html.replace(/%VITE_APP_TITLE%/g, title);
            return injectHoistedPortalCspMeta(withTitle, resolvedPortalCsp);
          },
        },
        configurePreviewServer: configureVitePreviewTimeouts,
      };
    })(),
    (() => {
      let isE2eProd = false;
      let resolvedOutDir = path.resolve(__dirname, "dist");
      const indexEntry = path.resolve(__dirname, "src/index.js");
      const syncQueueEntry = path.resolve(__dirname, "src/sync-queue.js");
      return {
        name: "e2e-module-entrypoints",
        apply: "build",
        configResolved(resolved) {
          const outDir = resolved.build?.outDir;
          resolvedOutDir = path.resolve(
            resolved.root || __dirname,
            outDir || "dist"
          );
          isE2eProd =
            outDir === "node_modules/.e2e-prod" ||
            outDir === "node_modules/.e2e-demo";
        },
        transform(code, id) {
          if (!isE2eProd) return null;
          const cleanId = id.split("?")[0];
          if (cleanId === indexEntry) {
            return {
              code: `${code}\nglobalThis.__CADENCE_E2E_INDEX__ = { state, refreshSubmissionsState, syncOfflineQueue, checkPINWrapper };\nconst __cadenceE2eBase = "/subject-portal/src";\nvoid import(/* @vite-ignore */ \`\${__cadenceE2eBase}/index.js\`).catch(() => {});\nvoid import(/* @vite-ignore */ \`\${__cadenceE2eBase}/sync-queue.js\`).catch(() => {});\n`,
              map: null,
            };
          }
          if (cleanId === syncQueueEntry) {
            return {
              code: `${code}\nglobalThis.__CADENCE_E2E_SYNC_QUEUE__ = { queueSubmission, clearSessionKey };\n`,
              map: null,
            };
          }
          return null;
        },
        generateBundle() {
          if (!isE2eProd) return;
          this.emitFile({
            type: "asset",
            fileName: "src/index.js",
            source:
              "const m = globalThis.__CADENCE_E2E_INDEX__;\nexport const { state, refreshSubmissionsState, syncOfflineQueue, checkPINWrapper } = m;\n",
          });
          this.emitFile({
            type: "asset",
            fileName: "src/sync-queue.js",
            source:
              "const m = globalThis.__CADENCE_E2E_SYNC_QUEUE__;\nexport const { queueSubmission, clearSessionKey } = m;\n",
          });
        },
        writeBundle(_options, bundle) {
          const swOutputPath = path.join(resolvedOutDir, "sw.js");
          if (!fs.existsSync(swOutputPath)) return;

          const emittedBundlePaths = Object.keys(bundle)
            .map((fileName) => fileName.replace(/^\/+/, ""))
            .filter(
              (fileName) =>
                Boolean(fileName) &&
                !fileName.endsWith(".map") &&
                fileName !== "sw.js" &&
                fileName !== "index.html" &&
                fileName !== "manifest.json" &&
                !fileName.endsWith("mockServiceWorker.js")
            )
            .sort();

          const injectedEntries = emittedBundlePaths
            .map((relPath) => `  \`\${SW_BASE}${relPath}\`,`)
            .join("\n");
          const replacement =
            emittedBundlePaths.length > 0
              ? `const INJECTED_BUNDLE_ASSETS = [\n${injectedEntries}\n];`
              : "const INJECTED_BUNDLE_ASSETS = [];";

          const currentSwSource = fs.readFileSync(swOutputPath, "utf8");
          const updatedSwSource = currentSwSource.replace(
            /const INJECTED_BUNDLE_ASSETS = \[\];/,
            replacement
          );
          fs.writeFileSync(swOutputPath, updatedSwSource, "utf8");
        },
      };
    })(),
    createStripMockWorkerInProdPlugin(__dirname),
    createDemoFixturesPlugin(__dirname),
    createConstantFoldDemoModePlugin(__dirname),
  ],
  server: {
    port: DEV_PORT,
    strictPort: true,
    proxy: createViteGatewayProxy(GATEWAY_URL),
  },
  preview: {
    host: "::",
  },
  resolve: {
    alias: createUiWorkspaceAliases(__dirname),
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
  },
  test: {
    environment: "jsdom",
    globals: true,
    testTimeout: 15000,
    exclude: ["**/e2e/**", "**/docs-portal/**", "node_modules/**", "dist/**"],
    setupFiles: [path.resolve(__dirname, "./tests/setup.js")],
    ...(process.env.CADENCE_TEST_EVIDENCE_DIR?.trim()
      ? {
          reporters: ["default", "junit"],
          outputFile: {
            junit: path.resolve(
              __dirname,
              "../..",
              process.env.CADENCE_TEST_EVIDENCE_DIR.trim(),
              "vitest-subject-portal.xml"
            ),
          },
        }
      : {}),
    coverage: {
      provider: "v8",
      reporter: ["text", "json", "html"],
      // Every source file counts (ADR-2530); the mock service worker is not hand-written code.
      include: ["src/**/*.{ts,js,vue}"],
      exclude: ["src/mocks/**"],
      thresholds: {
        // A ratchet floor under the whole portal: raise it when coverage rises, never lower it.
        lines: 77,
        statements: 76,
        functions: 75,
        branches: 61,
        // The file ADR-251 put under its 80 percent gate keeps it.
        "src/sync-queue.js": {
          lines: 80,
          functions: 80,
          branches: 80,
          statements: 80,
        },
      },
    },
  },
}));
