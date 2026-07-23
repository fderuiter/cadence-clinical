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
  if (!matrixData || !matrixData.visits || !matrixData.forms) {
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

export {
  canonicalSerialize,
  generateCanonicalSignature,
  verifyCanonicalSignature,
  generateGatewaySignature,
  verifyGatewaySignature,
} from "./signing.js";
