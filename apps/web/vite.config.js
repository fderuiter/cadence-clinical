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
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: [path.resolve(__dirname, "./tests/setup.js")],
    exclude: ["**/node_modules/**", "**/dist/**", "**/tests/e2e/**"],
  },
});
