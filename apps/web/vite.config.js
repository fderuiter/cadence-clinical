// Vue SPA Vite configuration
import { defineConfig, loadEnv } from "vite";
import vue from "@vitejs/plugin-vue";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { createStripMockWorkerInProdPlugin } from "../../scripts/vite-strip-mock-worker.mjs";
import {
  buildModeAwareCspContent,
  injectHoistedCspMeta as injectHoistedWebCspMeta,
  resolveViteOidcOrigin as resolveWebOidcOrigin,
} from "../../scripts/vite-hoist-csp.mjs";
import {
  createConstantFoldDemoModePlugin,
  createDemoFixturesPlugin,
  createUiWorkspaceAliases,
} from "../../scripts/vite-strip-demo-fixtures.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const DEV_PORT = 3000;
// Where the dev server's /api proxy forwards: the API gateway (the AppHost passes its endpoint).
const GATEWAY_URL = process.env.CADENCE_GATEWAY_URL || "http://localhost:8000";
const IS_E2E_BUILD = process.env.CADENCE_E2E_BUILD === "1";

export { injectHoistedWebCspMeta, resolveWebOidcOrigin };

export function buildWebCspContent(options = {}) {
  return buildModeAwareCspContent(options, {
    allowBlobMedia: true,
    allowBlobWorker: true,
    allowBlobFrame: true,
  });
}

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
      let resolvedOutDir = "dist";
      let resolvedCsp = buildWebCspContent();
      return {
        name: "html-transform",
        configResolved(resolved) {
          resolvedBase = resolved.base || "/cadence-clinical/";
          resolvedOutDir = resolved.build?.outDir || "dist";
          const fileEnv = loadEnv(resolved.mode, __dirname, "VITE_");
          const mergedEnv = { ...fileEnv, ...process.env };
          resolvedCsp = buildWebCspContent({
            mode: resolved.mode,
            isProduction: Boolean(resolved.isProduction),
            env: mergedEnv,
          });
        },
        transformIndexHtml: {
          order: "post",
          handler(html) {
            const title = process.env.VITE_APP_TITLE || "Cadence Clinical";
            const transformed = html.replace(/%VITE_APP_TITLE%/g, title);
            const withRecovery = IS_E2E_BUILD
              ? transformed.replace(
                  "</title>",
                  `</title>\n    <script src="${resolvedBase}e2e-boot-recovery.js"></script>`
                )
              : transformed;
            return injectHoistedWebCspMeta(withRecovery, resolvedCsp);
          },
        },
        configureServer(server) {
          server.middlewares.use((req, res, next) => {
            const cleanPath = (req.url || "").split("?")[0];
            if (
              cleanPath === "/silent-check-sso.html" ||
              cleanPath ===
                `${resolvedBase.replace(/\/?$/, "/")}silent-check-sso.html`
            ) {
              const ssoSrcPath = path.resolve(
                __dirname,
                "public/silent-check-sso.html"
              );
              if (fs.existsSync(ssoSrcPath)) {
                const rawHtml = fs.readFileSync(ssoSrcPath, "utf8");
                res.statusCode = 200;
                res.setHeader("Content-Type", "text/html; charset=utf-8");
                res.end(injectHoistedWebCspMeta(rawHtml, resolvedCsp));
                return;
              }
            }
            next();
          });
        },
        generateBundle() {
          if (!IS_E2E_BUILD) return;
          this.emitFile({
            type: "asset",
            fileName: "e2e-boot-recovery.js",
            source: E2E_BOOT_RECOVERY_SCRIPT,
          });
        },
        writeBundle(outputOptions) {
          const outDir = outputOptions?.dir
            ? path.resolve(__dirname, outputOptions.dir)
            : path.resolve(__dirname, resolvedOutDir);
          const emittedSsoPath = path.join(outDir, "silent-check-sso.html");
          if (fs.existsSync(emittedSsoPath)) {
            const rawSsoHtml = fs.readFileSync(emittedSsoPath, "utf8");
            fs.writeFileSync(
              emittedSsoPath,
              injectHoistedWebCspMeta(rawSsoHtml, resolvedCsp),
              "utf8"
            );
          }
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
    createConstantFoldDemoModePlugin(__dirname),
  ],
  resolve: {
    alias: [
      ...createUiWorkspaceAliases(__dirname),
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
    entries: [
      "index.html",
      "tests/browser/observation.html",
      "../../packages/ui/index.js",
    ],
    include: ["ui > axe-core"],
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
              const normalizedId = id.replace(/\\/g, "/");
              if (normalizedId.includes("vite/preload-helper")) {
                return "preload-helper";
              }
              if (
                /\/packages\/ui\/(?:session|callerScope)\.js(?:$|\?)/.test(
                  normalizedId
                )
              ) {
                return "ui-shell";
              }
              if (!normalizedId.includes("node_modules/")) {
                return undefined;
              }

              const pkgPath = normalizedId.slice(
                normalizedId.lastIndexOf("node_modules/") +
                  "node_modules/".length
              );

              if (
                pkgPath.startsWith("vue/") ||
                pkgPath.startsWith("@vue/") ||
                pkgPath.startsWith("vue-router/") ||
                pkgPath.startsWith("pinia/")
              ) {
                return "vendor-vue";
              }
              if (pkgPath.startsWith("hls.js/")) {
                return "vendor-media";
              }
              if (pkgPath.startsWith("keycloak-js/")) {
                return "vendor-auth";
              }
              if (
                pkgPath.startsWith("vuedraggable/") ||
                pkgPath.startsWith("sortablejs/")
              ) {
                return "vendor-vuedraggable";
              }
              return undefined;
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
      "**/tests/browser/**",
    ],
  },
});
