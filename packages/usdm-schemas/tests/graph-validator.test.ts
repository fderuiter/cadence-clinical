import { describe, it, expect } from "vitest";
import {
  validateUsdmGraph,
  detectCycles,
  inspectExpressionAST,
} from "../src/graph-validator";

describe("USDM Standalone Graph Validation Engine", () => {
  it("successfully identifies a cyclic loop of three variables and returns the exact loop path", () => {
    // Construct 3 variables with skip-logic cyclic dependencies: varA -> varB -> varC -> varA
    const fields = [
      {
        id: "varA",
        label: "Variable A",
        relevant: {
          node_type: "OPERATOR",
          value: "==",
          children: [
            { node_type: "XPATH", value: "varB" },
            { node_type: "LITERAL", value: "YES" },
          ],
        },
      },
      {
        id: "varB",
        label: "Variable B",
        relevant: {
          node_type: "OPERATOR",
          value: "==",
          children: [
            { node_type: "XPATH", value: "varC" },
            { node_type: "LITERAL", value: "10" },
          ],
        },
      },
      {
        id: "varC",
        label: "Variable C",
        relevant: {
          node_type: "OPERATOR",
          value: "==",
          children: [
            { node_type: "XPATH", value: "varA" },
            { node_type: "LITERAL", value: "ACTIVE" },
          ],
        },
      },
    ];

    const result = validateUsdmGraph({ ecrfFields: fields });

    expect(result.valid).toBe(false);
    expect(result.cyclePath).toBeDefined();
    expect(result.cyclePath).toEqual(["varA", "varB", "varC", "varA"]);

    const cycleError = result.errors.find((e) => e.code === "CYCLE_DETECTED");
    expect(cycleError).toBeDefined();
    expect(cycleError?.path).toEqual(["varA", "varB", "varC", "varA"]);
    expect(cycleError?.message).toContain("varA -> varB -> varC -> varA");
  });

  it("restricts math and logic operations in rules to predefined operator whitelist", () => {
    const validAst = {
      node_type: "OPERATOR",
      value: "and",
      children: [
        {
          node_type: "OPERATOR",
          value: ">",
          children: [
            { node_type: "XPATH", value: "sys_bp" },
            { node_type: "LITERAL", value: 140 },
          ],
        },
        {
          node_type: "FUNCTION",
          value: "is_not_empty",
          children: [{ node_type: "XPATH", value: "dia_bp" }],
        },
      ],
    };

    const validInsp = inspectExpressionAST(validAst);
    expect(validInsp.unwhitelistedOps).toHaveLength(0);

    const invalidAst = {
      node_type: "FUNCTION",
      value: "random", // stochastic / non-deterministic
      children: [{ node_type: "LITERAL", value: 100 }],
    };

    const invalidInsp = inspectExpressionAST(invalidAst);
    expect(invalidInsp.unwhitelistedOps).toContain("random");

    const studyWithUnwhitelisted = {
      ecrfFields: [
        {
          id: "field_stochastic",
          relevant: invalidAst,
        },
      ],
    };

    const res = validateUsdmGraph(studyWithUnwhitelisted);
    expect(res.valid).toBe(false);
    expect(
      res.errors.some((e) => e.code === "UNWHITELISTED_OPERATOR")
    ).toBe(true);
  });

  it("dynamically verifies that referenced components exist within current study projection", () => {
    const studyProjection = {
      encounters: [{ id: "V-101", name: "Baseline Visit" }],
      activities: [{ id: "ACT-01", name: "Blood Draw" }],
      ecrfFields: [{ id: "age", label: "Age" }],
    };

    // Valid rule/field referencing existing age
    const validResult = validateUsdmGraph(studyProjection, {
      fields: [
        {
          id: "pediatric_flag",
          relevant: {
            node_type: "OPERATOR",
            value: "<",
            children: [
              { node_type: "XPATH", value: "age" },
              { node_type: "LITERAL", value: 18 },
            ],
          },
        },
      ],
    });
    expect(validResult.valid).toBe(true);

    // Invalid rule referencing non-existent field 'missing_var'
    const invalidRefResult = validateUsdmGraph(studyProjection, {
      fields: [
        {
          id: "pediatric_flag",
          relevant: {
            node_type: "OPERATOR",
            value: "<",
            children: [
              { node_type: "XPATH", value: "missing_var" },
              { node_type: "LITERAL", value: 18 },
            ],
          },
        },
      ],
    });

    expect(invalidRefResult.valid).toBe(false);
    expect(
      invalidRefResult.errors.some(
        (e) =>
          e.code === "MISSING_REFERENCE" && e.referencedId === "missing_var"
      )
    ).toBe(true);

    // Invalid activity referencing non-existent encounter
    const invalidActResult = validateUsdmGraph({
      encounters: [{ id: "V-101" }],
      activities: [{ id: "ACT-01", assignedEncounterIds: ["V-999"] }],
    });

    expect(invalidActResult.valid).toBe(false);
    expect(
      invalidActResult.errors.some(
        (e) => e.code === "MISSING_REFERENCE" && e.referencedId === "V-999"
      )
    ).toBe(true);
  });

  it("executes graph validation on a 100-node study graph in under 50ms", () => {
    const fields: any[] = [];
    const encounters: any[] = [];

    for (let i = 0; i < 50; i++) {
      encounters.push({ id: `V-${i}`, name: `Visit ${i}` });
    }

    for (let i = 0; i < 100; i++) {
      fields.push({
        id: `field_${i}`,
        relevant:
          i > 0
            ? {
                node_type: "OPERATOR",
                value: "==",
                children: [
                  { node_type: "XPATH", value: `field_${i - 1}` },
                  { node_type: "LITERAL", value: "VAL" },
                ],
              }
            : null,
      });
    }

    const start = performance.now();
    const result = validateUsdmGraph({ encounters, ecrfFields: fields });
    const duration = performance.now() - start;

    expect(result.valid).toBe(true);
    expect(duration).toBeLessThan(50);
  });
});
