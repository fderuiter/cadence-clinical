/**
 * A small debounce utility that limits function execution during rapid invocation.
 *
 * @param {Function} func - The function to debounce.
 * @param {number} wait - The delay in milliseconds before executing the function.
 * @returns {Function} The debounced function.
 */
export function debounce(func, wait) {
  let timeout;
  return function (...args) {
    const context = this;
    clearTimeout(timeout);
    timeout = setTimeout(() => {
      func.apply(context, args);
    }, wait);
  };
}

export {
  canonicalSerialize,
  generateCanonicalSignature,
  verifyCanonicalSignature,
  generateGatewaySignature,
  verifyGatewaySignature,
  generateJwtHS256,
  sha256,
  validateField,
  buildLedgerBlock,
  encryptAESGCM,
  decryptAESGCM,
  deriveSessionKey,
  deriveKeyFromPIN,
} from "./signing.js";

/**
 * Generates vanilla HTML string for a single condition row in the rule builder.
 * Includes selectors for form, field, operator, right-hand operand type/value/field reference.
 *
 * @param {number} index - Index of the condition row.
 * @param {Array} forms - List of available forms [{ id, name }].
 * @param {Array} fields - List of available fields [{ id, name, formId }].
 * @param {Object} [initialData={}] - Initial data for pre-populating fields.
 * @returns {string} The HTML string.
 */
export function createConditionRowHTML(
  index,
  forms = [],
  fields = [],
  initialData = {}
) {
  const cond = {
    formId: "",
    fieldId: "",
    operator: "==",
    rightType: "constant",
    rightValue: "",
    rightFieldId: "",
    rightFormId: "",
    ...initialData,
  };
  const formOptions = forms
    .map(
      (f) =>
        `<option value="${f.id}" ${cond.formId === f.id ? "selected" : ""}>${f.name}</option>`
    )
    .join("");
  const fieldOptions = fields
    .map(
      (f) =>
        `<option value="${f.id}" ${cond.fieldId === f.id ? "selected" : ""}>${f.name}</option>`
    )
    .join("");
  const rightFieldOptions = fields
    .map(
      (f) =>
        `<option value="${f.id}" ${cond.rightFieldId === f.id ? "selected" : ""}>${f.name}</option>`
    )
    .join("");

  const operators = [
    { value: "==", text: "equals" },
    { value: "!=", text: "does not equal" },
    { value: "<", text: "is less than" },
    { value: "<=", text: "is less than or equal to" },
    { value: ">", text: "is greater than" },
    { value: ">=", text: "is greater than or equal to" },
    { value: "is_empty", text: "is empty" },
    { value: "is_not_empty", text: "is not empty" },
  ];

  const operatorOptions = operators
    .map(
      (op) =>
        `<option value="${op.value}" ${cond.operator === op.value ? "selected" : ""}>${op.text}</option>`
    )
    .join("");

  const showRightOperand =
    cond.operator !== "is_empty" && cond.operator !== "is_not_empty";

  let rightOperandHTML = "";
  if (showRightOperand) {
    rightOperandHTML = `
      <div class="form-group cond-right-group" style="flex: 1; min-width: 100px;">
        <label for="cond-right-type-${index}" style="font-size: 0.75rem; display: block; margin-bottom: 4px;">Right Value Type</label>
        <select id="cond-right-type-${index}" class="cond-right-type" data-index="${index}" style="width: 100%; padding: 6px; font-size: 0.8rem; border-radius: 4px; border: 1px solid var(--border);">
          <option value="constant" ${cond.rightType === "constant" ? "selected" : ""}>Constant Value</option>
          <option value="field_ref" ${cond.rightType === "field_ref" ? "selected" : ""}>Field Reference</option>
        </select>
      </div>

      ${
        cond.rightType === "constant"
          ? `
        <div class="form-group cond-right-val-group" style="flex: 1; min-width: 100px;">
          <label for="cond-right-value-${index}" style="font-size: 0.75rem; display: block; margin-bottom: 4px;">Constant Value</label>
          <input id="cond-right-value-${index}" class="cond-right-value" data-index="${index}" type="text" placeholder="Value..." value="${cond.rightValue}" style="width: 100%; padding: 6px; font-size: 0.8rem; border-radius: 4px; border: 1px solid var(--border);" />
        </div>
      `
          : `
        <div class="form-group cond-right-field-group" style="flex: 1; min-width: 100px;">
          <label for="cond-right-field-${index}" style="font-size: 0.75rem; display: block; margin-bottom: 4px;">Right Field</label>
          <select id="cond-right-field-${index}" class="cond-right-field" data-index="${index}" style="width: 100%; padding: 6px; font-size: 0.8rem; border-radius: 4px; border: 1px solid var(--border);">
            <option value="">-- Select Field --</option>
            ${rightFieldOptions}
          </select>
        </div>
      `
      }
    `;
  }

  return `
    <fieldset class="condition-row-fieldset" data-index="${index}" style="border: 1px dashed var(--border); border-radius: 8px; padding: 12px; background-color: #fafbfd; margin-bottom: 12px;">
      <legend style="font-size: 0.75rem; font-weight: bold; padding: 0 4px; color: var(--primary);">Condition Element #${index + 1}</legend>
      <div style="display: flex; gap: 8px; align-items: flex-end; flex-wrap: wrap;">
        <div class="form-group" style="flex: 1; min-width: 100px;">
          <label for="cond-form-${index}" style="font-size: 0.75rem; display: block; margin-bottom: 4px;">Left Form</label>
          <select id="cond-form-${index}" class="cond-form" data-index="${index}" style="width: 100%; padding: 6px; font-size: 0.8rem; border-radius: 4px; border: 1px solid var(--border);">
            <option value="">-- Select Form --</option>
            ${formOptions}
          </select>
        </div>

        <div class="form-group" style="flex: 1; min-width: 100px;">
          <label for="cond-field-${index}" style="font-size: 0.75rem; display: block; margin-bottom: 4px;">Left Field</label>
          <select id="cond-field-${index}" class="cond-field" data-index="${index}" style="width: 100%; padding: 6px; font-size: 0.8rem; border-radius: 4px; border: 1px solid var(--border);">
            <option value="">-- Select Field --</option>
            ${fieldOptions}
          </select>
        </div>

        <div class="form-group" style="flex: 1; min-width: 100px;">
          <label for="cond-operator-${index}" style="font-size: 0.75rem; display: block; margin-bottom: 4px;">Operator</label>
          <select id="cond-operator-${index}" class="cond-operator" data-index="${index}" style="width: 100%; padding: 6px; font-size: 0.8rem; border-radius: 4px; border: 1px solid var(--border);">
            ${operatorOptions}
          </select>
        </div>

        ${rightOperandHTML}

        <button type="button" class="btn btn-danger remove-condition-btn" data-action="remove-condition" data-index="${index}" aria-label="Remove Condition Element #${index + 1}" style="background-color: var(--error); color: white; border: none; padding: 6px 12px; border-radius: 4px; cursor: pointer; font-size: 0.8rem; height: fit-content; align-self: flex-end;">Remove</button>
      </div>
    </fieldset>
  `.trim();
}

