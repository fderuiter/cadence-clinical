import { describe, it, expect } from "vitest";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import {
  translateProperty,
  generate,
  calculateHash,
} from "../src/types/generate-types.js";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const usdmPath = path.resolve(__dirname, "../src/types/usdm.ts");
const hashPath = path.resolve(__dirname, "../src/types/.usdm-hash.json");

describe("Type-Array Union Generator (translateProperty)", () => {
  it("translates single primitive types correctly", () => {
    expect(translateProperty({ type: "string" })).toBe("string");
    expect(translateProperty({ type: "integer" })).toBe("number");
    expect(translateProperty({ type: "boolean" })).toBe("boolean");
    expect(translateProperty({ type: "null" })).toBe("null");
  });

  it("translates arrays of types into union types", () => {
    expect(translateProperty({ type: ["string", "null"] })).toBe(
      "string | null"
    );
    expect(translateProperty({ type: ["integer", "boolean", "null"] })).toBe(
      "number | boolean | null"
    );
  });

  it("translates array of empty types to any", () => {
    expect(translateProperty({ type: [] })).toBe("any");
  });

  it("translates array-based types recursively with internal structures", () => {
    const complexType = {
      type: ["array", "null"],
      items: { type: "string" },
    };
    expect(translateProperty(complexType)).toBe("string[] | null");
  });

  it("translates array-based types with enum values correctly", () => {
    const enumType = {
      type: ["string", "null"],
      enum: ["ACTIVE", "INACTIVE", null],
    };
    expect(translateProperty(enumType)).toBe('"ACTIVE" | "INACTIVE" | null');
  });
});

describe("Cache Validation and Execution Guard (generate)", () => {
  it("calculates a valid SHA-256 hash string", () => {
    const hash = calculateHash();
    expect(hash).toMatch(/^[a-f0-9]{64}$/);
  });

  it("returns early when content hash matches cached manifest", async () => {
    await generate({ force: true });
    expect(fs.existsSync(usdmPath)).toBe(true);
    expect(fs.existsSync(hashPath)).toBe(true);

    const startTime = performance.now();
    const result = await generate();
    const duration = performance.now() - startTime;

    expect(result.generated).toBe(false);
    expect(result.hash).toBe(calculateHash());
    expect(duration).toBeLessThan(100);
  }, 15000);

  it("forces regeneration when force option is true", async () => {
    await generate();
    const result = await generate({ force: true });

    expect(result.generated).toBe(true);
    expect(fs.existsSync(usdmPath)).toBe(true);
    expect(fs.existsSync(hashPath)).toBe(true);
  }, 15000);

  it("triggers full generation when usdm.ts is missing", async () => {
    await generate();
    const backupUsdm = fs.readFileSync(usdmPath, "utf8");
    fs.unlinkSync(usdmPath);

    try {
      const result = await generate();
      expect(result.generated).toBe(true);
      expect(fs.existsSync(usdmPath)).toBe(true);
    } finally {
      if (!fs.existsSync(usdmPath)) {
        fs.writeFileSync(usdmPath, backupUsdm, "utf8");
      }
    }
  }, 15000);

  it("triggers full generation when .usdm-hash.json is corrupted or missing", async () => {
    await generate();
    const backupHash = fs.readFileSync(hashPath, "utf8");
    fs.writeFileSync(hashPath, "{ invalid json }", "utf8");

    try {
      const result = await generate();
      expect(result.generated).toBe(true);
    } finally {
      fs.writeFileSync(hashPath, backupHash, "utf8");
    }
  }, 15000);
});
