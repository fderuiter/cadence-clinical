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

  resize(newCapacity) {
    this.capacity = Math.max(0, newCapacity);
    while (this.cache.size > this.capacity) {
      const firstKey = this.cache.keys().next().value;
      this.cache.delete(firstKey);
    }
  }
}

// Global initialization of compiler Cache
export const compilerCache = new SimpleLRUCache(200);

export function resizeCompilerCache(capacity) {
  compilerCache.resize(capacity);
}

export class DiagnosticRegistry {
  constructor() {
    this.listeners = new Set();
  }

  subscribe(callback) {
    this.listeners.add(callback);
    return () => {
      this.listeners.delete(callback);
    };
  }

  unsubscribe(callback) {
    this.listeners.delete(callback);
  }

  register(callback) {
    this.listeners.add(callback);
  }

  unregister(callback) {
    this.listeners.delete(callback);
  }

  emit(event) {
    for (const listener of this.listeners) {
      try {
        listener(event);
      } catch (err) {
        // Errors thrown within third-party dynamic listener callbacks must be caught internally to prevent app-level crashes or validation bypasses
        console.error("Error in diagnostic listener callback:", err);
      }
    }
  }
}

export const diagnosticRegistry = new DiagnosticRegistry();

export function registerDiagnosticListener(callback) {
  return diagnosticRegistry.subscribe(callback);
}