/**
 * Generates vanilla HTML string for the rule editor container, assembling multiple condition rows.
 *
 * @param {Array} forms - List of available forms [{ id, name }].
 * @param {Array} fields - List of available fields [{ id, name, formId }].
 * @param {Object} [options={}] - Options for pre-populating fields and conditions.
 * @returns {string} The HTML string.
 */
export function createRuleEditorHTML(forms = [], fields = [], options = {}) {
  const opt = {
    conditions: [],
    matchOperator: "and",
    ruleType: "skip_logic",
    targetField: "",
    ruleAction: "show",
    targetForm: "",
    queryMessage: "",
    ...options,
  };

  const ruleTypes = [
    { value: "skip_logic", text: "Skip Logic (Show/Hide fields)" },
    {
      value: "constraint",
      text: "Field Constraint (Single field query validation)",
    },
    { value: "edit_check", text: "Edit Check (Validation Error)" },
    { value: "derived_field", text: "Derived Calculation" },
    { value: "cross_form_check", text: "Cross-Form / Longitudinal Check" },
  ];

  const typeOptions = ruleTypes
    .map(
      (t) =>
        `<option value="${t.value}" ${opt.ruleType === t.value ? "selected" : ""}>${t.text}</option>`
    )
    .join("");
  const targetFieldOptions = fields
    .map(
      (f) =>
        `<option value="${f.id}" ${opt.targetField === f.id ? "selected" : ""}>${f.name}</option>`
    )
    .join("");
  const targetFormOptions = forms
    .map(
      (f) =>
        `<option value="${f.id}" ${opt.targetForm === f.id ? "selected" : ""}>${f.name}</option>`
    )
    .join("");

  // Build rows HTML
  const rowsHTML = (opt.conditions.length > 0 ? opt.conditions : [{}])
    .map((cond, idx) => {
      return createConditionRowHTML(idx, forms, fields, cond);
    })
    .join("\n");

  return `
    <div class="rule-editor-container" data-deid-ignore="deid-ignore" style="display: flex; flex-direction: column; gap: 16px;">
      <!-- Rule Type & Target definition -->
      <fieldset style="border: 1px solid var(--border); border-radius: 8px; padding: 16px;">
        <legend style="padding: 0 8px; font-weight: bold; color: var(--accent);">Rule Type & Target Definition</legend>

        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-bottom: 16px;">
          <div class="form-group">
            <label for="rule-type-select" style="display: block; margin-bottom: 6px; font-weight: 600; font-size: 0.85rem;">Rule Classification Type</label>
            <select id="rule-type-select" class="rule-type-selector" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 6px;">
              ${typeOptions}
            </select>
          </div>

          <div id="target-field-wrapper" class="form-group" style="${opt.ruleType === "cross_form_check" ? "display: none;" : ""}">
            <label for="target-field-select" style="display: block; margin-bottom: 6px; font-weight: 600; font-size: 0.85rem;">Target Field</label>
            <select id="target-field-select" class="target-field-selector" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 6px;">
              <option value="">-- Select Target Field --</option>
              ${targetFieldOptions}
            </select>
          </div>
        </div>

        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px;">
          <div id="skip-action-wrapper" class="form-group" style="${opt.ruleType !== "skip_logic" ? "display: none;" : ""}">
            <label for="rule-action-select" style="display: block; margin-bottom: 6px; font-weight: 600; font-size: 0.85rem;">Action on Target</label>
            <select id="rule-action-select" class="rule-action-selector" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 6px;">
              <option value="show" ${opt.ruleAction === "show" ? "selected" : ""}>Show target field</option>
              <option value="hide" ${opt.ruleAction === "hide" ? "selected" : ""}>Hide target field</option>
            </select>
          </div>

          <div id="target-form-wrapper" class="form-group" style="${opt.ruleType !== "skip_logic" ? "display: none;" : ""}">
            <label for="target-form-select" style="display: block; margin-bottom: 6px; font-weight: 600; font-size: 0.85rem;">Target Form (Optional)</label>
            <select id="target-form-select" class="target-form-selector" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 6px;">
              <option value="">-- Select Target Form --</option>
              ${targetFormOptions}
            </select>
          </div>

          <div id="query-message-wrapper" class="form-group" style="grid-column: span 2; ${opt.ruleType === "skip_logic" ? "display: none;" : ""}">
            <label for="query-message-input" style="display: block; margin-bottom: 6px; font-weight: 600; font-size: 0.85rem;">Auto-Query Discrepancy Message</label>
            <input id="query-message-input" class="query-message-input" type="text" placeholder="e.g., Systolic BP is out of logical range" value="${opt.queryMessage}" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 6px;" />
          </div>
        </div>
      </fieldset>

      <!-- Rule Conditions -->
      <fieldset style="border: 1px solid var(--border); border-radius: 8px; padding: 16px;">
        <legend style="padding: 0 8px; font-weight: bold; color: var(--accent);">Rule Conditions (Logical Expression Tree)</legend>

        <div class="form-group" style="margin-bottom: 16px;">
          <label for="match-operator-select" style="display: block; margin-bottom: 6px; font-weight: 600; font-size: 0.85rem;">Match Conditions Group Operator</label>
          <select id="match-operator-select" class="match-operator-selector" style="padding: 6px; border: 1px solid var(--border); border-radius: 6px; font-size: 0.85rem;">
            <option value="and" ${opt.matchOperator === "and" ? "selected" : ""}>All conditions must be met (AND)</option>
            <option value="or" ${opt.matchOperator === "or" ? "selected" : ""}>Any condition can be met (OR)</option>
          </select>
        </div>

        <!-- Dynamic Conditions list -->
        <div id="conditions-list-container" class="conditions-list-container" style="display: flex; flex-direction: column; gap: 12px;">
          ${rowsHTML}
        </div>

        <div style="margin-top: 12px;">
          <button type="button" class="btn btn-secondary add-condition-btn" data-action="add-condition" style="padding: 6px 12px; font-size: 0.8rem; cursor: pointer;">➕ Add Condition Row</button>
        </div>
      </fieldset>
    </div>
  `.trim();
}

