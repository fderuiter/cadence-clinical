import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import { resolve } from "path";
import { fileURLToPath } from "url";

const __dirname = fileURLToPath(new URL(".", import.meta.url));

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
  test: {
    coverage: {
      provider: "v8",
      reporter: ["text", "json", "html"],
      // Every hand-written source file counts (ADR-2530), including the components that no test reaches yet.
      include: ["*.js", "src/**/*.{js,ts,vue}"],
      exclude: [
        "*.config.js",
        "index.d.ts",
        "dist/**",
        "scripts/**",
        "tests/**",
      ],
      thresholds: {
        // A ratchet floor under the whole package: raise it when coverage rises, never lower it.
        lines: 37,
        statements: 36,
        functions: 29,
        branches: 33,
        // The Part 11 signing and eConsent helpers carry the 80 percent file gate (#5335).
        "{signing,econsent}.js": {
          lines: 80,
          functions: 80,
          branches: 80,
          statements: 80,
        },
      },
    },
  },
});
