// Vue SPA Vite configuration
import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export default defineConfig({
  base: process.env.VITE_BASE_PATH || "/cadence-clinical/",
  plugins: [
    vue(),
    {
      name: "html-transform",
      transformIndexHtml(html) {
        const title = process.env.VITE_APP_TITLE || "Cadence Clinical";
        return html.replace(/%VITE_APP_TITLE%/g, title);
      },
    },
  ],
  resolve: {
    alias: {
      ui: path.resolve(__dirname, "../../packages/ui/dist/index.js"),
      "@": path.resolve(__dirname, "./src"),
      pinia: path.resolve(__dirname, "node_modules/pinia"),
      vue: path.resolve(__dirname, "node_modules/vue"),
    },
  },
  server: {
    port: 3000,
    strictPort: true,
    proxy: {
      "/api": {
        target: "http://localhost:8000",
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
