/**
 * Standalone TypeScript USDM Graph Validation Engine.
 *
 * Performs:
 * 1. AST Expression Traversal & Whitelist Operator Validation (blocking non-deterministic/stochastic operations)
 * 2. Dynamic Reference Verification (verifying existence of referenced visits, fields, activities, etc.)
 * 3. Cyclic Dependency Detection in skip-logic using Parent-Pointer Path Reconstruction
 */

export const ALLOWED_OPERATORS = new Set([
  // Logical
  "and",
  "or",
  "not",
  // Comparison
  "==",
  "!=",
  "<",
  "<=",
  ">",
  ">=",
  // Arithmetic
  "+",
  "-",
  "*",
  "/",
  // Deterministic functions
  "is_empty",
  "empty",
  "is_not_empty",
  "indexed-repeat",
  "min",
  "max",
  "sum",
  "count",
  "abs",
  "round",
]);

export interface GraphValidationError {
  code: "CYCLE_DETECTED" | "MISSING_REFERENCE" | "UNWHITELISTED_OPERATOR" | "STRUCTURAL_ERROR";
  message: string;
  path?: string[];
  referencedId?: string;
  referencedType?: string;
  operator?: string;
}

export interface GraphValidationResult {
  valid: boolean;
  errors: GraphValidationError[];
  cyclePath?: string[];
}

/**
 * Traverses an AST expression node to extract referenced variable/field IDs
 * and check for unwhitelisted or stochastic operators.
 */
export function inspectExpressionAST(
  node: any,
  allowedOperators: Set<string> = ALLOWED_OPERATORS
): { references: string[]; unwhitelistedOps: string[] } {
  const references: string[] = [];
  const unwhitelistedOps: string[] = [];

  if (!node || typeof node !== "object") {
    return { references, unwhitelistedOps };
  }

  function traverse(curr: any) {
    if (!curr || typeof curr !== "object") return;

    const type = curr.node_type || curr.type || "";
    const valAttr = curr.value;
    const rawChildren = curr.children || curr.operands || [];
    const fieldRef = curr.field_ref;

    // Field reference extraction
    if (type === "XPATH" || type === "field_ref" || type === "variable") {
      let path = "";
      if (fieldRef && fieldRef.field_id) {
        path = fieldRef.field_id;
      } else if (typeof valAttr === "string") {
        path = valAttr;
      }
      if (path) {
        const bareField = path.split("/").pop();
        if (bareField) references.push(bareField);
      }
    }

    // Direct field_ref on node
    if (fieldRef && fieldRef.field_id && type !== "XPATH" && type !== "field_ref") {
      const bareField = fieldRef.field_id.split("/").pop();
      if (bareField) references.push(bareField);
    }

    // Check operators/functions
    if (
      type === "OPERATOR" ||
      type === "logical" ||
      type === "comparison" ||
      type === "FUNCTION" ||
      type === "function" ||
      type === "operator"
    ) {
      const op = (typeof valAttr === "string" ? valAttr : curr.operator || "").toLowerCase();
      if (op) {
        if (!allowedOperators.has(op)) {
          unwhitelistedOps.push(op);
        }
      }
    }

    // Traverse children
    if (Array.isArray(rawChildren)) {
      for (const child of rawChildren) {
        traverse(child);
      }
    }
  }

  traverse(node);
  return { references, unwhitelistedOps };
}

/**
 * Detects cycles in a directed dependency graph using Parent-Pointer Path Reconstruction.
 */
export function detectCycles(dependencyGraph: Map<string, Set<string>>): {
  hasCycle: boolean;
  cyclePath?: string[];
  error?: GraphValidationError;
} {
  const visited = new Set<string>();
  const inStack = new Set<string>();
  const parentMap = new Map<string, string>();

  for (const node of dependencyGraph.keys()) {
    if (visited.has(node)) continue;

    function dfs(u: string): string[] | null {
      visited.add(u);
      inStack.add(u);

      const neighbors = dependencyGraph.get(u) || new Set();
      for (const v of neighbors) {
        if (inStack.has(v)) {
          // Parent-Pointer Path Reconstruction
          const cyclePath: string[] = [v];
          let curr: string | undefined = u;
          while (curr && curr !== v) {
            cyclePath.unshift(curr);
            curr = parentMap.get(curr);
          }
          cyclePath.unshift(v); // Complete the loop v -> ... -> u -> v
          return cyclePath;
        }

        if (!visited.has(v)) {
          parentMap.set(v, u);
          const cycle = dfs(v);
          if (cycle) return cycle;
        }
      }

      inStack.delete(u);
      return null;
    }

    const cycle = dfs(node);
    if (cycle) {
      return {
        hasCycle: true,
        cyclePath: cycle,
        error: {
          code: "CYCLE_DETECTED",
          message: `Cyclic skip-logic dependency detected: ${cycle.join(" -> ")}`,
          path: cycle,
        },
      };
    }
  }

  return { hasCycle: false };
}

