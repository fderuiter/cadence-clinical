/**
 * Creates an HTML string for a clinical input field.
 * This reusable component is used for standard clinical data entry,
 * ensuring consistency across the application. Supports layout grid-spans
 * and query management controls.
 *
 * @param {string} id - The unique identifier for the input element.
 * @param {string} label - The label text for the input field.
 * @param {string} [value=""] - The initial value of the input field.
 * @param {Object|null} [query=null] - Optional query object containing state and history.
 * @param {number} [gridSpan=12] - Grid span from 1 to 12 for CDASH grid layouts.
 * @param {Object} [attributes={}] - Additional custom attributes.
 * @returns {string} The HTML string representing the clinical input.
 */
export function createClinicalInput(
  id,
  label,
  value = "",
  query = null,
  gridSpan = 12,
  attributes = {}
) {
  const extraAttrs = Object.entries(attributes)
    .map(([k, v]) => `${k}="${v}"`)
    .join(" ");

  const queryFlagHTML = createClinicalQueryFlag(id, query);
  const queryPanelHTML = createQueryPanel(id, query);

  return `
<div class="clinical-input grid-span-${gridSpan}" style="grid-column: span ${gridSpan};" id="field-container-${id}" ${extraAttrs}>
  <label for="${id}">${label}</label>
  <div class="input-wrapper">
    <input type="text" id="${id}" name="${id}" value="${value}" />
    ${queryFlagHTML}
  </div>
  ${queryPanelHTML}
</div>
  `.trim();
}

/**
 * Creates an HTML string for a clinical radio button grid group.
 * Follows strict accessibility guidelines using fieldsets and legends.
 *
 * @param {string} id - Unique identifier for the radio grid.
 * @param {string} label - Legend/label for the radio group.
 * @param {Array<string|Object>} options - Array of options (strings or {value, label} objects).
 * @param {string} [selectedValue=""] - Selected option value.
 * @param {Object|null} [query=null] - Optional query object.
 * @param {number} [gridSpan=12] - Column width in the grid.
 * @returns {string} The HTML string representing the radio grid.
 */
export function createClinicalRadioGrid(
  id,
  label,
  options = [],
  selectedValue = "",
  query = null,
  gridSpan = 12
) {
  const optionsHTML = options
    .map((opt, idx) => {
      const optVal = typeof opt === "string" ? opt : opt.value;
      const optLabel = typeof opt === "string" ? opt : opt.label;
      const isChecked = optVal === selectedValue ? " checked" : "";
      const optionId = `${id}_option_${idx}`;
      return `
      <div class="radio-option">
        <input type="radio" id="${optionId}" name="${id}" value="${optVal}"${isChecked} />
        <label for="${optionId}">${optLabel}</label>
      </div>
      `.trim();
    })
    .join("\n");

  const queryFlagHTML = createClinicalQueryFlag(id, query);
  const queryPanelHTML = createQueryPanel(id, query);

  return `
<fieldset class="clinical-radio-grid grid-span-${gridSpan}" style="grid-column: span ${gridSpan};" id="field-container-${id}">
  <legend>${label}</legend>
  <div class="radio-options-wrapper">
    <div class="radio-options">
      ${optionsHTML}
    </div>
    ${queryFlagHTML}
  </div>
  ${queryPanelHTML}
</fieldset>
  `.trim();
}

/**
 * Renders a clinical visit matrix representing subjects/forms against visits.
 * Displays progress/status for clinical trials in an accessible table.
 *
 * @param {Object} matrixData - Structure containing { visits: string[], forms: Object[] }
 * @returns {string} HTML string representing the visit matrix.
 */
