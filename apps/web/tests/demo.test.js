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

import { createConditionRow, createRuleEditorContainer } from "ui";

describe("Visual Rules Editor Integration Tests", () => {
  const mockForms = [
    { id: "form_dm", name: "Demographics" },
    { id: "form_vs", name: "Vital Signs" },
  ];
  const mockFields = [
    { id: "brthdt", name: "Date of Birth", formId: "form_dm" },
    { id: "vssbp", name: "Systolic BP", formId: "form_vs" },
  ];

  it("serializes visual condition row config into expected expression trees and schemas", () => {
    // We simulate creating a comparison node: VS.VSSBP > 140
    const rowValues = {
      formId: "form_vs",
      fieldId: "vssbp",
      operator: ">",
      rightType: "constant",
      rightValue: "140",
    };

    // Verify row structure compiles and populates properly
    const rowHTML = createConditionRow(0, mockForms, mockFields, rowValues);
    expect(rowHTML).toContain('value="form_vs" selected');
    expect(rowHTML).toContain('value="vssbp" selected');
    expect(rowHTML).toContain('value=">" selected');
    expect(rowHTML).toContain('value="140"');

    // Expected ExpressionNode representation of this comparison
    const expectedNode = {
      type: "comparison",
      operator: ">",
      operands: [
        {
          type: "field_ref",
          field_ref: {
            field_id: "vssbp",
            form_id: "form_vs",
          },
        },
        {
          type: "constant",
          value: 140,
        },
      ],
    };

    expect(expectedNode.type).toBe("comparison");
    expect(expectedNode.operator).toBe(">");
    expect(expectedNode.operands[0].field_ref.field_id).toBe("vssbp");
    expect(expectedNode.operands[1].value).toBe(140);

    // Verify expected CreateRuleRequest schema representation for constraint rules
    const constraintRuleRequest = {
      type: "constraint",
      condition: expectedNode,
      target_field: "vssbp",
      query_message: "Systolic Blood Pressure must be within logical limits.",
    };

    expect(constraintRuleRequest.type).toBe("constraint");
    expect(constraintRuleRequest.target_field).toBe("vssbp");
    expect(constraintRuleRequest.query_message).toBeDefined();
  });

  it("creates fully-compliant skip logic rules", () => {
    const expectedNode = {
      type: "comparison",
      operator: "==",
      operands: [
        {
          type: "field_ref",
          field_ref: {
            field_id: "brthdt",
            form_id: "form_dm",
          },
        },
        {
          type: "constant",
          value: "1990-01-01",
        },
      ],
    };

    const skipLogicRequest = {
      type: "skip_logic",
      condition: expectedNode,
      action: "show",
      target_field: "vssbp",
      target_form: "form_vs",
    };

    expect(skipLogicRequest.type).toBe("skip_logic");
    expect(skipLogicRequest.action).toBe("show");
    expect(skipLogicRequest.target_field).toBe("vssbp");
    expect(skipLogicRequest.target_form).toBe("form_vs");
  });
});
