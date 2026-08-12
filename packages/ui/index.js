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
  let inputAttrs = "";

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

    inputAttrs += ` aria-describedby="lookup-status-${id}"`;
    if (status === "invalid") {
      inputAttrs += ` aria-invalid="true"`;
    }
  } else {
    statusHtml = `<div id="lookup-status-${id}" class="lookup-status-indicator" role="status" aria-live="polite" style="display: none"></div>`;
  }

  return `
  <div id="field-container-${id}" class="clinical-input clinical-lookup-container grid-span-12" style="grid-column: span 12;">
    <label for="${id}">${label}</label>
    <div class="input-wrapper">
      <input id="${id}" type="text" name="${id}" value="${value}" autocomplete="off"${inputAttrs} />
    </div>
    ${statusHtml}
  </div>`;
}

export { ComplianceSDK } from "./sdk.ts";

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

export {
  normalizeApprovedConsent,
  shapeComprehensionAnswers,
  interpretComprehensionResult,
} from "./econsent.js";

/**
 * Renders an arm-aware clinical Schedule of Activities (SoA) matrix.
 * Supports grouped column headers for Arms, Epochs, and Visits, and
 * rich per-cell details (such as applicability, timing, conditional/optional states).
 *
 * @param {Object} soaData - The structured SoA dataset.
 * @returns {string} Fully semantic, accessible HTML table representing the SoA.
 */
export function createSoaBuilderMatrix(soaData) {
  if (!soaData || !soaData.rows) {
    return `<div class="clinical-visit-matrix-error">Invalid SoA matrix data.</div>`;
  }

  const epochs = soaData.epochs || [];
  const encounters = soaData.encounters || [];
  const rows = soaData.rows || [];
  const arms = soaData.arms || [];

  if (encounters.length === 0) {
    return `<div class="clinical-visit-matrix-error">No encounters defined for SoA matrix.</div>`;
  }

  const epochMap = {};
  epochs.forEach((ep) => {
    epochMap[ep.epoch_id] = ep;
  });

  const armMap = {};
  arms.forEach((arm) => {
    armMap[arm.arm_id] = arm;
  });

  const cols = encounters.map((enc) => {
    const epoch = epochMap[enc.epoch_id] || {
      epoch_id: enc.epoch_id,
      epoch_name: enc.epoch_id,
    };
    const armId = enc.arm_id || epoch.arm_id;
    const arm = armId
      ? armMap[armId] || { arm_id: armId, arm_name: armId }
      : null;

    return {
      arm,
      epoch,
      encounter: enc,
    };
  });

  const hasArms = cols.some((c) => c.arm !== null);
  const totalHeaderRows = hasArms ? 3 : 2;

  // Arm groups
  const armGroups = [];
  let currentArmGroup = null;
  cols.forEach((col) => {
    const armId = col.arm ? col.arm.arm_id : "shared";
    const armName = col.arm ? col.arm.arm_name : "Common / Shared";

    if (!currentArmGroup || currentArmGroup.id !== armId) {
      if (currentArmGroup) {
        armGroups.push(currentArmGroup);
      }
      currentArmGroup = {
        id: armId,
        name: armName,
        colspan: 0,
      };
    }
    currentArmGroup.colspan++;
  });
  if (currentArmGroup) {
    armGroups.push(currentArmGroup);
  }

  // Epoch groups
  const epochGroups = [];
  let currentEpochGroup = null;
  cols.forEach((col) => {
    const armId = col.arm ? col.arm.arm_id : "shared";
    const epochId = col.epoch.epoch_id;
    const epochName = col.epoch.epoch_name;
    const groupKey = `${armId}_${epochId}`;

    if (!currentEpochGroup || currentEpochGroup.key !== groupKey) {
      if (currentEpochGroup) {
        epochGroups.push(currentEpochGroup);
      }
      currentEpochGroup = {
        key: groupKey,
        name: epochName,
        colspan: 0,
      };
    }
    currentEpochGroup.colspan++;
  });
  if (currentEpochGroup) {
    epochGroups.push(currentEpochGroup);
  }

  let tableHtml = `<table class="clinical-visit-matrix clinical-soa-matrix"><thead>`;

  // Arm row
  if (hasArms) {
    tableHtml += `<tr><th scope="col" rowspan="${totalHeaderRows}" class="corner-header">Form / Procedure</th>`;
    armGroups.forEach((g) => {
      tableHtml += `<th scope="col" colspan="${g.colspan}" class="grouped-header arm-header">${g.name}</th>`;
    });
    tableHtml += `</tr>`;
  }

  // Epoch row
  if (hasArms) {
    tableHtml += `<tr>`;
    epochGroups.forEach((g) => {
      tableHtml += `<th scope="col" colspan="${g.colspan}" class="grouped-header epoch-header">${g.name}</th>`;
    });
    tableHtml += `</tr>`;
  } else {
    tableHtml += `<tr><th scope="col" rowspan="${totalHeaderRows}" class="corner-header">Form / Procedure</th>`;
    epochGroups.forEach((g) => {
      tableHtml += `<th scope="col" colspan="${g.colspan}" class="grouped-header epoch-header">${g.name}</th>`;
    });
    tableHtml += `</tr>`;
  }

  // Encounter row
  tableHtml += `<tr>`;
  cols.forEach((c) => {
    tableHtml += `<th scope="col" class="encounter-header">${c.encounter.encounter_name}</th>`;
  });
  tableHtml += `</tr></thead><tbody>`;

  // Rows
  rows.forEach((row) => {
    tableHtml += `<tr><th scope="row">${row.activity_name}</th>`;
    cols.forEach((col) => {
      const encId = col.encounter.encounter_id;
      const cell = row.cells?.find((c) => c.encounter_id === encId) || {
        is_applicable: false,
      };
      let cellClass = "status-n-a";
      let cellContent = "-";
      let statusText = "Not Applicable";

      if (cell.is_applicable) {
        cellClass = "status-applicable";
        statusText = "Applicable";
        if (cell.details) {
          const detailsLower = cell.details.toLowerCase();
          if (detailsLower.includes("conditional")) {
            cellClass = "status-conditional";
            statusText = "Conditional";
          } else if (detailsLower.includes("optional")) {
            cellClass = "status-optional";
            statusText = "Optional";
          } else {
            statusText = cell.details;
          }
        }
        cellContent = `✓${
          cell.details
            ? ` <span class="cell-details">${cell.details}</span>`
            : ""
        }`;
      }

      const parts = [];
      if (col.arm && col.arm.arm_name) {
        parts.push(col.arm.arm_name);
      }
      if (col.epoch && col.epoch.epoch_name) {
        parts.push(col.epoch.epoch_name);
      }
      if (col.encounter && col.encounter.encounter_name) {
        parts.push(col.encounter.encounter_name);
      }
      const colDetailsStr = parts.join(" - ") || col.encounter.encounter_name;

      const srLabel = `Form: ${row.activity_name}, Visit: ${colDetailsStr}, Status: ${statusText}`;
      const srSpan = `<span class="sr-only" style="position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0, 0, 0, 0); border: 0; white-space: nowrap;">${srLabel}</span>`;

      tableHtml += `<td class="${cellClass}">${cellContent}${srSpan}</td>`;
    });
    tableHtml += `</tr>`;
  });

  tableHtml += `</tbody></table>`;
  return tableHtml;
}

