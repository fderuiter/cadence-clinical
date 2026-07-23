import { defineConfig } from "vite";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export default defineConfig({
  base: "/cadence-clinical/",
  resolve: {
    alias: {
      ui: path.resolve(__dirname, "../../packages/ui/index.js"),
    },
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
  },
});
