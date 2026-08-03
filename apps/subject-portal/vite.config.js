import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export default defineConfig({
  base: "/subject-portal/",
  plugins: [vue()],
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
  },
});