export function createClinicalVisitMatrix(matrixData) {
  if (!matrixData) {
    return `<div class="clinical-visit-matrix-error">Invalid visit matrix data.</div>`;
  }

  // Delegate to SoA matrix if new projection structure is detected
  if (matrixData.rows || matrixData.encounters || matrixData.epochs) {
    return createClinicalSoAMatrix(matrixData);
  }

  if (!matrixData.visits || !matrixData.forms) {
    return `<div class="clinical-visit-matrix-error">Invalid visit matrix data.</div>`;
  }

  const visitsHeaderHTML = matrixData.visits
    .map((visit) => `<th scope="col">${visit}</th>`)
    .join("");

  const rowsHTML = matrixData.forms
    .map((form) => {
      const cellsHTML = form.statuses
        .map((status) => {
          const statusClass = `status-${status.toLowerCase().replace(/[^a-z0-9]/g, "-")}`;
          return `<td class="${statusClass}">${status}</td>`;
        })
        .join("");

      return `
    <tr>
      <th scope="row">${form.name}</th>
      ${cellsHTML}
    </tr>
      `.trim();
    })
    .join("\n");

  return `
<table class="clinical-visit-matrix">
  <thead>
    <tr>
      <th scope="col">Form / Procedure</th>
      ${visitsHeaderHTML}
    </tr>
  </thead>
  <tbody>
    ${rowsHTML}
  </tbody>
</table>
  `.trim();
}

/**
 * Renders an accessible, arm-aware Schedule of Activities (SoA) matrix.
 * Consumes the SoA projection shape (epochs, encounters, rows, arms) and
 * renders a table with grouped Arm -> Epoch -> Visit header rows.
 *
 * @param {Object} soaData - Structure containing { epochs?: Object[], encounters?: Object[], rows?: Object[], arms?: Object[] }
 * @returns {string} HTML string representing the SoA matrix.
 */
