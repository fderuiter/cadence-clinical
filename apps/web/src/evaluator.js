/**
 * Client-side AST evaluator for clinical rules (relevant skip logic and constraints).
 */
import { validateField as uiValidateField } from "ui";

class SimpleLRUCache {
  constructor(capacity = 200) {
    this.capacity = capacity;
    this.cache = new Map();
  }

  get(key) {
    if (!this.cache.has(key)) return undefined;
    const value = this.cache.get(key);
    this.cache.delete(key);
    this.cache.set(key, value);
    return value;
  }

  put(key, value) {
    if (this.cache.has(key)) {
      this.cache.delete(key);
    } else if (this.cache.size >= this.capacity) {
      const firstKey = this.cache.keys().next().value;
      this.cache.delete(firstKey);
    }
    this.cache.set(key, value);
  }

  has(key) {
    return this.cache.has(key);
  }
}

// Global initialization of compiler Cache
export const compilerCache = new SimpleLRUCache(200);

/**
 * Resolves a potentially relative path.
 *
 * @param {string} path
 * @returns {string} Absolute representation
 */
export function resolveRelativePath(path) {
  if (typeof path !== "string") return path;
  if (path.startsWith("../")) {
    return "/clinical_data/subject/" + path.substring(3);
  }
  return path;
}

function isNumericValue(v) {
  if (v === null || v === undefined) return false;
  if (typeof v === "number") return !isNaN(v);
  if (typeof v === "string") {
    const trimmed = v.trim();
    return trimmed !== "" && !isNaN(Number(trimmed));
  }
  return false;
}

function toNumericValue(v) {
  return typeof v === "number" ? v : Number(v);
}

function compareEqual(a, b) {
  const aNull = a === null || a === undefined;
  const bNull = b === null || b === undefined;
  if (aNull && bNull) return true;
  if (aNull || bNull) return false;

  if (isNumericValue(a) && isNumericValue(b)) {
    return toNumericValue(a) === toNumericValue(b);
  }
  if (typeof a === "boolean" || typeof b === "boolean") {
    return typeof a === "boolean" && typeof b === "boolean" && a === b;
  }
  return String(a) === String(b);
}

/**
 * Deterministically evaluates an ExpressionNode AST against the given data context.
 *
 * Supports both:
 * Style A (Pydantic / Designer rules): type, operator, operands, value, field_ref
 * Style B (TDD / XForms): node_type, value, children
 *
 * @param {Object} node - AST ExpressionNode
 * @param {Object} context - Flat or nested data object (field_id -> value)
 * @param {Object} currentIndices - Map of repeat group indices
 * @returns {*} Evaluation result
 */
