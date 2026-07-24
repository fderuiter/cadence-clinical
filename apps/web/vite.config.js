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
  build: {
    outDir: "dist",
    emptyOutDir: true,
  },
  test: {
    environment: "jsdom",
    globals: true,
  },
});
