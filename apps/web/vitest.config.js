import { fileURLToPath } from "node:url";
import { defineConfig, mergeConfig } from "vitest/config";
import viteConfig from "./vite.config.js";

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: "jsdom",
      globals: true,
      setupFiles: [fileURLToPath(new URL("./tests/setup.js", import.meta.url))],
      exclude: ["**/node_modules/**", "**/dist/**", "**/tests/e2e/**"],
    },
  })
);
