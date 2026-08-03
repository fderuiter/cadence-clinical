import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import { resolve } from "path";

export default defineConfig({
  plugins: [vue()],
  build: {
    lib: {
      entry: resolve(__dirname, "index.js"),
      name: "ui",
      fileName: "index",
      formats: ["es", "cjs"],
    },
    rollupOptions: {
      // Externalize dependencies that shouldn't be bundled into your library
      external: ["vue", "pinia"],
      output: {
        // Provide global variables for externalized dependencies in UMD/IIFE builds
        globals: {
          vue: "Vue",
          pinia: "Pinia",
        },
      },
    },
  },
});
