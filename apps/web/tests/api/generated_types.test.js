import fs from "fs";
import os from "os";
import path from "path";
import { describe, it, expect } from "vitest";
import * as generatorModule from "../../scripts/generate-api-types.js";

const { discoverOpenApiServices, OUTPUT_DIR, outputPath, renderServiceTypes } =
  generatorModule;

const DISCOVERED_SERVICES = discoverOpenApiServices();

// The typed clients compile against src/api/generated/*.ts, so those files must match docs/openapi/.
describe("generated API types", () => {
  it("auto-discovers per-service and per-module OpenAPI contracts without GENERATED_SERVICES", () => {
    expect("GENERATED_SERVICES" in generatorModule).toBe(false);
    expect(DISCOVERED_SERVICES).toContain("kernel");
    expect(DISCOVERED_SERVICES).toContain("intelligence");
    expect(DISCOVERED_SERVICES).toContain("execution_forms");
    expect(DISCOVERED_SERVICES).not.toContain("cdisc");

    const checkedInFiles = fs
      .readdirSync(OUTPUT_DIR)
      .filter((name) => name.endsWith(".ts"))
      .sort();
    expect(checkedInFiles).toEqual(
      DISCOVERED_SERVICES.map((service) => `${service}.ts`)
    );
  });

  it("ignores legacy subdirectories and cdisc_openapi.json when discovering contracts", () => {
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), "cadence-openapi-"));
    try {
      fs.writeFileSync(path.join(tmpDir, "kernel_openapi.json"), "{}");
      fs.writeFileSync(path.join(tmpDir, "execution_forms_openapi.json"), "{}");
      fs.writeFileSync(path.join(tmpDir, "cdisc_openapi.json"), "{}");
      fs.mkdirSync(path.join(tmpDir, "legacy"));
      fs.writeFileSync(path.join(tmpDir, "legacy", "old_openapi.json"), "{}");

      expect(discoverOpenApiServices(tmpDir)).toEqual([
        "execution_forms",
        "kernel",
      ]);
    } finally {
      fs.rmSync(tmpDir, { recursive: true, force: true });
    }
  });

  it.each(DISCOVERED_SERVICES)(
    "src/api/generated/%s.ts matches its OpenAPI document (run `pnpm --filter web generate:api`)",
    async (service) => {
      const checkedIn = fs.readFileSync(outputPath(service), "utf8");
      expect(checkedIn).toBe(await renderServiceTypes(service));
    },
    30000
  );
});
