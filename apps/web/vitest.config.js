import { fileURLToPath } from "node:url";
import { defineConfig, mergeConfig } from "vitest/config";
import viteConfig from "./vite.config.js";

/**
 * Resolves lazy demo workspace boundaries (`defineAsyncComponent(() => import("./demo/..."))`)
 * synchronously inside Vitest so existing synchronous `mount(CtmsView)` / `mount(EcrfView)`
 * tests render on tick 0 when `VITE_DEMO_MODE=true`.
 */
function eagerDemoWorkspacesForVitest() {
  return {
    name: "cadence-vitest-eager-demo-workspaces",
    enforce: "pre",
    transform(code, id) {
      const cleanId = id.split("?", 1)[0].replaceAll("\\", "/");
      if (
        !cleanId.endsWith("/src/views/EcrfView.vue") &&
        !cleanId.endsWith("/src/views/CtmsView.vue")
      ) {
        return null;
      }
      const next = code.replace(
        /const\s+(EcrfDemoWorkspace|CtmsDemoWorkspace)\s*=\s*defineAsyncComponent\(\s*\(\)\s*=>\s*import\((["']\.\/demo\/(?:Ecrf|Ctms)DemoWorkspace\.vue["'])\)\s*,?\s*\);?/g,
        "import $1 from $2;"
      );
      return next === code ? null : { code: next, map: null };
    },
  };
}

export default mergeConfig(
  viteConfig,
  defineConfig({
    plugins: [eagerDemoWorkspacesForVitest()],
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
        "**/dist-demo/**",
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
