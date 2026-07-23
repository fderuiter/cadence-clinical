import { createClinicalInput, createClinicalRadioGrid } from "ui";

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
  const fieldRegex = /<field id="([^"]+)" label="([^"]+)"\s*\/>/g;

  let html = `<form class="clinical-form">`;
  let match;
  while ((match = fieldRegex.exec(innerXML)) !== null) {
    const id = match[1];
    const label = match[2];
    html += createClinicalInput(id, label);
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
