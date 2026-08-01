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

/**
 * Generates the standard accessible HTML string representing a clinical code lookup input field
 * with its associated real-time terminology validation feedback indicator.
 *
 * @param {string} id - The input element identifier.
 * @param {string} label - The visible label for the field.
 * @param {string} value - The current value of the field.
 * @param {string} [status='none'] - The lookup validation state ('none', 'loading', 'valid', 'invalid', 'degraded').
 * @param {string} [statusMessage=''] - The dynamic explanation message of the current validation status.
 * @returns {string} The HTML markup.
 */
export function createClinicalLookupInput(
  id,
  label,
  value = "",
  status = "none",
  statusMessage = ""
) {
  let statusHtml;
  if (status !== "none") {
    let stateClass = "";
    let statusIcon = "";
    let ariaLiveMessage = statusMessage;

    if (status === "loading") {
      stateClass = "lookup-loading";
      statusIcon = "⏳";
      if (!ariaLiveMessage)
        ariaLiveMessage = "Searching terminology database...";
    } else if (status === "valid") {
      stateClass = "lookup-valid";
      statusIcon = "✅";
      if (!ariaLiveMessage) ariaLiveMessage = "Code is valid.";
    } else if (status === "invalid") {
      stateClass = "lookup-invalid";
      statusIcon = "❌";
      if (!ariaLiveMessage)
        ariaLiveMessage = "Invalid code. Please check and try again.";
    } else if (status === "degraded") {
      stateClass = "lookup-degraded";
      statusIcon = "⚠️";
      if (!ariaLiveMessage)
        ariaLiveMessage = "Terminology service degraded. Validation offline.";
    }

    statusHtml = `
    <div id="lookup-status-${id}" class="lookup-status-indicator ${stateClass}" role="status" aria-live="polite">
      <span class="lookup-status-icon" aria-hidden="true">${statusIcon}</span>
      <span class="lookup-status-text">${ariaLiveMessage}</span>
    </div>`;
  } else {
    statusHtml = `<div id="lookup-status-${id}" class="lookup-status-indicator" role="status" aria-live="polite" style="display: none"></div>`;
  }

  return `
  <div id="field-container-${id}" class="clinical-input clinical-lookup-container grid-span-12" style="grid-column: span 12;">
    <label for="${id}">${label}</label>
    <div class="input-wrapper">
      <input id="${id}" type="text" name="${id}" value="${value}" autocomplete="off" />
    </div>
    ${statusHtml}
  </div>`;
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
} from "./signing.js";

export {
  normalizeApprovedConsent,
  shapeComprehensionAnswers,
  interpretComprehensionResult,
} from "./econsent.js";

/**
 * Generates an accessible fieldset row HTML string for configuring a rule condition.
 *
 * @param {number} index - The zero-indexed position of the condition element.
 * @param {Array} forms - Available forms list.
 * @param {Array} fields - Available fields list.
 * @param {Object} [initialData={}] - Optional initial values.
 * @returns {string} Accessible HTML string.
 */
export function createConditionRowHTML(
  index,
  forms = [],
  fields = [],
  initialData = {}
) {
  const formOptions = forms
    .map(
      (f) =>
        `<option value="${f.id}" ${f.id === initialData.formId ? "selected" : ""}>${f.name || f.id}</option>`
    )
    .join("");

  const filteredFields = initialData.formId
    ? fields.filter((f) => f.formId === initialData.formId)
    : fields;

  const fieldOptions = filteredFields
    .map(
      (f) =>
        `<option value="${f.id}" ${f.id === initialData.fieldId ? "selected" : ""}>${f.name || f.id}</option>`
    )
    .join("");

  const operators = [
    { value: "==", label: "Equals (==)" },
    { value: "!=", label: "Not Equals (!=)" },
    { value: ">", label: "Greater Than (>)" },
    { value: "<", label: "Less Than (<)" },
    { value: ">=", label: "Greater Than or Equal (>=)" },
    { value: "<=", label: "Less Than or Equal (<=)" },
    { value: "is_empty", label: "Is Empty" },
    { value: "is_not_empty", label: "Is Not Empty" },
  ];

  const operatorOptions = operators
    .map(
      (op) =>
        `<option value="${op.value}" ${op.value === initialData.operator ? "selected" : ""}>${op.label}</option>`
    )
    .join("");

  const isConstant = (initialData.rightType || "constant") === "constant";

  return `
  <fieldset class="condition-row-fieldset" data-index="${index}" style="border: 1px solid #cbd5e1; padding: 12px; border-radius: 6px; margin-bottom: 8px;">
    <legend style="font-size: 0.85rem; font-weight: 600;">Condition Element #${index + 1}</legend>
    <div style="display: grid; grid-template-columns: repeat(12, 1fr); gap: 8px; align-items: center;">
      <div style="grid-column: span 3;">
        <label for="cond-form-${index}" style="font-size: 0.75rem; display: block;">Form</label>
        <select id="cond-form-${index}" class="cond-form" style="width: 100%; padding: 4px;">
          <option value="">Select Form</option>
          ${formOptions}
        </select>
      </div>
      <div style="grid-column: span 3;">
        <label for="cond-field-${index}" style="font-size: 0.75rem; display: block;">Field</label>
        <select id="cond-field-${index}" class="cond-field" style="width: 100%; padding: 4px;">
          <option value="">Select Field</option>
          ${fieldOptions}
        </select>
      </div>
      <div style="grid-column: span 2;">
        <label for="cond-op-${index}" style="font-size: 0.75rem; display: block;">Operator</label>
        <select id="cond-op-${index}" class="cond-operator" style="width: 100%; padding: 4px;">
          ${operatorOptions}
        </select>
      </div>
      <div style="grid-column: span 2;">
        <label for="cond-right-type-${index}" style="font-size: 0.75rem; display: block;">Target Type</label>
        <select id="cond-right-type-${index}" class="cond-right-type" style="width: 100%; padding: 4px;">
          <option value="constant" ${isConstant ? "selected" : ""}>Constant Value</option>
          <option value="field_ref" ${!isConstant ? "selected" : ""}>Field Reference</option>
        </select>
      </div>
      <div style="grid-column: span 2; text-align: right;">
        <label for="cond-val-${index}" style="font-size: 0.75rem; display: block;">Target Value</label>
        <input id="cond-val-${index}" type="text" class="cond-value" value="${initialData.rightValue || ""}" style="width: 100%; padding: 4px;" />
      </div>
    </div>
  </fieldset>`;
}

