import { defineConfig, mergeConfig } from "vitest/config";
import viteConfig from "./vite.config.js";

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: "jsdom",
      globals: true,
      setupFiles: ["./tests/setup.js"],
      exclude: ["**/node_modules/**", "**/dist/**", "**/tests/e2e/**"],
    },
  })
);
