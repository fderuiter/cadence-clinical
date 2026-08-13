import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export default defineConfig({
  base: "/subject-portal/",
  plugins: [
    vue(),
    {
      name: "html-transform",
      transformIndexHtml(html) {
        const title =
          process.env.VITE_APP_TITLE || "Subject Portal - Cadence Clinical";
        return html.replace(/%VITE_APP_TITLE%/g, title);
      },
    },
  ],
  server: {
    port: 5174,
  },
  resolve: {
    alias: {
      ui: path.resolve(__dirname, "../../packages/ui/index.js"),
    },
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
  },
  test: {
    environment: "jsdom",
    globals: true,
    exclude: ["**/e2e/**", "node_modules/**", "dist/**"],
    setupFiles: [path.resolve(__dirname, "./tests/setup.js")],
    coverage: {
      provider: "v8",
      reporter: ["text", "json", "html"],
      include: ["src/sync-queue.js"],
      thresholds: {
        lines: 80,
        functions: 80,
        branches: 80,
        statements: 80,
      },
    },
  },
});
