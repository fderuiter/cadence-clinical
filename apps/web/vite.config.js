// Vue SPA Vite configuration
import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

function overrideBasePlugin() {
  return {
    name: "override-base-plugin",
    config(config) {
      let base = process.env.VITE_BASE_PATH || "/";
      if (!base.startsWith("/")) base = "/" + base;
      if (!base.endsWith("/")) base = base + "/";
      config.base = base;
      return { base };
    },
    configResolved(config) {
      let base = process.env.VITE_BASE_PATH || "/";
      if (!base.startsWith("/")) base = "/" + base;
      if (!base.endsWith("/")) base = base + "/";
      config.base = base;
    }
  };
}

export default defineConfig({
  base: "/",
  plugins: [vue(), overrideBasePlugin()],
  resolve: {
    alias: {
      ui: path.resolve(__dirname, "../../packages/ui/dist/index.js"),
      "@": path.resolve(__dirname, "./src"),
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
