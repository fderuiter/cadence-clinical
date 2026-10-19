import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export default defineConfig({
  base: "/subject-portal/",
  plugins: [
    vue(),
    {
      name: "html-transform",
      transformIndexHtml(html) {
        const title =
          process.env.VITE_APP_TITLE || "Subject Portal - Cadence Clinical";
        return html.replace(/%VITE_APP_TITLE%/g, title);
      },
    },
  ],
  server: {
    port: 5174,
  },
  resolve: {
    alias: [
      // The mock gateway is test and dev tooling, kept out of the library's root export (#5335).
      {
        find: /^ui\/testing$/,
        replacement: path.resolve(
          __dirname,
          "../../packages/ui/mockGateway.js"
        ),
      },
      {
        find: /^ui$/,
        replacement: path.resolve(__dirname, "../../packages/ui/index.js"),
      },
    ],
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
  },
  test: {
    environment: "jsdom",
    globals: true,
    testTimeout: 15000,
    exclude: ["**/e2e/**", "node_modules/**", "dist/**"],
    setupFiles: [path.resolve(__dirname, "./tests/setup.js")],
    coverage: {
      provider: "v8",
      reporter: ["text", "json", "html"],
      // Every source file counts (ADR-2530); the mock service worker is not hand-written code.
      include: ["src/**/*.{ts,js,vue}"],
      exclude: ["src/mocks/**"],
      thresholds: {
        // A ratchet floor under the whole portal: raise it when coverage rises, never lower it.
        lines: 77,
        statements: 76,
        functions: 75,
        branches: 61,
        // The file ADR-251 put under its 80 percent gate keeps it.
        "src/sync-queue.js": {
          lines: 80,
          functions: 80,
          branches: 80,
          statements: 80,
        },
      },
    },
  },
});
