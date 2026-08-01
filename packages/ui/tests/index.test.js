import { describe, it, expect, vi } from "vitest";
import { debounce, createClinicalLookupInput } from "../index.js";

describe("createClinicalLookupInput", () => {
  it("generates correct HTML structure for default status none", () => {
    const html = createClinicalLookupInput("test-id", "Test Label", "C123");
    expect(html).toContain('id="field-container-test-id"');
    expect(html).toContain('label for="test-id"');
    expect(html).toContain('value="C123"');
    expect(html).toContain('id="lookup-status-test-id"');
    expect(html).toContain('style="display: none"');
  });

  it("generates loading status correctly", () => {
    const html = createClinicalLookupInput(
      "test-id",
      "Test Label",
      "C123",
      "loading"
    );
    expect(html).toContain("lookup-loading");
    expect(html).toContain("⏳");
    expect(html).toContain("Searching terminology database...");
  });

  it("generates valid status with custom message", () => {
    const html = createClinicalLookupInput(
      "test-id",
      "Test Label",
      "C123",
      "valid",
      "Code is VALID"
    );
    expect(html).toContain("lookup-valid");
    expect(html).toContain("✅");
    expect(html).toContain("Code is VALID");
  });

  it("generates invalid status correctly", () => {
    const html = createClinicalLookupInput(
      "test-id",
      "Test Label",
      "C123",
      "invalid"
    );
    expect(html).toContain("lookup-invalid");
    expect(html).toContain("❌");
    expect(html).toContain("Invalid code.");
  });

  it("generates degraded status correctly", () => {
    const html = createClinicalLookupInput(
      "test-id",
      "Test Label",
      "C123",
      "degraded"
    );
    expect(html).toContain("lookup-degraded");
    expect(html).toContain("⚠️");
    expect(html).toContain("Terminology service degraded.");
  });
});

describe("debounce", () => {
  it("delays execution and bounds rapid invocations to a single call", () => {
    vi.useFakeTimers();
    const callback = vi.fn();
    const debounced = debounce(callback, 200);

    debounced("first");
    debounced("second");
    debounced("third");

    expect(callback).not.toHaveBeenCalled();

    vi.advanceTimersByTime(199);
    expect(callback).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(callback).toHaveBeenCalledTimes(1);
    expect(callback).toHaveBeenCalledWith("third");

    vi.useRealTimers();
  });
});

import {
  createConditionRowHTML,
  createRuleEditorHTML,
  serializeConditionsTree,
  deserializeConditionsTree,
} from "../index.js";

describe("Rule Builder Widgets & Serialization Helpers", () => {
  const forms = [
    { id: "form_vs", name: "Vital Signs" },
    { id: "form_dm", name: "Demographics" },
  ];
  const fields = [
    { id: "vssbp", name: "Systolic BP", formId: "form_vs" },
    { id: "sex", name: "Sex", formId: "form_dm" },
  ];

  describe("createConditionRowHTML", () => {
    it("generates correct accessible HTML for a condition row with default data", () => {
      const html = createConditionRowHTML(0, forms, fields);
      expect(html).toContain('class="condition-row-fieldset"');
      expect(html).toContain('data-index="0"');
      expect(html).toContain("Condition Element #1");
      expect(html).toContain('class="cond-form"');
      expect(html).toContain('class="cond-field"');
      expect(html).toContain('class="cond-operator"');
      expect(html).toContain('class="cond-right-type"');
    });

    it("pre-populates fields based on initialData", () => {
      const initial = {
        formId: "form_vs",
        fieldId: "vssbp",
        operator: ">",
        rightType: "constant",
        rightValue: "140",
      };
      const html = createConditionRowHTML(1, forms, fields, initial);
      expect(html).toContain('value="form_vs" selected');
      expect(html).toContain('value="vssbp" selected');
      expect(html).toContain('value=">" selected');
      expect(html).toContain('value="constant" selected');
      expect(html).toContain('value="140"');
    });
  });

  describe("createRuleEditorHTML", () => {
    it("generates correct HTML structure for a rule editor", () => {
      const html = createRuleEditorHTML(forms, fields, {
        ruleType: "skip_logic",
        targetField: "vssbp",
      });
      expect(html).toContain('class="rule-editor-container"');
      expect(html).toContain('id="rule-type-select"');
      expect(html).toContain('id="target-field-select"');
      expect(html).toContain('class="conditions-list-container"');
      expect(html).toContain('class="btn btn-secondary add-condition-btn"');
    });
  });

  describe("serializeConditionsTree and deserializeConditionsTree", () => {
    it("serializes dynamic UI rows to structured Pydantic condition tree format", () => {
      const conditions = [
        {
          formId: "form_vs",
          fieldId: "vssbp",
          operator: ">",
          rightType: "constant",
          rightValue: "140",
        },
      ];
      const tree = serializeConditionsTree(conditions, "and");
      expect(tree.type).toBe("comparison");
      expect(tree.operator).toBe(">");
      expect(tree.operands[0].type).toBe("field_ref");
      expect(tree.operands[0].field_ref.field_id).toBe("vssbp");
      expect(tree.operands[1].type).toBe("constant");
      expect(tree.operands[1].value).toBe(140);
    });

    it("serializes multiple conditions into a logical match group", () => {
      const conditions = [
        {
          formId: "form_vs",
          fieldId: "vssbp",
          operator: ">",
          rightType: "constant",
          rightValue: "140",
        },
        {
          formId: "form_dm",
          fieldId: "sex",
          operator: "==",
          rightType: "constant",
          rightValue: "F",
        },
      ];
      const tree = serializeConditionsTree(conditions, "or");
      expect(tree.type).toBe("logical");
      expect(tree.operator).toBe("or");
      expect(tree.operands).toHaveLength(2);
    });

    it("round-trips deserialization back into local state objects", () => {
      const originalTree = {
        type: "logical",
        operator: "and",
        operands: [
          {
            type: "comparison",
            operator: ">=",
            operands: [
              {
                type: "field_ref",
                field_ref: { field_id: "vssbp", form_id: "form_vs" },
              },
              { type: "constant", value: 120 },
            ],
          },
        ],
      };

      const result = deserializeConditionsTree(originalTree);
      expect(result.matchOperator).toBe("and");
      expect(result.conditions).toHaveLength(1);
      expect(result.conditions[0].fieldId).toBe("vssbp");
      expect(result.conditions[0].operator).toBe(">=");
      expect(result.conditions[0].rightType).toBe("constant");
      expect(result.conditions[0].rightValue).toBe("120");
    });
  });
});
