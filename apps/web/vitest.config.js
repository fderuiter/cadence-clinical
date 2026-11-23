import { fileURLToPath } from "node:url";
import { defineConfig, mergeConfig } from "vitest/config";
import viteConfig from "./vite.config.js";

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: "jsdom",
      globals: true,
      testTimeout: 15000,
      hookTimeout: 15000,
      // Tests opt into a demo build with vi.stubEnv("VITE_DEMO_MODE", "true"); undo it after each test.
      unstubEnvs: true,
      setupFiles: [fileURLToPath(new URL("./tests/setup.js", import.meta.url))],
      // Playwright specs (tests/e2e, and tests/browser for the browser-to-database check) are not Vitest tests.
      exclude: [
        "**/node_modules/**",
        "**/dist/**",
        "**/tests/e2e/**",
        "**/tests/browser/**",
      ],
      coverage: {
        provider: "v8",
        reporter: ["text", "json", "html"],
        // Every hand-written source file counts (ADR-2530): not the generated client and types, the mock service
        // worker or the bootstrap in main.js.
        include: ["src/**/*.{ts,js,vue}"],
        exclude: ["src/api/generated/**", "src/mocks/**", "src/main.js"],
        thresholds: {
          // A ratchet floor under the whole app: raise it when coverage rises, never lower it.
          lines: 68,
          statements: 67,
          functions: 54,
          branches: 59,
          // The files ADR-251 put under its 80 percent gate keep it.
          "src/components/{ReasonForChangeModal,ReasonModal}.vue": {
            lines: 80,
            functions: 80,
            branches: 80,
            statements: 80,
          },
          "src/stores/signatures.ts": {
            lines: 80,
            functions: 80,
            branches: 80,
            statements: 80,
          },
        },
      },
    },
  })
);
