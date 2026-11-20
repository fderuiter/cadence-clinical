import { defineConfig, loadEnv } from "vite";
import vue from "@vitejs/plugin-vue";
import path from "path";
import { fileURLToPath } from "url";
import { createStripMockWorkerInProdPlugin } from "../../scripts/vite-strip-mock-worker.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const DEV_PORT = 5174;
// Where the dev server's /api proxy forwards: the API gateway (the AppHost passes its endpoint; docker compose names
// the front proxy).
const GATEWAY_URL = process.env.CADENCE_GATEWAY_URL || "http://localhost:8000";

export default defineConfig(({ mode }) => ({
  base: process.env.VITE_BASE_PATH || "/subject-portal/",
  cacheDir: mode === "demo" ? "node_modules/.vite-demo" : "node_modules/.vite",
  optimizeDeps: {
    entries: [
      "index.html",
      "src/**/*.js",
      "src/**/*.vue",
      "../../packages/ui/index.js",
      "../../packages/usdm-schemas/src/index.ts",
    ],
    include: [
      "vue",
      "vue-router",
      "pinia",
      "keycloak-js",
      "msw",
      "msw/browser",
      "ui > axe-core",
      "usdm-schemas > zod",
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
    {
      name: "html-transform",
      transformIndexHtml(html) {
        const title =
          process.env.VITE_APP_TITLE || "Subject Portal - Cadence Clinical";
        return html.replace(/%VITE_APP_TITLE%/g, title);
      },
      configurePreviewServer(server) {
        if (server.httpServer) {
          server.httpServer.keepAliveTimeout = 65000;
          server.httpServer.headersTimeout = 66000;
        }
      },
    },
    {
      name: "e2e-module-entrypoints",
      apply: "build",
      config(userConfig) {
        if (userConfig.build?.outDir !== "node_modules/.e2e-prod") {
          return undefined;
        }
        return {
          build: {
            rollupOptions: {
              preserveEntrySignatures: "exports-only",
              input: {
                main: path.resolve(__dirname, "index.html"),
                "src/index": path.resolve(__dirname, "src/index.js"),
                "src/sync-queue": path.resolve(__dirname, "src/sync-queue.js"),
              },
              output: {
                entryFileNames: (chunkInfo) =>
                  chunkInfo.name.startsWith("src/")
                    ? "[name].js"
                    : "assets/[name]-[hash].js",
              },
            },
          },
        };
      },
      transformIndexHtml: {
        order: "post",
        handler(html, ctx) {
          if (ctx.bundle && html.includes("/subject-portal/assets/main-")) {
            return html.replace(
              "</body>",
              '<script type="module">import "/subject-portal/src/index.js";import "/subject-portal/src/sync-queue.js";</script></body>'
            );
          }
          return html;
        },
      },
    },
    createStripMockWorkerInProdPlugin(__dirname),
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
      // The mock gateway is test and dev tooling, kept out of the library's root export (#5335).
      {
        find: /^ui\/testing$/,
        replacement: path.resolve(
          __dirname,
          "../../packages/ui/mockGateway.js"
        ),
      },
      {
        find: /^ui$/,
        replacement: path.resolve(__dirname, "../../packages/ui/index.js"),
      },
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
    exclude: ["**/e2e/**", "node_modules/**", "dist/**"],
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