export function createClinicalSoAMatrix(soaData) {
  if (!soaData || !soaData.rows) {
    return `<div class="clinical-visit-matrix-error">Invalid SoA matrix data.</div>`;
  }

  const { epochs = [], encounters = [], rows = [], arms = [] } = soaData;

  // Map epoch_id to epoch object for easy lookup
  const epochMap = {};
  epochs.forEach((ep) => {
    epochMap[ep.epoch_id] = ep;
  });

  // Map arm_id to arm object for easy lookup
  const armMap = {};
  arms.forEach((arm) => {
    armMap[arm.arm_id] = arm;
  });

  // Construct flat column objects matching each encounter
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

  // If there are no columns, we can't render anything useful.
  if (cols.length === 0) {
    return `<div class="clinical-visit-matrix-error">No encounters defined for SoA matrix.</div>`;
  }

  // 1. Group consecutive columns for Arms (Row 1 of the multi-level header)
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

  // 2. Group consecutive columns for Epochs (Row 2 of the multi-level header)
  const epochGroups = [];
  let currentEpochGroup = null;

  cols.forEach((col) => {
    // Unique key is (arm_id + epoch_id) to prevent grouping across different arms
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

  // Check if we should render the Arm row (only if at least one arm is specified, i.e., not all are shared)
  const hasArms = cols.some((c) => c.arm !== null);
  const totalHeaderRows = hasArms ? 3 : 2;

  // Generate header HTML
  let headerRowsHTML = "";

  // Arm Row (only if arms are defined)
  if (hasArms) {
    const armCellsHTML = armGroups
      .map(
        (g) =>
          `<th scope="col" colspan="${g.colspan}" class="grouped-header arm-header">${g.name}</th>`
      )
      .join("");
    headerRowsHTML += `
    <tr>
      <th scope="col" rowspan="${totalHeaderRows}" class="corner-header">Form / Procedure</th>
      ${armCellsHTML}
    </tr>
    `.trim();
  }

  // Epoch Row
  const epochCellsHTML = epochGroups
    .map(
      (g) =>
        `<th scope="col" colspan="${g.colspan}" class="grouped-header epoch-header">${g.name}</th>`
    )
    .join("");

  if (hasArms) {
    headerRowsHTML += `
    <tr>
      ${epochCellsHTML}
    </tr>
    `.trim();
  } else {
    headerRowsHTML = `
    <tr>
      <th scope="col" rowspan="${totalHeaderRows}" class="corner-header">Form / Procedure</th>
      ${epochCellsHTML}
    </tr>
    `.trim();
  }

  // Encounter/Visit Row
  const encounterCellsHTML = cols
    .map(
      (c) =>
        `<th scope="col" class="encounter-header">${c.encounter.encounter_name}</th>`
    )
    .join("");

  headerRowsHTML += `
    <tr>
      ${encounterCellsHTML}
    </tr>
  `.trim();

  // Generate body rows HTML
  const rowsHTML = rows
    .map((row) => {
      const cellsHTML = cols
        .map((col) => {
          const encId = col.encounter.encounter_id;
          const cell = row.cells.find((c) => c.encounter_id === encId) || {
            is_applicable: false,
          };

          let cellClass;
          let cellText;

          if (cell.is_applicable) {
            cellClass = "status-applicable";
            cellText = "✓";

            if (cell.details) {
              const detailsLower = cell.details.toLowerCase();
              if (detailsLower.includes("conditional")) {
                cellClass = "status-conditional";
              } else if (detailsLower.includes("optional")) {
                cellClass = "status-optional";
              }
              cellText += ` <span class="cell-details">${cell.details}</span>`;
            }
          } else {
            cellClass = "status-n-a";
            cellText = "-";
          }

          return `<td class="${cellClass}">${cellText}</td>`;
        })
        .join("");

      return `
    <tr>
      <th scope="row">${row.activity_name}</th>
      ${cellsHTML}
    </tr>
      `.trim();
    })
    .join("\n");

  return `
<table class="clinical-visit-matrix clinical-soa-matrix">
  <thead>
    ${headerRowsHTML}
  </thead>
  <tbody>
    ${rowsHTML}
  </tbody>
</table>
  `.trim();
}

/**
 * Renders an interactive visual indicator of a field's query status.
 *
 * @param {string} fieldId - The associated field identifier.
 * @param {Object|null} query - The query state metadata.
 * @returns {string} HTML button representing the query flag.
 */
export function createClinicalQueryFlag(fieldId, query) {
  const status = query && query.status ? query.status.toUpperCase() : "NONE";
  const statusClass = status.toLowerCase();
  const label =
    status === "NONE"
      ? "No active queries. Click to create."
      : `Query status: ${status}`;
  const icon = status === "NONE" ? "💬" : "⚠️";

  return `
<button class="query-flag query-status-${statusClass}"
        id="query-flag-${fieldId}"
        type="button"
        aria-expanded="false"
        aria-controls="query-panel-${fieldId}"
        aria-label="${label}">
  ${icon}
</button>
  `.trim();
}

/**
 * Creates the direct site query creation, response, and resolution interface.
 *
 * @param {string} fieldId - The associated field identifier.
 * @param {Object|null} query - The query state metadata.
 * @returns {string} HTML representing the interactive query panel.
 */
export function createQueryPanel(fieldId, query) {
  const status = query && query.status ? query.status.toUpperCase() : "NONE";
  let bodyHTML = "";

  if (status === "NONE") {
    bodyHTML = `
      <div class="query-create-section">
        <p class="query-panel-instruction">Raise a query for this field:</p>
        <div class="form-group">
          <label for="query-message-${fieldId}">Discrepancy Message</label>
          <textarea id="query-message-${fieldId}" placeholder="Enter clinical discrepancy details..." required></textarea>
        </div>
        <button type="button" class="btn-submit-query" data-field-id="${fieldId}" data-action="create-query">Submit Query</button>
      </div>
    `.trim();
  } else if (status === "OPEN" || status === "REOPENED") {
    bodyHTML = `
      <div class="query-details">
        <div class="query-status-badge badge-${status.toLowerCase()}">Status: ${status}</div>
        <p class="query-current-msg"><strong>Discrepancy:</strong> ${query.message}</p>
        <p class="query-meta">Raised by: ${query.createdBy || "System"} on ${query.createdAt || "N/A"}</p>
      </div>
      <div class="query-respond-section">
        <div class="form-group">
          <label for="query-response-${fieldId}">Your Response</label>
          <textarea id="query-response-${fieldId}" placeholder="Enter clinical justification or resolution explanation..." required></textarea>
        </div>
        <button type="button" class="btn-respond-query" data-field-id="${fieldId}" data-action="respond-query">Submit Response</button>
      </div>
    `.trim();
  } else if (status === "ANSWERED") {
    bodyHTML = `
      <div class="query-details">
        <div class="query-status-badge badge-answered">Status: ANSWERED</div>
        <p class="query-current-msg"><strong>Discrepancy:</strong> ${query.message}</p>
        <p class="query-response-msg"><strong>Response:</strong> ${query.response || "No response provided"}</p>
        <p class="query-meta">Responded by: ${query.respondedBy || "Investigator"} on ${query.respondedAt || "N/A"}</p>
      </div>
      <div class="query-actions-section">
        <button type="button" class="btn-close-query" data-field-id="${fieldId}" data-action="close-query">Close Query (Resolve)</button>
        <button type="button" class="btn-reopen-query" data-field-id="${fieldId}" data-action="reopen-query">Reopen Query</button>
      </div>
    `.trim();
  } else if (status === "CLOSED") {
    bodyHTML = `
      <div class="query-details">
        <div class="query-status-badge badge-closed">Status: CLOSED</div>
        <p class="query-current-msg"><strong>Discrepancy:</strong> ${query.message}</p>
        <p class="query-response-msg"><strong>Response:</strong> ${query.response || "N/A"}</p>
        <p class="query-meta">Closed by: ${query.closedBy || "CRA/DM"} on ${query.closedAt || "N/A"}</p>
        <p class="query-history-info">This query is permanently resolved and closed.</p>
      </div>
    `.trim();
  }

  return `
<div class="query-panel" id="query-panel-${fieldId}" style="display: none;" role="region" aria-labelledby="query-flag-${fieldId}">
  <div class="query-panel-header">
    <span class="query-panel-title">Query Manager - ${fieldId}</span>
    <button type="button" class="btn-close-panel" aria-label="Close query panel" onclick="document.getElementById('query-panel-${fieldId}').style.display='none'">×</button>
  </div>
  <div class="query-panel-body">
    ${bodyHTML}
  </div>
</div>
  `.trim();
}

/**
 * Renders an HTML table representing CTMS site milestones.
 *
 * @param {Array<Object>} milestones - List of milestones
 * @returns {string} HTML string representing the milestone table.
 */
export function createCtmsMilestoneTable(milestones) {
  if (!milestones || !Array.isArray(milestones)) {
    return `<div class="ctms-table-error">Invalid milestone data.</div>`;
  }
  const rows = milestones
    .map(
      (m) => `
    <tr>
      <td><strong>${m.type}</strong></td>
      <td>${m.plannedDate || "N/A"}</td>
      <td>${m.actualDate || "Pending"}</td>
      <td><span class="badge ${m.status === "ACHIEVED" ? "gxp" : ""}">${m.status}</span></td>
    </tr>
  `
    )
    .join("");

  return `
<table class="clinical-visit-matrix">
  <thead>
    <tr>
      <th scope="col">Milestone Type</th>
      <th scope="col">Planned Date</th>
      <th scope="col">Actual Date</th>
      <th scope="col">Status</th>
    </tr>
  </thead>
  <tbody>
    ${rows}
  </tbody>
</table>
  `.trim();
}

/**
 * Renders an HTML table representing CTMS monitoring visits.
 *
 * @param {Array<Object>} visits - List of visits
 * @returns {string} HTML string representing the visit table.
 */
export function createCtmsVisitTable(visits) {
  if (!visits || !Array.isArray(visits)) {
    return `<div class="ctms-table-error">Invalid monitoring visit data.</div>`;
  }
  const rows = visits
    .map(
      (v) => `
    <tr>
      <td><strong>${v.type}</strong></td>
      <td>${v.scheduledDate || "N/A"}</td>
      <td>${v.actualDate || "Pending"}</td>
      <td>${v.cra || "N/A"}</td>
      <td><span class="badge ${v.status === "SIGNED_OFF" ? "gxp" : ""}">${v.status}</span></td>
    </tr>
  `
    )
    .join("");

  return `
<table class="clinical-visit-matrix">
  <thead>
    <tr>
      <th scope="col">Visit Type</th>
      <th scope="col">Scheduled Date</th>
      <th scope="col">Actual Date</th>
      <th scope="col">CRA Assigned</th>
      <th scope="col">Status</th>
    </tr>
  </thead>
  <tbody>
    ${rows}
  </tbody>
</table>
  `.trim();
}

/**
 * Renders the condition row visual widget for rules composing.
 * Follows strict accessibility guidelines using fieldsets and legends.
 *
 * @param {number} index - Index of the condition row.
 * @param {Array<Object>} forms - List of available study forms.
 * @param {Array<Object>} fields - List of available study fields.
 * @param {Object} [selectedValues={}] - Selected values for pre-populating fields.
 * @returns {string} The HTML string representing the condition row.
 */
export function createConditionRow(
  index,
  forms = [],
  fields = [],
  selectedValues = {}
) {
  const {
    formId = "",
    fieldId = "",
    operator = "==",
    rightType = "constant",
    rightValue = "",
    rightFieldId = "",
  } = selectedValues;

  const formOptions = forms
    .map(
      (f) =>
        `<option value="${f.id || f}"${(f.id || f) === formId ? " selected" : ""}>${f.name || f.id || f}</option>`
    )
    .join("");

  const fieldOptions = fields
    .map(
      (f) =>
        `<option value="${f.id || f}"${(f.id || f) === fieldId ? " selected" : ""}>${f.name || f.id || f}</option>`
    )
    .join("");

  const rightFieldOptions = fields
    .map(
      (f) =>
        `<option value="${f.id || f}"${(f.id || f) === rightFieldId ? " selected" : ""}>${f.name || f.id || f}</option>`
    )
    .join("");

  const operators = [
    { value: "==", label: "equals" },
    { value: "!=", label: "does not equal" },
    { value: "<", label: "is less than" },
    { value: "<=", label: "is less than or equal to" },
    { value: ">", label: "is greater than" },
    { value: ">=", label: "is greater than or equal to" },
    { value: "is_empty", label: "is empty" },
    { value: "is_not_empty", label: "is not empty" },
  ];

  const operatorOptions = operators
    .map(
      (op) =>
        `<option value="${op.value}"${op.value === operator ? " selected" : ""}>${op.label}</option>`
    )
    .join("");

  return `
<fieldset class="condition-row" data-index="${index}" id="condition-row-${index}">
  <legend>Condition Element #${index + 1}</legend>

  <div class="condition-row-grid" style="display: flex; gap: 12px; align-items: flex-end; flex-wrap: wrap;">

    <div class="form-group" style="flex: 1; min-width: 120px;">
      <label for="cond-form-${index}">Left Form</label>
      <select id="cond-form-${index}" class="cond-form-select" data-index="${index}">
        <option value="">-- Select Form --</option>
        ${formOptions}
      </select>
    </div>

    <div class="form-group" style="flex: 1; min-width: 120px;">
      <label for="cond-field-${index}">Left Field</label>
      <select id="cond-field-${index}" class="cond-field-select" data-index="${index}">
        <option value="">-- Select Field --</option>
        ${fieldOptions}
      </select>
    </div>

    <div class="form-group" style="flex: 1; min-width: 120px;">
      <label for="cond-operator-${index}">Operator</label>
      <select id="cond-operator-${index}" class="cond-operator-select" data-index="${index}">
        ${operatorOptions}
      </select>
    </div>

    <div class="form-group cond-right-type-container" style="flex: 1; min-width: 120px; ${operator === "is_empty" || operator === "is_not_empty" ? "display: none;" : ""}">
      <label for="cond-right-type-${index}">Right Value Type</label>
      <select id="cond-right-type-${index}" class="cond-right-type-select" data-index="${index}">
        <option value="constant"${rightType === "constant" ? " selected" : ""}>Constant Value</option>
        <option value="field_ref"${rightType === "field_ref" ? " selected" : ""}>Field Reference</option>
      </select>
    </div>

    <div class="form-group cond-right-value-container" style="flex: 1; min-width: 120px; ${operator === "is_empty" || operator === "is_not_empty" || rightType !== "constant" ? "display: none;" : ""}">
      <label for="cond-right-value-${index}">Constant Value</label>
      <input type="text" id="cond-right-value-${index}" class="cond-right-value-input" value="${rightValue}" data-index="${index}" placeholder="Value..." />
    </div>

    <div class="form-group cond-right-field-container" style="flex: 1; min-width: 120px; ${operator === "is_empty" || operator === "is_not_empty" || rightType !== "field_ref" ? "display: none;" : ""}">
      <label for="cond-right-field-${index}">Right Field</label>
      <select id="cond-right-field-${index}" class="cond-right-field-select" data-index="${index}">
        <option value="">-- Select Field --</option>
        ${rightFieldOptions}
      </select>
    </div>

    <div class="form-group" style="flex-grow: 0;">
      <button type="button" class="btn btn-error btn-remove-condition" data-index="${index}" style="background-color: var(--error, #ef4444); color: white; border: none; padding: 8px 12px; border-radius: 4px; cursor: pointer;">Remove</button>
    </div>

  </div>
</fieldset>
  `.trim();
}

/**
 * Creates the overall Rule Editor widget.
 *
 * @param {Array<Object>} forms - List of available study forms.
 * @param {Array<Object>} fields - List of available study fields.
 * @returns {string} HTML string representing the rule editor container.
 */
export function createRuleEditorContainer(forms = [], fields = []) {
  const formOptions = forms
    .map((f) => `<option value="${f.id || f}">${f.name || f.id || f}</option>`)
    .join("");

  const fieldOptions = fields
    .map((f) => `<option value="${f.id || f}">${f.name || f.id || f}</option>`)
    .join("");

  return `
<div class="rule-editor-container" id="rule-editor-form">
  <fieldset class="rule-meta-fieldset" style="border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 16px;">
    <legend style="padding: 0 8px; font-weight: bold; color: var(--primary);">Rule Type & Target Definition</legend>
    <div style="display: flex; gap: 16px; flex-wrap: wrap; margin-bottom: 12px;">
      <div class="form-group" style="flex: 1; min-width: 200px;">
        <label for="rule-type" style="display: block; margin-bottom: 4px; font-weight: 600;">Rule Classification Type</label>
        <select id="rule-type" class="rule-type-select" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;">
          <option value="skip_logic">Skip Logic (Show/Hide fields)</option>
          <option value="constraint">Field Constraint (Single field query validation)</option>
          <option value="cross_form_check">Cross-Form / Longitudinal Check</option>
        </select>
      </div>

      <div class="form-group rule-target-container" style="flex: 1; min-width: 200px;">
        <label for="rule-target-field" style="display: block; margin-bottom: 4px; font-weight: 600;">Target Field</label>
        <select id="rule-target-field" class="rule-target-field-select" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;">
          <option value="">-- Select Target Field --</option>
          ${fieldOptions}
        </select>
      </div>
    </div>

    <div style="display: flex; gap: 16px; flex-wrap: wrap;" id="rule-extra-fields-grid">
      <div class="form-group rule-action-container" style="flex: 1; min-width: 200px;">
        <label for="rule-action" style="display: block; margin-bottom: 4px; font-weight: 600;">Action on Target</label>
        <select id="rule-action" class="rule-action-select" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;">
          <option value="show">Show target field</option>
          <option value="hide">Hide target field</option>
        </select>
      </div>

      <div class="form-group rule-target-form-container" style="flex: 1; min-width: 200px;">
        <label for="rule-target-form" style="display: block; margin-bottom: 4px; font-weight: 600;">Target Form (Optional)</label>
        <select id="rule-target-form" class="rule-target-form-select" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;">
          <option value="">-- Select Target Form --</option>
          ${formOptions}
        </select>
      </div>

      <div class="form-group rule-message-container" style="flex: 1; min-width: 200px; display: none;">
        <label for="rule-message" style="display: block; margin-bottom: 4px; font-weight: 600;">Auto-Query Discrepancy Message</label>
        <input type="text" id="rule-message" class="rule-message-input" placeholder="e.g. Systolic BP is out of logical range" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;" />
      </div>
    </div>
  </fieldset>

  <fieldset class="rule-conditions-fieldset" style="border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 16px;">
    <legend style="padding: 0 8px; font-weight: bold; color: var(--primary);">Rule Conditions (Logical Expression Tree)</legend>
    <div class="form-group" style="margin-bottom: 12px;">
      <label for="rule-logical-operator" style="display: block; margin-bottom: 4px; font-weight: 600;">Match Conditions Group Operator</label>
      <select id="rule-logical-operator" class="rule-logical-operator-select" style="padding: 6px; border: 1px solid var(--border); border-radius: 4px;">
        <option value="and">All conditions must be met (AND)</option>
        <option value="or">Any condition can be met (OR)</option>
      </select>
    </div>

    <div id="conditions-list" style="display: flex; flex-direction: column; gap: 12px;">
      <!-- Dynamic Condition Rows go here -->
    </div>

    <div style="margin-top: 12px; display: flex; gap: 8px;">
      <button type="button" id="btn-add-condition" class="btn btn-secondary" style="padding: 8px 12px; cursor: pointer;">➕ Add Condition Row</button>
    </div>
  </fieldset>

  <fieldset class="rule-preview-fieldset" style="border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 16px;">
    <legend style="padding: 0 8px; font-weight: bold; color: var(--primary);">Live Compilation & GxP Verification Preview</legend>
    <div class="preview-output" style="background-color: #f1f5f9; padding: 12px; border-radius: 6px; font-family: monospace; font-size: 0.85rem; border: 1px solid var(--border);">
      <div><strong>Compiled XPath Expression:</strong></div>
      <div id="rule-xpath-preview" style="color: #0369a1; margin-bottom: 8px; word-break: break-all;">(No conditions added)</div>

      <div><strong>Validation Feedback:</strong></div>
      <div id="rule-validation-failures" style="color: #b91c1c; margin-bottom: 4px; font-weight: 600;"></div>
      <div id="rule-circular-cycles" style="color: #b91c1c; font-weight: 600;"></div>
    </div>
  </fieldset>

  <div style="margin-top: 16px; display: flex; justify-content: flex-end; gap: 8px;">
    <button type="button" id="btn-cancel-rule" class="btn" style="padding: 8px 16px; cursor: pointer;">Cancel</button>
    <button type="button" id="btn-save-rule" class="btn btn-primary" style="padding: 8px 16px; cursor: pointer; background-color: var(--primary); color: white; border: none; border-radius: 4px;">Save Signed Rule</button>
  </div>
</div>
  `.trim();
}

/**
 * Creates an HTML string for a clinical terminology/code lookup input field.
 * Modeled on existing clinical input patterns, supporting loading, valid,
 * invalid, and degraded visual states accessibly.
 *
 * @param {string} id - The unique identifier for the input element.
 * @param {string} label - The label text for the input field.
 * @param {string} [value=""] - The initial value of the input field.
 * @param {string} [status="none"] - The lookup status: 'none', 'loading', 'valid', 'invalid', 'degraded'.
 * @param {string} [statusMessage=""] - Accessible text message describing the current status.
 * @param {number} [gridSpan=12] - Grid span from 1 to 12 for CDASH layouts.
 * @param {Object} [attributes={}] - Additional custom attributes.
 * @returns {string} The HTML string representing the lookup input.
 */
export function createClinicalLookupInput(
  id,
  label,
  value = "",
  status = "none",
  statusMessage = "",
  gridSpan = 12,
  attributes = {}
) {
  const extraAttrs = Object.entries(attributes)
    .map(([k, v]) => `${k}="${v}"`)
    .join(" ");

  let stateClass = "";
  let ariaLiveMessage = statusMessage;
  let statusIcon = "";

  if (status === "loading") {
    stateClass = "lookup-loading";
    statusIcon = "⏳";
    if (!ariaLiveMessage) ariaLiveMessage = "Searching terminology database...";
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

  const statusHTML =
    status !== "none"
      ? `
<div class="lookup-status-indicator ${stateClass}" id="lookup-status-${id}" role="status" aria-live="polite">
  <span class="lookup-status-icon" aria-hidden="true">${statusIcon}</span>
  <span class="lookup-status-text">${ariaLiveMessage}</span>
</div>
  `.trim()
      : `
<div class="lookup-status-indicator" id="lookup-status-${id}" role="status" aria-live="polite" style="display: none;"></div>
  `.trim();

  return `
<div class="clinical-input clinical-lookup-container grid-span-${gridSpan}" style="grid-column: span ${gridSpan};" id="field-container-${id}" ${extraAttrs}>
  <label for="${id}">${label}</label>
  <div class="input-wrapper">
    <input type="text" id="${id}" name="${id}" value="${value}" autocomplete="off" />
  </div>
  ${statusHTML}
</div>
  `.trim();
}

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
} from "./signing.js";
