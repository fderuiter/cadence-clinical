import { fileURLToPath } from "node:url";
import { defineConfig, mergeConfig } from "vitest/config";
import viteConfig from "./vite.config.js";

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: "jsdom",
      globals: true,
      testTimeout: 45000,
      setupFiles: [fileURLToPath(new URL("./tests/setup.js", import.meta.url))],
      exclude: ["**/node_modules/**", "**/dist/**", "**/tests/e2e/**"],
      coverage: {
        provider: "v8",
        reporter: ["text", "json", "html"],
        include: [
          "src/components/ReasonForChangeModal.vue",
          "src/components/ReasonModal.vue",
          "src/components/ReviewCommentsSidebar.vue",
          "src/components/TrivialComponent.vue",
          "src/stores/signatures.ts",
          "src/stores/sync.ts",
        ],
        thresholds: {
          lines: 80,
          functions: 80,
          branches: 80,
          statements: 80,
        },
      },
    },
  })
);
