/**
 * Creates an HTML string for a clinical input field.
 * This reusable component is used for standard clinical data entry,
 * ensuring consistency across the application.
 *
 * @param {string} id - The unique identifier for the input element.
 * @param {string} label - The label text for the input field.
 * @param {string} [value=""] - The initial value of the input field.
 * @returns {string} The HTML string representing the clinical input.
 */
export function createClinicalInput(id, label, value = "") {
  return `<div class="clinical-input"><label for="${id}">${label}</label><input type="text" id="${id}" name="${id}" value="${value}" /></div>`;
}
