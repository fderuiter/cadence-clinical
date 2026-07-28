import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export default defineConfig({
  base: "/cadence-clinical/",
  plugins: [vue()],
  resolve: {
    alias: {
      ui: path.resolve(__dirname, "../../packages/ui/index.js"),
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
      input: {
        main: path.resolve(__dirname, "index.html"),
        legacy: path.resolve(__dirname, "legacy.html"),
      },
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
  },
});