export function evaluateAST(node, context = {}, currentIndices = {}) {
  if (!node) return null;

  // Normalize fields between Style A and Style B
  const type = node.node_type || node.type || "";
  const valAttr = node.value;
  const rawChildren = node.children || node.operands || node.args || [];
  const fieldRef = node.field_ref;

  // 1. LITERAL / CONSTANT
  if (type === "LITERAL" || type === "constant") {
    return valAttr !== undefined ? valAttr : null;
  }

  // 2. XPATH / FIELD_REF
  if (type === "XPATH" || type === "field_ref") {
    let path = "";
    if (fieldRef && fieldRef.field_id) {
      path = fieldRef.field_id;
    } else if (typeof valAttr === "string") {
      path = valAttr;
    }

    if (!path) return null;

    // Direct lookup
    if (context[path] !== undefined) {
      return context[path];
    }

    // Relative path translation
    const resolvedPath = resolveRelativePath(path);
    if (context[resolvedPath] !== undefined) {
      return context[resolvedPath];
    }

    // Extract bare field name from path if needed (e.g. /clinical_data/.../vssbp -> vssbp)
    const bareField = path.split("/").pop();
    if (context[bareField] !== undefined) {
      return context[bareField];
    }

    return null;
  }

  // 3. OPERATOR / LOGICAL / COMPARISON / FUNCTION
  if (
    type === "OPERATOR" ||
    type === "logical" ||
    type === "comparison" ||
    type === "FUNCTION" ||
    type === "function"
  ) {
    const name = (
      typeof valAttr === "string" ? valAttr : node.operator || ""
    ).toLowerCase();

    // Logical operators (Kleene logic)
    if (name === "not" || name === "!") {
      if (rawChildren.length < 1) return null;
      const childVal = evaluateAST(rawChildren[0], context, currentIndices);
      if (typeof childVal === "boolean") {
        return !childVal;
      }
      return null;
    }

    if (name === "and" || name === "&&") {
      let hasUnknown = false;
      for (const child of rawChildren) {
        const childVal = evaluateAST(child, context, currentIndices);
        if (typeof childVal === "boolean") {
          if (!childVal) return false;
        } else {
          hasUnknown = true;
        }
      }
      return hasUnknown ? null : true;
    }

    if (name === "or" || name === "||") {
      let hasUnknown = false;
      for (const child of rawChildren) {
        const childVal = evaluateAST(child, context, currentIndices);
        if (typeof childVal === "boolean") {
          if (childVal) return true;
        } else {
          hasUnknown = true;
        }
      }
      return hasUnknown ? null : false;
    }

    if (name === "xor") {
      if (rawChildren.length < 2) return null;
      const leftVal = evaluateAST(rawChildren[0], context, currentIndices);
      const rightVal = evaluateAST(rawChildren[1], context, currentIndices);
      if (typeof leftVal !== "boolean" || typeof rightVal !== "boolean") {
        return null;
      }
      return leftVal !== rightVal;
    }

    // Arithmetic operators
    if (["+", "-", "*", "/"].includes(name)) {
      if (rawChildren.length < 2) return null;
      const leftVal = evaluateAST(rawChildren[0], context, currentIndices);
      const rightVal = evaluateAST(rawChildren[1], context, currentIndices);

      if (!isNumericValue(leftVal) || !isNumericValue(rightVal)) {
        return null;
      }
      const lNum = toNumericValue(leftVal);
      const rNum = toNumericValue(rightVal);

      if (name === "+") return lNum + rNum;
      if (name === "-") return lNum - rNum;
      if (name === "*") return lNum * rNum;
      if (name === "/") {
        if (rNum === 0) return null; // Safe division by zero
        return lNum / rNum;
      }
    }

    // Comparison operators (null-safe and coercion-enabled)
    if (["==", "!=", "<", "<=", ">", ">="].includes(name)) {
      if (rawChildren.length < 2) return null;
      const leftVal = evaluateAST(rawChildren[0], context, currentIndices);
      const rightVal = evaluateAST(rawChildren[1], context, currentIndices);

      const leftNull = leftVal === null || leftVal === undefined;
      const rightNull = rightVal === null || rightVal === undefined;

      if (leftNull && rightNull) {
        if (name === "==") return true;
        if (name === "!=") return false;
        return false;
      }
      if (leftNull || rightNull) {
        if (name === "==") return false;
        if (name === "!=") return true;
        return false;
      }

      if (isNumericValue(leftVal) && isNumericValue(rightVal)) {
        const l = toNumericValue(leftVal);
        const r = toNumericValue(rightVal);
        if (name === "==") return l === r;
        if (name === "!=") return l !== r;
        if (name === "<") return l < r;
        if (name === "<=") return l <= r;
        if (name === ">") return l > r;
        if (name === ">=") return l >= r;
      }

      if (typeof leftVal === "boolean" || typeof rightVal === "boolean") {
        if (typeof leftVal === "boolean" && typeof rightVal === "boolean") {
          if (name === "==") return leftVal === rightVal;
          if (name === "!=") return leftVal !== rightVal;
          return false;
        }
        if (name === "==") return false;
        if (name === "!=") return true;
        return false;
      }

      const lStr = String(leftVal);
      const rStr = String(rightVal);
      if (name === "==") return lStr === rStr;
      if (name === "!=") return lStr !== rStr;
      if (name === "<") return lStr < rStr;
      if (name === "<=") return lStr <= rStr;
      if (name === ">") return lStr > rStr;
      if (name === ">=") return lStr >= rStr;
    }

    // In / Not In
    if (name === "in" || name === "not_in") {
      if (rawChildren.length < 2) return false;
      const targetVal = evaluateAST(rawChildren[0], context, currentIndices);
      let options;
      if (rawChildren.length === 2) {
        const secondVal = evaluateAST(rawChildren[1], context, currentIndices);
        if (Array.isArray(secondVal)) {
          options = secondVal;
        } else {
          options = [secondVal];
        }
      } else {
        options = rawChildren
          .slice(1)
          .map((c) => evaluateAST(c, context, currentIndices));
      }

      let found = false;
      for (const opt of options) {
        if (compareEqual(targetVal, opt)) {
          found = true;
          break;
        }
      }
      return name === "in" ? found : !found;
    }

    // Between
    if (name === "between") {
      if (rawChildren.length < 3) return false;
      const val = evaluateAST(rawChildren[0], context, currentIndices);
      const min = evaluateAST(rawChildren[1], context, currentIndices);
      const max = evaluateAST(rawChildren[2], context, currentIndices);

      if (
        !isNumericValue(val) ||
        !isNumericValue(min) ||
        !isNumericValue(max)
      ) {
        return false;
      }
      const nVal = toNumericValue(val);
      const nMin = toNumericValue(min);
      const nMax = toNumericValue(max);
      return nVal >= nMin && nVal <= nMax;
    }

    // Regex match
    if (name === "regex_match") {
      if (rawChildren.length < 2) return false;
      const val = evaluateAST(rawChildren[0], context, currentIndices);
      const pattern = evaluateAST(rawChildren[1], context, currentIndices);

      if (
        val === null ||
        val === undefined ||
        pattern === null ||
        pattern === undefined
      ) {
        return false;
      }
      try {
        const regex = new RegExp(String(pattern));
        return regex.test(String(val));
      } catch {
        return false;
      }
    }

    // Contains
    if (name === "contains") {
      if (rawChildren.length < 2) return false;
      const container = evaluateAST(rawChildren[0], context, currentIndices);
      const item = evaluateAST(rawChildren[1], context, currentIndices);

      if (
        container === null ||
        container === undefined ||
        item === null ||
        item === undefined
      ) {
        return false;
      }
      if (Array.isArray(container)) {
        return container.some((elem) => compareEqual(elem, item));
      }
      return String(container).includes(String(item));
    }

    // Starts with / Ends with
    if (name === "starts_with") {
      if (rawChildren.length < 2) return false;
      const str = evaluateAST(rawChildren[0], context, currentIndices);
      const prefix = evaluateAST(rawChildren[1], context, currentIndices);
      if (
        str === null ||
        str === undefined ||
        prefix === null ||
        prefix === undefined
      ) {
        return false;
      }
      return String(str).startsWith(String(prefix));
    }

    if (name === "ends_with") {
      if (rawChildren.length < 2) return false;
      const str = evaluateAST(rawChildren[0], context, currentIndices);
      const suffix = evaluateAST(rawChildren[1], context, currentIndices);
      if (
        str === null ||
        str === undefined ||
        suffix === null ||
        suffix === undefined
      ) {
        return false;
      }
      return String(str).endsWith(String(suffix));
    }

    // String manipulation: lower, upper, trim
    if (name === "lower") {
      if (rawChildren.length < 1) return null;
      const str = evaluateAST(rawChildren[0], context, currentIndices);
      if (str === null || str === undefined) return null;
      return String(str).toLowerCase();
    }

    if (name === "upper") {
      if (rawChildren.length < 1) return null;
      const str = evaluateAST(rawChildren[0], context, currentIndices);
      if (str === null || str === undefined) return null;
      return String(str).toUpperCase();
    }

    if (name === "trim") {
      if (rawChildren.length < 1) return null;
      const str = evaluateAST(rawChildren[0], context, currentIndices);
      if (str === null || str === undefined) return null;
      return String(str).trim();
    }

    // Coalesce
    if (name === "coalesce") {
      for (const child of rawChildren) {
        const val = evaluateAST(child, context, currentIndices);
        if (val !== null && val !== undefined) {
          return val;
        }
      }
      return null;
    }

    // Null/empty functions
    if (name === "is_empty" || name === "empty") {
      if (rawChildren.length !== 1) return null;
      const childVal = evaluateAST(rawChildren[0], context, currentIndices);
      return (
        childVal === null ||
        childVal === undefined ||
        String(childVal).trim() === ""
      );
    }

    if (name === "is_not_empty") {
      if (rawChildren.length !== 1) return null;
      const childVal = evaluateAST(rawChildren[0], context, currentIndices);
      return (
        childVal !== null &&
        childVal !== undefined &&
        String(childVal).trim() !== ""
      );
    }

    // Indexed repeat
    if (name === "indexed-repeat") {
      if (rawChildren.length !== 3) return null;
      const targetFieldNode = rawChildren[0];
      const repeatGroupNode = rawChildren[1];
      const indexNode = rawChildren[2];

      const targetPath =
        targetFieldNode.value ||
        (targetFieldNode.field_ref && targetFieldNode.field_ref.field_id) ||
        "";
      const repeatGroup =
        repeatGroupNode.value ||
        (repeatGroupNode.field_ref && repeatGroupNode.field_ref.field_id) ||
        "";
      const indexVal = evaluateAST(indexNode, context, currentIndices);

      const targetIndex = parseInt(indexVal, 10);
      if (isNaN(targetIndex)) return null;

      const fieldName = targetPath.split("/").pop();
      const indexedPath = `${repeatGroup}[${targetIndex}]/${fieldName}`;

      return context[indexedPath] !== undefined ? context[indexedPath] : null;
    }

    // Aggregates
    if (["sum", "avg", "min", "max", "count"].includes(name)) {
      const vals = rawChildren
        .map((c) => evaluateAST(c, context, currentIndices))
        .filter((v) => v !== null && v !== undefined && v !== "");
      if (name === "count") return vals.length;
      const nums = vals.map((v) => parseFloat(v)).filter((n) => !isNaN(n));
      if (nums.length === 0) return null;
      if (name === "sum") return nums.reduce((acc, curr) => acc + curr, 0);
      if (name === "avg")
        return nums.reduce((acc, curr) => acc + curr, 0) / nums.length;
      if (name === "min") return Math.min(...nums);
      if (name === "max") return Math.max(...nums);
    }

    return null;
  }

  return null;
}