export function unregisterDiagnosticListener(callback) {
  diagnosticRegistry.unsubscribe(callback);
}

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
  const rawChildren = node.children || node.operands || [];
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

  // 3. OPERATOR / LOGICAL / COMPARISON
  if (type === "OPERATOR" || type === "logical" || type === "comparison") {
    const operator = (
      typeof valAttr === "string" ? valAttr : node.operator || ""
    ).toLowerCase();

    // Logical operators
    if (operator === "not") {
      if (rawChildren.length !== 1) {
        diagnosticRegistry.emit({
          type: "arity_mismatch",
          nodeType: type,
          name: operator,
          expected: 1,
          actual: rawChildren.length,
          message: `Operator '${operator}' expects exactly 1 operand, but got ${rawChildren.length}.`,
          node
        });
        return null;
      }
      const childVal = evaluateAST(rawChildren[0], context, currentIndices);
      return !childVal;
    }

    if (operator === "and") {
      // Kleene 3-valued logic: if any operand is false, return false.
      // Otherwise if any is null, return null. Else return true.
      if (rawChildren.length === 0) {
        diagnosticRegistry.emit({
          type: "arity_mismatch",
          nodeType: type,
          name: operator,
          expected: ">= 1",
          actual: 0,
          message: `Operator '${operator}' expects at least 1 operand, but got 0.`,
          node
        });
        return null;
      }
      let hasNull = false;
      for (const child of rawChildren) {
        const childVal = evaluateAST(child, context, currentIndices);
        if (childVal === false) return false;
        if (childVal === null || childVal === undefined) {
          hasNull = true;
        }
      }
      return hasNull ? null : true;
    }

    if (operator === "or") {
      // Kleene 3-valued logic: if any operand is true, return true.
      // Otherwise if any is null, return null. Else return false.
      if (rawChildren.length === 0) {
        diagnosticRegistry.emit({
          type: "arity_mismatch",
          nodeType: type,
          name: operator,
          expected: ">= 1",
          actual: 0,
          message: `Operator '${operator}' expects at least 1 operand, but got 0.`,
          node
        });
        return null;
      }
      let hasNull = false;
      for (const child of rawChildren) {
        const childVal = evaluateAST(child, context, currentIndices);
        if (childVal === true) return true;
        if (childVal === null || childVal === undefined) {
          hasNull = true;
        }
      }
      return hasNull ? null : false;
    }

    // Arithmetic and Comparison operators
    if (["+", "-", "*", "/"].includes(operator)) {
      if (rawChildren.length !== 2) {
        diagnosticRegistry.emit({
          type: "arity_mismatch",
          nodeType: type,
          name: operator,
          expected: 2,
          actual: rawChildren.length,
          message: `Operator '${operator}' expects exactly 2 operands, but got ${rawChildren.length}.`,
          node
        });
        return null;
      }
      const leftVal = evaluateAST(rawChildren[0], context, currentIndices);
      const rightVal = evaluateAST(rawChildren[1], context, currentIndices);

      // Null safety for arithmetic
      if (
        leftVal === null ||
        leftVal === undefined ||
        rightVal === null ||
        rightVal === undefined
      ) {
        return null;
      }
      const lNum = parseFloat(leftVal);
      const rNum = parseFloat(rightVal);
      if (isNaN(lNum) || isNaN(rNum)) {
        diagnosticRegistry.emit({
          type: "type_mismatch",
          nodeType: type,
          name: operator,
          expected: "numeric",
          actual: isNaN(lNum) ? (leftVal === null ? "null" : typeof leftVal) : (rightVal === null ? "null" : typeof rightVal),
          message: `Arithmetic operator '${operator}' expects numeric operands, but got non-numeric value.`,
          node
        });
        return null;
      }

      if (operator === "+") return lNum + rNum;
      if (operator === "-") return lNum - rNum;
      if (operator === "*") return lNum * rNum;
      if (operator === "/") {
        if (rNum === 0) return null; // Safe division by zero
        return lNum / rNum;
      }
    }

    // Comparison Operators (null-safe)
    if (["==", "!=", "<", "<=", ">", ">="].includes(operator)) {
      if (rawChildren.length !== 2) {
        diagnosticRegistry.emit({
          type: "arity_mismatch",
          nodeType: type,
          name: operator,
          expected: 2,
          actual: rawChildren.length,
          message: `Comparison operator '${operator}' expects exactly 2 operands, but got ${rawChildren.length}.`,
          node
        });
        return null;
      }
      const leftVal = evaluateAST(rawChildren[0], context, currentIndices);
      const rightVal = evaluateAST(rawChildren[1], context, currentIndices);

      // Direct equality/inequality is null-safe
      if (operator === "==") {
        const l = leftVal === undefined ? null : leftVal;
        const r = rightVal === undefined ? null : rightVal;
        return l === r;
      }
      if (operator === "!=") {
        const l = leftVal === undefined ? null : leftVal;
        const r = rightVal === undefined ? null : rightVal;
        return l !== r;
      }

      // Ordered comparison with nulls always returns false
      if (
        leftVal === null ||
        leftVal === undefined ||
        rightVal === null ||
        rightVal === undefined
      ) {
        return false;
      }

      const lNum = parseFloat(leftVal);
      const rNum = parseFloat(rightVal);

      // Fallback to string comparison if not both numeric
      const useNumeric = !isNaN(lNum) && !isNaN(rNum);
      const l = useNumeric ? lNum : String(leftVal);
      const r = useNumeric ? rNum : String(rightVal);

      if (operator === "<") return l < r;
      if (operator === "<=") return l <= r;
      if (operator === ">") return l > r;
      if (operator === ">=") return l >= r;
    }

    return null;
  }

  // 4. FUNCTION
  if (type === "FUNCTION" || type === "function") {
    const funcName = (
      typeof valAttr === "string" ? valAttr : node.operator || ""
    ).toLowerCase();

    if (funcName === "is_empty" || funcName === "empty") {
      if (rawChildren.length !== 1) {
        diagnosticRegistry.emit({
          type: "arity_mismatch",
          nodeType: type,
          name: funcName,
          expected: 1,
          actual: rawChildren.length,
          message: `Function '${funcName}' expects exactly 1 operand, but got ${rawChildren.length}.`,
          node
        });
        return null;
      }
      const childVal = evaluateAST(rawChildren[0], context, currentIndices);
      return (
        childVal === null ||
        childVal === undefined ||
        String(childVal).trim() === ""
      );
    }

    if (funcName === "is_not_empty") {
      if (rawChildren.length !== 1) {
        diagnosticRegistry.emit({
          type: "arity_mismatch",
          nodeType: type,
          name: funcName,
          expected: 1,
          actual: rawChildren.length,
          message: `Function '${funcName}' expects exactly 1 operand, but got ${rawChildren.length}.`,
          node
        });
        return null;
      }
      const childVal = evaluateAST(rawChildren[0], context, currentIndices);
      return (
        childVal !== null &&
        childVal !== undefined &&
        String(childVal).trim() !== ""
      );
    }

    if (funcName === "indexed-repeat") {
      if (rawChildren.length !== 3) {
        diagnosticRegistry.emit({
          type: "arity_mismatch",
          nodeType: type,
          name: funcName,
          expected: 3,
          actual: rawChildren.length,
          message: `Function '${funcName}' expects exactly 3 operands, but got ${rawChildren.length}.`,
          node
        });
        return null;
      }
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
      if (isNaN(targetIndex)) {
        diagnosticRegistry.emit({
          type: "type_mismatch",
          nodeType: type,
          name: funcName,
          expected: "integer",
          actual: typeof indexVal,
          message: `Function '${funcName}' expects integer index for third argument, but got non-integer value '${indexVal}'.`,
          node
        });
        return null;
      }

      const fieldName = targetPath.split("/").pop();
      const indexedPath = `${repeatGroup}[${targetIndex}]/${fieldName}`;

      return context[indexedPath] !== undefined ? context[indexedPath] : null;
    }

    // Unknown function fallback
    diagnosticRegistry.emit({
      type: "unknown_function",
      nodeType: type,
      name: funcName,
      message: `Unknown function '${funcName}'.`,
      node
    });
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
        ? evaluateAST(field.relevant, updatedValues) !== false
        : true;

      const wasVisible = visibleFields[field.id] !== false;
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
