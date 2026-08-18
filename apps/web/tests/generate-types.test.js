import { describe, it, expect } from "vitest";
import { translateProperty } from "../src/types/generate-types.js";

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
