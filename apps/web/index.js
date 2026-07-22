import { createClinicalInput } from "ui";

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
