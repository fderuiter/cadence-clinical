import {
  createClinicalInput,
  createClinicalRadioGrid,
  createClinicalVisitMatrix,
  createCtmsMilestoneTable,
  createCtmsVisitTable,
  createConditionRow,
  createRuleEditorContainer,
  generateGatewaySignature,
  createClinicalLookupInput,
  sha256 as sharedSha256,
  validateField as sharedValidateField,
  buildLedgerBlock,
  debounce as sharedDebounce,
} from "ui";

/**
 * Renders a clinical form HTML string from a backend XML payload.
 * It uses the shared design components (createClinicalInput) to generate each field.
 *
 * @param {string} xmlString - The XML payload containing the form definition.
 * @returns {string} The HTML string representing the rendered form.
 */
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

/**
 * Renders a clinical form HTML string dynamically based on a JSON layout definition.
 * Dynamically computes CSS grid columns and positions elements according to
 * CDASH metadata specifications.
 *
 * @param {string|Object} jsonPayload - The JSON payload or parsed object defining the form and its layout.
 * @returns {string} The HTML string representing the rendered form.
 */
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

/**
 * Validates a value against the specified CDASH metadata rules.
 *
 * @param {Object} fieldMeta - The field definition containing validation configurations.
 * @param {string|number} val - The input value to validate.
 * @returns {Object} An object `{ valid: boolean, message?: string }`.
 */
import {
  evaluateAST,
  compilerCache,
  getCompiledExpression,
} from "./src/evaluator.js";

export { evaluateAST, compilerCache, getCompiledExpression };
export { sharedDebounce as debounce };

export function validateField(fieldMeta, val, context = {}) {
  return sharedValidateField(fieldMeta, val, context, evaluateAST);
}

// Re-export sha256 from the shared ui library to prevent duplicate implementations
export { sharedSha256 as sha256 };

/**
 * Renders the clinical trial management system (CTMS) dashboard view.
 *
 * @param {Object} data - The CTMS dashboard dataset
 */
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
