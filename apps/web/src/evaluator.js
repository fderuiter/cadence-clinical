/**
 * Client-side AST evaluator for clinical rules (relevant skip logic and constraints).
 */

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
      const childVal = evaluateAST(rawChildren[0], context, currentIndices);
      return !childVal;
    }

    if (operator === "and") {
      // Kleene 3-valued logic: if any operand is false, return false.
      // Otherwise if any is null, return null. Else return true.
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
    if (rawChildren.length < 2) return null;
    const leftVal = evaluateAST(rawChildren[0], context, currentIndices);
    const rightVal = evaluateAST(rawChildren[1], context, currentIndices);

    // Null safety for arithmetic
    if (["+", "-", "*", "/"].includes(operator)) {
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
      if (isNaN(lNum) || isNaN(rNum)) return null;

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
      const childVal = evaluateAST(rawChildren[0], context, currentIndices);
      return (
        childVal === null ||
        childVal === undefined ||
        String(childVal).trim() === ""
      );
    }

    if (funcName === "is_not_empty") {
      const childVal = evaluateAST(rawChildren[0], context, currentIndices);
      return (
        childVal !== null &&
        childVal !== undefined &&
        String(childVal).trim() !== ""
      );
    }

    if (funcName === "indexed-repeat") {
      if (rawChildren.length < 3) return null;
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