/**
 * Validates USDM study graph payload or study projection.
 */
export function validateUsdmGraph(
  studyProjection: any,
  options: {
    fields?: any[];
    rules?: any[];
    projectionContext?: any;
  } = {}
): GraphValidationResult {
  const errors: GraphValidationError[] = [];
  const dependencyGraph = new Map<string, Set<string>>();

  // Extract components from studyProjection or options
  const root = studyProjection || {};
  const studyDesign = Array.isArray(root.studyDesigns) ? root.studyDesigns[0] || {} : root;

  // Gather existing component IDs across studyProjection, options, and projectionContext
  const encounters = [
    ...(options.projectionContext?.encounters || []),
    ...(root.encounters || []),
    ...(root.visits || []),
    ...(studyDesign.encounters || []),
    ...(root.epochId ? [root] : []),
  ];
  const activities = [
    ...(options.projectionContext?.activities || []),
    ...(root.activities || []),
    ...(root.procedures || []),
    ...(studyDesign.activities || []),
    ...(root.assignedEncounterIds || root.cells ? [root] : []),
  ];
  const arms = [
    ...(options.projectionContext?.arms || []),
    ...(root.arms || []),
    ...(studyDesign.arms || []),
  ];
  const epochs = [
    ...(options.projectionContext?.epochs || []),
    ...(root.epochs || []),
    ...(studyDesign.epochs || []),
  ];
  const fields = [
    ...(options.fields || []),
    ...(options.projectionContext?.fields || []),
    ...(root.ecrfFields || []),
    ...(root.fields || []),
    ...(studyDesign.fields || []),
  ];
  const rules = [
    ...(options.rules || []),
    ...(options.projectionContext?.rules || []),
    ...(root.rules || []),
  ];

  const existingVisitIds = new Set<string>();
  const existingActivityIds = new Set<string>();
  const existingArmIds = new Set<string>();
  const existingEpochIds = new Set<string>();
  const existingFieldIds = new Set<string>();

  for (const e of encounters) {
    if (e && e.id) existingVisitIds.add(e.id);
  }
  for (const a of activities) {
    if (a && a.id) existingActivityIds.add(a.id);
  }
  for (const arm of arms) {
    if (arm && arm.id) existingArmIds.add(arm.id);
  }
  for (const ep of epochs) {
    if (ep && ep.id) existingEpochIds.add(ep.id);
  }
  for (const f of fields) {
    if (f && f.id) existingFieldIds.add(f.id);
  }

  // Also include Biomedical Concepts if present
  const bcs = studyDesign.biomedicalConcepts || root.biomedicalConcepts || [];
  for (const bc of bcs) {
    if (bc && bc.id) existingFieldIds.add(bc.id);
    if (bc && Array.isArray(bc.properties)) {
      for (const prop of bc.properties) {
        if (prop && prop.id) existingFieldIds.add(prop.id);
      }
    }
  }

  // Build field dependency graph & inspect expressions
  // 1. Inspect Fields with `relevant` or `constraint` expressions
  for (const field of fields) {
    if (!field) continue;
    const fieldId = field.id;
    if (!fieldId) continue;
    if (!dependencyGraph.has(fieldId)) {
      dependencyGraph.set(fieldId, new Set());
    }

    const asts = [
      field.relevant,
      field.constraint,
      field.textExpression,
      field.logicalExpression,
    ].filter(Boolean);
    for (const ast of asts) {
      const exprAst = typeof ast === "string" ? null : ast;
      if (exprAst && typeof exprAst === "object") {
        const { references, unwhitelistedOps } = inspectExpressionAST(exprAst);

        for (const op of unwhitelistedOps) {
          errors.push({
            code: "UNWHITELISTED_OPERATOR",
            message: `Unwhitelisted operator or non-deterministic function '${op}' in field '${fieldId}'`,
            operator: op,
          });
        }

        for (const refId of references) {
          dependencyGraph.get(fieldId)!.add(refId);

          if (
            existingFieldIds.size > 0 &&
            !existingFieldIds.has(refId) &&
            !existingVisitIds.has(refId)
          ) {
            errors.push({
              code: "MISSING_REFERENCE",
              message: `Field '${fieldId}' references non-existent component '${refId}'`,
              referencedId: refId,
            });
          }
        }
      }
    }
  }

  // 2. Inspect Explicit Rules (if rules array provided or payload is a rule mutation)
  for (const rule of rules) {
    if (!rule) continue;
    const ruleId = rule.id || rule.targetFieldId;
    const targetId = rule.targetFieldId || rule.targetId || ruleId;

    if (targetId && !dependencyGraph.has(targetId)) {
      dependencyGraph.set(targetId, new Set());
    }

    const ast = rule.ast || rule.expression || rule.relevant || rule.logicalExpression;
    if (ast && typeof ast === "object") {
      const { references, unwhitelistedOps } = inspectExpressionAST(ast);
      for (const op of unwhitelistedOps) {
        errors.push({
          code: "UNWHITELISTED_OPERATOR",
          message: `Unwhitelisted operator or non-deterministic function '${op}' in rule '${
            ruleId || "unknown"
          }'`,
          operator: op,
        });
      }

      for (const refId of references) {
        if (targetId) dependencyGraph.get(targetId)!.add(refId);

        if (
          existingFieldIds.size > 0 &&
          !existingFieldIds.has(refId) &&
          !existingVisitIds.has(refId)
        ) {
          errors.push({
            code: "MISSING_REFERENCE",
            message: `Rule '${ruleId || "unknown"}' references non-existent component '${refId}'`,
            referencedId: refId,
          });
        }
      }
    }
  }

  // 3. Verify direct references in payload / USDM entities
  // Check payload for referencedVisitId / referencedActivityId if root is a mutation payload
  if (root.referencedVisitId && existingVisitIds.size > 0 && !existingVisitIds.has(root.referencedVisitId)) {
    errors.push({
      code: "MISSING_REFERENCE",
      message: `Payload references non-existent visit ID '${root.referencedVisitId}'`,
      referencedId: root.referencedVisitId,
      referencedType: "visit",
    });
  }
  if (root.referencedActivityId && existingActivityIds.size > 0 && !existingActivityIds.has(root.referencedActivityId)) {
    errors.push({
      code: "MISSING_REFERENCE",
      message: `Payload references non-existent activity ID '${root.referencedActivityId}'`,
      referencedId: root.referencedActivityId,
      referencedType: "activity",
    });
  }

  // Check Encounters epoch references
  for (const enc of encounters) {
    if (enc && enc.epochId && existingEpochIds.size > 0 && !existingEpochIds.has(enc.epochId)) {
      errors.push({
        code: "MISSING_REFERENCE",
        message: `Encounter '${enc.id || enc.name}' references non-existent epoch '${enc.epochId}'`,
        referencedId: enc.epochId,
        referencedType: "epoch",
      });
    }
  }

  // Check Activities assignedEncounterIds / cells
  for (const act of activities) {
    if (!act) continue;
    if (Array.isArray(act.assignedEncounterIds)) {
      for (const encId of act.assignedEncounterIds) {
        if (existingVisitIds.size > 0 && !existingVisitIds.has(encId)) {
          errors.push({
            code: "MISSING_REFERENCE",
            message: `Activity '${
              act.id || act.name
            }' references non-existent encounter '${encId}'`,
            referencedId: encId,
            referencedType: "encounter",
          });
        }
      }
    }

    if (Array.isArray(act.cells)) {
      for (const cell of act.cells) {
        if (
          cell &&
          cell.encounter_id &&
          existingVisitIds.size > 0 &&
          !existingVisitIds.has(cell.encounter_id)
        ) {
          errors.push({
            code: "MISSING_REFERENCE",
            message: `Activity cell references non-existent encounter '${cell.encounter_id}'`,
            referencedId: cell.encounter_id,
            referencedType: "encounter",
          });
        }
      }
    }
  }

  // 4. Cycle Detection
  const cycleResult = detectCycles(dependencyGraph);
  if (cycleResult.hasCycle && cycleResult.error) {
    errors.unshift(cycleResult.error);
  }

  return {
    valid: errors.length === 0,
    errors,
    cyclePath: cycleResult.cyclePath,
  };
}