/**
 * Compiled cached expression wrapper.
 * Returns a fast function that uses the compiled AST.
 *
 * @param {Object} node - AST ExpressionNode
 * @returns {Function} Function of form (context, currentIndices) => value
 */
export function getCompiledExpression(node) {
  if (!node) return () => null;
  const cacheKey = JSON.stringify(node);
  let compiled = compilerCache.get(cacheKey);

  if (!compiled) {
    compiled = (context, currentIndices) =>
      evaluateAST(node, context, currentIndices);
    compilerCache.put(cacheKey, compiled);
  }

  return compiled;
}

/**
 * Pure evaluation function for dynamic forms.
 * Determines field visibility, performs cascading dependent nullification,
 * and tracks field values and audit logs.
 *
 * @param {Array} fields - Array of field metadata objects
 * @param {Object} currentValues - Current state of form values
 * @param {Function} logPurge - Callback function to log field purges in the audit trail
 * @returns {Object} { visibleFields, updatedValues }
 */
export function renderFormFromJSON(fields, currentValues, logPurge = null) {
  const updatedValues = { ...currentValues };
  const visibleFields = {};

  let changed = true;
  let passes = 0;
  // Support cascading up to 10 levels deep
  while (changed && passes < 10) {
    changed = false;
    passes++;

    for (const field of fields) {
      const isRelevant = field.relevant
        ? !Object.is(evaluateAST(field.relevant, updatedValues), false)
        : true;

      const wasVisible = !Object.is(visibleFields[field.id], false);
      if (visibleFields[field.id] === undefined || isRelevant !== wasVisible) {
        visibleFields[field.id] = isRelevant;
        changed = true;
      }

      if (!isRelevant) {
        const val = updatedValues[field.id];
        if (val !== undefined && val !== "" && val !== null) {
          updatedValues[field.id] = "";
          if (logPurge) {
            logPurge(
              field.id,
              val,
              "System-initiated purge of inactive child variable due to parent value mutation"
            );
          }
          changed = true;
        }
      }
    }
  }

  return { visibleFields, updatedValues };
}