/**
 * Generates an accessible rule builder HTML string layout.
 *
 * @param {Array} forms - Available forms list.
 * @param {Array} fields - Available fields list.
 * @param {Object} [initialRule={}] - Rule configuration data.
 * @returns {string} Accessible HTML markup string.
 */
export function createRuleEditorHTML(
  // eslint-disable-next-line no-unused-vars
  forms = [],
  fields = [],
  initialRule = {}
) {
  const ruleTypeOptions = [
    { value: "skip_logic", label: "Skip Logic (Field Visibility)" },
    { value: "constraint", label: "Field Constraint Check" },
    { value: "edit_check", label: "Edit Check (Validation Error)" },
    { value: "derived_field", label: "Derived Calculation" },
  ]
    .map(
      (t) =>
        `<option value="${t.value}" ${t.value === initialRule.ruleType ? "selected" : ""}>${t.label}</option>`
    )
    .join("");

  const targetFieldOptions = fields
    .map(
      (f) =>
        `<option value="${f.id}" ${f.id === initialRule.targetField ? "selected" : ""}>${f.name || f.id}</option>`
    )
    .join("");

  return `
  <div class="rule-editor-container" style="padding: 16px; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px;" data-deid-ignore="deid-ignore">
    <h3 style="margin-top: 0;">Rule Configuration</h3>
    <div style="display: grid; grid-template-columns: repeat(12, 1fr); gap: 12px; margin-bottom: 16px;">
      <div style="grid-column: span 6;">
        <label for="rule-type-select">Rule Type</label>
        <select id="rule-type-select" class="rule-type-selector" style="width: 100%; padding: 6px;">
          ${ruleTypeOptions}
        </select>
      </div>
      <div style="grid-column: span 6;">
        <label for="target-field-select">Target Field</label>
        <select id="target-field-select" class="target-field-selector" style="width: 100%; padding: 6px;">
          <option value="">Select Target Field</option>
          ${targetFieldOptions}
        </select>
      </div>
      <div style="grid-column: span 12;">
        <label for="query-message-input">Query Message</label>
        <input id="query-message-input" type="text" class="query-message-input" value="${initialRule.queryMessage || ""}" placeholder="Discrepancy query explanation message..." style="width: 100%; padding: 6px;" />
      </div>
    </div>
    <h4>Condition Elements</h4>
    <div class="conditions-list-container"></div>
    <button type="button" class="btn btn-secondary add-condition-btn" style="margin-top: 8px;">+ Add Condition Element</button>
  </div>`;
}

/**
 * Serializes dynamic UI condition rows into a structured Pydantic condition tree AST.
 *
 * @param {Array} conditions - List of condition row objects.
 * @param {string} matchOperator - "and" or "or".
 * @returns {Object} Pydantic-compatible condition AST.
 */
export function serializeConditionsTree(
  conditions = [],
  matchOperator = "and"
) {
  const validConditions = conditions.filter((c) => c && c.fieldId);
  const operands = [];

  validConditions.forEach((cond) => {
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
