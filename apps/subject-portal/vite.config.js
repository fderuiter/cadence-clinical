import { defineConfig, loadEnv } from "vite";
import vue from "@vitejs/plugin-vue";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { createStripMockWorkerInProdPlugin } from "../../scripts/vite-strip-mock-worker.mjs";
import {
  buildModeAwareCspContent,
  injectHoistedCspMeta as injectHoistedPortalCspMeta,
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
    {
      // The gateway sends no CORS headers, so a dev server page that called http://localhost:8000 directly would be
      // blocked by the browser. Unless VITE_API_BASE_URL is set (environment or .env files), the dev server sends API
      // calls to its own origin, and the /api proxy below forwards them to the gateway (#5451, as #5429 did for the web
      // app). Builds and tests are unchanged.
      name: "same-origin-api-in-dev",
      config(_config, { command, mode }) {
        if (
          command === "serve" &&
          mode === "development" &&
          !loadEnv(mode, __dirname, "VITE_API_BASE_URL").VITE_API_BASE_URL
        ) {
          process.env.VITE_API_BASE_URL = `http://localhost:${DEV_PORT}`;
        }
      },
    },
    (() => {
      let resolvedPortalCsp = buildSubjectPortalCspContent();
      return {
        name: "html-transform",
        configResolved(resolved) {
          const fileEnv = loadEnv(resolved.mode, __dirname, "VITE_");
          const mergedEnv = { ...fileEnv, ...process.env };
          resolvedPortalCsp = buildSubjectPortalCspContent({
            mode: resolved.mode,
            isProduction: Boolean(resolved.isProduction),
            env: mergedEnv,
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
        configurePreviewServer(server) {
          if (server.httpServer) {
            server.httpServer.keepAliveTimeout = 65000;
            server.httpServer.headersTimeout = 66000;
          }
        },
      };
    })(),
    (() => {
      let isE2eProd = false;
      let resolvedOutDir = path.resolve(__dirname, "dist");
      const htmlEntry = path.resolve(__dirname, "index.html");
      const mainEntry = path.resolve(__dirname, "src/main.js");
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
        options(inputOptions) {
          if (!isE2eProd) return null;
          return {
            ...inputOptions,
            preserveEntrySignatures: "allow-extension",
            input: {
              index: htmlEntry,
              "e2e-index": indexEntry,
              "e2e-sync-queue": syncQueueEntry,
            },
          };
        },
        transform(code, id) {
          if (!isE2eProd) return null;
          const cleanId = id.split("?")[0];
          if (cleanId === mainEntry) {
            return {
              code: `${code}\nconst __cadenceE2eBase = "/subject-portal/src";\nvoid import(/* @vite-ignore */ \`\${__cadenceE2eBase}/index.js\`).catch(() => {});\nvoid import(/* @vite-ignore */ \`\${__cadenceE2eBase}/sync-queue.js\`).catch(() => {});\n`,
              map: null,
            };
          }
          return null;
        },
        generateBundle(_outputOptions, bundle) {
          if (!isE2eProd) return;
          const chunks = Object.values(bundle).filter(
            (item) => item && item.type === "chunk"
          );
          const e2eIndexChunk = chunks.find(
            (chunk) =>
              chunk.name === "e2e-index" || chunk.facadeModuleId === indexEntry
          );
          const e2eSyncQueueChunk = chunks.find(
            (chunk) =>
              chunk.name === "e2e-sync-queue" ||
              chunk.facadeModuleId === syncQueueEntry
          );
          if (e2eIndexChunk) {
            this.emitFile({
              type: "asset",
              fileName: "src/index.js",
              source: `export * from "../${e2eIndexChunk.fileName}";\n`,
            });
          }
          if (e2eSyncQueueChunk) {
            this.emitFile({
              type: "asset",
              fileName: "src/sync-queue.js",
              source: `export * from "../${e2eSyncQueueChunk.fileName}";\n`,
            });
          }
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
                !fileName.endsWith("mockServiceWorker.js") &&
                !/^assets\/keycloak/i.test(fileName)
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
    proxy: {
      "/api": {
        target: GATEWAY_URL,
        changeOrigin: true,
        secure: false,
      },
    },
  },
  preview: {
    host: "::",
  },
  resolve: {
    alias: [
      ...(mode === "test"
        ? [
            {
              find: /^ui\/signing$/,
              replacement: path.resolve(
                __dirname,
                "../../packages/ui/index.js"
              ),
            },
          ]
        : []),
      ...createUiWorkspaceAliases(__dirname),
    ],
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