/**
 * Serializes local UI condition rows and group operator into designer AST expression-tree JSON shape.
 *
 * @param {Array} conditions - Array of condition rows.
 * @param {string} [matchOperator='and'] - Match conditions group operator.
 * @returns {Object} The compiled Pydantic Condition Expression Tree.
 */
export function serializeConditionsTree(
  conditions = [],
  matchOperator = "and"
) {
  const operands = [];
  conditions.forEach((cond) => {
    if (!cond.fieldId) return;

    const leftRef = {
      type: "field_ref",
      field_ref: {
        field_id: cond.fieldId,
        form_id: cond.formId || null,
      },
    };

    if (cond.operator === "is_empty" || cond.operator === "is_not_empty") {
      operands.push({
        type: "function",
        operator: cond.operator,
        operands: [leftRef],
      });
    } else {
      let rightNode;
      if (cond.rightType === "constant") {
        let val = cond.rightValue;
        if (val === "true") val = true;
        else if (val === "false") val = false;
        else if (!isNaN(parseFloat(val)) && isFinite(val))
          val = parseFloat(val);

        rightNode = {
          type: "constant",
          value: val,
        };
      } else {
        rightNode = {
          type: "field_ref",
          field_ref: {
            field_id: cond.rightFieldId || "",
            form_id: cond.rightFormId || null,
          },
        };
      }

      operands.push({
        type: "comparison",
        operator: cond.operator,
        operands: [leftRef, rightNode],
      });
    }
  });

  if (operands.length === 0) {
    return { type: "constant", value: true };
  }

  if (operands.length === 1) {
    return operands[0];
  }

  return {
    type: "logical",
    operator: matchOperator,
    operands: operands,
  };
}

