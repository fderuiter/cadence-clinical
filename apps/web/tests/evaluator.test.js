import { describe, it, expect, beforeEach } from "vitest";
import { setActivePinia, createPinia } from "pinia";
import { mount } from "@vue/test-utils";
import { validateField } from "ui";
import {
  evaluateAST,
  compilerCache,
  getCompiledExpression,
  renderFormFromJSON,
  validateField as indexValidateField,
} from "../src/evaluator.js";
import { useClinicalStore } from "../src/stores/clinical";
import HeadlessFormRenderer from "../src/components/crf/HeadlessFormRenderer.vue";

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
      let res = validateField(
        heightFieldMeta,
        "1.75",
        { height: 1.75 },
        evaluateAST
      );
      expect(res.valid).toBe(true);

      // Invalid value violating constraint (value <= 0)
      res = validateField(heightFieldMeta, "0", { height: 0 }, evaluateAST);
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

  describe("index.js Module (renderFormFromJSON & validateField)", () => {
    it("renders and evaluates skip logic, hides fields by default, and cascades nullifications with correct audit log message", () => {
      const fields = [
        { id: "pulse", label: "Pulse", type: "text" },
        {
          id: "pulse_details",
          label: "Pulse Details",
          type: "text",
          relevant: {
            type: "comparison",
            operator: ">",
            operands: [
              { type: "field_ref", field_ref: { field_id: "pulse" } },
              { type: "constant", value: 100 },
            ],
          },
        },
      ];

      const initialValues = { pulse: "120", pulse_details: "Tachycardic" };
      const auditLogs = [];
      const logPurge = (fieldId, val, reason) => {
        auditLogs.push({ fieldId, val, reason });
      };

      // When pulse is 120, pulse_details is relevant
      const res1 = renderFormFromJSON(fields, initialValues, logPurge);
      expect(res1.visibleFields.pulse_details).toBe(true);
      expect(res1.updatedValues.pulse_details).toBe("Tachycardic");
      expect(auditLogs.length).toBe(0);

      // Change pulse to 70 (parent value mutation)
      const res2 = renderFormFromJSON(
        fields,
        { ...res1.updatedValues, pulse: "70" },
        logPurge
      );
      expect(res2.visibleFields.pulse_details).toBe(false);
      expect(res2.updatedValues.pulse_details).toBe("");
      expect(auditLogs.length).toBe(1);
      expect(auditLogs[0]).toEqual({
        fieldId: "pulse_details",
        val: "Tachycardic",
        reason:
          "System-initiated purge of inactive child variable due to parent value mutation",
      });
    });

    it("evaluates constraints via validateField in index.js", () => {
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
      const res1 = indexValidateField(heightFieldMeta, "1.75", {
        height: 1.75,
      });
      expect(res1.valid).toBe(true);

      // Invalid value violating constraint (value <= 0)
      const res2 = indexValidateField(heightFieldMeta, "0", { height: 0 });
      expect(res2.valid).toBe(false);
      expect(res2.message).toBe("Height must be strictly greater than zero.");
    });
  });

  describe("Numeric String Coercion in Comparisons", () => {
    it("coerces numeric strings with numbers and other numeric strings", () => {
      const eqNode = (left, right) => ({
        type: "comparison",
        operator: "==",
        operands: [
          { type: "constant", value: left },
          { type: "constant", value: right },
        ],
      });

      const neqNode = (left, right) => ({
        type: "comparison",
        operator: "!=",
        operands: [
          { type: "constant", value: left },
          { type: "constant", value: right },
        ],
      });

      expect(evaluateAST(eqNode("120", 120))).toBe(true);
      expect(evaluateAST(eqNode("120.00", 120))).toBe(true);
      expect(evaluateAST(eqNode("120", "120.0"))).toBe(true);
      expect(evaluateAST(neqNode("120", 130))).toBe(true);
      expect(evaluateAST(eqNode("120", 130))).toBe(false);
      expect(evaluateAST(neqNode("120", 120))).toBe(false);
    });

    it("handles ordered comparisons with numeric string coercion", () => {
      const cmpNode = (op, left, right) => ({
        type: "comparison",
        operator: op,
        operands: [
          { type: "constant", value: left },
          { type: "constant", value: right },
        ],
      });

      expect(evaluateAST(cmpNode("<", "100", 120))).toBe(true);
      expect(evaluateAST(cmpNode("<=", "120", "120"))).toBe(true);
      expect(evaluateAST(cmpNode(">", "150", 120))).toBe(true);
      expect(evaluateAST(cmpNode(">=", "120", 120))).toBe(true);
    });
  });

  describe("Kleene 3-Valued Logic for Non-Booleans", () => {
    it("treats non-boolean primitives as unknown (null)", () => {
      const notNode = (val) => ({
        type: "logical",
        operator: "not",
        operands: [{ type: "constant", value: val }],
      });

      const andNode = (vals) => ({
        type: "logical",
        operator: "and",
        operands: vals.map((v) => ({ type: "constant", value: v })),
      });

      const orNode = (vals) => ({
        type: "logical",
        operator: "or",
        operands: vals.map((v) => ({ type: "constant", value: v })),
      });

      expect(evaluateAST(notNode(true))).toBe(false);
      expect(evaluateAST(notNode(false))).toBe(true);
      expect(evaluateAST(notNode("hello"))).toBeNull();
      expect(evaluateAST(notNode(123))).toBeNull();
      expect(evaluateAST(notNode(null))).toBeNull();

      expect(evaluateAST(andNode([true, false, "string"]))).toBe(false);
      expect(evaluateAST(andNode([true, "string"]))).toBeNull();
      expect(evaluateAST(andNode([true, true]))).toBe(true);

      expect(evaluateAST(orNode([false, true, "string"]))).toBe(true);
      expect(evaluateAST(orNode([false, "string"]))).toBeNull();
      expect(evaluateAST(orNode([false, false]))).toBe(false);
    });
  });

  describe("New Backend Operators and Functions", () => {
    it("evaluates in and not_in", () => {
      const inNode = (target, opts) => ({
        type: "function",
        operator: "in",
        operands: [
          { type: "constant", value: target },
          ...opts.map((o) => ({ type: "constant", value: o })),
        ],
      });

      const notInNode = (target, opts) => ({
        type: "function",
        operator: "not_in",
        operands: [
          { type: "constant", value: target },
          ...opts.map((o) => ({ type: "constant", value: o })),
        ],
      });

      expect(evaluateAST(inNode("120", [100, 120, 140]))).toBe(true);
      expect(evaluateAST(inNode("150", [100, 120, 140]))).toBe(false);
      expect(evaluateAST(notInNode("150", [100, 120, 140]))).toBe(true);
      expect(evaluateAST(notInNode("120", [100, 120, 140]))).toBe(false);
    });

    it("evaluates between", () => {
      const node = (val, min, max) => ({
        type: "function",
        operator: "between",
        operands: [
          { type: "constant", value: val },
          { type: "constant", value: min },
          { type: "constant", value: max },
        ],
      });

      expect(evaluateAST(node("5", 1, 10))).toBe(true);
      expect(evaluateAST(node(12, 1, 10))).toBe(false);
      expect(evaluateAST(node("invalid", 1, 10))).toBe(false);
      expect(evaluateAST(node(null, 1, 10))).toBe(false);
    });

    it("evaluates regex_match safely", () => {
      const node = (val, pattern) => ({
        type: "function",
        operator: "regex_match",
        operands: [
          { type: "constant", value: val },
          { type: "constant", value: pattern },
        ],
      });

      expect(evaluateAST(node("PAT-1234", "^PAT-\\d+$"))).toBe(true);
      expect(evaluateAST(node("PAT-ABC", "^PAT-\\d+$"))).toBe(false);
      expect(evaluateAST(node(null, "^PAT-\\d+$"))).toBe(false);
      expect(evaluateAST(node("PAT-1234", "[unclosed"))).toBe(false);
    });

    it("evaluates xor", () => {
      const node = (a, b) => ({
        type: "logical",
        operator: "xor",
        operands: [
          { type: "constant", value: a },
          { type: "constant", value: b },
        ],
      });

      expect(evaluateAST(node(true, false))).toBe(true);
      expect(evaluateAST(node(false, true))).toBe(true);
      expect(evaluateAST(node(true, true))).toBe(false);
      expect(evaluateAST(node(false, false))).toBe(false);
      expect(evaluateAST(node("str", true))).toBeNull();
    });

    it("evaluates contains, starts_with, and ends_with", () => {
      const containsNode = (c, i) => ({
        type: "function",
        operator: "contains",
        operands: [
          { type: "constant", value: c },
          { type: "constant", value: i },
        ],
      });

      const startsWithNode = (s, p) => ({
        type: "function",
        operator: "starts_with",
        operands: [
          { type: "constant", value: s },
          { type: "constant", value: p },
        ],
      });

      const endsWithNode = (s, sf) => ({
        type: "function",
        operator: "ends_with",
        operands: [
          { type: "constant", value: s },
          { type: "constant", value: sf },
        ],
      });

      expect(evaluateAST(containsNode("Cadence Clinical", "Clinical"))).toBe(true);
      expect(evaluateAST(containsNode([10, "20", 30], 20))).toBe(true);
      expect(evaluateAST(containsNode(null, "test"))).toBe(false);

      expect(evaluateAST(startsWithNode("Cadence", "Cad"))).toBe(true);
      expect(evaluateAST(startsWithNode("Cadence", "den"))).toBe(false);
      expect(evaluateAST(startsWithNode(null, "Cad"))).toBe(false);

      expect(evaluateAST(endsWithNode("Cadence", "nce"))).toBe(true);
      expect(evaluateAST(endsWithNode("Cadence", "Cad"))).toBe(false);
      expect(evaluateAST(endsWithNode(null, "nce"))).toBe(false);
    });

    it("evaluates lower, upper, trim, and coalesce", () => {
      const fnNode = (fn, val) => ({
        type: "function",
        operator: fn,
        operands: [{ type: "constant", value: val }],
      });

      const coalesceNode = (vals) => ({
        type: "function",
        operator: "coalesce",
        operands: vals.map((v) => ({ type: "constant", value: v })),
      });

      expect(evaluateAST(fnNode("lower", "CADENCE"))).toBe("cadence");
      expect(evaluateAST(fnNode("lower", null))).toBeNull();

      expect(evaluateAST(fnNode("upper", "cadence"))).toBe("CADENCE");
      expect(evaluateAST(fnNode("upper", null))).toBeNull();

      expect(evaluateAST(fnNode("trim", "  cadence  "))).toBe("cadence");
      expect(evaluateAST(fnNode("trim", null))).toBeNull();

      expect(evaluateAST(coalesceNode([null, undefined, "first", "second"]))).toBe("first");
      expect(evaluateAST(coalesceNode([null, null]))).toBeNull();
    });
  });

  describe("HeadlessFormRenderer Violation Auto-Detection", () => {
    it("flags edit check violation when res is true and constraint violation when res is false", async () => {
      const formDefinition = {
        form_id: "form_vitals",
        version_tag: "1.0.0",
        data_schema: {
          name: "Vital Signs",
          variables: [
            { id: "vssbp", name: "Systolic BP", data_type: "number" },
            { id: "vsdpb", name: "Diastolic BP", data_type: "number" },
            { id: "height", name: "Height", data_type: "number" },
          ],
        },
        ui_schema: {
          sections: [
            {
              id: "sec1",
              title: "Vitals",
              columns: 2,
              widgets: [
                { variable_id: "vssbp", widget: "number_input" },
                { variable_id: "vsdpb", widget: "number_input" },
                { variable_id: "height", widget: "number_input" },
              ],
            },
          ],
        },
        logic_schema: {
          edit_checks: [
            {
              // Edit check: SBP < DBP error condition (res === true -> violation)
              target_field: "vssbp",
              query_message: "Systolic BP cannot be lower than Diastolic BP.",
              ast_expression: {
                type: "comparison",
                operator: "<",
                operands: [
                  { type: "field_ref", field_ref: { field_id: "vssbp" } },
                  { type: "field_ref", field_ref: { field_id: "vsdpb" } },
                ],
              },
            },
          ],
          constraints: [
            {
              // Validity constraint: Height > 0 (res === false -> violation)
              type: "constraint",
              target_field: "height",
              query_message: "Height must be greater than zero.",
              ast_expression: {
                type: "comparison",
                operator: ">",
                operands: [
                  { type: "field_ref", field_ref: { field_id: "height" } },
                  { type: "constant", value: 0 },
                ],
              },
            },
          ],
        },
      };

      const wrapper = mount(HeadlessFormRenderer, {
        props: {
          formDefinition,
          initialValues: {
            vssbp: 80,
            vsdpb: 100,
            height: -5,
          },
        },
      });

      // Both edit check (80 < 100 is true => violated) and constraint (-5 > 0 is false => violated) should trigger field errors
      expect(wrapper.vm.fieldErrors.vssbp).toBe("Systolic BP cannot be lower than Diastolic BP.");
      expect(wrapper.vm.fieldErrors.height).toBe("Height must be greater than zero.");

      // Fix values so neither rule is violated
      wrapper.vm.setFormData({
        vssbp: 120,
        vsdpb: 80,
        height: 1.75,
      });

      expect(wrapper.vm.fieldErrors.vssbp).toBeUndefined();
      expect(wrapper.vm.fieldErrors.height).toBeUndefined();
    });
  });
});
