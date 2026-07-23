import { describe, it, expect } from "vitest";
import { validateField, sha256, renderFormFromJSON } from "../index.js";

describe("validateField", () => {
  it("returns valid: true if field has no validation rules", () => {
    const field = { id: "test", label: "Test" };
    expect(validateField(field, "")).toEqual({ valid: true });
    expect(validateField(field, "123")).toEqual({ valid: true });
  });

  it("handles required rule on empty values", () => {
    const field = { id: "test", label: "Test", validation: { required: true } };
    expect(validateField(field, "")).toEqual({
      valid: false,
      message: "This field is required.",
    });
    expect(validateField(field, "  ")).toEqual({
      valid: false,
      message: "This field is required.",
    });
    expect(validateField(field, "valid value")).toEqual({ valid: true });
  });

  it("handles pattern regex rules", () => {
    const field = {
      id: "brthdt",
      label: "Birth Date",
      validation: {
        pattern: "^\\d{4}-\\d{2}-\\d{2}$",
        message: "Date must be YYYY-MM-DD",
      },
    };
    // If value is empty, it shouldn't trigger pattern error unless required: true is set
    expect(validateField(field, "")).toEqual({ valid: true });

    expect(validateField(field, "1990-12-31")).toEqual({ valid: true });
    expect(validateField(field, "12-31-1990")).toEqual({
      valid: false,
      message: "Date must be YYYY-MM-DD",
    });
  });

  it("handles numeric min/max rules", () => {
    const field = {
      id: "vssbp",
      label: "Systolic BP",
      validation: {
        min: 50,
        max: 250,
        message: "Must be between 50 and 250",
      },
    };

    expect(validateField(field, "120")).toEqual({ valid: true });
    expect(validateField(field, "49")).toEqual({
      valid: false,
      message: "Must be between 50 and 250",
    });
    expect(validateField(field, "251")).toEqual({
      valid: false,
      message: "Must be between 50 and 250",
    });
    expect(validateField(field, "not-a-number")).toEqual({
      valid: false,
      message: "Value must be a number.",
    });
  });
});

describe("sha256 hashing", () => {
  it("generates a correct 64-character hex signature for a message", async () => {
    const hash = await sha256("Cadence Clinical GxP Compliance");
    expect(hash).toHaveLength(64);
    expect(hash).toMatch(/^[0-9a-f]{64}$/);

    // Verify it is deterministic
    const secondHash = await sha256("Cadence Clinical GxP Compliance");
    expect(hash).toBe(secondHash);

    // Verify change in payload changes the hash
    const otherHash = await sha256("Cadence Clinical GxP CompliancE");
    expect(hash).not.toBe(otherHash);
  });
});

describe("renderFormFromJSON integration", () => {
  it("correctly embeds CDASH data-attributes on the fields", () => {
    const payload = {
      formId: "TEST",
      fields: [
        {
          id: "brthdt",
          label: "Birth Date",
          type: "text",
          cdash: "DM.BRTHDT",
        },
      ],
    };
    const html = renderFormFromJSON(payload);
    expect(html).toContain('data-cdash="DM.BRTHDT"');
    expect(html).toContain('id="brthdt"');
  });
});
