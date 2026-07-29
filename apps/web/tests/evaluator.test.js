import { describe, it, expect, beforeEach } from "vitest";
import { setActivePinia, createPinia } from "pinia";
import {
  evaluateAST,
  compilerCache,
  getCompiledExpression,
  validateField,
} from "../index.js";
import { useClinicalStore } from "../src/stores/clinical.js";

describe("Client-side AST Evaluator & Cascading Nullification", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    compilerCache.cache.clear();
  });

  describe("AST Node Evaluation (evaluateAST)", () => {
    it("handles LITERAL / constant values", () => {
      const nodeA = { type: "constant", value: 42 };
      const nodeB = { node_type: "LITERAL", value: "hello" };

      expect(evaluateAST(nodeA)).toBe(42);
      expect(evaluateAST(nodeB)).toBe("hello");
    });

    it("handles XPATH / field_ref values", () => {
      const nodeA = { type: "field_ref", field_ref: { field_id: "vssbp" } };
      const nodeB = { node_type: "XPATH", value: "../vssbp" };

      const context = { vssbp: 120 };
      expect(evaluateAST(nodeA, context)).toBe(120);
      expect(evaluateAST(nodeB, context)).toBe(120);
    });

    it("evaluates comparisons", () => {
      const node = {
        type: "comparison",
        operator: ">=",
        operands: [
          { type: "field_ref", field_ref: { field_id: "pulse" } },
          { type: "constant", value: 100 },
        ],
      };

      expect(evaluateAST(node, { pulse: 105 })).toBe(true);
      expect(evaluateAST(node, { pulse: 90 })).toBe(false);
    });

    it("supports null behavior / semantics in comparison", () => {
      // Comparison operator with nulls
      const node = {
        type: "comparison",
        operator: ">",
        operands: [
          { type: "field_ref", field_ref: { field_id: "height" } },
          { type: "constant", value: 0 },
        ],
      };

      // If either is null/undefined, ordered comparison returns false
      expect(evaluateAST(node, { height: null })).toBe(false);
      expect(evaluateAST(node, { height: undefined })).toBe(false);

      // Equality comparison is null-safe
      const eqNode = {
        type: "comparison",
        operator: "==",
        operands: [
          { type: "field_ref", field_ref: { field_id: "height" } },
          { type: "constant", value: null },
        ],
      };
      expect(evaluateAST(eqNode, { height: null })).toBe(true);
      expect(evaluateAST(eqNode, { height: 1.8 })).toBe(false);
    });

    it("performs BMI calculations & ensures null-height safety", () => {
      // weight / (height * height)
      const bmiExpression = {
        type: "comparison",
        operator: "/",
        operands: [
          { type: "field_ref", field_ref: { field_id: "weight" } },
          {
            type: "comparison",
            operator: "*",
            operands: [
              { type: "field_ref", field_ref: { field_id: "height" } },
              { type: "field_ref", field_ref: { field_id: "height" } },
            ],
          },
        ],
      };

      // Valid case: 70kg / (1.75m * 1.75m) ~ 22.85
      const res = evaluateAST(bmiExpression, { weight: 70, height: 1.75 });
      expect(res).toBeCloseTo(22.857, 2);

      // Null safety check: weight is 70, height is null
      expect(
        evaluateAST(bmiExpression, { weight: 70, height: null })
      ).toBeNull();
      // Zero division safety: weight is 70, height is 0
      expect(evaluateAST(bmiExpression, { weight: 70, height: 0 })).toBeNull();
    });

    it("supports indexed-repeat", () => {
      const node = {
        type: "function",
        operator: "indexed-repeat",
        operands: [
          { type: "field_ref", field_ref: { field_id: "vssbp" } },
          { type: "field_ref", field_ref: { field_id: "repeating_vs" } },
          { type: "constant", value: 2 },
        ],
      };

      const context = {
        "repeating_vs[1]/vssbp": 110,
        "repeating_vs[2]/vssbp": 130,
      };

      expect(evaluateAST(node, context)).toBe(130);
    });

    it("supports is_empty and is_not_empty", () => {
      const isEmptyNode = {
        type: "function",
        operator: "is_empty",
        operands: [{ type: "field_ref", field_ref: { field_id: "comment" } }],
      };

      expect(evaluateAST(isEmptyNode, { comment: "" })).toBe(true);
      expect(evaluateAST(isEmptyNode, { comment: null })).toBe(true);
      expect(evaluateAST(isEmptyNode, { comment: "hello" })).toBe(false);
    });
  });

  describe("LRU Compiler Caching", () => {
    it("reuses cached expression compilers up to capacity 200", () => {
      const node = {
        type: "comparison",
        operator: "==",
        operands: [
          { type: "field_ref", field_ref: { field_id: "vssbp" } },
          { type: "constant", value: 120 },
        ],
      };

      const fn1 = getCompiledExpression(node);
      const fn2 = getCompiledExpression(node);

      expect(fn1).toBe(fn2); // Identical function returned from cache

      // Let's populate the cache to exceed capacity of 200
      for (let i = 0; i < 205; i++) {
        getCompiledExpression({ type: "constant", value: i });
      }

      // First node should be evicted due to LRU 200 capacity limit
      getCompiledExpression(node);
      expect(compilerCache.cache.size).toBeLessThanOrEqual(200);
    });
  });

  describe("Cascading Nullification & Form Visibility", () => {
    it("applies relevant hides/toggles, purges inactive child variable, and logs exact mandated audit reason", async () => {
      const store = useClinicalStore();

      // Ensure pulse_details is hidden initially since pulse = 72 (not > 100)
      store.formValues.pulse = "72";
      store.formValues.pulse_details = "I was tachycardic earlier";
      await store.evaluateRules();

      // pulse_details should have become irrelevant
      expect(store.fieldVisibility.pulse_details).toBe(false);
      // Its value must be purged from memory
      expect(store.formValues.pulse_details).toBe("");

      // It must log the EXACT mandated audit reason in the ledger blocks
      const purgeBlock = store.ledgerBlocks.find(
        (b) =>
          b.action === "FIELD_PURGE" && b.details.fieldId === "pulse_details"
      );
      expect(purgeBlock).toBeDefined();
      expect(purgeBlock.reason).toBe(
        "System-initiated purge of inactive child variable due to parent value mutation"
      );
    });

    it("supports cascading triggers where nullifying one field hides another in a chain", async () => {
      const store = useClinicalStore();

      // Configure a visibility chain:
      // A (pulse) > 100 enables B (pulse_details)
      // B (pulse_details) == "ALERT" enables C (bmi_status)
      const fieldC = store.ecrfFields.find((f) => f.id === "bmi_status");

      fieldC.relevant = {
        type: "comparison",
        operator: "==",
        operands: [
          { type: "field_ref", field_ref: { field_id: "pulse_details" } },
          { type: "constant", value: "ALERT" },
        ],
      };

      // Set initial state:
      store.formValues.pulse = "120"; // enables B
      store.formValues.pulse_details = "ALERT"; // enables C
      store.formValues.bmi_status = "Dangerous";
      await store.evaluateRules();

      // Both B and C should be visible
      expect(store.fieldVisibility.pulse_details).toBe(true);
      expect(store.fieldVisibility.bmi_status).toBe(true);

      // Now change pulse back to 70 (parent value mutation)
      store.formValues.pulse = "70";
      await store.evaluateRules(); // triggers cascading nullification

      // pulse_details should be hidden and cleared
      expect(store.fieldVisibility.pulse_details).toBe(false);
      expect(store.formValues.pulse_details).toBe("");

      // This should cascade to bmi_status which depends on pulse_details === 'ALERT'
      expect(store.fieldVisibility.bmi_status).toBe(false);
      expect(store.formValues.bmi_status).toBe("");

      // Check purge audit logs are recorded for both cascading levels
      const purges = store.ledgerBlocks.filter(
        (b) => b.action === "FIELD_PURGE"
      );
      expect(purges.length).toBeGreaterThanOrEqual(2);
      purges.forEach((p) => {
        expect(p.reason).toBe(
          "System-initiated purge of inactive child variable due to parent value mutation"
        );
      });
    });
  });

  describe("Constraint Augments Existing Validation", () => {
    it("reports constraint failures with validateField", () => {
      const heightFieldMeta = {
        id: "height",
        validation: { required: true },
        constraint: {
          condition: {
            type: "comparison",
            operator: ">",
            operands: [
              { type: "field_ref", field_ref: { field_id: "height" } },
              { type: "constant", value: 0 },
            ],
          },
          query_message: "Height must be strictly greater than zero.",
        },
      };

      // Valid value
      let res = validateField(heightFieldMeta, "1.75", { height: 1.75 });
      expect(res.valid).toBe(true);

      // Invalid value violating constraint (value <= 0)
      res = validateField(heightFieldMeta, "0", { height: 0 });
      expect(res.valid).toBe(false);
      expect(res.message).toBe("Height must be strictly greater than zero.");
    });
  });

  describe("Function Arity and Indexed Repeat Parity", () => {
    it("handles valid indexed-repeat with exactly 3 operands", () => {
      const node = {
        type: "function",
        operator: "indexed-repeat",
        operands: [
          { type: "field_ref", field_ref: { field_id: "vssbp" } },
          { type: "field_ref", field_ref: { field_id: "repeating_vs" } },
          { type: "constant", value: 2 },
        ],
      };

      const context = {
        "repeating_vs[1]/vssbp": 110,
        "repeating_vs[2]/vssbp": 130,
      };

      expect(evaluateAST(node, context)).toBe(130);
    });

    it("returns null for indexed-repeat with invalid operand arity", () => {
      // 2 operands
      const node2 = {
        type: "function",
        operator: "indexed-repeat",
        operands: [
          { type: "field_ref", field_ref: { field_id: "vssbp" } },
          { type: "field_ref", field_ref: { field_id: "repeating_vs" } },
        ],
      };

      // 4 operands
      const node4 = {
        type: "function",
        operator: "indexed-repeat",
        operands: [
          { type: "field_ref", field_ref: { field_id: "vssbp" } },
          { type: "field_ref", field_ref: { field_id: "repeating_vs" } },
          { type: "constant", value: 2 },
          { type: "constant", value: 4 },
        ],
      };

      const context = {
        "repeating_vs[1]/vssbp": 110,
        "repeating_vs[2]/vssbp": 130,
      };

      expect(evaluateAST(node2, context)).toBeNull();
      expect(evaluateAST(node4, context)).toBeNull();
    });

    it("returns null for empty/is_empty/is_not_empty with invalid arity", () => {
      const isEmptyInvalid = {
        type: "function",
        operator: "is_empty",
        operands: [
          { type: "field_ref", field_ref: { field_id: "comment" } },
          { type: "field_ref", field_ref: { field_id: "vssbp" } },
        ],
      };

      const isNotEmptyInvalid = {
        type: "function",
        operator: "is_not_empty",
        operands: [],
      };

      expect(
        evaluateAST(isEmptyInvalid, { comment: "", vssbp: 120 })
      ).toBeNull();
      expect(evaluateAST(isNotEmptyInvalid, {})).toBeNull();
    });
  });
});
