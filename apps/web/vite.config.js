// Vue SPA Vite configuration
import { defineConfig, loadEnv } from "vite";
import vue from "@vitejs/plugin-vue";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const DEV_PORT = 3000;
// Where the dev server's /api proxy forwards: the API gateway (the AppHost passes its endpoint).
const GATEWAY_URL = process.env.CADENCE_GATEWAY_URL || "http://localhost:8000";

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
    {
      name: "html-transform",
      transformIndexHtml(html) {
        const title = process.env.VITE_APP_TITLE || "Cadence Clinical";
        return html.replace(/%VITE_APP_TITLE%/g, title);
      },
    },
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
  build: {
    outDir: "dist",
    emptyOutDir: true,
    rollupOptions: {
      output: {
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
            if (id.includes("stores/clinical") || id.includes("/clinical.")) {
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
            return "api-services";
          }

          // Third-party vendor packages
          if (id.includes("node_modules")) {
            if (
              id.includes("vue") ||
              id.includes("vue-router") ||
              id.includes("pinia")
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
      "**/tests/browser/**",
    ],
  },
});
