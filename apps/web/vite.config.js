// Vue SPA Vite configuration
import { defineConfig, loadEnv } from "vite";
import vue from "@vitejs/plugin-vue";
import path from "path";
import { fileURLToPath } from "url";
import { createStripMockWorkerInProdPlugin } from "../../scripts/vite-strip-mock-worker.mjs";
import { createDemoFixturesPlugin } from "../../scripts/vite-strip-demo-fixtures.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const DEV_PORT = 3000;
// Where the dev server's /api proxy forwards: the API gateway (the AppHost passes its endpoint).
const GATEWAY_URL = process.env.CADENCE_GATEWAY_URL || "http://localhost:8000";
const IS_E2E_BUILD = process.env.CADENCE_E2E_BUILD === "1";

const E2E_BOOT_RECOVERY_SCRIPT = `(function () {
  var RETRY_KEY = "__cadence_e2e_boot_retry__";
  var assetFailed = false;
  var reloading = false;
  function retryBoot() {
    var attempts = Number(sessionStorage.getItem(RETRY_KEY) || "0");
    if (reloading || attempts >= 3) return;
    reloading = true;
    sessionStorage.setItem(RETRY_KEY, String(attempts + 1));
    window.location.reload();
  }
  window.addEventListener(
    "error",
    function (e) {
      var t = e && e.target;
      if (t && (t.tagName === "SCRIPT" || t.tagName === "LINK")) {
        assetFailed = true;
        if (document.readyState === "complete") {
          setTimeout(retryBoot, 150);
        }
      }
    },
    true
  );
  window.addEventListener("vite:preloadError", function (e) {
    e.preventDefault();
    setTimeout(retryBoot, 150);
  });
  window.addEventListener("load", function () {
    var app = document.getElementById("app");
    if (app) {
      if (app.childNodes.length > 0) {
        sessionStorage.removeItem(RETRY_KEY);
        return;
      }
      var obs = new MutationObserver(function () {
        if (app.childNodes.length > 0) {
          sessionStorage.removeItem(RETRY_KEY);
          obs.disconnect();
        }
      });
      obs.observe(app, { childList: true });
    }
    if (assetFailed) {
      setTimeout(retryBoot, 150);
      return;
    }
    setTimeout(function () {
      if (app && app.childNodes.length === 0) {
        retryBoot();
      }
    }, 1500);
  });
})();
`;

export default defineConfig({
  base: process.env.VITE_BASE_PATH || "/cadence-clinical/",
  plugins: [
    vue(),
    {
      // The gateway sends no CORS headers, so a dev server page that called http://localhost:8000 directly would be
      // blocked by the browser. Unless VITE_API_BASE_URL is set (environment or .env files), the dev server sends API calls to its own origin,
      // and the /api proxy below forwards them to the gateway (#5429). Builds, tests and demo mode are unchanged.
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
      let resolvedBase = "/cadence-clinical/";
      return {
        name: "html-transform",
        configResolved(resolved) {
          resolvedBase = resolved.base || "/cadence-clinical/";
        },
        transformIndexHtml(html) {
          const title = process.env.VITE_APP_TITLE || "Cadence Clinical";
          const transformed = html.replace(/%VITE_APP_TITLE%/g, title);
          return IS_E2E_BUILD
            ? transformed.replace(
                "</title>",
                `</title>\n    <script src="${resolvedBase}e2e-boot-recovery.js"></script>`
              )
            : transformed;
        },
        generateBundle() {
          if (!IS_E2E_BUILD) return;
          this.emitFile({
            type: "asset",
            fileName: "e2e-boot-recovery.js",
            source: E2E_BOOT_RECOVERY_SCRIPT,
          });
        },
        configurePreviewServer(server) {
          if (server.httpServer) {
            server.httpServer.keepAliveTimeout = 65000;
            server.httpServer.headersTimeout = 66000;
          }
        },
      };
    })(),
    createStripMockWorkerInProdPlugin(__dirname),
    createDemoFixturesPlugin(__dirname),
  ],
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
        replacement: path.resolve(__dirname, "../../packages/ui/dist/index.js"),
      },
      { find: "@", replacement: path.resolve(__dirname, "./src") },
      {
        find: "pinia",
        replacement: path.resolve(__dirname, "node_modules/pinia"),
      },
      {
        find: /^vuedraggable$/,
        replacement: path.resolve(
          __dirname,
          "node_modules/vuedraggable/src/vuedraggable.js"
        ),
      },
      { find: "vue", replacement: path.resolve(__dirname, "node_modules/vue") },
    ],
  },
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
    ...(IS_E2E_BUILD ? { proxy: {} } : {}),
  },
  optimizeDeps: {
    entries: ["index.html", "tests/browser/observation.html"],
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    rollupOptions: {
      output: IS_E2E_BUILD
        ? {
            codeSplitting: false,
          }
        : {
            manualChunks(id) {
              // Workspace library dependencies
              if (id.includes("/packages/ui/") || id.includes("packages/ui/")) {
                return "workspace-ui";
              }
              if (
                id.includes("/packages/usdm-schemas/") ||
                id.includes("packages/usdm-schemas/")
              ) {
                return "workspace-usdm";
              }
              if (id.includes("/packages/") || id.includes("packages/")) {
                return "workspace-packages";
              }

              // Core application stores
              if (id.includes("/src/stores/") || id.includes("src/stores/")) {
                if (
                  id.includes("stores/clinical") ||
                  id.includes("/clinical.")
                ) {
                  return "store-clinical";
                }
                if (id.includes("stores/etmf") || id.includes("/etmf.")) {
                  return "store-etmf";
                }
                const match = id.match(/src\/stores\/([^/.]+)/);
                if (match && match[1]) {
                  const name = match[1]
                    .replace(/([a-z])([A-Z])/g, "$1-$2")
                    .toLowerCase();
                  return `store-${name}`;
                }
                return "store-core";
              }

              // Shared API clients and HTTP services
              if (
                id.includes("/src/api/") ||
                id.includes("src/api/") ||
                id.includes("/src/services/") ||
                id.includes("src/services/")
              ) {
                if (
                  /src\/api\/(?:auditor|designer|econsent|fileshare|ingestionClient|interop|notifications|soaClient|statisticalComputing|terminologyClient)\.[jt]s$/.test(
                    id
                  ) ||
                  /src\/services\/api\.[jt]s$/.test(id)
                ) {
                  return "api-legacy-stubs";
                }
                return "api-services";
              }

              // Third-party vendor packages
              if (id.includes("node_modules")) {
                if (
                  id.includes("/vue/") ||
                  id.includes("/@vue/") ||
                  id.includes("/vue-router/") ||
                  id.includes("/pinia/")
                ) {
                  return "vendor-vue";
                }
                if (id.includes("hls.js")) {
                  return "vendor-media";
                }
                if (id.includes("keycloak-js")) {
                  return "vendor-auth";
                }
                return "vendor";
              }
            },
          },
    },
    chunkSizeWarningLimit: 1000,
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: [path.resolve(__dirname, "./tests/setup.js")],
    exclude: [
      "**/node_modules/**",
      "**/dist/**",
      "**/tests/e2e/**",
      "**/tests/e2e-staged/**",
      "**/tests/browser/**",
    ],
  },
});
