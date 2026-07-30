import { validateField as uiValidateField, sha256 as uiSha256 } from "ui";
import { evaluateAST, compilerCache, getCompiledExpression } from "../evaluator.js";

export { evaluateAST, compilerCache, getCompiledExpression };

export function validateField(fieldMeta, val, context = {}) {
  return uiValidateField(fieldMeta, val, context, evaluateAST);
}

export function sha256(val) {
  return uiSha256(val);
}

export function renderFormFromXML(xmlString) {
  if (!xmlString) return "";

  // Simple regex-based parser for demonstration (in reality, use DOMParser)
  const formMatch = xmlString.match(/<form[^>]*>(.*?)<\/form>/is);
  if (!formMatch) return "";

  const innerXML = formMatch[1];
  const fieldRegex = /<field\b([^>]*?)\/?>/gi;

  let html = `<form class="clinical-form">`;
  let match;
  while ((match = fieldRegex.exec(innerXML)) !== null) {
    const attrString = match[1];
    const idMatch = attrString.match(/\bid="([^"]*)"/);
    const labelMatch = attrString.match(/\blabel="([^"]*)"/);
    if (idMatch && labelMatch) {
      const id = idMatch[1];
      const label = labelMatch[1];
      html += createClinicalInput(id, label);
    }
  }
  html += `</form>`;

  return html;
}

export function renderFormFromJSON(jsonPayload) {
  if (!jsonPayload) return "";

  let payload = jsonPayload;
  if (typeof jsonPayload === "string") {
    try {
      payload = JSON.parse(jsonPayload);
    } catch {
      return `<div class="render-error">Invalid JSON payload configuration.</div>`;
    }
  }

  const formId = payload.formId || "unknown";
  const formTitle = payload.formTitle || "";
  const layout = payload.layout || { columns: 12 };
  const columns = layout.columns || 12;
  const fields = payload.fields || [];

  let html = `<form class="clinical-form clinical-form-grid" id="form-${formId}" style="display: grid; grid-template-columns: repeat(${columns}, 1fr); gap: 16px;">`;

  if (formTitle) {
    html += `<h2 class="form-title" style="grid-column: span ${columns};">${formTitle}</h2>`;
  }

  fields.forEach((field) => {
    const gridSpan = field.gridSpan || 12;
    const value = field.value || "";
    const query = field.query || null;

    if (field.type === "radio" || field.type === "choice_single") {
      const options = field.options || [];
      html += createClinicalRadioGrid(
        field.id,
        field.label,
        options,
        value,
        query,
        gridSpan
      );
    } else if (field.type === "concept_code") {
      const status = field.status || "none";
      const statusMessage = field.statusMessage || "";
      const attrs = {};
      if (field.cdash) {
        attrs["data-cdash"] = field.cdash;
      }
      html += createClinicalLookupInput(
        field.id,
        field.label,
        value,
        status,
        statusMessage,
        gridSpan,
        attrs
      );
    } else {
      const attrs = {};
      if (field.cdash) {
        attrs["data-cdash"] = field.cdash;
      }
      html += createClinicalInput(
        field.id,
        field.label,
        value,
        query,
        gridSpan,
        attrs
      );
    }
  });

  html += `</form>`;
  return html;
}

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

export function renderCtms(data) {
  if (!data) return;
  const milestonesContainer = document.getElementById(
    "ctms-milestones-container"
  );
  const visitsContainer = document.getElementById("ctms-visits-container");
  const workloadContainer = document.getElementById("ctms-workload-container");
  const recruitmentContainer = document.getElementById(
    "ctms-recruitment-container"
  );

  if (milestonesContainer) {
    milestonesContainer.innerHTML = createCtmsMilestoneTable(data.milestones);
  }
  if (visitsContainer) {
    visitsContainer.innerHTML = createCtmsVisitTable(data.visits);
  }
  if (workloadContainer) {
    const rows = (data.allocations || [])
      .map(
        (a) => `
      <tr>
        <td><strong>${a.cra}</strong></td>
        <td>${a.activeAllocations}</td>
        <td>${(a.sites || []).join(", ")}</td>
        <td>${(a.studies || []).join(", ")}</td>
      </tr>
    `
      )
      .join("");
    workloadContainer.innerHTML = `
<table class="clinical-visit-matrix">
  <thead>
    <tr>
      <th scope="col">CRA</th>
      <th scope="col">Active Allocations</th>
      <th scope="col">Allocated Sites</th>
      <th scope="col">Allocated Studies</th>
    </tr>
  </thead>
  <tbody>
    ${rows}
  </tbody>
</table>
    `.trim();
  }
  if (recruitmentContainer) {
    const rows = (data.recruitment || [])
      .map(
        (r) => `
      <tr>
        <td><strong>${r.siteId}</strong></td>
        <td>${r.screened}</td>
        <td>${r.enrolled}</td>
        <td>${r.target}</td>
        <td>${Math.round((r.enrolled / r.target) * 100)}%</td>
      </tr>
    `
      )
      .join("");
    recruitmentContainer.innerHTML = `
<table class="clinical-visit-matrix">
  <thead>
    <tr>
      <th scope="col">Site ID</th>
      <th scope="col">Screened</th>
      <th scope="col">Enrolled</th>
      <th scope="col">Target</th>
      <th scope="col">Progress</th>
    </tr>
  </thead>
  <tbody>
    ${rows}
  </tbody>
</table>
    `.trim();
  }
}

export function createClinicalVisitMatrix(matrixData) {
  if (!matrixData) {
    return `<div class="clinical-visit-matrix-error">Invalid visit matrix data.</div>`;
  }

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

export function createClinicalSoAMatrix(soaData) {
  if (!soaData || !soaData.rows) {
    return `<div class="clinical-visit-matrix-error">Invalid SoA matrix data.</div>`;
  }

  const { epochs = [], encounters = [], rows = [], arms = [] } = soaData;

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

  if (cols.length === 0) {
    return `<div class="clinical-visit-matrix-error">No encounters defined for SoA matrix.</div>`;
  }

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

  const hasArms = cols.some((c) => c.arm !== null);
  const totalHeaderRows = hasArms ? 3 : 2;

  let headerRowsHTML = "";

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
