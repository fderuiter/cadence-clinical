import path from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "../..");
const evidenceDir = process.env.CADENCE_TEST_EVIDENCE_DIR?.trim();

export default defineConfig({
  test: {
    environment: "node",
    ...(evidenceDir
      ? {
          reporters: ["default", "junit"],
          outputFile: {
            junit: path.resolve(
              repoRoot,
              evidenceDir,
              "vitest-usdm-schemas.xml"
            ),
          },
        }
      : {}),
  },
});