/**
 * Wrapper for field validation that supports AST constraints.
 *
 * @param {Object} fieldMeta - Field metadata configuration
 * @param {any} val - Field value
 * @param {Object} context - Optional form context for constraint evaluation
 * @returns {Object} { valid: boolean, message: string }
 */
export function validateField(fieldMeta, val, context = {}) {
  return uiValidateField(fieldMeta, val, context, evaluateAST);
}

/**
 * Traces AST evaluation recursively and produces a detailed execution tree with
 * intermediate truth values, operand evaluations, and referenced field dependencies.
 *
 * @param {Object} node - AST ExpressionNode
 * @param {Object} context - Data payload (field_id -> value)
 * @param {Object} currentIndices - Map of repeat indices
 * @returns {Object} Trace node { type, val, operator, children, description, referencedFields }
 */
export function traceASTEvaluation(node, context = {}, currentIndices = {}) {
  if (!node) {
    return {
      type: "null",
      val: null,
      description: "Empty node",
      referencedFields: [],
    };
  }

  const type = node.node_type || node.type || "";
  const valAttr = node.value;
  const rawChildren = node.children || node.operands || [];
  const fieldRef = node.field_ref;

  // 1. LITERAL / CONSTANT
  if (type === "LITERAL" || type === "constant") {
    const val = valAttr !== undefined ? valAttr : null;
    return {
      type: "constant",
      val,
      description: `Constant: ${JSON.stringify(val)}`,
      referencedFields: [],
    };
  }

  // 2. XPATH / FIELD_REF
  if (type === "XPATH" || type === "field_ref") {
    let fieldId = "";
    if (fieldRef && fieldRef.field_id) {
      fieldId = fieldRef.field_id;
    } else if (typeof valAttr === "string") {
      fieldId = valAttr;
    }

    const val = evaluateAST(node, context, currentIndices);
    return {
      type: "field_ref",
      fieldId,
      val,
      description: `Field '${fieldId}': ${val === null || val === undefined ? "(empty)" : JSON.stringify(val)}`,
      referencedFields: fieldId ? [fieldId] : [],
    };
  }

  // 3. OPERATOR / LOGICAL / COMPARISON
  if (type === "OPERATOR" || type === "logical" || type === "comparison") {
    const operator = (
      typeof valAttr === "string" ? valAttr : node.operator || ""
    ).toLowerCase();

    const childrenTraces = rawChildren.map((c) =>
      traceASTEvaluation(c, context, currentIndices)
    );
    const referencedFields = [
      ...new Set(childrenTraces.flatMap((t) => t.referencedFields || [])),
    ];
    const val = evaluateAST(node, context, currentIndices);

    return {
      type: type === "logical" ? "logical" : "comparison",
      operator,
      val,
      children: childrenTraces,
      description: `${operator.toUpperCase()} [${childrenTraces.map((c) => c.val).join(", ")}] => ${val}`,
      referencedFields,
    };
  }

  // 4. FUNCTION
  if (type === "FUNCTION" || type === "function") {
    const operator = (
      typeof valAttr === "string" ? valAttr : node.operator || ""
    ).toLowerCase();

    const childrenTraces = rawChildren.map((c) =>
      traceASTEvaluation(c, context, currentIndices)
    );
    const referencedFields = [
      ...new Set(childrenTraces.flatMap((t) => t.referencedFields || [])),
    ];
    const val = evaluateAST(node, context, currentIndices);

    return {
      type: "function",
      operator,
      val,
      children: childrenTraces,
      description: `fn:${operator}(${childrenTraces.map((c) => c.val).join(", ")}) => ${val}`,
      referencedFields,
    };
  }

  const val = evaluateAST(node, context, currentIndices);
  return {
    type: "unknown",
    val,
    description: `Evaluated => ${val}`,
    referencedFields: [],
  };
}
