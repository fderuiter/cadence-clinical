import path from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

const repoRoot = path.dirname(fileURLToPath(import.meta.url));
const evidenceDir = process.env.CADENCE_TEST_EVIDENCE_DIR?.trim();

export default defineConfig({
  test: {
    environment: "node",
    include: ["docs/.vitepress/**/*.test.mjs"],
    exclude: [
      "**/node_modules/**",
      "**/dist/**",
      "docs/.vitepress/{.temp,cache,dist}/**",
    ],
    ...(evidenceDir
      ? {
          reporters: ["default", "junit"],
          outputFile: {
            junit: path.resolve(repoRoot, evidenceDir, "vitest-docs.xml"),
          },
        }
      : {}),
  },
});