/**
 * Backwards compatible clinical visit matrix function. Delegates to
 * `createSoaBuilderMatrix` if hierarchical input structure is detected,
 * or renders a simple 2D visit matrix for flat arrays.
 */
export function createClinicalVisitMatrix(visitsOrSoa, forms = []) {
  // If first argument is hierarchical soaData, delegate to createSoaBuilderMatrix
  if (
    visitsOrSoa &&
    (visitsOrSoa.rows || visitsOrSoa.encounters || visitsOrSoa.epochs)
  ) {
    return createSoaBuilderMatrix(visitsOrSoa);
  }

  // Simple, backwards-compatible 2D Visit × Form matrix renderer
  const visits = Array.isArray(visitsOrSoa) ? visitsOrSoa : [];
  if (visits.length === 0) {
    return `<div class="clinical-visit-matrix-error">No encounters defined for SoA matrix.</div>`;
  }

  let tableHtml = `<table class="clinical-visit-matrix"><thead><tr><th scope="col">Form / Procedure</th>`;
  visits.forEach((v) => {
    tableHtml += `<th scope="col">${v}</th>`;
  });
  tableHtml += `</tr></thead><tbody>`;

  forms.forEach((form) => {
    tableHtml += `<tr><th scope="row">${form.name}</th>`;
    visits.forEach((v, idx) => {
      const status = form.statuses?.[idx] || "N/A";
      let cellClass = "status-n-a";
      if (status === "Complete") {
        cellClass = "status-complete";
      } else if (status === "Pending") {
        cellClass = "status-pending";
      }
      const srLabel = `Form: ${form.name}, Visit: ${v}, Status: ${status}`;
      const srSpan = `<span class="sr-only" style="position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0, 0, 0, 0); border: 0; white-space: nowrap;">${srLabel}</span>`;
      tableHtml += `<td class="${cellClass}">${status}${srSpan}</td>`;
    });
    tableHtml += `</tr>`;
  });

  tableHtml += `</tbody></table>`;
  return tableHtml;
}

export { toBeAccessible } from "./accessibility-matcher.js";

import ClinicalFormField from "./src/components/clinical/ClinicalFormField.vue";
import ClinicalInput from "./src/components/clinical/ClinicalInput.vue";
import ClinicalRadioGroup from "./src/components/clinical/ClinicalRadioGroup.vue";
import ClinicalLookupInput from "./src/components/clinical/ClinicalLookupInput.vue";
import ClinicalQueryFlag from "./src/components/clinical/ClinicalQueryFlag.vue";
import ClinicalQueryPanel from "./src/components/clinical/ClinicalQueryPanel.vue";

export {
  ClinicalFormField,
  ClinicalInput,
  ClinicalRadioGroup,
  ClinicalLookupInput,
  ClinicalQueryFlag,
  ClinicalQueryPanel,
};

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