/**
 * Deserializes a designer AST expression-tree JSON shape back into condition rows and a group operator.
 *
 * @param {Object} tree - The condition tree to deserialize.
 * @returns {Object} { conditions: Array, matchOperator: string }
 */
export function deserializeConditionsTree(tree) {
  if (!tree) {
    return { conditions: [], matchOperator: "and" };
  }

  let matchOperator = "and";
  let operands;

  if (tree.type === "logical") {
    matchOperator = tree.operator || "and";
    operands = tree.operands || [];
  } else if (tree.type === "constant" && tree.value === true) {
    return { conditions: [], matchOperator: "and" };
  } else {
    operands = [tree];
  }

  const conditions = operands
    .map((node) => {
      if (node.type === "comparison") {
        const left = node.operands[0];
        const right = node.operands[1];
        return {
          formId: left?.field_ref?.form_id || "",
          fieldId: left?.field_ref?.field_id || "",
          operator: node.operator || "==",
          rightType: right?.type === "field_ref" ? "field_ref" : "constant",
          rightValue: right?.type === "constant" ? String(right.value) : "",
          rightFieldId:
            right?.type === "field_ref" ? right.field_ref.field_id || "" : "",
          rightFormId:
            right?.type === "field_ref" ? right.field_ref.form_id || "" : "",
        };
      } else if (node.type === "function") {
        const left = node.operands[0];
        return {
          formId: left?.field_ref?.form_id || "",
          fieldId: left?.field_ref?.field_id || "",
          operator: node.operator || "is_empty",
          rightType: "constant",
          rightValue: "",
          rightFieldId: "",
          rightFormId: "",
        };
      }
      return null;
    })
    .filter(Boolean);

  return { conditions, matchOperator };
}

export { toBeAccessible } from "./accessibility-matcher.js";

// Dynamic Hover Pointer Capability Detection
export function initHoverDetection() {
  if (typeof window !== "undefined" && typeof document !== "undefined") {
    if (typeof window.matchMedia === "function") {
      const mediaQuery = window.matchMedia("(hover: hover)");
      const updateHoverClass = (e) => {
        if (e.matches) {
          document.body.classList.add("can-hover");
        } else {
          document.body.classList.remove("can-hover");
        }
      };

      updateHoverClass(mediaQuery);
      if (mediaQuery.addEventListener) {
        mediaQuery.addEventListener("change", updateHoverClass);
      } else if (mediaQuery.addListener) {
        mediaQuery.addListener(updateHoverClass);
      }
    } else {
      document.body.classList.add("can-hover");
    }
  }
}

initHoverDetection();
